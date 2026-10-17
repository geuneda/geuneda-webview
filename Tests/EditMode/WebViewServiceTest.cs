using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

// ReSharper disable once CheckNamespace

namespace Geuneda.WebView.Tests
{
	[TestFixture]
	public class WebViewServiceTest
	{
		private const string PageUrl = "https://notice.example.com/ko/index.html";

		private FakeWebViewFactory _factory;
		private WebViewService _service;
		private UniTaskCompletionSource _timeout;
		private TimeSpan _delay;
		private List<string> _opened;
		private int _closed;

		[SetUp]
		public void SetUp()
		{
			_factory = new FakeWebViewFactory();
			_opened = new List<string>();
			_closed = 0;
			_service = CreateService();
		}

		[TearDown]
		public void TearDown()
		{
			_service.Dispose();
		}

		private WebViewService CreateService(Action<WebViewServiceOptions> change = null)
		{
			var options = new WebViewServiceOptions
			{
				Factory = _factory,
				AllowedOrigins = new[] { "https://notice.example.com", "http://127.0.0.1:5700" },
				LoadTimeout = TimeSpan.FromSeconds(5)
			};
			change?.Invoke(options);

			var service = new WebViewService(options)
			{
				Delay = (delay, token) =>
				{
					_delay = delay;
					var source = new UniTaskCompletionSource();
					token.Register(() => source.TrySetCanceled());
					_timeout = source;
					return source.Task;
				},
				OpenExternal = url => _opened.Add(url),
				SafeArea = () => new Rect(0, 100, 1080, 1700),
				ScreenSize = () => new Vector2Int(1080, 1920)
			};
			service.Closed += () => _closed++;
			return service;
		}

		private static T Run<T>(UniTask<T> task)
		{
			Assert.IsTrue(task.Status.IsCompleted(), "Expected the task to complete synchronously");
			return task.GetAwaiter().GetResult();
		}

		private FakeWebViewNative OpenAndShow(RectTransform frame = null)
		{
			var open = _service.OpenAsync(PageUrl, frame);
			_factory.Last.Ready();
			Assert.IsTrue(Run(open).IsSuccess);
			return _factory.Last;
		}

		private static WebViewCommandHandler Returns(Result<object> result)
		{
			return (command, token) => UniTask.FromResult(result);
		}

		[Test]
		public void Constructor_RejectsBadOptions()
		{
			Assert.Throws<ArgumentNullException>(() => new WebViewService(null));
			Assert.Throws<ArgumentException>(() => CreateService(o => o.Factory = null));
			Assert.Throws<ArgumentException>(() => CreateService(o => o.AllowedOrigins = null));
			Assert.Throws<ArgumentException>(() => CreateService(o => o.AllowedOrigins = Array.Empty<string>()));
			Assert.Throws<ArgumentException>(() => CreateService(o => o.AllowedOrigins = new[] { "https://notice.example.com/ko" }));
			Assert.Throws<ArgumentException>(() => CreateService(o => o.AllowedOrigins = new[] { "https://notice.example.com?x=1" }));
			Assert.Throws<ArgumentException>(() => CreateService(o => o.AllowedOrigins = new[] { "ftp://notice.example.com" }));
			Assert.Throws<ArgumentException>(() => CreateService(o => o.AllowedOrigins = new[] { "notice.example.com" }));
			Assert.Throws<ArgumentException>(() => CreateService(o => o.AllowedOrigins = new[] { "https://user@notice.example.com" }));
			Assert.Throws<ArgumentException>(() => CreateService(o => o.LoadTimeout = TimeSpan.Zero));
		}

		[Test]
		public void Constructor_NormalizesOriginsForTheAdapter()
		{
			var service = CreateService(o => o.AllowedOrigins = new[] { "HTTPS://Notice.Example.COM/", "https://notice.example.com:443", "http://127.0.0.1:5700" });
			service.OpenAsync("https://notice.example.com/").Forget();

			CollectionAssert.AreEqual(new[] { "https://notice.example.com", "http://127.0.0.1:5700" }, _factory.Last.Settings.AllowedOrigins);
			service.Dispose();
		}

		[Test]
		public void DenyPattern_BlocksEveryHttpUrlOutsideTheAllowedOrigins()
		{
			_service.OpenAsync(PageUrl).Forget();
			var deny = new Regex(_factory.Last.Settings.DenyUrlPattern);

			foreach (var url in new[]
				{
					"https://evil.com/", "http://notice.example.com/", "https://notice.example.com.evil.com/", "https://notice.example.com:8443/",
					"https://notice.example.com@evil.com/", "https://evil.com/?https://notice.example.com/", "http://127.0.0.1:5701/"
				})
			{
				Assert.IsTrue(deny.IsMatch(url), url);
			}

			foreach (var url in new[]
				{
					"https://notice.example.com", "https://notice.example.com/ko/index.html?x=1#top", "https://notice.example.com?x", "https://notice.example.com#x",
					"HTTPS://NOTICE.EXAMPLE.COM/", "http://127.0.0.1:5700/coupon.html", "unity:{}", "about:blank", "mailto:help@example.com"
				})
			{
				Assert.IsFalse(deny.IsMatch(url), url);
			}
		}

		[Test]
		public void Open_ShowsThePageAfterTheBridgeIsReady()
		{
			var open = _service.OpenAsync(PageUrl);
			var native = _factory.Last;

			Assert.IsFalse(open.Status.IsCompleted());
			Assert.IsTrue(_service.IsOpen);
			Assert.IsFalse(native.Visible);
			CollectionAssert.AreEqual(new[] { PageUrl }, native.LoadedUrls);
			CollectionAssert.AreEqual(new[] { new WebViewMargins(0, 120, 0, 100) }, native.Margins);
			Assert.AreEqual(TimeSpan.FromSeconds(5), _delay);
			Assert.AreEqual(PageUrl, _service.PageUrl);

			native.Ready();

			Assert.IsTrue(Run(open).IsSuccess);
			Assert.IsTrue(native.Visible);
			Assert.IsTrue(_service.IsOpen);
		}

		[Test]
		public void Open_RejectsUrlsOutsideTheAllowedOrigins()
		{
			foreach (var url in new[] { null, "", "/ko/index.html", "https://evil.com/", "ftp://notice.example.com/", "https://notice.example.com.evil.com/" })
			{
				Assert.Throws<ArgumentException>(() => _service.OpenAsync(url).Forget(), url);
			}

			Assert.IsEmpty(_factory.Created);
		}

		[Test]
		public void Open_WhileOpen_IsInProgress()
		{
			_service.OpenAsync(PageUrl).Forget();

			var second = Run(_service.OpenAsync(PageUrl));

			Assert.AreEqual(ResultCodes.InProgress, second.Code);
			Assert.AreEqual(1, _factory.Created.Count);
		}

		[Test]
		public void Open_LoadError_FailsAndDisposesTheWebView()
		{
			var open = _service.OpenAsync(PageUrl);
			LogAssert.Expect(LogType.Warning, new Regex("Could not open the web view: Page failed to load: net::ERR_CONNECTION_REFUSED"));

			_factory.Last.Listener.OnLoadError("net::ERR_CONNECTION_REFUSED");

			Assert.AreEqual(WebViewResultCodes.LoadFailed, Run(open).Code);
			Assert.IsTrue(_factory.Last.Disposed);
			Assert.IsFalse(_service.IsOpen);
			Assert.AreEqual(0, _closed);
		}

		[Test]
		public void Open_HttpErrorPageWithoutTheBridge_Fails()
		{
			var open = _service.OpenAsync(PageUrl);
			LogAssert.Expect(LogType.Warning, new Regex("HTTP 404 without the web view bridge"));

			_factory.Last.Listener.OnHttpError(404);
			_factory.Last.Listener.OnPageLoaded(PageUrl);

			Assert.AreEqual(WebViewResultCodes.LoadFailed, Run(open).Code);
			Assert.IsTrue(_factory.Last.Disposed);
		}

		[Test]
		public void Open_ResourceHttpErrorOnABridgePage_StillOpens()
		{
			var open = _service.OpenAsync(PageUrl);

			// Android 는 이미지 같은 리소스 오류도 알린다. 브리지 준비가 로드 끝보다 먼저 온다
			_factory.Last.Listener.OnHttpError(404);
			_factory.Last.Ready();
			_factory.Last.Listener.OnPageLoaded(PageUrl);

			Assert.IsTrue(Run(open).IsSuccess);
			Assert.IsTrue(_service.IsOpen);
		}

		[Test]
		public void Open_LoadedWithoutTheBridge_TimesOut()
		{
			var open = _service.OpenAsync(PageUrl);
			_factory.Last.Listener.OnPageLoaded(PageUrl);

			Assert.IsFalse(open.Status.IsCompleted());
			LogAssert.Expect(LogType.Warning, new Regex("did not start the web view bridge within 5s"));

			_timeout.TrySetResult();

			Assert.AreEqual(ResultCodes.Timeout, Run(open).Code);
			Assert.IsTrue(_factory.Last.Disposed);
			Assert.IsFalse(_service.IsOpen);
		}

		[Test]
		public void Open_Cancelled_DisposesTheWebView()
		{
			using (var cancellation = new CancellationTokenSource())
			{
				var open = _service.OpenAsync(PageUrl, null, cancellation.Token);
				cancellation.Cancel();

				Assert.AreEqual(ResultCodes.Cancelled, Run(open).Code);
				Assert.IsTrue(_factory.Last.Disposed);
				Assert.IsFalse(_service.IsOpen);
				Assert.AreEqual(0, _closed);
			}
		}

		[Test]
		public void Open_AlreadyCancelled_DoesNotCreateAWebView()
		{
			Assert.AreEqual(ResultCodes.Cancelled, Run(_service.OpenAsync(PageUrl, null, new CancellationToken(true))).Code);
			Assert.IsEmpty(_factory.Created);
		}

		[Test]
		public void Open_CancelAfterShown_DoesNothing()
		{
			using (var cancellation = new CancellationTokenSource())
			{
				var open = _service.OpenAsync(PageUrl, null, cancellation.Token);
				_factory.Last.Ready();
				Assert.IsTrue(Run(open).IsSuccess);

				cancellation.Cancel();

				Assert.IsTrue(_service.IsOpen);
				Assert.IsFalse(_factory.Last.Disposed);
			}
		}

		[Test]
		public void Open_AfterAFailure_CanOpenAgainRightAway()
		{
			var reopened = default(UniTask<Result>);
			var first = _service.OpenAsync(PageUrl);
			first.ContinueWith(result => { reopened = _service.OpenAsync(PageUrl); }).Forget();
			LogAssert.Expect(LogType.Warning, new Regex("Could not open the web view"));

			_factory.Last.Listener.OnLoadError("offline");

			Assert.AreEqual(2, _factory.Created.Count);
			Assert.IsFalse(reopened.Status.IsCompleted());
		}

		[Test]
		public void Close_WhileOpening_CancelsTheOpen()
		{
			var open = _service.OpenAsync(PageUrl);

			_service.Close();

			Assert.AreEqual(ResultCodes.Cancelled, Run(open).Code);
			Assert.IsTrue(_factory.Last.Disposed);
			Assert.AreEqual(0, _closed);
		}

		[Test]
		public void Close_DisposesAndRaisesClosedOnce()
		{
			var native = OpenAndShow();

			_service.Close();
			_service.Close();

			Assert.IsTrue(native.Disposed);
			Assert.IsFalse(_service.IsOpen);
			Assert.IsNull(_service.PageUrl);
			Assert.AreEqual(1, _closed);
		}

		[Test]
		public void Close_HandlerException_IsLoggedAndOthersStillRun()
		{
			OpenAndShow();
			var after = 0;
			_service.Closed += () => throw new InvalidOperationException("game bug");
			_service.Closed += () => after++;
			LogAssert.Expect(LogType.Error, new Regex("Closed handler failed"));
			LogAssert.Expect(LogType.Exception, new Regex("game bug"));

			_service.Close();

			Assert.AreEqual(1, _closed);
			Assert.AreEqual(1, after);
		}

		[Test]
		public void LateCallbacksFromAClosedWebView_AreIgnored()
		{
			_service.OpenAsync(PageUrl).Forget();
			var old = _factory.Last;
			_service.Close();
			var open = _service.OpenAsync(PageUrl);

			old.Ready();
			old.Listener.OnLoadError("late");

			Assert.IsFalse(open.Status.IsCompleted());
			Assert.IsFalse(_factory.Last.Disposed);
		}

		[Test]
		public void Call_RunsTheHandlerAndAnswersWithTheSameId()
		{
			WebViewCommand received = null;
			_service.AddCommand("echo", (command, token) =>
			{
				received = command;
				return UniTask.FromResult(Result<object>.Ok(new JObject { ["text"] = command.Args["text"] }));
			});
			var native = OpenAndShow();

			native.Call("7", "echo", new JObject { ["text"] = "hi" });

			Assert.AreEqual("echo", received.Name);
			Assert.AreEqual(PageUrl, received.PageUrl);
			var result = native.Result("7");
			Assert.AreEqual(true, (bool) result["ok"]);
			Assert.AreEqual(0, (int) result["code"]);
			Assert.AreEqual("hi", (string) result["value"]["text"]);
		}

		[Test]
		public void Call_NumericIdAndMissingArgs_AreAccepted()
		{
			WebViewCommand received = null;
			_service.AddCommand("ping", (command, token) =>
			{
				received = command;
				return UniTask.FromResult(Result<object>.Ok(null));
			});
			var native = OpenAndShow();

			native.Send(new JObject { ["type"] = "call", ["id"] = 3, ["command"] = "ping" });

			Assert.AreEqual(0, received.Args.Count);
			var result = native.Received()[0];
			Assert.AreEqual(JTokenType.Integer, result["id"].Type);
			Assert.AreEqual(FakeWebViewNative.Doc, (string) result["doc"]);
			Assert.IsNull(result["value"]);
		}

		[Test]
		public void Call_SerializesValuesInCamelCase()
		{
			_service.AddCommand("profile", Returns(Result<object>.Ok(new Profile { NickName = "geuneda", Level = 3 })));
			var native = OpenAndShow();

			native.Call("1", "profile");

			var value = native.Result("1")["value"];
			Assert.AreEqual("geuneda", (string) value["nickName"]);
			Assert.AreEqual(3, (int) value["level"]);
			Assert.IsNull(value["title"], "null values are left out");
		}

		[Test]
		public void Call_Failure_AnswersCodeAndMessage()
		{
			_service.AddCommand("coupon.redeem", Returns(Result<object>.Fail(11003, "Expired")));
			var native = OpenAndShow();

			native.Call("1", "coupon.redeem");

			var result = native.Result("1");
			Assert.AreEqual(false, (bool) result["ok"]);
			Assert.AreEqual(11003, (int) result["code"]);
			Assert.AreEqual("Expired", (string) result["message"]);
		}

		[Test]
		public void Call_UnknownCommand_AnswersUnknownCommand()
		{
			var native = OpenAndShow();

			native.Call("1", "nothing");

			Assert.AreEqual(WebViewResultCodes.UnknownCommand, (int) native.Result("1")["code"]);
		}

		[Test]
		public void Call_BadCommandNameOrArgs_AnswersInvalidArguments()
		{
			_service.AddCommand("echo", Returns(Result<object>.Ok(null)));
			var native = OpenAndShow();

			native.Send(new JObject { ["type"] = "call", ["id"] = "1", ["command"] = "echo", ["args"] = new JArray(1) });
			native.Send(new JObject { ["type"] = "call", ["id"] = "2", ["command"] = "bad name" });
			native.Send(new JObject { ["type"] = "call", ["id"] = "3", ["command"] = 5 });

			foreach (var id in new[] { "1", "2", "3" })
			{
				Assert.AreEqual(WebViewResultCodes.InvalidArguments, (int) native.Result(id)["code"], id);
			}
		}

		[Test]
		public void InvalidMessages_AreLoggedAndIgnored()
		{
			var native = OpenAndShow();

			foreach (var text in new[]
				{
					"", "not json", "[]", "{\"type\":\"other\"}", "{\"type\":\"call\",\"command\":\"echo\"}", "{\"type\":\"call\",\"id\":{},\"command\":\"echo\"}",
					"{\"type\":\"call\",\"id\":\"" + new string('x', 65) + "\",\"command\":\"echo\"}", "{\"type\":\"ready\"} {}"
				})
			{
				LogAssert.Expect(LogType.Warning, new Regex("Ignored an invalid web view message"));
				native.Listener.OnMessage(text);
			}

			Assert.IsEmpty(native.Scripts);
		}

		[Test]
		public void Call_HandlerException_AnswersUnknownAndLogsError()
		{
			_service.AddCommand("broken", (command, token) => throw new InvalidOperationException("handler bug"));
			var native = OpenAndShow();
			LogAssert.Expect(LogType.Error, new Regex("Web view command 'broken' failed"));
			LogAssert.Expect(LogType.Exception, new Regex("handler bug"));

			native.Call("1", "broken");

			Assert.AreEqual(ResultCodes.Unknown, (int) native.Result("1")["code"]);
		}

		[Test]
		public void Call_PendingWhenClosed_CancelsTheTokenAndDropsTheResult()
		{
			var source = new UniTaskCompletionSource<Result<object>>();
			var token = default(CancellationToken);
			_service.AddCommand("slow", (command, cancellationToken) =>
			{
				token = cancellationToken;
				return source.Task;
			});
			var native = OpenAndShow();
			native.Call("1", "slow");

			_service.Close();
			source.TrySetResult(Result<object>.Ok("late"));

			Assert.IsTrue(token.IsCancellationRequested);
			Assert.IsEmpty(native.Scripts);
		}

		[Test]
		public void Call_FromAPageOutsideTheAllowedOrigins_IsIgnored()
		{
			var called = false;
			_service.AddCommand("echo", (command, token) =>
			{
				called = true;
				return UniTask.FromResult(Result<object>.Ok(null));
			});
			var native = OpenAndShow();
			native.Listener.OnPageLoaded("https://evil.com/");
			LogAssert.Expect(LogType.Warning, new Regex("outside the allowed origins: https://evil.com/"));

			native.Call("1", "echo");

			Assert.IsFalse(called);
			Assert.IsEmpty(native.Scripts);
			Assert.AreEqual("https://evil.com/", _service.PageUrl);
		}

		[Test]
		public void Call_AnswerAfterThePageLeftTheAllowedOrigins_IsDropped()
		{
			var source = new UniTaskCompletionSource<Result<object>>();
			_service.AddCommand("slow", (command, token) => source.Task);
			var native = OpenAndShow();
			native.Call("1", "slow");

			native.Listener.OnPageLoaded("https://evil.com/");
			source.TrySetResult(Result<object>.Ok("secret"));

			Assert.IsEmpty(native.Scripts);
		}

		[Test]
		public void Call_EscapesLineSeparatorsForOldJavaScriptEngines()
		{
			_service.AddCommand("text", Returns(Result<object>.Ok("a\u2028b\u2029c")));
			var native = OpenAndShow();

			native.Call("1", "text");

			StringAssert.DoesNotContain("\u2028", native.Scripts[0]);
			StringAssert.Contains("a\\u2028b\\u2029c", native.Scripts[0]);
			Assert.AreEqual("a\u2028b\u2029c", (string) native.Result("1")["value"]);
		}

		[Test]
		public void Ready_GivesTheKeyToTheMainFrameAndOnlyBoundOpens()
		{
			var open = _service.OpenAsync(PageUrl);
			var native = _factory.Last;

			native.SendReady();

			Assert.AreEqual(1, native.Binds.Count);
			Assert.AreEqual(FakeWebViewNative.Doc, (string) native.Binds[0]["doc"]);
			StringAssert.IsMatch("^[0-9a-f]{32}$", native.Key);
			Assert.IsFalse(open.Status.IsCompleted(), "a ready from any frame does not open the web view");

			LogAssert.Expect(LogType.Warning, new Regex("without the session key"));
			native.Send(new JObject { ["type"] = "bound", ["key"] = "guessed" });
			Assert.IsFalse(open.Status.IsCompleted());

			native.Send(new JObject { ["type"] = "bound" });
			Assert.IsTrue(Run(open).IsSuccess);
		}

		[Test]
		public void EachOpen_UsesANewKey()
		{
			var first = OpenAndShow().Key;
			_service.Close();

			Assert.AreNotEqual(first, OpenAndShow().Key);
		}

		[Test]
		public void Call_WithoutTheSessionKey_IsIgnored()
		{
			var called = false;
			_service.AddCommand("echo", (command, token) =>
			{
				called = true;
				return UniTask.FromResult(Result<object>.Ok(null));
			});
			var native = OpenAndShow();

			// 다른 출처의 iframe 은 준비를 알려도 키를 받지 못한다(키는 메인 프레임에서만 실행되는 스크립트로 감)
			native.SendReady("iframe-doc");
			LogAssert.Expect(LogType.Warning, new Regex("without the session key"));
			LogAssert.Expect(LogType.Warning, new Regex("without the session key"));
			native.Listener.OnMessage(new JObject { ["type"] = "call", ["doc"] = "iframe-doc", ["id"] = "1", ["command"] = "echo" }.ToString());
			native.Listener.OnMessage(new JObject { ["type"] = "call", ["doc"] = "iframe-doc", ["key"] = "guessed", ["id"] = "2", ["command"] = "echo" }.ToString());

			Assert.IsFalse(called);
			Assert.IsEmpty(native.Scripts);
		}

		[Test]
		public void Result_GoesToTheDocumentThatCalled()
		{
			_service.AddCommand("echo", Returns(Result<object>.Ok(null)));
			var native = OpenAndShow();
			var key = native.Key;

			// 같은 웹뷰에서 다른 페이지로 옮김: 새 문서도 같은 열기의 키를 받는다
			native.SendReady("doc-2");
			native.Send(new JObject { ["type"] = "call", ["doc"] = "doc-2", ["id"] = "1", ["command"] = "echo" });

			Assert.AreEqual("doc-2", (string) native.Binds[1]["doc"]);
			Assert.AreEqual(key, native.Key);
			Assert.AreEqual("doc-2", (string) native.Result("1")["doc"]);
		}

		[Test]
		public void Call_HandlerCancellationOrTimeout_IsANormalFailure()
		{
			_service.AddCommand("cancelled", (command, token) => throw new OperationCanceledException());
			_service.AddCommand("timeout", (command, token) => throw new TimeoutException("server slow"));
			var native = OpenAndShow();

			native.Call("1", "cancelled");
			native.Call("2", "timeout");

			Assert.AreEqual(ResultCodes.Cancelled, (int) native.Result("1")["code"]);
			Assert.AreEqual(ResultCodes.Timeout, (int) native.Result("2")["code"]);
			Assert.AreEqual("server slow", (string) native.Result("2")["message"]);
		}

		[Test]
		public void Close_CancellationCallbackThrows_StillDisposesTheWebView()
		{
			_service.AddCommand("slow", (command, token) =>
			{
				token.Register(() => throw new InvalidOperationException("callback bug"));
				return new UniTaskCompletionSource<Result<object>>().Task;
			});
			var native = OpenAndShow();
			native.Call("1", "slow");
			LogAssert.Expect(LogType.Error, new Regex("cancellation callback threw"));
			LogAssert.Expect(LogType.Exception, new Regex("callback bug"));

			_service.Close();

			Assert.IsTrue(native.Disposed);
			Assert.IsFalse(_service.IsOpen);
			Assert.AreEqual(1, _closed);
		}

		[Test]
		public void Open_TimeoutWithAThrowingCancellationCallback_StillFinishes()
		{
			_service.AddCommand("slow", (command, token) =>
			{
				token.Register(() => throw new InvalidOperationException("callback bug"));
				return new UniTaskCompletionSource<Result<object>>().Task;
			});
			var open = _service.OpenAsync(PageUrl);
			var native = _factory.Last;
			native.SendReady();
			native.Call("1", "slow");
			LogAssert.Expect(LogType.Warning, new Regex("did not start the web view bridge"));
			LogAssert.Expect(LogType.Error, new Regex("cancellation callback threw"));
			LogAssert.Expect(LogType.Exception, new Regex("callback bug"));

			_timeout.TrySetResult();

			Assert.AreEqual(ResultCodes.Timeout, Run(open).Code);
			Assert.IsTrue(native.Disposed);
			Assert.IsFalse(_service.IsOpen);
		}

		[Test]
		public void Layout_ScreenSizeChangeWithTheSameMargins_IsSentAgain()
		{
			var size = new Vector2Int(1080, 1920);
			_service.SafeArea = () => new Rect(0, 0, size.x, size.y);
			_service.ScreenSize = () => size;
			var native = OpenAndShow();

			_service.RefreshLayout();
			size = new Vector2Int(1920, 1080);
			_service.RefreshLayout();

			CollectionAssert.AreEqual(new[] { new WebViewMargins(0, 0, 0, 0), new WebViewMargins(0, 0, 0, 0) }, native.Margins);
		}

		[Test]
		public void Scripts_RunOnlyWhileTheMainFrameIsOnAnAllowedOrigin()
		{
			_service.AddCommand("echo", Returns(Result<object>.Ok(null)));
			var native = OpenAndShow();

			native.Call("1", "echo");
			_service.Emit("notice.changed");

			foreach (var script in native.Scripts)
			{
				StringAssert.StartsWith(
					"(function(o){if(o===\"https://notice.example.com\"||o===\"http://127.0.0.1:5700\"){window.geuneda&&window.geuneda.__receive(", script);
				StringAssert.EndsWith(");}})(window.location.origin);", script);
			}

			Assert.AreEqual(2, native.Scripts.Count);
		}

		[Test]
		public void PageStarted_OffTheAllowedOrigins_ClosesTheWebView()
		{
			var native = OpenAndShow();

			// 이동 제한을 거친 시작 알림(허용 출처, http·https 가 아님)은 그대로 둔다
			native.Listener.OnPageStarted("https://notice.example.com/ko/detail.html");
			native.Listener.OnPageStarted("about:blank");
			native.Listener.OnPageStarted("about:srcdoc");
			Assert.IsTrue(_service.IsOpen);

			// 이동 제한을 비껴간 메인 프레임 이동(Android 는 POST 이동에 이동 제한 콜백을 부르지 않음)
			LogAssert.Expect(LogType.Warning, new Regex("navigated off the allowed origins"));
			native.Listener.OnPageStarted("https://evil.com/collect");

			Assert.IsTrue(native.Disposed);
			Assert.AreEqual(1, _closed);
		}

		[Test]
		public void PageStarted_OffTheAllowedOriginsWhileOpening_FailsTheOpen()
		{
			var open = _service.OpenAsync(PageUrl);
			LogAssert.Expect(LogType.Warning, new Regex("navigated off the allowed origins: https://evil.com/"));

			_factory.Last.Listener.OnPageStarted("https://evil.com/");

			Assert.AreEqual(WebViewResultCodes.LoadFailed, Run(open).Code);
			Assert.IsTrue(_factory.Last.Disposed);
		}

		[Test]
		public void CloseCommand_ClosesTheWebView()
		{
			var native = OpenAndShow();

			native.Call("1", "close");

			Assert.IsTrue(native.Disposed);
			Assert.AreEqual(1, _closed);
		}

		[Test]
		public void OpenUrlCommand_OpensHttpUrlsInTheSystemBrowser()
		{
			var native = OpenAndShow();

			native.Call("1", "openUrl", new JObject { ["url"] = "https://www.youtube.com/watch?v=1" });
			native.Call("2", "openUrl", new JObject { ["url"] = "javascript:alert(1)" });
			native.Call("3", "openUrl");

			CollectionAssert.AreEqual(new[] { "https://www.youtube.com/watch?v=1" }, _opened);
			Assert.AreEqual(true, (bool) native.Result("1")["ok"]);
			Assert.AreEqual(WebViewResultCodes.InvalidArguments, (int) native.Result("2")["code"]);
			Assert.AreEqual(WebViewResultCodes.InvalidArguments, (int) native.Result("3")["code"]);
		}

		[Test]
		public void Emit_SendsEventsOnlyToAnOpenPage()
		{
			_service.Emit("notice.changed");
			_service.OpenAsync(PageUrl).Forget();
			_service.Emit("notice.changed");
			var native = _factory.Last;
			native.Ready();

			_service.Emit("notice.changed", new JObject { ["count"] = 2 });

			var events = native.Received();
			Assert.AreEqual(1, events.Count);
			Assert.AreEqual("event", (string) events[0]["type"]);
			Assert.AreEqual("notice.changed", (string) events[0]["name"]);
			Assert.AreEqual(2, (int) events[0]["value"]["count"]);
			Assert.Throws<ArgumentException>(() => _service.Emit("bad name"));
		}

		[Test]
		public void HandleBack_GoesBackThenCloses()
		{
			Assert.IsFalse(_service.HandleBack());
			var native = OpenAndShow();
			native.CanGoBack = true;

			Assert.IsTrue(_service.HandleBack());
			Assert.AreEqual(1, native.GoBackCount);
			Assert.IsTrue(_service.IsOpen);

			native.CanGoBack = false;

			Assert.IsTrue(_service.HandleBack());
			Assert.IsTrue(native.Disposed);
			Assert.AreEqual(1, _closed);
		}

		[Test]
		public void HandleBack_WhileOpening_CancelsTheOpen()
		{
			var open = _service.OpenAsync(PageUrl);
			_factory.Last.CanGoBack = true;

			Assert.IsTrue(_service.HandleBack());
			Assert.AreEqual(ResultCodes.Cancelled, Run(open).Code);
			Assert.AreEqual(0, _factory.Last.GoBackCount);
		}

		[Test]
		public void AddCommand_ValidatesAndRemovesOnDispose()
		{
			WebViewCommandHandler handler = Returns(Result<object>.Ok(null));

			Assert.Throws<ArgumentException>(() => _service.AddCommand("close", handler));
			Assert.Throws<ArgumentException>(() => _service.AddCommand("openUrl", handler));
			Assert.Throws<ArgumentException>(() => _service.AddCommand("bad name", handler));
			Assert.Throws<ArgumentException>(() => _service.AddCommand(new string('a', 65), handler));
			Assert.Throws<ArgumentNullException>(() => _service.AddCommand("echo", null));

			var first = _service.AddCommand("echo", handler);
			Assert.Throws<InvalidOperationException>(() => _service.AddCommand("echo", handler));

			first.Dispose();
			var second = _service.AddCommand("echo", handler);
			first.Dispose();
			var native = OpenAndShow();
			native.Call("1", "echo");

			Assert.AreEqual(true, (bool) native.Result("1")["ok"], "disposing an old registration keeps the new one");

			second.Dispose();
			native.Call("2", "echo");

			Assert.AreEqual(WebViewResultCodes.UnknownCommand, (int) native.Result("2")["code"]);
		}

		[Test]
		public void Layout_FollowsTheFrameAndClosesWhenItIsDestroyed()
		{
			var gameObject = new GameObject("WebViewFrame", typeof(RectTransform));

			try
			{
				var frame = (RectTransform) gameObject.transform;
				frame.position = new Vector3(540, 960, 0);
				frame.sizeDelta = new Vector2(800, 1200);
				var native = OpenAndShow(frame);

				_service.RefreshLayout();
				frame.sizeDelta = new Vector2(1000, 1200);
				_service.RefreshLayout();

				CollectionAssert.AreEqual(new[] { new WebViewMargins(140, 360, 140, 360), new WebViewMargins(40, 360, 40, 360) }, native.Margins);

				Object.DestroyImmediate(gameObject);
				_service.RefreshLayout();

				Assert.IsTrue(native.Disposed);
				Assert.AreEqual(1, _closed);
			}
			finally
			{
				if (gameObject != null)
				{
					Object.DestroyImmediate(gameObject);
				}
			}
		}

		[Test]
		public void Layout_ClipsTheFrameToTheScreen()
		{
			Assert.AreEqual(new WebViewMargins(0, 0, 0, 0), WebViewLayout.Margins(new Rect(-10, -10, 2000, 3000), 1080, 1920));
			Assert.AreEqual(new WebViewMargins(10, 20, 30, 40), WebViewLayout.Margins(Rect.MinMaxRect(10.4f, 39.6f, 1049.5f, 1900f), 1080, 1920));
		}

		[Test]
		public void Dispose_ClosesAndRejectsNewOpens()
		{
			var native = OpenAndShow();

			_service.Dispose();

			Assert.IsTrue(native.Disposed);
			Assert.AreEqual(1, _closed);
			Assert.Throws<ObjectDisposedException>(() => _service.OpenAsync(PageUrl).Forget());
		}

		public sealed class Profile
		{
			public string NickName { get; set; }
			public int Level { get; set; }
			public string Title { get; set; }
		}
	}
}

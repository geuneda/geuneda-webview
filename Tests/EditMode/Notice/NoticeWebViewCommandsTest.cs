using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Core;
using Geuneda.Http;
using Geuneda.Notice;
using Geuneda.WebView.Tests;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

// ReSharper disable once CheckNamespace

namespace Geuneda.WebView.Notice.Tests
{
	[TestFixture]
	public class NoticeWebViewCommandsTest
	{
		private const string StateKey = "geuneda.webview.tests.notice";
		private const string Document = "{\"defaultLanguage\":\"en\",\"notices\":[" +
			"{\"id\":\"maintenance\",\"priority\":10,\"startsAt\":\"2026-10-01T00:00:00Z\",\"endsAt\":\"2026-11-01T00:00:00Z\",\"popup\":true," +
			"\"contents\":{\"ko\":{\"title\":\"점검\",\"body\":\"본문\",\"linkUrl\":\"https://notice.example.com/m\",\"linkLabel\":\"자세히\"},\"en\":{\"title\":\"Maintenance\"}}}," +
			"{\"id\":\"event-1\",\"category\":\"event\",\"action\":\"shop:gem\",\"contents\":{\"en\":{\"title\":\"Event\"}}}," +
			"{\"id\":\"expired\",\"endsAt\":\"2026-01-01T00:00:00Z\",\"contents\":{\"en\":{\"title\":\"Old\"}}}]}";

		private static readonly DateTime Now = new DateTime(2026, 10, 17, 0, 0, 0, DateTimeKind.Utc);

		private FakeWebViewFactory _factory;
		private WebViewService _webView;
		private DocumentTransport _transport;
		private NoticeService _notices;
		private IDisposable _commands;
		private FakeWebViewNative _native;

		[SetUp]
		public void SetUp()
		{
			PlayerPrefs.DeleteKey(StateKey);
			_factory = new FakeWebViewFactory();
			_webView = new WebViewService(new WebViewServiceOptions { Factory = _factory, AllowedOrigins = new[] { "https://notice.example.com" } });
			_transport = new DocumentTransport { Json = Document };
			_notices = new NoticeService(new NoticeServiceOptions
			{
				HttpClient = new HttpClientBuilder().WithBaseAddress("https://api.example.com/").WithTransport(_transport).Build(),
				DocumentUri = "notices",
				Language = "ko",
				Platform = NoticePlatforms.Android,
				StateKey = StateKey
			});
			Assert.IsTrue(Run(_notices.RefreshAsync()).IsSuccess);
			_commands = NoticeWebViewCommands.Add(_webView, _notices, () => Now);

			var open = _webView.OpenAsync("https://notice.example.com/ko/index.html");
			_factory.Last.Ready();
			Assert.IsTrue(Run(open).IsSuccess);
			_native = _factory.Last;
		}

		[TearDown]
		public void TearDown()
		{
			_commands.Dispose();
			_webView.Dispose();
			PlayerPrefs.DeleteKey(StateKey);
		}

		private static T Run<T>(UniTask<T> task)
		{
			Assert.IsTrue(task.Status.IsCompleted(), "Expected the task to complete synchronously");
			return task.GetAwaiter().GetResult();
		}

		private JObject Call(string id, string command, JObject args = null)
		{
			_native.Call(id, command, args);
			return _native.Result(id);
		}

		[Test]
		public void List_ReturnsTheFilteredNoticesWithReadState()
		{
			_notices.MarkRead("event-1");

			var result = Call("1", NoticeWebViewCommands.List);

			Assert.AreEqual(true, (bool) result["ok"], result.ToString());
			var value = result["value"];
			Assert.AreEqual("ko", (string) value["language"]);
			Assert.AreEqual(1, (int) value["unreadCount"]);

			var notices = (JArray) value["notices"];
			Assert.AreEqual(2, notices.Count);

			var maintenance = notices[0];
			Assert.AreEqual("maintenance", (string) maintenance["id"]);
			Assert.AreEqual("notice", (string) maintenance["category"]);
			Assert.AreEqual(10, (int) maintenance["priority"]);
			Assert.AreEqual(true, (bool) maintenance["popup"]);
			Assert.AreEqual("점검", (string) maintenance["title"]);
			Assert.AreEqual("본문", (string) maintenance["body"]);
			Assert.AreEqual("https://notice.example.com/m", (string) maintenance["linkUrl"]);
			Assert.AreEqual("자세히", (string) maintenance["linkLabel"]);
			Assert.AreEqual(false, (bool) maintenance["read"]);
			var script = _native.Scripts[_native.Scripts.Count - 1];
			StringAssert.Contains("\"startsAt\":\"2026-10-01T00:00:00Z\"", script);
			StringAssert.Contains("\"endsAt\":\"2026-11-01T00:00:00Z\"", script);
			Assert.IsNull(maintenance["action"]);

			var notice = notices[1];
			Assert.AreEqual("event-1", (string) notice["id"]);
			Assert.AreEqual("shop:gem", (string) notice["action"]);
			Assert.AreEqual("Event", (string) notice["title"], "falls back to the document language");
			Assert.AreEqual(true, (bool) notice["read"]);
			Assert.IsNull(notice["startsAt"]);
			Assert.IsNull(notice["body"]);
		}

		[Test]
		public void List_FiltersByCategory()
		{
			var result = Call("1", NoticeWebViewCommands.List, new JObject { ["category"] = "event" });
			var notices = (JArray) result["value"]["notices"];

			Assert.AreEqual(1, notices.Count);
			Assert.AreEqual("event-1", (string) notices[0]["id"]);
			Assert.AreEqual(1, (int) result["value"]["unreadCount"]);
		}

		[Test]
		public void List_RejectsANonStringCategory()
		{
			Assert.AreEqual(WebViewResultCodes.InvalidArguments, (int) Call("1", NoticeWebViewCommands.List, new JObject { ["category"] = 1 })["code"]);
		}

		[Test]
		public void Read_MarksTheNoticeAndSendsChanged()
		{
			var result = Call("1", NoticeWebViewCommands.Read, new JObject { ["id"] = "maintenance" });

			Assert.AreEqual(true, (bool) result["ok"]);
			Assert.IsTrue(_notices.IsRead("maintenance"));
			Assert.IsTrue(HasChangedEvent());
		}

		[Test]
		public void Read_RejectsMissingOrInvalidIds()
		{
			Assert.AreEqual(WebViewResultCodes.InvalidArguments, (int) Call("1", NoticeWebViewCommands.Read)["code"]);
			Assert.AreEqual(WebViewResultCodes.InvalidArguments, (int) Call("2", NoticeWebViewCommands.Read, new JObject { ["id"] = "bad id" })["code"]);
		}

		[Test]
		public void Refresh_PassesTheNoticeResultThrough()
		{
			_transport.Json = "{\"notices\":[";

			var failed = Call("1", NoticeWebViewCommands.Refresh);

			Assert.AreEqual(NoticeResultCodes.InvalidDocument, (int) failed["code"]);

			_transport.Json = "{\"defaultLanguage\":\"en\",\"notices\":[]}";

			Assert.AreEqual(true, (bool) Call("2", NoticeWebViewCommands.Refresh)["ok"]);
			Assert.IsTrue(HasChangedEvent());
		}

		[Test]
		public void Dispose_RemovesTheCommandsAndStopsTheEvents()
		{
			_commands.Dispose();
			_commands.Dispose();
			_notices.MarkRead("maintenance");

			Assert.AreEqual(WebViewResultCodes.UnknownCommand, (int) Call("1", NoticeWebViewCommands.List)["code"]);
			Assert.IsFalse(HasChangedEvent());

			_commands = NoticeWebViewCommands.Add(_webView, _notices, () => Now);
		}

		private bool HasChangedEvent()
		{
			foreach (var message in _native.Received())
			{
				if ((string) message["type"] == "event" && (string) message["name"] == NoticeWebViewCommands.ChangedEvent)
				{
					return true;
				}
			}

			return false;
		}

		private sealed class DocumentTransport : IHttpTransport
		{
			public string Json { get; set; }

			public UniTask<Result<HttpResponse>> SendAsync(HttpRequest request, CancellationToken cancellationToken)
			{
				var response = new HttpResponse(request.Uri, 200, Encoding.UTF8.GetBytes(Json), new Dictionary<string, string>());
				return UniTask.FromResult(Result<HttpResponse>.Ok(response));
			}
		}
	}
}

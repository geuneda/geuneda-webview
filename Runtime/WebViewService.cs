using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using UnityEngine;

// ReSharper disable once CheckNamespace

namespace Geuneda.WebView
{
	/// <summary>
	/// 기본 웹뷰입니다. 부트스트랩에서 만들어 <see cref="IWebViewService"/> 로 바인딩합니다. 규칙은 <see cref="IWebViewService"/> 를 봅니다.
	/// <c>Dispose</c> 는 열린 웹뷰를 닫습니다.
	/// </summary>
	public sealed partial class WebViewService : IWebViewService, IDisposable
	{
		private static readonly ModuleLogger Logger = new ModuleLogger("Geuneda.WebView");

		private const string CloseCommand = "close";
		private const string OpenUrlCommand = "openUrl";

		private readonly IWebViewFactory _factory;
		private readonly HashSet<string> _origins;
		private readonly WebViewNativeSettings _settings;
		private readonly string _originCondition;
		private readonly TimeSpan _loadTimeout;
		private readonly int _mainThreadId;
		private readonly Dictionary<string, CommandRegistration> _commands = new Dictionary<string, CommandRegistration>(StringComparer.Ordinal);
		private readonly Vector3[] _corners = new Vector3[4];

		// 페이지 JS 가 읽는 camelCase. 설정 객체는 바꿀 수 있으므로 서비스마다 따로 둔다
		private readonly JsonSerializer _serializer = JsonSerializer.Create(new JsonSerializerSettings
		{
			ContractResolver = new CamelCasePropertyNamesContractResolver(),
			NullValueHandling = NullValueHandling.Ignore
		});

		private Session _session;
		private bool _disposed;

		// 테스트가 바꾼다
		internal Func<TimeSpan, CancellationToken, UniTask> Delay = (delay, token) => UniTask.Delay(delay, DelayType.Realtime, PlayerLoopTiming.Update, token);
		internal Action<string> OpenExternal = Application.OpenURL;
		internal Func<Rect> SafeArea = () => Screen.safeArea;
		internal Func<Vector2Int> ScreenSize = () => new Vector2Int(Screen.width, Screen.height);

		/// <summary>메인 스레드에서 만듭니다(다른 스레드에서 온 취소를 메인 스레드로 옮길 때 기준).</summary>
		/// <exception cref="ArgumentNullException"><paramref name="options"/> 가 null 인 경우</exception>
		/// <exception cref="ArgumentException">어댑터가 없거나, 허용 출처가 없거나 모양이 틀렸거나, 제한 시간이 0 이하인 경우</exception>
		public WebViewService(WebViewServiceOptions options)
		{
			if (options == null)
			{
				throw new ArgumentNullException(nameof(options));
			}

			if (options.Factory == null)
			{
				throw new ArgumentException("Factory is required", nameof(options));
			}

			if (options.LoadTimeout <= TimeSpan.Zero)
			{
				throw new ArgumentException("LoadTimeout must be positive", nameof(options));
			}

			var origins = new List<string>();

			foreach (var origin in options.AllowedOrigins ?? Array.Empty<string>())
			{
				var normalized = WebViewOrigins.Normalize(origin) ??
					throw new ArgumentException($"Allowed origin must look like https://host[:port] with an ASCII host and no path: '{origin}'", nameof(options));

				if (!origins.Contains(normalized))
				{
					origins.Add(normalized);
				}
			}

			if (origins.Count == 0)
			{
				throw new ArgumentException("At least one allowed origin is required", nameof(options));
			}

			_factory = options.Factory;
			_origins = new HashSet<string>(origins, StringComparer.Ordinal);
			_settings = new WebViewNativeSettings(origins.AsReadOnly(), WebViewOrigins.BuildDenyPattern(origins));
			_originCondition = WebViewMessages.OriginCondition(origins);
			_loadTimeout = options.LoadTimeout;
			_mainThreadId = Thread.CurrentThread.ManagedThreadId;
		}

		public event Action Closed;

		public bool IsOpen => _session != null;

		public string PageUrl => _session?.PageUrl;

		// 오용 예외를 호출 자리에서 던지려고 async 가 아닌 메서드에서 검사한다(async 메서드는 예외를 작업에 담음)
		public UniTask<Result> OpenAsync(string url, RectTransform frame = null, CancellationToken cancellationToken = default)
		{
			if (_disposed)
			{
				throw new ObjectDisposedException(nameof(WebViewService));
			}

			if (!IsAllowedPage(url))
			{
				throw new ArgumentException($"Url must be an absolute http or https url on an allowed origin: '{url}'", nameof(url));
			}

			if (_session != null)
			{
				return UniTask.FromResult(Result.Fail(ResultCodes.InProgress, "A web view is already open"));
			}

			if (cancellationToken.IsCancellationRequested)
			{
				return UniTask.FromResult(Result.Fail(ResultCodes.Cancelled, "Opening the web view was cancelled"));
			}

			return OpenCoreAsync(url, frame, cancellationToken);
		}

		private async UniTask<Result> OpenCoreAsync(string url, RectTransform frame, CancellationToken cancellationToken)
		{
			var session = new Session(this, frame, frame != null ? WebViewLayout.CameraOf(frame) : null, url, NewKey());
			_session = session;

			try
			{
				session.Native = _factory.Create(_settings, session) ?? throw new InvalidOperationException("Web view factory returned null");
			}
			catch
			{
				_session = null;
				throw;
			}

			RefreshLayout(session);
			session.Native.LoadUrl(url);
			PlayerLoopHelper.AddAction(PlayerLoopTiming.LastPostLateUpdate, session);
			WaitLoadTimeoutAsync(session).Forget();

			using (cancellationToken.RegisterWithoutCaptureExecutionContext(() => CancelOpening(session)))
			{
				return await session.Opening.Task;
			}
		}

		public void Close()
		{
			var session = _session;

			if (session == null)
			{
				return;
			}

			if (!session.Shown)
			{
				FinishOpening(session, Result.Fail(ResultCodes.Cancelled, "The web view was closed while opening"));
				return;
			}

			Teardown(session);

			foreach (var handler in Closed?.GetInvocationList() ?? Array.Empty<Delegate>())
			{
				try
				{
					((Action) handler)();
				}
				catch (Exception exception)
				{
					Logger.Error(exception, "Web view Closed handler failed");
				}
			}
		}

		public bool HandleBack()
		{
			var session = _session;

			if (session == null)
			{
				return false;
			}

			if (session.Shown && session.Native.CanGoBack)
			{
				session.Native.GoBack();
				return true;
			}

			Close();
			return true;
		}

		public IDisposable AddCommand(string name, WebViewCommandHandler handler)
		{
			if (!WebViewMessages.IsName(name))
			{
				throw new ArgumentException($"Command name must be 1-{WebViewMessages.MaxNameLength} letters, digits, '.', '_' or '-': '{name}'", nameof(name));
			}

			if (name == CloseCommand || name == OpenUrlCommand)
			{
				throw new ArgumentException($"'{name}' is a built-in command", nameof(name));
			}

			if (handler == null)
			{
				throw new ArgumentNullException(nameof(handler));
			}

			if (_commands.ContainsKey(name))
			{
				throw new InvalidOperationException($"Command '{name}' is already added");
			}

			var registration = new CommandRegistration(this, name, handler);
			_commands.Add(name, registration);
			return registration;
		}

		public void Emit(string name, object value = null)
		{
			if (!WebViewMessages.IsName(name))
			{
				throw new ArgumentException($"Event name must be 1-{WebViewMessages.MaxNameLength} letters, digits, '.', '_' or '-': '{name}'", nameof(name));
			}

			var session = _session;

			if (session == null || !session.Shown)
			{
				Logger.Debug($"Dropped web view event '{name}': no page is open");
				return;
			}

			Deliver(session, WebViewMessages.EventScript(name, value, _serializer, _originCondition));
		}

		public void Dispose()
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			Close();
		}

		/// <summary>지금 영역으로 여백을 맞춥니다(열려 있는 동안 매 프레임 <c>LastPostLateUpdate</c>). 테스트가 직접 부릅니다.</summary>
		internal void RefreshLayout()
		{
			var session = _session;

			if (session != null)
			{
				RefreshLayout(session);
			}
		}

		private bool RefreshLayout(Session session)
		{
			if (session != _session)
			{
				return false;
			}

			Rect rect;

			if (session.HasFrame)
			{
				if (session.Frame == null)
				{
					Logger.Debug("The web view frame was destroyed; closing the web view");
					Close();
					return false;
				}

				rect = WebViewLayout.ScreenRect(session.Frame, session.Camera, _corners);
			}
			else
			{
				rect = SafeArea();
			}

			var size = ScreenSize();
			var margins = WebViewLayout.Margins(rect, size.x, size.y);

			// 어댑터의 여백 적용은 네이티브 호출이라 바뀔 때만 한다. 여백이 같아도 화면 크기가 바뀌면 다시 넘긴다(데스크톱 gree 는 화면 크기 - 여백으로 뷰 크기를 정함)
			if (!session.HasMargins || margins != session.Margins || size != session.ScreenSize)
			{
				session.Native.SetMargins(margins);
				session.Margins = margins;
				session.ScreenSize = size;
				session.HasMargins = true;
			}

			return true;
		}

		private async UniTaskVoid WaitLoadTimeoutAsync(Session session)
		{
			try
			{
				await Delay(_loadTimeout, session.Cancellation.Token);
			}
			catch (OperationCanceledException)
			{
				return;
			}

			if (session == _session && !session.Shown)
			{
				FinishOpening(session, Result.Fail(ResultCodes.Timeout,
					$"The page did not start the web view bridge within {_loadTimeout.TotalSeconds:0.###}s: {session.PageUrl}"));
			}
		}

		private void CancelOpening(Session session)
		{
			if (Thread.CurrentThread.ManagedThreadId != _mainThreadId)
			{
				MainThreadDispatcher.Post(() => CancelOpening(session));
				return;
			}

			if (session == _session && !session.Shown)
			{
				FinishOpening(session, Result.Fail(ResultCodes.Cancelled, "Opening the web view was cancelled"));
			}
		}

		private void FinishOpening(Session session, Result result)
		{
			if (result.IsSuccess)
			{
				session.Shown = true;
				session.Native.SetVisible(true);
				session.Opening.TrySetResult(result);
				return;
			}

			if (result.Code != ResultCodes.Cancelled)
			{
				Logger.Warning($"Could not open the web view: {result.Message}");
			}

			// 결과를 받은 호출자가 바로 다시 열 수 있게 먼저 정리한다. 정리가 던져도 열기는 끝낸다
			try
			{
				Teardown(session);
			}
			finally
			{
				session.Opening.TrySetResult(result);
			}
		}

		private void Teardown(Session session)
		{
			if (_session == session)
			{
				_session = null;
			}

			// 처리기가 토큰에 단 콜백이 던져도 네이티브는 버린다. 토큰 원천은 타이머·연결이 없어 해제하지 않는다(이미 받은 처리기가 토큰을 계속 쓸 수 있게)
			try
			{
				session.Cancellation.Cancel();
			}
			catch (AggregateException exception)
			{
				Logger.Error(exception, "A web view command cancellation callback threw");
			}
			finally
			{
				session.Native.Dispose();
			}
		}

		/// <summary>
		/// 메인 프레임이 허용 출처 밖 http·https 주소로 이동을 시작했습니다. 이동 제한을 거친 시작 알림은 허용 출처이거나 http·https 가 아니므로,
		/// 이것은 제한을 비껴간 이동(Android 는 POST 이동에 이동 제한 콜백을 부르지 않음)입니다. 그 페이지를 띄워 두지 않고 닫습니다.
		/// </summary>
		private void LeaveDisallowedPage(Session session, string url)
		{
			if (!session.Shown)
			{
				FinishOpening(session, Result.Fail(WebViewResultCodes.LoadFailed, $"The page navigated off the allowed origins: {url}"));
				return;
			}

			Logger.Warning($"The web view navigated off the allowed origins ({url}); closing it");
			Close();
		}

		private bool IsAllowedPage(string url)
		{
			var origin = WebViewOrigins.OriginOf(url);
			return origin != null && _origins.Contains(origin);
		}

		private static string NewKey()
		{
			var bytes = new byte[16];

			using (var random = RandomNumberGenerator.Create())
			{
				random.GetBytes(bytes);
			}

			return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
		}
	}
}

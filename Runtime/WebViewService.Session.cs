using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Core;
using UnityEngine;

// ReSharper disable once CheckNamespace

namespace Geuneda.WebView
{
	public sealed partial class WebViewService
	{
		/// <summary>열기 한 번입니다. 네이티브 알림과 프레임 처리를 이 열기에 묶어, 닫은 뒤 늦게 온 알림을 무시합니다.</summary>
		private sealed class Session : IWebViewNativeListener, IPlayerLoopItem
		{
			private readonly WebViewService _owner;

			public Session(WebViewService owner, RectTransform frame, Canvas rootCanvas, string url, string key)
			{
				_owner = owner;
				Frame = frame;
				HasFrame = frame != null;
				RootCanvas = rootCanvas;
				PageUrl = url;
				Key = key;
			}

			/// <summary>이 열기의 키입니다. 준비 알림에 답해 메인 프레임에만 넘기고, 이 키가 있는 글만 받습니다.</summary>
			public string Key { get; }

			public RectTransform Frame { get; }

			public bool HasFrame { get; }

			/// <summary>영역의 루트 캔버스(열 때 한 번 찾음)입니다. 영역이 없거나 캔버스 밖이면 null 입니다.</summary>
			public Canvas RootCanvas { get; }

			public CancellationTokenSource Cancellation { get; } = new CancellationTokenSource();

			public UniTaskCompletionSource<Result> Opening { get; } = new UniTaskCompletionSource<Result>();

			public IWebViewNative Native { get; set; }

			public string PageUrl { get; set; }

			public bool Shown { get; set; }

			/// <summary>어느 프레임에서든 준비 알림이 왔는지입니다(브리지 없는 오류 페이지 판정에만 씀).</summary>
			public bool ReadySeen { get; set; }

			public int HttpErrorStatus { get; set; }

			public bool HasMargins { get; set; }

			public WebViewMargins Margins { get; set; }

			public Vector2Int ScreenSize { get; set; }

			void IWebViewNativeListener.OnMessage(string message)
			{
				_owner.OnMessage(this, message);
			}

			void IWebViewNativeListener.OnPageStarted(string url)
			{
				if (this == _owner._session && WebViewOrigins.IsHttpUrl(url) && !_owner.IsAllowedPage(url))
				{
					_owner.LeaveDisallowedPage(this, url);
				}
			}

			void IWebViewNativeListener.OnPageLoaded(string url)
			{
				if (this != _owner._session)
				{
					return;
				}

				PageUrl = url;

				// 브리지 없이 로드가 끝났고 HTTP 오류가 있었으면 오류 페이지다(브리지 페이지는 준비 알림이 로드 끝보다 먼저 옴)
				if (!Shown && !ReadySeen && HttpErrorStatus >= 400)
				{
					_owner.FinishOpening(this, Result.Fail(WebViewResultCodes.LoadFailed, $"HTTP {HttpErrorStatus} without the web view bridge: {url}"));
				}
			}

			void IWebViewNativeListener.OnLoadError(string description)
			{
				if (this != _owner._session)
				{
					return;
				}

				if (!Shown)
				{
					_owner.FinishOpening(this, Result.Fail(WebViewResultCodes.LoadFailed, $"Page failed to load: {description}"));
					return;
				}

				Logger.Warning($"Web view page failed to load: {description}");
			}

			void IWebViewNativeListener.OnHttpError(int statusCode)
			{
				if (this != _owner._session)
				{
					return;
				}

				// Android 는 이미지 같은 리소스 오류도 온다. 열기 판정에만 쓰고 로그는 Debug
				if (!Shown && statusCode >= 400)
				{
					HttpErrorStatus = statusCode;
				}

				Logger.Debug($"Web view HTTP error {statusCode}");
			}

			bool IPlayerLoopItem.MoveNext()
			{
				return _owner.RefreshLayout(this);
			}
		}
	}
}

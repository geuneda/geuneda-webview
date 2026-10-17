// ReSharper disable once CheckNamespace

namespace Geuneda.WebView
{
	/// <summary>
	/// 네이티브 웹뷰를 만드는 어댑터 계약입니다(<c>com.geuneda.webview.gree</c> 의 <c>GreeWebViewFactory</c> 등). <see cref="WebViewService"/> 가 열 때마다 하나를 만들고 닫을 때 버립니다.
	/// </summary>
	public interface IWebViewFactory
	{
		/// <summary>
		/// 보이지 않는 웹뷰를 만듭니다. <paramref name="settings"/> 의 이동 제한을 첫 로드 전에 걸어야 합니다. 메인 스레드에서 부릅니다.
		/// </summary>
		/// <param name="listener">네이티브 알림을 받을 곳. 메인 스레드에서 부릅니다</param>
		IWebViewNative Create(WebViewNativeSettings settings, IWebViewNativeListener listener);
	}
}

using System;

// ReSharper disable once CheckNamespace

namespace Geuneda.WebView
{
	/// <summary>
	/// 네이티브 웹뷰 하나입니다(어댑터 구현). 만들 때 보이지 않고, 모든 메서드는 메인 스레드에서 부릅니다.
	/// 네이티브가 아직 준비되지 않았으면 준비된 뒤 마지막 값으로 적용합니다(<see cref="EvaluateJavaScript"/> 는 버림).
	/// </summary>
	public interface IWebViewNative : IDisposable
	{
		/// <summary>뒤로 갈 페이지 기록이 있는지입니다.</summary>
		bool CanGoBack { get; }

		void LoadUrl(string url);

		/// <summary>
		/// 지금 메인 프레임에서만 스크립트를 실행합니다(iframe 에서 실행하면 안 됨: 열기 키를 메인 프레임에만 넘기는 데 기댐). 결과는 받지 않습니다.
		/// </summary>
		void EvaluateJavaScript(string script);

		/// <summary>
		/// 화면 가장자리에서 웹뷰까지의 거리입니다(Unity 화면 픽셀, <c>Screen.width</c>·<c>Screen.height</c> 기준). 여백이 같아도 화면 크기가 바뀌면 다시 부르므로
		/// 그때 뷰 크기를 다시 맞춥니다.
		/// </summary>
		void SetMargins(WebViewMargins margins);

		void SetVisible(bool visible);

		void GoBack();
	}
}

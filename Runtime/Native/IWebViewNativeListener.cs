// ReSharper disable once CheckNamespace

namespace Geuneda.WebView
{
	/// <summary>어댑터가 네이티브 알림을 넘기는 곳입니다. 메인 스레드에서 부릅니다. 버린(<c>Dispose</c>) 뒤의 알림은 무시합니다.</summary>
	public interface IWebViewNativeListener
	{
		/// <summary>페이지의 JS 파일이 보낸 JSON 글입니다. 전달 경로의 인코딩(iOS·macOS 의 URL 인코딩 등)은 플러그인·어댑터가 풀어 넘깁니다.</summary>
		void OnMessage(string message);

		/// <summary>
		/// 이동을 시작했습니다(플랫폼에 따라 iframe 이동도 옴). 이동 제한을 거친 주소이거나, 제한을 비껴간 메인 프레임 이동(Android POST)입니다.
		/// 서비스는 허용 출처 밖 http·https 주소면 웹뷰를 닫습니다.
		/// </summary>
		void OnPageStarted(string url);

		/// <summary>메인 프레임 로드가 끝났습니다.</summary>
		void OnPageLoaded(string url);

		/// <summary>메인 프레임을 불러오지 못했습니다(연결 실패, 이동 막힘 등).</summary>
		void OnLoadError(string description);

		/// <summary>
		/// HTTP 오류 응답입니다. 플랫폼에 따라 메인 프레임이 아닌 리소스(이미지 등)의 오류도 옵니다(Android). 상태 코드를 모르면 0 입니다.
		/// </summary>
		void OnHttpError(int statusCode);
	}
}

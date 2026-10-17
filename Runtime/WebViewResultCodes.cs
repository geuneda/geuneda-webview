// ReSharper disable once CheckNamespace

namespace Geuneda.WebView
{
	/// <summary>
	/// 웹뷰 패키지의 결과 코드입니다(core 결과 코드 범위 표의 12000 블록). 제한 시간·취소·중복 열기는 core 공용 코드(<c>Timeout</c>, <c>Cancelled</c>, <c>InProgress</c>)입니다.
	/// 함께 주는 JS 파일(<c>Web~/geuneda-webview.js</c>)도 같은 값을 씁니다.
	/// </summary>
	public static class WebViewResultCodes
	{
		/// <summary>페이지를 열지 못했습니다: 연결 실패, HTTP 오류 페이지(브리지 준비 없이 로드가 끝남)</summary>
		public const int LoadFailed = 12001;

		/// <summary>등록되지 않은 명령입니다(페이지에 보내는 결과)</summary>
		public const int UnknownCommand = 12002;

		/// <summary>명령 인자가 없거나 형식이 맞지 않습니다(페이지에 보내는 결과)</summary>
		public const int InvalidArguments = 12003;

		/// <summary>페이지가 앱 웹뷰 밖(일반 브라우저)에서 열려 Unity 를 부를 수 없습니다. JS 파일만 돌려줍니다</summary>
		public const int BridgeUnavailable = 12004;
	}
}

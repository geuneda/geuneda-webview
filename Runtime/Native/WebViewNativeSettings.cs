using System.Collections.Generic;

// ReSharper disable once CheckNamespace

namespace Geuneda.WebView
{
	/// <summary>네이티브 웹뷰를 만들 때 넘기는 설정입니다. 만든 뒤 바뀌지 않습니다.</summary>
	public sealed class WebViewNativeSettings
	{
		internal WebViewNativeSettings(IReadOnlyList<string> allowedOrigins, string denyUrlPattern)
		{
			AllowedOrigins = allowedOrigins;
			DenyUrlPattern = denyUrlPattern;
		}

		/// <summary>허용한 출처(<c>https://host[:port]</c>, 소문자, 기본 포트 생략)입니다.</summary>
		public IReadOnlyList<string> AllowedOrigins { get; }

		/// <summary>
		/// 이동을 막을 주소 정규식입니다: 허용한 출처 밖의 <c>http</c>·<c>https</c> 주소. Java·ICU(NSRegularExpression)·.NET 이 같은 뜻으로 읽는 문법만 씁니다
		/// (<c>(?i)</c>, <c>^</c>, 부정 전방 탐색, 역슬래시 이스케이프). 주소의 어디서든 찾는 방식(<c>find</c>)으로 써도 시작에 고정되어 있습니다.
		/// </summary>
		public string DenyUrlPattern { get; }
	}
}

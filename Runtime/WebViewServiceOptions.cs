using System;
using System.Collections.Generic;

// ReSharper disable once CheckNamespace

namespace Geuneda.WebView
{
	/// <summary><see cref="WebViewService"/> 설정입니다. 서비스가 만들 때 복사하므로 그 뒤에 바꿔도 영향이 없습니다.</summary>
	public sealed class WebViewServiceOptions
	{
		/// <summary>네이티브 웹뷰 어댑터입니다(필수). 예: <c>new GreeWebViewFactory()</c>.</summary>
		public IWebViewFactory Factory { get; set; }

		/// <summary>
		/// 페이지를 열고 명령을 받을 출처입니다(하나 이상, <c>https://notice.example.com</c>, <c>http://127.0.0.1:5700</c> 처럼 경로 없이). 이 밖의 http·https 주소로는 이동하지 않습니다.
		/// </summary>
		public IEnumerable<string> AllowedOrigins { get; set; }

		/// <summary>페이지가 브리지 준비를 알릴 때까지 기다리는 시간입니다. 넘으면 열기가 <c>ResultCodes.Timeout</c> 입니다.</summary>
		public TimeSpan LoadTimeout { get; set; } = TimeSpan.FromSeconds(10);
	}
}

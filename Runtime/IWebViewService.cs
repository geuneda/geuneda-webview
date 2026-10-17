using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Core;
using UnityEngine;

// ReSharper disable once CheckNamespace

namespace Geuneda.WebView
{
	/// <summary>
	/// 앱 안 웹뷰 하나입니다. 기본 구현은 <see cref="WebViewService"/> 이고, services 의 <c>MainInstaller</c> 에 이 인터페이스로 바인딩합니다
	/// (정리는 <c>MainInstaller.CleanDispose&lt;IWebViewService&gt;()</c>).
	/// </summary>
	/// <remarks>
	/// <para>
	/// 페이지는 <c>Web~/geuneda-webview.js</c> 를 <c>&lt;script src&gt;</c> 로(<c>async</c>·<c>defer</c> 없이) 넣고 <c>geuneda.call(명령, 인자)</c> 로 명령을 부릅니다.
	/// 기본 명령은 <c>close</c>(닫기)와 <c>openUrl</c>(외부 브라우저)이고, 나머지는 <see cref="AddCommand"/> 로 더합니다(공지·쿠폰은 <c>NoticeWebViewCommands</c>, <c>CouponWebViewCommands</c>).
	/// </para>
	/// <para>
	/// 허용한 출처(<see cref="WebViewServiceOptions.AllowedOrigins"/>)의 페이지에서 온 명령만 실행하고, 그 밖의 http·https 주소로는 이동하지 않습니다.
	/// JS 파일이 다른 출처로 가는 링크를 외부 브라우저로 엽니다. 페이지에는 계정 토큰을 넘기지 않습니다(쿠폰 사용도 Unity 가 인증해 보냄).
	/// 명령은 이 열기의 키(메인 프레임에만 넘김)를 붙인 글만 실행하므로 다른 출처의 iframe 은 명령을 보낼 수 없습니다. 결과는 명령을 보낸 문서에만 갑니다.
	/// </para>
	/// <para>메인 스레드에서 씁니다. <see cref="Closed"/> 와 명령 처리기도 메인 스레드에서 옵니다.</para>
	/// <para>
	/// <see cref="IDisposable.Dispose"/> 는 열린 웹뷰를 닫고(열려 있었으면 <see cref="Closed"/>, 열기 중이면 그 열기는 <c>ResultCodes.Cancelled</c>) 그 뒤의 열기를 막습니다(<see cref="ObjectDisposedException"/>).
	/// 여러 번 불러도 됩니다.
	/// </para>
	/// </remarks>
	public interface IWebViewService : IDisposable
	{
		/// <summary>열고 있거나 열려 있으면 true 입니다.</summary>
		bool IsOpen { get; }

		/// <summary>지금 페이지 주소(마지막으로 로드가 끝난 메인 프레임, 그 전에는 연 주소)입니다. 닫혀 있으면 null 입니다.</summary>
		string PageUrl { get; }

		/// <summary>열려 있던 웹뷰가 닫혔습니다(<see cref="Close"/>, 페이지의 <c>close</c>, 뒤로가기, 영역 파괴). 열기에 실패하면 오지 않습니다. 처리기 예외는 로그만 남깁니다.</summary>
		event Action Closed;

		/// <summary>
		/// 웹뷰를 만들어 <paramref name="url"/> 을 엽니다. 페이지의 JS 파일과 연결되면(준비 알림 → 키 전달 → 키로 응답) 보이게 하고 성공합니다.
		/// </summary>
		/// <param name="url">허용한 출처의 http·https 절대 주소. 언어 등은 게임이 주소에 넣습니다</param>
		/// <param name="frame">웹뷰를 띄울 UI 영역. 매 프레임 위치·크기를 따라가고, 파괴되면 닫습니다. null 이면 안전 영역(<c>Screen.safeArea</c>) 전체입니다</param>
		/// <param name="cancellationToken">열기를 멈춥니다(<c>ResultCodes.Cancelled</c>, 웹뷰를 버림). 열린 뒤에는 영향이 없습니다</param>
		/// <returns>
		/// 실패: 연결 실패·HTTP 오류 페이지는 <see cref="WebViewResultCodes.LoadFailed"/>, 제한 시간 안에 연결되지 않으면 <c>ResultCodes.Timeout</c>,
		/// 취소·열기 중 닫기는 <c>ResultCodes.Cancelled</c>, 이미 열려 있으면 <c>ResultCodes.InProgress</c> 입니다. 실패하면 웹뷰를 버립니다.
		/// </returns>
		/// <exception cref="ArgumentException">주소가 허용한 출처의 http·https 절대 주소가 아닌 경우</exception>
		/// <exception cref="ObjectDisposedException"><see cref="IDisposable.Dispose"/> 뒤에 부른 경우</exception>
		UniTask<Result> OpenAsync(string url, RectTransform frame = null, CancellationToken cancellationToken = default);

		/// <summary>웹뷰를 닫고 버립니다. 진행 중인 명령 처리기의 토큰을 취소하고 그 결과는 버립니다. 닫혀 있으면 아무것도 하지 않습니다.</summary>
		void Close();

		/// <summary>
		/// 뒤로가기입니다. 페이지 기록이 있으면 이전 페이지로, 없으면 닫습니다(열기 중이면 열기를 멈춤). 처리했으면 true, 열려 있지 않으면 false 입니다.
		/// 게임의 뒤로가기 처리기(uiservice 등)가 웹뷰가 열려 있는 동안 부릅니다.
		/// </summary>
		bool HandleBack();

		/// <summary>페이지가 부를 명령을 더합니다. 돌려준 객체를 <c>Dispose</c> 하면 뺍니다.</summary>
		/// <param name="name">1~64자 ASCII 영문·숫자·<c>.</c>·<c>_</c>·<c>-</c>. 대소문자를 가립니다</param>
		/// <exception cref="ArgumentException">이름이 규칙에 맞지 않거나 기본 명령(<c>close</c>, <c>openUrl</c>)인 경우</exception>
		/// <exception cref="InvalidOperationException">같은 이름이 이미 있는 경우</exception>
		IDisposable AddCommand(string name, WebViewCommandHandler handler);

		/// <summary>
		/// 페이지에 이벤트를 보냅니다(<c>geuneda.on(name, handler)</c>). 값은 Newtonsoft camelCase 로 직렬화합니다(IL2CPP 에서는 <c>JObject</c> 나 <c>[Preserve]</c> 형식). 열린 페이지가 없으면 버립니다.
		/// </summary>
		/// <exception cref="ArgumentException">이름이 명령 이름 규칙에 맞지 않는 경우</exception>
		void Emit(string name, object value = null);
	}
}

using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Core;
using Newtonsoft.Json.Linq;

// ReSharper disable once CheckNamespace

namespace Geuneda.WebView
{
	/// <summary>
	/// 페이지가 부른 명령을 처리합니다. 결과는 페이지의 <c>geuneda.call</c> Promise 가 <c>{ok, code, message, value}</c> 로 받습니다
	/// (성공 값은 Newtonsoft camelCase 로 직렬화). IL2CPP 코드 제거에 대비해 값은 <c>JObject</c> 로 만들거나 형식·속성에 <c>[Preserve]</c> 를 붙입니다.
	/// </summary>
	/// <remarks>
	/// 메인 스레드에서 부릅니다. <paramref name="cancellationToken"/> 은 웹뷰가 닫히면 취소되고, 그 뒤의 결과는 버립니다. 닫혀도 끝까지 해야 하는 일(지급 요청 등)에는 넘기지 않습니다.
	/// 정상 실패는 결과 코드로 돌려줍니다. 처리기가 던진 취소·제한 시간 예외는 <c>Cancelled</c>·<c>Timeout</c> 으로 답하고, 그 밖의 예외(버그)는 Error 로그를 남기고 <c>ResultCodes.Unknown</c> 으로 답합니다(페이지의 Promise 가 끝나도록).
	/// </remarks>
	public delegate UniTask<Result<object>> WebViewCommandHandler(WebViewCommand command, CancellationToken cancellationToken);

	/// <summary>페이지가 부른 명령 하나입니다.</summary>
	public sealed class WebViewCommand
	{
		internal WebViewCommand(string name, JObject args, string pageUrl)
		{
			Name = name;
			Args = args;
			PageUrl = pageUrl;
		}

		public string Name { get; }

		/// <summary>페이지가 넘긴 인자입니다. 주지 않았으면 빈 객체입니다. 페이지가 보낸 값이니 형식을 확인하고 씁니다.</summary>
		public JObject Args { get; }

		/// <summary>명령을 보낸 페이지 주소(허용한 출처)입니다.</summary>
		public string PageUrl { get; }

		/// <summary>인자 <paramref name="name"/> 이 문자열이면 true 입니다(없거나 다른 형식이면 false).</summary>
		public bool TryGetString(string name, out string value)
		{
			var token = Args[name];

			if (token != null && token.Type == JTokenType.String)
			{
				value = (string) token;
				return true;
			}

			value = null;
			return false;
		}
	}
}

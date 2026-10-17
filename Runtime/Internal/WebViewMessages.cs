using System.Collections.Generic;
using System.IO;
using System.Text;
using Geuneda.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// ReSharper disable once CheckNamespace

namespace Geuneda.WebView
{
	internal enum WebViewMessageKind
	{
		Invalid,
		Ready,
		Bound,
		Call
	}

	/// <summary>
	/// 페이지가 보낸 글 하나입니다. <see cref="WebViewMessageKind.Call"/> 이고 <see cref="Error"/> 가 있으면 요청 ID 는 맞지만 명령 이름이나 인자가 틀린 것이라
	/// 그 ID 로 <see cref="WebViewResultCodes.InvalidArguments"/> 를 답합니다.
	/// </summary>
	internal readonly struct WebViewMessage
	{
		public WebViewMessage(WebViewMessageKind kind, string doc, string key, JToken id, string command, JObject args, string error)
		{
			Kind = kind;
			Doc = doc;
			Key = key;
			Id = id;
			Command = command;
			Args = args;
			Error = error;
		}

		public WebViewMessageKind Kind { get; }

		/// <summary>보낸 문서의 ID 입니다(JS 파일이 문서마다 무작위로 만듦). 결과를 그 문서에만 전하는 데 씁니다.</summary>
		public string Doc { get; }

		/// <summary>열기마다 Unity 가 메인 프레임에만 넘긴 키입니다(<c>bound</c>, <c>call</c>). 다른 프레임은 알 수 없습니다.</summary>
		public string Key { get; }

		public JToken Id { get; }

		public string Command { get; }

		public JObject Args { get; }

		public string Error { get; }
	}

	/// <summary>
	/// 페이지와 주고받는 글의 형식입니다(<c>Web~/geuneda-webview.js</c> 와 짝).
	/// 페이지 → Unity: <c>{"type":"ready","doc":D}</c>, <c>{"type":"bound","doc":D,"key":K}</c>,
	/// <c>{"type":"call","doc":D,"key":K,"id":"1","command":"notice.list","args":{}}</c>.
	/// Unity → 페이지(<c>window.geuneda.__receive(...)</c>, 메인 프레임에서만 실행): <c>{"type":"bind","doc":D,"key":K}</c>,
	/// <c>{"type":"result","doc":D,"id":"1","ok":true,"code":0,"value":...}</c>, <c>{"type":"event","name":"notice.changed","value":...}</c>.
	/// </summary>
	internal static class WebViewMessages
	{
		public const int MaxIdLength = 64;
		public const int MaxNameLength = 64;

		private const int MaxDepth = 64;

		/// <summary>명령·이벤트 이름 규칙: 1~64자 ASCII 영문·숫자·<c>.</c>·<c>_</c>·<c>-</c>.</summary>
		public static bool IsName(string name)
		{
			if (string.IsNullOrEmpty(name) || name.Length > MaxNameLength)
			{
				return false;
			}

			foreach (var c in name)
			{
				if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '.' || c == '_' || c == '-'))
				{
					return false;
				}
			}

			return true;
		}

		public static WebViewMessage Parse(string text)
		{
			if (string.IsNullOrEmpty(text))
			{
				return Invalid("empty message");
			}

			JToken root;

			try
			{
				using (var reader = new JsonTextReader(new StringReader(text)) { DateParseHandling = DateParseHandling.None, MaxDepth = MaxDepth })
				{
					root = JToken.ReadFrom(reader);

					if (reader.Read())
					{
						return Invalid("trailing content after the message");
					}
				}
			}
			catch (JsonException exception)
			{
				return Invalid(exception.Message);
			}

			if (!(root is JObject message))
			{
				return Invalid("message is not a JSON object");
			}

			var type = StringOf(message["type"]);
			var doc = StringOf(message["doc"]);

			if (!IsName(doc))
			{
				return Invalid($"doc must be 1-{MaxNameLength} letters, digits, '.', '_' or '-'");
			}

			var key = StringOf(message["key"]);

			if (type == "ready")
			{
				return new WebViewMessage(WebViewMessageKind.Ready, doc, null, null, null, null, null);
			}

			if (type == "bound")
			{
				return new WebViewMessage(WebViewMessageKind.Bound, doc, key, null, null, null, null);
			}

			if (type != "call")
			{
				return Invalid($"unknown message type '{type}'");
			}

			var id = message["id"];

			if (!IsId(id))
			{
				return Invalid($"call id must be a string of 1-{MaxIdLength} characters or an integer");
			}

			var command = StringOf(message["command"]);

			if (!IsName(command))
			{
				return new WebViewMessage(WebViewMessageKind.Call, doc, key, id, null, null,
					$"Command name must be 1-{MaxNameLength} letters, digits, '.', '_' or '-'");
			}

			var args = message["args"];

			if (args == null || args.Type == JTokenType.Null)
			{
				return new WebViewMessage(WebViewMessageKind.Call, doc, key, id, command, new JObject(), null);
			}

			return args is JObject argsObject
				? new WebViewMessage(WebViewMessageKind.Call, doc, key, id, command, argsObject, null)
				: new WebViewMessage(WebViewMessageKind.Call, doc, key, id, command, null, "Command args must be a JSON object");
		}

		/// <summary>
		/// 스크립트를 실행하는 순간의 메인 프레임 출처가 허용 출처인지 보는 JS 조건식입니다(<c>o==="https://a.com"||...</c>).
		/// <c>window.location</c> 은 페이지가 바꿀 수 없는 속성이고 비교는 <c>===</c> 만 써서 페이지가 고친 프로토타입에 기대지 않습니다.
		/// </summary>
		public static string OriginCondition(IReadOnlyList<string> origins)
		{
			var condition = new StringBuilder();

			for (var i = 0; i < origins.Count; i++)
			{
				if (i > 0)
				{
					condition.Append("||");
				}

				condition.Append("o===").Append(JsonConvert.ToString(origins[i]));
			}

			return condition.ToString();
		}

		public static string BindScript(string doc, string key, string originCondition)
		{
			return Script(new JObject { ["type"] = "bind", ["doc"] = doc, ["key"] = key }, originCondition);
		}

		public static string ResultScript(string doc, JToken id, Result<object> result, JsonSerializer serializer, string originCondition)
		{
			var message = new JObject { ["type"] = "result", ["doc"] = doc, ["id"] = id, ["ok"] = result.IsSuccess, ["code"] = result.Code };

			if (result.IsSuccess)
			{
				if (result.Value != null)
				{
					message["value"] = JToken.FromObject(result.Value, serializer);
				}
			}
			else if (result.Message != null)
			{
				message["message"] = result.Message;
			}

			return Script(message, originCondition);
		}

		public static string EventScript(string name, object value, JsonSerializer serializer, string originCondition)
		{
			var message = new JObject { ["type"] = "event", ["name"] = name };

			if (value != null)
			{
				message["value"] = JToken.FromObject(value, serializer);
			}

			return Script(message, originCondition);
		}

		private static string Script(JObject message, string originCondition)
		{
			// U+2028·U+2029 는 JSON 에서는 되지만 ES2019 이전 JS 문자열 리터럴에서는 줄바꿈이다(옛 웹뷰에서 문법 오류)
			var json = message.ToString(Formatting.None).Replace("\u2028", "\\u2028").Replace("\u2029", "\\u2029");

			// 실행 순간의 메인 프레임이 허용 출처일 때만 넘긴다. 마지막 로드 끝 주소만 믿으면, 이동 제한을 비껴간 이동(Android POST) 뒤 로드가 끝나기 전의
			// 허용 밖 문서가 키를 받는다(리뷰 2회차 🔴)
			return "(function(o){if(" + originCondition + "){window.geuneda&&window.geuneda.__receive(" + json + ");}})(window.location.origin);";
		}

		private static bool IsId(JToken id)
		{
			if (id == null)
			{
				return false;
			}

			if (id.Type == JTokenType.Integer)
			{
				return true;
			}

			var text = id.Type == JTokenType.String ? (string) id : null;
			return !string.IsNullOrEmpty(text) && text.Length <= MaxIdLength;
		}

		private static string StringOf(JToken token)
		{
			return token != null && token.Type == JTokenType.String ? (string) token : null;
		}

		private static WebViewMessage Invalid(string error)
		{
			return new WebViewMessage(WebViewMessageKind.Invalid, null, null, null, null, null, error);
		}
	}
}

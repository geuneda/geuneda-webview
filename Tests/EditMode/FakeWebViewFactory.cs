using System.Collections.Generic;
using Newtonsoft.Json.Linq;

// ReSharper disable once CheckNamespace

namespace Geuneda.WebView.Tests
{
	/// <summary>가짜 어댑터입니다. 만든 웹뷰를 모두 남기고, 테스트가 네이티브 알림을 직접 보냅니다.</summary>
	public sealed class FakeWebViewFactory : IWebViewFactory
	{
		public List<FakeWebViewNative> Created { get; } = new List<FakeWebViewNative>();

		public FakeWebViewNative Last => Created[Created.Count - 1];

		public IWebViewNative Create(WebViewNativeSettings settings, IWebViewNativeListener listener)
		{
			var native = new FakeWebViewNative(settings, listener);
			Created.Add(native);
			return native;
		}
	}

	/// <summary>
	/// 가짜 네이티브 웹뷰입니다. 페이지의 JS 파일처럼 준비 알림(<see cref="SendReady"/>)을 보내고, Unity 가 넘긴 키(<see cref="Binds"/>)로
	/// <c>bound</c>·<c>call</c> 을 보냅니다. 키 전달 스크립트는 <see cref="Binds"/>, 그 밖의 스크립트(결과·이벤트)는 <see cref="Scripts"/> 에 남깁니다.
	/// </summary>
	public sealed class FakeWebViewNative : IWebViewNative
	{
		public const string Doc = "doc-1";

		private const string ReceiveCall = "window.geuneda&&window.geuneda.__receive(";
		private const string ScriptSuffix = ");}})(window.location.origin);";

		public FakeWebViewNative(WebViewNativeSettings settings, IWebViewNativeListener listener)
		{
			Settings = settings;
			Listener = listener;
		}

		public WebViewNativeSettings Settings { get; }

		public IWebViewNativeListener Listener { get; }

		public List<string> LoadedUrls { get; } = new List<string>();

		public List<string> Scripts { get; } = new List<string>();

		public List<JObject> Binds { get; } = new List<JObject>();

		public List<WebViewMargins> Margins { get; } = new List<WebViewMargins>();

		/// <summary>Unity 가 마지막으로 넘긴 키입니다(없으면 null).</summary>
		public string Key => Binds.Count == 0 ? null : (string) Binds[Binds.Count - 1]["key"];

		public bool Visible { get; private set; }

		public bool Disposed { get; private set; }

		public int GoBackCount { get; private set; }

		public bool CanGoBack { get; set; }

		public void LoadUrl(string url)
		{
			LoadedUrls.Add(url);
		}

		public void EvaluateJavaScript(string script)
		{
			var message = Parse(script);

			if ((string) message["type"] == "bind")
			{
				Binds.Add(message);
				return;
			}

			Scripts.Add(script);
		}

		public void SetMargins(WebViewMargins margins)
		{
			Margins.Add(margins);
		}

		public void SetVisible(bool visible)
		{
			Visible = visible;
		}

		public void GoBack()
		{
			GoBackCount++;
		}

		public void Dispose()
		{
			Disposed = true;
		}

		public void SendReady(string doc = Doc)
		{
			Listener.OnMessage(new JObject { ["type"] = "ready", ["doc"] = doc }.ToString());
		}

		/// <summary>페이지의 JS 파일처럼 준비를 알리고, 받은 키로 <c>bound</c> 를 보냅니다.</summary>
		public void Ready()
		{
			SendReady();
			Send(new JObject { ["type"] = "bound" });
		}

		public void Call(string id, string command, JObject args = null)
		{
			var message = new JObject { ["type"] = "call", ["id"] = id, ["command"] = command };

			if (args != null)
			{
				message["args"] = args;
			}

			Send(message);
		}

		/// <summary>문서 ID 와 받은 키를 붙여 보냅니다(이미 있으면 그대로).</summary>
		public void Send(JObject message)
		{
			if (message["doc"] == null)
			{
				message["doc"] = Doc;
			}

			if (message["key"] == null && Key != null)
			{
				message["key"] = Key;
			}

			Listener.OnMessage(message.ToString());
		}

		/// <summary>Unity 가 페이지에 보낸 결과·이벤트(<c>__receive</c> 인자)입니다.</summary>
		public List<JObject> Received()
		{
			var messages = new List<JObject>();

			foreach (var script in Scripts)
			{
				messages.Add(Parse(script));
			}

			return messages;
		}

		public JObject Result(string id)
		{
			foreach (var message in Received())
			{
				if ((string) message["type"] == "result" && (string) message["id"] == id)
				{
					return message;
				}
			}

			return null;
		}

		/// <summary>스크립트(<c>(function(o){if(출처 조건){window.geuneda&&window.geuneda.__receive(JSON);}})(window.location.origin);</c>)의 JSON 입니다.</summary>
		private static JObject Parse(string script)
		{
			var start = script.IndexOf(ReceiveCall, System.StringComparison.Ordinal) + ReceiveCall.Length;
			return JObject.Parse(script.Substring(start, script.Length - ScriptSuffix.Length - start));
		}
	}
}

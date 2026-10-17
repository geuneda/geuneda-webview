using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Core;
using Geuneda.Notice;
using Newtonsoft.Json.Linq;

// ReSharper disable once CheckNamespace

namespace Geuneda.WebView.Notice
{
	/// <summary>
	/// 공지 페이지가 부를 명령입니다. 목록은 <see cref="INoticeService"/> 가 거른 것(기간·플랫폼·언어, 읽음 여부)이라 게임 UI 팝업과 같은 판정·기록을 씁니다.
	/// </summary>
	/// <remarks>
	/// <list type="bullet">
	/// <item><c>notice.list</c> <c>{category?}</c> → <c>{language, unreadCount, notices: [{id, category, priority, startsAt, endsAt, popup, action, title, body, linkUrl, linkLabel, read}]}</c>
	/// (정렬됨, 시각은 UTC ISO 8601, 없는 값은 뺌)</item>
	/// <item><c>notice.read</c> <c>{id}</c> → 읽음 기록. 처음이면 <c>notice.changed</c> 가 옵니다</item>
	/// <item><c>notice.refresh</c> → 문서를 다시 받음. 실패는 공지 패키지의 결과 코드 그대로</item>
	/// <item>이벤트 <c>notice.changed</c>(값 없음): <see cref="INoticeService.Changed"/> 마다. 페이지가 <c>notice.list</c> 를 다시 부릅니다</item>
	/// </list>
	/// </remarks>
	public static class NoticeWebViewCommands
	{
		public const string List = "notice.list";
		public const string Read = "notice.read";
		public const string Refresh = "notice.refresh";
		public const string ChangedEvent = "notice.changed";

		/// <summary>명령을 더하고 공지 변경을 이벤트로 보냅니다. 돌려준 객체를 <c>Dispose</c> 하면 명령과 구독을 뺍니다.</summary>
		/// <param name="utcNow">기간 판정 시각(UTC). null 이면 <c>DateTime.UtcNow</c>. 기기 시계를 믿지 않으면 서버 시각을 넘깁니다</param>
		/// <exception cref="ArgumentNullException">웹뷰나 공지 서비스가 null 인 경우</exception>
		/// <exception cref="InvalidOperationException">같은 이름의 명령이 이미 있는 경우</exception>
		public static IDisposable Add(IWebViewService webView, INoticeService notices, Func<DateTime> utcNow = null)
		{
			if (webView == null)
			{
				throw new ArgumentNullException(nameof(webView));
			}

			if (notices == null)
			{
				throw new ArgumentNullException(nameof(notices));
			}

			var clock = utcNow ?? (() => DateTime.UtcNow);
			var registrations = new List<IDisposable>();

			try
			{
				registrations.Add(webView.AddCommand(List, (command, token) => UniTask.FromResult(ListNotices(notices, clock(), command))));
				registrations.Add(webView.AddCommand(Read, (command, token) => UniTask.FromResult(MarkRead(notices, command))));
				registrations.Add(webView.AddCommand(Refresh, (command, token) => RefreshAsync(notices, token)));
			}
			catch
			{
				Dispose(registrations);
				throw;
			}

			return new Registration(webView, notices, registrations);
		}

		private static Result<object> ListNotices(INoticeService notices, DateTime utcNow, WebViewCommand command)
		{
			string category = null;

			if (command.Args["category"] != null && !command.TryGetString("category", out category))
			{
				return Result<object>.Fail(WebViewResultCodes.InvalidArguments, "notice.list category must be a string");
			}

			var items = new JArray();

			foreach (var notice in notices.GetNotices(utcNow, category))
			{
				items.Add(ToJson(notice, notices.IsRead(notice.Id)));
			}

			return Result<object>.Ok(new JObject
			{
				["language"] = notices.Language,
				["unreadCount"] = notices.GetUnreadCount(utcNow, category),
				["notices"] = items
			});
		}

		// 리플렉션 직렬화 대신 JObject 로 만든다(IL2CPP 코드 제거에 속성 getter 가 지워지지 않게)
		private static JObject ToJson(Geuneda.Notice.Notice notice, bool read)
		{
			var json = new JObject
			{
				["id"] = notice.Id,
				["category"] = notice.Category,
				["priority"] = notice.Priority,
				["popup"] = notice.Popup,
				["title"] = notice.Title,
				["read"] = read
			};

			AddIfPresent(json, "startsAt", notice.StartsAt);
			AddIfPresent(json, "endsAt", notice.EndsAt);
			AddIfPresent(json, "action", notice.Action);
			AddIfPresent(json, "body", notice.Body);
			AddIfPresent(json, "linkUrl", notice.LinkUrl);
			AddIfPresent(json, "linkLabel", notice.LinkLabel);
			return json;
		}

		private static void AddIfPresent(JObject json, string name, DateTime? value)
		{
			if (value.HasValue)
			{
				json[name] = value.Value;
			}
		}

		private static void AddIfPresent(JObject json, string name, string value)
		{
			if (value != null)
			{
				json[name] = value;
			}
		}

		private static Result<object> MarkRead(INoticeService notices, WebViewCommand command)
		{
			if (!command.TryGetString("id", out var id))
			{
				return Result<object>.Fail(WebViewResultCodes.InvalidArguments, "notice.read needs a string id");
			}

			try
			{
				notices.MarkRead(id);
			}
			catch (ArgumentException exception)
			{
				// 페이지가 보낸 값이라 오용 예외가 아니라 인자 오류로 답한다
				return Result<object>.Fail(WebViewResultCodes.InvalidArguments, exception.Message);
			}

			return Result<object>.Ok(null);
		}

		private static async UniTask<Result<object>> RefreshAsync(INoticeService notices, CancellationToken token)
		{
			var result = await notices.RefreshAsync(token);
			return result.IsSuccess ? Result<object>.Ok(null) : Result<object>.Fail(result.Code, result.Message);
		}

		private static void Dispose(List<IDisposable> registrations)
		{
			foreach (var registration in registrations)
			{
				registration.Dispose();
			}
		}

		private sealed class Registration : IDisposable
		{
			private readonly IWebViewService _webView;
			private readonly INoticeService _notices;
			private readonly List<IDisposable> _registrations;
			private bool _disposed;

			public Registration(IWebViewService webView, INoticeService notices, List<IDisposable> registrations)
			{
				_webView = webView;
				_notices = notices;
				_registrations = registrations;
				_notices.Changed += OnChanged;
			}

			public void Dispose()
			{
				if (_disposed)
				{
					return;
				}

				_disposed = true;
				_notices.Changed -= OnChanged;
				NoticeWebViewCommands.Dispose(_registrations);
			}

			private void OnChanged()
			{
				_webView.Emit(ChangedEvent);
			}
		}
	}
}

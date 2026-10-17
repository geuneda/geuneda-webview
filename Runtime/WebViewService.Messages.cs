using System;
using Cysharp.Threading.Tasks;
using Geuneda.Core;
using Newtonsoft.Json;

// ReSharper disable once CheckNamespace

namespace Geuneda.WebView
{
	/// <summary>페이지가 보낸 글의 처리: 키 확인, 명령 실행, 결과 전달.</summary>
	public sealed partial class WebViewService
	{
		private void OnMessage(Session session, string text)
		{
			if (session != _session)
			{
				return;
			}

			if (!IsAllowedPage(session.PageUrl))
			{
				Logger.Warning($"Ignored a web view message from a page outside the allowed origins: {session.PageUrl}");
				return;
			}

			var message = WebViewMessages.Parse(text);

			if (message.Kind == WebViewMessageKind.Invalid)
			{
				Logger.Warning($"Ignored an invalid web view message: {message.Error}");
				return;
			}

			if (message.Kind == WebViewMessageKind.Ready)
			{
				// 키는 EvaluateJS 로 보내므로 메인 프레임의 문서만 받는다. 다른 출처의 iframe 이 보낸 준비 알림이어도 키는 그 iframe 에 가지 않는다
				session.ReadySeen = true;
				Deliver(session, WebViewMessages.BindScript(message.Doc, session.Key, _originCondition));
				return;
			}

			// 키가 없으면 메인 프레임 문서가 보낸 글이 아니다(iframe, 키를 받기 전의 글)
			if (!string.Equals(message.Key, session.Key, StringComparison.Ordinal))
			{
				Logger.Warning("Ignored a web view message without the session key (not from the main frame page)");
				return;
			}

			if (message.Kind == WebViewMessageKind.Bound)
			{
				if (!session.Shown)
				{
					FinishOpening(session, Result.Ok());
				}

				return;
			}

			if (message.Error != null)
			{
				Deliver(session, ResultScript(message, Result<object>.Fail(WebViewResultCodes.InvalidArguments, message.Error)));
				return;
			}

			RunCommandAsync(session, message).Forget();
		}

		private async UniTaskVoid RunCommandAsync(Session session, WebViewMessage message)
		{
			var command = new WebViewCommand(message.Command, message.Args, session.PageUrl);
			Result<object> result;

			switch (message.Command)
			{
				case CloseCommand:
					Close();
					return;
				case OpenUrlCommand:
					result = OpenUrl(command);
					break;
				default:
					if (!_commands.TryGetValue(message.Command, out var registration))
					{
						result = Result<object>.Fail(WebViewResultCodes.UnknownCommand, $"Unknown command '{message.Command}'");
						break;
					}

					try
					{
						result = await registration.Handler(command, session.Cancellation.Token);
					}
					catch (OperationCanceledException) when (session.Cancellation.IsCancellationRequested)
					{
						return;
					}
					catch (Exception exception) when (exception is OperationCanceledException || exception is TimeoutException)
					{
						// 처리기 자신의 취소·제한 시간은 정상 실패다(Error 로그 없음)
						result = Result<object>.Fail(ResultCodes.FromException(exception), exception.Message);
					}
					catch (Exception exception)
					{
						// 페이지 경계: 처리기 버그로 페이지의 Promise 가 끝나지 않는 일이 없게 답은 보내고, 버그는 예외와 함께 Error 로 남긴다(ads 의 어댑터 Show 예외와 같음)
						Logger.Error(exception, $"Web view command '{message.Command}' failed");
						result = Result<object>.Fail(ResultCodes.Unknown, $"Command '{message.Command}' failed");
					}

					break;
			}

			Deliver(session, ResultScript(message, result));
		}

		private Result<object> OpenUrl(WebViewCommand command)
		{
			if (!command.TryGetString("url", out var url) || !WebViewOrigins.IsHttpUrl(url))
			{
				return Result<object>.Fail(WebViewResultCodes.InvalidArguments, "openUrl needs an absolute http or https url");
			}

			OpenExternal(url);
			return Result<object>.Ok(null);
		}

		private string ResultScript(WebViewMessage message, Result<object> result)
		{
			try
			{
				return WebViewMessages.ResultScript(message.Doc, message.Id, result, _serializer, _originCondition);
			}
			catch (JsonException exception)
			{
				Logger.Error(exception, $"Could not serialize the result of web view command '{message.Command}'");
				return WebViewMessages.ResultScript(message.Doc, message.Id, Result<object>.Fail(ResultCodes.Unknown, "Result could not be serialized"),
					_serializer, _originCondition);
			}
		}

		private void Deliver(Session session, string script)
		{
			// 닫혔거나(답이 늦음) 허용 밖 페이지로 바뀌었으면 보내지 않는다
			if (session == _session && IsAllowedPage(session.PageUrl))
			{
				session.Native.EvaluateJavaScript(script);
			}
		}

		private sealed class CommandRegistration : IDisposable
		{
			private readonly WebViewService _owner;
			private readonly string _name;

			public CommandRegistration(WebViewService owner, string name, WebViewCommandHandler handler)
			{
				_owner = owner;
				_name = name;
				Handler = handler;
			}

			public WebViewCommandHandler Handler { get; }

			public void Dispose()
			{
				// 같은 이름을 다시 더한 뒤 옛 등록을 버려도 새 등록은 남긴다(같은 처리기를 넘겼어도 등록마다 다름)
				if (_owner._commands.TryGetValue(_name, out var registration) && registration == this)
				{
					_owner._commands.Remove(_name);
				}
			}
		}
	}
}

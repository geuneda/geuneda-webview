using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

// ReSharper disable once CheckNamespace

namespace Geuneda.WebView
{
	/// <summary>출처 규칙입니다. 호스트는 ASCII(<c>a-z</c>, <c>0-9</c>, <c>.</c>, <c>-</c>)만 받고 소문자 바꾸기도 ASCII 로만 합니다.</summary>
	internal static class WebViewOrigins
	{
		/// <summary><c>https://host[:port]</c> 모양(경로 <c>/</c> 까지만)이면 비교용 출처(소문자, 기본 포트 생략), 아니면 null 입니다.</summary>
		public static string Normalize(string origin)
		{
			if (origin == null || !Uri.TryCreate(origin, UriKind.Absolute, out var uri) || !IsHttp(uri))
			{
				return null;
			}

			if (uri.UserInfo.Length > 0 || uri.AbsolutePath != "/" || uri.Query.Length > 0 || uri.Fragment.Length > 0)
			{
				return null;
			}

			return Compose(uri);
		}

		/// <summary>http·https 절대 주소의 출처입니다. 아니면(호스트가 ASCII 가 아니어도) null 입니다.</summary>
		public static string OriginOf(string url)
		{
			return url != null && Uri.TryCreate(url, UriKind.Absolute, out var uri) && IsHttp(uri) ? Compose(uri) : null;
		}

		public static bool IsHttpUrl(string url)
		{
			return url != null && Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
				(uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) && uri.Host.Length > 0;
		}

		/// <summary>
		/// 허용한 출처 밖의 http·https 주소에 맞는 정규식입니다. 허용한 출처 바로 뒤에는 끝이나 <c>/</c>·<c>?</c>·<c>#</c> 이 와야 해서
		/// <c>https://a.com.evil.com</c>, <c>https://a.com:8443</c>, <c>https://a.com@evil.com</c> 은 막습니다.
		/// </summary>
		public static string BuildDenyPattern(IReadOnlyList<string> origins)
		{
			var pattern = new StringBuilder("(?i)^(?!(?:");

			for (var i = 0; i < origins.Count; i++)
			{
				if (i > 0)
				{
					pattern.Append('|');
				}

				foreach (var c in origins[i])
				{
					// Java·ICU·.NET 모두 영문·숫자가 아닌 글자 앞의 역슬래시는 그 글자 자체다(영문 앞은 특수 구문이라 붙이지 않음)
					if (!IsAsciiLetterOrDigit(c))
					{
						pattern.Append('\\');
					}

					pattern.Append(c);
				}
			}

			return pattern.Append(")(?:[/?#]|$))https?://").ToString();
		}

		private static bool IsHttp(Uri uri)
		{
			return (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) && IsAsciiHost(uri.Host);
		}

		private static string Compose(Uri uri)
		{
			var host = ToLowerAscii(uri.Host);
			return uri.IsDefaultPort ? uri.Scheme + "://" + host : uri.Scheme + "://" + host + ":" + uri.Port.ToString(CultureInfo.InvariantCulture);
		}

		private static bool IsAsciiHost(string host)
		{
			if (string.IsNullOrEmpty(host))
			{
				return false;
			}

			foreach (var c in host)
			{
				if (!IsAsciiLetterOrDigit(c) && c != '.' && c != '-')
				{
					return false;
				}
			}

			return true;
		}

		private static bool IsAsciiLetterOrDigit(char c)
		{
			return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');
		}

		private static string ToLowerAscii(string value)
		{
			var chars = value.ToCharArray();

			for (var i = 0; i < chars.Length; i++)
			{
				if (chars[i] >= 'A' && chars[i] <= 'Z')
				{
					chars[i] = (char) (chars[i] + 32);
				}
			}

			return new string(chars);
		}
	}
}

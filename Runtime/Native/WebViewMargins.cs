using System;

// ReSharper disable once CheckNamespace

namespace Geuneda.WebView
{
	/// <summary>화면 가장자리에서 웹뷰까지의 거리입니다(Unity 화면 픽셀). 0 이상입니다.</summary>
	public readonly struct WebViewMargins : IEquatable<WebViewMargins>
	{
		public WebViewMargins(int left, int top, int right, int bottom)
		{
			Left = left;
			Top = top;
			Right = right;
			Bottom = bottom;
		}

		public int Left { get; }

		public int Top { get; }

		public int Right { get; }

		public int Bottom { get; }

		public bool Equals(WebViewMargins other)
		{
			return Left == other.Left && Top == other.Top && Right == other.Right && Bottom == other.Bottom;
		}

		public override bool Equals(object obj)
		{
			return obj is WebViewMargins other && Equals(other);
		}

		public override int GetHashCode()
		{
			unchecked
			{
				return ((Left * 397 ^ Top) * 397 ^ Right) * 397 ^ Bottom;
			}
		}

		public static bool operator ==(WebViewMargins left, WebViewMargins right)
		{
			return left.Equals(right);
		}

		public static bool operator !=(WebViewMargins left, WebViewMargins right)
		{
			return !left.Equals(right);
		}

		public override string ToString()
		{
			return $"(left {Left}, top {Top}, right {Right}, bottom {Bottom})";
		}
	}
}

using UnityEngine;

// ReSharper disable once CheckNamespace

namespace Geuneda.WebView
{
	/// <summary>영역(화면 픽셀 사각형)을 웹뷰 여백으로 바꿉니다.</summary>
	internal static class WebViewLayout
	{
		/// <summary>화면 좌표(왼쪽 아래 원점) 사각형을 가장자리별 거리로 바꿉니다. 화면 밖으로 나간 부분은 0 으로 자릅니다.</summary>
		public static WebViewMargins Margins(Rect screenRect, int screenWidth, int screenHeight)
		{
			var left = Mathf.RoundToInt(screenRect.xMin);
			var bottom = Mathf.RoundToInt(screenRect.yMin);
			var right = screenWidth - Mathf.RoundToInt(screenRect.xMax);
			var top = screenHeight - Mathf.RoundToInt(screenRect.yMax);
			return new WebViewMargins(Mathf.Max(0, left), Mathf.Max(0, top), Mathf.Max(0, right), Mathf.Max(0, bottom));
		}

		/// <summary>영역을 그리는 카메라입니다. 캔버스 밖이거나 Overlay 캔버스면 null(월드 좌표가 곧 화면 좌표)입니다.</summary>
		public static Camera CameraOf(RectTransform frame)
		{
			var canvas = frame.GetComponentInParent<Canvas>();

			if (canvas == null)
			{
				return null;
			}

			canvas = canvas.rootCanvas;
			return canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
		}

		/// <summary>영역의 네 모서리를 감싸는 화면 좌표 사각형입니다. <paramref name="corners"/> 는 길이 4 의 작업용 배열입니다.</summary>
		public static Rect ScreenRect(RectTransform frame, Camera camera, Vector3[] corners)
		{
			frame.GetWorldCorners(corners);
			var min = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
			var max = min;

			for (var i = 1; i < corners.Length; i++)
			{
				var point = RectTransformUtility.WorldToScreenPoint(camera, corners[i]);
				min = Vector2.Min(min, point);
				max = Vector2.Max(max, point);
			}

			return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
		}
	}
}

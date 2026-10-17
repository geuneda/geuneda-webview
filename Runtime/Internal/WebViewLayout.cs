using UnityEngine;

// ReSharper disable once CheckNamespace

namespace Geuneda.WebView
{
	/// <summary>영역(화면 픽셀 사각형)을 웹뷰 여백으로 바꿉니다.</summary>
	internal static class WebViewLayout
	{
		private static readonly Rect FullViewport = new Rect(0f, 0f, 1f, 1f);

		/// <summary>화면 좌표(왼쪽 아래 원점) 사각형을 가장자리별 거리로 바꿉니다. 화면 밖으로 나간 부분은 0 으로 자릅니다.</summary>
		public static WebViewMargins Margins(Rect screenRect, int screenWidth, int screenHeight)
		{
			var left = Mathf.RoundToInt(screenRect.xMin);
			var bottom = Mathf.RoundToInt(screenRect.yMin);
			var right = screenWidth - Mathf.RoundToInt(screenRect.xMax);
			var top = screenHeight - Mathf.RoundToInt(screenRect.yMax);
			return new WebViewMargins(Mathf.Max(0, left), Mathf.Max(0, top), Mathf.Max(0, right), Mathf.Max(0, bottom));
		}

		/// <summary>영역의 루트 캔버스입니다. 캔버스 밖이면 null 입니다.</summary>
		public static Canvas RootCanvasOf(RectTransform frame)
		{
			var canvas = frame.GetComponentInParent<Canvas>();
			return canvas == null ? null : canvas.rootCanvas;
		}

		/// <summary>
		/// 영역의 네 모서리를 감싸는 화면 좌표(왼쪽 아래 원점) 사각형입니다. <paramref name="corners"/> 는 길이 4 의 작업용 배열입니다.
		/// </summary>
		/// <remarks>
		/// <para>
		/// 화면 공간 캔버스(Overlay, Camera)는 모서리를 루트 캔버스 안의 비율로 바꿔 <see cref="CanvasToScreen"/> 으로 화면 크기에 맞춥니다.
		/// 모서리와 캔버스 크기는 같은 배치에서 나오므로, 화면 크기가 캔버스보다 먼저 바뀐 프레임에도 비율이 맞습니다
		/// (Android 는 <c>Screen.SetResolution</c> 을 부른 프레임의 <c>LastPostLateUpdate</c> 에 <c>Screen</c> 은 새 크기, 캔버스와 모서리는 옛 크기).
		/// Camera 캔버스는 캔버스 평면 밖의 모서리(z 이동, 3D 회전)를 카메라 시선으로 평면에 옮겨 원근을 반영하고(<see cref="ToCanvasPlane"/>),
		/// Overlay 는 0.2.0 과 같이 z 를 버립니다(정사영).
		/// </para>
		/// <para>캔버스 밖과 WorldSpace 캔버스는 월드 모서리를 카메라로 투영합니다(카메라가 없으면 월드 좌표가 곧 화면 좌표). 크기가 0 인 캔버스(배치 전)도 이 경로입니다.</para>
		/// </remarks>
		public static Rect ScreenRect(RectTransform frame, Canvas root, Vector2Int screenSize, Vector3[] corners)
		{
			frame.GetWorldCorners(corners);

			if (root != null && root.renderMode != RenderMode.WorldSpace)
			{
				var rootTransform = (RectTransform) root.transform;
				var canvasRect = rootTransform.rect;

				if (canvasRect.width > 0f && canvasRect.height > 0f)
				{
					// Camera 캔버스는 카메라 뷰포트를 채운다. 카메라가 없으면 Overlay 처럼 그린다
					var camera = root.renderMode == RenderMode.ScreenSpaceCamera ? root.worldCamera : null;

					if (camera != null)
					{
						// 캔버스 평면 밖의 모서리(z 이동, 3D 회전)는 카메라 시선을 따라 평면에 옮겨 원근을 반영한다.
						// 카메라 위치·방향은 해상도와 상관없으므로 화면 크기만 먼저 바뀐 프레임에도 비율이 맞는다
						var cameraTransform = camera.transform;
						var planePoint = rootTransform.position;
						var planeNormal = rootTransform.forward;

						for (var i = 0; i < corners.Length; i++)
						{
							corners[i] = ToCanvasPlane(corners[i], cameraTransform.position, cameraTransform.forward, camera.orthographic, planePoint, planeNormal);
						}
					}

					// 루트 로컬 z 는 버린다(Overlay 는 월드 z 를 버리는 정사영과 같음)
					Vector2 localMin = rootTransform.InverseTransformPoint(corners[0]);
					var localMax = localMin;

					for (var i = 1; i < corners.Length; i++)
					{
						Vector2 point = rootTransform.InverseTransformPoint(corners[i]);
						localMin = Vector2.Min(localMin, point);
						localMax = Vector2.Max(localMax, point);
					}

					var viewport = camera != null ? camera.rect : FullViewport;
					return CanvasToScreen(Rect.MinMaxRect(localMin.x, localMin.y, localMax.x, localMax.y), canvasRect, viewport, screenSize);
				}
			}

			var projection = root != null && root.renderMode != RenderMode.ScreenSpaceOverlay ? root.worldCamera : null;
			var min = RectTransformUtility.WorldToScreenPoint(projection, corners[0]);
			var max = min;

			for (var i = 1; i < corners.Length; i++)
			{
				var point = RectTransformUtility.WorldToScreenPoint(projection, corners[i]);
				min = Vector2.Min(min, point);
				max = Vector2.Max(max, point);
			}

			return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
		}

		/// <summary>
		/// 점을 카메라 시선을 따라 캔버스 평면에 옮깁니다. 원근 카메라는 카메라 위치에서 점을 지나는 직선, 직교 카메라는 카메라 방향의 평행선과 평면이 만나는 점입니다.
		/// 시선이 평면과 나란하거나 원근 카메라의 뒤에 있는 점은 그대로 돌려줍니다(보이지 않는 점).
		/// </summary>
		public static Vector3 ToCanvasPlane(Vector3 point, Vector3 cameraPosition, Vector3 cameraForward, bool orthographic, Vector3 planePoint, Vector3 planeNormal)
		{
			var direction = orthographic ? cameraForward : point - cameraPosition;
			var origin = orthographic ? point : cameraPosition;
			var denominator = Vector3.Dot(direction, planeNormal);

			if (Mathf.Abs(denominator) < 1e-6f)
			{
				return point;
			}

			var distance = Vector3.Dot(planePoint - origin, planeNormal) / denominator;
			return !orthographic && distance <= 0f ? point : origin + direction * distance;
		}

		/// <summary>
		/// 캔버스 로컬 사각형을 화면 좌표로 바꿉니다. 캔버스 안의 비율(0~1)을 뷰포트(정규화 사각형, 0~1 로 자름)에 놓고 화면 크기를 곱합니다.
		/// <paramref name="canvas"/> 는 크기가 0 보다 커야 합니다.
		/// </summary>
		public static Rect CanvasToScreen(Rect local, Rect canvas, Rect viewport, Vector2Int screenSize)
		{
			var viewXMin = Mathf.Clamp01(viewport.xMin);
			var viewYMin = Mathf.Clamp01(viewport.yMin);
			var viewWidth = Mathf.Clamp01(viewport.xMax) - viewXMin;
			var viewHeight = Mathf.Clamp01(viewport.yMax) - viewYMin;
			var xMin = (viewXMin + (local.xMin - canvas.xMin) / canvas.width * viewWidth) * screenSize.x;
			var xMax = (viewXMin + (local.xMax - canvas.xMin) / canvas.width * viewWidth) * screenSize.x;
			var yMin = (viewYMin + (local.yMin - canvas.yMin) / canvas.height * viewHeight) * screenSize.y;
			var yMax = (viewYMin + (local.yMax - canvas.yMin) / canvas.height * viewHeight) * screenSize.y;
			return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
		}
	}
}

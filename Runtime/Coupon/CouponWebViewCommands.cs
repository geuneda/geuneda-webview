using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Coupon;
using Geuneda.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

// ReSharper disable once CheckNamespace

namespace Geuneda.WebView.Coupon
{
	/// <summary>
	/// 쿠폰 페이지가 부를 명령입니다. 사용 요청은 Unity 의 <see cref="ICouponService"/> 가 인증해 보내므로 페이지는 계정 토큰을 받지 않습니다.
	/// </summary>
	/// <remarks>
	/// <c>coupon.redeem</c> <c>{code}</c> → <c>{campaignId, alreadyRedeemed, redeemedAt, grant}</c>(grant 는 <typeparamref name="TGrant"/> 를 camelCase 로).
	/// 실패는 쿠폰 패키지의 결과 코드 그대로입니다(<c>CouponResultCodes</c>, 연결 실패 등은 core 공용 코드). 문구는 페이지가 코드로 고릅니다.
	/// </remarks>
	public static class CouponWebViewCommands
	{
		public const string Redeem = "coupon.redeem";

		private static readonly ModuleLogger Logger = new ModuleLogger("Geuneda.WebView.Coupon");

		/// <summary>명령을 더합니다. 돌려준 객체를 <c>Dispose</c> 하면 뺍니다.</summary>
		/// <param name="redeemed">
		/// 사용에 성공하면(이미 쓴 캠페인 포함, <c>AlreadyRedeemed</c> 로 구분) 부릅니다. 보상 반영(서버 상태 다시 받기 등)에 씁니다.
		/// 사용 요청은 웹뷰가 닫혀도 끝까지 하므로(서버가 지급했을 수 있음) 닫은 뒤에도 옵니다. 예외는 Error 로그만 남깁니다
		/// </param>
		/// <typeparam name="TGrant">게임 서버 지급 결과의 형식(<see cref="ICouponService.RedeemAsync{TGrant}"/> 와 같음)</typeparam>
		/// <exception cref="ArgumentNullException">웹뷰나 쿠폰 서비스가 null 인 경우</exception>
		/// <exception cref="InvalidOperationException">같은 이름의 명령이 이미 있는 경우</exception>
		public static IDisposable Add<TGrant>(IWebViewService webView, ICouponService coupons, Action<CouponRedemption<TGrant>> redeemed = null)
		{
			if (webView == null)
			{
				throw new ArgumentNullException(nameof(webView));
			}

			if (coupons == null)
			{
				throw new ArgumentNullException(nameof(coupons));
			}

			// 지급 결과를 받을 때(쿠폰 패키지)와 같은 camelCase. 설정 객체는 바꿀 수 있으므로 등록마다 따로 둔다
			var serializer = JsonSerializer.Create(new JsonSerializerSettings
			{
				ContractResolver = new CamelCasePropertyNamesContractResolver(),
				NullValueHandling = NullValueHandling.Ignore
			});

			return webView.AddCommand(Redeem, (command, token) => RedeemAsync(coupons, redeemed, serializer, command));
		}

		private static async UniTask<Result<object>> RedeemAsync<TGrant>(ICouponService coupons, Action<CouponRedemption<TGrant>> redeemed,
			JsonSerializer serializer, WebViewCommand command)
		{
			if (!command.TryGetString("code", out var code))
			{
				return Result<object>.Fail(WebViewResultCodes.InvalidArguments, "coupon.redeem needs a string code");
			}

			// 닫기 토큰을 넘기지 않는다: 닫아도 요청은 끝까지 가서 지급 여부를 알아야 보상을 맞춘다(요청 제한 시간이 끝을 보장)
			var result = await coupons.RedeemAsync<TGrant>(code, CancellationToken.None);

			if (!result.TryGetValue(out var redemption))
			{
				return Result<object>.Fail(result.Code, result.Message);
			}

			if (redeemed != null)
			{
				try
				{
					redeemed(redemption);
				}
				catch (Exception exception)
				{
					Logger.Error(exception, "Coupon redeemed callback failed");
				}
			}

			// 리플렉션 직렬화 대신 JObject 로 만든다(IL2CPP 코드 제거). 지급 결과 형식의 보존은 게임 몫이다(쿠폰 패키지에서 받을 때와 같음)
			var json = new JObject { ["campaignId"] = redemption.CampaignId, ["alreadyRedeemed"] = redemption.AlreadyRedeemed };

			if (redemption.RedeemedAt.HasValue)
			{
				json["redeemedAt"] = redemption.RedeemedAt.Value;
			}

			if (redemption.Grant != null)
			{
				json["grant"] = JToken.FromObject(redemption.Grant, serializer);
			}

			return Result<object>.Ok(json);
		}
	}
}

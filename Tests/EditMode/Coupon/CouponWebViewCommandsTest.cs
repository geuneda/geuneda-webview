using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Core;
using Geuneda.Coupon;
using Geuneda.WebView.Tests;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

// ReSharper disable once CheckNamespace

namespace Geuneda.WebView.Coupon.Tests
{
	[TestFixture]
	public class CouponWebViewCommandsTest
	{
		private FakeWebViewFactory _factory;
		private WebViewService _webView;
		private FakeCouponService _coupons;
		private List<CouponRedemption<TestGrant>> _redeemed;
		private IDisposable _commands;
		private FakeWebViewNative _native;

		public sealed class TestGrant
		{
			public int Gems { get; set; }
			public string ItemId { get; set; }
		}

		[SetUp]
		public void SetUp()
		{
			_factory = new FakeWebViewFactory();
			_webView = new WebViewService(new WebViewServiceOptions { Factory = _factory, AllowedOrigins = new[] { "https://coupon.example.com" } });
			_coupons = new FakeCouponService();
			_redeemed = new List<CouponRedemption<TestGrant>>();
			_commands = CouponWebViewCommands.Add<TestGrant>(_webView, _coupons, redemption => _redeemed.Add(redemption));

			var open = _webView.OpenAsync("https://coupon.example.com/ko/coupon.html");
			_factory.Last.Ready();
			Assert.IsTrue(open.GetAwaiter().GetResult().IsSuccess);
			_native = _factory.Last;
		}

		[TearDown]
		public void TearDown()
		{
			_commands.Dispose();
			_webView.Dispose();
		}

		[Test]
		public void Redeem_ReturnsTheGrantAndCallsBack()
		{
			var redemption = new CouponRedemption<TestGrant>("welcome", new TestGrant { Gems = 100, ItemId = "sword" },
				new DateTimeOffset(2026, 10, 17, 0, 0, 0, TimeSpan.Zero), false);
			_coupons.Next = UniTask.FromResult(Result<CouponRedemption<TestGrant>>.Ok(redemption));

			_native.Call("1", CouponWebViewCommands.Redeem, new JObject { ["code"] = "welcome-2026" });

			Assert.AreEqual("welcome-2026", _coupons.Codes[0], "the coupon service normalizes the code");
			Assert.AreEqual(CancellationToken.None, _coupons.Tokens[0]);
			var result = _native.Result("1");
			Assert.AreEqual(true, (bool) result["ok"], result.ToString());
			Assert.AreEqual("welcome", (string) result["value"]["campaignId"]);
			Assert.AreEqual(false, (bool) result["value"]["alreadyRedeemed"]);
			Assert.AreEqual(100, (int) result["value"]["grant"]["gems"]);
			Assert.AreEqual("sword", (string) result["value"]["grant"]["itemId"]);
			StringAssert.Contains("\"redeemedAt\":\"2026-10-17T00:00:00+00:00\"", _native.Scripts[0]);
			CollectionAssert.AreEqual(new[] { redemption }, _redeemed);
		}

		[Test]
		public void Redeem_AlreadyRedeemedWithoutGrant_IsStillASuccess()
		{
			var redemption = new CouponRedemption<TestGrant>("welcome", null, null, true);
			_coupons.Next = UniTask.FromResult(Result<CouponRedemption<TestGrant>>.Ok(redemption));

			_native.Call("1", CouponWebViewCommands.Redeem, new JObject { ["code"] = "WELCOME2026" });

			var value = _native.Result("1")["value"];
			Assert.AreEqual(true, (bool) value["alreadyRedeemed"]);
			Assert.IsNull(value["grant"]);
			Assert.IsNull(value["redeemedAt"]);
			Assert.AreEqual(1, _redeemed.Count);
		}

		[Test]
		public void Redeem_Failure_PassesTheCouponCodeThrough()
		{
			_coupons.Next = UniTask.FromResult(Result<CouponRedemption<TestGrant>>.Fail(CouponResultCodes.Expired, "Campaign ended"));

			_native.Call("1", CouponWebViewCommands.Redeem, new JObject { ["code"] = "OLD2020" });

			var result = _native.Result("1");
			Assert.AreEqual(CouponResultCodes.Expired, (int) result["code"]);
			Assert.AreEqual("Campaign ended", (string) result["message"]);
			Assert.IsEmpty(_redeemed);
		}

		[Test]
		public void Redeem_WithoutAStringCode_IsInvalidArguments()
		{
			_native.Call("1", CouponWebViewCommands.Redeem);
			_native.Call("2", CouponWebViewCommands.Redeem, new JObject { ["code"] = 1234 });

			Assert.AreEqual(WebViewResultCodes.InvalidArguments, (int) _native.Result("1")["code"]);
			Assert.AreEqual(WebViewResultCodes.InvalidArguments, (int) _native.Result("2")["code"]);
			Assert.IsEmpty(_coupons.Codes);
		}

		[Test]
		public void Redeem_ClosedWhileRequesting_StillFinishesAndCallsBack()
		{
			var source = new UniTaskCompletionSource<Result<CouponRedemption<TestGrant>>>();
			_coupons.Next = source.Task;
			_native.Call("1", CouponWebViewCommands.Redeem, new JObject { ["code"] = "WELCOME2026" });

			_webView.Close();
			source.TrySetResult(Result<CouponRedemption<TestGrant>>.Ok(new CouponRedemption<TestGrant>("welcome", null, null, false)));

			Assert.AreEqual(1, _redeemed.Count, "the server may have granted the reward");
			Assert.IsEmpty(_native.Scripts, "the closed page gets no answer");
		}

		[Test]
		public void Redeem_CallbackException_IsLoggedAndThePageStillGetsTheResult()
		{
			_commands.Dispose();
			_commands = CouponWebViewCommands.Add<TestGrant>(_webView, _coupons, redemption => throw new InvalidOperationException("game bug"));
			_coupons.Next = UniTask.FromResult(Result<CouponRedemption<TestGrant>>.Ok(new CouponRedemption<TestGrant>("welcome", null, null, false)));
			LogAssert.Expect(LogType.Error, new Regex("Coupon redeemed callback failed"));
			LogAssert.Expect(LogType.Exception, new Regex("game bug"));

			_native.Call("1", CouponWebViewCommands.Redeem, new JObject { ["code"] = "WELCOME2026" });

			Assert.AreEqual(true, (bool) _native.Result("1")["ok"]);
		}

		[Test]
		public void Add_RejectsNullsAndDuplicates()
		{
			Assert.Throws<ArgumentNullException>(() => CouponWebViewCommands.Add<TestGrant>(null, _coupons));
			Assert.Throws<ArgumentNullException>(() => CouponWebViewCommands.Add<TestGrant>(_webView, null));
			Assert.Throws<InvalidOperationException>(() => CouponWebViewCommands.Add<TestGrant>(_webView, _coupons));
		}

		private sealed class FakeCouponService : ICouponService
		{
			public List<string> Codes { get; } = new List<string>();

			public List<CancellationToken> Tokens { get; } = new List<CancellationToken>();

			public object Next { get; set; }

			public UniTask<Result<CouponRedemption<TGrant>>> RedeemAsync<TGrant>(string code, CancellationToken cancellationToken = default)
			{
				Codes.Add(code);
				Tokens.Add(cancellationToken);
				return (UniTask<Result<CouponRedemption<TGrant>>>) Next;
			}
		}
	}
}

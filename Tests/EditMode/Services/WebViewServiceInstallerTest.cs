using System;
using Cysharp.Threading.Tasks;
using Geuneda.Core;
using Geuneda.Services;
using NUnit.Framework;

// ReSharper disable once CheckNamespace

namespace Geuneda.WebView.Tests
{
	/// <summary>services 의 <c>MainInstaller</c> 에 인터페이스로 바인딩하고 <c>CleanDispose</c> 로 정리합니다(com.geuneda.services 가 있을 때만 켜짐).</summary>
	[TestFixture]
	public class WebViewServiceInstallerTest
	{
		private const string PageUrl = "https://notice.example.com/ko/index.html";

		private FakeWebViewFactory _factory;
		private WebViewService _service;
		private int _closed;

		[SetUp]
		public void SetUp()
		{
			_factory = new FakeWebViewFactory();
			_closed = 0;
			_service = new WebViewService(new WebViewServiceOptions
			{
				Factory = _factory,
				AllowedOrigins = new[] { "https://notice.example.com" },
				LoadTimeout = TimeSpan.FromSeconds(30)
			});
			_service.Closed += () => _closed++;
			MainInstaller.Bind<IWebViewService>(_service);
		}

		[TearDown]
		public void TearDown()
		{
			_service.Dispose();
			MainInstaller.Clean<IWebViewService>();
		}

		[Test]
		public void CleanDispose_ClosesTheOpenWebViewAndRemovesTheBinding()
		{
			var open = _service.OpenAsync(PageUrl);
			_factory.Last.Ready();
			Assert.AreEqual(UniTaskStatus.Succeeded, open.Status);
			Assert.IsTrue(open.GetAwaiter().GetResult().IsSuccess);

			Assert.IsTrue(MainInstaller.CleanDispose<IWebViewService>());

			Assert.IsTrue(_factory.Last.Disposed);
			Assert.AreEqual(1, _closed);
			Assert.IsFalse(_service.IsOpen);
			Assert.IsFalse(MainInstaller.TryResolve<IWebViewService>(out _));
			Assert.Throws<ObjectDisposedException>(() => _service.OpenAsync(PageUrl).Forget());
		}

		[Test]
		public void CleanDispose_WhileOpening_CancelsTheOpen()
		{
			var open = _service.OpenAsync(PageUrl);

			Assert.IsTrue(MainInstaller.CleanDispose<IWebViewService>());

			Assert.AreEqual(UniTaskStatus.Succeeded, open.Status);
			Assert.AreEqual(ResultCodes.Cancelled, open.GetAwaiter().GetResult().Code);
			Assert.IsTrue(_factory.Last.Disposed);
			Assert.AreEqual(0, _closed, "a failed open does not raise Closed");
		}
	}
}

# Geuneda WebView

앱 안 웹뷰로 공지·쿠폰 같은 웹 페이지를 띄우고 페이지와 명령을 주고받습니다. 페이지는 함께 주는 JS 파일(`Web~/geuneda-webview.js`)의 `geuneda.call(명령, 인자)` 로 Unity 명령을 부르고 결과를 Promise 로 받습니다. Unity 는 페이지에 이벤트를 보냅니다. 네이티브 웹뷰는 어댑터 패키지가 넣습니다(`com.geuneda.webview.gree`). 결과는 core 결과 규약(`Result`)을 따릅니다.

| 기능 | 타입 | 네임스페이스 |
|------|------|-------------|
| 웹뷰 | `IWebViewService`, `WebViewService`, `WebViewServiceOptions`, `WebViewResultCodes`(12000 블록) | `Geuneda.WebView` |
| 명령 | `WebViewCommandHandler`, `WebViewCommand` | `Geuneda.WebView` |
| 어댑터 계약 | `IWebViewFactory`, `IWebViewNative`, `IWebViewNativeListener`, `WebViewNativeSettings`, `WebViewMargins` | `Geuneda.WebView` |
| 공지 명령(notice 가 있을 때) | `NoticeWebViewCommands` | `Geuneda.WebView.Notice` |
| 쿠폰 명령(coupon 이 있을 때) | `CouponWebViewCommands` | `Geuneda.WebView.Coupon` |
| 페이지 쪽 | `Web~/geuneda-webview.js`, 샘플 `Web~/notice.html`, `Web~/coupon.html` | |

## 설치

`Packages/manifest.json` 에 추가합니다. UPM 은 git 의존성을 자동으로 받지 않으므로 core, UniTask 와 어댑터(gree unity-webview 포함)도 함께 넣습니다. 어댑터 설치는 `com.geuneda.webview.gree` 의 README 를 봅니다.

```json
{
  "dependencies": {
    "com.cysharp.unitask": "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask",
    "com.geuneda.core": "https://github.com/geuneda/geuneda-core.git",
    "com.geuneda.webview": "https://github.com/geuneda/geuneda-webview.git",
    "com.geuneda.webview.gree": "https://github.com/geuneda/geuneda-webview-gree.git"
  }
}
```

공지·쿠폰 명령은 `com.geuneda.notice`·`com.geuneda.coupon` 이 프로젝트에 있으면 켜집니다(어셈블리 `Geuneda.WebView.Notice`, `Geuneda.WebView.Coupon`).

## 사용법 (Unity)

```csharp
using Geuneda.WebView;
using Geuneda.WebView.Coupon;
using Geuneda.WebView.Gree;
using Geuneda.WebView.Notice;

var webView = new WebViewService(new WebViewServiceOptions
{
    Factory = new GreeWebViewFactory(),
    AllowedOrigins = new[] { "https://notice.example.com" },   // 이 출처의 페이지만 열고 명령을 받음
    // LoadTimeout 기본 10초
});
MainInstaller.Bind<IWebViewService>(webView);

// 공지·쿠폰 명령. 돌려준 객체를 Dispose 하면 뺀다
NoticeWebViewCommands.Add(webView, notices, () => timeService.DateTimeUtcNow);
CouponWebViewCommands.Add<MyGrant>(webView, coupons, redemption => SyncWalletFromServerAsync().Forget());

// 게임 명령
webView.AddCommand("game.openShop", (command, token) =>
{
    if (!command.TryGetString("productId", out var productId))
    {
        return UniTask.FromResult(Result<object>.Fail(WebViewResultCodes.InvalidArguments, "productId is required"));
    }

    shop.Open(productId);
    return UniTask.FromResult(Result<object>.Ok(null));
});

// 열기: 언어 등은 주소에 넣는다. frame 은 웹뷰를 띄울 UI 영역(없으면 안전 영역 전체)
var opened = await webView.OpenAsync($"https://notice.example.com/{language}/notice.html", noticeFrame, token);

if (!opened.IsSuccess)
{
    ShowToast("공지를 열 수 없습니다");   // LoadFailed, Timeout, Cancelled, InProgress
}

// 뒤로가기: 게임의 뒤로가기 처리기가 웹뷰가 열려 있는 동안 부른다
if (webView.HandleBack()) return;   // 페이지 기록이 있으면 이전 페이지, 없으면 닫기

// 종료: 열린 웹뷰를 닫고(열기 중이면 그 열기는 Cancelled) 바인딩을 지운다
MainInstaller.CleanDispose<IWebViewService>();
```

- **열기 성공은 페이지의 JS 파일과 연결됐을 때**입니다. 보이지 않게 만들어 로드하고, JS 파일이 준비를 알리면 Unity 가 이 열기의 키를 메인 프레임에 넘기며, 페이지가 그 키로 답하면 보이게 합니다. 연결 실패는 `LoadFailed`, 브리지 없이 HTTP 오류로 로드가 끝나면(404 페이지 등) `LoadFailed`, 제한 시간 안에 연결되지 않으면 `Timeout` 이고 웹뷰를 버립니다.
- **영역**: `frame` 의 화면 위치·크기를 매 프레임(`LastPostLateUpdate`) 따라가고 바뀔 때만 네이티브에 알립니다. 영역이 파괴되면(씬 전환) 닫습니다. 웹뷰는 네이티브 뷰라 게임 UI 위에 그려지므로 닫기 버튼은 페이지(`geuneda.close()`)나 영역 밖에 둡니다. Android 에서는 `Screen.SetResolution` 직후 한 프레임 동안 여백이 어긋나 보일 수 있고 다음 프레임에 맞춰집니다(Android 에뮬레이터에서 보고, iOS 시뮬레이터에서는 화면·캔버스 크기가 같은 프레임에 바뀌어 재현되지 않음).
- **닫기**: `Close()`, 페이지의 `close`, 뒤로가기, 영역 파괴. 닫히면 `Closed` 가 오고, 진행 중인 명령 처리기의 토큰이 취소되며 그 결과는 버립니다.
- **정리**: `IWebViewService` 는 `IDisposable` 입니다(0.2.0). `Dispose` 는 `Close` 처럼 닫고 그 뒤의 열기를 막습니다(`ObjectDisposedException`). 여러 번 불러도 됩니다. 그래서 `MainInstaller.CleanDispose<IWebViewService>()` 로 닫고 바인딩을 지울 수 있습니다. 0.1.0 은 구현 클래스만 `IDisposable` 이라 이 호출이 컴파일되지 않았습니다.
- **Android 뒤로가기**: Android System WebView 는 사용자 제스처 없이 스크립트로 옮긴 방문(`location.href` 등)을 뒤로가기 기록에서 건너뜁니다. 페이지가 스크립트로 이동했으면 첫 뒤로가기가 그 앞 페이지로 가지 않고 웹뷰를 닫을 수 있습니다. 뒤로 돌아와야 하는 이동은 사용자가 누르는 링크로 만듭니다.
- 하나만 엽니다. 열려 있는데 다시 열면 `ResultCodes.InProgress` 입니다.
- 결과는 명령을 보낸 문서에만 갑니다(JS 파일이 문서마다 무작위 ID 를 붙임). 같은 웹뷰에서 페이지를 옮기거나 새로고침해도 이전 문서의 결과가 새 문서의 Promise 를 끝내지 않습니다.

### 명령 결과

처리기는 `Result<object>` 를 돌려주고, 페이지는 `{ok, code, message, value}` 로 받습니다.

| 상황 | 페이지가 받는 것 |
|------|------|
| 성공 | `ok: true`, `code: 0`, `value`(Newtonsoft camelCase, null 속성 뺌) |
| 처리기 실패 | `ok: false`, 그 `code`·`message`(쿠폰이면 `CouponResultCodes` 그대로) |
| 등록되지 않은 명령 | `UnknownCommand`(12002) |
| 명령 이름·인자 형식이 틀림 | `InvalidArguments`(12003) |
| 처리기가 던진 취소·제한 시간 예외 | `ResultCodes.Cancelled`(2)·`Timeout`(3), 로그 없음 |
| 그 밖의 처리기 예외(버그) | `ResultCodes.Unknown`(1), Unity 에 예외와 함께 Error 로그 |
| 앱 웹뷰 밖(일반 브라우저) | `BridgeUnavailable`(12004, JS 파일이 돌려줌) |
| 인자를 JSON 으로 만들 수 없음(순환 참조, BigInt) | `InvalidArguments`(12003, JS 파일이 돌려줌, 보내지 않음) |
| 네이티브 전달 경로가 던짐 | `Unknown`(1, JS 파일이 돌려줌) |

IL2CPP 코드 제거에 대비해 값은 `JObject` 로 만들거나 형식·속성에 `[Preserve]` 를 붙입니다(패키지의 공지·쿠폰 명령은 `JObject`).

**글자**: Unity 가 페이지에 보내는 스크립트(결과·이벤트·키)는 ASCII 로만 만듭니다. ASCII 가 아닌 글자는 모두 `\uXXXX` 로 보내므로 한글, 이모지, U+2028·U+2029, 짝 없는 서로게이트(UTF-16 길이로 자른 글)도 그대로 페이지의 JS 문자열이 됩니다. 값 안의 `JRaw`(미리 만든 JSON)도 ASCII 로 바꿔 쓰고, 주석 토큰은 쓰지 않습니다. 0.1.0 은 날것으로 보내서 짝 없는 서로게이트가 든 결과·이벤트가 iOS 에서 통째로 사라졌고(`geuneda.call` 이 끝나지 않음) 0.2.0 에서 고쳤습니다. 한글 같은 글자는 스크립트에서 글자당 6바이트가 됩니다. 반대로 페이지가 보낸 짝 없는 서로게이트는 Unity 가 U+FFFD 로 받습니다(Newtonsoft 읽기).

## 페이지 (`Web~/`)

```html
<script src="geuneda-webview.js"></script>   <!-- 페이지 스크립트보다 먼저, async·defer 없이 -->
<script>
  geuneda.call('notice.list', { category: 'event' }).then(function (result) {
    if (result.ok) render(result.value.notices);
    else showError(result.code);
  });
  geuneda.on('notice.changed', reload);              // Unity 가 보낸 이벤트
  closeButton.onclick = geuneda.close;
  // 다른 출처로 가는 <a href> 는 자동으로 외부 브라우저로 열림. 직접 열려면 geuneda.openUrl(url)
</script>
```

- `geuneda.call` 은 거절(reject)하지 않고 결과 객체로 끝납니다. 보내지 못한 요청(직렬화 실패, 전달 경로 예외)은 그 요청만 실패로 끝나고 뒤 요청은 그대로 갑니다(0.2.0, 0.1.0 은 reject 하거나 키를 받기 전 대기열의 뒤 요청까지 멈췄음). 요청 ID 로 짝지으므로 여러 번 동시에 불러도 섞이지 않습니다. 다만 웹뷰가 닫히거나 페이지를 떠나면(그 문서가 사라짐), 또는 Unity 의 처리기가 끝나지 않으면 Promise 도 끝나지 않습니다.
- JS 파일은 페이지 서버가 함께 내려줍니다(패키지 버전과 맞춥니다). 샘플 `notice.html`·`coupon.html` 은 그대로 쓰거나 고쳐 씁니다(공지 내용은 `textContent` 로만 넣음).
- 기본 명령은 `close`, `openUrl({url})`(http·https 만) 입니다.

### 공지 명령 (`NoticeWebViewCommands`)

| 명령 | 인자 | 값 |
|------|------|------|
| `notice.list` | `{category?}` | `{language, unreadCount, notices: [{id, category, priority, startsAt, endsAt, popup, action, title, body, linkUrl, linkLabel, read}]}` |
| `notice.read` | `{id}` | 없음. 처음 읽으면 `notice.changed` 이벤트 |
| `notice.refresh` | 없음 | 없음. 실패는 공지 패키지 코드(`InvalidDocument` 등) |
| 이벤트 `notice.changed` | | 없음. 문서 갱신·언어 변경·읽음·'오늘 안 보기' 때 |

목록은 `INoticeService` 가 거른 것(기간·플랫폼·언어, 정렬)이라 게임 UI 팝업과 판정·읽음 기록이 같습니다. 시각은 UTC ISO 8601 이고 없는 값은 뺍니다.

### 쿠폰 명령 (`CouponWebViewCommands`)

| 명령 | 인자 | 값 |
|------|------|------|
| `coupon.redeem` | `{code}` | `{campaignId, alreadyRedeemed, redeemedAt, grant}`. 실패는 `CouponResultCodes` |

사용 요청은 Unity 의 `ICouponService` 가 인증 미들웨어를 거쳐 보내므로 페이지는 계정 토큰을 받지 않습니다. 웹뷰가 닫혀도 요청은 끝까지 하고(서버가 지급했을 수 있음) `redeemed` 콜백이 옵니다.

## 보안

- **허용 출처**(`AllowedOrigins`, `https://host[:port]`): 이 밖의 주소는 열지 않고(예외), 네이티브 이동 제한으로 그 밖의 http·https 주소로 이동하지 않으며(`WebViewNativeSettings.DenyUrlPattern`), 지금 페이지가 허용 출처가 아니면 명령·이벤트를 버립니다.
- **실행 순간의 출처 검사**: Unity 가 페이지에 보내는 스크립트(키·결과·이벤트)는 실행되는 순간의 `window.location.origin` 이 허용 출처일 때만 페이지에 넘깁니다. 이동 제한을 비껴간 이동(Android 는 POST 이동에 이동 제한 콜백을 부르지 않음)으로 허용 밖 문서가 메인 프레임이 되어도 키를 받지 못합니다. 그런 이동이 시작되면(허용 밖 http·https 시작 알림) 웹뷰를 닫습니다(열기 중이면 `LoadFailed`).
- **플랫폼별 허용 밖 이동**: iOS(WKWebView)는 링크·`location`·form POST·서버 리디렉트·`window.open`·iframe 이 모두 이동 제한에 걸려 요청이 나가지 않고, 웹뷰는 지금 페이지에 그대로 남습니다(시작 알림이 오지 않으므로 닫지 않음, iOS 18.6 시뮬레이터에서 확인). Android 는 GET 이동은 막지만 form POST 는 이동 제한을 거치지 않으므로, 서비스가 시작 알림을 보고 닫기 전에 그 요청이 본문과 함께 허용 밖 서버에 이미 도착합니다. 열기 키는 스크립트로만 넘기므로 함께 가지 않습니다. 허용 출처 페이지에서 다른 출처로 form 을 보내지 않습니다.
- **토큰을 넘기지 않습니다.** 레퍼런스는 페이지가 id 토큰을 받아 쿠폰 서버를 직접 불렀습니다. 여기서는 Unity 가 인증해 보내고 페이지는 결과만 받습니다.
- **키**: 열기마다 무작위 키를 만들어 준비를 알린 문서에 `EvaluateJS` 로 넘깁니다. `EvaluateJS` 는 메인 프레임에서만 실행되므로 다른 출처의 iframe(불투명 출처의 sandbox iframe 포함)은 키를 알 수 없고, 키가 없는 글은 버립니다(gree 는 보낸 프레임을 알려 주지 않으므로 키로 가림). 같은 출처의 iframe 은 페이지 코드와 같은 권한입니다.
- **iframe**: iOS 는 iframe 이동에도 이동 제한이 걸려 다른 출처 iframe 이 열리지 않습니다. 다른 출처 영상 등을 넣으려면 그 출처도 허용하거나 외부 브라우저로 엽니다.
- 명령 처리기는 페이지가 보낸 값(`command.Args`)을 검사합니다.

## 레퍼런스와 다른 점

레퍼런스는 Lucky Defense 의 PercentWebview(gree 감쌈), WebView.Notice, Coupon.WebViewPlugin 입니다.

- **따른 것**: gree unity-webview, 페이지 → Unity 는 JSON 명령, Unity → 페이지는 `EvaluateJS`, 공지 `read`·`refresh`·`moveUrl`(여기서는 `notice.read`·`notice.refresh`·`openUrl`), 쿠폰 사용 명령, 닫기 명령.
- **보완한 것**:
  - 요청 ID 로 결과를 짝짓습니다(레퍼런스는 `window.receiver.finishUseCoupon(json)` 같은 고정 콜백).
  - 공지 목록을 Unity 가 거릅니다(레퍼런스는 페이지가 서버에서 받아 따로 거르고 Unity 는 읽음만 기록).
  - 토큰을 페이지에 넘기지 않고, 허용 출처로 명령·이동을 제한합니다.
  - 로드 완료를 브리지 연결로 판정합니다(레퍼런스는 Android HTTP 오류가 리소스에도 와서 로드 실패로 읽힐 수 있음, 해제하지 않는 로드 콜백 누적).
  - 명령은 메인 프레임에만 넘긴 키가 있어야 실행합니다(레퍼런스는 어느 프레임의 글이든 실행).
  - iOS·macOS 에서 gree 가 받은 글을 URL 디코딩하므로 JS 파일이 인코딩해 보냅니다(`+`·`%` 가 깨지지 않음).

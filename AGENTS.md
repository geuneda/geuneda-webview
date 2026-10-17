# Geuneda.WebView - AI 에이전트 가이드

> **관련 파일**: `CLAUDE.md`는 이 파일을 감쌉니다 — `CLAUDE.md`가 아니라 `AGENTS.md`를 편집하세요. `README.md`는 사용자 대상 문서입니다.

## 1. 패키지 개요
- **패키지**: `com.geuneda.webview`
- **Unity**: 2021.3+
- **의존성**: `com.geuneda.core`(ModuleLogger, Result, MainThreadDispatcher), `com.cysharp.unitask`, `com.unity.nuget.newtonsoft-json`, `com.unity.modules.ui`(Canvas, RectTransformUtility). 선택: `com.geuneda.notice`·`com.geuneda.coupon`(있으면 `Geuneda.WebView.Notice`·`Geuneda.WebView.Coupon` 이 켜짐, versionDefines)
- **역할**: 웹뷰 열기·닫기·영역·뒤로가기와 페이지 ↔ Unity 명령 브리지. 네이티브 웹뷰는 어댑터(`com.geuneda.webview.gree`)가 `IWebViewFactory` 로 넣습니다(D3)
- **레퍼런스**: Percent `PercentWebview`(gree 감쌈, `{type, value}` 명령, `EvaluateJS` 고정 콜백), `WebView.Notice`(read·refresh·moveUrl), `Coupon.WebViewPlugin`(requestPlayerData 로 id 토큰을 페이지에 줌). 분석은 lucky-defense 레포 `data/analysis/tech/percent/analytics-liveops.md` 2~4절

## 2. 구조

| 파일 | 역할 |
|------|------|
| `Runtime/IWebViewService.cs`, `WebViewServiceOptions.cs` | 공개 계약, 설정 |
| `Runtime/WebViewService.cs` | 공개 API, 열기 판정, 영역, 정리 |
| `Runtime/WebViewService.Messages.cs` | 페이지 글 처리(키 확인, 명령 실행, 결과 전달), 명령 등록 |
| `Runtime/WebViewService.Session.cs` | 열기 한 번(네이티브 알림, 프레임 처리) |
| `Runtime/WebViewCommand.cs` | `WebViewCommandHandler`(처리기), `WebViewCommand`(이름, 인자, 페이지 주소) |
| `Runtime/WebViewResultCodes.cs` | 12000 블록 |
| `Runtime/Native/` | 어댑터 계약(`IWebViewFactory`, `IWebViewNative`, `IWebViewNativeListener`, `WebViewNativeSettings`, `WebViewMargins`) |
| `Runtime/Internal/WebViewOrigins.cs` | 출처 규칙(ASCII 호스트), 이동 제한 정규식 |
| `Runtime/Internal/WebViewMessages.cs` | 페이지와 주고받는 글의 형식(읽기·쓰기) |
| `Runtime/Internal/WebViewLayout.cs` | 영역 → 화면 사각형(화면 공간 캔버스는 루트 캔버스 비율, `CanvasToScreen`) → 여백 |
| `Runtime/Notice/`, `Runtime/Coupon/` | 선택 어셈블리(공지·쿠폰 명령) |
| `Web~/geuneda-webview.js` | 페이지 쪽 브리지(Unity 가 가져오지 않는 폴더) |
| `Web~/notice.html`, `Web~/coupon.html` | 샘플 페이지 |
| `Web~/tests/` | Node 테스트(`geuneda-webview.test.mjs`), 어댑터 상호 운용 테스트 페이지(`interop.html`) |

## 3. 규칙

- **열기 성공 = 연결**: 페이지 JS 파일의 `ready` → Unity 가 `bind`(문서 ID, 열기 키)를 `EvaluateJS` 로 → 페이지가 그 키로 `bound`. 로드 끝(`OnPageLoaded`)만으로 성공시키지 않습니다(404 페이지도 로드는 끝남). Android gree 는 이미지 같은 리소스의 HTTP 오류도 알리므로 HTTP 오류만으로 실패시키지 않고, 준비 알림이 하나도 없이 로드가 끝났고 그 전에 400 이상이 있었을 때만 `LoadFailed` 입니다. 브리지 페이지는 준비 알림이 로드 끝보다 먼저 옵니다(같은 네이티브 큐, 스크립트 실행이 load 이벤트보다 먼저. 2026-10-17 iOS 18.6 시뮬레이터에서 6번 모두 확인, `bound` 는 로드 끝 앞뒤 어느 쪽에도 옴). 그 밖에는 제한 시간까지 기다립니다.
- **열기 한 번 = `Session`**: 네이티브 알림·프레임 처리·명령 결과를 세션에 묶고, 지금 세션이 아니면(닫힌 뒤 늦게 온 알림) 무시합니다. 실패하면 먼저 정리하고 결과를 넘깁니다(결과를 받은 호출자가 바로 다시 열 수 있게).
- **오용 예외는 호출 자리에서**: `OpenAsync` 는 async 가 아닌 메서드에서 검사하고 async 본체(`OpenCoreAsync`)를 부릅니다(async 메서드는 예외를 작업에 담음).
- **키(리뷰 1회차 🔴)**: gree 는 보낸 프레임을 알려 주지 않습니다(Android `addJavascriptInterface` 는 모든 프레임, iOS 는 `frameInfo` 를 버림). 그래서 열기마다 무작위 키를 만들어 준비 알림에 답할 때 `EvaluateJS`(메인 프레임에서만 실행)로 넘기고, `bound`·`call` 은 키가 맞을 때만 받습니다. 준비 알림 자체는 아무 프레임이나 보낼 수 있지만 키는 메인 프레임으로만 갑니다. 페이지가 보낸 URL 같은 자기 신고 값으로 가리지 않습니다.
- **문서 ID(리뷰 1회차 🔴)**: JS 파일이 문서마다 무작위 ID 를 만들어 모든 글에 붙이고, 결과에 그 ID 를 돌려줍니다. 요청 ID 는 문서마다 1 부터 다시 시작하므로 세션만으로 거르면 페이지를 옮긴 뒤 이전 문서의 결과가 새 문서의 Promise 를 끝냅니다.
- **실행 순간의 출처 검사(리뷰 2회차 🔴)**: 페이지로 가는 모든 스크립트는 `(function(o){if(o==="허용 출처"||...){window.geuneda.__receive(...)}})(window.location.origin)` 모양입니다(`WebViewMessages.Script`). 마지막 로드 끝 주소(`PageUrl`)만 믿으면, Android 의 POST 이동처럼 이동 제한(`shouldOverrideUrlLoading`)을 비껴간 이동 뒤 로드가 끝나기 전에 허용 밖 문서가 키를 받습니다. `window.location` 은 페이지가 바꿀 수 없는 속성이고(`let location` 도 SyntaxError) 비교는 `===` 만 씁니다.
- **허용 밖 이동 시작이면 닫기**: gree 의 시작 알림은 iOS·macOS 에서 이동 제한을 통과한 뒤에만 오고, Android 는 그 밖에 `onPageStarted`(메인 프레임만, POST 포함)에서도 옵니다. 그래서 허용 밖 http·https 시작 알림은 제한을 비껴간 메인 프레임 이동이고, 서비스는 웹뷰를 닫습니다. 열기 중이면 `LoadFailed` 이거나 열기 성공 직후 `Closed` 입니다. Android 는 POST 의 `onPageStarted` 를 응답 커밋 때 보내므로, 그 사이 원래(허용) 문서가 `bind` 를 받아 `bound` 를 먼저 보내면 열기가 성공한 뒤 같은 프레임에 닫힙니다(2026-10-17 Android 13 에뮬레이터 6번 중 2번, Android 16 기기 3번 중 0번). 어느 쪽이든 웹뷰는 남지 않고 키는 허용 문서에만 갑니다. 허용 출처·http(s) 가 아닌 시작 알림(iOS 의 iframe 등)은 무시하고 `PageUrl` 도 바꾸지 않습니다.
- **허용 출처 세 겹**: 연 주소 검사(예외), 네이티브 이동 제한(`DenyUrlPattern`, 걸 수 없는 플랫폼이면 어댑터가 열기를 실패시킴), 지금 페이지 주소 검사(명령·결과·이벤트·키 전달). 페이지 주소는 메인 프레임 로드 끝(`OnPageLoaded`)으로만 바꿉니다. iOS gree 는 iframe 이동에도 시작 알림(`CallOnStarted`)을 보내므로 시작 알림은 쓰지 않습니다.
- **이동 제한 정규식**은 Java(`Pattern.find`)·ICU(`firstMatchInString`)·.NET 이 같은 뜻으로 읽는 문법만 씁니다: `(?i)`, `^`, 부정 전방 탐색, 영문·숫자가 아닌 글자 앞의 역슬래시. 허용 출처 뒤에는 끝·`/`·`?`·`#` 만 옵니다(`a.com.evil.com`, `a.com:8443`, `a.com@evil.com` 막음). http·https 만 맞추므로 `unity:`·`about:blank`·`mailto:` 는 그대로입니다.
- **명령 등록 해제는 등록 객체 기준**입니다(같은 처리기를 다시 등록한 뒤 옛 등록을 버려도 새 등록이 남음, 테스트로 발견).
- **처리기 예외(페이지 경계)**: 세션이 닫혀 생긴 취소는 조용히 끝내고, 처리기 자신의 취소·제한 시간 예외는 `ResultCodes.FromException` 으로 `Cancelled`·`Timeout`(로그 없음)입니다. 그 밖의 예외는 버그라 예외와 함께 Error 로그를 남기고 `Unknown` 으로 답합니다. 공통 규칙은 `catch (Exception)` 으로 버그를 결과로 숨기지 말라는 것이지만, 여기는 페이지의 Promise 가 끝나야 하는 비동기 경계라 답을 보내고 버그는 Error 로 드러냅니다(ads 가 어댑터 `Show` 예외를 Error 로그와 `DisplayFailed` 로 끝내는 것과 같은 경계, 리뷰 1회차 🔵).
- **정리 순서**: `Teardown` 은 토큰 취소 콜백이 던져도(`AggregateException`, Error 로그) 네이티브를 버리고, 열기 실패는 정리가 던져도 결과를 넘깁니다(리뷰 1회차 🟡). 토큰 원천은 타이머·연결이 없어 해제하지 않습니다(이미 받은 처리기가 토큰을 계속 씀).
- **직렬화**: Newtonsoft camelCase, null 속성 뺌, 서비스마다 설정 객체를 따로 둡니다. 패키지 명령의 값은 `JObject` 로 만듭니다(IL2CPP 코드 제거에 리플렉션 getter 가 지워지지 않게). 페이지로 가는 스크립트는 ASCII 로만 만듭니다(`WebViewMessages.Script`, `StringEscapeHandling.EscapeNonAscii`). 0.1.0 은 날것으로 보내서 짝 없는 서로게이트가 든 결과·이벤트가 gree iOS 에서 사라졌습니다(IL2CPP 가 UTF-8 로 바꾼 바이트가 올바르지 않아 `[NSString stringWithUTF8String:]` 이 nil, `geuneda.call` 이 끝나지 않음, 2026-10-17 iOS 시뮬레이터). `\uXXXX` 는 JS 가 같은 코드 단위로 읽으므로 데이터가 그대로 가고, U+2028·U+2029(ES2019 이전 엔진)와 어댑터·플랫폼의 문자열 변환(JNI, Windows)에도 기대지 않습니다. `Replace` 같은 후처리로 따로 고치지 않습니다. 이스케이프 설정을 거치지 않는 날 JSON(`JRaw`, 변환기의 `WriteRawValue`)은 `AsciiJsonWriter.WriteRaw` 가 ASCII 가 아닌 코드 단위를 `\uXXXX` 로 바꾸고(올바른 JSON 에서 그런 글자는 문자열 안에만 옴), 주석은 쓰지 않습니다(2026-10-17 리뷰 1회차). 반대 방향은 Newtonsoft 읽기가 짝 없는 서로게이트 이스케이프를 U+FFFD 로 바꿉니다(받아들임).
- **JS 파일의 사전**: 대기 중 요청과 이벤트 처리기는 `Object.create(null)` 입니다(`constructor`·`__proto__` 같은 이름도 보통 키, 리뷰 1회차 🟡).
- **JS 파일의 전달 경로**: iOS·macOS(gree WKWebView)는 `window.webkit.messageHandlers.unityControl` 로 **URL 인코딩해** 보냅니다. gree 가 받은 글에 `UnEscapeURL` 을 하기 때문입니다(`+` 가 공백이 됨, 상호 운용 테스트에서 인코딩을 빼는 변형으로 실패를 확인). Android 는 `window.Unity.call` 로 그대로 보냅니다(디코딩 없음). webkit 경로를 먼저 봅니다(iOS 는 `window.Unity` 도 주입함).
- **JS 파일의 명령 전송은 거절하지 않습니다**: 명령은 `deliver` 로 하나씩 보내고, 직렬화 실패(순환 참조, BigInt)는 `invalidArguments`, 전달 경로 예외는 `unknown` 결과로 그 요청만 끝냅니다. 실패 설명은 `describe` 로 만듭니다(던진 값이 Symbol·프로토타입 없는 객체여도 다시 던지지 않음, 2026-10-17 리뷰 2회차). 키를 받기 전 대기열을 보낼 때도 한 요청의 실패가 뒤 요청을 막지 않습니다(0.1.0 은 Promise executor 안의 예외로 reject, 2026-10-17 리뷰 1회차). 닫힌 웹뷰·떠난 문서·끝나지 않는 처리기의 Promise 는 끝나지 않습니다(문서와 함께 사라짐).
- **영역**: `PlayerLoopHelper.AddAction(LastPostLateUpdate)` 로 Canvas 배치가 끝난 뒤 읽고, 여백이나 화면 크기가 바뀔 때만 어댑터에 넘깁니다(gree Android 의 여백 적용은 JNI). 화면 크기도 보는 이유는 데스크톱 gree 가 뷰 크기를 화면 크기 - 여백으로 정하기 때문입니다(여백 0 인 전체 화면에서 창 크기 변경, 리뷰 1회차 🔴). 작업용 모서리 배열은 인스턴스 필드입니다(D2).
- **영역 좌표계(0.2.1)**: 화면 공간 캔버스(Overlay, Camera) 아래의 영역은 모서리를 루트 캔버스 로컬 좌표로 바꿔 캔버스 안의 비율(0~1)로 만들고, 카메라 뷰포트(`Camera.rect`, 0~1 로 자름, Overlay 와 카메라 없는 Camera 캔버스는 전체)와 `ScreenSize()` 를 곱합니다(`WebViewLayout.CanvasToScreen`). 영역 사각형과 기준 크기가 같은 배치에서 나와야 하기 때문입니다. Android 는 `Screen.SetResolution` 을 부른 프레임의 `LastPostLateUpdate` 에서 `Screen`·`Canvas.pixelRect` 는 새 크기인데 루트 캔버스 `rect`·`scaleFactor` 와 영역 모서리는 옛 크기라, 0.2.0 은 옛 화면 픽셀 모서리를 새 화면 크기로 계산해 한 프레임 동안 틀린 여백을 보냈습니다(2026-10-17 Android 13 에뮬레이터·Android 16 기기에서 프레임마다 기록, 다음 프레임부터 캔버스가 맞음). `Canvas.pixelRect` 는 이미 새 값이라 낡은 프레임 판별에 쓸 수 없습니다. Camera 캔버스는 모서리를 카메라 시선(원근은 카메라 위치에서, 직교는 카메라 방향으로)을 따라 루트 캔버스 평면에 먼저 옮깁니다(`WebViewLayout.ToCanvasPlane`). 그래야 z 이동·3D 회전한 영역의 원근이 0.2.0 의 투영과 같게 남고, 카메라 위치·방향은 해상도와 상관없어 낡은 프레임에도 맞습니다(2026-10-17 리뷰 1회차 🔴: 처음 안은 z 를 버려 원근 카메라에서 z 로 옮긴 영역을 잘못 놓음). Overlay 는 0.2.0 과 같이 z 를 버립니다(정사영). WorldSpace 캔버스·캔버스 밖·크기 0 캔버스(배치 전)는 월드 모서리를 카메라로 투영합니다(카메라가 없으면 월드 좌표가 곧 화면 좌표). 루트 캔버스는 열 때 한 번 찾습니다(`Session.RootCanvas`). 매 프레임 경로라 할당하지 않습니다. 안전 영역 경로(영역 없음)는 `Screen.safeArea` 가 `Screen` 과 같은 프레임에 바뀌어 고칠 것이 없습니다(Android 에서 기록으로 확인).
- **다른 스레드의 취소**: 만든 스레드(메인)가 아니면 `MainThreadDispatcher.Post` 로 옮깁니다. 그 밖의 API 는 메인 스레드 전용입니다.
- **쿠폰 명령은 닫기 토큰을 넘기지 않습니다**: 닫혀도 요청을 끝까지 해야 지급 여부를 알고 `redeemed` 콜백으로 보상을 맞춥니다.
- **수명**: `IWebViewService` 는 `IDisposable` 입니다(0.2.0, `MainInstaller.CleanDispose<IWebViewService>()`). `Dispose` 는 `Close` 와 같이 닫고(열기 중이면 `Cancelled`) 그 뒤의 `OpenAsync` 를 `ObjectDisposedException` 으로 막으며 여러 번 불러도 됩니다. 0.1.0 은 구현 클래스만 `IDisposable` 이라 `CleanDispose` 를 쓸 수 없었습니다(2026-10-17, 호환성 shim 없이 0.2.0 으로 올림, servertime 0.2.0 과 같은 변경).
- **static 가변 상태 금지**(D2). 로거만 static 입니다.

## 4. 테스트

- Unity: EditMode `Geuneda.WebView.Tests.EditMode`(가짜 어댑터 `FakeWebViewFactory`, 지연·외부 열기·안전 영역·화면 크기는 internal 필드로 바꿈), `Geuneda.WebView.Notice.Tests.EditMode`(실제 `NoticeService` + 가짜 전송, PlayerPrefs 키 `geuneda.webview.tests.notice`), `Geuneda.WebView.Coupon.Tests.EditMode`(가짜 `ICouponService`). `Geuneda.WebView.Services.Tests.EditMode`(services 가 있을 때만, `MainInstaller.CleanDispose<IWebViewService>()` 가 열린 웹뷰를 닫고 바인딩을 지움, 열기 중이면 `Cancelled`). 선택 테스트 어셈블리도 notice·coupon·services 가 있을 때만 켜집니다. 페이지로 가는 스크립트가 ASCII 이고 짝 없는 서로게이트·이모지·U+2028·속성 이름을 코드 단위 그대로 담는지는 `Scripts_AreAsciiAndKeepEveryUtf16CodeUnit`(0.1.0 코드에서는 실패함을 확인), 값 안의 `JRaw`·주석은 `Scripts_AreAsciiForRawJsonAndDropComments`(이스케이프 설정만 쓴 코드에서는 실패함을 확인), `IDisposable` 제약은 `Dispose_ThroughTheInterface_ClosesTheWebViewOnce` 가 봅니다. 영역 좌표계는 `Layout_CanvasToScreen_*`(순수 함수, 옛 캔버스와 새 화면 크기가 섞인 입력, 카메라 뷰포트)와 `Layout_ScreenSpaceCanvas_KeepsTheFrameProportionsWhenTheScreenChangesFirst`(실제 Canvas: Overlay, 중첩 캔버스, 배율 0.5, 카메라 없는 Camera 캔버스), `Layout_CameraCanvas_PlacesTheFrameInTheCameraViewport`, `Layout_CameraCanvas_ProjectsAFrameOffTheCanvasPlaneAlongTheCameraRays`(실제 카메라와 캔버스에서 z 로만 옮긴 영역이 원근이면 반으로, 직교면 그대로), `Layout_ToCanvasPlane_FollowsTheCameraRay`, `Layout_WorldSpaceCanvas_ProjectsTheWorldCorners`(기존 경로 유지)가 봅니다. 캔버스 배치를 기다리지 않게 루트 RectTransform 을 직접 둡니다. 0.2.0 런타임으로 되돌리면 캔버스 테스트 5개가 실패하고 기기에서 본 값 `(216, 0, 0, 96)` 이 나옵니다(2026-10-17).
- JS: 패키지 루트에서 `node --test "Web~/tests/*.test.mjs"`(Node 21 이상). 직렬화 실패·전달 예외·대기열의 실패가 뒤 요청을 막지 않음(0.1.0 JS 로는 실패함을 확인).
- 실제 웹뷰: `com.geuneda.webview.gree` 의 `GreeWebViewInteropTest`(macOS 에디터, `Web~` 를 정적 서버로). `interop.html` 은 Unity 가 보낸 결과·이벤트의 글을 코드 단위 16진수로 돌려줍니다(페이지 → Unity 는 짝 없는 서로게이트를 U+FFFD 로 받으므로).
- 기기 확인(2026-10-17, iOS 18.6 시뮬레이터, IL2CPP 코드 제거 High, gree `dist/package-nofragment` 20f14061): 준비 알림이 로드 끝보다 먼저, 404 브리지 페이지 열림·브리지 없는 404 `LoadFailed`·200 `Timeout`, 영역과 웹뷰 차이 1pt 이하, `+`·공백·`%`·U+2028·NUL·8000자 등 33개 글 왕복, 짝 없는 서로게이트 결과·이벤트 도착(0.1.0 은 사라짐), 허용 밖 href·POST·302·`window.open`·iframe·`_blank` 차단(요청 없음, 웹뷰 남음), sandbox·다른 출처 iframe 명령 차단, `Screen.SetResolution` 직후 여백 어긋남 없음. macOS Mono 플레이어에서 창 크기 변경(내부 `SetResolution`, 바깥 AX 크기 변경) 시 gree 뷰 크기가 따라옴.
- 기기 확인(2026-10-17, Android 13 arm64 에뮬레이터 System WebView 109·Galaxy S23+ Android 16 WebView 155, IL2CPP 코드 제거 High, gree `dist/package-nofragment` 20f14061): 상호 운용 8단계(짝 없는 서로게이트 결과·이벤트 16진수 `0061d83d0062d83dde002028d55cdc00` 포함, 0.1.0 도 Android 에서는 그대로 옴), 404·브리지 없음·연결 거부, 허용 밖 GET 차단·POST 닫기, 뒤로가기(키 이벤트와 3버튼 내비게이션 바 버튼 모두 Unity 에 옴, 링크 탭 이동은 이전 페이지 → 닫기, 제스처 없는 스크립트 이동은 바로 닫기), 영역·안전 영역 스크린샷 경계 0px(해상도 절반에서 정수 여백 반올림으로 1px). `Screen.SetResolution` 직후 8프레임의 여백을 Overlay·Camera·중첩 캔버스·안전 영역에서 기록해 모두 맞음(0.2.0 은 첫 프레임이 틀림). 스모크와 분석 스크립트는 lucky-defense 세션 스크래치 `android-verify/`(이 레포 밖).

## 5. 문서

- 공개 API, 명령 규약, JS 파일을 바꾸면 `README.md`, `CHANGELOG.md`, `package.json` 버전, JS 파일 머리의 버전, geuneda-frameworks 호스트 레포의 `claude-skills/geuneda-webview/`(SKILL.md, references/api.md)와 `claude-skills/geuneda-frameworks/` 를 함께 고치고 `claude-skills/install.sh geuneda-webview`, `claude-skills/install.sh geuneda-frameworks` 로 설치합니다.
- 어댑터 계약(`Runtime/Native/`)을 바꾸면 `com.geuneda.webview.gree` 를 함께 고칩니다.

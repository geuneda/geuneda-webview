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
| `Runtime/Internal/WebViewLayout.cs` | 영역 → 화면 사각형 → 여백 |
| `Runtime/Notice/`, `Runtime/Coupon/` | 선택 어셈블리(공지·쿠폰 명령) |
| `Web~/geuneda-webview.js` | 페이지 쪽 브리지(Unity 가 가져오지 않는 폴더) |
| `Web~/notice.html`, `Web~/coupon.html` | 샘플 페이지 |
| `Web~/tests/` | Node 테스트(`geuneda-webview.test.mjs`), 어댑터 상호 운용 테스트 페이지(`interop.html`) |

## 3. 규칙

- **열기 성공 = 연결**: 페이지 JS 파일의 `ready` → Unity 가 `bind`(문서 ID, 열기 키)를 `EvaluateJS` 로 → 페이지가 그 키로 `bound`. 로드 끝(`OnPageLoaded`)만으로 성공시키지 않습니다(404 페이지도 로드는 끝남). Android gree 는 이미지 같은 리소스의 HTTP 오류도 알리므로 HTTP 오류만으로 실패시키지 않고, 준비 알림이 하나도 없이 로드가 끝났고 그 전에 400 이상이 있었을 때만 `LoadFailed` 입니다. 브리지 페이지는 준비 알림이 로드 끝보다 먼저 옵니다(같은 네이티브 큐, 스크립트 실행이 load 이벤트보다 먼저). 그 밖에는 제한 시간까지 기다립니다.
- **열기 한 번 = `Session`**: 네이티브 알림·프레임 처리·명령 결과를 세션에 묶고, 지금 세션이 아니면(닫힌 뒤 늦게 온 알림) 무시합니다. 실패하면 먼저 정리하고 결과를 넘깁니다(결과를 받은 호출자가 바로 다시 열 수 있게).
- **오용 예외는 호출 자리에서**: `OpenAsync` 는 async 가 아닌 메서드에서 검사하고 async 본체(`OpenCoreAsync`)를 부릅니다(async 메서드는 예외를 작업에 담음).
- **키(리뷰 1회차 🔴)**: gree 는 보낸 프레임을 알려 주지 않습니다(Android `addJavascriptInterface` 는 모든 프레임, iOS 는 `frameInfo` 를 버림). 그래서 열기마다 무작위 키를 만들어 준비 알림에 답할 때 `EvaluateJS`(메인 프레임에서만 실행)로 넘기고, `bound`·`call` 은 키가 맞을 때만 받습니다. 준비 알림 자체는 아무 프레임이나 보낼 수 있지만 키는 메인 프레임으로만 갑니다. 페이지가 보낸 URL 같은 자기 신고 값으로 가리지 않습니다.
- **문서 ID(리뷰 1회차 🔴)**: JS 파일이 문서마다 무작위 ID 를 만들어 모든 글에 붙이고, 결과에 그 ID 를 돌려줍니다. 요청 ID 는 문서마다 1 부터 다시 시작하므로 세션만으로 거르면 페이지를 옮긴 뒤 이전 문서의 결과가 새 문서의 Promise 를 끝냅니다.
- **실행 순간의 출처 검사(리뷰 2회차 🔴)**: 페이지로 가는 모든 스크립트는 `(function(o){if(o==="허용 출처"||...){window.geuneda.__receive(...)}})(window.location.origin)` 모양입니다(`WebViewMessages.Script`). 마지막 로드 끝 주소(`PageUrl`)만 믿으면, Android 의 POST 이동처럼 이동 제한(`shouldOverrideUrlLoading`)을 비껴간 이동 뒤 로드가 끝나기 전에 허용 밖 문서가 키를 받습니다. `window.location` 은 페이지가 바꿀 수 없는 속성이고(`let location` 도 SyntaxError) 비교는 `===` 만 씁니다.
- **허용 밖 이동 시작이면 닫기**: gree 의 시작 알림은 iOS·macOS 에서 이동 제한을 통과한 뒤에만 오고, Android 는 그 밖에 `onPageStarted`(메인 프레임만, POST 포함)에서도 옵니다. 그래서 허용 밖 http·https 시작 알림은 제한을 비껴간 메인 프레임 이동이고, 서비스는 웹뷰를 닫습니다(열기 중이면 `LoadFailed`). 허용 출처·http(s) 가 아닌 시작 알림(iOS 의 iframe 등)은 무시하고 `PageUrl` 도 바꾸지 않습니다.
- **허용 출처 세 겹**: 연 주소 검사(예외), 네이티브 이동 제한(`DenyUrlPattern`, 걸 수 없는 플랫폼이면 어댑터가 열기를 실패시킴), 지금 페이지 주소 검사(명령·결과·이벤트·키 전달). 페이지 주소는 메인 프레임 로드 끝(`OnPageLoaded`)으로만 바꿉니다. iOS gree 는 iframe 이동에도 시작 알림(`CallOnStarted`)을 보내므로 시작 알림은 쓰지 않습니다.
- **이동 제한 정규식**은 Java(`Pattern.find`)·ICU(`firstMatchInString`)·.NET 이 같은 뜻으로 읽는 문법만 씁니다: `(?i)`, `^`, 부정 전방 탐색, 영문·숫자가 아닌 글자 앞의 역슬래시. 허용 출처 뒤에는 끝·`/`·`?`·`#` 만 옵니다(`a.com.evil.com`, `a.com:8443`, `a.com@evil.com` 막음). http·https 만 맞추므로 `unity:`·`about:blank`·`mailto:` 는 그대로입니다.
- **명령 등록 해제는 등록 객체 기준**입니다(같은 처리기를 다시 등록한 뒤 옛 등록을 버려도 새 등록이 남음, 테스트로 발견).
- **처리기 예외(페이지 경계)**: 세션이 닫혀 생긴 취소는 조용히 끝내고, 처리기 자신의 취소·제한 시간 예외는 `ResultCodes.FromException` 으로 `Cancelled`·`Timeout`(로그 없음)입니다. 그 밖의 예외는 버그라 예외와 함께 Error 로그를 남기고 `Unknown` 으로 답합니다. 공통 규칙은 `catch (Exception)` 으로 버그를 결과로 숨기지 말라는 것이지만, 여기는 페이지의 Promise 가 끝나야 하는 비동기 경계라 답을 보내고 버그는 Error 로 드러냅니다(ads 가 어댑터 `Show` 예외를 Error 로그와 `DisplayFailed` 로 끝내는 것과 같은 경계, 리뷰 1회차 🔵).
- **정리 순서**: `Teardown` 은 토큰 취소 콜백이 던져도(`AggregateException`, Error 로그) 네이티브를 버리고, 열기 실패는 정리가 던져도 결과를 넘깁니다(리뷰 1회차 🟡). 토큰 원천은 타이머·연결이 없어 해제하지 않습니다(이미 받은 처리기가 토큰을 계속 씀).
- **직렬화**: Newtonsoft camelCase, null 속성 뺌, 서비스마다 설정 객체를 따로 둡니다. 패키지 명령의 값은 `JObject` 로 만듭니다(IL2CPP 코드 제거에 리플렉션 getter 가 지워지지 않게). 스크립트에 넣는 JSON 의 U+2028·U+2029 는 이스케이프합니다(ES2019 이전 엔진).
- **JS 파일의 사전**: 대기 중 요청과 이벤트 처리기는 `Object.create(null)` 입니다(`constructor`·`__proto__` 같은 이름도 보통 키, 리뷰 1회차 🟡).
- **JS 파일의 전달 경로**: iOS·macOS(gree WKWebView)는 `window.webkit.messageHandlers.unityControl` 로 **URL 인코딩해** 보냅니다. gree 가 받은 글에 `UnEscapeURL` 을 하기 때문입니다(`+` 가 공백이 됨, 상호 운용 테스트에서 인코딩을 빼는 변형으로 실패를 확인). Android 는 `window.Unity.call` 로 그대로 보냅니다(디코딩 없음). webkit 경로를 먼저 봅니다(iOS 는 `window.Unity` 도 주입함).
- **영역**: `PlayerLoopHelper.AddAction(LastPostLateUpdate)` 로 Canvas 배치가 끝난 뒤 읽고, 여백이나 화면 크기가 바뀔 때만 어댑터에 넘깁니다(gree Android 의 여백 적용은 JNI). 화면 크기도 보는 이유는 데스크톱 gree 가 뷰 크기를 화면 크기 - 여백으로 정하기 때문입니다(여백 0 인 전체 화면에서 창 크기 변경, 리뷰 1회차 🔴). 작업용 모서리 배열은 인스턴스 필드입니다(D2).
- **다른 스레드의 취소**: 만든 스레드(메인)가 아니면 `MainThreadDispatcher.Post` 로 옮깁니다. 그 밖의 API 는 메인 스레드 전용입니다.
- **쿠폰 명령은 닫기 토큰을 넘기지 않습니다**: 닫혀도 요청을 끝까지 해야 지급 여부를 알고 `redeemed` 콜백으로 보상을 맞춥니다.
- **static 가변 상태 금지**(D2). 로거만 static 입니다.

## 4. 테스트

- Unity: EditMode `Geuneda.WebView.Tests.EditMode`(가짜 어댑터 `FakeWebViewFactory`, 지연·외부 열기·안전 영역·화면 크기는 internal 필드로 바꿈), `Geuneda.WebView.Notice.Tests.EditMode`(실제 `NoticeService` + 가짜 전송, PlayerPrefs 키 `geuneda.webview.tests.notice`), `Geuneda.WebView.Coupon.Tests.EditMode`(가짜 `ICouponService`). 선택 테스트 어셈블리도 notice·coupon 이 있을 때만 켜집니다.
- JS: 패키지 루트에서 `node --test "Web~/tests/*.test.mjs"`(Node 21 이상).
- 실제 웹뷰: `com.geuneda.webview.gree` 의 `GreeWebViewInteropTest`(macOS 에디터, `Web~` 를 정적 서버로).

## 5. 문서

- 공개 API, 명령 규약, JS 파일을 바꾸면 `README.md`, `CHANGELOG.md`, `package.json` 버전, JS 파일 머리의 버전, geuneda-frameworks 호스트 레포의 `claude-skills/geuneda-webview/`(SKILL.md, references/api.md)와 `claude-skills/geuneda-frameworks/` 를 함께 고치고 `claude-skills/install.sh geuneda-webview`, `claude-skills/install.sh geuneda-frameworks` 로 설치합니다.
- 어댑터 계약(`Runtime/Native/`)을 바꾸면 `com.geuneda.webview.gree` 를 함께 고칩니다.

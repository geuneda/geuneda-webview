# Changelog
이 패키지의 모든 주요 변경사항은 이 파일에 기록됩니다.

이 형식은 [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)를 기반으로 하며,
이 프로젝트는 [Semantic Versioning](http://semver.org/spec/v2.0.0.html)을 따릅니다.

## [0.2.0] - 2026-10-17

**Changed**:
- **호환되지 않는 변경**: `IWebViewService` 가 `IDisposable` 을 상속합니다. 이제 `MainInstaller.CleanDispose<IWebViewService>()` 로 열린 웹뷰를 닫고(열기 중이면 그 열기는 `Cancelled`) 바인딩을 지울 수 있습니다. 0.1.0 에서는 구현 클래스 `WebViewService` 만 `IDisposable` 이라 이 호출이 컴파일되지 않았습니다. `IWebViewService` 를 직접 구현한 클래스(테스트 가짜 포함)는 `Dispose()` 를 구현해야 합니다. `WebViewService.Dispose` 의 동작은 그대로입니다

**Fixed**:
- Unity 가 페이지에 보내는 스크립트(키·결과·이벤트)를 ASCII 로만 만듭니다(Newtonsoft `StringEscapeHandling.EscapeNonAscii`, ASCII 가 아닌 글자는 `\uXXXX`). 0.1.0 은 날것으로 보내서 짝 없는 서로게이트(UTF-16 길이로 자른 글 등)가 든 결과·이벤트가 gree iOS 에서 통째로 사라졌고(UTF-8 로 바꾸지 못해 `evaluateJavaScript` 가 실행되지 않음) 페이지의 `geuneda.call` 이 끝나지 않았습니다(iOS 18.6 시뮬레이터에서 재현, 고친 뒤 도착 확인). macOS 에디터(Mono)에서는 네이티브 호출의 문자열 변환이 `ArgumentException`(Illegal byte sequence)을 던져 결과를 잃었습니다(코드상 `Emit` 은 호출자에게 던짐). 이제 짝 없는 서로게이트·이모지·U+2028 도 코드 단위 그대로 페이지에 갑니다. 값 안의 `JRaw`(이스케이프 설정을 거치지 않음)도 ASCII 가 아닌 코드 단위를 `\uXXXX` 로 바꿔 쓰고, 주석 토큰은 쓰지 않습니다. 한글 같은 글자는 스크립트에서 글자당 6바이트가 됩니다
- `Web~/geuneda-webview.js`: `geuneda.call` 이 인자를 JSON 으로 만들 수 없거나(순환 참조, BigInt) 네이티브 전달 경로가 던지면 reject 하던 것을 고쳐, 그 요청만 `InvalidArguments`(12003)·`Unknown`(1) 결과로 끝냅니다. 키를 받기 전 대기열에 그런 요청이 있으면 뒤 요청까지 보내지 못하던 것도 고쳤습니다. 페이지 서버의 JS 파일을 이 버전으로 바꿉니다

**Docs**:
- Android 뒤로가기는 사용자 제스처 없이 스크립트로 옮긴 방문을 건너뜀, Android 의 허용 밖 form POST 는 닫기 전에 요청 본문이 그 서버에 도착함(키는 가지 않음), iOS 는 허용 밖 이동을 막고 웹뷰가 그대로 남음, Android `Screen.SetResolution` 직후 한 프레임의 여백 어긋남, 페이지가 보낸 짝 없는 서로게이트는 U+FFFD 로 받음
- `Web~/tests/interop.html`: Unity 가 보낸 결과·이벤트의 글(짝 없는 서로게이트·이모지·U+2028)을 코드 단위 16진수로 돌려주는 경우 추가(com.geuneda.webview.gree 0.2.0 의 `GreeWebViewInteropTest` 가 확인). Node 테스트에 직렬화 실패·전달 예외·대기열 경우 추가

## [0.1.0] - 2026-10-17

**New**:
- `IWebViewService`/`WebViewService`: 허용 출처의 페이지를 보이지 않게 만들어 로드하고, 페이지의 JS 파일과 연결되면(준비 알림 → 메인 프레임에 키 전달 → 키로 응답) 보이게 하며 성공(연결 실패·브리지 없는 HTTP 오류 페이지는 `LoadFailed`, 제한 시간 `Timeout`, 취소·열기 중 닫기 `Cancelled`, 중복 열기 `InProgress`). RectTransform 영역(없으면 안전 영역)을 매 프레임 따라가고(여백이나 화면 크기가 바뀔 때 적용) 영역이 파괴되면 닫음. `HandleBack`(페이지 기록 → 닫기), `Close`/`Closed`, `AddCommand`(등록 해제는 `Dispose`), `Emit`(페이지 이벤트)
- 명령 규약: 페이지 → Unity `{"type":"ready"|"bound"|"call","doc","key","id","command","args"}`, Unity → 페이지 `window.geuneda.__receive({"type":"bind","doc","key"})`·`{"type":"result","doc","id","ok","code","message","value"}`·`{"type":"event","name","value"}`. 키가 맞는 글만 받고(다른 출처 iframe 차단), 결과는 보낸 문서에만, 페이지로 가는 스크립트는 실행 순간의 메인 프레임 출처가 허용 출처일 때만 전달. 기본 명령 `close`, `openUrl`. 처리기의 취소·제한 시간 예외는 `Cancelled`·`Timeout`, 그 밖의 예외는 `Unknown` 으로 답하고 Error 로그, 닫히면 처리기 토큰 취소·결과 버림
- 보안: 허용 출처 밖 주소는 열지 않고(예외), 네이티브 이동 제한 정규식(`DenyUrlPattern`, Java·ICU·.NET 공통 문법)으로 이동을 막으며, 지금 페이지가 허용 출처가 아니면 명령·이벤트를 버리며, 이동 제한을 비껴간 허용 밖 이동이 시작되면(Android POST) 웹뷰를 닫음. 페이지에 토큰을 넘기지 않음
- `WebViewResultCodes`(12000 블록), 어댑터 계약 `IWebViewFactory`·`IWebViewNative`·`IWebViewNativeListener`·`WebViewNativeSettings`·`WebViewMargins`
- `Geuneda.WebView.Notice`(com.geuneda.notice 가 있을 때): `notice.list`·`notice.read`·`notice.refresh`, 이벤트 `notice.changed`
- `Geuneda.WebView.Coupon`(com.geuneda.coupon 이 있을 때): `coupon.redeem`(닫혀도 요청을 끝까지 하고 `redeemed` 콜백)
- `Web~/geuneda-webview.js`(Promise 명령, 문서 ID·키 연결, 키를 받기 전 명령은 대기, 이벤트, 다른 출처 링크는 외부 브라우저, iOS·macOS 는 URL 인코딩해 보냄), 샘플 `notice.html`·`coupon.html`, Node 테스트와 상호 운용 테스트 페이지

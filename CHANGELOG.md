# Changelog
이 패키지의 모든 주요 변경사항은 이 파일에 기록됩니다.

이 형식은 [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)를 기반으로 하며,
이 프로젝트는 [Semantic Versioning](http://semver.org/spec/v2.0.0.html)을 따릅니다.

## [0.1.0] - 2026-10-17

**New**:
- `IWebViewService`/`WebViewService`: 허용 출처의 페이지를 보이지 않게 만들어 로드하고, 페이지의 JS 파일과 연결되면(준비 알림 → 메인 프레임에 키 전달 → 키로 응답) 보이게 하며 성공(연결 실패·브리지 없는 HTTP 오류 페이지는 `LoadFailed`, 제한 시간 `Timeout`, 취소·열기 중 닫기 `Cancelled`, 중복 열기 `InProgress`). RectTransform 영역(없으면 안전 영역)을 매 프레임 따라가고(여백이나 화면 크기가 바뀔 때 적용) 영역이 파괴되면 닫음. `HandleBack`(페이지 기록 → 닫기), `Close`/`Closed`, `AddCommand`(등록 해제는 `Dispose`), `Emit`(페이지 이벤트)
- 명령 규약: 페이지 → Unity `{"type":"ready"|"bound"|"call","doc","key","id","command","args"}`, Unity → 페이지 `window.geuneda.__receive({"type":"bind","doc","key"})`·`{"type":"result","doc","id","ok","code","message","value"}`·`{"type":"event","name","value"}`. 키가 맞는 글만 받고(다른 출처 iframe 차단), 결과는 보낸 문서에만, 페이지로 가는 스크립트는 실행 순간의 메인 프레임 출처가 허용 출처일 때만 전달. 기본 명령 `close`, `openUrl`. 처리기의 취소·제한 시간 예외는 `Cancelled`·`Timeout`, 그 밖의 예외는 `Unknown` 으로 답하고 Error 로그, 닫히면 처리기 토큰 취소·결과 버림
- 보안: 허용 출처 밖 주소는 열지 않고(예외), 네이티브 이동 제한 정규식(`DenyUrlPattern`, Java·ICU·.NET 공통 문법)으로 이동을 막으며, 지금 페이지가 허용 출처가 아니면 명령·이벤트를 버리며, 이동 제한을 비껴간 허용 밖 이동이 시작되면(Android POST) 웹뷰를 닫음. 페이지에 토큰을 넘기지 않음
- `WebViewResultCodes`(12000 블록), 어댑터 계약 `IWebViewFactory`·`IWebViewNative`·`IWebViewNativeListener`·`WebViewNativeSettings`·`WebViewMargins`
- `Geuneda.WebView.Notice`(com.geuneda.notice 가 있을 때): `notice.list`·`notice.read`·`notice.refresh`, 이벤트 `notice.changed`
- `Geuneda.WebView.Coupon`(com.geuneda.coupon 이 있을 때): `coupon.redeem`(닫혀도 요청을 끝까지 하고 `redeemed` 콜백)
- `Web~/geuneda-webview.js`(Promise 명령, 문서 ID·키 연결, 키를 받기 전 명령은 대기, 이벤트, 다른 출처 링크는 외부 브라우저, iOS·macOS 는 URL 인코딩해 보냄), 샘플 `notice.html`·`coupon.html`, Node 테스트와 상호 운용 테스트 페이지

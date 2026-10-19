# Android 사용 준비와 확인 항목

[README로 돌아가기](../README.md)

## 현재 상태

2026-10-19에 Android 로그인·과제 조회를 위한 플러그인과 앱 코드를 추가했습니다. Windows 에디터 동작, Android 조건의 C# 소스 컴파일, Android JavaScript 통신 테스트와 Manifest 변환을 확인했습니다. Android 기기에서 설치·실행하거나 APK를 검증한 상태는 아닙니다.

현재 프로젝트의 Android 최소 API는 25이며 ARMv7·ARM64, IL2CPP로 설정했습니다. 지원 OS에서도 JavaScript 기능과 학교 로그인 호환성을 위해 Android System WebView를 최신 상태로 사용해야 합니다. Android에서는 Windows용 WebView2나 PC의 Node.js 서버가 필요하지 않습니다.

## 추가한 파일

| 파일 | 역할 |
| --- | --- |
| `Assets/Plugins/UnityWebView/Android/WebViewPlugin.aar` | 공식 `gree/unity-webview` Android 네이티브 WebView. Android에만 포함 |
| `Assets/Plugins/UnityWebView/link.xml` | 네이티브 연결에서 사용하는 WebView C# 콜백의 코드 제거 방지 |
| `Assets/Notifications/Editor/AndroidCampusSetup.cs` | 생성된 Android Manifest에 네트워크 권한·하드웨어 가속·키보드 설정 반영 |
| `SmartleadCapture.txt`, `SmartleadCoursework.txt` | Android의 `window.Unity.call`과 Windows의 `window.chrome.webview.postMessage` 지원 |
| `SmartleadWebView.cs` | 뒤로가기, 앱 복귀 시 갱신, 연결 끊김 안내·복구와 Android 쿠키 저장 |
| `PixelPlayerMovement.cs` | 모바일에서 접어둘 수 있는 방향 이동 패드 |

플러그인은 현재 C# 래퍼와 같은 공식 커밋 `5604a7dc1beba961db00c84f0f68c979bf23092e`의 `nofragment/release` 파일을 이름만 바꿔 추가했습니다. 바이너리를 수정하지 않았으며, 출처·SHA256은 [PROVENANCE.txt](../Assets/Plugins/UnityWebView/PROVENANCE.txt), 라이선스는 [LICENSE.txt](../Assets/Plugins/UnityWebView/LICENSE.txt)에 있습니다.

이 버전은 로그인·조회용으로 사용합니다. 학교 과제의 **파일 첨부·업로드 입력창은 지원하지 않습니다.** 과제 제출이 필요하면 학교 공식 웹사이트나 학교가 제공하는 앱을 사용합니다.

## 앱 사용 방법

1. 인터넷에 연결한 상태에서 앱을 열고 SmartLEAD에 로그인합니다. PC에서 로그인했어도 Android 앱에서는 처음에 별도로 로그인해야 합니다.
2. 과목 카드나 일정을 터치하면 상세 목록이 열립니다. 패널 상단 또는 목록 맨 위에서 아래로 밀면 닫힙니다.
3. Android 뒤로가기는 알림 패널이 열려 있으면 패널을 닫고, 로그인된 학교 화면이 열려 있으면 캠퍼스로 돌아옵니다. 로그인 화면에서는 가능한 경우 이전 페이지로 돌아갑니다.
4. 캠퍼스에서 뒤로가기를 누르면 앱을 백그라운드로 보냅니다. 다른 앱에서 돌아오면 로그인된 페이지를 갱신합니다. 입력 중인 로그인 화면은 자동 새로고침하지 않습니다.
5. 오른쪽 아래 **이동**을 누르면 방향 패드가 열립니다. 화살표를 누르고 있는 동안 캐릭터가 움직이며 **이동 닫기**로 패드를 접습니다. 알림 패널이나 학교 페이지가 열린 동안 이동 입력은 멈춥니다.

로그인 쿠키는 WebView의 쿠키 저장소를 사용합니다. 학교가 세션을 만료시키면 다시 로그인해야 하며, 프로젝트는 학교 비밀번호를 별도로 저장하지 않습니다.

## 권한과 네트워크

생성된 Manifest에는 `INTERNET`과 `ACCESS_NETWORK_STATE`를 포함합니다. 이 기능을 위해 카메라·마이크·외부 저장소·알림 표시 권한을 추가하지 않습니다. 홈 화면 위젯·푸시 알림·앱 종료 상태의 자동 조회는 아직 구현하지 않았습니다.

연결이 끊기면 기존에 조회한 내용을 유지하면서 안내합니다. 연결이 복구된 뒤 로그인된 화면은 다시 조회하고, 인증 화면은 입력을 보존해 사용자가 이어서 로그인하거나 **재접속**할 수 있게 합니다. 인터넷 연결 상태 표시는 기기 연결 여부이며, 학교 서버의 응답 성공을 보장하지는 않습니다.

앱이 백그라운드에 있는 동안 과제·알림을 계속 조회하는 방식은 아닙니다. 앱에 돌아왔을 때 다시 확인합니다.

## 확인한 내용

- Android용 공식 AAR의 출처·해시, Java 브리지 클래스와 포함된 ProGuard 보존 규칙 확인.
- Android 플랫폼 조건의 WebView 래퍼와 앱 C# 코드 컴파일.
- Android 브리지로 과목·알림·과제·마감일이 Windows와 같은 형식으로 전달되는 자동 테스트.
- 두 브리지가 함께 존재할 때 메시지를 중복 전달하지 않는 테스트.
- 요청 식별 값·페이지 정보를 유지하고 비로그인 페이지에서 수집하지 않는 테스트.
- Manifest 처리의 중복 권한 방지, 네트워크 권한, 키보드·하드웨어 가속 설정과 기존 Activity 보존 확인.

## 추후 Android 기기에서 확인할 항목

- [ ] 첫 로그인과 앱 재실행 후 세션 유지, 학교 로그인 만료 처리.
- [ ] 키보드 표시·숨김 시 입력창 가림, 자동 완성·붙여넣기와 뒤로가기.
- [ ] 실제 과목·과제·알림 수집, 제출 완료 제외와 한국 시간 마감일 표시.
- [ ] 목록 스크롤과 아래로 스와이프 닫기, 닫은 뒤 뒤쪽 과목이 눌리지 않는지 확인.
- [ ] 과목 선택과 방향 패드 입력이 충돌하지 않는지 확인.
- [ ] 홈 화면·다른 앱·화면 꺼짐 이후 복귀, Wi-Fi·모바일 데이터 전환과 끊김 복구.
- [ ] 노치·상태 표시줄·제스처 내비게이션 영역에서 버튼과 글자가 잘리지 않는지 확인.

실제 문제가 발생하면 기기 모델·Android 버전·WebView 버전·재현 순서와 오류 문구를 [문제 해결 기록](TROUBLESHOOTING.ko.md)에 추가합니다. 비밀번호·쿠키·세션 키와 개인 알림 원문은 기록에서 제외합니다.

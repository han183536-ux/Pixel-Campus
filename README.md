# Pixel-Campus

Unity에서 SmartLEAD 알림을 과목별로 감지하고, 과목마다 색이 다른 큐브로 표시하는 프로젝트입니다. 읽은 알림 기록도 표시하며 과목명, 과목·분반 코드, 조회한 알림 건수를 함께 보여줍니다.

## 실행

1. Unity Hub에서 **Unity 2022.3.62f3**로 이 프로젝트를 엽니다.
2. Windows에 WebView2 Runtime이 설치되어 있어야 합니다.
3. `Assets/Scenes/NotificationDemo.unity`를 열고 Play를 누릅니다.
4. 학교 로그인 페이지에서 직접 로그인합니다. 필요하면 **학교 로그인 / 계정 확인** 버튼을 누릅니다.
5. **학교 알림 기록 보기**로 학교 목록과 결과를 비교할 수 있습니다. 학교 화면을 닫으면 오른쪽에 과목별 큐브가 표시됩니다.

현재 WebView 연결은 Windows x64를 대상으로 합니다. 학교 계정 비밀번호는 프로젝트에 저장하지 않습니다. 자세한 조회 범위와 제한은 [WEBVIEW-SETUP.ko.md](WEBVIEW-SETUP.ko.md)를 참고하세요.

## 파일 구성

- `Assets/`: 스크립트, 씬, 리소스, 플러그인과 `.meta` 파일
- `Packages/`: Unity 패키지 목록과 잠금 파일
- `ProjectSettings/`: Unity 프로젝트 설정
- `NotificationServer/`: 선택적으로 사용하는 로컬 서버와 테스트
- `BrowserBridge/`: 브라우저 확장 기반의 보조 연결 코드

`Library`, `Temp`, 로그, 빌드 결과물과 개인 계정의 알림 데이터는 Git에서 제외합니다. 옆에 있는 별도 CLI 비교 프로젝트도 이 저장소에는 포함하지 않습니다. Unity에서 `Library` 등은 프로젝트를 열 때 다시 생성합니다.

## 변경 사항 저장하기

Unity에서 씬과 에셋을 저장한 뒤 프로젝트 폴더의 PowerShell에서 실행합니다.

```powershell
git status
git add Assets Packages ProjectSettings NotificationServer BrowserBridge README.md WEBVIEW-SETUP.ko.md .gitignore .gitattributes
git commit -m "Describe the change"
git push
```

작업을 시작하기 전에는 `git pull --ff-only`로 최신 변경을 받습니다. Unity의 `.meta` 파일은 에셋과 함께 커밋해야 씬 참조가 유지됩니다. 변경 목적마다 커밋을 나누면 이전 상태를 찾기 쉽습니다.

## 새 컴퓨터에서 받기

```powershell
git clone https://github.com/han183536-ux/Pixel-Campus.git
```

Unity Hub에서 복제한 폴더를 추가하고 지정한 Unity 버전으로 엽니다. 개인 계정의 로그인 상태는 새 컴퓨터에서 다시 확인해야 합니다.

## 로컬 서버 테스트

Node.js 20 이상에서 실행합니다. 현재 WebView 알림 조회 모드에는 이 서버가 필수는 아닙니다.

```powershell
cd NotificationServer
node --test
```

서버 연결 방식은 [NotificationServer/README.ko.md](NotificationServer/README.ko.md)를 참고하세요. 파일 기반 연결에는 `smartlead.notifications.example.json`을 `smartlead.notifications.json`으로 복사한 뒤 사용합니다. 실제 알림 파일은 Git에서 제외됩니다.

## 라이선스와 기획

라이선스와 기획 내용은 추후 추가합니다. 포함된 UnityWebView 플러그인의 라이선스와 출처는 `Assets/Plugins/UnityWebView/`에서 확인할 수 있습니다.

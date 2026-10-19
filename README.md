# Pixel-Campus

Unity에서 SmartLEAD 알림을 과목별로 감지하고, 과목마다 색이 다른 큐브로 표시하는 프로젝트입니다. 읽은 알림 기록도 표시하며 과목명, 과목·분반 코드, 조회한 알림 건수를 함께 보여줍니다.

## 실행

1. Unity Hub에서 **Unity 6.3 LTS (6000.3.26f1)**로 이 프로젝트를 엽니다.
2. Windows에 WebView2 Runtime이 설치되어 있어야 합니다.
3. 프로젝트를 열면 Game 뷰에 540×960(9:16) 세로 미리보기가 자동 적용됩니다. Android로 빌드 대상을 바꿔도 같은 비율을 적용합니다. `Tools > Pixel Campus > Open Portrait Demo`로 데모 씬을 열고 Play를 누릅니다. 비율을 다시 적용하려면 `Tools > Pixel Campus > Use Portrait Screen`을 선택합니다.
4. 첫 화면의 SmartLEAD 페이지에서 직접 로그인합니다. 로그인된 계정이 확인되면 알림 건수와 관계없이 픽셀 캠퍼스로 자동 전환됩니다. 유효한 학교 세션이 남아 있으면 확인 후 바로 전환될 수 있습니다.
5. 픽셀 캠퍼스 상단의 **알림** 버튼으로 목록을 열고, **계정** 버튼으로 학교 화면을 다시 열 수 있습니다. 목록 안의 **학교 알림 기록 보기**로 학교 기록과 비교한 뒤 **캠퍼스** 버튼으로 돌아옵니다.
6. 과목 큐브나 과목명을 PC에서는 좌클릭, 모바일에서는 터치하면 해당 과목의 과제·알림·쪽지 목록이 열리고 미제출 과제를 우선 선택합니다. 다른 항목을 누르면 본문을 펼칠 수 있습니다. 원문 링크가 제공된 항목은 **학교에서 원문 보기**로 이동할 수 있으며, **닫기**로 캠퍼스에 돌아옵니다.
7. 전체 수강 과목을 작은 카드로 표시하며, 화면을 넘는 과목은 마우스 휠 또는 터치 스크롤로 확인합니다. 미제출 과제가 있는 과목과 과제를 마감순으로 먼저 표시합니다. 과제 상세에는 마감일과 남은 시간이 표시되며 제출 완료 과제와 읽은 알림은 기본 목록에서 제외합니다. 알림의 마감일은 제공된 데이터나 연결된 과제에서 확인될 때만 표시합니다. 확인된 알림과 미제출 과제가 없으면 **알림 없음**, 수집에 실패하면 **과제 확인 필요**로 구분합니다.

과제 수집은 로그인된 세션에서 Moodle 과목·과제·제출 상태 API를 먼저 시도하고, 허용되지 않으면 수강 목록과 과제 페이지를 읽습니다. 개인 연장 기한을 반영하며 제출 상태를 확인할 수 없는 항목은 숨기지 않습니다. 전체 과목 API가 차단되고 대시보드만 읽을 수 있으면 **전체 과목 확인 필요**를 표시합니다. 학교 데이터는 실행 중 2분 간격 또는 **다시 확인**으로 갱신하며, 남은 시간은 기기의 현재 시각으로 계속 계산합니다. 실제 SmartLEAD 버전의 화면 구조와 API 허용 여부는 로그인된 기기에서 확인해야 합니다.

캠퍼스의 큐브 아래 **남은 과제 일정**에서 과목명·과제명·마감 시각·남은 시간을 바로 확인합니다. 학교 캘린더도 독립적으로 조회하므로 과목 목록이나 과제 API가 차단되어도 조회 가능한 마감 일정부터 표시합니다. 제출 완료 여부를 확인하지 못한 일정에는 **제출 확인 필요**를 표시합니다. 조회가 45초 이상 지연되면 대기 상태 대신 안내와 **일정 다시 확인 / 학교 일정 보기** 버튼을 제공합니다.

SmartLEAD의 Coursemos 화면은 `.course-info-container`와 `[role="main"]`에서 과목·과제 목록을 읽고, 과제 표의 **종료 일시 / 제출** 열과 상세 화면의 **제출 여부**를 사용합니다. `YYYY-MM-DD HH:mm` 날짜는 한국 시간으로 해석합니다. 앞으로 마감되는 과제를 먼저 정렬하고, 현재 수강 목록에 없는 지난 학기 캘린더의 마감된 과제는 제외합니다.

현재 PC 개발용 WebView 연결은 Windows x64를 대상으로 합니다. Android 실기기 출시에는 Android용 네이티브 WebView 플러그인과 터치 이동 입력을 별도로 구성해야 합니다. 세로 화면, 안전 영역과 앱 화면 전환은 공통 코드로 구현되어 있습니다. 학교 계정 비밀번호는 프로젝트에 저장하지 않습니다. 자세한 조회 범위와 제한은 [WEBVIEW-SETUP.ko.md](WEBVIEW-SETUP.ko.md)를 참고하세요.

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


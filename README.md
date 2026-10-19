# Pixel-Campus

Unity에서 한림 SmartLEAD의 **과목별 알림·쪽지와 과제 일정**을 확인하는 픽셀 캠퍼스입니다. 과목마다 색이 다른 3D 큐브를 표시하고, 미제출 과제의 마감일과 남은 시간을 캠퍼스 화면에서 바로 확인할 수 있습니다.

## 최근 추가·개선 사항

- **모바일 세로 화면:** 에디터 Game 뷰에 540×960(9:16) 미리보기를 자동 적용합니다. Android로 빌드 대상을 바꿔도 같은 비율을 선택하며, 안전 영역을 반영합니다.
- **로그인 세션 유지:** 최초 실행과 재접속 시 기존 학교 세션부터 확인합니다. 로그인된 상태에서 로그인 페이지를 다시 열어 로그아웃을 요구하던 흐름을 수정했습니다.
- **과목 선택과 상세 보기:** PC에서는 큐브·과목명을 좌클릭하고, 모바일에서는 터치해 해당 과목의 과제·알림·쪽지 상세 목록을 엽니다.
- **알림 패널 닫기:** 패널 상단·목록 맨 위에서 아래로 스와이프하면 캠퍼스로 돌아옵니다. PC에서는 마우스 좌클릭 드래그로도 같은 동작을 할 수 있습니다. 목록 중간에서 시작한 드래그는 스크롤로 처리합니다.
- **과제 수집:** 수강 과목과 과제명, 제출 여부, 마감일을 가져옵니다. 제출 완료 과제는 기본 목록에서 제외하며, 확인 가능한 개인 연장 기한을 반영합니다.
- **과제 우선 표시:** 미제출 과제가 있는 과목을 먼저 배치합니다. 앞으로 마감되는 과제를 가까운 순으로 표시하고, 마감이 지난 과제와 마감 정보가 없는 과제는 뒤에 표시합니다.
- **남은 과제 일정:** 큐브 아래에 과목명·과제명·마감 시각·남은 시간을 표시합니다. 일정을 선택하면 해당 항목의 상세 내용을 엽니다.
- **작은 과목 카드와 스크롤:** 과목 목록을 알림 유무와 별도로 수집합니다. 알림이 없는 과목도 표시하며, 화면을 넘는 과목과 일정은 마우스 휠이나 터치 스크롤로 확인합니다.
- **빈 목록과 조회 실패 구분:** 확인된 알림과 미제출 과제가 없으면 **알림 없음**, 조회 실패나 미확인 상태는 **과제 확인 필요 / 제출 확인 필요**로 구분합니다.
- **실제 SmartLEAD 화면 대응:** Coursemos의 과목 목록과 과제 표에 맞춰 수집 경로를 수정했습니다. 과목 API와 `#region-main`에만 의존해 과제를 가져오지 못하던 문제를 해결했습니다.
- **Android 연결 준비:** 공식 Android WebView 플러그인과 `Unity.call` 통신 경로를 추가했습니다. 뒤로가기, 앱 복귀·네트워크 복구 시 갱신, 접을 수 있는 터치 이동 패드를 지원합니다. Android 실기기 동작은 아직 검증하지 않았습니다.

## 실행

1. Unity Hub에서 **Unity 6.3 LTS (6000.3.26f1)**로 이 프로젝트를 엽니다.
2. Windows에 WebView2 Runtime이 설치되어 있어야 합니다.
3. 프로젝트를 열면 Game 뷰에 540×960(9:16) 세로 미리보기가 자동 적용됩니다. Android로 빌드 대상을 바꿔도 같은 비율을 적용합니다. `Tools > Pixel Campus > Open Portrait Demo`로 데모 씬을 열고 Play를 누릅니다. 비율을 다시 적용하려면 `Tools > Pixel Campus > Use Portrait Screen`을 선택합니다.
4. 유효한 학교 세션이 남아 있으면 확인 후 바로 캠퍼스로 이동합니다. 세션이 없거나 만료됐으면 표시되는 SmartLEAD 페이지에서 직접 로그인합니다.
5. 캠퍼스의 과목 큐브와 **남은 과제 일정**을 확인합니다. 큐브·과목명·일정을 좌클릭하거나 터치하면 상세 목록이 열립니다.
6. 상세 목록의 항목을 선택해 본문을 펼칩니다. **전체 알림 보기**로 전체 목록을 확인할 수 있습니다. 원문 링크가 제공된 항목은 **학교에서 원문 보기**로 이동할 수 있으며, 패널을 아래로 스와이프해 캠퍼스에 돌아옵니다.
7. **일정 다시 확인**으로 즉시 갱신하거나 **학교 일정 보기**로 학교 캘린더를 엽니다. **계정** 버튼으로 학교 계정 화면을 확인하고, 학교 화면 상단의 **캠퍼스** 버튼으로 돌아옵니다.

## 표시 정보와 갱신

| 화면 | 표시 내용 |
| --- | --- |
| 과목 카드 | 과목명·과목 코드, 미제출 과제 수, 가까운 과제명과 남은 시간, 알림 건수 |
| 남은 과제 일정 | 과목명·과제명, 한국 시간 기준 마감일, 남은 시간 또는 마감 경과 시간 |
| 상세 목록 | 미제출 과제 우선, 읽지 않았거나 읽음 여부가 미확인인 알림·쪽지, 본문과 제공된 원문 링크 |
| 확인된 빈 목록 | **알림 없음 / 남은 과제 없음** |
| 조회 실패·불확실한 상태 | **과제 확인 필요 / 제출 확인 필요 / 전체 과목 확인 필요** |

학교 데이터는 앱 실행 중 **2분 간격**으로 갱신하며 수동으로도 다시 확인할 수 있습니다. 남은 시간은 기기의 현재 시각으로 계속 계산합니다. 일반 알림은 제공된 마감 정보나 연결된 과제의 마감일을 확인할 수 있을 때만 남은 시간을 표시합니다.

## SmartLEAD 수집 방식

로그인된 WebView 세션에서 Moodle 과목·과제·제출 상태 API를 먼저 시도하고, 제공되지 않으면 학교 대시보드와 과제 페이지를 읽습니다. 현재 SmartLEAD의 Coursemos 화면에서는 다음 구조를 지원합니다.

- `.course-info-container`와 `[role="main"]`의 과목·과제 목록
- 과제 표의 **종료 일시 / 제출** 열과 상세 화면의 **제출 여부**
- 한국 시간 기준 `YYYY-MM-DD HH:mm` 및 한국어 날짜 표기

학교 캘린더도 별도로 조회하므로 과목·과제 API가 차단돼도 조회 가능한 마감 일정부터 표시합니다. 캘린더 일정의 제출 완료 여부를 확인하지 못하면 **제출 확인 필요**를 표시합니다. 제출 완료 과제는 연결된 캘린더 일정과 중복 표시하지 않으며, 현재 수강 목록에 없는 지난 학기의 마감된 캘린더 과제는 제외합니다.

과목 목록의 전체 범위를 보장할 수 없는 대체 조회는 **전체 과목 확인 필요**로 표시합니다. 조회 실패를 과제 0건으로 처리하지 않으며, 45초 이상 지연되면 계속 대기하는 대신 안내를 표시합니다. 알림 기록은 학교에서 다시 확인할 수 있도록 **학교 알림 기록 보기**를 제공합니다.

## 사용한 방법

| 영역 | 구현 방법 | 주요 파일 |
| --- | --- | --- |
| 세로 화면 | Game 뷰의 현재 빌드 대상 그룹에 540×960 미리보기 선택, 안전 영역과 화면 크기 반영 | `PortraitCampusSetup.cs`, `CampusScreenLayout.cs` |
| 학교 로그인 | UnityWebView·WebView2의 로그인 세션 재사용, 알림 페이지 진입 후 계정 상태 확인 | `SmartleadWebView.cs` |
| 알림 수집 | 학교 알림 목록·기록 페이지 조회, Moodle 보조 API, 로그인 여부·출처·요청 식별 값 검증 | `SmartleadCapture.txt` |
| 과제 수집 | Moodle API 시도 후 실제 Coursemos 대시보드·과제 표 조회, 캘린더 별도 수집 | `SmartleadCoursework.txt` |
| 과목·일정 표시 | 과목 식별 정보로 큐브 구성, 클릭·터치로 상세 열기, 제출 상태 필터링과 마감 정렬 | `SmartleadCourseCubes.cs`, `NotificationDetailUI.cs`, `SmartleadWebView.cs` |
| 남은 시간 | 마감 시각을 Unix 시간으로 변환하고 현재 시각과 비교, 한국 시간으로 마감일 표시 | `SmartleadCoursework.txt`, `CampusScreenLayout.cs` |
| 검증 | Node.js 수집 테스트, Unity 컴파일, 로그인된 실제 WebView와 Game 뷰 확인 | `NotificationServer/capture.test.mjs`, `NotificationServer/coursework.test.mjs` |

## 오류 메시지와 해결 기록

다음은 개발 중 확인한 오류와 화면 증상입니다. 발생 조건, 원인, 수정 파일, 검증 방법과 새 기록 양식은 [문제 해결 기록](docs/TROUBLESHOOTING.ko.md)에 정리했습니다. 화면의 안내 문구와 실제 예외·서버 오류는 구분해 기록합니다.

| 오류 메시지 또는 증상 | 원인과 처리 | 상세 |
| --- | --- | --- |
| `NullReferenceException: Object reference not set to an instance of an object` | Unity에 없는 `GetCurrentGroup()`을 리플렉션으로 호출함. `currentGroup` 속성으로 수정 | [PC-001](docs/TROUBLESHOOTING.ko.md#pc-001) |
| 재접속 시 이미 로그인됐다는 확인창에서 로그아웃을 요구 | 세션이 남아 있는데 로그인 페이지를 직접 열었음. 알림 페이지에서 기존 세션 확인 | [PC-002](docs/TROUBLESHOOTING.ko.md#pc-002) |
| `과제 확인 중…`이 계속 표시됨 | 조회 실패가 알림에서 생성된 과목 카드에 전달되지 않음. 실패·완료 상태와 시간 초과 처리 보완 | [PC-003](docs/TROUBLESHOOTING.ko.md#pc-003) |
| `수강 과목 조회 실패 · 학교에서 다시 확인해 주세요.` | 과목 API와 HTML 선택자가 실제 학교 환경과 맞지 않음. 대시보드·과제 표 구조에 맞춰 수정 | [PC-004](docs/TROUBLESHOOTING.ko.md#pc-004) |
| `데이터베이스 테이블 external_functions 에서 데이터 레코드를 찾지 못 함` | 요청한 과목 API 함수가 학교 서버에 등록되지 않음. API 실패 시 HTML 조회 사용 | [PC-004](docs/TROUBLESHOOTING.ko.md#pc-004) |
| 과제를 찾았지만 마감일·제출 여부를 읽지 못함 | 숫자 날짜와 `제출 여부` 항목을 지원하지 않았음. 실제 표기 형식 추가 | [PC-005](docs/TROUBLESHOOTING.ko.md#pc-005) |

기존 연결 문서의 AJAX 헤더 문제와 개발 검증 중의 `NETSDK1004`도 상세 기록에 포함했습니다. GitHub에는 비밀번호·쿠키·세션 키·개인 알림 원문 대신 재현에 필요한 오류 문구와 익명화한 예시를 기록합니다.

## 지원 환경과 개발 범위

- **현재 검증 환경:** Windows x64, Unity 6.3 LTS 에디터, WebView2 Runtime.
- **Android 구현:** 공식 WebView AAR, 세로 화면·안전 영역, 알림·과제 수신, 터치 스크롤·스와이프 닫기, 뒤로가기, 앱 복귀 시 갱신과 터치 이동 패드를 추가했습니다. ARMv7·ARM64와 IL2CPP를 설정했습니다. Android 조건의 소스 컴파일과 통신 테스트는 통과했으며 실기기 검증은 남아 있습니다.
- **위젯·백그라운드 알림:** 홈 화면 위젯, 푸시 알림과 앱 종료 상태의 자동 조회는 아직 구현하지 않았습니다.
- **로그인 정보:** 학교 페이지에서 직접 로그인하며, 학교 계정 비밀번호를 프로젝트에 저장하지 않습니다.

알림 연결의 세부 설정은 [WEBVIEW-SETUP.ko.md](WEBVIEW-SETUP.ko.md)를 참고하세요.

Android에 추가한 파일, 권한, 조작 방법과 추후 기기에서 확인할 항목은 [Android 사용 안내](docs/ANDROID.ko.md)에 정리했습니다.

## 검증

2026-10-19 기준으로 Windows 에디터의 실제 로그인된 SmartLEAD 세션에서 수강 과목·과제·제출 여부·마감일 수집과 Game 뷰의 일정 표시를 확인했습니다. Android·Windows 통신 경로를 포함한 관련 수집 테스트 **23개**를 통과했습니다. Android 실기기 로그인·키보드·뒤로가기는 별도 검증이 필요합니다.

```powershell
cd NotificationServer
node --test capture.test.mjs coursework.test.mjs
```

테스트에는 로그인 만료, 읽은 알림 기록, 과목 목록 대체 조회, Coursemos 과제 표, 마감일과 연장 기한, 캘린더 대체 조회, 응답 시간 초과 처리가 포함됩니다.

## 파일 구성

- `Assets/`: 스크립트, 씬, 리소스, 플러그인과 `.meta` 파일
- `Packages/`: Unity 패키지 목록과 잠금 파일
- `ProjectSettings/`: Unity 프로젝트 설정
- `NotificationServer/`: 선택적으로 사용하는 로컬 서버와 테스트
- `BrowserBridge/`: 브라우저 확장 기반의 보조 연결 코드
- `docs/TROUBLESHOOTING.ko.md`: 사용한 방법, 오류 원인·해결·검증 내역과 추가 기록 양식
- `docs/ANDROID.ko.md`: Android용 파일·권한·조작 방법과 실기기 확인 항목

`Library`, `Temp`, 로그, 빌드 결과물과 개인 계정의 알림 데이터는 Git에서 제외합니다. 옆에 있는 별도 CLI 비교 프로젝트도 이 저장소에는 포함하지 않습니다. Unity에서 `Library` 등은 프로젝트를 열 때 다시 생성합니다.

## 변경 사항 저장하기

Unity에서 씬과 에셋을 저장한 뒤 프로젝트 폴더의 PowerShell에서 실행합니다.

```powershell
git status
git add Assets Packages ProjectSettings NotificationServer BrowserBridge docs README.md WEBVIEW-SETUP.ko.md .gitignore .gitattributes
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


# 구현 방법과 문제 해결 기록

[README로 돌아가기](../README.md)

이 문서는 Pixel-Campus의 구현 방법과 개발 중 확인한 오류를 함께 기록합니다. PC-001~PC-005와 PC-007은 2026-10-19 작업 중 확인한 내용이며, PC-006은 기존 연결 문서에서 가져온 기록입니다. 오류가 최초로 발생한 시각이나 원문이 남아 있지 않은 부분은 추정해 채우지 않습니다.

## 구현 흐름

1. `PortraitCampusSetup`에서 에디터의 현재 빌드 대상에 맞는 9:16 Game 뷰를 선택합니다. `CampusScreenLayout`은 안전 영역과 카메라·콘텐츠 영역을 계산합니다.
2. `SmartleadWebView`가 학교 알림 페이지를 열어 기존 로그인 세션을 확인합니다. 인증이 필요하면 학교 로그인 화면을 표시하며, 계정이 확인되면 캠퍼스로 전환합니다.
3. `SmartleadCapture.txt`가 학교 알림 목록과 기록을 읽습니다. 요청 식별 값(nonce), 현재 페이지와 학교 출처가 맞는 응답만 Unity에서 받아들입니다.
4. `SmartleadCoursework.txt`는 과목·과제 API를 시도하고, 사용할 수 없으면 대시보드와 과제 표를 읽습니다. 캘린더 조회는 별도로 실행해 확인되는 마감 일정부터 전달합니다.
5. 과제 표의 제출 여부와 마감일을 기준으로 완료 과제를 제외합니다. 한국 시간의 마감일을 Unix 시간으로 바꾸고 현재 시각과 비교해 남은 시간·마감 경과 시간을 계산합니다.
6. 과목 카드와 일정 목록을 표시합니다. 미제출 과제를 우선하며, 앞으로 마감되는 과제, 마감된 과제, 마감 정보가 없는 과제 순으로 정렬합니다.

주요 소스:

- [에디터 세로 화면 설정](../Assets/Notifications/Editor/PortraitCampusSetup.cs)
- [화면 영역·시간 계산](../Assets/Notifications/Runtime/CampusScreenLayout.cs)
- [WebView 연결·응답 처리·상세 일정](../Assets/Notifications/Runtime/SmartleadWebView.cs)
- [알림 수집](../Assets/Notifications/Resources/SmartleadCapture.txt)
- [과목·과제 수집](../Assets/Notifications/Resources/SmartleadCoursework.txt)
- [과목 큐브와 입력](../Assets/Notifications/Runtime/SmartleadCourseCubes.cs)
- [상세 화면 전환](../Assets/Notifications/Runtime/NotificationDetailUI.cs)

## 기록의 구분

- **예외:** Unity 콘솔이나 실행 환경이 출력한 오류입니다. 확보한 원문을 코드 블록에 남깁니다.
- **서버 오류:** HTTP 응답 내용으로 확인한 오류입니다. HTTP 상태 코드가 200이어도 응답 본문은 오류일 수 있습니다.
- **화면 증상:** 오류 예외 없이 표시되는 안내, 빈 목록, 잘못된 화면 전환입니다. 서버가 실제로 반환한 오류와 구분합니다.
- **검증 범위:** 자동 테스트, Unity 컴파일, 실제 로그인 세션에서 확인한 내용을 각각 적습니다. 테스트 통과만으로 학교 서버 연결 성공을 단정하지 않습니다.

<a id="pc-001"></a>

## PC-001 · 세로 미리보기 초기화 중 NullReferenceException

- **환경:** Unity 6.3 LTS, 6000.3.26f1.
- **구분:** Unity 예외. 사용자 콘솔 로그로 확인했습니다.
- **발생 조건:** 에디터가 세로 미리보기를 자동 초기화할 때 발생했습니다.

발생 당시 로그:

```text
NullReferenceException: Object reference not set to an instance of an object
WebNotifications.Editor.PortraitCampusSetup.SelectPortraitPreview () (at Assets/Notifications/Editor/PortraitCampusSetup.cs:75)
WebNotifications.Editor.PortraitCampusSetup.InitializePreview () (at Assets/Notifications/Editor/PortraitCampusSetup.cs:37)
UnityEditor.EditorApplication.Internal_CallDelayFunctions ()
```

**원인:** `sizesType.GetMethod("GetCurrentGroup")`이 null을 반환했는데 바로 `Invoke()`를 호출했습니다. 설치된 Unity 버전은 `GetCurrentGroup()` 메서드 대신 `currentGroup` 속성을 제공합니다.

**해결:** `sizesType.GetProperty("currentGroup").GetValue(sizes)`로 변경했습니다. 빌드 대상 변경은 `IActiveBuildTargetChanged`로 처리합니다.

**검증:** 설치된 Unity 어셈블리에서 관련 메서드·속성과 생성자를 확인하고 수정 코드를 컴파일했습니다. 로그의 줄 번호는 발생 당시 기준입니다.

**수정 파일:** `Assets/Notifications/Editor/PortraitCampusSetup.cs`.

<a id="pc-002"></a>

## PC-002 · 재접속 후 기존 계정의 로그아웃을 요구하는 확인창

- **구분:** 학교 화면의 확인창과 로그인 흐름 문제. 예외가 아닙니다.
- **발생 조건:** WebView에 유효한 학교 세션이 남아 있는 상태에서 상단 **재접속**을 누릅니다.
- **관찰:** 이미 로그인됐다는 안내와 함께 다른 사용자로 로그인하려면 로그아웃해야 한다는 확인창이 표시됐습니다. 전체 문구의 텍스트 원문은 별도로 저장하지 않았습니다.

**원인:** 앱 내부의 인증 플래그가 false라는 이유로 `/login/index.php`를 직접 열었습니다. 앱의 플래그와 학교 WebView의 세션 상태가 항상 같지는 않습니다.

**해결:** 최초 실행·재접속 시 `/local/ubion/user/index.php`를 열어 학교가 기존 세션을 확인하도록 변경했습니다. 세션이 유효하면 캠퍼스로 이동하고, 만료됐으면 학교의 로그인 흐름을 따릅니다. 계정 화면도 기존 세션을 재사용합니다.

**검증:** 관련 수집 테스트와 Unity 컴파일을 확인했습니다. 이후 실제 Play 재실행에서도 기존 학교 세션으로 인증 상태를 복구했습니다.

**수정 파일:** `Assets/Notifications/Runtime/SmartleadWebView.cs`.

**2026-10-22 재로그인 흐름 보완:** 로그아웃 후 비밀번호 입력 확인을 추가한 뒤에도 같은 확인창에서 캠퍼스로 이동하지 못하는 화면이 보고되었습니다. 코드에서 로그인 제출 알림을 현재 주소·수집 nonce로만 검증해 SSO 리다이렉트 뒤 도착한 알림을 버리는 경로와, 실제 폼이 없는 로그인 확인 페이지에서도 입력 확인을 초기화하는 경로를 확인했습니다. 로그인 문서에 발급한 별도 검증 값을 주소 전환 중 유지하고, 실제 폼 준비 알림에서만 새 입력을 시작하도록 변경했습니다. 폼 제출은 Unity가 입력 여부·저장 설정을 수신한 뒤 재개하므로 화면 이동이 먼저 진행되지 않습니다. 이미 로그인된 확인창은 입력 확인이 남아 있으면 알림 페이지로 이동해 학교 인증을 확인하고, 재입력이 필요한 상태라면 남은 세션을 로그아웃한 뒤 로그인하도록 처리합니다. 제출 재개·중복 제출 방지·잘못된 검증 값 거부·확인창 감지 등 관련 Node 테스트 총 40개와 C# 빌드를 통과했습니다. 실제 계정의 전체 재로그인 흐름은 별도 확인이 필요합니다.

**2026-10-22 후속 수정 (위 제출 재개 방식 대체):** 실제 사용자 환경에서 SSO 로그인을 누르면 `/login.php`의 통합 로그인 선택 화면으로 반복 복귀했습니다. 앞선 입력 확인 조건은 정상 학교 페이지에서도 `Logout()`을 호출했고, 제출 이벤트 취소·재실행은 학교 SSO의 원래 처리를 변경했습니다. 이 두 처리를 제거했습니다. 이제 폼 제출은 수동 관찰만 하고 앱 응답을 기다리지 않으며, 학교의 보호된 페이지에서 받은 인증 성공으로 캠퍼스를 엽니다. 명시적 로그아웃의 계정 정보·쿠키 삭제와 다음 로그인 폼의 비밀번호 초기화는 유지하고, 로그아웃 뒤 앱을 재시작해도 쿠키 정리 후 새 로그인에서 시작합니다. 실제 학교 계정으로 전체 흐름을 재검증해야 하며 Node의 폼 모형 테스트가 이를 대체하지 않습니다.

<a id="pc-003"></a>

## PC-003 · 과목 카드가 계속 “과제 확인 중…”으로 표시됨

- **구분:** 화면 상태 처리 문제.
- **발생 조건:** 알림 정보로는 과목 카드를 만들 수 있지만 별도의 수강 과목 조회는 실패한 경우입니다.
- **확인한 화면 문구:** `과제 확인 중…`.

**원인:** 과목 목록 조회 실패를 등록된 수강 과목에만 전달했습니다. 알림에서 생성된 과목 카드에는 실패 상태가 전달되지 않아 초기 문구가 계속 남았습니다. 요청 시간 제한도 응답 헤더를 받은 뒤 해제돼 본문 다운로드 지연을 충분히 처리하지 못했습니다.

**해결:** 전체 수집의 실패·완료 상태를 카드에 반영하고, 45초 이상 지연되면 안내를 표시하도록 했습니다. 요청의 12초 제한을 응답 본문을 읽을 때까지 유지하며, **일정 다시 확인 / 학교 일정 보기** 경로를 제공합니다.

**검증:** 본문 응답이 멈추는 테스트에서 수집이 무기한 대기하지 않고 실패·완료 상태로 끝나는 것을 확인했습니다. 이 수정은 대기 상태를 해결하며, 실제 데이터 조회 실패의 원인은 PC-004에서 별도로 처리했습니다.

**수정 파일:** `SmartleadWebView.cs`, `SmartleadCourseCubes.cs`, `SmartleadCoursework.txt`.

<a id="pc-004"></a>

## PC-004 · 과목 조회 API 부재와 학교 화면 선택자 불일치

- **구분:** 서버 오류와 HTML 수집 경로 문제.
- **화면 문구:** `수강 과목 조회 실패 · 학교에서 다시 확인해 주세요.`
- **확인 방법:** 실제 로그인된 Unity WebView에서 API 응답 구조와 학교 대시보드·과제 페이지의 DOM을 읽었습니다.

HTTP 200 응답 본문에서 확인한 오류:

```text
데이터베이스 테이블 external_functions 에서 데이터 레코드를 찾지 못 함
```

**원인:** 요청한 `core_course_get_enrolled_courses_by_timeline_classification` 함수가 해당 학교 서버에 등록되지 않았습니다. 대체 HTML 조회도 표준 Moodle의 `#region-main`과 `.course-list`에만 의존해 Coursemos의 실제 목록을 찾지 못했습니다. 이 오류만으로 학교 데이터베이스 전체가 고장 났다고 판단하지 않습니다.

**해결:**

1. API를 사용할 수 없으면 `/my/` 등 학교 대시보드에서 과목을 읽습니다.
2. `.course-info-container h4 a`와 `[role="main"]`의 실제 과목 링크를 지원합니다.
3. 중복 링크와 `View course` 같은 일반 이동 버튼을 과목명으로 사용하지 않습니다.
4. 과제 페이지는 `#region-main` 외에 `[role="main"]`, `#page-content`도 지원합니다.
5. 과제 표의 **종료 일시 / 제출** 열에서 마감일과 제출 여부를 직접 읽습니다.

**검증:** 실제 로그인 세션에서 수강 과목·과제·제출 여부·마감일 수집을 확인했습니다. Game 뷰에서도 미제출 과제와 남은 시간이 표시됐습니다. 같은 레이아웃을 다루는 회귀 테스트를 추가했습니다. 대체 조회가 전체 과목 범위를 보장하지 못하면 **전체 과목 확인 필요** 안내는 유지합니다.

**수정 파일:** `Assets/Notifications/Resources/SmartleadCoursework.txt`, `NotificationServer/coursework.test.mjs`.

<a id="pc-005"></a>

## PC-005 · 숫자 날짜와 “제출 여부” 항목을 인식하지 못함

- **구분:** 수집 형식 불일치. 별도의 예외 원문은 없습니다.
- **발생 조건:** 학교가 날짜를 `YYYY-MM-DD HH:mm`으로 표시하고 상세 표의 항목명을 **제출 여부**로 제공하는 경우입니다.

**원인:** 초기 날짜 파서는 한국어 연·월·일 표기와 `time[datetime]`을 중심으로 처리했습니다. 제출 상태도 **제출 상태**라는 항목명 위주로 읽어 실제 **제출 여부**를 놓쳤습니다.

**해결:** 숫자 날짜를 한국 시간으로 변환하고 **제출 여부**, **종료 일시**, 과제 목록의 **제출** 열을 지원했습니다. **마감까지 남은 기한**처럼 설명 문장이 들어가는 행을 실제 마감일로 잘못 읽지 않도록 구분했습니다.

**검증:** 숫자 날짜, 한국어 날짜, 개인 연장 기한, 미제출·제출 완료 상태를 자동 테스트로 확인했습니다. 실제 학교 과제의 마감일·제출 상태도 확인했습니다.

**수정 파일:** `Assets/Notifications/Resources/SmartleadCoursework.txt`.

<a id="pc-006"></a>

## PC-006 · 학교 알림 요청이 JSON 대신 “Ajax 전용 페이지” HTML을 반환함

- **기록 출처:** 기존 [WEBVIEW-SETUP.ko.md](../WEBVIEW-SETUP.ko.md). 최초 발생 시각과 전체 응답 원문은 이 기록에 없습니다.
- **구분:** 학교 AJAX 요청 형식 문제.
- **기존 문서에 기록된 안내:** `Ajax 전용 페이지`.

**원인:** 학교의 `/theme/coursemosv2/action.php` 요청에 필요한 `X-Requested-With: XMLHttpRequest` 헤더가 없으면 JSON 대신 HTML이 반환될 수 있습니다.

**해결:** 해당 헤더와 현재 페이지의 세션 검증 값을 포함해 요청합니다. HTML이나 잘못된 JSON을 빈 알림 목록으로 처리하지 않습니다.

**검증:** 알림 수집 테스트에서 필수 헤더와 세션 검증, HTML·잘못된 응답 처리, 로그인 만료 시 동작을 확인합니다.

**관련 파일:** `SmartleadCapture.txt`, `NotificationServer/capture.test.mjs`.

<a id="pc-007"></a>

## PC-007 · 외부 dotnet 빌드 검증의 NETSDK1004

- **구분:** 개발 도구의 빌드 오류. 앱 실행 오류와 구분합니다.
- **발생 조건:** Unity가 생성한 C# 프로젝트에 `dotnet build Assembly-CSharp-Editor.csproj --no-restore`를 실행합니다.

확인한 오류의 핵심 부분:

```text
error NETSDK1004: 자산 파일 '.../Temp/obj/Assembly-CSharp-Editor/project.assets.json'을(를) 찾을 수 없습니다.
NuGet 패키지 복원을 실행하여 이 파일을 생성하세요.
```

**원인:** 외부 .NET SDK 빌드에 필요한 복원 산출물이 없는 상태였습니다. 이 결과만으로 Unity 스크립트 자체의 컴파일 실패를 판단할 수 없습니다.

**처리:** Unity 설치에 포함된 컴파일러와 프로젝트 참조로 C# 코드를 확인하고, 실제 Unity 에디터에서도 재컴파일과 Play 실행을 확인했습니다. 자동 생성된 `.csproj` 파일을 문제 해결용으로 직접 수정하지 않았습니다.

**검증:** 수정 코드의 컴파일과 이후 실제 Game 뷰 실행을 확인했습니다. 외부 `dotnet build` 경로 자체를 복구한 것으로 기록하지 않습니다.

<a id="pc-008"></a>

## PC-008 · Play 재시작 시 이전 도메인의 GC 핸들 해제 경고

- 기록일: 2026-10-22
- 환경: Windows, Unity 6000.3.26f1 에디터
- 상태: 요청 정리 코드 수정 및 자동 검증 완료 · 에디터 재현 확인 필요
- 실제 오류: `Release of invalid GC handle. The handle is from a previous domain. The release operation is skipped.`
- 확인한 근거: Editor.log의 네이티브 스택에 `Coroutine::CleanupCoroutine`, `UnityWebRequestAsyncOperation::InvokeCoroutine`, `EditorSceneManager::RestoreSceneBackups`, `PlayerLoopController::EnterPlayMode`가 기록되어 있습니다. WebView 네이티브 렌더링이나 비밀번호 보호 저장소 호출은 이 스택에 없습니다.
- 원인 범위: 도메인 재로드 이후 이전 HTTP 요청의 코루틴 완료가 처리되는 경로입니다. 공지 수집은 코루틴 중지만 수행했고 진행 중 요청의 명시적 취소·해제가 없었습니다. 이 경로가 경고를 유발한 유일한 원인인지에 대한 에디터 재현 검증은 남아 있습니다.
- 수정: `NotificationRequestScope.cs`로 진행 중 요청을 추적하고 비활성화 시 취소·해제합니다. 네이티브 AsyncOperation을 직접 yield하지 않고 완료 여부를 프레임마다 확인하며, 취소된 요청은 결과 파싱·콜백을 실행하지 않습니다. 학교 공지 조회·게시판 추가·로컬 로그인·알림 API에 적용했습니다.
- 검증: `./NotificationServer/test-request-scope.ps1`에서 정상 완료, 대기 중 취소, 시작 전 취소, 중복 해제 방지, 동시 요청 4개 정리, 재활성화 후 새 요청 등 13개 검증을 통과했습니다. 테스트는 실제 Unity 에디터의 도메인 재로드를 대신하지 않습니다.
- 사용자 확인: Play를 종료하고 Unity 에디터를 다시 연 뒤 공지 조회 중 Play 종료·재시작으로 같은 경고가 새로 발생하는지 확인합니다. 이미 남은 이전 실행의 핸들 때문에 수정 직후 한 번 더 나타날 수 있습니다.

<a id="pc-009"></a>

## PC-009 · 과제 상세 상단만 보이고 본문이 검은 화면

- 기록일: 2026-10-22
- 환경: Windows Unity 에디터
- 확인한 근거: Editor.log에 `AssignmentDetail.html.meta`의 GUID가 유효하지 않아 에셋을 무시했다는 기록이 있습니다. 실제 `.meta` GUID 길이는 33자로 Unity의 32자 형식에 맞지 않았습니다.
- 영향: `Resources.Load<TextAsset>("AssignmentDetail")`가 템플릿을 읽지 못한 상태에서 표시 코드가 조용히 반환했습니다. 로딩·오류 안내도 같은 템플릿을 사용하므로 본문 전체가 비었습니다.
- 수정: 템플릿 GUID를 32자리로 수정했습니다. 템플릿 누락 시 Unity 로그와 앱 자체 안내를 표시하고, 새 상세 WebView가 초기화되기 전에는 HTML을 보관했다가 초기화 후 로드합니다. 렌더러 오류·초기화 시간 초과도 앱 안내로 표시합니다.
- 검증: Assets 전체 `.meta` GUID 형식을 검사하고 C# 컴파일을 통과했습니다. `node --test NotificationServer/asset-metadata.test.mjs`로 같은 메타데이터 오류를 검사할 수 있습니다. 실제 에디터의 과제 상세 재표시는 별도 확인이 필요합니다.

## 검증 명령과 결과 기록

프로젝트 루트에서 실행합니다.

```powershell
cd NotificationServer
node --test capture.test.mjs coursework.test.mjs
```

2026-10-19 변경 사항은 관련 수집 테스트 23개, Unity 컴파일, Windows의 실제 로그인 세션 수집과 Game 뷰 표시를 확인했습니다. Android 브리지 자동 테스트와 Android 조건 소스 컴파일도 확인했으며, 추가한 파일과 검증 범위는 [Android 사용 안내](ANDROID.ko.md)에 기록했습니다. Android 실기기·홈 화면 위젯·백그라운드 수집을 검증한 기록은 아닙니다.

## 새 오류를 추가하는 양식

새 문제는 PC-010부터 번호를 이어 적고 README 요약 표에도 링크를 추가합니다. 메시지를 수정해서 인용하지 말고, 개인정보를 가린 곳은 `[가림]`으로 표시합니다. 원인이 확인되기 전에는 **원인 미확정 / 조사 중**으로 기록하고, 해결됐다는 표현은 검증 후 사용합니다.

```markdown
<a id="pc-010"></a>

## PC-010 · 문제 제목

- 기록일:
- 환경: Unity 버전 / OS / Editor 또는 실기기
- 구분: 예외 / 서버 오류 / 화면 증상 / 개발 도구
- 상태: 조사 중 / 수정 완료 / 검증 완료
- 발생 조건과 재현 순서:
- 실제 오류 원문 또는 화면 문구: 원문이 없으면 미확보로 표시
- 예상 동작과 실제 동작:
- 확인한 근거:
- 원인: 미확정이면 추측과 확인된 사실을 구분
- 해결 방법:
- 수정 파일 또는 커밋:
- 검증 방법과 결과:
- 남아 있는 제한:
```

공개 기록에는 비밀번호·쿠키·세션 키·개인 알림 본문·개인 과제 제출물과 계정 식별 정보를 넣지 않습니다. 스크린샷은 필요한 부분을 익명화한 뒤 사용합니다. 프로젝트의 `Logs`·`Temp`에 있는 진단 자료를 통째로 커밋하지 않습니다.

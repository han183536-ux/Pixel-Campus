# Unity · SmartLEAD 알림 기본 틀

현재 기본 화면은 로그인된 WebView로 SmartLEAD 과제·알림을 수집합니다. 홈페이지 공개 공지는 별도의 Unity `UniversityNoticeFeed`가 학교 공식 RSS를 직접 조회합니다. 상단 **학교 공지**에서 메인·SW중심대학사업단·학과·부속기관의 공개 게시판 241개를 통합 검색하고 **신청·제출 일정**에서 시작/마감까지 남은 시간을 확인할 수 있습니다. 선착순은 시작일을 우선 표시합니다. 공개 원문의 신청·접수·제출 기간을 분석하며 로그인 전용·이미지/첨부파일만으로 안내된 일정은 원문 확인이 필요합니다. 이 기능에는 아래 Node 서버나 SmartLEAD 토큰이 필요하지 않습니다. 기본 목록은 `node NotificationServer/discover-university-boards.mjs`로 공식 링크에서 재생성하고 `./NotificationServer/test-university-notices.ps1`로 RSS·기간 분석을 검증합니다. 추가 RSS는 화면의 **게시판 추가**로 등록할 수 있습니다. 자세한 범위는 루트 `README.md`를 참고하세요. 아래 서버 연결 방식은 대체 연결입니다.

## 1. Unity만 실행하기

현재 `Assets/Scenes/NotificationDemo.unity`는 실제 서버 연결 모드로 저장되어 있습니다. Windows Unity Editor에서 Play를 누르면 로컬 서버가 자동 실행됩니다. Game 화면의 학교 계정 로그인 폼을 사용합니다.
서버 없이 샘플만 확인하려면 `Web Notifications > Notification Manager > Use Sample Data`를 켜고 Play를 누릅니다.
씬이 없다면 `Tools > Web Notifications > Create Demo Scene`으로 생성할 수 있습니다.

- SmartLEAD / 일정 / 새 소식 건물 3개와 소스별 배지
- 한국 시간(UTC+9) 기준 오늘 게시된 알림만 표시
- 목록 또는 건물 배지 선택 → 상세 내용 → 원문 열기
- 목록에서 알림을 선택하면 읽음 처리. PlayerPrefs에 이 기기의 상태를 저장
- 샘플 JSON의 `{{TODAY}}`를 실행일로 교체하므로 날짜가 바뀌어도 테스트 가능
- 기존 SampleScene은 유지. 데모 씬은 별도 저장

## 2. 중간 서버와 연결하기

Node.js 20 이상이 필요하며 추가 패키지 설치는 없습니다. Windows Unity Editor에서는 자동 실행되며, 수동 실행 또는 데스크톱 빌드에서는 프로젝트 루트에서 아래 명령을 사용합니다. 기본 모드는 `smartlead`입니다.

```powershell
node NotificationServer/server.mjs
```

Unity의 `Web Notifications` 오브젝트에서 `Notification Manager > Use Sample Data`를 끕니다.
`Notification Api Client > Base Url`은 `http://127.0.0.1:8787`입니다.
실행 중 바꿨다면 새로고침을 누릅니다. 변경을 저장하려면 Play를 종료한 뒤 Inspector에서 설정합니다.

- `GET /health`: 실행 모드 확인
- `GET /api/notifications`: 오늘 알림 (기본)
- `GET /api/notifications?today=false`: 수집된 전체 기간
- `GET /api/notifications?source=smartlead`: 소스 필터
- `GET /api/sources`: 수집된 데이터의 소스 목록

Unity는 기본 30초 간격으로 조회합니다. 실패 시 최대 300초까지 간격을 늘리고 마지막 정상 데이터를 유지합니다.
서버도 30초 동안 수집 결과를 재사용합니다. 중복은 source_id + id 기준으로 제거합니다.
한국 시간 자정이 지나면 전날 알림을 화면에서 제거합니다.

## 3. SmartLEAD 개인 알림 연결

대상: https://smartlead.hallym.ac.kr/

2026-09-17 SmartLEAD의 `/webservice/rest/server.php`가 Moodle REST 인증 오류(`invalidtoken`)로 응답하는 것을 확인했습니다. REST 진입점은 활성화되어 있지만, **각 계정에 허용된 API 함수와 토큰 발급 가능 여부는 로그인 후 확인해야 합니다.** 공개 게시판 스크래핑은 사용하지 않습니다.

### Unity 화면에서 학교 계정으로 로그인 (기본)

**2026-09-17 진단:** 공식 공개 설정 `tool_mobile_get_public_config`의 응답은 `enablewebservices: 1`, `enablemobilewebservice: 0`, `typeoflogin: 1`입니다. 현재 SmartLEAD는 이 프로젝트가 사용한 표준 모바일 앱 로그인을 비활성화했습니다. 비밀번호 오류나 2단계 인증이 원인이라고 확정한 것이 아닙니다. Unity는 로그인 폼을 활성화하기 전에 이 설정을 조회하며, 비활성 상태에서는 자격증명을 전송하지 않고 정확한 이유를 표시합니다. 학교가 설정을 변경하면 '연결 방식 확인'으로 다시 조회할 수 있습니다.

실제 연결에는 학교에서 허용한 웹서비스와 개인 알림 조회 권한이 필요합니다. 담당자에게 `core_webservice_get_site_info`, `message_popup_get_popup_notifications` 사용이 가능한 서비스·토큰을 요청할 수 있습니다. 발급받은 토큰은 아래 대체 경로로 입력하며, 실제 권한은 서버가 검증합니다. 'SmartLEAD 웹사이트 열기'는 원문 확인용이며 브라우저에 로그인했다고 Unity가 자동으로 연결되지는 않습니다.

1. `NotificationDemo` 씬을 열고 Play를 누릅니다. `NotificationLocalServer`가 필요할 때 로컬 서버를 숨김 실행합니다.
2. Game 화면에서 학교 아이디·비밀번호를 입력하고 **학교 계정으로 로그인**을 누릅니다.
3. 성공하면 오늘 개인 알림이 표시됩니다. 실패 이유는 로그인 폼 아래에 표시됩니다.
4. **로그아웃**을 누르면 서버의 토큰과 알림 캐시, Unity 화면의 알림을 제거합니다.

입력 비밀번호는 마스킹되며 제출 직후 입력 필드에서도 지웁니다. Unity는 계정 정보를 같은 PC의 `127.0.0.1` 서버에 전달하고, 서버가 SmartLEAD의 HTTPS 로그인 API에 전달합니다. 학교 토큰은 Unity에 반환하지 않습니다. 비밀번호·토큰·아이디는 파일이나 PlayerPrefs에 저장하지 않습니다. Unity에서 시작한 서버는 Play 종료 시 함께 종료됩니다. 이미 수동 실행된 서버는 종료하지 않습니다.

이 UI는 학교의 앱 로그인 API를 사용합니다. 학교가 통합인증(SSO)·2단계 인증만 허용하거나 모바일 API를 차단했다면 학교 계정 입력만으로 연결되지 않을 수 있습니다. 이 경우 실패 안내를 표시하며, 아래의 학교가 허용한 토큰 방식이 필요합니다. SSO 브라우저 로그인을 구현했다고 가정하지 않습니다.

로컬 인증 API:

- `POST /api/auth/login`: `{ "username": "...", "password": "..." }`
- `POST /api/auth/logout`: `{}`
- JSON Content-Type과 `X-Notification-Client: unity` 헤더 필수
- 브라우저 Origin 요청 차단, localhost Host 제한, 입력 길이 제한, 재시도 간격 제한
- 로그인 성공은 계정 및 알림 API 권한 검증이 끝난 후 확정
- 계정 변경 또는 로그아웃 중 진행되던 요청이 이전 계정 데이터를 복원하지 않도록 세션 세대를 검증

### 별도 터미널 또는 토큰으로 실행 (대체 경로)

1. 기존 테스트 서버가 실행 중이면 해당 터미널에서 Ctrl+C로 종료합니다(8787 포트).
2. `Tools > Web Notifications > Start SmartLEAD Server (Login)`을 누릅니다.
3. 열린 PowerShell 창에 본인 SmartLEAD ID와 비밀번호를 입력합니다. 비밀번호는 숨김 입력입니다. 채팅에 입력하지 않습니다.
4. 서버 주소가 출력되면 이 창을 열어 둡니다.
5. `Tools > Web Notifications > Use SmartLEAD Connection`을 누르고 씬을 저장한 뒤 Play를 누릅니다.
6. `SmartLEAD 개인 알림 연결됨` 상태와 오늘의 개인 알림을 확인합니다. 오늘 알림이 없다면 목록이 비어 있는 것이 정상입니다.

PowerShell에서 직접 실행할 수도 있습니다.

```powershell
& .\NotificationServer\start-smartlead.ps1 -Login
```

로그인 스크립트는 Moodle의 `/login/token.php`에 `moodle_mobile_app` 서비스로 로그인합니다. 이 방식은 학교가 모바일 웹서비스와 해당 계정 인증을 허용하는 경우에만 동작합니다. SSO·2단계 인증을 우회하지 않습니다. 비밀번호와 토큰을 파일, PlayerPrefs, Unity 씬에 저장하지 않습니다. 토큰은 서버 프로세스 환경변수에만 전달되며 종료하면 다시 입력해야 합니다.

SSO 계정이거나 토큰 자동 발급이 거부되면 학교에서 발급·허용한 웹서비스 토큰이 필요합니다. `Start SmartLEAD Server (Token)` 메뉴 또는 아래 명령으로 숨김 입력합니다.

```powershell
& .\NotificationServer\start-smartlead.ps1
```

토큰에 필요한 함수:

- `core_webservice_get_site_info`: 본인 계정 및 허용 함수 확인
- `message_popup_get_popup_notifications`: 본인 개인 알림 조회 (`useridto=0`)

조회는 최신순 100개씩 페이지를 넘기며 한국 시간의 오늘 범위까지만 가져옵니다. 한 번에 최대 2,000개, 전체 요청 시간 8초입니다. 조회가 중간에 실패하면 불완전한 데이터로 이전 목록을 덮어쓰지 않습니다. 토큰은 URL이 아닌 HTTPS POST 본문으로 전송합니다. 로그인 오류의 원본 응답과 개인정보는 로그에 출력하지 않습니다.

`GET /api/connection`에서 `configured`, `state`, `message`, `updated_at`으로 연결 상태를 확인합니다. 이 응답은 토큰이나 계정 식별자를 포함하지 않습니다. Unity에도 설정 필요·토큰 만료·권한 부족을 한글로 표시합니다. 처음 조회가 실패하면 503, 이전 데이터가 있으면 `stale=true`로 반환합니다.

SmartLEAD에서 읽은 상태를 반영하며 Unity에서 읽은 상태는 이 PC에 추가로 저장합니다. **Unity에서 읽어도 학교 사이트의 읽음 상태는 바꾸지 않습니다.** 로컬 읽음 키에 계정 식별자를 포함하므로 다른 계정과 섞이지 않습니다. 이 연결은 SmartLEAD의 개인 알림함에 실제로 생성된 항목을 가져옵니다. 모든 강의 게시판과 과제를 별도로 검색하거나 미래 마감 일정을 수집하는 기능은 아닙니다.

API 규격 참고: [Moodle 공식 개인 알림 구현](https://github.com/moodle/moodle/blob/MOODLE_39_STABLE/message/output/popup/externallib.php), [Moodle 웹서비스 안내](https://docs.moodle.org/en/Web_services).

### 파일 입력 테스트와 다른 API 확장

먼저 허용된 방법으로 확보한 데이터를 `NotificationServer/smartlead.notifications.json`에 넣어 테스트할 수 있습니다.

```json
{
  "notifications": [{
    "id": "smartlead_notice_001",
    "source_id": "smartlead",
    "source_name": "한림 SmartLEAD",
    "title": "공지 제목",
    "message": "공지 내용",
    "published_at": "2026-09-17T09:00:00+09:00",
    "url": "https://smartlead.hallym.ac.kr/",
    "target_object_id": "building_smartlead",
    "priority": "normal",
    "icon_type": "notice"
  }]
}
```

`published_at`에 실제 게시 시각과 시간대를 넣습니다. 테스트 파일의 날짜가 오늘이 아니면 Unity에는 나타나지 않습니다.
서버를 종료하고 아래처럼 재실행합니다.

```powershell
$env:NOTIFICATION_MODE = 'file'
node NotificationServer/server.mjs
```

다른 JSON API를 연결할 때는 서버의 `api` 모드를 사용합니다.
`NOTIFICATION_UPSTREAM_URL`은 홈페이지 주소가 아닌 **확인된 API 주소**여야 합니다.
현재 api 모드는 위 JSON 규격을 반환하는 HTTPS API를 기대합니다.
SmartLEAD는 전용 `smartlead` 모드와 `smartlead.mjs` 어댑터를 사용합니다.
`NOTIFICATION_UPSTREAM_TOKEN`은 서버 환경변수에서만 읽으며 Unity에 넣지 않습니다.
학교 계정 비밀번호나 브라우저 세션 쿠키를 저장하는 기능은 없습니다.

## 4. 코드 구성과 범위

`Assets/Notifications/Runtime`:

- `NotificationDataModel`: JSON 모델
- `NotificationApiClient`: UnityWebRequest 통신과 타임아웃
- `NotificationManager`: 오늘 필터, 중복 제거, 폴링, 읽음 상태
- `NotificationSourceRegistry`: source_id와 씬 오브젝트 매핑
- `NotificationIndicator`: 건물 배지 및 클릭
- `NotificationDetailUI`: 목록, 필터, 상세, 원문 링크 (프로토타입 IMGUI)

알림 건물을 추가할 때 Registry와 Indicator의 sourceId를 서버의 source_id와 맞춥니다.
샘플의 일정·새 소식은 확장용 자리이며 Google Calendar 등의 실제 연결은 없습니다.
이번 범위는 로컬 Unity Editor/데스크톱 프로토타입입니다. 웹 대시보드나 WebGL 배포는 포함하지 않습니다.
서버는 127.0.0.1에만 열리며 다중 사용자 인증·서버 읽음 동기화·데이터베이스는 아직 없습니다.
배포 시 HTTPS와 사용자 인증을 추가해야 합니다. 읽음 기록은 현재 기기별이며 PlayerPrefs 초기화 시 사라집니다.

## 5. 검증

```powershell
node --test NotificationServer/server.test.mjs NotificationServer/smartlead.test.mjs
node --test NotificationServer/auth.test.mjs
```

한국 날짜 경계, 중복 처리, 잘못된 날짜·URL, 오늘 조회, 소스 필터, 데이터 없음, HTTP 메서드와 브라우저 출처 제한을 검증합니다.
Unity Bridge로 데모 씬 생성·컴파일·Play 모드·샘플 3개 로드·로컬 API 수신·읽음 저장·콘솔을 확인했습니다.
개인 알림 어댑터는 모의 API로 페이지 처리·한국 날짜 경계·계정 일치·읽음 상태·토큰 만료·권한 부족·오류 시 데이터 유지를 검증했습니다. 실제 개인 알림 수신은 본인 로그인 후 확인해야 합니다.
Unity Bridge로 실제 Game 화면의 로그인 폼을 확인했고, 로컬 모의 학교 서버로 로그인 → 개인 알림 수신 → 비밀번호 입력값 제거 → 로그아웃 흐름을 검증했습니다. 학교 실계정으로 로그인한 결과와는 구분됩니다.

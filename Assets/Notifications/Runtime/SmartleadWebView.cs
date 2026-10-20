using System;
using System.Collections.Generic;
using Gree.UnityWebView;
using UnityEngine;

namespace WebNotifications
{
    [RequireComponent(typeof(NotificationManager))]
    public class SmartleadWebView : MonoBehaviour
    {
        [Serializable] private class Notice { public int id, courseId; public long dueAt, createdAt; public string sender, title, body, url, courseName, courseCode, type; public bool read, readKnown, submitted, submissionKnown; }
        [Serializable] private class Capture { public string kind, nonce, page, source, error; public int count, total, courseId; public bool found, unreadLimited, historyLimited, countOnly, unreadUnknown, authenticated; public Notice[] items; }
        private const string NotificationPage = "https://smartlead.hallym.ac.kr/local/ubion/user/index.php";
        private WebViewObject view;
        private SmartleadCourseCubes courseCubes;
        private string nonce, currentUrl, script;
        private float nextRefresh;
        private bool applicationPaused, resumeRefreshPending, networkWasUnavailable;
        private float resumeRefreshAt;
        private bool authenticationVisible;
        private bool accountPagePinned;
        private bool returnFromLogin;
        private bool schoolResult;
        private bool historyResult;
        private int notificationPriority;
        private string historyStatus = "읽은 알림 포함 기록 조회 중";
        private string schoolStatus = "학교 알림 조회 중";
        private readonly List<string> titles = new List<string>();
        private readonly List<string> messages = new List<string>();
        private readonly List<Notice> notificationDetails = new List<Notice>();
        private readonly List<Notice> messageDetails = new List<Notice>();
        private readonly List<Notice> assignmentDetails = new List<Notice>();
        private readonly List<Notice> scheduleDetails = new List<Notice>();
        private readonly List<Notice> incomingSchedules = new List<Notice>();
        private readonly Dictionary<int, Notice> enrolledCourses = new Dictionary<int, Notice>();
        private readonly HashSet<int> assignmentsConfirmed = new HashSet<int>();
        private readonly HashSet<int> assignmentsFailed = new HashSet<int>();
        private readonly Dictionary<int, List<Notice>> assignmentUpdates = new Dictionary<int, List<Notice>>();
        private readonly HashSet<int> receivedCourses = new HashSet<int>();
        private string courseworkStatus = "과목 · 과제 확인 중…";
        private bool notificationsConfirmed, rosterPartial;
        private bool courseworkFailed, scheduleAvailable, courseworkFinished;
        private float courseworkDeadline;
        public string CourseworkStatus => networkWasUnavailable ? Status : courseworkStatus;
        private string selectedCourseKey, selectedCourseTitle;
        private Notice selectedNotice;
        private bool editingClassroom;
        private string classroomInput = "";
        public string Status { get; private set; } = "알림 확인 준비 중";
        public string MessageStatus { get; private set; } = "쪽지 확인 준비 중";
        private string apiStatus = "Moodle 알림 API 확인 중";
        public int Count { get; private set; }
        public int Total { get; private set; }
        public bool HasResult { get; private set; }
        public int MessageCount { get; private set; }
        public int MessageTotal { get; private set; }
        public bool HasMessagesResult { get; private set; }
        public bool IsAuthenticated { get; private set; }
        public bool IsSchoolPageVisible => authenticationVisible;
        private GUIStyle touchButton;
        private GUIStyle agendaLabel, agendaButton;

        private void Start()
        {
            if (!GetComponent<NotificationManager>().useWebView) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            Input.backButtonLeavesApp = false;
#endif
            courseCubes = GetComponent<SmartleadCourseCubes>();
            if (courseCubes == null) courseCubes = gameObject.AddComponent<SmartleadCourseCubes>();
            var asset = Resources.Load<TextAsset>("SmartleadCapture");
            if (asset == null) { Status = "알림 확인 스크립트가 없습니다."; return; }
            script = asset.text;
            var coursework = Resources.Load<TextAsset>("SmartleadCoursework");
            if (coursework != null) script += "\n" + coursework.text;
            else { courseworkFailed = true; courseworkStatus = "과제 수집 파일을 불러오지 못했습니다."; }
            try
            {
                view = new GameObject("SmartLEAD Notification WebView").AddComponent<WebViewObject>();
                view.transform.SetParent(transform);
                view.Init(cb: Receive, err: _ => Status = "SmartLEAD 연결 실패",
                    httpErr: _ => Status = "SmartLEAD 페이지 오류",
                    started: url => { currentUrl = url; nonce = null; HasResult = false; HasMessagesResult = false;
                        notificationsConfirmed = false;
                        schoolResult = false; schoolStatus = "학교 알림 조회 중"; apiStatus = "Moodle 알림 API 확인 중";
                        historyResult = false; notificationPriority = 0; historyStatus = "읽은 알림 포함 기록 조회 중";
                        titles.Clear(); messages.Clear();
                        Status = "알림 확인 중"; MessageStatus = "쪽지 확인 중"; },
                    ld: url => { currentUrl = url; Inject(); }, androidForceDarkMode: 1);
                ShowAuthentication();
                // Let the school reuse its session or redirect to login when it has expired.
                view.LoadURL(NotificationPage);
                Status = "SmartLEAD에 로그인하면 픽셀 캠퍼스로 이동합니다.";
                MessageStatus = "로그인 대기 중";
                nextRefresh = Time.unscaledTime + 120;
            }
            catch (Exception ex)
            {
                Status = Application.platform == RuntimePlatform.Android ?
                    "Android WebView 초기화 실패. Android System WebView 업데이트와 플러그인 포함 여부를 확인해 주세요." :
                    "WebView 초기화 실패: " + ex.GetType().Name;
            }
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                applicationPaused = true;
#if UNITY_ANDROID && !UNITY_EDITOR
                view?.SaveCookies();
#endif
                return;
            }
            if (!applicationPaused) return;
            applicationPaused = false;
            resumeRefreshPending = true;
            resumeRefreshAt = Time.realtimeSinceStartup + .5f;
            // Time spent outside the app is not a failed school request.
            courseworkDeadline = 0;
        }

        private bool PreserveLoginInput => authenticationVisible && !IsAuthenticated;

        private void HandleMobileBack()
        {
            var detail = GetComponent<NotificationDetailUI>();
            if (detail != null && detail.IsNotificationPanelOpen) { detail.CloseNotifications(); return; }
            if (view != null && authenticationVisible)
            {
                // The Android IME normally consumes its own Back event. Do not
                // navigate away from an input if an Escape also reaches Unity.
                if (view.IsKeyboardVisible) return;
                if (IsAuthenticated) { HideAccountPage(); return; }
                if (view.CanGoBack()) { view.GoBack(); return; }
            }
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var unity = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = unity.GetStatic<AndroidJavaObject>("currentActivity"))
                activity.Call<bool>("moveTaskToBack", true);
#endif
        }

        private static bool SchoolPage(string value)
        {
            return Uri.TryCreate(value, UriKind.Absolute, out var url) && url.Scheme == "https" &&
                url.Host == "smartlead.hallym.ac.kr" && url.Port == 443 && string.IsNullOrEmpty(url.UserInfo);
        }

        private void Inject()
        {
            if (!SchoolPage(currentUrl) || new Uri(currentUrl).AbsolutePath.StartsWith("/login", StringComparison.OrdinalIgnoreCase))
            {
                IsAuthenticated = false;
                courseCubes?.Clear();
                notificationDetails.Clear(); messageDetails.Clear(); SelectCourse(null, null);
                ClearCoursework();
                accountPagePinned = false;
                returnFromLogin = true;
                ShowAuthentication();
                Status = "SmartLEAD에 로그인하면 픽셀 캠퍼스로 이동합니다.";
                return;
            }
            var path = new Uri(currentUrl).AbsolutePath;
            if (returnFromLogin && (path == "/" || path == "/dashboard.php"))
            {
                returnFromLogin = false;
                Status = "개인 알림 화면으로 이동 중";
                view.LoadURL(NotificationPage);
                return;
            }
            nonce = Guid.NewGuid().ToString("N");
            receivedCourses.Clear();
            assignmentUpdates.Clear();
            incomingSchedules.Clear();
            courseworkDeadline = Time.unscaledTime + 45;
            courseworkFinished = false;
            view.EvaluateJS(script.Replace("__UNITY_NONCE__", nonce));
        }

        private void Update()
        {
            if (applicationPaused) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (Input.GetKeyDown(KeyCode.Escape)) { HandleMobileBack(); return; }
#endif
            if (view == null) return;
            if (Application.isMobilePlatform && Application.internetReachability == NetworkReachability.NotReachable)
            {
                networkWasUnavailable = true;
                courseworkDeadline = 0;
                Status = "인터넷 연결이 없습니다. 연결 후 다시 확인해 주세요.";
                return;
            }
            if (networkWasUnavailable)
            {
                networkWasUnavailable = false;
                resumeRefreshPending = true;
                resumeRefreshAt = Time.realtimeSinceStartup + .5f;
            }
            if (resumeRefreshPending && Time.realtimeSinceStartup >= resumeRefreshAt)
            {
                resumeRefreshPending = false;
                // Opening/closing the keyboard produces focus changes on Android.
                // Resume checks must never reload an unfinished login form.
                if (!PreserveLoginInput) Refresh();
                else Status = "학교 로그인 화면에서 계속 진행하거나 재접속해 주세요.";
            }
            if (courseworkDeadline > 0 && Time.unscaledTime >= courseworkDeadline)
            {
                courseworkDeadline = 0;
                courseworkFinished = true;
                courseworkFailed = true;
                courseworkStatus = "과제 조회가 지연되고 있습니다. 다시 확인하거나 학교 일정을 열어 주세요.";
                SyncCourseOverview();
            }
            if (authenticationVisible)
            {
                ApplyWebViewMargins();
                return;
            }
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 120;
            Refresh();
        }

        private void ShowAuthentication()
        {
            if (view == null) return;
            authenticationVisible = true;
            courseCubes?.SetVisible(false);
            ApplyWebViewMargins();
            view.SetVisibility(true);
        }

        public void OpenAccountPage()
        {
            if (view == null) return;
            accountPagePinned = true;
            ShowAuthentication();
            view.LoadURL(NotificationPage);
        }

        public void OpenHistoryPage()
        {
            if (view == null) return;
            accountPagePinned = true;
            ShowAuthentication();
            view.LoadURL("https://smartlead.hallym.ac.kr/local/ubnotification/");
        }

        private void HideAccountPage()
        {
            if (!IsAuthenticated) return;
            accountPagePinned = false;
            authenticationVisible = false;
            view?.SetVisibility(false);
            courseCubes?.SetVisible(true);
            nextRefresh = Time.unscaledTime + 120;
        }

        private static string CourseLabel(Notice item)
        {
            if (!string.IsNullOrWhiteSpace(item.courseName))
                return "[" + item.courseName + (string.IsNullOrWhiteSpace(item.courseCode) ? "" : " (" + item.courseCode + ")") + "] ";
            if (item.courseId > 0) return "[온라인 강의실 ID " + item.courseId + "] ";
            return "[과목 미확인] ";
        }

        private static string ReadLabel(Notice item)
        {
            return !item.readKnown ? "읽음 상태 미확인 · " : item.read ? "읽음 · " : "안 읽음 · ";
        }

        private void UpdateCourseCubes(Notice[] items, bool isMessage)
        {
            SyncCourseOverview();
        }

        private void ApplyWebViewMargins()
        {
            if (view == null) return;
            var area = CampusScreenLayout.ContentArea;
            view.SetMargins(Mathf.RoundToInt(area.x), Mathf.RoundToInt(area.y),
                Mathf.RoundToInt(Screen.width - area.xMax), Mathf.RoundToInt(Screen.height - area.yMax));
        }

        public void CloseSchoolPage() { HideAccountPage(); }

        public void Refresh()
        {
            if (view == null) return;
            if (Application.isMobilePlatform && Application.internetReachability == NetworkReachability.NotReachable)
            {
                networkWasUnavailable = true;
                Status = "인터넷 연결을 확인한 뒤 다시 시도해 주세요.";
                return;
            }
            // A false local flag can coexist with valid school cookies after an editor restart.
            // Visiting login directly then opens the school's "already logged in" dialog.
            if (!IsAuthenticated || !SchoolPage(currentUrl) ||
                new Uri(currentUrl).AbsolutePath.StartsWith("/login", StringComparison.OrdinalIgnoreCase))
            {
                accountPagePinned = false;
                Status = "기존 로그인 상태를 확인하고 있습니다…";
                view.LoadURL(NotificationPage);
            }
            else view.Reload();
        }

        private void Receive(string message)
        {
            if (string.IsNullOrEmpty(nonce) || string.IsNullOrEmpty(message) || message.Length > 64000 || !SchoolPage(currentUrl)) return;
            Capture data;
            try { data = JsonUtility.FromJson<Capture>(message); } catch { return; }
            if (data == null || data.nonce != nonce || data.page != currentUrl) return;
            if (ReceiveCoursework(data)) return;
            if (data.kind == "session")
            {
                if (!data.authenticated) return;
                IsAuthenticated = true;
                if (!accountPagePinned) HideAccountPage();
                return;
            }
            if (data.kind == "authentication")
            {
                nonce = null; // Reject late results from either collector after a session expires.
                IsAuthenticated = false;
                HasResult = false;
                HasMessagesResult = false;
                schoolResult = false;
                historyResult = false;
                notificationPriority = 0;
                historyStatus = "학교 로그인 후 알림 기록을 조회합니다.";
                titles.Clear(); messages.Clear();
                notificationDetails.Clear(); messageDetails.Clear(); selectedNotice = null;
                SelectCourse(null, null);
                courseCubes?.Clear();
                ClearCoursework();
                schoolStatus = "학교 로그인 계정 확인 필요";
                ShowAuthentication();
                Status = "SmartLEAD에 로그인하면 픽셀 캠퍼스로 이동합니다.";
                MessageStatus = "로그인 대기 중";
                return;
            }
            if (data.kind == "api_status")
            {
                if (data.source == "school_history") { historyStatus = "읽은 알림 포함 기록 조회 실패: " + data.error; return; }
                if (data.source == "school") { schoolStatus = "학교 알림 조회 실패: " + data.error; return; }
                if (schoolResult) return;
                if (data.source == "messages") MessageStatus = "쪽지 API 사용 불가: " + data.error;
                else apiStatus = "Moodle 알림 API 사용 불가: " + data.error;
                return;
            }
            if (schoolResult && data.source != "school_api" && data.source != "school_history" &&
                !(data.kind == "notifications" && data.source == "message_api")) return;
            if (data.source == "school_api") { schoolResult = true; schoolStatus = "학교 알림 응답 확인"; }
            if (data.kind == "messages")
            {
                MessageCount = Mathf.Clamp(data.count, 0, 9999);
                MessageTotal = Mathf.Clamp(data.total, 0, 9999);
                messages.Clear();
                if (data.items != null)
                    foreach (var item in data.items)
                        if (item != null && messages.Count < 30)
                        {
                            var sender = string.IsNullOrWhiteSpace(item.sender) ? "발신자 미확인" : item.sender;
                            var preview = string.IsNullOrWhiteSpace(item.title) ? "내용 없음" : item.title;
                            messages.Add(CourseLabel(item) + ReadLabel(item) + sender + ": " + preview);
                        }
                HasMessagesResult = data.found;
                if (data.found && !data.countOnly) ReplaceDetails(messageDetails, data.items, true);
                if (data.found && !data.countOnly) UpdateCourseCubes(data.items, true);
                MessageStatus = !data.found ? "쪽지 영역을 확인하지 못했습니다." : data.countOnly ?
                    "읽지 않은 쪽지 대화 " + MessageCount + "건 (서버) · 발신자 목록 미확인" :
                    (data.source == "school_api" ? "학교 알림 목록의 쪽지 " : "조회된 쪽지 ") + MessageTotal +
                    (data.historyLimited || data.unreadLimited ? "건 이상" : "건") +
                    (data.unreadUnknown ? " · 읽음 상태 미확인" : " · 읽지 않음 " + MessageCount + (data.unreadLimited ? "건 이상" : "건")) + " (서버)";
                if (data.found && authenticationVisible && !accountPagePinned)
                {
                    HideAccountPage();
                }
                return;
            }
            if (data.kind != "notifications") return;
            var priority = data.source == "school_history" ? 3 : data.source == "message_api" ? 2 : data.source == "school_api" ? 1 : 0;
            if (priority < notificationPriority) return;
            notificationPriority = priority;
            if (data.source == "school_history") { historyResult = true; historyStatus = "학교 알림 기록 응답 확인"; }
            if (data.source == "popup_api" || data.source == "message_api") apiStatus = "Moodle 알림 API 응답 확인";
            Count = Mathf.Clamp(data.count, 0, 9999);
            Total = Mathf.Clamp(data.total, 0, 9999);
            titles.Clear();
            if (data.items != null)
                foreach (var item in data.items)
                    if (item != null && !string.IsNullOrWhiteSpace(item.title) && titles.Count < 50)
                        titles.Add(CourseLabel(item) + ReadLabel(item) + item.title.Substring(0, Mathf.Min(200, item.title.Length)));
            HasResult = data.found;
            notificationsConfirmed = data.found && !data.historyLimited && !data.unreadLimited &&
                !data.countOnly && data.count <= (data.items?.Length ?? 0);
            if (data.found) ReplaceDetails(notificationDetails, data.items, false);
            if (data.found) UpdateCourseCubes(data.items, false);
            if (data.found && authenticationVisible && !accountPagePinned)
            {
                HideAccountPage();
            }
            else if (!IsAuthenticated && !data.found && !HasMessagesResult) ShowAuthentication();
            Status = !data.found ? "알림 영역 없음 (" + new Uri(currentUrl).AbsolutePath + "). " + apiStatus :
                (data.source == "school_history" ? "학교 알림 기록 (읽은 알림 포함) " :
                    data.source == "school_api" ? "학교 알림·쪽지 " : "Moodle 최근 알림 (읽은 알림 포함) ") + Total + "건" +
                (data.historyLimited ? " · 최근 목록 일부 표시" : "") +
                (data.unreadUnknown ? " · 읽음 상태 미확인" : " · 읽지 않음 " + Count + "건") + " (" +
                (data.source == "dom" ? "화면" : "서버") + ")";
        }

        private void ClearCoursework()
        {
            assignmentDetails.Clear(); enrolledCourses.Clear(); assignmentsConfirmed.Clear(); assignmentsFailed.Clear();
            scheduleDetails.Clear(); incomingSchedules.Clear(); courseworkFailed = false; scheduleAvailable = false; courseworkFinished = false; courseworkDeadline = 0;
            assignmentUpdates.Clear(); receivedCourses.Clear(); notificationsConfirmed = false; rosterPartial = false;
            courseworkStatus = "과목 · 과제 확인 중…";
        }

        private bool ReceiveCoursework(Capture data)
        {
            switch (data.kind)
            {
                case "schedule":
                    if (data.items != null) foreach (var item in data.items)
                        if (item != null && item.dueAt > 0 && SchoolPage(item.url))
                        {
                            incomingSchedules.Add(item);
                            scheduleDetails.RemoveAll(previous => SameActivity(previous.url, item.url));
                            scheduleDetails.Add(item);
                        }
                    break;
                case "schedule_complete":
                    scheduleAvailable = data.found;
                    if (data.found && !data.historyLimited)
                        scheduleDetails.RemoveAll(previous => !incomingSchedules.Exists(item => SameActivity(previous.url, item.url)));
                    break;
                case "coursework_complete":
                    courseworkDeadline = 0;
                    courseworkFinished = true;
                    courseworkFailed = enrolledCourses.Count == 0 || assignmentsFailed.Count > 0 ||
                        assignmentsConfirmed.Count + assignmentsFailed.Count < enrolledCourses.Count;
                    if (courseworkFailed && scheduleAvailable) courseworkStatus = "학교 일정 표시 중 · 제출 상태 확인 필요";
                    break;
                case "course":
                    if (data.items != null) foreach (var item in data.items)
                        if (item != null && item.courseId > 0) { enrolledCourses[item.courseId] = item; receivedCourses.Add(item.courseId); }
                    break;
                case "courses_complete":
                    rosterPartial = data.historyLimited;
                    if (!rosterPartial)
                    {
                        var removed = new List<int>();
                        foreach (var id in enrolledCourses.Keys) if (!receivedCourses.Contains(id)) removed.Add(id);
                        foreach (var id in removed) { enrolledCourses.Remove(id); assignmentDetails.RemoveAll(item => item.courseId == id); assignmentsConfirmed.Remove(id); assignmentsFailed.Remove(id); }
                    }
                    courseworkStatus = rosterPartial ? "일부 과목만 확인됨 · 전체 목록 확인 필요" : "수강 " + enrolledCourses.Count + "과목 · 과제 확인 중…";
                    break;
                case "assignments_begin":
                    assignmentUpdates[data.courseId] = new List<Notice>();
                    break;
                case "assignment":
                    if (data.items != null) foreach (var item in data.items)
                        if (item != null && assignmentUpdates.TryGetValue(item.courseId, out var pending)) pending.Add(item);
                    break;
                case "assignments_complete":
                    if (data.found && assignmentUpdates.TryGetValue(data.courseId, out var updates))
                    {
                        assignmentDetails.RemoveAll(item => item.courseId == data.courseId);
                        assignmentDetails.AddRange(updates);
                        assignmentDetails.Sort((a, b) => CompareDeadlines(a.dueAt, b.dueAt));
                        assignmentUpdates.Remove(data.courseId);
                        if (!data.historyLimited) { assignmentsConfirmed.Add(data.courseId); assignmentsFailed.Remove(data.courseId); }
                        else { assignmentsConfirmed.Remove(data.courseId); assignmentsFailed.Add(data.courseId); }
                    }
                    else { assignmentsConfirmed.Remove(data.courseId); assignmentsFailed.Add(data.courseId); }
                    courseworkStatus = "수강 " + enrolledCourses.Count + "과목 · 과제 확인 " + assignmentsConfirmed.Count + "과목" +
                        (assignmentsFailed.Count > 0 ? " · 확인 필요 " + assignmentsFailed.Count + "과목" : "") +
                        (rosterPartial ? " · 전체 과목 확인 필요" : "");
                    break;
                case "coursework_status":
                    courseworkFailed = true;
                    courseworkStatus = data.error;
                    foreach (var id in enrolledCourses.Keys) { assignmentsConfirmed.Remove(id); assignmentsFailed.Add(id); }
                    break;
                default: return false;
            }
            SyncCourseOverview();
            return true;
        }

        private static int CompareDeadlines(long left, long right) =>
            CampusScreenLayout.CompareDeadlines(left, right);

        private static bool SameActivity(string left, string right)
        {
            if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right)) return false;
            if (!Uri.TryCreate(left, UriKind.Absolute, out var a) || !Uri.TryCreate(right, UriKind.Absolute, out var b)) return false;
            if (a.Host != b.Host || a.AbsolutePath != b.AbsolutePath) return false;
            string ActivityId(Uri url)
            {
                foreach (var pair in url.Query.TrimStart('?').Split('&'))
                    if (pair.StartsWith("id=", StringComparison.Ordinal)) return pair.Substring(3);
                return null;
            }
            var id = ActivityId(a);
            return !string.IsNullOrEmpty(id) && id == ActivityId(b);
        }

        private List<Notice> PendingAssignments()
        {
            var result = assignmentDetails.FindAll(item => !item.submissionKnown || !item.submitted);
            foreach (var item in scheduleDetails)
            {
                // Calendars include old enrolments. Keep overdue work from the current
                // roster, and future deadlines when a partial roster cannot identify a course.
                if (enrolledCourses.Count > 0 && !enrolledCourses.ContainsKey(item.courseId) &&
                    item.dueAt <= DateTimeOffset.UtcNow.ToUnixTimeSeconds()) continue;
                if (!assignmentDetails.Exists(task => SameActivity(task.url, item.url))) result.Add(item);
            }
            result.Sort((a, b) => CompareDeadlines(a.dueAt, b.dueAt));
            return result;
        }

        private string NoticeCourseKey(Notice item)
        {
            if (item.courseId > 0 && enrolledCourses.TryGetValue(item.courseId, out var course))
                return CourseCubeNotice.CourseKey(course.courseId, course.courseName, course.courseCode);
            Notice match = null;
            foreach (var candidate in enrolledCourses.Values)
            {
                bool sameCode = !string.IsNullOrWhiteSpace(item.courseCode) && item.courseCode == candidate.courseCode;
                bool sameName = !string.IsNullOrWhiteSpace(item.courseName) &&
                    (candidate.courseName == item.courseName || candidate.courseName == item.courseName + " (" + item.courseCode + ")") &&
                    (string.IsNullOrWhiteSpace(candidate.courseCode) || string.IsNullOrWhiteSpace(item.courseCode) || sameCode);
                if (!sameCode && !sameName) continue;
                if (match != null) return CourseCubeNotice.CourseKey(item.courseId, item.courseName, item.courseCode);
                match = candidate;
            }
            if (match != null) return CourseCubeNotice.CourseKey(match.courseId, match.courseName, match.courseCode);
            return CourseCubeNotice.CourseKey(item.courseId, item.courseName, item.courseCode);
        }

        private void SyncCourseOverview()
        {
            if (courseCubes == null) return;
            var overview = new Dictionary<string, CourseCubeNotice>(StringComparer.Ordinal);
            foreach (var item in enrolledCourses.Values) AddOverview(overview, item);
            foreach (var item in notificationDetails)
            {
                var entry = AddOverview(overview, item);
                if (entry != null && (!item.readKnown || !item.read)) entry.notifications++;
            }
            foreach (var item in messageDetails)
            {
                var entry = AddOverview(overview, item);
                if (entry != null && (!item.readKnown || !item.read)) entry.messages++;
            }
            foreach (var item in PendingAssignments())
            {
                var entry = AddOverview(overview, item);
                if (entry == null || item.submissionKnown && item.submitted) continue;
                if (!item.submissionKnown) entry.unknownAssignments++; else entry.pendingAssignments++;
                if (entry.nextTaskTitle == null || CompareDeadlines(item.dueAt, entry.nextDue) < 0)
                { entry.nextDue = item.dueAt; entry.nextTaskTitle = item.title; }
            }
            courseCubes.SetOverview(overview.Values);
        }

        private CourseCubeNotice AddOverview(Dictionary<string, CourseCubeNotice> overview, Notice item)
        {
            var key = NoticeCourseKey(item);
            if (key == null) return null;
            if (!overview.TryGetValue(key, out var result))
            {
                var identity = item.courseId > 0 && enrolledCourses.TryGetValue(item.courseId, out var course) ? course : item;
                // Match notice course codes to the roster even when the notice omits its Moodle ID.
                foreach (var candidate in enrolledCourses.Values)
                    if (CourseCubeNotice.CourseKey(candidate.courseId, candidate.courseName, candidate.courseCode) == key) { identity = candidate; break; }
                result = new CourseCubeNotice { courseId = identity.courseId, courseName = identity.courseName,
                    courseCode = identity.courseCode, assignmentsKnown = assignmentsConfirmed.Contains(identity.courseId),
                    assignmentsFailed = assignmentsFailed.Contains(identity.courseId) || (courseworkFailed || courseworkFinished) && !assignmentsConfirmed.Contains(identity.courseId),
                    notificationsKnown = notificationsConfirmed };
                overview.Add(key, result);
            }
            return result;
        }

        private void ReplaceDetails(List<Notice> destination, Notice[] items, bool isMessage)
        {
            var previous = selectedNotice;
            bool replaceSelection = previous != null && destination.Contains(previous);
            destination.Clear();
            if (items != null)
                foreach (var item in items)
                    if (item != null && (isMessage || item.type != "message")) destination.Add(item);
            if (replaceSelection)
                selectedNotice = destination.Find(item => NoticeCourseKey(item) == NoticeCourseKey(previous) &&
                    (previous.id > 0 ? item.id == previous.id : item.title == previous.title && item.sender == previous.sender));
        }

        public void SelectCourse(string key, string title)
        {
            editingClassroom = false;
            selectedCourseKey = key;
            selectedCourseTitle = title;
            selectedNotice = string.IsNullOrEmpty(key) ? null :
                PendingAssignments().Find(item => NoticeCourseKey(item) == key) ??
                notificationDetails.Find(item => NoticeCourseKey(item) == key && (!item.readKnown || !item.read)) ??
                messageDetails.Find(item => NoticeCourseKey(item) == key);
        }

        public float AgendaHeight => (PendingAssignments().Count * 68 + 135) * CampusScreenLayout.Scale;

        public void DrawUpcomingAgenda(Rect area)
        {
            float scale = CampusScreenLayout.Scale;
            if (agendaLabel == null)
            {
                agendaLabel = new GUIStyle(GUI.skin.label) { wordWrap = true, richText = false };
                agendaButton = new GUIStyle(GUI.skin.button) { wordWrap = true, richText = false, alignment = TextAnchor.MiddleLeft };
            }
            agendaLabel.fontSize = Mathf.RoundToInt(12 * scale);
            agendaButton.fontSize = Mathf.RoundToInt(11 * scale);
            var tasks = PendingAssignments();
            GUILayout.BeginArea(area);
            GUILayout.Label("남은 과제 일정 · " + tasks.Count + "건", agendaLabel, GUILayout.Height(24 * scale));
            foreach (var item in tasks)
            {
                var title = item.title ?? "과제";
                if (title.Length > 54) title = title.Substring(0, 54) + "…";
                var date = item.dueAt > 0 && item.dueAt <= 253402300799L ?
                    DateTimeOffset.FromUnixTimeSeconds(item.dueAt).ToOffset(TimeSpan.FromHours(9)).ToString("MM/dd HH:mm") + " · " : "";
                var caption = (string.IsNullOrWhiteSpace(item.courseName) ? "과목 " + item.courseId : item.courseName) + " · " + title +
                    "\n" + date + CampusScreenLayout.RemainingTime(item.dueAt) +
                    (!item.submissionKnown ? " · 제출 확인 필요" : "");
                if (GUILayout.Button(caption, agendaButton, GUILayout.Height(64 * scale)))
                {
                    GetComponent<NotificationDetailUI>()?.OpenCourseNotifications(NoticeCourseKey(item), item.courseName);
                    selectedNotice = item;
                }
            }
            bool emptyConfirmed = enrolledCourses.Count > 0 && assignmentsConfirmed.Count == enrolledCourses.Count && !rosterPartial;
            GUILayout.Label(networkWasUnavailable ? Status : tasks.Count == 0 ? emptyConfirmed ? "남은 과제 없음" : courseworkStatus : "마감이 가까운 과제부터 표시합니다. 제출 상태 미확인 일정은 학교에서 확인해 주세요.",
                agendaLabel, GUILayout.Height(54 * scale));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("일정 다시 확인", GUILayout.Height(34 * scale))) Refresh();
            if (GUILayout.Button("학교 일정 보기", GUILayout.Height(34 * scale)))
            {
                if (view != null)
                {
                    accountPagePinned = true; ShowAuthentication();
                    view.LoadURL("https://smartlead.hallym.ac.kr/calendar/view.php?view=upcoming");
                }
            }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void OpenNoticePage(Notice item)
        {
            if (view == null || !SchoolPage(item.url)) return;
            accountPagePinned = true;
            ShowAuthentication();
            view.LoadURL(item.url);
        }

        private void DrawNotices(GUIStyle label)
        {
            if (!string.IsNullOrEmpty(selectedCourseKey))
            {
                GUILayout.Label(selectedCourseTitle + " · 과제 / 알림", label);
                DrawClassroom(label);
                if (GUILayout.Button("전체 알림 보기", touchButton)) SelectCourse(null, null);
            }
            int count = 0;
            DrawNoticeList(PendingAssignments(), "과제", label, ref count);
            DrawNoticeList(notificationDetails, "알림", label, ref count);
            DrawNoticeList(messageDetails, "쪽지", label, ref count);
            if (count == 0)
            {
                bool assignmentsKnown = enrolledCourses.Count > 0;
                foreach (var course in enrolledCourses.Values)
                    if ((string.IsNullOrEmpty(selectedCourseKey) || NoticeCourseKey(course) == selectedCourseKey) &&
                        !assignmentsConfirmed.Contains(course.courseId)) assignmentsKnown = false;
                GUILayout.Label(assignmentsKnown && notificationsConfirmed ? "알림 없음" : "알림 · 과제 확인 중이거나 조회가 필요합니다.", label);
            }
        }

        private string ClassroomCode(Notice item)
        {
            var key = NoticeCourseKey(item);
            return key != null && key.StartsWith("code:", StringComparison.Ordinal) ? key.Substring(5) : item.courseCode;
        }

        private void DrawClassroom(GUIStyle label)
        {
            // Use the same exact course/section identity as the notification filter.
            var code = selectedCourseKey.StartsWith("code:", StringComparison.Ordinal) ? selectedCourseKey.Substring(5) : null;
            if (code == null)
                foreach (var course in enrolledCourses.Values)
                    if (NoticeCourseKey(course) == selectedCourseKey) { code = course.courseCode; break; }
            GUILayout.Label(CourseClassrooms.Label(code), label);
            GUILayout.Label(CourseClassrooms.Source(code), label);
            if (string.IsNullOrWhiteSpace(code)) return;
            if (!editingClassroom && GUILayout.Button("강의실 입력 / 수정", touchButton))
            { classroomInput = CourseClassrooms.Room(code); editingClassroom = true; }
            if (!editingClassroom) return;
            GUILayout.Label(CourseClassrooms.Term + "학기 · 수강신청 시간표의 강의실 (요일별 여러 교실 입력 가능)", label);
            classroomInput = GUILayout.TextField(classroomInput, 120, GUILayout.MinHeight(34 * CampusScreenLayout.Scale));
            bool enabled = GUI.enabled;
            GUI.enabled = enabled && !string.IsNullOrWhiteSpace(classroomInput);
            if (GUILayout.Button("이 기기에 저장", touchButton))
            { CourseClassrooms.Save(code, classroomInput); editingClassroom = false; }
            GUI.enabled = enabled;
            if (GUILayout.Button("입력값 초기화", touchButton))
            { CourseClassrooms.Reset(code); classroomInput = CourseClassrooms.Room(code); editingClassroom = false; }
            if (GUILayout.Button("취소", touchButton)) editingClassroom = false;
        }

        private void DrawNoticeList(List<Notice> items, string kind, GUIStyle label, ref int count)
        {
            foreach (var item in items)
            {
                if (!string.IsNullOrEmpty(selectedCourseKey) && NoticeCourseKey(item) != selectedCourseKey) continue;
                bool assignment = kind == "과제";
                if (assignment ? item.submissionKnown && item.submitted : item.readKnown && item.read) continue;
                count++;
                string title = string.IsNullOrWhiteSpace(item.title) ? "제목 없음" : item.title;
                long due = item.dueAt;
                if (!assignment && due <= 0 && !string.IsNullOrEmpty(item.url))
                {
                    var task = PendingAssignments().Find(candidate => SameActivity(candidate.url, item.url));
                    if (task != null) due = task.dueAt;
                }
                var time = CampusScreenLayout.RemainingTime(due);
                if (GUILayout.Button(kind + " · " + title + "\n" + time +
                    (assignment && !item.submissionKnown ? " · 제출 확인 필요" : ""), touchButton))
                    selectedNotice = ReferenceEquals(selectedNotice, item) ? null : item;
                if (!ReferenceEquals(selectedNotice, item)) continue;
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label(CourseLabel(item) + (assignment ? item.submissionKnown ? "미제출" : "제출 확인 필요" : ReadLabel(item)), label);
                GUILayout.Label(CourseClassrooms.Label(ClassroomCode(item)), label);
                GUILayout.Label(time, label);
                if (due > 0 && due <= 253402300799L)
                    GUILayout.Label("마감: " + DateTimeOffset.FromUnixTimeSeconds(due).ToOffset(TimeSpan.FromHours(9)).ToString("yyyy-MM-dd HH:mm") + " (한국 시간)", label);
                if (!string.IsNullOrWhiteSpace(item.sender)) GUILayout.Label("보낸 사람: " + item.sender, label);
                GUILayout.Label(title, label);
                GUILayout.Label(string.IsNullOrWhiteSpace(item.body) ?
                    "학교에서 본문을 제공하지 않았습니다. 학교 알림 기록에서 상세 내용을 확인해 주세요." : item.body, label);
                if (SchoolPage(item.url) && GUILayout.Button("학교에서 원문 보기", touchButton)) OpenNoticePage(item);
                GUILayout.EndVertical();
            }
        }

        public void Draw(GUIStyle label)
        {
            if (touchButton == null) touchButton = new GUIStyle(GUI.skin.button);
            touchButton.fontSize = Mathf.RoundToInt(12 * CampusScreenLayout.Scale);
            touchButton.wordWrap = true;
            touchButton.fixedHeight = 0;
            touchButton.padding = new RectOffset(8, 8, Mathf.RoundToInt(8 * CampusScreenLayout.Scale), Mathf.RoundToInt(8 * CampusScreenLayout.Scale));
            GUILayout.Label("과제 · 알림", label);
            DrawNotices(label);
            GUILayout.Label(courseworkStatus, label);
            GUILayout.Label(Status, label);
            GUILayout.Label(MessageStatus, label);
            if (!schoolResult) GUILayout.Label(schoolStatus, label);
            if (!historyResult) GUILayout.Label(historyStatus, label);
            if (GUILayout.Button("다시 확인", touchButton)) Refresh();
            if (GUILayout.Button("학교 로그인 / 계정 확인", touchButton)) OpenAccountPage();
            if (GUILayout.Button("학교 알림 기록 보기", touchButton)) OpenHistoryPage();
            if (accountPagePinned && (HasResult || HasMessagesResult) && GUILayout.Button("학교 화면 닫기", touchButton)) HideAccountPage();
        }

        private void OnDestroy() { if (view != null) Destroy(view.gameObject); }
    }
}



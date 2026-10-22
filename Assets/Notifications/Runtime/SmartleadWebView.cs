using System;
using System.Collections.Generic;
using Gree.UnityWebView;
using UnityEngine;

namespace WebNotifications
{
    [RequireComponent(typeof(NotificationManager))]
    public class SmartleadWebView : MonoBehaviour
    {
        [Serializable] private class Attachment { public string name, url; }
        [Serializable] private class Notice { public int id, courseId, week; public long dueAt, createdAt, startAt, modifiedAt, lateDueAt; public string sender, title, body, url, courseName, courseCode, type, submissionStatus, gradingStatus, submissionText, attendanceStatus, progressText, availabilityText; public bool read, readKnown, submitted, submissionKnown, detailLoaded, submissionFilesKnown, detailTruncated, attendanceKnown, attended, attendancePeriodKnown, attendanceStale; public Attachment[] submittedFiles, instructionFiles, instructionImages; }
        [Serializable] private class Capture { public string kind, nonce, page, source, error, username, password, requestId, html, url; public int count, total, courseId, index; public bool found, unreadLimited, historyLimited, countOnly, unreadUnknown, authenticated, remember, automatic, passwordEntered; public Notice[] items; }
        private const string RememberIdKey = "smartlead.remember-id";
        private const string SavedIdKey = "smartlead.saved-id";
        private const string AutomaticLoginKey = "smartlead.automatic-login";
        private const string RequirePasswordKey = "smartlead.require-password";
        private readonly Dictionary<string, string> loginMessagePages = new Dictionary<string, string>();
        private bool RequiresPassword => PlayerPrefs.GetInt(RequirePasswordKey, 0) == 1;
        private string pendingUsername, pendingPassword;
        private bool automaticLoginAttempted;
        private bool signingOut;
        private float logoutDeadline;
        [Serializable] private class LoginSettings { public string nonce, username, password; public bool remember, automatic, autoSubmit, secureStorageAvailable, requirePasswordEntry; }
        [Serializable] private class DetailRequest { public string requestId; public Notice item; }
        private bool showSubmittedAssignments;
        private WebViewObject assignmentView;
        private bool assignmentVisible, assignmentAttachmentVisible;
        private bool assignmentSubmissionVisible, refreshAssignmentAfterSubmission;
        private SubmissionConversionBridge submissionConversion;
        private bool submissionConversionPrepared;
        [Serializable] private class ConversionSettings { public string nonce; public bool available; }
        private Notice assignmentDocumentTarget;
        private string assignmentDocumentRequest, assignmentDocumentNonce, assignmentDocumentHtml;
        private string pendingAssignmentHtml;
        public string AssignmentRenderStatus { get; private set; }
        private float assignmentRendererDeadline;
        private string[] assignmentDocumentParts;
        private float assignmentDocumentDeadline;
        public bool IsAssignmentDetailVisible => assignmentVisible;
        public bool IsAssignmentSubmissionVisible => assignmentSubmissionVisible;
        public string AssignmentDetailTitle => assignmentSubmissionVisible ? "과제 파일 첨부·제출" : assignmentAttachmentVisible ? "과제 첨부파일" : assignmentDocumentTarget?.title ?? "과제 상세";
        private const string NotificationPage = "https://smartlead.hallym.ac.kr/local/ubion/user/index.php";
        private WebViewObject view;
        private SmartleadCourseCubes courseCubes;
        private string nonce, currentUrl, script;
        private float nextRefresh;
        private bool applicationPaused, resumeRefreshPending, networkWasUnavailable;
        private float resumeRefreshAt;
        private bool authenticationVisible;
        private bool websiteNoticesVisible;
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
        private readonly List<Notice> lectureDetails = new List<Notice>();
        private readonly Dictionary<int, List<Notice>> lectureUpdates = new Dictionary<int, List<Notice>>();
        private readonly HashSet<int> lecturesConfirmed = new HashSet<int>(), lecturesFailed = new HashSet<int>();
        private string lectureStatus = "동영상 출석 일정 확인 중…";
        private float lectureDeadline;
        private bool lecturePageVisible, showCompletedLectures;
        public bool IsLecturePageVisible => lecturePageVisible;
        private readonly List<Notice> incomingSchedules = new List<Notice>();
        private readonly Dictionary<int, Notice> enrolledCourses = new Dictionary<int, Notice>();
        private readonly Dictionary<int, Notice> incomingCourses = new Dictionary<int, Notice>();
        private bool currentRosterKnown;
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
        public bool IsSchoolPageVisible => authenticationVisible || assignmentVisible;
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
            var lectures = Resources.Load<TextAsset>("SmartleadVideoAttendance");
            if (lectures != null) script += "\n" + lectures.text;
            else lectureStatus = "동영상 출석 수집 파일을 불러오지 못했습니다.";
            var coursework = Resources.Load<TextAsset>("SmartleadCoursework");
            if (coursework != null) script += "\n" + coursework.text;
            else { courseworkFailed = true; courseworkStatus = "과제 수집 파일을 불러오지 못했습니다."; }
            try
            {
                view = new GameObject("SmartLEAD Notification WebView").AddComponent<WebViewObject>();
                view.transform.SetParent(transform);
                view.Init(cb: Receive, err: _ => { Status = "SmartLEAD 연결 실패"; if (assignmentSubmissionVisible) AssignmentRenderStatus = "학교 제출 화면 연결에 실패했습니다. 뒤로 돌아가 다시 열어 주세요."; },
                    httpErr: _ => { Status = "SmartLEAD 페이지 오류"; if (assignmentSubmissionVisible) AssignmentRenderStatus = "학교 제출 화면에서 오류가 발생했습니다. 학교 화면의 안내를 확인해 주세요."; },
                    started: url => { currentUrl = url; nonce = null; HasResult = false; HasMessagesResult = false;
                        if (assignmentSubmissionVisible) { submissionConversionPrepared = false; submissionConversion?.Dispose(); return; }
                        if (lecturePageVisible) return;
                        if (SchoolPage(url) && new Uri(url).AbsolutePath.Equals("/login/logout.php", StringComparison.OrdinalIgnoreCase))
                        {
                            RequireFreshLogin();
                            PlayerPrefs.SetInt(AutomaticLoginKey, 0); PlayerPrefs.Save();
                            SmartleadCredentialStore.Delete(); pendingPassword = null; automaticLoginAttempted = true;
                        }
                        notificationsConfirmed = false;
                        schoolResult = false; schoolStatus = "학교 알림 조회 중"; apiStatus = "Moodle 알림 API 확인 중";
                        historyResult = false; notificationPriority = 0; historyStatus = "읽은 알림 포함 기록 조회 중";
                        titles.Clear(); messages.Clear();
                        Status = "알림 확인 중"; MessageStatus = "쪽지 확인 중"; },
                    ld: url => { currentUrl = url; Inject(); }, androidForceDarkMode: 1);
                ShowAuthentication();
                // An explicit logout must also survive an editor/app restart. Discard any
                // remaining local cookies before starting a fresh school login.
                if (RequiresPassword)
                {
                    view.ClearCookies(); view.SaveCookies();
                    view.LoadURL("https://smartlead.hallym.ac.kr/login.php");
                }
                else view.LoadURL(NotificationPage);
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
                view?.SaveCookies();
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
            if (signingOut) return;
            if (assignmentVisible) { CloseAssignmentDetail(); return; }
            if (lecturePageVisible) { CloseSchoolPage(); return; }
            var detail = GetComponent<NotificationDetailUI>();
            if (detail != null && detail.IsNotificationPanelOpen) { detail.CloseNotifications(); return; }
            if (view != null && authenticationVisible)
            {
                // The Android IME normally consumes its own Back event. Do not
                // navigate away from an input if an Escape also reaches Unity.
                if (view.IsKeyboardVisible) return;
                if (IsAuthenticated) { CloseSchoolPage(); return; }
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

        private static bool LoginPage(string value)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var url) || url.Scheme != "https" ||
                url.Port != 443 || !string.IsNullOrEmpty(url.UserInfo)) return false;
            return (url.Host == "smartlead.hallym.ac.kr" &&
                (url.AbsolutePath == "/login/index.php" || url.AbsolutePath == "/login.php")) ||
                (url.Host == "sso.hallym.ac.kr" && url.AbsolutePath.Equals("/Account/LogIn", StringComparison.OrdinalIgnoreCase));
        }

        private void Inject()
        {
            // Keep the school's player and tracking scripts intact while studying.
            if (lecturePageVisible) return;
            // Keep the live school file picker, scripts, session and save forms intact.
            // Collectors must not navigate or replace a submission in progress.
            if (assignmentSubmissionVisible)
            {
                AssignmentRenderStatus = null;
                if (!submissionConversionPrepared && SchoolPage(currentUrl))
                {
                    var prepare = Resources.Load<TextAsset>("SmartleadSubmissionPrepare");
                    var worker = Resources.Load<TextAsset>("SubmissionHancomWorker");
                    if (prepare != null && worker != null)
                    {
                        var settings = new ConversionSettings { nonce = Guid.NewGuid().ToString("N"), available = SubmissionDocumentConverter.IsAvailable };
                        if (submissionConversion == null) submissionConversion = new SubmissionConversionBridge();
                        submissionConversion.Configure(settings.nonce, currentUrl, worker.text);
                        submissionConversionPrepared = true;
                        view.EvaluateJS(prepare.text.Replace("__SUBMISSION_CONVERSION_SETTINGS__", JsonUtility.ToJson(settings)));
                    }
                }
                return;
            }
            if (!SchoolPage(currentUrl) || new Uri(currentUrl).AbsolutePath.StartsWith("/login", StringComparison.OrdinalIgnoreCase))
            {
                if (refreshAssignmentAfterSubmission) DismissAssignmentDetail();
                IsAuthenticated = false;
                courseCubes?.Clear();
                notificationDetails.Clear(); messageDetails.Clear(); SelectCourse(null, null);
                ClearCoursework();
                accountPagePinned = false;
                returnFromLogin = true;
                ShowAuthentication();
                Status = "SmartLEAD에 로그인하면 픽셀 캠퍼스로 이동합니다.";
                if (LoginPage(currentUrl))
                {
                    var loginScript = Resources.Load<TextAsset>("SmartleadLogin");
                    if (loginScript != null)
                    {
                        nonce = Guid.NewGuid().ToString("N");
                        // Submit callbacks can arrive after navigation has already changed currentUrl/nonce.
                        // Retain the issued login document token until a real new form or logout supersedes it.
                        if (loginMessagePages.Count >= 8) loginMessagePages.Clear();
                        loginMessagePages[nonce] = currentUrl;
                        var settings = new LoginSettings { nonce = nonce,
                            remember = PlayerPrefs.GetInt(RememberIdKey, 1) == 1,
                            username = PlayerPrefs.GetString(SavedIdKey, ""), secureStorageAvailable = SmartleadCredentialStore.IsSupported,
                            requirePasswordEntry = RequiresPassword };
                        settings.automatic = !RequiresPassword && PlayerPrefs.GetInt(AutomaticLoginKey, 0) == 1 && settings.secureStorageAvailable;
                        if (settings.automatic && SmartleadCredentialStore.TryLoad(out var savedUsername, out var savedPassword))
                        {
                            settings.username = savedUsername; settings.password = savedPassword;
                            settings.autoSubmit = !automaticLoginAttempted;
                            automaticLoginAttempted = true;
                        }
                        view.EvaluateJS(loginScript.text.Replace("__LOGIN_SETTINGS__", JsonUtility.ToJson(settings)));
                    }
                }
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
            incomingCourses.Clear();
            assignmentUpdates.Clear();
            incomingSchedules.Clear();
            courseworkDeadline = Time.unscaledTime + 45;
            courseworkFinished = false;
            lectureDeadline = Time.unscaledTime + 90;
            lectureUpdates.Clear(); lecturesConfirmed.Clear(); lecturesFailed.Clear();
            lectureStatus = "동영상 출석 일정 확인 중…";
            view.EvaluateJS(script.Replace("__UNITY_NONCE__", nonce));
        }

        private void Update()
        {
            if (applicationPaused) return;
            if (signingOut)
            {
                if (Time.unscaledTime >= logoutDeadline) FinishLogout();
                return;
            }
#if UNITY_ANDROID && !UNITY_EDITOR
            if (Input.GetKeyDown(KeyCode.Escape)) { HandleMobileBack(); return; }
#endif
            if (view == null) return;
            if (lecturePageVisible) { ApplyWebViewMargins(); return; }
            if (assignmentVisible)
            {
                if (assignmentSubmissionVisible) submissionConversion?.Poll(view.EvaluateJS);
                ApplyWebViewMargins();
                if (pendingAssignmentHtml != null && assignmentView != null && assignmentView.IsInitialized())
                {
                    var html = pendingAssignmentHtml; pendingAssignmentHtml = null;
                    assignmentRendererDeadline = Time.unscaledTime + 10;
                    assignmentView.LoadHTML(html, "https://smartlead.hallym.ac.kr/");
                }
                if (assignmentRendererDeadline > 0 && Time.unscaledTime >= assignmentRendererDeadline)
                {
                    assignmentRendererDeadline = 0;
                    AssignmentRenderStatus = "과제 화면을 표시하지 못했습니다. 새로고침을 눌러 다시 시도해 주세요.";
                }
                if (assignmentDocumentDeadline > 0 && Time.unscaledTime >= assignmentDocumentDeadline)
                {
                    assignmentDocumentDeadline = 0; assignmentDocumentRequest = null;
                    RenderAssignmentDocument("<p class='notice'>과제 내용을 불러오는 데 시간이 걸리고 있습니다. 위의 새로고침을 눌러 다시 시도해 주세요.</p>");
                }
                return;
            }
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
            if (lectureDeadline > 0 && Time.unscaledTime >= lectureDeadline)
            {
                lectureDeadline = 0;
                lectureStatus = "동영상 출석 조회가 지연되고 있습니다. 다시 확인해 주세요.";
                foreach (var id in enrolledCourses.Keys) if (!lecturesConfirmed.Contains(id)) lecturesFailed.Add(id);
                foreach (var item in lectureDetails) if (!lecturesConfirmed.Contains(item.courseId)) item.attendanceStale = true;
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
            view.SetVisibility(!websiteNoticesVisible && !signingOut);
        }

        public void SetWebsiteNoticesVisible(bool visible)
        {
            websiteNoticesVisible = visible;
            view?.SetVisibility(!visible && (authenticationVisible || assignmentSubmissionVisible) && !signingOut);
            assignmentView?.SetVisibility(!visible && assignmentVisible && !assignmentSubmissionVisible);
        }

        public void OpenAccountPage()
        {
            if (view == null) return;
            accountPagePinned = true;
            ShowAuthentication();
            view.LoadURL(NotificationPage);
        }

        public void Logout()
        {
            if (signingOut) return;
            lecturePageVisible = false;
            DismissAssignmentDetail();
            RequireFreshLogin();
            ForgetSavedLogin();
            automaticLoginAttempted = true;
            signingOut = true;
            logoutDeadline = Time.unscaledTime + 10;
            IsAuthenticated = false; HasResult = HasMessagesResult = false;
            Count = Total = MessageCount = MessageTotal = 0;
            titles.Clear(); messages.Clear(); notificationDetails.Clear(); messageDetails.Clear();
            SelectCourse(null, null); ClearCoursework(); courseCubes?.Clear();
            accountPagePinned = false; returnFromLogin = true;
            resumeRefreshPending = false;
            Status = "로그아웃 중…"; MessageStatus = "로그인 대기 중";
            ShowAuthentication();
            var logoutScript = Resources.Load<TextAsset>("SmartleadLogout");
            if (view != null && SchoolPage(currentUrl) && !string.IsNullOrEmpty(nonce) && logoutScript != null)
                view.EvaluateJS(logoutScript.text.Replace("__UNITY_NONCE__", nonce));
            else FinishLogout();
        }

        private void ForgetSavedLogin()
        {
            PlayerPrefs.DeleteKey(SavedIdKey);
            PlayerPrefs.SetInt(RememberIdKey, 0);
            PlayerPrefs.SetInt(AutomaticLoginKey, 0);
            PlayerPrefs.Save();
            SmartleadCredentialStore.Delete(); pendingUsername = pendingPassword = null;
        }

        private void RequireFreshLogin()
        {
            loginMessagePages.Clear();
            PlayerPrefs.SetInt(RequirePasswordKey, 1);
            PlayerPrefs.Save();
        }

        private void FinishLogout()
        {
            if (!signingOut) return;
            signingOut = false; nonce = null;
            view?.ClearCookies(); // Includes the app WebView's Hallym SSO cookies, preventing silent re-login.
            view?.SaveCookies();
            Status = "로그아웃했습니다. 학교 계정으로 다시 로그인해 주세요.";
            ShowAuthentication();
            view?.LoadURL("https://smartlead.hallym.ac.kr/login.php");
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
            courseCubes?.SetVisible(!assignmentVisible);
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
            assignmentView?.SetMargins(Mathf.RoundToInt(area.x), Mathf.RoundToInt(area.y),
                Mathf.RoundToInt(Screen.width - area.xMax), Mathf.RoundToInt(Screen.height - area.yMax));
        }

        public void CloseSchoolPage()
        {
            if (lecturePageVisible)
            {
                lecturePageVisible = false; resumeRefreshPending = false;
                // Navigate away only after the user leaves, so the school player
                // can flush its normal playback record during unload.
                HideAccountPage(); view?.LoadURL(NotificationPage);
                return;
            }
            HideAccountPage();
        }

        public void Refresh()
        {
            if (signingOut || assignmentSubmissionVisible || lecturePageVisible) return;
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
            if (string.IsNullOrEmpty(message) || message.Length > 64000) return;
            Capture data;
            try { data = JsonUtility.FromJson<Capture>(message); } catch { return; }
            if (data == null) return;
            if (data.kind == "submission_convert")
            {
                if (assignmentSubmissionVisible && SchoolPage(currentUrl)) submissionConversion?.Receive(message, currentUrl);
                return;
            }
            if (data.kind == "login_ready" || data.kind == "login_existing_session" || data.kind == "login_preferences" || data.kind == "login_submit")
            {
                if (signingOut || string.IsNullOrEmpty(data.nonce) || !LoginPage(data.page) ||
                    !loginMessagePages.TryGetValue(data.nonce, out var loginPage) || loginPage != data.page) return;
                if (!(SchoolPage(currentUrl) || (Uri.TryCreate(currentUrl, UriKind.Absolute, out var navigation) &&
                    navigation.Scheme == "https" && navigation.Host == "sso.hallym.ac.kr" && navigation.Port == 443 && string.IsNullOrEmpty(navigation.UserInfo)))) return;
                if (data.kind == "login_ready")
                {
                    loginMessagePages.Clear(); loginMessagePages[data.nonce] = data.page;
                    pendingUsername = pendingPassword = null;
                    return;
                }
                if (data.kind == "login_existing_session")
                {
                    // The school already has a session. Verify it on a protected page;
                    // never automatically log out a successful SSO callback.
                    view?.LoadURL(NotificationPage);
                    return;
                }
                PlayerPrefs.SetInt(RememberIdKey, data.remember ? 1 : 0);
                bool automatic = data.automatic && data.remember && SmartleadCredentialStore.IsSupported;
                PlayerPrefs.SetInt(AutomaticLoginKey, automatic ? 1 : 0);
                if (!automatic) { SmartleadCredentialStore.Delete(); pendingPassword = null; }
                if (!data.remember) { PlayerPrefs.DeleteKey(SavedIdKey); pendingUsername = null; }
                else if (data.kind == "login_submit")
                {
                    automaticLoginAttempted |= automatic;
                    pendingUsername = !string.IsNullOrWhiteSpace(data.username) && data.username.Length <= 256 ? data.username.Trim() : null;
                    pendingPassword = automatic && !string.IsNullOrEmpty(data.password) && data.password.Length <= 2048 ? data.password : null;
                }
                data.password = null;
                PlayerPrefs.Save();
                return;
            }
            if (string.IsNullOrEmpty(nonce) || data.nonce != nonce || data.page != currentUrl || !SchoolPage(currentUrl)) return;
            if (signingOut)
            {
                if (data.kind == "logout_complete") FinishLogout();
                return;
            }
            if (data.kind == "assignment_document" || data.kind == "assignment_document_error")
            {
                if (!assignmentVisible || string.IsNullOrEmpty(assignmentDocumentRequest) || data.requestId != assignmentDocumentRequest) return;
                if (data.kind == "assignment_document_error")
                {
                    assignmentDocumentDeadline = 0; assignmentDocumentRequest = null;
                    RenderAssignmentDocument("<p class='notice'>" + System.Net.WebUtility.HtmlEncode(data.error) + "</p>");
                    return;
                }
                if (assignmentDocumentTarget == null || !SameActivity(data.url, assignmentDocumentTarget.url) || data.total < 1 || data.total > 525 ||
                    data.index < 0 || data.index >= data.total || data.html == null || data.html.Length > 8000) return;
                if (assignmentDocumentParts == null) assignmentDocumentParts = new string[data.total];
                if (assignmentDocumentParts.Length != data.total) return;
                assignmentDocumentParts[data.index] = data.html;
                if (Array.Exists(assignmentDocumentParts, part => part == null)) return;
                assignmentDocumentDeadline = 0; assignmentDocumentRequest = null;
                RenderAssignmentDocument(string.Concat(assignmentDocumentParts));
                assignmentDocumentParts = null;
                return;
            }
            if (ReceiveCoursework(data)) return;
            if (data.kind == "session")
            {
                if (!data.authenticated) return;
                PlayerPrefs.DeleteKey(RequirePasswordKey); PlayerPrefs.Save();
                loginMessagePages.Clear();
                IsAuthenticated = true;
                // Commit the ID only after the school confirms authentication.
                if (!string.IsNullOrEmpty(pendingUsername) && PlayerPrefs.GetInt(RememberIdKey, 1) == 1)
                {
                    PlayerPrefs.SetString(SavedIdKey, pendingUsername);
                    PlayerPrefs.Save();
                }
                if (!string.IsNullOrEmpty(pendingPassword) && PlayerPrefs.GetInt(AutomaticLoginKey, 0) == 1)
                {
                    if (!SmartleadCredentialStore.Save(pendingUsername, pendingPassword))
                    { PlayerPrefs.SetInt(AutomaticLoginKey, 0); PlayerPrefs.Save(); Status = "자동 로그인 정보를 안전하게 저장하지 못했습니다. 계정 화면에서 다시 설정해 주세요."; }
                }
                pendingUsername = null; pendingPassword = null;
                view?.SaveCookies();
                if (!accountPagePinned) HideAccountPage();
                if (refreshAssignmentAfterSubmission && assignmentVisible)
                {
                    refreshAssignmentAfterSubmission = false;
                    RefreshAssignmentDetail();
                }
                return;
            }
            if (data.kind == "authentication")
            {
                if (refreshAssignmentAfterSubmission) DismissAssignmentDetail();
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
            DismissAssignmentDetail();
            assignmentDetails.Clear(); enrolledCourses.Clear(); assignmentsConfirmed.Clear(); assignmentsFailed.Clear();
            scheduleDetails.Clear(); incomingSchedules.Clear(); courseworkFailed = false; scheduleAvailable = false; courseworkFinished = false; courseworkDeadline = 0;
            lectureDetails.Clear(); lectureUpdates.Clear(); lecturesConfirmed.Clear(); lecturesFailed.Clear();
            lectureDeadline = 0; lectureStatus = "동영상 출석 일정 확인 중…";
            assignmentUpdates.Clear(); receivedCourses.Clear(); incomingCourses.Clear(); currentRosterKnown = false;
            notificationsConfirmed = false; rosterPartial = false;
            courseworkStatus = "과목 · 과제 확인 중…";
        }

        private bool ReceiveCoursework(Capture data)
        {
            switch (data.kind)
            {
                case "lectures_begin":
                    lectureDeadline = Time.unscaledTime + 90;
                    lectureStatus = "동영상 출석 일정 확인 중…";
                    break;
                case "lectures_course_begin":
                    lectureUpdates[data.courseId] = new List<Notice>();
                    break;
                case "lecture":
                    if (data.items != null) foreach (var item in data.items)
                        if (item != null && IsLectureUrl(item.url) && lectureUpdates.TryGetValue(item.courseId, out var pendingLectures))
                        {
                            if (item.startAt <= 0 || item.dueAt <= item.startAt || item.dueAt > 253402300799L) { item.startAt = item.dueAt = 0; item.attendancePeriodKnown = false; }
                            if (item.lateDueAt <= item.dueAt || item.lateDueAt > 253402300799L) item.lateDueAt = 0;
                            pendingLectures.Add(item);
                        }
                    break;
                case "lectures_complete":
                    foreach (var previous in lectureDetails) if (previous.courseId == data.courseId) previous.attendanceStale = true;
                    bool snapshotReceived = lectureUpdates.TryGetValue(data.courseId, out var lectureSnapshot);
                    if (data.found && snapshotReceived)
                    {
                        if (!data.historyLimited) lectureDetails.RemoveAll(item => item.courseId == data.courseId);
                        foreach (var item in lectureSnapshot)
                        {
                            lectureDetails.RemoveAll(previous => SameActivity(previous.url, item.url));
                            lectureDetails.Add(item);
                        }
                        lectureUpdates.Remove(data.courseId);
                    }
                    if (data.found && snapshotReceived && !data.historyLimited) { lecturesConfirmed.Add(data.courseId); lecturesFailed.Remove(data.courseId); }
                    else { lecturesConfirmed.Remove(data.courseId); lecturesFailed.Add(data.courseId); }
                    lectureStatus = "동영상 확인 " + lecturesConfirmed.Count + "/" + enrolledCourses.Count + "과목" +
                        (lecturesFailed.Count > 0 ? " · 일부 출석 기간 확인 필요" : "");
                    break;
                case "lectures_scan_complete":
                    lectureDeadline = 0;
                    if (!data.found)
                    {
                        lectureStatus = "동영상 출석 일정 조회 실패 · 학교에서 확인해 주세요.";
                        foreach (var id in enrolledCourses.Keys) if (!lecturesConfirmed.Contains(id)) lecturesFailed.Add(id);
                        foreach (var item in lectureDetails) if (!lecturesConfirmed.Contains(item.courseId)) item.attendanceStale = true;
                    }
                    break;
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
                        if (item != null && item.courseId > 0) { incomingCourses[item.courseId] = item; receivedCourses.Add(item.courseId); }
                    break;
                case "courses_complete":
                    rosterPartial = data.historyLimited;
                    // Replace the snapshot atomically, including a partial current roster.
                    // Retaining missing records here used to keep old semesters forever.
                    var removed = new List<int>();
                    foreach (var id in enrolledCourses.Keys) if (!receivedCourses.Contains(id)) removed.Add(id);
                    foreach (var id in removed) { enrolledCourses.Remove(id); assignmentDetails.RemoveAll(item => item.courseId == id); assignmentsConfirmed.Remove(id); assignmentsFailed.Remove(id); lectureDetails.RemoveAll(item => item.courseId == id); lecturesConfirmed.Remove(id); lecturesFailed.Remove(id); }
                    foreach (var course in incomingCourses) enrolledCourses[course.Key] = course.Value;
                    currentRosterKnown = true;
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
            var result = assignmentDetails.FindAll(item => (!item.submissionKnown || !item.submitted) && IsCurrentCourse(item));
            foreach (var item in scheduleDetails)
            {
                // Calendars include old enrolments. Keep overdue work from the current
                // roster, and future deadlines only while the roster is unavailable.
                if (currentRosterKnown ? !IsCurrentCourse(item) : item.dueAt <= DateTimeOffset.UtcNow.ToUnixTimeSeconds()) continue;
                if (!assignmentDetails.Exists(task => SameActivity(task.url, item.url))) result.Add(item);
            }
            result.Sort((a, b) => CompareDeadlines(a.dueAt, b.dueAt));
            return result;
        }

        private List<Notice> PendingLectures()
        {
            var items = lectureDetails.FindAll(item => IsCurrentCourse(item) && (item.attendanceStale || !item.attendanceKnown || !item.attended));
            items.Sort((a, b) => CompareDeadlines(a.dueAt, b.dueAt));
            return items;
        }

        private static bool IsLectureUrl(string value)
        {
            if (!SchoolPage(value)) return false;
            var path = new Uri(value).AbsolutePath;
            return (path == "/mod/vod/view.php" || path == "/mod/vod/viewer.php" || path == "/mod/ubvod/view.php" || path == "/mod/ubvod/viewer.php") &&
                System.Text.RegularExpressions.Regex.IsMatch(new Uri(value).Query, @"^\?id=[1-9]\d*$");
        }

        private string NoticeCourseKey(Notice item)
        {
            if (item.courseId > 0 && enrolledCourses.TryGetValue(item.courseId, out var course))
                return CourseCubeNotice.CourseKey(course.courseId, course.courseName, course.courseCode);
            // A positive Moodle course ID identifies a specific offering. Never
            // relabel an old offering using a matching name/code from a new semester.
            if (item.courseId > 0) return "id:" + item.courseId;
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

        private bool IsCurrentCourse(Notice item)
        {
            if (item == null) return false;
            if (item.courseId > 0) return enrolledCourses.ContainsKey(item.courseId);
            var key = NoticeCourseKey(item);
            if (key == null) return false;
            foreach (var course in enrolledCourses.Values)
                if (CourseCubeNotice.CourseKey(course.courseId, course.courseName, course.courseCode) == key) return true;
            return false;
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
                entry.deadlines.Add(new CampusScreenLayout.DeadlinePeriod { startAt = item.startAt, dueAt = item.dueAt });
                if (!item.submissionKnown) entry.unknownAssignments++; else entry.pendingAssignments++;
                if (entry.nextTaskTitle == null || CompareDeadlines(item.dueAt, entry.nextDue) < 0)
                { entry.nextDue = item.dueAt; entry.nextTaskTitle = item.title; }
            }
            foreach (var item in PendingLectures())
            {
                var entry = AddOverview(overview, item);
                if (entry == null) continue;
                entry.pendingLectures++;
                if (!item.attendanceKnown || item.attendanceStale) entry.unknownLectures++;
                if (item.attendancePeriodKnown) entry.deadlines.Add(new CampusScreenLayout.DeadlinePeriod { startAt = item.startAt, dueAt = item.dueAt });
                if (entry.nextTaskTitle == null || CompareDeadlines(item.dueAt, entry.nextDue) < 0)
                { entry.nextDue = item.dueAt; entry.nextTaskTitle = "동영상 · " + item.title; }
            }
            courseCubes.SetOverview(overview.Values);
        }

        private CourseCubeNotice AddOverview(Dictionary<string, CourseCubeNotice> overview, Notice item)
        {
            if (!IsCurrentCourse(item)) return null;
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
                    notificationsKnown = notificationsConfirmed,
                    lecturesKnown = lecturesConfirmed.Contains(identity.courseId), lecturesFailed = lecturesFailed.Contains(identity.courseId) };
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

        public float AgendaHeight => (PendingAssignments().Count * 68 + PendingLectures().Count * 104 + 235) * CampusScreenLayout.Scale;

        private CampusScreenLayout.DeadlinePeriod NoticeDeadline(Notice item)
        {
            var task = string.IsNullOrEmpty(item.url) ? null :
                PendingAssignments().Find(candidate => SameActivity(candidate.url, item.url));
            return new CampusScreenLayout.DeadlinePeriod { startAt = task?.startAt ?? item.startAt, dueAt = task?.dueAt ?? item.dueAt };
        }

        public Color SelectedDeadlineColor
        {
            get
            {
                var periods = new List<CampusScreenLayout.DeadlinePeriod>();
                foreach (var item in PendingAssignments())
                    if (string.IsNullOrEmpty(selectedCourseKey) || NoticeCourseKey(item) == selectedCourseKey)
                        periods.Add(new CampusScreenLayout.DeadlinePeriod { startAt = item.startAt, dueAt = item.dueAt });
                foreach (var item in PendingLectures())
                    if (item.attendancePeriodKnown && (string.IsNullOrEmpty(selectedCourseKey) || NoticeCourseKey(item) == selectedCourseKey))
                        periods.Add(new CampusScreenLayout.DeadlinePeriod { startAt = item.startAt, dueAt = item.dueAt });
                return CampusScreenLayout.DeadlineColor(CampusScreenLayout.CourseStage(periods, DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
            }
        }

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
                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                caption += "\n" + CampusScreenLayout.DeadlineLabel(item.dueAt, now);
                var background = GUI.backgroundColor;
                GUI.backgroundColor = CampusScreenLayout.DeadlineColor(CampusScreenLayout.Stage(item.dueAt, now));
                bool clicked = GUILayout.Button(caption, agendaButton, GUILayout.Height(64 * scale));
                GUI.backgroundColor = background;
                if (clicked)
                {
                    OpenAssignmentDetail(item);
                }
            }
            bool emptyConfirmed = enrolledCourses.Count > 0 && assignmentsConfirmed.Count == enrolledCourses.Count && !rosterPartial;
            GUILayout.Label(networkWasUnavailable ? Status : tasks.Count == 0 ? emptyConfirmed ? "남은 과제 없음" : courseworkStatus : "마감이 가까운 과제부터 표시합니다. 제출 상태 미확인 일정은 학교에서 확인해 주세요.",
                agendaLabel, GUILayout.Height(54 * scale));
            var lectures = PendingLectures();
            GUILayout.Label("동영상 출석 일정 · " + lectures.Count + "건", agendaLabel, GUILayout.Height(24 * scale));
            foreach (var item in lectures)
            {
                var title = item.title ?? "동영상 강의";
                if (title.Length > 54) title = title.Substring(0, 54) + "…";
                var caption = CourseLabel(item) + title + "\n" + LecturePeriod(item) + "\n" + LectureRemaining(item) +
                    "\n" + LectureAttendanceLabel(item);
                var background = GUI.backgroundColor;
                GUI.backgroundColor = CampusScreenLayout.DeadlineColor(CampusScreenLayout.Stage(item.dueAt, DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
                bool clicked = GUILayout.Button(caption, agendaButton, GUILayout.Height(100 * scale));
                GUI.backgroundColor = background;
                if (clicked)
                {
                    GetComponent<NotificationDetailUI>()?.OpenCourseNotifications(NoticeCourseKey(item), item.courseName);
                    selectedNotice = item;
                }
            }
            GUILayout.Label(networkWasUnavailable ? Status : lectureStatus + (rosterPartial ? " · 전체 과목 확인 필요" : ""), agendaLabel, GUILayout.Height(48 * scale));
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

        private static string LectureRemaining(Notice item) => LectureAttendancePeriod.Status(item.startAt, item.dueAt, item.lateDueAt,
            item.attendancePeriodKnown, item.attendanceKnown && !item.attendanceStale, item.attended, DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        private static string LectureAttendanceLabel(Notice item) => item.attendanceStale ? "이전 조회 정보 · 출석 다시 확인 필요" :
            item.attendanceKnown ? "학교 출결: " + item.attendanceStatus : "출석 상태 확인 필요" +
            (string.IsNullOrWhiteSpace(item.attendanceStatus) ? "" : " · " + item.attendanceStatus);

        private static string LectureTime(long value) => value > 0 && value <= 253402300799L ?
            DateTimeOffset.FromUnixTimeSeconds(value).ToOffset(TimeSpan.FromHours(9)).ToString("MM/dd HH:mm") : "미확인";

        private static string LecturePeriod(Notice item) => item.attendancePeriodKnown ?
            "출석 인정: " + LectureTime(item.startAt) + " ~ " + LectureTime(item.dueAt) + " (한국 시간)" : "출석 인정 기간 확인 필요";

        private void OpenLecture(Notice item)
        {
            if (view == null || !IsLectureUrl(item.url)) return;
            lecturePageVisible = true;
            accountPagePinned = true; nonce = null;
            ShowAuthentication();
            view.LoadURL(item.url);
        }

        private void DrawLectureList(GUIStyle label, ref int count)
        {
            GUILayout.Label("동영상 출석 일정", label);
            GUILayout.Label(lectureStatus, label);
            showCompletedLectures = GUILayout.Toggle(showCompletedLectures, "출석 확인된 동영상도 보기", touchButton);
            var items = lectureDetails.FindAll(item => IsCurrentCourse(item) &&
                (string.IsNullOrEmpty(selectedCourseKey) || NoticeCourseKey(item) == selectedCourseKey) &&
                (showCompletedLectures || item.attendanceStale || !item.attendanceKnown || !item.attended));
            items.Sort((a, b) => CompareDeadlines(a.dueAt, b.dueAt));
            foreach (var item in items)
            {
                count++;
                var previous = GUI.backgroundColor;
                GUI.backgroundColor = CampusScreenLayout.DeadlineColor(CampusScreenLayout.Stage(item.dueAt, DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
                if (GUILayout.Button("동영상 · " + item.title + "\n" + LectureRemaining(item) + "\n" + LectureAttendanceLabel(item), touchButton))
                    selectedNotice = ReferenceEquals(selectedNotice, item) ? null : item;
                GUI.backgroundColor = previous;
                if (!ReferenceEquals(selectedNotice, item)) continue;
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label(CourseLabel(item) + (item.week > 0 ? item.week + "주차" : ""), label);
                GUILayout.Label(LecturePeriod(item), label);
                if (item.lateDueAt > 0) GUILayout.Label("지각 인정 종료: " + LectureTime(item.lateDueAt) + " (한국 시간)", label);
                GUILayout.Label(LectureAttendanceLabel(item), label);
                if (!string.IsNullOrWhiteSpace(item.progressText)) GUILayout.Label(item.progressText, label);
                if (!string.IsNullOrWhiteSpace(item.availabilityText)) GUILayout.Label(item.availabilityText, label);
                GUILayout.Label("학교 플레이어에서 시청한 뒤 학교의 출석 상태를 확인하세요.", label);
                if (GUILayout.Button("강의 보기", touchButton)) OpenLecture(item);
                GUILayout.EndVertical();
            }
        }

        private void DrawNotices(GUIStyle label)
        {
            if (!string.IsNullOrEmpty(selectedCourseKey))
            {
                GUILayout.Label(selectedCourseTitle + " · 과제 / 동영상 / 알림", label);
                DrawClassroom(label);
                if (GUILayout.Button("전체 알림 보기", touchButton)) SelectCourse(null, null);
            }
            int count = 0;
            showSubmittedAssignments = GUILayout.Toggle(showSubmittedAssignments, "제출 완료 과제도 보기", touchButton);
            DrawNoticeList(PendingAssignments(), "과제", label, ref count);
            if (showSubmittedAssignments)
                DrawNoticeList(assignmentDetails.FindAll(item => item.submissionKnown && item.submitted && IsCurrentCourse(item)), "제출 완료 과제", label, ref count);
            DrawLectureList(label, ref count);
            DrawNoticeList(notificationDetails, "알림", label, ref count);
            DrawNoticeList(messageDetails, "쪽지", label, ref count);
            if (count == 0)
            {
                bool assignmentsKnown = enrolledCourses.Count > 0;
                foreach (var course in enrolledCourses.Values)
                    if ((string.IsNullOrEmpty(selectedCourseKey) || NoticeCourseKey(course) == selectedCourseKey) &&
                        (!assignmentsConfirmed.Contains(course.courseId) || !lecturesConfirmed.Contains(course.courseId))) assignmentsKnown = false;
                GUILayout.Label(assignmentsKnown && notificationsConfirmed ? "남은 일정·알림 없음" : "알림·과제·동영상 확인 중이거나 조회가 필요합니다.", label);
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
                if (currentRosterKnown && NoticeCourseKey(item) != null && !IsCurrentCourse(item)) continue;
                if (!string.IsNullOrEmpty(selectedCourseKey) && NoticeCourseKey(item) != selectedCourseKey) continue;
                bool assignment = kind == "과제" || kind == "제출 완료 과제";
                if (!assignment && item.readKnown && item.read) continue;
                count++;
                string title = string.IsNullOrWhiteSpace(item.title) ? "제목 없음" : item.title;
                var period = NoticeDeadline(item);
                long due = period.dueAt;
                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var color = CampusScreenLayout.DeadlineColor(CampusScreenLayout.Stage(due, now));
                var time = CampusScreenLayout.RemainingTime(due);
                var background = GUI.backgroundColor;
                GUI.backgroundColor = color;
                bool clicked = GUILayout.Button(kind + " · " + title + "\n" + time +
                    (assignment && !item.submissionKnown ? " · 제출 확인 필요" : ""), touchButton);
                GUI.backgroundColor = background;
                if (clicked)
                {
                    if (assignment) OpenAssignmentDetail(item);
                    else selectedNotice = ReferenceEquals(selectedNotice, item) ? null : item;
                }
                if (assignment) continue;
                if (!ReferenceEquals(selectedNotice, item)) continue;
                GUI.backgroundColor = color;
                GUILayout.BeginVertical(GUI.skin.box);
                GUI.backgroundColor = background;
                GUILayout.Label(CourseLabel(item) + (assignment ? item.submissionKnown ? item.submitted ? "제출 완료" : "미제출 / 작성 중" : "제출 확인 필요" : ReadLabel(item)), label);
                GUILayout.Label(CourseClassrooms.Label(ClassroomCode(item)), label);
                GUILayout.Label(time, label);
                GUILayout.Label(CampusScreenLayout.DeadlineLabel(due, now), label);
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

        private void OpenAssignmentDetail(Notice item)
        {
            if (view == null || string.IsNullOrEmpty(nonce) || !SchoolPage(item.url)) return;
            if (assignmentView == null)
            {
                assignmentView = new GameObject("Pixel Campus Assignment Detail").AddComponent<WebViewObject>();
                assignmentView.transform.SetParent(transform);
                assignmentView.Init(cb: ReceiveAssignmentAction,
                    ld: _ => { AssignmentRenderStatus = null; assignmentRendererDeadline = 0; },
                    err: _ => { AssignmentRenderStatus = "과제 화면을 표시하지 못했습니다. 새로고침으로 다시 시도해 주세요."; assignmentRendererDeadline = 0; },
                    httpErr: _ => { AssignmentRenderStatus = "과제 첨부파일을 불러오지 못했습니다. 뒤로 돌아가 다시 시도해 주세요."; assignmentRendererDeadline = 0; },
                    androidForceDarkMode: 1);
            }
            assignmentDocumentTarget = item;
            assignmentVisible = true; assignmentAttachmentVisible = false;
            assignmentDocumentNonce = Guid.NewGuid().ToString("N");
            view.SetVisibility(false); courseCubes?.SetVisible(false);
            ApplyWebViewMargins(); assignmentView.SetVisibility(!websiteNoticesVisible);
            RefreshAssignmentDetail();
        }

        public void RefreshAssignmentDetail()
        {
            if (!assignmentVisible || assignmentSubmissionVisible || assignmentDocumentTarget == null) return;
            assignmentAttachmentVisible = false;
            assignmentDocumentParts = null;
            RenderAssignmentDocument("<p class='notice'>과제 본문과 제출 상황을 불러오는 중…</p>");
            var capture = Resources.Load<TextAsset>("SmartleadAssignmentDocument");
            if (capture == null || string.IsNullOrEmpty(nonce))
            {
                RenderAssignmentDocument("<p class='notice'>과제 화면을 준비하지 못했습니다. 뒤로 돌아가 다시 확인해 주세요.</p>");
                return;
            }
            assignmentDocumentRequest = Guid.NewGuid().ToString("N");
            assignmentDocumentDeadline = Time.unscaledTime + 16;
            var request = new DetailRequest { requestId = assignmentDocumentRequest, item = assignmentDocumentTarget };
            view.EvaluateJS(capture.text.Replace("__ASSIGNMENT_REQUEST__", JsonUtility.ToJson(request)).Replace("__UNITY_NONCE__", nonce));
        }

        private void RenderAssignmentDocument(string content)
        {
            var template = Resources.Load<TextAsset>("AssignmentDetail");
            if (template == null)
            {
                AssignmentRenderStatus = "과제 화면 템플릿을 불러오지 못했습니다. Unity의 AssignmentDetail.html 에셋 가져오기 상태를 확인해 주세요.";
                Debug.LogError("Pixel Campus: Resources/AssignmentDetail.html 템플릿을 불러오지 못했습니다. .meta GUID와 TextAsset 가져오기 상태를 확인하세요.");
                return;
            }
            if (assignmentView == null)
            {
                AssignmentRenderStatus = "과제 화면 표시 장치가 준비되지 않았습니다. 뒤로 돌아가 다시 열어 주세요.";
                return;
            }
            // Replace content last: school text containing a template marker must remain text.
            assignmentDocumentHtml = template.text.Replace("__DOCUMENT_NONCE__", assignmentDocumentNonce)
                .Replace("__COURSE_TITLE__", System.Net.WebUtility.HtmlEncode(assignmentDocumentTarget?.courseName ?? ""))
                .Replace("__ASSIGNMENT_TITLE__", System.Net.WebUtility.HtmlEncode(assignmentDocumentTarget?.title ?? "과제 상세"))
                .Replace("__ASSIGNMENT_CONTENT__", content);
            AssignmentRenderStatus = "과제 화면 준비 중…";
            assignmentRendererDeadline = Time.unscaledTime + 10;
            if (!assignmentView.IsInitialized()) pendingAssignmentHtml = assignmentDocumentHtml;
            else
            {
                pendingAssignmentHtml = null;
                assignmentView.LoadHTML(assignmentDocumentHtml, "https://smartlead.hallym.ac.kr/");
            }
        }

        private void ReceiveAssignmentAction(string message)
        {
            if (!assignmentVisible || string.IsNullOrEmpty(message) || message.Length > 8000) return;
            Capture data;
            try { data = JsonUtility.FromJson<Capture>(message); } catch { return; }
            if (data == null || data.nonce != assignmentDocumentNonce || assignmentSubmissionVisible) return;
            if (data.kind == "assignment_submission") { OpenAssignmentSubmission(); return; }
            if (data.kind != "assignment_attachment" || !SchoolPage(data.url)) return;
            var path = new Uri(data.url).AbsolutePath;
            if (!(path.Contains("/pluginfile.php/") || path.Contains("/draftfile.php/"))) return;
            assignmentAttachmentVisible = true;
            assignmentView.LoadURL(data.url); // Only an explicitly selected file, never the original assignment page.
        }

        public void OpenAssignmentSubmission()
        {
            if (!assignmentVisible || assignmentDocumentTarget == null || view == null ||
                !SchoolPage(assignmentDocumentTarget.url)) return;
            var path = new Uri(assignmentDocumentTarget.url).AbsolutePath;
            if (path != "/mod/assign/view.php" && path != "/mod/ubassign/view.php") return;
            assignmentDocumentRequest = null; assignmentDocumentParts = null;
            assignmentDocumentDeadline = assignmentRendererDeadline = 0;
            pendingAssignmentHtml = null;
            assignmentSubmissionVisible = true; assignmentAttachmentVisible = false;
            submissionConversionPrepared = false;
            refreshAssignmentAfterSubmission = false;
            AssignmentRenderStatus = "학교 제출 화면을 여는 중…";
            assignmentView?.EvaluateJS("document.querySelectorAll('video,audio').forEach(function(player){player.pause();});");
            assignmentView?.SetVisibility(false);
            // Reuse the authenticated WebView so HttpOnly cookies and the school's
            // CSRF/session fields never have to be copied into a separate uploader.
            view.SetVisibility(!websiteNoticesVisible);
            view.LoadURL(assignmentDocumentTarget.url);
        }

        public void CloseAssignmentDetail()
        {
            if (assignmentSubmissionVisible)
            {
                submissionConversion?.Dispose();
                assignmentSubmissionVisible = false;
                refreshAssignmentAfterSubmission = true;
                view.SetVisibility(false);
                assignmentView?.SetVisibility(!websiteNoticesVisible);
                RenderAssignmentDocument("<p class='notice'>학교에서 제출 상태를 다시 확인하고 있습니다…</p>");
                assignmentDocumentDeadline = Time.unscaledTime + 16;
                // Restore the collector page only after the user leaves the form.
                // Loading it during a file chooser/app resume would discard input.
                resumeRefreshPending = false;
                view.LoadURL(NotificationPage);
                return;
            }
            if (assignmentAttachmentVisible)
            {
                assignmentAttachmentVisible = false;
                assignmentView?.LoadHTML(assignmentDocumentHtml, "https://smartlead.hallym.ac.kr/");
                return;
            }
            DismissAssignmentDetail();
            courseCubes?.SetVisible(IsAuthenticated && !authenticationVisible);
        }

        private void DismissAssignmentDetail()
        {
            // Hiding a WebView does not necessarily stop its media playback.
            assignmentView?.EvaluateJS("document.querySelectorAll('video,audio').forEach(function(player){player.pause();});");
            assignmentVisible = assignmentAttachmentVisible = false;
            if (assignmentSubmissionVisible) view?.SetVisibility(false);
            assignmentSubmissionVisible = refreshAssignmentAfterSubmission = false;
            submissionConversion?.Dispose();
            assignmentDocumentRequest = assignmentDocumentHtml = assignmentDocumentNonce = null;
            pendingAssignmentHtml = null; AssignmentRenderStatus = null; assignmentRendererDeadline = 0;
            assignmentDocumentParts = null; assignmentDocumentTarget = null; assignmentDocumentDeadline = 0;
            assignmentView?.SetVisibility(false);
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
            if ((PlayerPrefs.HasKey(SavedIdKey) || PlayerPrefs.GetInt(AutomaticLoginKey, 0) == 1) && GUILayout.Button("저장된 ID·비밀번호 삭제 / 자동 로그인 해제", touchButton))
            {
                ForgetSavedLogin();
            }
            if (GUILayout.Button("학교 알림 기록 보기", touchButton)) OpenHistoryPage();
            if (accountPagePinned && (HasResult || HasMessagesResult) && GUILayout.Button("학교 화면 닫기", touchButton)) HideAccountPage();
        }

        private void OnApplicationQuit() { view?.SaveCookies(); }
        private void OnDestroy() { submissionConversion?.Dispose(); pendingPassword = null; if (assignmentView != null) Destroy(assignmentView.gameObject); if (view != null) { view.SaveCookies(); Destroy(view.gameObject); } }
    }
}



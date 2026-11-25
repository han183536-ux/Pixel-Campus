using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Gree.UnityWebView;
using UnityEngine;

namespace WebNotifications
{
    [RequireComponent(typeof(NotificationManager))]
    public class SmartleadWebView : MonoBehaviour
    {
        [Serializable] private class Attachment { public string name, url; }
        [Serializable] private class Notice { public int id, courseId, week; public long dueAt, createdAt, startAt, modifiedAt, lateDueAt; public string sender, title, body, url, courseName, courseCode, type, submissionStatus, gradingStatus, submissionText, attendanceStatus, progressText, availabilityText; public bool read, readKnown, submitted, submissionKnown, detailLoaded, submissionFilesKnown, detailTruncated, attendanceKnown, attended, attendancePeriodKnown, attendanceStale; public Attachment[] submittedFiles, instructionFiles, instructionImages; }
        [Serializable] private class Capture { public string kind, nonce, page, source, error, username, password, requestId, html, url; public int count, total, courseId, index; public bool found, unreadLimited, historyLimited, countOnly, unreadUnknown, authenticated, remember, automatic, passwordEntered, previewsIncomplete, canEditSubmission; public Notice[] items; }
        private const string RememberIdKey = "smartlead.remember-id";
        private const string SavedIdKey = "smartlead.saved-id";
        private const string AutomaticLoginKey = "smartlead.automatic-login";
        private const string RequirePasswordKey = "smartlead.require-password";
        private const string LoginPrompt = "로그인하면 지도를 볼 수 있습니다.";
        private readonly Dictionary<string, string> loginMessagePages = new Dictionary<string, string>();
        private bool RequiresPassword => PlayerPrefs.GetInt(RequirePasswordKey, 0) == 1;
        private string pendingUsername, pendingPassword;
        private bool automaticLoginAttempted;
        private bool signingOut;
        private float logoutDeadline;
        [Serializable] private class LoginSettings { public string nonce, username, password; public bool remember, automatic, autoSubmit, secureStorageAvailable, requirePasswordEntry; }
        [Serializable] private class DetailRequest { public string requestId; public Notice item; }
        [Serializable] private sealed class OutlineRequest { public string requestId; public int courseId; }
        [Serializable] private sealed class OutlineActivity { public string title, body, url, format, fileName; }
        [Serializable] private sealed class OutlineWeek { public string key, title, body; public OutlineActivity[] activities; }
        [Serializable] private sealed class CourseOutline { public OutlineWeek[] weeks; public bool partial; }
        private readonly Dictionary<int, CourseOutline> courseOutlines = new Dictionary<int, CourseOutline>();
        private readonly HashSet<string> expandedWeeks = new HashSet<string>();
        private OutlineRequest outlineRequest;
        private string[] outlineParts;
        private float outlineDeadline;
        private int outlineCourseId;
        private bool showCourseOutline, outlineFailed;
        private OutlineActivity selectedOutlineActivity;
        private string selectedOutlineWeekTitle;
        internal bool IsOutlineActivityOpen => selectedOutlineActivity != null;
        private bool showSubmittedAssignments;
        private WebViewObject assignmentView;
        private bool assignmentVisible, assignmentAttachmentVisible;
        private bool assignmentSubmissionVisible, refreshAssignmentAfterSubmission;
        private bool assignmentEditAvailable;
        [Serializable] private sealed class UploadFile { public string name; public long size; }
        [Serializable] private sealed class UploadPacket
        {
            public string kind, nonce, page, requestId, status, data, error, url;
            public int fileIndex, index;
            public UploadFile[] files;
            public string[] acceptedTypes;
        }
        private string assignmentUploadId, assignmentUploadStatus;
        private float assignmentUploadDeadline;
        private bool assignmentUploadDraft;
        internal bool IsAssignmentUploadInProgress => assignmentUploadId != null;
        private SubmissionConversionBridge submissionConversion;
        private bool submissionConversionPrepared;
        [Serializable] private class ConversionSettings { public string nonce; public bool available; }
        private Notice assignmentDocumentTarget;
        private string assignmentDocumentRequest, assignmentDocumentNonce, assignmentDocumentHtml;
        private string pendingAssignmentHtml;
        private const int AssignmentCacheLimit = 8;
        private const float AssignmentCacheSeconds = 120;
        private readonly Dictionary<string, (string body, bool canEditSubmission, float receivedAt)> assignmentCache =
            new Dictionary<string, (string body, bool canEditSubmission, float receivedAt)>(StringComparer.Ordinal);
        private static string assignmentFontRule;
        private const int MaxDocumentCharsWithEmbeddedFont = 400_000;
        public string AssignmentRenderStatus { get; private set; }
        private float assignmentRendererDeadline;
        private string[] assignmentDocumentParts;
        private float assignmentDocumentDeadline;
        public bool IsAssignmentDetailVisible => assignmentVisible;
        public bool IsAssignmentSubmissionVisible => assignmentSubmissionVisible;
        public string AssignmentDetailTitle => assignmentSubmissionVisible ? "과제 파일 첨부·제출" :
            assignmentAttachmentVisible ? "과제 첨부파일" : "과제 상세";
        public string AssignmentCourseName
        {
            get
            {
                if (assignmentDocumentTarget == null) return "";
                if (!string.IsNullOrWhiteSpace(assignmentDocumentTarget.courseName))
                    return assignmentDocumentTarget.courseName.Trim();
                return enrolledCourses.TryGetValue(assignmentDocumentTarget.courseId, out var course)
                    ? course.courseName?.Trim() ?? "" : "";
            }
        }
        private const string NotificationPage = "https://smartlead.hallym.ac.kr/local/ubion/user/index.php";
        private const string SchoolLoginUrl = "https://smartlead.hallym.ac.kr/login.php";
        private enum LoginStage { Idle, Initializing, CheckingSession, OpeningLogin }
        private LoginStage loginStage;
        private float loginDeadline;
        private bool triedLoginPage;
        public bool LoginBrowserUnavailable { get; private set; }
        public bool LoginPluginMissing { get; private set; }
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        internal string LoginDiagnostic { get; private set; }
#endif
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
        private int notificationPriority;
        private readonly List<string> titles = new List<string>();
        private readonly List<string> messages = new List<string>();
        private readonly List<Notice> notificationDetails = new List<Notice>();
        private readonly List<Notice> messageDetails = new List<Notice>();
        private readonly List<Notice> assignmentDetails = new List<Notice>();
        private readonly List<Notice> scheduleDetails = new List<Notice>();
        private List<Notice> pendingAssignments, sortedCourses;
        private long pendingAssignmentsMinute;
        private bool overviewDirty;
        private readonly List<Notice> lectureDetails = new List<Notice>();
        private readonly Dictionary<int, List<Notice>> lectureUpdates = new Dictionary<int, List<Notice>>();
        private readonly HashSet<int> lecturesConfirmed = new HashSet<int>(), lecturesFailed = new HashSet<int>();
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
        private bool notificationsConfirmed, rosterPartial;
        private bool courseworkFailed, courseworkFinished;
        private float courseworkDeadline;
        public string CourseworkStatus => networkWasUnavailable ? "인터넷 연결을 확인해 주세요." :
            courseworkFailed || rosterPartial ? "일부 과제를 확인할 수 없습니다. 다시 확인해 주세요." : "과제를 불러오는 중…";
        private string selectedCourseKey, selectedCourseTitle;
        private Notice selectedNotice;
        private bool editingClassroom;
        private string classroomInput = "";
        public string Status { get; private set; } = "알림 확인 준비 중";
        public string MessageStatus { get; private set; } = "쪽지 확인 준비 중";
        public int Count { get; private set; }
        public int Total { get; private set; }
        public bool HasResult { get; private set; }
        public int MessageCount { get; private set; }
        public int MessageTotal { get; private set; }
        public bool HasMessagesResult { get; private set; }
        public bool IsAuthenticated { get; private set; }
        public bool IsSchoolPageVisible => authenticationVisible || assignmentVisible;

        private void Start()
        {
            if (!GetComponent<NotificationManager>().useWebView) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            Input.backButtonLeavesApp = false;
#endif
            courseCubes = GetComponent<SmartleadCourseCubes>();
            if (courseCubes == null) courseCubes = gameObject.AddComponent<SmartleadCourseCubes>();
            if (GetComponent<CampusMapView>() == null) gameObject.AddComponent<CampusMapView>();
            var asset = Resources.Load<TextAsset>("SmartleadCapture");
            if (asset == null) { Status = "알림 확인 스크립트가 없습니다."; return; }
            script = asset.text;
            var lectures = Resources.Load<TextAsset>("SmartleadVideoAttendance");
            if (lectures != null) script += "\n" + lectures.text;
            var coursework = Resources.Load<TextAsset>("SmartleadCoursework");
            if (coursework != null) script += "\n" + coursework.text;
            else courseworkFailed = true;
            InitializeLoginBrowser();
        }

        private void InitializeLoginBrowser()
        {
            LoginBrowserUnavailable = false;
            LoginPluginMissing = false;
            triedLoginPage = false;
            loginStage = LoginStage.Initializing;
            loginDeadline = Time.realtimeSinceStartup + 20;
            Status = "학교 로그인 창을 준비하고 있습니다…";
            MessageStatus = "로그인 대기 중";
            try
            {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
                LoginDiagnostic = null;
                if (!WindowsLoginBrowser.PrepareProfile()) { FailLoginBrowser("ProfileUnavailable"); return; }
#endif
                view = new GameObject("SmartLEAD Notification WebView").AddComponent<WebViewObject>();
                view.transform.SetParent(transform);
                view.Init(cb: Receive, err: _ => { HandleSchoolPageError("SmartLEAD 연결 실패"); if (assignmentSubmissionVisible) AssignmentRenderStatus = "학교 제출 화면 연결에 실패했습니다. 뒤로 돌아가 다시 열어 주세요."; },
                    httpErr: _ => { HandleSchoolPageError("SmartLEAD 페이지 오류"); if (assignmentSubmissionVisible) AssignmentRenderStatus = "학교 제출 화면에서 오류가 발생했습니다. 학교 화면의 안내를 확인해 주세요."; },
                    started: url => { currentUrl = url; nonce = null; HasResult = false; HasMessagesResult = false;
                        // A redirect already opened the form. A slow page load must
                        // not trigger another navigation while the user is typing.
                        if (loginStage == LoginStage.CheckingSession && LoginPage(url))
                        {
                            triedLoginPage = true;
                            loginStage = LoginStage.OpeningLogin;
                            loginDeadline = Time.realtimeSinceStartup + 15;
                            Status = "학교 로그인 화면을 여는 중…";
                        }
                        if (assignmentSubmissionVisible) { submissionConversionPrepared = false; submissionConversion?.Dispose(); return; }
                        if (lecturePageVisible) return;
                        if (SchoolPage(url) && new Uri(url).AbsolutePath.Equals("/login/logout.php", StringComparison.OrdinalIgnoreCase))
                        {
                            RequireFreshLogin();
                            PlayerPrefs.SetInt(AutomaticLoginKey, 0); PlayerPrefs.Save();
                            SmartleadCredentialStore.Delete(); pendingPassword = null; automaticLoginAttempted = true;
                        }
                        notificationsConfirmed = false;
                        schoolResult = false; notificationPriority = 0;
                        titles.Clear(); messages.Clear();
                        if (loginStage == LoginStage.Idle) Status = IsAuthenticated ? "연결 중…" : LoginPrompt; },
                    ld: url => { if (url == "about:blank") return; currentUrl = url; Inject(); }, androidForceDarkMode: 1);
#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
                // On Windows Init returns after native creation. An unavailable runtime
                // can leave an empty handle without throwing a managed exception.
                if (!view.IsInitialized()) { FailLoginBrowser(); return; }
#endif
                nextRefresh = Time.unscaledTime + 120;
            }
            catch (Exception ex)
            {
                LoginPluginMissing = ex is DllNotFoundException;
                FailLoginBrowser(ex.GetType().Name);
            }
        }

        private void FailLoginBrowser(string errorType = null)
        {
            loginStage = LoginStage.Idle;
            if (view != null)
            {
                view.gameObject.SetActive(false);
                Destroy(view.gameObject);
                view = null;
            }
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (errorType == nameof(DllNotFoundException) && WindowsLoginBrowser.TryPreloadPlugin())
            {
                InitializeLoginBrowser();
                return;
            }
            if (errorType == null && WindowsLoginBrowser.TryStartSystemFallback())
            {
                Status = "Windows에 설치된 웹뷰로 다시 여는 중…";
                Debug.Log("SmartLEAD WebView: bundled initialization failed; restarting with system WebView2.");
                Application.Quit();
                return;
            }
            LoginDiagnostic = WindowsLoginBrowser.DescribeFailure(errorType);
            Debug.LogWarning("SmartLEAD WebView initialization failed: " + LoginDiagnostic);
#else
            Debug.LogWarning("SmartLEAD WebView initialization failed" + (errorType == null ? "." : " (" + errorType + ")."));
#endif
            LoginBrowserUnavailable = true;
            Status = "학교 로그인 창을 열지 못했습니다.";
        }

        private void NavigateForLogin(bool openForm)
        {
            loginStage = openForm ? LoginStage.OpeningLogin : LoginStage.CheckingSession;
            triedLoginPage |= openForm;
            loginDeadline = Time.realtimeSinceStartup + 15;
            Status = openForm ? "학교 로그인 화면을 여는 중…" : "기존 로그인 상태를 확인하고 있습니다…";
            ShowAuthentication();
            view.LoadURL(openForm ? SchoolLoginUrl : NotificationPage);
        }

        private void HandleSchoolPageError(string message)
        {
            if (loginStage == LoginStage.CheckingSession && !triedLoginPage)
            {
                NavigateForLogin(true);
                return;
            }
            loginStage = LoginStage.Idle;
            Status = message;
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
            if (detail != null && detail.NavigateBack(this)) return;
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
                loginStage = LoginStage.Idle;
                if (refreshAssignmentAfterSubmission) DismissAssignmentDetail();
                IsAuthenticated = false;
                courseCubes?.Clear();
                notificationDetails.Clear(); messageDetails.Clear(); SelectCourse(null, null);
                ClearCoursework();
                accountPagePinned = false;
                returnFromLogin = true;
                ShowAuthentication();
                Status = LoginPrompt;
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
                NavigateForLogin(false);
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
            if (loginStage == LoginStage.Initializing)
            {
                if (!view.IsInitialized())
                {
                    if (Time.realtimeSinceStartup >= loginDeadline) FailLoginBrowser();
                    return;
                }
                if (RequiresPassword) { view.ClearCookies(); view.SaveCookies(); }
                NavigateForLogin(RequiresPassword);
                return;
            }
            if (loginStage != LoginStage.Idle && Time.realtimeSinceStartup >= loginDeadline)
                HandleSchoolPageError("학교 응답이 지연되고 있습니다. 재접속해 주세요.");
            if (showCourseOutline && outlineRequest == null && !outlineFailed) RequestCourseOutline();
            if (outlineRequest != null && Time.unscaledTime >= outlineDeadline)
            {
                ClearOutlineRequest();
                outlineFailed = true;
            }
            if (lecturePageVisible) { ApplyWebViewMargins(); return; }
            if (assignmentVisible)
            {
                if (assignmentSubmissionVisible) submissionConversion?.Poll(view.EvaluateJS);
                if (assignmentUploadId != null && Time.realtimeSinceStartup >= assignmentUploadDeadline)
                {
                    ShowAssignmentUploadReply(new UploadPacket { requestId = assignmentUploadId, status = "error",
                        error = "저장 결과를 확인하지 못했습니다. 제출 상태를 확인해 주세요." });
                    StopAssignmentUpload();
                }
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
                    AssignmentRenderStatus = "과제 화면을 표시하지 못했습니다. 뒤로 돌아가 다시 열어 주세요.";
                }
                if (assignmentDocumentDeadline > 0 && Time.unscaledTime >= assignmentDocumentDeadline)
                {
                    assignmentDocumentDeadline = 0; assignmentDocumentRequest = null;
                    RenderAssignmentDocument("<p class='notice'>과제 내용을 불러오는 데 시간이 걸리고 있습니다. 과제 상세를 다시 열어 주세요.</p>");
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
                overviewDirty = true;
            }
            if (lectureDeadline > 0 && Time.unscaledTime >= lectureDeadline)
            {
                lectureDeadline = 0;
                foreach (var id in enrolledCourses.Keys) if (!lecturesConfirmed.Contains(id)) lecturesFailed.Add(id);
                foreach (var item in lectureDetails) if (!lecturesConfirmed.Contains(item.courseId)) item.attendanceStale = true;
                overviewDirty = true;
            }
            if (authenticationVisible)
            {
                ApplyWebViewMargins();
                return;
            }
            if (outlineRequest != null || Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 120;
            Refresh();
        }

        private void LateUpdate()
        {
            if (!overviewDirty) return;
            overviewDirty = false;
            SyncCourseOverview();
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

        internal bool HasSavedLogin => PlayerPrefs.HasKey(SavedIdKey) || PlayerPrefs.GetInt(AutomaticLoginKey, 0) == 1;

        internal void ForgetSavedLogin()
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
            if (LoginBrowserUnavailable) { InitializeLoginBrowser(); return; }
            if (loginStage == LoginStage.Initializing) return;
            if (view == null) return;
            if (Application.isMobilePlatform && Application.internetReachability == NetworkReachability.NotReachable)
            {
                networkWasUnavailable = true;
                Status = "인터넷 연결을 확인한 뒤 다시 시도해 주세요.";
                return;
            }
            // Startup probes existing cookies. An explicit retry from the signed-out
            // screen opens the form even when the protected-page probe was silent.
            if (!IsAuthenticated || !SchoolPage(currentUrl) ||
                new Uri(currentUrl).AbsolutePath.StartsWith("/login", StringComparison.OrdinalIgnoreCase))
            {
                accountPagePinned = false;
                NavigateForLogin(!IsAuthenticated);
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
                    NavigateForLogin(false);
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
            if (data.kind == "assignment_upload_reply")
            {
                ReceiveAssignmentUploadReply(message);
                return;
            }
            if (data.kind == "course_outline")
            {
                ReceiveCourseOutline(data);
                return;
            }
            if (data.kind == "assignment_document" || data.kind == "assignment_document_error")
            {
                if (!assignmentVisible || string.IsNullOrEmpty(assignmentDocumentRequest) || data.requestId != assignmentDocumentRequest) return;
                if (data.kind == "assignment_document_error")
                {
                    assignmentDocumentDeadline = 0; assignmentDocumentRequest = null;
                    RenderAssignmentDocument("<p class='notice'>과제를 불러오지 못했습니다. 다시 확인해 주세요.</p>");
                    return;
                }
                if (assignmentDocumentTarget == null || !SameActivity(data.url, assignmentDocumentTarget.url) || data.total < 1 || data.total > 525 ||
                    data.index < 0 || data.index >= data.total || data.html == null || data.html.Length > 8000) return;
                if (assignmentDocumentParts == null) assignmentDocumentParts = new string[data.total];
                if (assignmentDocumentParts.Length != data.total) return;
                assignmentDocumentParts[data.index] = data.html;
                if (Array.Exists(assignmentDocumentParts, part => part == null)) return;
                assignmentDocumentDeadline = 0; assignmentDocumentRequest = null;
                string body = string.Concat(assignmentDocumentParts);
                assignmentEditAvailable = data.canEditSubmission;
                if (!data.previewsIncomplete) CacheAssignment(assignmentDocumentTarget.url, body, assignmentEditAvailable);
                RenderAssignmentDocument(body);
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
                loginStage = LoginStage.Idle;
                triedLoginPage = false;
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
                notificationPriority = 0;
                titles.Clear(); messages.Clear();
                notificationDetails.Clear(); messageDetails.Clear(); selectedNotice = null;
                SelectCourse(null, null);
                courseCubes?.Clear();
                ClearCoursework();
                ShowAuthentication();
                Status = LoginPrompt;
                MessageStatus = "로그인 대기 중";
                if (SchoolPage(currentUrl) && !LoginPage(currentUrl))
                {
                    if (!triedLoginPage) NavigateForLogin(true);
                    else HandleSchoolPageError("학교 세션을 확인하지 못했습니다. 재접속해 주세요.");
                }
                return;
            }
            if (data.kind == "api_status") return;
            if (schoolResult && data.source != "school_api" && data.source != "school_history" &&
                !(data.kind == "notifications" && data.source == "message_api")) return;
            if (data.source == "school_api") schoolResult = true;
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
                if (data.found && !data.countOnly)
                {
                    ReplaceDetails(messageDetails, data.items, true);
                    overviewDirty = true;
                }
                MessageStatus = data.found ? "" : "쪽지를 불러오지 못했습니다.";
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
            overviewDirty = true;
            if (data.found && authenticationVisible && !accountPagePinned)
            {
                HideAccountPage();
            }
            else if (!IsAuthenticated && !data.found && !HasMessagesResult) ShowAuthentication();
            Status = data.found ? LoginPrompt : "학교 알림을 불러오지 못했습니다.";
        }

        private void ClearCoursework()
        {
            DismissAssignmentDetail();
            assignmentCache.Clear();
            courseOutlines.Clear();
            CloseCourseOutline();
            assignmentDetails.Clear(); enrolledCourses.Clear(); assignmentsConfirmed.Clear(); assignmentsFailed.Clear();
            scheduleDetails.Clear(); incomingSchedules.Clear(); courseworkFailed = false; courseworkFinished = false; courseworkDeadline = 0;
            lectureDetails.Clear(); lectureUpdates.Clear(); lecturesConfirmed.Clear(); lecturesFailed.Clear();
            lectureDeadline = 0;
            assignmentUpdates.Clear(); receivedCourses.Clear(); incomingCourses.Clear(); currentRosterKnown = false;
            notificationsConfirmed = false; rosterPartial = false;
            pendingAssignments = sortedCourses = null;
            overviewDirty = false;
        }

        private bool ReceiveCoursework(Capture data)
        {
            switch (data.kind)
            {
                case "lectures_begin":
                    lectureDeadline = Time.unscaledTime + 90;
                    return true;
                case "lectures_course_begin":
                    lectureUpdates[data.courseId] = new List<Notice>();
                    return true;
                case "lecture":
                    if (data.items != null) foreach (var item in data.items)
                        if (item != null && IsLectureUrl(item.url) && lectureUpdates.TryGetValue(item.courseId, out var pendingLectures))
                        {
                            if (item.startAt <= 0 || item.dueAt <= item.startAt || item.dueAt > 253402300799L) { item.startAt = item.dueAt = 0; item.attendancePeriodKnown = false; }
                            if (item.lateDueAt <= item.dueAt || item.lateDueAt > 253402300799L) item.lateDueAt = 0;
                            pendingLectures.Add(item);
                        }
                    return true;
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
                    break;
                case "lectures_scan_complete":
                    lectureDeadline = 0;
                    if (!data.found)
                    {
                        foreach (var id in enrolledCourses.Keys) if (!lecturesConfirmed.Contains(id)) lecturesFailed.Add(id);
                        foreach (var item in lectureDetails) if (!lecturesConfirmed.Contains(item.courseId)) item.attendanceStale = true;
                    }
                    break;
                case "schedule":
                    pendingAssignments = null;
                    if (data.items != null) foreach (var item in data.items)
                        if (item != null && item.dueAt > 0 && SchoolPage(item.url))
                        {
                            incomingSchedules.Add(item);
                            scheduleDetails.RemoveAll(previous => SameActivity(previous.url, item.url));
                            scheduleDetails.Add(item);
                        }
                    break;
                case "schedule_complete":
                    pendingAssignments = null;
                    if (data.found && !data.historyLimited)
                        scheduleDetails.RemoveAll(previous => !incomingSchedules.Exists(item => SameActivity(previous.url, item.url)));
                    break;
                case "coursework_complete":
                    courseworkDeadline = 0;
                    courseworkFinished = true;
                    courseworkFailed = enrolledCourses.Count == 0 || assignmentsFailed.Count > 0 ||
                        assignmentsConfirmed.Count + assignmentsFailed.Count < enrolledCourses.Count;
                    break;
                case "course":
                    if (data.items != null) foreach (var item in data.items)
                        if (item != null && item.courseId > 0) { incomingCourses[item.courseId] = item; receivedCourses.Add(item.courseId); }
                    return true;
                case "courses_complete":
                    rosterPartial = data.historyLimited;
                    // Replace the snapshot atomically, including a partial current roster.
                    // Retaining missing records here used to keep old semesters forever.
                    var removed = new List<int>();
                    foreach (var id in enrolledCourses.Keys) if (!receivedCourses.Contains(id)) removed.Add(id);
                    foreach (var id in removed) { enrolledCourses.Remove(id); assignmentDetails.RemoveAll(item => item.courseId == id); assignmentsConfirmed.Remove(id); assignmentsFailed.Remove(id); lectureDetails.RemoveAll(item => item.courseId == id); lecturesConfirmed.Remove(id); lecturesFailed.Remove(id); }
                    foreach (var course in incomingCourses) enrolledCourses[course.Key] = course.Value;
                    currentRosterKnown = true;
                    pendingAssignments = sortedCourses = null;
                    break;
                case "assignments_begin":
                    assignmentUpdates[data.courseId] = new List<Notice>();
                    return true;
                case "assignment":
                    if (data.items != null) foreach (var item in data.items)
                        if (item != null && assignmentUpdates.TryGetValue(item.courseId, out var pending)) pending.Add(item);
                    return true;
                case "assignments_complete":
                    pendingAssignments = null;
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
                    break;
                case "coursework_status":
                    courseworkFailed = true;
                    foreach (var id in enrolledCourses.Keys) { assignmentsConfirmed.Remove(id); assignmentsFailed.Add(id); }
                    break;
                default: return false;
            }
            overviewDirty = true;
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
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (pendingAssignments != null && pendingAssignmentsMinute == now / 60) return pendingAssignments;
            var result = assignmentDetails.FindAll(item => (!item.submissionKnown || !item.submitted) && IsCurrentCourse(item));
            foreach (var item in scheduleDetails)
            {
                // Calendars include old enrolments. Keep overdue work from the current
                // roster, and future deadlines only while the roster is unavailable.
                if (currentRosterKnown ? !IsCurrentCourse(item) : item.dueAt <= now) continue;
                if (!assignmentDetails.Exists(task => SameActivity(task.url, item.url))) result.Add(item);
            }
            result.Sort((a, b) => CompareDeadlines(a.dueAt, b.dueAt));
            pendingAssignmentsMinute = now / 60;
            return pendingAssignments = result;
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

        public void SelectCourse(string key, string title, bool openOutline = false)
        {
            CloseCourseOutline();
            editingClassroom = false;
            selectedCourseKey = key;
            selectedCourseTitle = title;
            foreach (var course in enrolledCourses.Values)
                if (NoticeCourseKey(course) == key) { outlineCourseId = course.courseId; break; }
            selectedNotice = string.IsNullOrEmpty(key) ? null :
                PendingAssignments().Find(item => NoticeCourseKey(item) == key) ??
                notificationDetails.Find(item => NoticeCourseKey(item) == key && (!item.readKnown || !item.read)) ??
                messageDetails.Find(item => NoticeCourseKey(item) == key);
            showCourseOutline = openOutline;
            if (openOutline) RequestCourseOutline();
        }

        internal void CloseCourseOutline()
        {
            if (outlineRequest != null && view != null && SchoolPage(currentUrl))
                view.EvaluateJS("window.__pixelCampusOutlineAbort?.abort();");
            ClearOutlineRequest();
            expandedWeeks.Clear();
            CloseOutlineActivity();
            outlineCourseId = 0;
            showCourseOutline = outlineFailed = false;
        }

        internal void CloseOutlineActivity()
        {
            selectedOutlineActivity = null;
            selectedOutlineWeekTitle = null;
        }

        private void ClearOutlineRequest()
        {
            outlineRequest = null;
            outlineParts = null;
        }

        private void RequestCourseOutline()
        {
            if (view == null || !IsAuthenticated || IsSchoolPageVisible || outlineCourseId <= 0 || outlineRequest != null ||
                courseOutlines.ContainsKey(outlineCourseId) || string.IsNullOrEmpty(nonce) || !SchoolPage(currentUrl)) return;
            var scriptAsset = Resources.Load<TextAsset>("SmartleadCourseOutline");
            if (scriptAsset == null) { outlineFailed = true; return; }
            outlineRequest = new OutlineRequest { courseId = outlineCourseId, requestId = Guid.NewGuid().ToString("N") };
            outlineDeadline = Time.unscaledTime + 50;
            view.EvaluateJS(scriptAsset.text.Replace("__UNITY_NONCE__", nonce)
                .Replace("__COURSE_OUTLINE_REQUEST__", JsonUtility.ToJson(outlineRequest)));
        }

        private void ReceiveCourseOutline(Capture data)
        {
            if (outlineRequest == null || data.requestId != outlineRequest.requestId || data.courseId != outlineRequest.courseId)
                return;
            if (!data.found)
            {
                ClearOutlineRequest();
                outlineFailed = true;
                return;
            }
            if (data.total < 1 || data.total > 525 || data.index < 0 || data.index >= data.total ||
                data.html == null || data.html.Length > 6000) return;
            if (outlineParts == null) outlineParts = new string[data.total];
            if (outlineParts.Length != data.total) return;
            outlineParts[data.index] = data.html;
            if (Array.Exists(outlineParts, part => part == null)) return;
            try
            {
                var outline = JsonUtility.FromJson<CourseOutline>(string.Concat(outlineParts));
                if (outline?.weeks == null || outline.weeks.Length == 0) outlineFailed = true;
                else courseOutlines[data.courseId] = outline;
            }
            catch (ArgumentException) { outlineFailed = true; }
            finally { ClearOutlineRequest(); }
        }


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

        private static readonly StringComparer KoreanCourseOrder = StringComparer.Create(CultureInfo.GetCultureInfo("ko-KR"), true);

        public void DrawCourseTab(float width)
        {
            if (sortedCourses == null)
            {
                sortedCourses = new List<Notice>(enrolledCourses.Values);
                sortedCourses.Sort((left, right) =>
                {
                    int order = KoreanCourseOrder.Compare(left.courseName, right.courseName);
                    if (order != 0) return order;
                    order = StringComparer.Ordinal.Compare(left.courseCode, right.courseCode);
                    return order != 0 ? order : left.courseId.CompareTo(right.courseId);
                });
            }
            if (sortedCourses.Count == 0)
                CampusTheme.StatusCard(currentRosterKnown && !rosterPartial ? "현재 수강 중인 과목이 없습니다." :
                    courseworkFailed || networkWasUnavailable ? "수강 과목을 불러오지 못했습니다." : "수강 과목을 불러오는 중…");
            var detail = GetComponent<NotificationDetailUI>();
            foreach (var item in sortedCourses)
            {
                string name = string.IsNullOrWhiteSpace(item.courseName) ? "과목 " + item.courseId : item.courseName;
                string room = CourseClassrooms.Room(item.courseCode);
                string caption = item.courseCode ?? "";
                if (!string.IsNullOrWhiteSpace(room))
                    caption = caption.Length > 0 ? caption + "  /  " + room : room;
                string section = CourseSection(item.courseCode);
                if (CampusTheme.ListCard(name, caption, width, CampusTheme.CourseMarker, courseCard: true, section: section))
                    detail?.OpenCourseNotifications(NoticeCourseKey(item), name, openOutline: true);
            }
        }

        private static string CourseSection(string code)
        {
            if (string.IsNullOrEmpty(code)) return null;
            int separator = code.LastIndexOf('-');
            if (separator <= 0 || separator != code.Length - 3) return null;
            return code[separator + 1] is >= '0' and <= '9' && code[separator + 2] is >= '0' and <= '9'
                ? code.Substring(separator + 1) : null;
        }

        public void DrawAssignmentTab(float width)
        {
            var tasks = PendingAssignments();
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            CampusTheme.ScreenTitle("남은 과제", null);
            if (tasks.Count == 0)
            {
                bool emptyConfirmed = currentRosterKnown && !rosterPartial && assignmentsConfirmed.Count == enrolledCourses.Count;
                CampusTheme.StatusCard(emptyConfirmed ? "남은 과제가 없습니다." : CourseworkStatus);
            }
            foreach (var item in tasks)
            {
                string title = string.IsNullOrWhiteSpace(item.title) ? "과제" : item.title;
                string course = string.IsNullOrWhiteSpace(item.courseName) ? "과목 " + item.courseId : item.courseName;
                string date = item.dueAt > 0 && item.dueAt <= 253402300799L ?
                    DateTimeOffset.FromUnixTimeSeconds(item.dueAt).ToOffset(TimeSpan.FromHours(9)).ToString("M월 d일 HH:mm") + " · " : "";
                string caption = course + (item.submissionKnown ? "" : " · 제출 확인 필요") +
                    "\n" + date + CampusScreenLayout.RemainingTime(item.dueAt);
                var deadline = CampusScreenLayout.DeadlineColor(CampusScreenLayout.Stage(item.dueAt, now));
                if (CampusTheme.ListCard(title, caption, width, deadline, deadlineGradient: true))
                    OpenAssignmentDetail(item);
            }
            GUILayout.Space(8 * CampusScreenLayout.Scale);
            GUILayout.BeginHorizontal();
            if (CampusTheme.Action("과제 다시 확인")) Refresh();
            if (CampusTheme.Action("학교 일정 보기", GUI.skin.button) && view != null)
            {
                accountPagePinned = true;
                ShowAuthentication();
                view.LoadURL("https://smartlead.hallym.ac.kr/calendar/view.php?view=upcoming");
            }
            GUILayout.EndHorizontal();
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

        private void DrawLectureList(GUIStyle label, float width, ref int count)
        {
            CampusTheme.Section("동영상 출석");
            showCompletedLectures = CampusTheme.Option("출석 확인된 동영상 표시", showCompletedLectures);
            GUILayout.Space(8 * CampusScreenLayout.Scale);
            var items = lectureDetails.FindAll(item => IsCurrentCourse(item) &&
                (string.IsNullOrEmpty(selectedCourseKey) || NoticeCourseKey(item) == selectedCourseKey) &&
                (showCompletedLectures || item.attendanceStale || !item.attendanceKnown || !item.attended));
            items.Sort((a, b) => CompareDeadlines(a.dueAt, b.dueAt));
            foreach (var item in items)
            {
                count++;
                var deadline = CampusScreenLayout.DeadlineColor(CampusScreenLayout.Stage(item.dueAt, DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
                var lectureTitle = string.IsNullOrWhiteSpace(item.title) ? "동영상 강의" : item.title;
                if (CampusTheme.ListCard(lectureTitle, LectureRemaining(item) + "\n" + LectureAttendanceLabel(item),
                    width, deadline, ReferenceEquals(selectedNotice, item)))
                    selectedNotice = ReferenceEquals(selectedNotice, item) ? null : item;
                if (!ReferenceEquals(selectedNotice, item)) continue;
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label(CourseLabel(item) + (item.week > 0 ? item.week + "주차" : ""), CampusTheme.SectionTitle);
                DrawLectureStatus(item, label);
                GUILayout.Label("학교 플레이어에서 시청한 뒤 학교의 출석 상태를 확인하세요.", label);
                if (CampusTheme.Action("강의 보기", CampusTheme.Primary)) OpenLecture(item);
                GUILayout.EndVertical();
                GUILayout.Space(8 * CampusScreenLayout.Scale);
            }
        }

        private static void DrawLectureStatus(Notice item, GUIStyle label)
        {
            GUILayout.Label(LecturePeriod(item), label);
            if (item.lateDueAt > 0) GUILayout.Label("지각 인정 종료: " + LectureTime(item.lateDueAt) + " (한국 시간)", label);
            GUILayout.Label(LectureAttendanceLabel(item), label);
            if (!string.IsNullOrWhiteSpace(item.progressText)) GUILayout.Label(item.progressText, label);
            if (!string.IsNullOrWhiteSpace(item.availabilityText)) GUILayout.Label(item.availabilityText, label);
        }

        private void DrawNotices(GUIStyle label, float width)
        {
            if (!string.IsNullOrEmpty(selectedCourseKey))
                CampusTheme.ScreenTitle(selectedCourseTitle, null);
            int count = 0;
            DrawNoticeList(notificationDetails, "알림", label, width, ref count);
            DrawNoticeList(messageDetails, "쪽지", label, width, ref count);
            CampusTheme.Section("과제");
            showSubmittedAssignments = CampusTheme.Option("제출 완료 과제 표시", showSubmittedAssignments);
            GUILayout.Space(8 * CampusScreenLayout.Scale);
            DrawNoticeList(PendingAssignments(), null, label, width, ref count, assignment: true);
            if (showSubmittedAssignments)
                DrawNoticeList(assignmentDetails.FindAll(item => item.submissionKnown && item.submitted && IsCurrentCourse(item)),
                    "제출 완료 과제", label, width, ref count, assignment: true);
            DrawLectureList(label, width, ref count);
            if (count == 0)
            {
                bool assignmentsKnown = enrolledCourses.Count > 0;
                foreach (var course in enrolledCourses.Values)
                    if ((string.IsNullOrEmpty(selectedCourseKey) || NoticeCourseKey(course) == selectedCourseKey) &&
                        (!assignmentsConfirmed.Contains(course.courseId) || !lecturesConfirmed.Contains(course.courseId))) assignmentsKnown = false;
                CampusTheme.StatusCard(assignmentsKnown && notificationsConfirmed ? "남은 일정이나 알림이 없습니다." :
                    courseworkFailed || networkWasUnavailable ? "일부 일정을 불러오지 못했습니다." : "일정과 알림을 불러오는 중…");
            }
            if (!string.IsNullOrEmpty(selectedCourseKey))
            {
                CampusTheme.Section("강의실");
                GUILayout.BeginVertical(GUI.skin.box);
                DrawClassroom(label);
                GUILayout.EndVertical();
                if (CampusTheme.Action("모든 과목의 알림 보기")) SelectCourse(null, null);
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
            GUILayout.Label(CourseClassrooms.Label(code), CampusTheme.SectionTitle);
            GUILayout.Label(CourseClassrooms.Source(code), CampusTheme.Caption);
            if (string.IsNullOrWhiteSpace(code)) return;
            if (!editingClassroom && CampusTheme.Action("강의실 입력 / 수정"))
            { classroomInput = CourseClassrooms.Room(code); editingClassroom = true; }
            if (!editingClassroom) return;
            GUILayout.Label(CourseClassrooms.Term + "학기 · 수강신청 시간표의 강의실 (요일별 여러 교실 입력 가능)", label);
            classroomInput = GUILayout.TextField(classroomInput, 120, GUILayout.MinHeight(44 * CampusScreenLayout.Scale));
            bool enabled = GUI.enabled;
            GUI.enabled = enabled && !string.IsNullOrWhiteSpace(classroomInput);
            if (CampusTheme.Action("이 기기에 저장", CampusTheme.Primary))
            { CourseClassrooms.Save(code, classroomInput); editingClassroom = false; }
            GUI.enabled = enabled;
            if (CampusTheme.Action("입력값 초기화", GUI.skin.button))
            { CourseClassrooms.Reset(code); classroomInput = CourseClassrooms.Room(code); editingClassroom = false; }
            if (CampusTheme.Action("취소", GUI.skin.button)) editingClassroom = false;
        }

        private void DrawNoticeList(List<Notice> items, string section, GUIStyle label, float width, ref int count, bool assignment = false)
        {
            bool needsHeading = section != null;
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            foreach (var item in items)
            {
                if (currentRosterKnown && NoticeCourseKey(item) != null && !IsCurrentCourse(item)) continue;
                if (!string.IsNullOrEmpty(selectedCourseKey) && NoticeCourseKey(item) != selectedCourseKey) continue;
                if (!assignment && item.readKnown && item.read) continue;
                if (needsHeading)
                {
                    CampusTheme.Section(section);
                    needsHeading = false;
                }
                count++;
                string title = string.IsNullOrWhiteSpace(item.title) ? "제목 없음" : item.title;
                var period = NoticeDeadline(item);
                long due = period.dueAt;
                var color = CampusScreenLayout.DeadlineColor(CampusScreenLayout.Stage(due, now));
                var time = CampusScreenLayout.RemainingTime(due);
                string status = assignment ? item.submissionKnown ? item.submitted ? "제출 완료" : "미제출" : "제출 확인 필요" :
                    item.readKnown ? "안 읽음" : "읽음 상태 미확인";
                string caption = CourseLabel(item) + status;
                if (!string.IsNullOrWhiteSpace(item.sender)) caption += " · " + item.sender;
                if (assignment || due > 0) caption += "\n" + time;
                if (!assignment && color == Color.white) color = CampusTheme.Accent;
                bool clicked = CampusTheme.ListCard(title, caption, width, color,
                    expanded: !assignment && ReferenceEquals(selectedNotice, item), deadlineGradient: assignment);
                if (clicked)
                {
                    if (assignment) OpenAssignmentDetail(item);
                    else selectedNotice = ReferenceEquals(selectedNotice, item) ? null : item;
                }
                if (assignment) continue;
                if (!ReferenceEquals(selectedNotice, item)) continue;
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label(title, CampusTheme.SectionTitle);
                GUILayout.Label(CourseClassrooms.Label(ClassroomCode(item)), CampusTheme.Caption);
                if (due > 0 && due <= 253402300799L)
                    GUILayout.Label("마감 " + DateTimeOffset.FromUnixTimeSeconds(due).ToOffset(TimeSpan.FromHours(9)).ToString("yyyy-MM-dd HH:mm") +
                        " (한국 시간) · " + CampusScreenLayout.DeadlineLabel(due, now), CampusTheme.Caption);
                GUILayout.Label(string.IsNullOrWhiteSpace(item.body) ?
                    "학교에서 본문을 제공하지 않았습니다. 학교 알림 기록에서 상세 내용을 확인해 주세요." : item.body, label);
                if (SchoolPage(item.url) && CampusTheme.Action("학교에서 원문 보기")) OpenNoticePage(item);
                GUILayout.EndVertical();
                GUILayout.Space(8 * CampusScreenLayout.Scale);
            }
        }

        private bool OpenAssignmentDetail(Notice item)
        {
            if (view == null || !IsAuthenticated || !SchoolPage(item.url)) return false;
            bool hasCache = assignmentCache.TryGetValue(item.url, out var saved) &&
                Time.unscaledTime - saved.receivedAt < AssignmentCacheSeconds;
            if (!hasCache && string.IsNullOrEmpty(nonce)) return false;
            if (assignmentView == null)
            {
                assignmentView = new GameObject("Pixel Campus Assignment Detail").AddComponent<WebViewObject>();
                assignmentView.transform.SetParent(transform);
                assignmentView.Init(cb: ReceiveAssignmentAction,
                    ld: _ => { AssignmentRenderStatus = null; assignmentRendererDeadline = 0; },
                    err: _ => { AssignmentRenderStatus = "과제 화면을 표시하지 못했습니다. 뒤로 돌아가 다시 열어 주세요."; assignmentRendererDeadline = 0; },
                    httpErr: _ => { AssignmentRenderStatus = "과제 첨부파일을 불러오지 못했습니다. 뒤로 돌아가 다시 시도해 주세요."; assignmentRendererDeadline = 0; },
                    androidForceDarkMode: 1);
            }
            assignmentDocumentTarget = item;
            assignmentEditAvailable = hasCache && saved.canEditSubmission;
            assignmentUploadStatus = null;
            assignmentUploadDraft = false;
            assignmentVisible = true; assignmentAttachmentVisible = false;
            assignmentDocumentNonce = Guid.NewGuid().ToString("N");
            view.SetVisibility(false); courseCubes?.SetVisible(false);
            ApplyWebViewMargins(); assignmentView.SetVisibility(!websiteNoticesVisible);
            if (hasCache) RenderAssignmentDocument(saved.body);
            else RefreshAssignmentDetail();
            return true;
        }

        private void CacheAssignment(string url, string body, bool canEditSubmission)
        {
            if (body.Length > 250_000) return;
            if (assignmentCache.Count >= AssignmentCacheLimit && !assignmentCache.ContainsKey(url))
            {
                string oldest = null;
                float time = float.MaxValue;
                foreach (var entry in assignmentCache)
                    if (entry.Value.receivedAt < time) { oldest = entry.Key; time = entry.Value.receivedAt; }
                if (oldest != null) assignmentCache.Remove(oldest);
            }
            assignmentCache[url] = (body, canEditSubmission, Time.unscaledTime);
        }

        private void RefreshAssignmentDetail()
        {
            if (!assignmentVisible || assignmentSubmissionVisible || assignmentDocumentTarget == null ||
                !string.IsNullOrEmpty(assignmentDocumentRequest)) return;
            assignmentDocumentParts = null;
            if (string.IsNullOrEmpty(assignmentDocumentHtml))
                RenderAssignmentDocument("<p class='notice'>과제를 불러오는 중…</p>");
            var capture = Resources.Load<TextAsset>("SmartleadAssignmentDocument");
            if (capture == null || string.IsNullOrEmpty(nonce))
            {
                if (!assignmentCache.ContainsKey(assignmentDocumentTarget.url))
                    RenderAssignmentDocument("<p class='notice'>과제를 불러오지 못했습니다. 다시 확인해 주세요.</p>");
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
                AssignmentRenderStatus = "과제 화면을 열 수 없습니다. 다시 확인해 주세요.";
                Debug.LogError("Pixel Campus: Resources/AssignmentDetail.html 템플릿을 불러오지 못했습니다. .meta GUID와 TextAsset 가져오기 상태를 확인하세요.");
                return;
            }
            if (assignmentView == null)
            {
                AssignmentRenderStatus = "과제 화면 표시 장치가 준비되지 않았습니다. 뒤로 돌아가 다시 열어 주세요.";
                return;
            }
            var courseTitle = System.Net.WebUtility.HtmlEncode(assignmentDocumentTarget?.courseName ?? "");
            var assignmentTitle = System.Net.WebUtility.HtmlEncode(assignmentDocumentTarget?.title ?? "과제 상세");
            string font = GetAssignmentFontRule();
            // Leave headroom for string transport to the native WebView. Long documents use the CSS fallback.
            if (template.text.Length + courseTitle.Length + assignmentTitle.Length + content.Length + font.Length >
                MaxDocumentCharsWithEmbeddedFont) font = "";
            // Replace content last: school text containing a template marker must remain text.
            assignmentDocumentHtml = template.text.Replace("__ASSIGNMENT_FONT_RULE__", font)
                .Replace("__DOCUMENT_NONCE__", assignmentDocumentNonce)
                .Replace("__COURSE_TITLE__", courseTitle)
                .Replace("__ASSIGNMENT_TITLE__", assignmentTitle)
                .Replace("__ASSIGNMENT_UPLOAD_STATUS__", System.Net.WebUtility.HtmlEncode(
                    assignmentUploadStatus ?? "파일을 선택한 뒤 업로드·저장을 누르세요."))
                .Replace("__ASSIGNMENT_UPLOAD_OPTIONS__", assignmentUploadDraft ? "open" : "")
                .Replace("__ASSIGNMENT_CONTENT__", content);
            AssignmentRenderStatus = null;
            assignmentRendererDeadline = Time.unscaledTime + 10;
            if (!assignmentView.IsInitialized()) pendingAssignmentHtml = assignmentDocumentHtml;
            else
            {
                pendingAssignmentHtml = null;
                assignmentView.LoadHTML(assignmentDocumentHtml, "https://smartlead.hallym.ac.kr/");
            }
        }

        private static string GetAssignmentFontRule()
        {
            if (assignmentFontRule != null) return assignmentFontRule;
            // The bundled WOFF2 is a renamed Pretendard 1.3.9 subset for Latin and common Hangul.
            var asset = Resources.Load<TextAsset>("CampusFonts/CampusDocumentSans");
            assignmentFontRule = asset == null ? "" :
                "@font-face{font-family:'Campus Document Sans';src:url(data:font/woff2;base64," +
                Convert.ToBase64String(asset.bytes) + ") format('woff2');font-weight:400;font-display:swap}";
            return assignmentFontRule;
        }

        private void ReceiveAssignmentAction(string message)
        {
            if (!assignmentVisible || string.IsNullOrEmpty(message) || message.Length > 64000) return;
            Capture data;
            try { data = JsonUtility.FromJson<Capture>(message); } catch { return; }
            if (data == null || data.nonce != assignmentDocumentNonce || assignmentSubmissionVisible) return;
            if (data.kind == "assignment_upload")
            {
                ReceiveAssignmentUpload(message);
                return;
            }
            if (assignmentUploadId != null) return;
            if (data.kind == "assignment_submission") { OpenAssignmentSubmission(); return; }
            if (data.kind != "assignment_attachment" || !SchoolPage(data.url)) return;
            var path = new Uri(data.url).AbsolutePath;
            if (!(path.Contains("/pluginfile.php/") || path.Contains("/draftfile.php/"))) return;
            assignmentAttachmentVisible = true;
            assignmentView.LoadURL(data.url); // Only an explicitly selected file, never the original assignment page.
        }

        private void ReceiveAssignmentUpload(string message)
        {
            UploadPacket packet;
            try { packet = JsonUtility.FromJson<UploadPacket>(message); } catch { return; }
            if (packet == null || !Guid.TryParseExact(packet.requestId, "N", out _) ||
                assignmentAttachmentVisible || assignmentDocumentTarget == null || view == null) return;
            if (packet.status == "begin" && assignmentUploadId == null)
            {
                var script = Resources.Load<TextAsset>("SmartleadAssignmentUpload");
                var url = AssignmentSubmissionPage(assignmentDocumentTarget.url, false);
                if (!IsAuthenticated || !SchoolPage(currentUrl) || string.IsNullOrEmpty(nonce) ||
                    !assignmentEditAvailable || script == null || url == null ||
                    packet.files == null || packet.files.Length == 0 ||
                    Array.Exists(packet.files, file => file == null || file.size <= 0 ||
                        string.IsNullOrWhiteSpace(file.name) || file.name.Length > 255))
                {
                    ShowAssignmentUploadReply(new UploadPacket { requestId = packet.requestId, status = "unsupported",
                        error = "파일 정보나 제출 조건을 확인해 주세요. 이 과제는 추가 제출 옵션이 필요할 수 있습니다." });
                    return;
                }
                assignmentUploadId = packet.requestId;
                assignmentUploadDeadline = Time.realtimeSinceStartup + 315;
                packet.nonce = nonce;
                packet.url = url;
                view.EvaluateJS(script.text.Replace("__ASSIGNMENT_UPLOAD_REQUEST__", JsonUtility.ToJson(packet)));
                return;
            }
            if (assignmentUploadId != packet.requestId) return;
            if (packet.status == "chunk" && packet.fileIndex >= 0 && packet.index >= 0 &&
                !string.IsNullOrEmpty(packet.data) && packet.data.Length <= 48000)
                view.EvaluateJS("window.PixelCampusAssignmentUpload?.receive(" + JsonUtility.ToJson(packet) + ");");
            else if (packet.status == "cancel")
            {
                ShowAssignmentUploadReply(new UploadPacket { requestId = packet.requestId, status = "error",
                    error = "첨부 파일을 읽지 못했습니다. 파일을 다시 선택해 주세요." });
                StopAssignmentUpload();
            }
        }

        private void ReceiveAssignmentUploadReply(string message)
        {
            UploadPacket packet;
            try { packet = JsonUtility.FromJson<UploadPacket>(message); } catch { return; }
            if (packet == null || assignmentUploadId == null || packet.requestId != assignmentUploadId || !assignmentVisible) return;
            ShowAssignmentUploadReply(packet);
            if (packet.status is "pull" or "progress" or "constraints") return;
            assignmentUploadId = null;
            if (packet.status != "saved" && packet.status != "draft") return;
            assignmentUploadStatus = packet.error;
            assignmentUploadDraft = packet.status == "draft";
            if (assignmentUploadDraft) assignmentEditAvailable = false;
            assignmentCache.Remove(assignmentDocumentTarget.url);
            RefreshAssignmentDetail();
        }

        private void ShowAssignmentUploadReply(UploadPacket packet)
        {
            packet.nonce = packet.page = null;
            assignmentView?.EvaluateJS("window.PixelCampusUpload?.receive(" + JsonUtility.ToJson(packet) + ");");
        }

        private void StopAssignmentUpload()
        {
            if (assignmentUploadId == null) return;
            assignmentUploadId = null;
            view?.EvaluateJS("window.PixelCampusAssignmentUpload?.cancel();");
        }

        private static string AssignmentSubmissionPage(string url, bool canEditSubmission)
        {
            if (!SchoolPage(url)) return null;
            var address = new Uri(url);
            if (address.AbsolutePath != "/mod/assign/view.php" && address.AbsolutePath != "/mod/ubassign/view.php") return null;
            var ids = Regex.Matches(address.Query, @"(?:^\?|&)id=([^&]*)");
            if (ids.Count != 1 || !Regex.IsMatch(ids[0].Groups[1].Value, @"\A[1-9][0-9]*\z")) return null;
            return address.GetLeftPart(UriPartial.Path) + "?id=" + ids[0].Groups[1].Value +
                (canEditSubmission ? "&action=editsubmission" : "");
        }

        public void OpenAssignmentSubmission()
        {
            if (!assignmentVisible || assignmentAttachmentVisible || assignmentSubmissionVisible || assignmentUploadId != null || !IsAuthenticated ||
                assignmentDocumentTarget == null || view == null) return;
            var submissionPage = AssignmentSubmissionPage(assignmentDocumentTarget.url, assignmentEditAvailable);
            if (submissionPage == null) return;
            assignmentCache.Remove(assignmentDocumentTarget.url);
            assignmentDocumentRequest = null; assignmentDocumentParts = null;
            assignmentDocumentDeadline = assignmentRendererDeadline = 0;
            pendingAssignmentHtml = null;
            assignmentSubmissionVisible = true; assignmentAttachmentVisible = false;
            submissionConversionPrepared = false;
            refreshAssignmentAfterSubmission = false;
            AssignmentRenderStatus = null;
            assignmentView?.EvaluateJS("document.querySelectorAll('video,audio').forEach(function(player){player.pause();});");
            assignmentView?.SetVisibility(false);
            // Reuse the authenticated WebView so HttpOnly cookies and the school's
            // CSRF/session fields never have to be copied into a separate uploader.
            view.SetVisibility(!websiteNoticesVisible);
            view.LoadURL(submissionPage);
        }

        public void CloseAssignmentDetail() => CloseAssignmentDetail(returnToCampus: false);

        internal void CloseAssignmentDetail(bool returnToCampus)
        {
            if (assignmentUploadId != null) return;
            bool restoreCollector = assignmentSubmissionVisible;
            if (!returnToCampus && restoreCollector)
            {
                submissionConversion?.Dispose();
                assignmentSubmissionVisible = false;
                refreshAssignmentAfterSubmission = true;
                view.SetVisibility(false);
                assignmentView?.SetVisibility(!websiteNoticesVisible);
                RenderAssignmentDocument("<p class='notice'>학교에서 제출 상태를 다시 확인하고 있습니다…</p>");
                assignmentDocumentDeadline = Time.unscaledTime + 16;
            }
            else if (!returnToCampus && assignmentAttachmentVisible)
            {
                assignmentAttachmentVisible = false;
                assignmentView?.LoadHTML(assignmentDocumentHtml, "https://smartlead.hallym.ac.kr/");
            }
            else
            {
                DismissAssignmentDetail();
                courseCubes?.SetVisible(IsAuthenticated && !authenticationVisible);
            }

            if (!restoreCollector) return;
            // Restore the collector page only after the user leaves the form.
            // Loading it during a file chooser/app resume would discard input.
            resumeRefreshPending = false;
            view.LoadURL(NotificationPage);
        }

        private void DismissAssignmentDetail()
        {
            // Hiding a WebView does not necessarily stop its media playback.
            assignmentView?.EvaluateJS("document.querySelectorAll('video,audio').forEach(function(player){player.pause();});");
            assignmentVisible = assignmentAttachmentVisible = false;
            if (assignmentSubmissionVisible) view?.SetVisibility(false);
            assignmentSubmissionVisible = refreshAssignmentAfterSubmission = false;
            assignmentEditAvailable = false;
            StopAssignmentUpload();
            assignmentUploadStatus = null;
            submissionConversion?.Dispose();
            assignmentDocumentRequest = assignmentDocumentHtml = assignmentDocumentNonce = null;
            pendingAssignmentHtml = null; AssignmentRenderStatus = null; assignmentRendererDeadline = 0;
            assignmentDocumentParts = null; assignmentDocumentTarget = null; assignmentDocumentDeadline = 0;
            assignmentView?.SetVisibility(false);
        }

        public void Draw(GUIStyle label, float width)
        {
            if (IsOutlineActivityOpen)
            {
                DrawOutlineDetails();
                return;
            }
            if (!string.IsNullOrEmpty(selectedCourseKey))
            {
                GUILayout.BeginHorizontal();
                if (CampusTheme.Action("강의 정보", showCourseOutline ? CampusTheme.Secondary : GUI.skin.button))
                {
                    showCourseOutline = true;
                    RequestCourseOutline();
                }
                if (CampusTheme.Action("과제와 알림", !showCourseOutline ? CampusTheme.Secondary : GUI.skin.button))
                    showCourseOutline = false;
                GUILayout.EndHorizontal();
                if (showCourseOutline)
                {
                    DrawCourseOutline(label, width);
                    return;
                }
            }
            DrawNotices(label, width);
            GUILayout.Space(12 * CampusScreenLayout.Scale);
            if (CampusTheme.Action("다시 확인", CampusTheme.Primary)) Refresh();
            if (CampusTheme.Action("학교 알림 기록 보기")) OpenHistoryPage();
        }

        private void DrawCourseOutline(GUIStyle label, float width)
        {
            CampusTheme.ScreenTitle(selectedCourseTitle, null);
            if (courseOutlines.TryGetValue(outlineCourseId, out var outline))
            {
                float scale = CampusScreenLayout.Scale;
                foreach (var week in outline.weeks)
                {
                    if (week == null || string.IsNullOrEmpty(week.key)) continue;
                    bool expanded = expandedWeeks.Contains(week.key);
                    if (CampusTheme.ListCard(week.title, null, width, Color.white, expanded, disclosure: true))
                    {
                        if (expanded) expandedWeeks.Remove(week.key);
                        else expandedWeeks.Add(week.key);
                    }
                    if (!expanded) continue;
                    GUILayout.BeginVertical(GUI.skin.box);
                    if (!string.IsNullOrWhiteSpace(week.body)) GUILayout.Label(week.body, label);
                    if (week.activities == null || week.activities.Length == 0)
                    {
                        if (string.IsNullOrWhiteSpace(week.body)) GUILayout.Label("공개된 자료가 없습니다.", CampusTheme.Caption);
                    }
                    else foreach (var activity in week.activities)
                    {
                        if (activity == null) continue;
                        var format = OutlineActivityFormat(activity);
                        var preview = OutlinePreview(activity.body);
                        string badge = format;
                        if (format.Length > 5)
                        {
                            badge = "파일";
                            preview = string.IsNullOrEmpty(preview) ? format : format + " · " + preview;
                        }
                        if (CampusTheme.ListCard(OutlineActivityTitle(activity), preview, width - 32 * scale, Color.white,
                            fileBadge: badge, fileTint: OutlineBadgeTint(format)))
                            SelectOutlineActivity(week, activity);
                    }
                    GUILayout.EndVertical();
                    GUILayout.Space(8 * scale);
                }
                if (outline.partial) CampusTheme.StatusCard("일부 주차를 불러오지 못했습니다. 원문을 열거나 다시 확인해 주세요.");
            }
            else
            {
                CampusTheme.StatusCard(outlineCourseId <= 0 || outlineFailed ? "강의 정보를 불러오지 못했습니다." : "강의 정보를 불러오는 중…");
            }
            if (outlineCourseId <= 0) return;
            if (outlineRequest == null && (outlineFailed || outline != null) && CampusTheme.Action("다시 확인"))
            {
                courseOutlines.Remove(outlineCourseId);
                outlineFailed = false;
                RequestCourseOutline();
            }
            if (CampusTheme.Action("학교 강의실 열기"))
                OpenNoticePage(new Notice { url = "https://smartlead.hallym.ac.kr/course/view.php?id=" + outlineCourseId });
        }

        private void SelectOutlineActivity(OutlineWeek week, OutlineActivity activity)
        {
            if (IsOutlineActivityUrl(activity.url))
            {
                string path = new Uri(activity.url).AbsolutePath;
                if (path == "/mod/assign/view.php" || path == "/mod/ubassign/view.php")
                {
                    var assignment = assignmentDetails.Find(item => SameActivity(item.url, activity.url)) ??
                        new Notice { courseId = outlineCourseId, courseName = selectedCourseTitle,
                            title = OutlineActivityTitle(activity), url = activity.url };
                    if (OpenAssignmentDetail(assignment)) return;
                }
            }
            selectedOutlineActivity = activity;
            selectedOutlineWeekTitle = week.title;
        }

        private void DrawOutlineDetails()
        {
            var activity = selectedOutlineActivity;
            string title = OutlineActivityTitle(activity);
            var context = string.IsNullOrWhiteSpace(selectedOutlineWeekTitle) ? selectedCourseTitle :
                selectedCourseTitle + " · " + selectedOutlineWeekTitle;
            GUILayout.Label(context, CampusTheme.Caption);
            CampusTheme.ScreenTitle(title, OutlineActivityFormat(activity));
            if (!string.IsNullOrWhiteSpace(activity.fileName) && activity.fileName != title)
                GUILayout.Label(activity.fileName, CampusTheme.Caption);
            var lecture = IsLectureUrl(activity.url) ? lectureDetails.Find(item => SameActivity(item.url, activity.url)) : null;
            string body = activity.body;
            if (string.IsNullOrWhiteSpace(body) && IsOutlineActivityUrl(activity.url))
                body = notificationDetails.Find(item => SameActivity(item.url, activity.url) &&
                    !string.IsNullOrWhiteSpace(item.body))?.body;
            if (!string.IsNullOrWhiteSpace(body)) CampusTheme.StatusCard(body);
            if (lecture != null)
            {
                GUILayout.BeginVertical(GUI.skin.box);
                DrawLectureStatus(lecture, CampusTheme.Body);
                GUILayout.EndVertical();
            }
            else if (string.IsNullOrWhiteSpace(body))
                CampusTheme.StatusCard("등록된 자료 설명이 없습니다.");
        }

        private static string OutlineActivityTitle(OutlineActivity activity) =>
            string.IsNullOrWhiteSpace(activity.title) ? "강의 자료" : activity.title;

        private static string OutlinePreview(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return null;
            const int limit = 52;
            string preview = Regex.Replace(body, @"\s+", " ").Trim();
            if (preview.Length <= limit) return preview;
            int end = char.IsHighSurrogate(preview[limit - 1]) ? limit - 1 : limit;
            return preview.Substring(0, end).TrimEnd() + "…";
        }

        private static string OutlineActivityFormat(OutlineActivity activity)
        {
            if (!string.IsNullOrEmpty(activity.format) && Regex.IsMatch(activity.format, @"\A[A-Z0-9]{1,12}\z"))
                return activity.format;
            if (!IsOutlineActivityUrl(activity.url)) return "안내";
            if (IsLectureUrl(activity.url)) return "동영상";
            string path = new Uri(activity.url).AbsolutePath;
            if (path.StartsWith("/pluginfile.php/", StringComparison.Ordinal)) return "파일";
            return path.Split('/')[2] switch
            {
                "resource" => "파일",
                "folder" => "폴더",
                "assign" or "ubassign" => "과제",
                "forum" or "ubboard" => "게시판",
                "quiz" => "퀴즈",
                "url" => "링크",
                "page" or "book" or "label" => "문서",
                _ => "자료"
            };
        }

        private static Color OutlineBadgeTint(string format) => format switch
        {
            "PDF" => new Color32(169, 48, 59, 255),
            "PPT" or "PPTX" or "과제" => new Color32(153, 69, 29, 255),
            "XLS" or "XLSX" or "CSV" => new Color32(30, 104, 72, 255),
            "동영상" or "MP4" or "WEBM" or "MOV" => new Color32(106, 68, 151, 255),
            "DOC" or "DOCX" or "HWP" or "HWPX" or "링크" => CampusTheme.Accent,
            _ => CampusTheme.Muted
        };

        private static bool IsOutlineActivityUrl(string url)
        {
            if (!SchoolPage(url)) return false;
            var address = new Uri(url);
            return !System.Text.RegularExpressions.Regex.IsMatch(address.Query, @"(?:[?&])(sesskey|token|action|edit|delete|submit)=", System.Text.RegularExpressions.RegexOptions.IgnoreCase) &&
                System.Text.RegularExpressions.Regex.IsMatch(address.AbsolutePath, @"^/(?:mod/[a-z0-9_]+/(?:view|viewer|index)\.php$|pluginfile\.php/\d+/)");
        }

        private void OnApplicationQuit() { view?.SaveCookies(); }
        private void OnDestroy() { submissionConversion?.Dispose(); pendingPassword = null; if (assignmentView != null) Destroy(assignmentView.gameObject); if (view != null) { view.SaveCookies(); Destroy(view.gameObject); } }
    }
}



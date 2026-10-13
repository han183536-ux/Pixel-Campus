using System;
using System.Collections.Generic;
using Gree.UnityWebView;
using UnityEngine;

namespace WebNotifications
{
    [RequireComponent(typeof(NotificationManager))]
    public class SmartleadWebView : MonoBehaviour
    {
        [Serializable] private class Notice { public int id, courseId; public string sender, title, courseName, courseCode, type; public bool read, readKnown; }
        [Serializable] private class Capture { public string kind, nonce, page, source, error; public int count, total; public bool found, unreadLimited, historyLimited, countOnly, unreadUnknown; public Notice[] items; }
        private const string NotificationPage = "https://smartlead.hallym.ac.kr/local/ubion/user/index.php";
        private WebViewObject view;
        private SmartleadCourseCubes courseCubes;
        private string nonce, currentUrl, script;
        private float nextRefresh;
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
        public string Status { get; private set; } = "알림 확인 준비 중";
        public string MessageStatus { get; private set; } = "쪽지 확인 준비 중";
        private string apiStatus = "Moodle 알림 API 확인 중";
        public int Count { get; private set; }
        public int Total { get; private set; }
        public bool HasResult { get; private set; }
        public int MessageCount { get; private set; }
        public int MessageTotal { get; private set; }
        public bool HasMessagesResult { get; private set; }

        private void Start()
        {
            if (!GetComponent<NotificationManager>().useWebView) return;
            courseCubes = GetComponent<SmartleadCourseCubes>();
            if (courseCubes == null) courseCubes = gameObject.AddComponent<SmartleadCourseCubes>();
            var asset = Resources.Load<TextAsset>("SmartleadCapture");
            if (asset == null) { Status = "알림 확인 스크립트가 없습니다."; return; }
            script = asset.text;
            try
            {
                view = new GameObject("SmartLEAD Notification WebView").AddComponent<WebViewObject>();
                view.transform.SetParent(transform);
                view.Init(cb: Receive, err: _ => Status = "SmartLEAD 연결 실패",
                    httpErr: _ => Status = "SmartLEAD 페이지 오류",
                    started: url => { currentUrl = url; nonce = null; HasResult = false; HasMessagesResult = false;
                        schoolResult = false; schoolStatus = "학교 알림 조회 중"; apiStatus = "Moodle 알림 API 확인 중";
                        historyResult = false; notificationPriority = 0; historyStatus = "읽은 알림 포함 기록 조회 중";
                        titles.Clear(); messages.Clear();
                        Status = "알림 확인 중"; MessageStatus = "쪽지 확인 중"; },
                    ld: url => { currentUrl = url; Inject(); });
                view.SetVisibility(false);
                view.LoadURL(NotificationPage);
                nextRefresh = Time.unscaledTime + 30;
            }
            catch (Exception ex) { Status = "WebView 초기화 실패: " + ex.GetType().Name; }
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
                returnFromLogin = true;
                ShowAuthentication();
                Status = "오른쪽 학교 페이지에서 로그인하면 알림·쪽지를 확인합니다.";
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
            view.EvaluateJS(script.Replace("__UNITY_NONCE__", nonce));
        }

        private void Update()
        {
            if (view == null) return;
            if (authenticationVisible)
            {
                view.SetMargins(Mathf.Min(464, Mathf.RoundToInt(Screen.width * .42f) + 24), 12, 12, 12);
                return;
            }
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 30;
            Refresh();
        }

        private void ShowAuthentication()
        {
            if (view == null || authenticationVisible) return;
            authenticationVisible = true;
            courseCubes?.SetVisible(false);
            view.SetMargins(Mathf.Min(464, Mathf.RoundToInt(Screen.width * .42f) + 24), 12, 12, 12);
            view.SetVisibility(true);
        }

        public void OpenAccountPage()
        {
            if (view == null) return;
            accountPagePinned = true;
            courseCubes?.Clear();
            ShowAuthentication();
            view.LoadURL("https://smartlead.hallym.ac.kr/login/index.php");
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
            accountPagePinned = false;
            authenticationVisible = false;
            view.SetVisibility(false);
            courseCubes?.SetVisible(true);
            nextRefresh = Time.unscaledTime + 30;
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
            if (courseCubes == null) return;
            var entries = new List<CourseCubeNotice>();
            if (items != null)
                foreach (var item in items)
                    if (item != null && (isMessage || item.type != "message"))
                        entries.Add(new CourseCubeNotice { courseId = item.courseId,
                            courseName = item.courseName, courseCode = item.courseCode });
            if (isMessage) courseCubes.SetMessages(entries); else courseCubes.SetNotifications(entries);
        }

        public void Refresh()
        {
            if (view == null) return;
            if (!SchoolPage(currentUrl) || new Uri(currentUrl).AbsolutePath.StartsWith("/login", StringComparison.OrdinalIgnoreCase))
                view.LoadURL(NotificationPage);
            else view.Reload();
        }

        private void Receive(string message)
        {
            if (string.IsNullOrEmpty(nonce) || string.IsNullOrEmpty(message) || message.Length > 64000 || !SchoolPage(currentUrl)) return;
            Capture data;
            try { data = JsonUtility.FromJson<Capture>(message); } catch { return; }
            if (data == null || data.nonce != nonce || data.page != currentUrl) return;
            if (data.kind == "authentication")
            {
                HasResult = false;
                HasMessagesResult = false;
                schoolResult = false;
                historyResult = false;
                notificationPriority = 0;
                historyStatus = "학교 로그인 후 알림 기록을 조회합니다.";
                titles.Clear(); messages.Clear();
                courseCubes?.Clear();
                schoolStatus = "학교 로그인 계정 확인 필요";
                ShowAuthentication();
                Status = "오른쪽 학교 페이지에서 로그인하면 알림·쪽지를 확인합니다.";
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
            if (data.found) UpdateCourseCubes(data.items, false);
            if (data.found && authenticationVisible && !accountPagePinned)
            {
                HideAccountPage();
            }
            else if (!data.found && !HasMessagesResult) ShowAuthentication();
            Status = !data.found ? "알림 영역 없음 (" + new Uri(currentUrl).AbsolutePath + "). " + apiStatus :
                (data.source == "school_history" ? "학교 알림 기록 (읽은 알림 포함) " :
                    data.source == "school_api" ? "학교 알림·쪽지 " : "Moodle 최근 알림 (읽은 알림 포함) ") + Total + "건" +
                (data.historyLimited ? " · 최근 목록 일부 표시" : "") +
                (data.unreadUnknown ? " · 읽음 상태 미확인" : " · 읽지 않음 " + Count + "건") + " (" +
                (data.source == "dom" ? "화면" : "서버") + ")";
        }

        public void Draw(GUIStyle label)
        {
            GUILayout.Label("SmartLEAD 쪽지·알림 감지", label);
            GUILayout.Label(Status, label);
            GUILayout.Label(MessageStatus, label);
            if (!schoolResult) GUILayout.Label(schoolStatus, label);
            if (!historyResult) GUILayout.Label(historyStatus, label);
            if (GUILayout.Button("다시 확인")) Refresh();
            if (GUILayout.Button("학교 로그인 / 계정 확인")) OpenAccountPage();
            if (GUILayout.Button("학교 알림 기록 보기")) OpenHistoryPage();
            if (accountPagePinned && (HasResult || HasMessagesResult) && GUILayout.Button("학교 화면 닫기")) HideAccountPage();
            if (HasMessagesResult)
            {
                foreach (var message in messages) GUILayout.Label("• " + message, label);
                if (MessageCount > messages.Count) GUILayout.Label("읽지 않은 쪽지: " + MessageCount, label);
            }
            if (HasResult)
            {
                foreach (var title in titles) GUILayout.Label("• " + title, label);
                if (Count > titles.Count) GUILayout.Label("읽지 않은 알림: " + Count, label);
            }
        }

        private void OnDestroy() { if (view != null) Destroy(view.gameObject); }
    }
}

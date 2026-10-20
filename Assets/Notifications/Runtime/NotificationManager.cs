using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace WebNotifications
{
    [RequireComponent(typeof(NotificationApiClient))]
    public class NotificationManager : MonoBehaviour
    {
        public bool useSampleData = true;
        public bool useWebView = false;
        [Min(5)] public float pollSeconds = 30;
        public TextAsset sampleJson;
        public List<NotificationData> Items { get; private set; } = new List<NotificationData>();
        public string Status { get; private set; } = "준비 중";
        public bool IsLoading { get; private set; }
        public bool HasLoaded { get; private set; }
        public bool IsAuthenticated { get; private set; }
        public bool IsAuthenticating { get; private set; }
        public event Action Changed;
        private Coroutine loop;
        private int failures;
        private string day;

        public static DateTime TodayKorea => DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(9)).Date;

        private void OnEnable()
        {
            if (GetComponent<UniversityNoticeFeed>() == null) gameObject.AddComponent<UniversityNoticeFeed>();
            if (useWebView) Status = "WebView 연결 대기"; else loop = StartCoroutine(Poll());
        }
        private void OnDisable()
        {
            StopAllCoroutines();
            loop = null;
            IsLoading = false;
        }
        private void Update()
        {
            var currentDay = TodayKorea.ToString("yyyy-MM-dd");
            if (day == currentDay) return;
            day = currentDay;
            if (useSampleData && !useWebView) Refresh();
            else { Items.RemoveAll(n => !IsToday(n)); Changed?.Invoke(); }
        }
        private IEnumerator Poll()
        {
            while (true)
            {
                yield return Reload();
                yield return new WaitForSecondsRealtime(Mathf.Min(300, Mathf.Max(5, pollSeconds) * Mathf.Pow(2, Mathf.Min(failures, 3))));
            }
        }
        public void Refresh() { if (useWebView) { GetComponent<SmartleadWebView>()?.Refresh(); return; } if (isActiveAndEnabled && !IsLoading && !IsAuthenticating) StartCoroutine(Reload()); }
        public void ReceiveWebView(NotificationData item)
        {
            if (!useWebView || Items.Exists(n => n.id == item.id)) return;
            item.is_read = PlayerPrefs.GetInt(ReadKey(item), 0) == 1;
            Items.Insert(0, item);
            if (Items.Count > 500) Items.RemoveAt(Items.Count - 1);
            HasLoaded = true;
            Status = "WebView에서 링크 수신 · " + DateTime.Now.ToString("HH:mm:ss");
            Changed?.Invoke();
        }
        public void ClearWebViewItems() { Items.Clear(); HasLoaded = false; Status = "수신 목록을 비웠습니다."; Changed?.Invoke(); }
        public void BeginAuthentication()
        {
            StopAllCoroutines(); IsLoading = false; useSampleData = false;
            IsAuthenticating = true;
            Items.Clear(); HasLoaded = false; IsAuthenticated = false;
            Status = "학교 계정 로그인 대기";
            Changed?.Invoke();
        }
        public void FinishAuthentication(bool connected)
        {
            IsAuthenticated = connected;
            IsAuthenticating = false;
            if (connected && isActiveAndEnabled) loop = StartCoroutine(Poll());
            else Status = "학교 계정으로 로그인해 주세요.";
        }
        private IEnumerator Reload()
        {
            if (IsLoading) yield break;
            IsLoading = true;
            if (useSampleData)
            {
                try
                {
                    if (sampleJson == null) throw new InvalidOperationException("샘플 JSON이 지정되지 않았습니다.");
                    var json = sampleJson.text.Replace("{{TODAY}}", TodayKorea.ToString("yyyy-MM-dd"));
                    Apply(JsonUtility.FromJson<NotificationResponse>(json));
                    Status = "샘플 모드 · 한국 시간 기준 오늘";
                }
                catch (Exception ex) { Status = ex.Message; }
            }
            else
            {
                yield return GetComponent<NotificationApiClient>().Fetch(response =>
                {
                    Apply(response);
                    IsAuthenticated = response.mode == "smartlead" && !response.stale;
                    failures = response.stale ? failures + 1 : 0;
                    Status = response.stale ? response.message + " · 마지막 수신 데이터" :
                        (response.mode == "smartlead" ? "SmartLEAD 개인 알림 연결됨" : "연결됨 (" + response.mode + ")") + " · " + DateTime.Now.ToString("HH:mm:ss");
                }, error => { failures++; Status = error + (HasLoaded ? " · 마지막 데이터 유지, 자동 재시도" : " · 자동 재시도"); });
            }
            IsLoading = false;
            Changed?.Invoke();
        }
        private static bool IsToday(NotificationData item)
        {
            return item != null && DateTimeOffset.TryParse(item.published_at, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var time) && time.ToOffset(TimeSpan.FromHours(9)).Date == TodayKorea;
        }
        private void Apply(NotificationResponse response)
        {
            if (response == null || response.notifications == null) throw new FormatException("알림 형식 오류");
            var unique = new HashSet<string>();
            var next = new List<NotificationData>();
            foreach (var item in response.notifications)
            {
                if (!IsToday(item) || string.IsNullOrEmpty(item.id) || string.IsNullOrEmpty(item.source_id)) continue;
                if (!unique.Add(item.source_id + ":" + item.id)) continue;
                item.is_read |= PlayerPrefs.GetInt(ReadKey(item), 0) == 1;
                next.Add(item);
            }
            next.Sort((a, b) => DateTimeOffset.Parse(b.published_at).CompareTo(DateTimeOffset.Parse(a.published_at)));
            Items = next;
            HasLoaded = true;
            Changed?.Invoke();
        }
        private static string ReadKey(NotificationData item) => "web-notification.read." + item.source_id + ":" + item.id;
        public void MarkRead(NotificationData item)
        {
            item.is_read = true;
            PlayerPrefs.SetInt(ReadKey(item), 1);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }
        public int Unread(string source) => Items.FindAll(n => !n.is_read && (string.IsNullOrEmpty(source) || n.source_id == source)).Count;
    }
}

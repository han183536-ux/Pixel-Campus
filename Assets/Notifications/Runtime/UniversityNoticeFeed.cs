using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace WebNotifications
{
    // Public RSS feeds use no SmartLEAD credentials or local Node server.
    public sealed class UniversityNoticeFeed : MonoBehaviour
    {
        [Serializable] public class Board { public string name, url; }
        [Serializable] public class BoardList { public Board[] boards; }
        [Serializable] public class SavedFeed { public string url, updatedAt; public NotificationData[] items; }
        [Serializable] private class Cache { public SavedFeed[] feeds; }
        private readonly List<Board> boards = new List<Board>();
        private readonly Dictionary<string, SavedFeed> feeds = new Dictionary<string, SavedFeed>();
        private readonly Dictionary<string, string> errors = new Dictionary<string, string>();
        private readonly List<NotificationData> items = new List<NotificationData>();
        public IReadOnlyList<NotificationData> Items => items;
        public bool IsLoading { get; private set; }
        public string Status { get; private set; } = "학교 공지 준비 중";
        private string filter = "", search = "", selectedId, addUrl = "", addName = "", addStatus = "";
        private int days = 90;
        private bool unreadOnly, adding, discovering, selectingSource, applicationsOnly, showErrors, hideCompleted = true;
        private string sourceSearch = "";
        private int visibleLimit = 100;
        private int reloadIndex, successful, workers, detailIndex, detailsDone;
        private readonly HashSet<string> detailLoading = new HashSet<string>();
        private float nextRefresh;
        private float nextRebuild;
        private GUIStyle button, filterButton;
        private const string BoardsKey = "PixelCampus.UniversityNoticeBoards.v1";
        private string CachePath => Path.Combine(Application.persistentDataPath, "university-notices-v1.json");

        private void Start()
        {
            var asset = Resources.Load<TextAsset>("UniversityNoticeBoards");
            try
            {
                // Updated built-in sources must also reach users with saved custom boards.
                foreach (var json in new[] { asset != null ? asset.text : "{}", PlayerPrefs.GetString(BoardsKey, "{}") })
                {
                    var config = JsonUtility.FromJson<BoardList>(json);
                    if (config?.boards != null) foreach (var board in config.boards)
                        if (board != null && Allowed(board.url) && !boards.Exists(b => FeedKey(b.url) == FeedKey(board.url))) boards.Add(board);
                }
                if (File.Exists(CachePath))
                {
                    var cache = JsonUtility.FromJson<Cache>(File.ReadAllText(CachePath));
                    if (cache?.feeds != null) foreach (var saved in cache.feeds)
                        if (saved != null && boards.Exists(b => b.url == saved.url) && saved.items != null) feeds[saved.url] = saved;
                }
                foreach (var saved in feeds.Values) foreach (var item in saved.items) if (item != null) UniversityNoticeDeadline.Analyze(item);
            }
            catch (Exception) { Status = "저장된 공지를 읽지 못해 학교에서 다시 확인합니다."; }
            Rebuild();
            Refresh();
        }
        private void Update() { if (!IsLoading && !discovering && Time.unscaledTime >= nextRefresh) Refresh(); }
        public void Refresh()
        {
            if (IsLoading || discovering || !isActiveAndEnabled) return;
            StartCoroutine(Reload());
        }
        private IEnumerator Reload()
        {
            IsLoading = true;
            Status = "학교 공지 확인 중 · 이전 수집 목록도 볼 수 있습니다.";
            reloadIndex = successful = 0; workers = 4;
            var snapshot = boards.ToArray();
            for (int i = 0; i < 4; i++) StartCoroutine(LoadBoards(snapshot));
            while (workers > 0) yield return null;
            Rebuild();
            var pending = items.FindAll(item =>
                TryPublished(item.published_at, out var date) && date > DateTimeOffset.UtcNow.AddDays(-90) &&
                item.detail_checked_at < DateTimeOffset.UtcNow.AddHours(-24).ToUnixTimeSeconds());
            // Check full articles: RSS summaries are often truncated before the application period.
            pending.Sort((a, b) => a.detail_checked_at.CompareTo(b.detail_checked_at));
            detailIndex = detailsDone = 0; workers = 4;
            for (int i = 0; i < 4; i++) StartCoroutine(LoadDetails(pending));
            while (workers > 0) { Status = "게시판 " + successful + "/" + boards.Count + "개 · 신청 일정 원문 확인 " + detailsDone + "/" + pending.Count; yield return null; }
            IsLoading = false;
            nextRefresh = Time.unscaledTime + 900;
            Status = "게시판 " + successful + "/" + boards.Count + "개 확인 · " + items.Count + "건 · " +
                DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(9)).ToString("MM/dd HH:mm") + " 확인";
            if (errors.Count > 0) Status += " · 일부 조회 실패";
            SaveCache();
        }
        private IEnumerator LoadBoards(Board[] snapshot)
        {
            // A board fails independently; a partial failure never erases its last good results.
            while (reloadIndex < snapshot.Length)
            {
                var board = snapshot[reloadIndex++];
                using (var request = UnityWebRequest.Get(board.url))
                {
                    request.timeout = 12;
                    request.redirectLimit = 0;
                    yield return request.SendWebRequest();
                    try
                    {
                        if (request.result != UnityWebRequest.Result.Success) throw new IOException("게시판 연결 실패");
                        var parsed = ParseRss(request.downloadHandler.text, board);
                        if (feeds.TryGetValue(board.url, out var previous)) foreach (var item in parsed)
                        {
                            var old = Array.Find(previous.items, n => n.id == item.id);
                            if (old != null && old.detail_checked_at > 0) { item.message = old.message; item.detail_checked_at = old.detail_checked_at; UniversityNoticeDeadline.Analyze(item); }
                        }
                        feeds[board.url] = new SavedFeed { url = board.url, updatedAt = DateTimeOffset.UtcNow.ToString("o"), items = parsed.ToArray() };
                        errors.Remove(board.url);
                        successful++;
                        if (Time.unscaledTime >= nextRebuild) { Rebuild(); nextRebuild = Time.unscaledTime + .5f; }
                        Status = "공개 게시판 확인 " + reloadIndex + "/" + snapshot.Length + " · " + items.Count + "건";
                    }
                    catch (Exception) { errors[board.url] = "조회 실패 · 이전 수집 결과 유지"; }
                }
            }
            workers--;
        }
        private IEnumerator LoadDetails(List<NotificationData> pending)
        {
            while (detailIndex < pending.Count) { var item = pending[detailIndex++]; yield return FetchDetail(item); detailsDone++; }
            workers--;
        }
        private IEnumerator FetchDetail(NotificationData item)
        {
            if (!Allowed(item.url) || !detailLoading.Add(item.id)) yield break;
            using (var request = UnityWebRequest.Get(item.url))
            {
                request.timeout = 12; request.redirectLimit = 0;
                yield return request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.Success)
                {
                    var body = ExtractArticle(request.downloadHandler.text);
                    if (!string.IsNullOrWhiteSpace(body))
                    {
                        item.message = body; item.detail_checked_at = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                        UniversityNoticeDeadline.Analyze(item);
                    }
                }
            }
            detailLoading.Remove(item.id);
        }
        public static string ExtractArticle(string html)
        {
            // K2web's actual article body, excluding navigation, publication and related posts.
            var container = Regex.Match(html ?? "", @"class\s*=\s*[""'][^""']*\b(?:board-view|viewCont)\b[^""']*[""']", RegexOptions.IgnoreCase);
            int offset = container.Success ? container.Index : 0;
            var start = Regex.Match((html ?? "").Substring(offset), @"<(?:div|section)\b[^>]*class\s*=\s*[""'][^""']*\b(?:artclView|txt)\b[^""']*[""'][^>]*>", RegexOptions.IgnoreCase);
            if (!start.Success) return null;
            int begin = offset + start.Index + start.Length, depth = 1;
            foreach (Match tag in Regex.Matches(html.Substring(begin), @"</?(?:div|section)\b[^>]*>", RegexOptions.IgnoreCase))
            {
                depth += tag.Value.StartsWith("</") ? -1 : 1;
                if (depth == 0) return UniversityNoticeDeadline.Text(html.Substring(begin, tag.Index));
            }
            return null;
        }
        private void SaveCache()
        {
            try { File.WriteAllText(CachePath, JsonUtility.ToJson(new Cache { feeds = new List<SavedFeed>(feeds.Values).ToArray() })); }
            catch (Exception) { Status += " · 기기 저장 실패"; }
        }
        private static string FeedKey(string url) => new Uri(url).AbsolutePath;
        private void OnDisable() { StopAllCoroutines(); detailLoading.Clear(); IsLoading = discovering = false; nextRefresh = 0; }
        public static bool Allowed(string value) => Uri.TryCreate(value, UriKind.Absolute, out var url) &&
            url.Scheme == "https" && url.Port == 443 && string.IsNullOrEmpty(url.UserInfo) &&
            (url.Host == "hallym.ac.kr" || url.Host.EndsWith(".hallym.ac.kr", StringComparison.OrdinalIgnoreCase));
        private static string Plain(string html, int limit)
        {
            string value = Regex.Replace(html ?? "", @"<(script|style)\b[^>]*>.*?</\1>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            value = Regex.Replace(value, "<[^>]*>", " ");
            value = Regex.Replace(WebUtility.HtmlDecode(value), @"\s+", " ").Trim();
            return value.Length > limit ? value.Substring(0, limit) + "…" : value;
        }
        public static List<NotificationData> ParseRss(string xml, Board board)
        {
            if (board == null || !Allowed(board.url) || string.IsNullOrWhiteSpace(xml)) throw new FormatException("공지 RSS 없음");
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 2 * 1024 * 1024 };
            XDocument document;
            using (var reader = XmlReader.Create(new StringReader(xml.Trim()), settings)) document = XDocument.Load(reader);
            var channel = document.Root?.Name.LocalName == "rss" ? document.Root.Element("channel") : null;
            if (channel == null) throw new FormatException("공지 RSS 형식 변경");
            var result = new List<NotificationData>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            int candidates = 0;
            foreach (var entry in channel.Elements("item"))
            {
                candidates++;
                string link = WebUtility.HtmlDecode((string)entry.Element("link") ?? "").Trim();
                if (!Uri.TryCreate(new Uri(board.url), link, out var url) || !Allowed(url.AbsoluteUri) ||
                    !Regex.IsMatch(url.AbsolutePath, @"^/bbs/[^/]+/\d+/\d+/artclView\.do$")) continue;
                var canonical = url.GetLeftPart(UriPartial.Path);
                string title = Plain((string)entry.Element("title"), 250).TrimEnd('}').Trim();
                if (title.Length == 0 || !TryPublished((string)entry.Element("pubDate"), out var date) || !seen.Add(canonical)) continue;
                var item = new NotificationData { id = canonical, source_id = board.url, source_name = board.name,
                    source_type = "university", title = title, message = UniversityNoticeDeadline.Text((string)entry.Element("description")),
                    url = canonical, published_at = date.ToString("o"), icon_type = "notice", priority = "normal" };
                UniversityNoticeDeadline.Analyze(item);
                result.Add(item);
                if (result.Count == 50) break;
            }
            if (candidates > 0 && result.Count == 0) throw new FormatException("공지 항목을 확인하지 못했습니다.");
            return result;
        }
        public static bool TryPublished(string value, out DateTimeOffset date)
        {
            value = (value ?? "").Trim();
            // Hallym RSS dates omit the zone and use Korea local time, including fractional seconds.
            if (DateTime.TryParseExact(value, new[] { "yyyy-MM-dd HH:mm:ss.FFFFFFF", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd" },
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
            { date = new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), TimeSpan.FromHours(9)); return true; }
            return DateTimeOffset.TryParse(value.Replace(" GMT", " +00:00"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out date);
        }
        private void Rebuild()
        {
            items.Clear();
            var unique = new HashSet<string>();
            foreach (var board in boards) if (feeds.TryGetValue(board.url, out var saved))
                foreach (var item in saved.items)
                    if (item != null && Allowed(item.url) && TryPublished(item.published_at, out _) && unique.Add(item.id))
                    { item.is_read = PlayerPrefs.GetInt("university-notice.read." + item.id, 0) == 1; items.Add(item); }
            items.Sort((a, b) => DateTimeOffset.Parse(b.published_at, CultureInfo.InvariantCulture).CompareTo(DateTimeOffset.Parse(a.published_at, CultureInfo.InvariantCulture)));
        }
        public static string DiscoverRss(string html, string pageUrl)
        {
            if (!Allowed(pageUrl)) return null;
            foreach (Match match in Regex.Matches(html ?? "", @"href\s*=\s*[""'](?<url>[^""']*/bbs/[^""']+/\d+/rssList\.do[^""']*)[""']", RegexOptions.IgnoreCase))
                if (Uri.TryCreate(new Uri(pageUrl), WebUtility.HtmlDecode(match.Groups["url"].Value), out var url) && Allowed(url.AbsoluteUri)) return url.AbsoluteUri;
            return null;
        }
        private IEnumerator AddBoard()
        {
            discovering = true;
            addStatus = "공개 게시판 확인 중…";
            var pageUrl = addUrl.Trim();
            string feedUrl = pageUrl.Contains("/rssList.do") ? pageUrl : null;
            if (feedUrl == null) using (var request = UnityWebRequest.Get(pageUrl))
            {
                request.timeout = 12; request.redirectLimit = 0;
                yield return request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.Success) feedUrl = DiscoverRss(request.downloadHandler.text, pageUrl);
            }
            string error = null;
            if (feedUrl == null) error = "공개 RSS를 찾지 못했습니다. 게시판의 RSS 주소를 입력해 주세요.";
            else if (boards.Exists(b => FeedKey(b.url) == FeedKey(feedUrl))) error = "이미 등록된 게시판입니다.";
            else using (var request = UnityWebRequest.Get(feedUrl))
            {
                request.timeout = 12; request.redirectLimit = 0;
                yield return request.SendWebRequest();
                try
                {
                    if (request.result != UnityWebRequest.Result.Success) throw new IOException();
                    var board = new Board { name = addName.Trim(), url = feedUrl };
                    var parsed = ParseRss(request.downloadHandler.text, board);
                    boards.Add(board);
                    feeds[feedUrl] = new SavedFeed { url = feedUrl, updatedAt = DateTimeOffset.UtcNow.ToString("o"), items = parsed.ToArray() };
                    PlayerPrefs.SetString(BoardsKey, JsonUtility.ToJson(new BoardList { boards = boards.ToArray() })); PlayerPrefs.Save();
                    Rebuild(); adding = false; addName = addUrl = ""; nextRefresh = 0;
                }
                catch (Exception) { error = "RSS 조회에 실패했습니다. 로그인 없이 볼 수 있는 게시판인지 확인해 주세요."; }
            }
            addStatus = error ?? "게시판을 추가했습니다.";
            discovering = false;
        }
        public void Draw(GUIStyle label)
        {
            float scale = CampusScreenLayout.Scale;
            if (button == null) button = new GUIStyle(GUI.skin.button) { wordWrap = true, richText = false, alignment = TextAnchor.MiddleLeft };
            if (filterButton == null) filterButton = new GUIStyle(GUI.skin.button) { wordWrap = true, richText = false };
            button.fontSize = Mathf.RoundToInt(12 * scale);
            filterButton.fontSize = Mathf.RoundToInt(12 * scale);
            button.padding = new RectOffset(8, 8, 8, 8);
            GUILayout.Label("학교 공지 · 로그인 없이 확인", label);
            GUILayout.Label(Status, label);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(IsLoading ? "조회 중…" : "다시 확인", GUILayout.MinHeight(34 * scale))) Refresh();
            if (GUILayout.Button(adding ? "추가 닫기" : "게시판 추가", GUILayout.MinHeight(34 * scale))) adding = !adding;
            GUILayout.EndHorizontal();
            if (adding)
            {
                GUILayout.Label("학과·부서 게시판 이름과 HTTPS 주소를 입력하세요. 공개 RSS가 있는 한림대 게시판을 지원합니다.", label);
                addName = GUILayout.TextField(addName, 40, GUILayout.MinHeight(32 * scale));
                addUrl = GUILayout.TextField(addUrl, 500, GUILayout.MinHeight(32 * scale));
                bool enabled = GUI.enabled;
                GUI.enabled = enabled && !IsLoading && !discovering && !string.IsNullOrWhiteSpace(addName) && Allowed(addUrl.Trim());
                if (GUILayout.Button(discovering ? "확인 중…" : "게시판 등록", GUILayout.MinHeight(34 * scale))) StartCoroutine(AddBoard());
                GUI.enabled = enabled;
                GUILayout.Label(addStatus, label);
            }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(filter == "" ? "✓ 전체" : "전체", filterButton, GUILayout.MinHeight(32 * scale))) filter = "";
            if (GUILayout.Button(unreadOnly ? "✓ 안 읽음" : "안 읽음", filterButton, GUILayout.MinHeight(32 * scale))) unreadOnly = !unreadOnly;
            if (GUILayout.Button(days == 0 ? "수집 전체" : "최근 " + days + "일", filterButton, GUILayout.MinHeight(32 * scale))) days = days == 90 ? 30 : days == 30 ? 0 : 90;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(applicationsOnly ? "✓ 신청·제출 일정" : "신청·제출 일정", filterButton, GUILayout.MinHeight(32 * scale))) { applicationsOnly = !applicationsOnly; visibleLimit = 100; }
            if (GUILayout.Button(selectingSource ? "출처 닫기" : "출처 선택 (" + boards.Count + ")", filterButton, GUILayout.MinHeight(32 * scale))) selectingSource = !selectingSource;
            if (GUILayout.Button(hideCompleted ? "완료 숨김" : "완료 포함", filterButton, GUILayout.MinHeight(32 * scale))) hideCompleted = !hideCompleted;
            GUILayout.EndHorizontal();
            if (selectingSource)
            {
                GUILayout.Label("출처 이름 검색", label);
                sourceSearch = GUILayout.TextField(sourceSearch, 100, GUILayout.MinHeight(32 * scale));
                int shownSources = 0;
                foreach (var board in boards)
                {
                    if (sourceSearch.Length > 0 && board.name.IndexOf(sourceSearch, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (++shownSources > 30) continue;
                    if (GUILayout.Button((filter == board.url ? "✓ " : "") + board.name, filterButton, GUILayout.MinHeight(32 * scale))) { filter = board.url; selectingSource = false; visibleLimit = 100; }
                }
                if (shownSources > 30) GUILayout.Label("검색어로 출처를 좁혀 주세요. " + shownSources + "개 중 30개 표시", label);
            }
            if (filter != "") GUILayout.Label("출처: " + (boards.Find(b => b.url == filter)?.name ?? filter), label);
            GUILayout.Label("제목·내용 검색", label);
            search = GUILayout.TextField(search, 100, GUILayout.MinHeight(32 * scale));
            if (errors.Count > 0 && GUILayout.Button((showErrors ? "접기 · " : "조회 실패 출처 보기 · ") + errors.Count + "개", GUILayout.MinHeight(30 * scale))) showErrors = !showErrors;
            if (showErrors) foreach (var board in boards)
                if (errors.TryGetValue(board.url, out var error)) GUILayout.Label(board.name + " · " + error +
                    (feeds.TryGetValue(board.url, out var old) ? " · 마지막 성공 " + DateTimeOffset.Parse(old.updatedAt).ToOffset(TimeSpan.FromHours(9)).ToString("MM/dd HH:mm") : ""), label);
            int count = 0;
            var cutoff = NotificationManager.TodayKorea.AddDays(-days);
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var display = applicationsOnly ? new List<NotificationData>(items) : items;
            if (applicationsOnly) display.Sort((a, b) => CampusScreenLayout.CompareDeadlines(a.application_start_at > now ? a.application_start_at : a.deadline_at,
                b.application_start_at > now ? b.application_start_at : b.deadline_at));
            foreach (var item in display)
            {
                var date = DateTimeOffset.Parse(item.published_at, CultureInfo.InvariantCulture).ToOffset(TimeSpan.FromHours(9));
                if (filter != "" && item.source_id != filter || unreadOnly && item.is_read && selectedId != item.id || days > 0 && date.Date < cutoff) continue;
                if (!string.IsNullOrWhiteSpace(search) && (item.title + " " + item.message).IndexOf(search.Trim(), StringComparison.OrdinalIgnoreCase) < 0) continue;
                bool complete = PlayerPrefs.GetInt("university-notice.complete." + item.id, 0) == 1;
                if (applicationsOnly && !item.application_notice || hideCompleted && complete) continue;
                count++;
                if (count > visibleLimit) continue;
                var color = GUI.backgroundColor;
                if (item.deadline_at > 0 && item.application_start_at <= now && !complete)
                    GUI.backgroundColor = CampusScreenLayout.DeadlineColor(CampusScreenLayout.Stage(item.deadline_at, now));
                var schedule = UniversityNoticeDeadline.Summary(item, now);
                if (GUILayout.Button((complete ? "완료 · " : item.is_read ? "읽음 · " : "● ") + item.title + "\n" + item.source_name + " · " + date.ToString("yyyy-MM-dd") +
                    (schedule.Length > 0 ? "\n" + schedule : ""), button))
                {
                    selectedId = selectedId == item.id ? null : item.id;
                    item.is_read = true;
                    PlayerPrefs.SetInt("university-notice.read." + item.id, 1); PlayerPrefs.Save();
                    if (selectedId != null && item.detail_checked_at < DateTimeOffset.UtcNow.AddHours(-24).ToUnixTimeSeconds()) StartCoroutine(FetchDetail(item));
                }
                GUI.backgroundColor = color;
                if (selectedId != item.id) continue;
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label(item.message.Length > 0 ? item.message : "게시판에서 요약을 제공하지 않았습니다.", label);
                if (item.application_notice)
                {
                    GUILayout.Label(schedule, label);
                    if (!string.IsNullOrEmpty(item.deadline_evidence)) GUILayout.Label("기한 근거: " + item.deadline_evidence, label);
                    if (!string.IsNullOrEmpty(item.deadline_note)) GUILayout.Label(item.deadline_note, label);
                    if (GUILayout.Button(complete ? "완료 표시 취소" : "신청/제출 완료로 표시", GUILayout.MinHeight(34 * scale)))
                    { PlayerPrefs.SetInt("university-notice.complete." + item.id, complete ? 0 : 1); PlayerPrefs.Save(); }
                }
                GUILayout.Label(item.detail_checked_at > 0 ? "공개 원문에서 확인한 내용입니다. 이미지·첨부파일의 일정과 신청 가능 인원은 원문에서 확인해 주세요." :
                    "RSS 요약입니다. 신청 일정은 공개 원문에서 추가 확인합니다. 이미지·첨부파일에만 있는 날짜는 직접 확인해 주세요.", label);
                if (GUILayout.Button("학교 홈페이지에서 원문 보기", GUILayout.MinHeight(34 * scale))) Application.OpenURL(item.url);
                GUILayout.EndVertical();
            }
            if (count > visibleLimit && GUILayout.Button("100건 더 보기 (총 " + count + "건)", GUILayout.MinHeight(34 * scale))) visibleLimit += 100;
            GUILayout.Label(Math.Min(count, visibleLimit) + "/" + count + "건 표시 · 게시판별 최근 최대 50건 · 앱 실행 중 15분마다 갱신", label);
            GUILayout.Label("공식 사이트에서 확인된 공개 RSS 게시판을 모읍니다. 로그인 전용·외부 서비스·RSS 미제공 사이트는 자동 수집 목록에 포함되지 않을 수 있습니다.", label);
            if (count == 0) GUILayout.Label(IsLoading ? "학교에서 공지를 확인하고 있습니다." : errors.Count > 0 && items.Count == 0 ? "공지를 불러오지 못했습니다. 다시 확인해 주세요." : "현재 필터에 해당하는 공지가 없습니다.", label);
        }
    }
}

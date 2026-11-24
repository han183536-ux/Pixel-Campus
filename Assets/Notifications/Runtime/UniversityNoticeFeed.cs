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
        private const int PageSize = 24, BackgroundDetailLimit = 12;
        private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(15);
        private readonly HashSet<string> errors = new HashSet<string>();
        private readonly List<NotificationData> items = new List<NotificationData>();
        private readonly List<(NotificationData item, DateTimeOffset date, bool complete)> filtered =
            new List<(NotificationData item, DateTimeOffset date, bool complete)>();
        public IReadOnlyList<NotificationData> Items => items;
        public bool IsLoading { get; private set; }
        private string filter = "", search = "", selectedId, addUrl = "", addName = "", addStatus = "";
        private int days = 90;
        private bool filtersExpanded, unreadOnly, adding, discovering, selectingSource, applicationsOnly, hideCompleted = true;
        private string sourceSearch = "";
        private int visibleLimit = PageSize, drawnLimit;
        private int reloadIndex, workers;
        private bool rebuildPending, cacheDirty, filtersDirty = true;
        private DateTime filterDate;
        private long filterMinute;
        private readonly HashSet<string> detailLoading = new HashSet<string>();
        private readonly Dictionary<string, long> detailAttempts = new Dictionary<string, long>();
        private readonly NotificationRequestScope requests = new NotificationRequestScope();
        private Coroutine backgroundDetails;
        private float nextRefresh;
        private float nextRebuild;
        private float nextCacheSave;
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
            }
            catch (Exception ex) { Debug.LogWarning("Pixel Campus notice cache: " + ex.GetType().Name); }
            Rebuild();
            RequestRefresh(force: false);
        }
        private void Update()
        {
            if (rebuildPending && Time.unscaledTime >= nextRebuild) Rebuild();
            if (cacheDirty && !IsLoading && backgroundDetails == null && detailLoading.Count == 0 &&
                Time.unscaledTime >= nextCacheSave) SaveCache();
            if (!IsLoading && !discovering && Time.unscaledTime >= nextRefresh) RequestRefresh(force: false);
        }

        public void Refresh() => RequestRefresh(force: true);

        private void RequestRefresh(bool force)
        {
            if (IsLoading || discovering || !isActiveAndEnabled) return;
            var now = DateTimeOffset.UtcNow;
            var due = force ? boards.ToArray() : boards.FindAll(board =>
                !feeds.TryGetValue(board.url, out var saved) || !TryPublished(saved.updatedAt, out var updated) ||
                updated > now || now - updated >= RefreshInterval).ToArray();
            nextRefresh = Time.unscaledTime + (float)RefreshInterval.TotalSeconds;
            if (due.Length > 0) StartCoroutine(Reload(due));
            else StartBackgroundDetails();
        }

        private IEnumerator Reload(Board[] snapshot)
        {
            IsLoading = true;
            reloadIndex = 0;
            workers = Mathf.Min(4, snapshot.Length);
            int parallel = workers;
            for (int i = 0; i < parallel; i++) StartCoroutine(LoadBoards(snapshot));
            while (workers > 0) yield return null;
            if (rebuildPending) Rebuild();
            IsLoading = false;
            nextRefresh = Time.unscaledTime + (float)RefreshInterval.TotalSeconds;
            StartBackgroundDetails();
        }
        private IEnumerator LoadBoards(Board[] snapshot)
        {
            // A board fails independently; a partial failure never erases its last good results.
            while (reloadIndex < snapshot.Length)
            {
                var board = snapshot[reloadIndex++];
                using (var lease = requests.Track(UnityWebRequest.Get(board.url)))
                {
                    var request = lease.Request;
                    request.timeout = 12;
                    request.redirectLimit = 0;
                    yield return lease.Send();
                    if (lease.Cancelled) yield break;
                    try
                    {
                        if (request.result != UnityWebRequest.Result.Success) throw new IOException("게시판 연결 실패");
                        var parsed = ParseRss(request.downloadHandler.text, board);
                        if (feeds.TryGetValue(board.url, out var previous)) foreach (var item in parsed)
                        {
                            var old = Array.Find(previous.items, n => n != null && n.id == item.id);
                            if (old != null && old.detail_checked_at > 0) { item.message = old.message; item.detail_checked_at = old.detail_checked_at; UniversityNoticeDeadline.Analyze(item); }
                        }
                        feeds[board.url] = new SavedFeed { url = board.url, updatedAt = DateTimeOffset.UtcNow.ToString("o"), items = parsed.ToArray() };
                        errors.Remove(board.url);
                        rebuildPending = true;
                        MarkCacheDirty();
                    }
                    catch (Exception) { errors.Add(board.url); }
                }
            }
            workers--;
        }
        private void StartBackgroundDetails()
        {
            if (backgroundDetails == null && items.Count > 0) backgroundDetails = StartCoroutine(PreloadDetails());
        }

        private IEnumerator PreloadDetails()
        {
            // RSS is already usable. Enrich a small batch after the list has loaded, prioritizing application notices.
            yield return null;
            var now = DateTimeOffset.UtcNow;
            long stale = now.AddHours(-24).ToUnixTimeSeconds();
            var cutoff = now.AddDays(-90);
            var attempts = new List<string>(detailAttempts.Keys);
            foreach (var id in attempts)
                if (detailAttempts[id] < stale) detailAttempts.Remove(id);
            var pending = items.FindAll(item => item.detail_checked_at < stale &&
                !detailAttempts.ContainsKey(item.id) &&
                TryPublished(item.published_at, out var published) && published >= cutoff);
            // Rebuild keeps recent items first. Take from that order within each priority.
            int remaining = BackgroundDetailLimit;
            for (int priority = 0; priority < 2 && remaining > 0 && !IsLoading; priority++)
            {
                // Leave part of the batch for items whose RSS did not mention an application.
                int budget = priority == 0 ? 8 : remaining;
                foreach (var item in pending)
                {
                    if (budget == 0 || IsLoading) break;
                    if (item.application_notice != (priority == 0)) continue;
                    budget--;
                    remaining--;
                    yield return FetchDetail(item);
                }
            }
            backgroundDetails = null;
        }

        private IEnumerator FetchDetail(NotificationData item)
        {
            if (!Allowed(item.url) || !detailLoading.Add(item.id)) yield break;
            detailAttempts[item.id] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            try
            {
                using (var lease = requests.Track(UnityWebRequest.Get(item.url)))
                {
                    var request = lease.Request;
                    request.timeout = 12; request.redirectLimit = 0;
                    yield return lease.Send();
                    if (lease.Cancelled || request.result != UnityWebRequest.Result.Success) yield break;
                    var body = ExtractArticle(request.downloadHandler.text);
                    if (string.IsNullOrWhiteSpace(body)) yield break;
                    ApplyVerifiedBody(item, body);
                    if (feeds.TryGetValue(item.source_id, out var saved))
                    {
                        var current = Array.Find(saved.items, entry => entry != null && entry.id == item.id);
                        if (current != null && !ReferenceEquals(current, item)) ApplyVerifiedBody(current, body);
                    }
                    filtersDirty = true;
                    MarkCacheDirty();
                }
            }
            finally { detailLoading.Remove(item.id); }
        }

        private static void ApplyVerifiedBody(NotificationData item, string body)
        {
            item.message = body;
            item.detail_checked_at = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            UniversityNoticeDeadline.Analyze(item);
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
        private void MarkCacheDirty()
        {
            if (!cacheDirty) nextCacheSave = Time.unscaledTime + 2f;
            cacheDirty = true;
        }

        private void SaveCache()
        {
            cacheDirty = false;
            try { File.WriteAllText(CachePath, JsonUtility.ToJson(new Cache { feeds = new List<SavedFeed>(feeds.Values).ToArray() })); }
            catch (Exception ex) { Debug.LogWarning("Pixel Campus notice cache: " + ex.GetType().Name); }
        }
        private static string FeedKey(string url) => new Uri(url).AbsolutePath;
        private void OnDisable()
        {
            requests.CancelAll();
            StopAllCoroutines();
            detailLoading.Clear();
            backgroundDetails = null;
            IsLoading = discovering = false;
            nextRefresh = 0;
            if (cacheDirty) SaveCache();
        }
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
            var ordered = new List<(NotificationData item, DateTimeOffset date)>();
            foreach (var board in boards) if (feeds.TryGetValue(board.url, out var saved))
                foreach (var item in saved.items)
                    if (item != null && Allowed(item.url) && TryPublished(item.published_at, out var date) && unique.Add(item.id))
                    {
                        item.is_read = PlayerPrefs.GetInt("university-notice.read." + item.id, 0) == 1;
                        ordered.Add((item, date));
                    }
            ordered.Sort((a, b) => b.date.CompareTo(a.date));
            foreach (var row in ordered) items.Add(row.item);
            filtersDirty = true;
            rebuildPending = false;
            nextRebuild = Time.unscaledTime + 2;
        }

        private void UpdateVisibleList(long now)
        {
            if (Event.current.type != EventType.Layout) return;
            var today = NotificationManager.TodayKorea;
            long minute = now / 60;
            if (filtersDirty || filterDate != today || applicationsOnly && filterMinute != minute)
            {
                filtered.Clear();
                var cutoff = today.AddDays(-days);
                var query = search.Trim();
                var korea = TimeSpan.FromHours(9);
                foreach (var item in items)
                {
                    if (filter != "" && item.source_id != filter || unreadOnly && item.is_read && selectedId != item.id ||
                        applicationsOnly && !item.application_notice || !TryPublished(item.published_at, out var timestamp)) continue;
                    var date = timestamp.ToOffset(korea);
                    if (days > 0 && date.Date < cutoff) continue;
                    if (query.Length > 0 && (item.title + " " + item.message).IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    bool complete = PlayerPrefs.GetInt("university-notice.complete." + item.id, 0) == 1;
                    if (!hideCompleted || !complete) filtered.Add((item, date, complete));
                }
                if (applicationsOnly)
                    filtered.Sort((a, b) => CampusScreenLayout.CompareDeadlines(
                        a.item.application_start_at > now ? a.item.application_start_at : a.item.deadline_at,
                        b.item.application_start_at > now ? b.item.application_start_at : b.item.deadline_at));
                filtersDirty = false;
                filterDate = today;
                filterMinute = minute;
            }
            drawnLimit = Math.Min(visibleLimit, filtered.Count);
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
            if (feedUrl == null) using (var lease = requests.Track(UnityWebRequest.Get(pageUrl)))
            {
                var request = lease.Request;
                request.timeout = 12; request.redirectLimit = 0;
                yield return lease.Send();
                if (lease.Cancelled) yield break;
                if (request.result == UnityWebRequest.Result.Success) feedUrl = DiscoverRss(request.downloadHandler.text, pageUrl);
            }
            string error = null;
            if (feedUrl == null) error = "공개 RSS를 찾지 못했습니다. 게시판의 RSS 주소를 입력해 주세요.";
            else if (boards.Exists(b => FeedKey(b.url) == FeedKey(feedUrl))) error = "이미 등록된 게시판입니다.";
            else using (var lease = requests.Track(UnityWebRequest.Get(feedUrl)))
            {
                var request = lease.Request;
                request.timeout = 12; request.redirectLimit = 0;
                yield return lease.Send();
                if (lease.Cancelled) yield break;
                try
                {
                    if (request.result != UnityWebRequest.Result.Success) throw new IOException();
                    var board = new Board { name = addName.Trim(), url = feedUrl };
                    var parsed = ParseRss(request.downloadHandler.text, board);
                    boards.Add(board);
                    feeds[feedUrl] = new SavedFeed { url = feedUrl, updatedAt = DateTimeOffset.UtcNow.ToString("o"), items = parsed.ToArray() };
                    PlayerPrefs.SetString(BoardsKey, JsonUtility.ToJson(new BoardList { boards = boards.ToArray() })); PlayerPrefs.Save();
                    Rebuild();
                    MarkCacheDirty();
                    adding = false; addName = addUrl = "";
                }
                catch (Exception) { error = "RSS 조회에 실패했습니다. 로그인 없이 볼 수 있는 게시판인지 확인해 주세요."; }
            }
            addStatus = error ?? "게시판을 추가했습니다.";
            discovering = false;
        }
        private void DrawFilterControls(GUIStyle label, float scale)
        {
            GUILayout.BeginHorizontal();
            bool canRefresh = GUI.enabled;
            GUI.enabled = canRefresh && !IsLoading;
            if (CampusTheme.Action("다시 확인", CampusTheme.Primary)) Refresh();
            GUI.enabled = canRefresh;
            if (CampusTheme.Action(adding ? "추가 닫기" : "게시판 추가")) adding = !adding;
            GUILayout.EndHorizontal();
            if (adding)
            {
                GUILayout.Label("학과·부서 게시판 이름과 HTTPS 주소를 입력하세요. 공개 RSS가 있는 한림대 게시판을 지원합니다.", label);
                GUILayout.Label("게시판 이름", CampusTheme.Caption);
                addName = GUILayout.TextField(addName, 40, GUILayout.MinHeight(44 * scale));
                GUILayout.Label("HTTPS 주소", CampusTheme.Caption);
                addUrl = GUILayout.TextField(addUrl, 500, GUILayout.MinHeight(44 * scale));
                bool enabled = GUI.enabled;
                GUI.enabled = enabled && !IsLoading && !discovering && !string.IsNullOrWhiteSpace(addName) && Allowed(addUrl.Trim());
                if (CampusTheme.Action(discovering ? "확인 중…" : "게시판 등록", CampusTheme.Primary)) StartCoroutine(AddBoard());
                GUI.enabled = enabled;
                GUILayout.Label(addStatus, label);
            }
            GUILayout.BeginHorizontal();
            if (CampusTheme.Action(filter == "" ? "전체 출처" : "전체로", filter == "" ? CampusTheme.Secondary : GUI.skin.button)) filter = "";
            if (CampusTheme.Action(unreadOnly ? "안 읽음만" : "읽음 포함", unreadOnly ? CampusTheme.Secondary : GUI.skin.button)) unreadOnly = !unreadOnly;
            if (CampusTheme.Action(days == 0 ? "전체 기간" : "최근 " + days + "일", GUI.skin.button)) days = days == 90 ? 30 : days == 30 ? 0 : 90;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (CampusTheme.Action(applicationsOnly ? "신청 일정만" : "신청 일정", applicationsOnly ? CampusTheme.Secondary : GUI.skin.button))
                applicationsOnly = !applicationsOnly;
            if (CampusTheme.Action(selectingSource ? "출처 닫기" : "출처 선택", selectingSource ? CampusTheme.Secondary : GUI.skin.button))
                selectingSource = !selectingSource;
            if (CampusTheme.Action(hideCompleted ? "완료 숨김" : "완료 포함", hideCompleted ? CampusTheme.Secondary : GUI.skin.button)) hideCompleted = !hideCompleted;
            GUILayout.EndHorizontal();
            if (selectingSource)
            {
                GUILayout.Label("출처 이름 검색", label);
                sourceSearch = GUILayout.TextField(sourceSearch, 100, GUILayout.MinHeight(44 * scale));
                int shownSources = 0;
                foreach (var board in boards)
                {
                    if (sourceSearch.Length > 0 && board.name.IndexOf(sourceSearch, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (++shownSources > 30) break;
                    if (CampusTheme.Action(board.name, filter == board.url ? CampusTheme.Secondary : GUI.skin.button))
                    { filter = board.url; selectingSource = false; }
                }
                if (shownSources > 30) GUILayout.Label("찾는 출처의 이름을 입력해 주세요.", CampusTheme.Caption);
            }
            if (filter != "") GUILayout.Label("출처: " + (boards.Find(b => b.url == filter)?.name ?? filter), CampusTheme.Caption);
        }

        public void Draw(GUIStyle label, float width)
        {
            float scale = CampusScreenLayout.Scale;
            var previousFilters = (filter, search, unreadOnly, days, applicationsOnly, hideCompleted);
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();
            search = CampusTheme.SearchField(search, "제목·내용 검색", 100);
            GUILayout.Space(8 * scale);
            if (CampusTheme.FilterButton(filtersExpanded)) filtersExpanded = !filtersExpanded;
            GUILayout.EndHorizontal();
            if (filtersExpanded)
            {
                GUILayout.Space(6 * scale);
                DrawFilterControls(label, scale);
            }
            GUILayout.EndVertical();
            if (previousFilters != (filter, search, unreadOnly, days, applicationsOnly, hideCompleted))
            {
                visibleLimit = PageSize;
                filtersDirty = true;
            }
            CampusTheme.Section("공지 목록");
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            UpdateVisibleList(now);
            for (int index = 0; index < drawnLimit; index++)
            {
                var (item, date, complete) = filtered[index];
                var deadline = item.deadline_at > 0 && item.application_start_at <= now && !complete
                    ? CampusScreenLayout.DeadlineColor(CampusScreenLayout.Stage(item.deadline_at, now)) : Color.white;
                var schedule = UniversityNoticeDeadline.Summary(item, now);
                var marker = deadline != Color.white ? deadline : !item.is_read ? CampusTheme.Accent : Color.white;
                var caption = (complete ? "완료 · " : item.is_read ? "" : "안 읽음 · ") + item.source_name + " · " + date.ToString("yyyy-MM-dd");
                if (schedule.Length > 0) caption += "\n" + schedule;
                if (CampusTheme.ListCard(item.title, caption, width, marker, selectedId == item.id))
                {
                    selectedId = selectedId == item.id ? null : item.id;
                    item.is_read = true;
                    filtersDirty = true;
                    PlayerPrefs.SetInt("university-notice.read." + item.id, 1); PlayerPrefs.Save();
                    if (selectedId != null && item.detail_checked_at < DateTimeOffset.UtcNow.AddHours(-24).ToUnixTimeSeconds()) StartCoroutine(FetchDetail(item));
                }
                if (selectedId != item.id) continue;
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label(item.title, CampusTheme.SectionTitle);
                GUILayout.Label(!string.IsNullOrEmpty(item.message) ? item.message : "학교 홈페이지에서 공지 내용을 확인할 수 있습니다.", label);
                if (item.application_notice)
                {
                    if (CampusTheme.Action(complete ? "완료 표시 취소" : "신청/제출 완료로 표시"))
                    {
                        PlayerPrefs.SetInt("university-notice.complete." + item.id, complete ? 0 : 1);
                        PlayerPrefs.Save();
                        filtersDirty = true;
                    }
                }
                if (CampusTheme.Action("학교 홈페이지에서 원문 보기", CampusTheme.Primary)) Application.OpenURL(item.url);
                GUILayout.EndVertical();
                GUILayout.Space(8 * scale);
            }
            if (filtered.Count > drawnLimit && CampusTheme.Action("더 보기")) visibleLimit += PageSize;
            if (drawnLimit == 0)
            {
                bool sourceFailed = filter != "" ? errors.Contains(filter) && !feeds.ContainsKey(filter) :
                    items.Count == 0 && errors.Count > 0 && feeds.Count == 0;
                CampusTheme.StatusCard(IsLoading && items.Count == 0 ? "공지를 불러오는 중…" : sourceFailed ?
                    "공지를 가져오지 못했습니다. 다시 확인해 주세요." : "표시할 공지가 없습니다.");
            }
        }
    }
}

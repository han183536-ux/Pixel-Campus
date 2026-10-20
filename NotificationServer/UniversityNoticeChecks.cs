using System;
using System.IO;
using System.Linq;
using WebNotifications;

// Run with test-university-notices.ps1 after Unity has generated Assembly-CSharp.csproj.
internal static class UniversityNoticeChecks
{
    private static int checks;
    private static readonly UniversityNoticeFeed.Board Board = new UniversityNoticeFeed.Board {
        name = "학사", url = "https://www.hallym.ac.kr/bbs/hallym/157/rssList.do?row=50" };
    private static void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; }
    private static string Item(string id = "1", string date = "2026-10-20 00:00:00.0", string link = null) =>
        "<item><title><![CDATA[학사 &amp; 안내}]]></title><link>" + (link ?? "/bbs/hallym/157/" + id + "/artclView.do?layout=unknown") +
        "</link><pubDate>" + date + "</pubDate><description><![CDATA[<script>bad()</script><p>본문 &amp; 내용</p>]]></description></item>";
    private static string Feed(string body) => "<rss version='2.0'><channel>" + body + "</channel></rss>";
    private static void Reject(string value)
    {
        bool failed = false;
        try { UniversityNoticeFeed.ParseRss(value, Board); } catch { failed = true; }
        Check(failed, "Malformed feed must not replace last good data");
    }
    private static void Main(string[] args)
    {
        var parsed = UniversityNoticeFeed.ParseRss("\n\n" + Feed(Item()), Board);
        Check(parsed.Count == 1, "Parse RSS with leading whitespace");
        Check(parsed[0].title == "학사 & 안내", "Decode title and remove Hallym RSS suffix");
        Check(parsed[0].message == "본문 & 내용", "Render plain text without script");
        Check(parsed[0].url == "https://www.hallym.ac.kr/bbs/hallym/157/1/artclView.do", "Canonical original link");
        Check(parsed[0].source_name == "학사", "Keep board attribution");
        Check(DateTimeOffset.Parse(parsed[0].published_at).Offset == TimeSpan.FromHours(9), "Hallym RSS dates are Korea local");
        Check(UniversityNoticeFeed.TryPublished("2026-10-20T03:00:00+09:00", out var iso) && iso.Hour == 3, "ISO cached dates");
        Check(UniversityNoticeFeed.TryPublished("Tue, 20 Oct 2026 00:00:00 GMT", out var utc) && utc.Offset == TimeSpan.Zero, "RFC RSS dates");
        Check(!UniversityNoticeFeed.TryPublished("invalid", out _), "Unknown date is not today");
        Check(UniversityNoticeFeed.ParseRss(Feed(Item() + Item()), Board).Count == 1, "Duplicate article suppressed");
        Check(UniversityNoticeFeed.ParseRss(Feed(Item() + Item("2", "bad")), Board).Count == 1, "Bad item does not hide good item");
        Check(UniversityNoticeFeed.ParseRss(Feed(""), Board).Count == 0, "Confirmed empty RSS");
        Check(UniversityNoticeFeed.ParseRss(Feed(string.Concat(Enumerable.Range(1, 51).Select(i => Item(i.ToString())))), Board).Count == 50, "Bounded board size");
        foreach (var url in new[] { "http://www.hallym.ac.kr/", "https://hallym.ac.kr.evil.example/", "https://www.hallym.ac.kr:8443/", "https://user@www.hallym.ac.kr/" })
            Check(!UniversityNoticeFeed.Allowed(url), "Reject unrelated or credential-bearing URL");
        Check(UniversityNoticeFeed.Allowed("https://sw.hallym.ac.kr/"), "Department subdomains supported");
        Reject("<html>로그인</html>"); Reject("<rss><channel>");
        Reject("<!DOCTYPE rss [<!ENTITY x SYSTEM 'file:///secret'>]><rss><channel><item>&x;</item></channel></rss>");
        Reject(Feed(Item(link: "https://evil.example/bbs/hallym/157/1/artclView.do")));
        Reject(Feed(Item(link: "javascript:alert(1)")));
        Reject(Feed(Item(date: "invalid")));
        var html = "<a href='/bbs/sw/200/rssList.do?row=50&amp;x=1'>RSS</a>";
        Check(UniversityNoticeFeed.DiscoverRss(html, "https://sw.hallym.ac.kr/sw/1/subview.do") ==
            "https://sw.hallym.ac.kr/bbs/sw/200/rssList.do?row=50&x=1", "Discover department RSS with relative URL");
        Check(UniversityNoticeFeed.DiscoverRss("<a href='https://evil.example/bbs/sw/200/rssList.do'>RSS</a>", Board.url) == null,
            "Discovery stays on university domains");
        long At(string value) => DateTimeOffset.Parse(value + "+09:00").ToUnixTimeSeconds();
        NotificationData Notice(string body, string title = "특강 참가자 모집", string published = "2026-10-20")
        {
            var item = new NotificationData {title = title, message = body, published_at = published};
            UniversityNoticeDeadline.Analyze(item); return item;
        }
        var range = Notice("신청기간: 2026.10.21.(수) 09:00 ~ 10.24.(토) 18:00\n교육기간: 2026.11.1 ~ 11.4");
        Check(range.application_start_at == At("2026-10-21T09:00:00"), "Application start from range");
        Check(range.deadline_at == At("2026-10-24T18:00:00"), "Deadline is application end, not event date");
        Check(!range.deadline_date_only, "Precise time preserved");
        var spaced = Notice("신청 기간 : 2026. 10. 6.( 화 ) ~ 10. 22.( 목 ) 18:00 까지");
        Check(spaced.deadline_at == At("2026-10-22T18:00:00"), "Spaced weekday does not hide precise closing time");
        var unrelated = Notice("제출 기한 : 2026. 10. 12.(월) 오후 6시까지\n- 제출처: 교학팀\n- 2차 면접: 2026. 10. 23.(금) 오후 4시");
        Check(unrelated.deadline_at == At("2026-10-12T18:00:00"), "Bullets and interview dates never form an application range");
        var simple = Notice("접수 마감: 10월28일(수) 23:59까지");
        Check(simple.deadline_at == At("2026-10-28T23:59:00"), "Real SW notice format");
        var dateOnly = Notice("모집기간: ~2026.10.23.(금)\n활동기간: 2026.11.2 ~ 2027.1.29");
        Check(dateOnly.deadline_at == At("2026-10-23T23:59:59") && dateOnly.deadline_date_only, "Date-only is explicitly approximate");
        var firstCome = Notice("신청기간: 2026년 10월 21일 오전 9시부터 선착순 마감\n교육일시: 2026.11.1 10:00");
        Check(firstCome.application_start_at == At("2026-10-21T09:00:00"), "First-come opening time");
        Check(firstCome.deadline_at == 0 && firstCome.application_first_come, "No invented first-come closing time");
        Check(UniversityNoticeDeadline.Summary(firstCome, At("2026-10-20T12:00:00")).Contains("신청 시작까지"), "Opening countdown before first come");
        Check(UniversityNoticeDeadline.Summary(firstCome, At("2026-10-21T10:00:00")).Contains("신청 가능 · 선착순 마감"), "First-come status after opening");
        var both = Notice("접수기간: 2026.10.21 09:00 ~ 2026.10.23 18:00 (선착순 마감)");
        Check(both.application_first_come && both.application_start_at > 0 && both.deadline_at == At("2026-10-23T18:00:00"), "First-come can also have a closing date");
        Check(UniversityNoticeDeadline.Summary(both, At("2026-10-24T10:00:00")).Contains("신청 마감"), "First-come expired status");
        var rollover = Notice("신청기간: 12.28 ~ 1.5까지", published: "2026-12-20");
        Check(rollover.application_start_at == At("2026-12-28T00:00:00") && rollover.deadline_at == At("2027-01-05T23:59:59"), "Year rollover within application range");
        Check(Notice("신청기한: 1.5까지", published: "2026-12-20").deadline_at == At("2027-01-05T23:59:59"), "Missing-year deadline near New Year");
        Check(Notice("신청기한: 7.1까지", published: "2026-10-20").deadline_at == At("2026-07-01T23:59:59"), "Old dates are not silently moved into next year");
        Check(Notice("교육일시: 2026.10.23 18:00\n행사기간: 2026.10.24 ~ 10.25").deadline_at == 0, "Never use lecture/event dates as application deadline");
        Check(Notice("선착순 30명 접수, 세부사항은 포스터 참고").application_start_at == 0, "Missing opening remains unknown");
        Check(Notice("신청기간: 상시 접수").deadline_at == 0, "Rolling application has no invented deadline");
        Check(Notice("접수 마감: 2026.02.30 18:00").deadline_at == 0, "Invalid dates rejected");
        Check(Notice("신청기한: 2026.10.23 18:00\n제출기한: 2026.10.25 18:00").deadline_at == 0, "Different application/submission deadlines require checking original");
        var refined = Notice("접수마감: 2026.10.23 18:00", "특강 신청 마감: 10.23까지");
        Check(refined.deadline_at == At("2026-10-23T18:00:00"), "Body refines date-only title");
        Check(Notice("등록된 행사 사진입니다. 2026.10.23", "행사 소식").deadline_at == 0, "Dates without deadline label ignored");
        Check(UniversityNoticeFeed.ExtractArticle("<div class='board-view'><div class='txt'><p>신청기간</p><div>본문</div></div><div>다른 글</div></div>") == "신청기간\n본문", "Only actual article body is used");
        Check(UniversityNoticeFeed.ExtractArticle("<html>로그인</html>") == null, "Unknown or login pages do not replace article");
        if (File.Exists("Temp/hlsw-article.html"))
        {
            var liveBody = UniversityNoticeFeed.ExtractArticle(File.ReadAllText("Temp/hlsw-article.html"));
            Check(!string.IsNullOrWhiteSpace(liveBody), "Live SW original article body found");
            Check(Notice(liveBody).deadline_at == At("2026-10-28T23:59:00"), "Live SW application deadline extracted");
        }
        if (args.Length > 0)
            foreach (var file in new[] { "hallym-academic.xml", "hallym-156.xml", "hallym-155.xml", "hallym-1413.xml", "hallym-151.xml" })
            {
                var live = UniversityNoticeFeed.ParseRss(File.ReadAllText(Path.Combine(args[0], file)), Board);
                Check(live.Count > 0, "Live feed: " + file);
                Console.WriteLine(file + ": " + live.Count + " articles parsed");
            }
        Console.WriteLine(checks + " university notice checks passed.");
    }
}

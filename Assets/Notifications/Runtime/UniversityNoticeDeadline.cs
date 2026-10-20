using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;

namespace WebNotifications
{
    // Only application/submission fields are dates, never the event/publication date.
    public static class UniversityNoticeDeadline
    {
        private const string Action = @"신청|접수|제출|응모|수강|참가|모집|공모|지원서|등록";
        private static readonly Regex Field = new Regex(@"(?<label>(?:(?:신청|접수|제출|응모|등록|지원서?|모집|참가)\s*(?:서\s*)?(?:시작일?|개시일?|종료일?|기간|기한|마감일?|일시|일정)|마감(?:일|시간|기한)?|(?:신청|접수|제출|모집)\s*(?=[:：~∼～(\[]|\d{1,2}[/.월]|20\d{2}[.년])))\s*[:：]?", RegexOptions.Compiled);
        private static readonly Regex Date = new Regex(@"(?<!\d)(?:(?<y>20\d{2}|\d{2})\s*[.년/\-]\s*)?(?<m>\d{1,2})\s*[.월/\-]\s*(?<d>\d{1,2})(?!\d)\s*(?:일|\.)?(?:\s*\(\s*[월화수목금토일]\s*(?:요일)?\s*\))?\s*(?:(?<ap>오전|오후)\s*)?(?:(?<h>\d{1,2})\s*[:시]\s*(?<min>\d{1,2})?\s*분?)?", RegexOptions.Compiled);
        public static string Text(string html)
        {
            var value = Regex.Replace(html ?? "", @"<(script|style)\b[^>]*>.*?</\1>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            value = Regex.Replace(value, @"</(?:p|div|tr|li|h[1-6])\s*>|<br\s*/?>", "\n", RegexOptions.IgnoreCase);
            value = WebUtility.HtmlDecode(Regex.Replace(value, "<[^>]*>", " "));
            return Regex.Replace(Regex.Replace(value, @"[^\S\n]+", " "), @" *\n *", "\n").Trim();
        }
        public static bool IsApplication(string title, string body) => Regex.IsMatch((title ?? "") + " " + (body ?? ""), Action);
        public static void Analyze(NotificationData item)
        {
            item.application_start_at = item.deadline_at = 0;
            item.deadline_evidence = item.deadline_note = "";
            item.deadline_date_only = false;
            item.application_start_date_only = false;
            string text = (item.title ?? "") + "\n" + Text(item.message);
            item.application_notice = IsApplication(item.title, item.message);
            item.application_first_come = item.application_notice && Regex.IsMatch(text, "선착순|정원\\s*(?:충원|마감)|인원\\s*충원");
            if (!item.application_notice) return;
            if (!UniversityNoticeFeed.TryPublished(item.published_at, out var published)) return;
            var starts = new HashSet<long>();
            var preciseStarts = new HashSet<long>();
            var ends = new Dictionary<long, Tuple<bool, string>>();
            foreach (Match field in Field.Matches(text))
            {
                int begin = field.Index + field.Length;
                int length = Math.Min(200, text.Length - begin);
                string segment = text.Substring(begin, length);
                // Stop before the next field, paragraph or unrelated schedule.
                var stop = Regex.Match(segment, @"\n\s*\n|(?:활동|행사|교육|운영|강의|면접|발표|사업|입주|연수)\s*(?:기간|일시|일정|일자|[:：])|(?:신청|접수|제출|모집|등록)\s*(?:방법|대상|장소|자격|처)|(?:장소|대상)\s*[:：]");
                if (stop.Success) segment = segment.Substring(0, stop.Index);
                var next = Field.Match(segment);
                if (next.Success) segment = segment.Substring(0, next.Index);
                var dates = Date.Matches(segment).Cast<Match>().ToList();
                if (dates.Count == 0) continue;
                // A single field with several rounds is ambiguous; do not invent one deadline.
                if (dates.Count > 2) { item.deadline_note = "여러 신청 일정 · 원문 확인"; continue; }
                int year = published.Year;
                if (!ReadDate(dates[0], year, out var first, out bool firstTime)) continue;
                // Missing years are anchored to the publication, including New Year rollover.
                if (!dates[0].Groups["y"].Success && published.Month >= 10 && first.Month <= 3 && first < published) first = first.AddYears(1);
                bool startField = Regex.IsMatch(field.Value, "시작|개시");
                bool endField = Regex.IsMatch(field.Value, "마감|종료|기한");
                bool range = dates.Count == 2 && Regex.IsMatch(segment.Substring(dates[0].Index + dates[0].Length,
                    Math.Max(0, dates[1].Index - dates[0].Index - dates[0].Length)), @"^\s*(?:[~∼～–—-]|부터)\s*$");
                if (range)
                {
                    if (!ReadDate(dates[1], first.Year, out var last, out bool lastTime)) continue;
                    if (!dates[1].Groups["y"].Success && last < first && first.Month >= 10 && last.Month <= 3) last = last.AddYears(1);
                    if (last < first) { item.deadline_note = "신청 기간 순서 확인 필요 · 원문 확인"; continue; }
                    starts.Add(first.ToUnixTimeSeconds());
                    if (firstTime) preciseStarts.Add(first.ToUnixTimeSeconds());
                    AddEnd(ends, last, lastTime, field.Value + " " + segment.Trim());
                }
                else if (startField || (!endField && Regex.IsMatch(segment, "부터|선착순|상시|수시")))
                { starts.Add(first.ToUnixTimeSeconds()); if (firstTime) preciseStarts.Add(first.ToUnixTimeSeconds()); }
                else if (endField || dates.Count == 1 && Regex.IsMatch(segment, "까지|[~∼～]"))
                    AddEnd(ends, first, firstTime, field.Value + " " + segment.Trim());
                else item.deadline_note = "신청 날짜 용도 확인 필요 · 원문 확인";
            }
            foreach (var precise in preciseStarts) starts.RemoveWhere(value => !preciseStarts.Contains(value) &&
                DateTimeOffset.FromUnixTimeSeconds(value).ToOffset(TimeSpan.FromHours(9)).Date == DateTimeOffset.FromUnixTimeSeconds(precise).ToOffset(TimeSpan.FromHours(9)).Date);
            if (starts.Count == 1) { item.application_start_at = starts.First(); item.application_start_date_only = !preciseStarts.Contains(item.application_start_at); }
            if (ends.Count == 1)
            {
                var end = ends.First(); item.deadline_at = end.Key;
                item.deadline_date_only = end.Value.Item1; item.deadline_evidence = end.Value.Item2;
                item.deadline_note = item.deadline_date_only ? "종료 시각 미기재 · 해당 날짜 말일까지로 표시" : "";
            }
            else if (ends.Count > 1) item.deadline_note = "여러 마감일 · 원문 확인";
            if (starts.Count > 1) { item.application_start_at = 0; item.deadline_note = "여러 신청 시작일 · 원문 확인"; }
            if (item.application_first_come && item.deadline_at == 0) item.deadline_note = "선착순 마감 · 잔여 인원은 원문 확인";
            if (item.deadline_at == 0 && item.application_start_at == 0 && string.IsNullOrEmpty(item.deadline_note))
                item.deadline_note = "신청 일정 확인 필요 · 원문/첨부파일 확인";
        }
        private static void AddEnd(Dictionary<long, Tuple<bool, string>> ends, DateTimeOffset value, bool timed, string evidence)
        {
            if (!timed) value = value.Date.AddDays(1).AddSeconds(-1).SpecifyKorea();
            // A precise body time refines a date-only title for the same day.
            var sameDay = ends.Keys.Where(key => DateTimeOffset.FromUnixTimeSeconds(key).ToOffset(TimeSpan.FromHours(9)).Date == value.Date).ToList();
            if (!timed && sameDay.Exists(key => !ends[key].Item1)) return;
            if (timed) foreach (var key in sameDay) if (ends[key].Item1) ends.Remove(key);
            ends[value.ToUnixTimeSeconds()] = Tuple.Create(!timed, evidence.Length > 220 ? evidence.Substring(0, 220) : evidence);
        }
        private static DateTimeOffset SpecifyKorea(this DateTime value) => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Unspecified), TimeSpan.FromHours(9));
        private static bool ReadDate(Match date, int year, out DateTimeOffset value, out bool timed)
        {
            value = default; timed = date.Groups["h"].Success;
            try
            {
                if (date.Groups["y"].Success) { year = int.Parse(date.Groups["y"].Value); if (year < 100) year += 2000; }
                int hour = timed ? int.Parse(date.Groups["h"].Value) : 0;
                if (date.Groups["ap"].Success) hour = hour % 12 + (date.Groups["ap"].Value == "오후" ? 12 : 0);
                int minute = date.Groups["min"].Success ? int.Parse(date.Groups["min"].Value) : 0;
                value = new DateTimeOffset(year, int.Parse(date.Groups["m"].Value), int.Parse(date.Groups["d"].Value), hour, minute, 0, TimeSpan.FromHours(9));
                return true;
            }
            catch (ArgumentOutOfRangeException) { return false; }
        }
        public static string Summary(NotificationData item, long now)
        {
            if (!item.application_notice) return "";
            string start = item.application_start_at > 0 ? "신청 시작 " + DateTimeOffset.FromUnixTimeSeconds(item.application_start_at).ToOffset(TimeSpan.FromHours(9)).ToString(item.application_start_date_only ? "MM/dd (시각 미기재)" : "MM/dd HH:mm") : "";
            if (item.application_start_at > now) return start + " · 신청 시작까지 " + Remaining(item.application_start_at - now);
            string end = item.deadline_at > 0 ? (item.deadline_at <= now ? "신청 마감" : "마감까지 " + Remaining(item.deadline_at - now)) +
                " · " + DateTimeOffset.FromUnixTimeSeconds(item.deadline_at).ToOffset(TimeSpan.FromHours(9)).ToString(item.deadline_date_only ? "MM/dd (시각 미기재)" : "MM/dd HH:mm") : "";
            if (item.application_first_come) return (start.Length > 0 ? start + " · " : "신청 시작일 확인 필요 · ") +
                (item.deadline_at > 0 && item.deadline_at <= now ? "신청 마감" : item.application_start_at > 0 ? "신청 가능 · 선착순 마감" : "선착순 마감") + (end.Length > 0 ? "\n" + end : "");
            return (start.Length > 0 ? start + " · " : "") + (end.Length > 0 ? end : item.deadline_note);
        }
        private static string Remaining(long seconds)
        {
            var time = TimeSpan.FromSeconds(seconds);
            return time.TotalDays >= 1 ? (int)time.TotalDays + "일 " + time.Hours + "시간" :
                time.TotalHours >= 1 ? (int)time.TotalHours + "시간 " + time.Minutes + "분" : Math.Max(1, (int)Math.Ceiling(time.TotalMinutes)) + "분";
        }
    }
}

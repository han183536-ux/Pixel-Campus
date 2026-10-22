using System;

namespace WebNotifications
{
    public static class LectureAttendancePeriod
    {
        public static string Status(long startAt, long dueAt, long lateDueAt, bool periodKnown, bool attendanceKnown, bool attended, long now)
        {
            if (attendanceKnown && attended) return "학교 출석 확인됨";
            if (!periodKnown || startAt <= 0 || dueAt <= startAt || dueAt > 253402300799L) return "출석 인정 기간 확인 필요";
            if (now < startAt) return "출석 인정 시작까지 " + Remaining(startAt - now);
            if (now < dueAt) return "출석 인정 종료까지 " + Remaining(dueAt - now);
            if (lateDueAt > dueAt && now < lateDueAt) return "출석 기간 종료 · 지각 인정 종료까지 " + Remaining(lateDueAt - now);
            return "출석 인정 기간 종료";
        }

        private static string Remaining(long seconds)
        {
            var time = TimeSpan.FromSeconds(seconds);
            if (time.TotalDays >= 1) return (int)time.TotalDays + "일 " + time.Hours + "시간";
            if (time.TotalHours >= 1) return (int)time.TotalHours + "시간 " + time.Minutes + "분";
            return Math.Max(1, (int)Math.Ceiling(time.TotalMinutes)) + "분";
        }
    }
}

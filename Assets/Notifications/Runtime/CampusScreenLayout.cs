using System;
using UnityEngine;

namespace WebNotifications
{
    public static class CampusScreenLayout
    {
        public static int CompareDeadlines(long left, long right)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            int leftRank = left > now ? 0 : left > 0 ? 1 : 2;
            int rightRank = right > now ? 0 : right > 0 ? 1 : 2;
            int rank = leftRank.CompareTo(rightRank);
            if (rank != 0) return rank;
            return leftRank == 1 ? right.CompareTo(left) : left.CompareTo(right);
        }
        public static string RemainingTime(long dueAt)
        {
            if (dueAt <= 0 || dueAt > 253402300799L) return "마감 정보 없음";
            var seconds = dueAt - DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var time = TimeSpan.FromSeconds(Math.Abs((double)seconds));
            var value = time.TotalDays >= 1 ? (int)time.TotalDays + "일 " + time.Hours + "시간" :
                time.TotalHours >= 1 ? (int)time.TotalHours + "시간 " + time.Minutes + "분" :
                Math.Max(1, (int)Math.Ceiling(time.TotalMinutes)) + "분";
            return seconds > 0 ? value + " 남음" : "마감 " + value + " 지남";
        }
        public static float Scale => Mathf.Clamp(Screen.width / 390f, .8f, 3f);
        public static float HeaderHeight => 58f * Scale;
        public static Rect SafeArea
        {
            get
            {
                var area = Screen.safeArea;
                if (area.width <= 0 || area.height <= 0) area = new Rect(0, 0, Screen.width, Screen.height);
                return new Rect(area.x, Screen.height - area.yMax, area.width, area.height);
            }
        }
        public static Rect ContentArea
        {
            get
            {
                var area = SafeArea;
                return new Rect(area.x, area.y + HeaderHeight, area.width, Mathf.Max(1, area.height - HeaderHeight));
            }
        }
        public static Rect CameraRect
        {
            get
            {
                var area = ContentArea;
                return new Rect(area.x / Screen.width, (Screen.height - area.yMax) / Screen.height,
                    area.width / Screen.width, area.height / Screen.height);
            }
        }
    }
}

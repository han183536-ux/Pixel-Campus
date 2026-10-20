using System;
using System.Collections.Generic;
using UnityEngine;

namespace WebNotifications
{
    public static class CampusScreenLayout
    {
        public enum DeadlineStage { None, Comfortable, Caution, Warning, Urgent, Expired }
        public struct DeadlinePeriod { public long startAt, dueAt; }
        public static DeadlineStage Stage(long dueAt, long now)
        {
            if (dueAt <= 0 || dueAt > 253402300799L) return DeadlineStage.None;
            if (dueAt <= now) return DeadlineStage.Expired;
            long remaining = dueAt - now;
            return remaining <= 24 * 60 * 60 ? DeadlineStage.Urgent :
                remaining <= 3 * 24 * 60 * 60 ? DeadlineStage.Warning :
                remaining <= 7 * 24 * 60 * 60 ? DeadlineStage.Caution : DeadlineStage.Comfortable;
        }
        public static DeadlineStage CourseStage(IEnumerable<DeadlinePeriod> periods, long now)
        {
            var result = DeadlineStage.None;
            bool expired = false, active = false;
            foreach (var period in periods)
            {
                var stage = Stage(period.dueAt, now);
                if (stage == DeadlineStage.Expired) { expired = true; continue; }
                active = true;
                if (stage > result) result = stage;
            }
            return !active && expired ? DeadlineStage.Expired : result;
        }
        public static Color DeadlineColor(DeadlineStage stage)
        {
            switch (stage)
            {
                case DeadlineStage.Comfortable: return new Color32(148, 163, 184, 255);
                case DeadlineStage.Caution: return new Color32(234, 179, 8, 255);
                case DeadlineStage.Warning: return new Color32(249, 115, 22, 255);
                case DeadlineStage.Urgent: return new Color32(239, 68, 68, 255);
                case DeadlineStage.Expired: return new Color32(113, 113, 122, 255);
                default: return Color.white;
            }
        }
        public static string DeadlineLabel(long dueAt, long now)
        {
            return StageLabel(Stage(dueAt, now));
        }
        public static string StageLabel(DeadlineStage stage)
        {
            switch (stage)
            {
                case DeadlineStage.Comfortable: return "여유";
                case DeadlineStage.Caution: return "주의";
                case DeadlineStage.Warning: return "경고";
                case DeadlineStage.Urgent: return "긴급";
                case DeadlineStage.Expired: return "만료";
                default: return "마감 정보 없음";
            }
        }
        public static void DeadlineStripe(Rect area, Color color)
        {
            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(area, Texture2D.whiteTexture);
            GUI.color = previous;
        }
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

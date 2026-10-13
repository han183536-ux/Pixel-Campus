using UnityEngine;

namespace WebNotifications
{
    public static class CampusScreenLayout
    {
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

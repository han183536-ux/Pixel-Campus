using System;

namespace WebNotifications
{
    [Serializable]
    public class NotificationData
    {
        public string id, source_id, source_name, source_type, title, message, url;
        public string published_at, received_at, priority, target_object_id, icon_type;
        public string course_id, course_name;
        public bool is_read;
    }

    [Serializable]
    public class NotificationResponse
    {
        public NotificationData[] notifications;
        public string updated_at;
        public string mode, message;
        public bool stale;
    }

    [Serializable]
    public class NotificationErrorResponse { public string error, message; }
    [Serializable]
    public class NotificationLoginOptions { public bool available; public string code, message; }
}

using UnityEngine;

namespace WebNotifications
{
    public class NotificationIndicator : MonoBehaviour
    {
        public string sourceId;
        public NotificationManager manager;
        public NotificationDetailUI detailUI;
        private void OnGUI()
        {
            if (manager != null && manager.useWebView) return;
            if (manager == null || Camera.main == null) return;
            var screen = Camera.main.WorldToScreenPoint(transform.position + Vector3.up * 2.4f);
            if (screen.z < 0) return;
            var count = manager.Unread(sourceId);
            var previous = CampusTheme.Enter();
            try
            {
                float scale = CampusScreenLayout.Scale;
                var icon = new Rect(screen.x - 20 * scale, Screen.height - screen.y, 40 * scale, 40 * scale);
                CampusTheme.DrawNoticeIcon(icon, count, scale, count > 0 ? CampusTheme.Accent : CampusTheme.Muted);
                var target = new Rect(icon.x - 4 * scale, icon.y - 8 * scale, 64 * scale, 52 * scale);
                if (GUI.Button(target, new GUIContent("", sourceId + " · " + count + "건"), GUIStyle.none))
                    detailUI?.SelectSource(sourceId);
            }
            finally { GUI.skin = previous; }
        }
        private void OnMouseDown() { if (detailUI != null) detailUI.SelectSource(sourceId); }
    }
}

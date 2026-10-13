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
            GUI.backgroundColor = count > 0 ? new Color(1, .55f, .2f) : Color.gray;
            if (GUI.Button(new Rect(screen.x - 55, Screen.height - screen.y, 110, 34), sourceId + " · " + count))
                detailUI.SelectSource(sourceId);
            GUI.backgroundColor = Color.white;
        }
        private void OnMouseDown() { if (detailUI != null) detailUI.SelectSource(sourceId); }
    }
}

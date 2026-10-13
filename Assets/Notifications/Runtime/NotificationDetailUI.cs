using System;
using UnityEngine;

namespace WebNotifications
{
    public class NotificationDetailUI : MonoBehaviour
    {
        public NotificationManager manager;
        public NotificationSourceRegistry registry;
        public Camera mapCamera;
        private string source = "";
        private NotificationData selected;
        private Vector2 scroll;
        private GUIStyle wrapped;
        public void SelectSource(string id) { source = id; selected = null; scroll = Vector2.zero; }
        private float PanelWidth => Mathf.Min(440, Screen.width * .42f);
        private void LateUpdate()
        {
            if (mapCamera == null) return;
            float start = (PanelWidth + 24) / Screen.width;
            mapCamera.rect = new Rect(start, 0, 1 - start, 1);
        }
        private void OnGUI()
        {
            if (manager == null) return;
            if (wrapped == null) wrapped = new GUIStyle(GUI.skin.label) { wordWrap = true, richText = false };
            if (manager.useWebView)
            {
                GUILayout.BeginArea(new Rect(12, 12, PanelWidth, Screen.height - 24), GUI.skin.box);
                manager.GetComponent<SmartleadWebView>()?.Draw(wrapped);
                GUILayout.EndArea();
                return;
            }
            float width = PanelWidth;
            GUILayout.BeginArea(new Rect(12, 12, width, Screen.height - 24), GUI.skin.box);
            GUILayout.Label((manager.useWebView ? "오늘 수신한 공지 링크 · " : "오늘의 웹 알림 · ") + NotificationManager.TodayKorea.ToString("yyyy-MM-dd"), wrapped);
            var web = manager.GetComponent<SmartleadWebView>();
            if (manager.useWebView && web != null) web.Draw(wrapped);
            var login = manager.GetComponent<NotificationLoginUI>();
            if (login != null && !manager.useSampleData && !manager.useWebView) login.Draw(wrapped);
            bool loginUnavailable = !manager.useWebView && login != null && login.LoginUnavailable;
            if (!loginUnavailable) GUILayout.Label(manager.Status, wrapped);
            GUILayout.Label("안 읽은 알림 " + manager.Unread(""));
            GUI.enabled = !manager.IsLoading && !manager.IsAuthenticating && !loginUnavailable;
            if (GUILayout.Button(manager.IsLoading ? "불러오는 중…" : "새로고침")) manager.Refresh();
            GUI.enabled = true;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("전체")) SelectSource("");
            if (registry != null && registry.sources != null)
                foreach (var entry in registry.sources)
                    if (GUILayout.Button(entry.displayName)) SelectSource(entry.sourceId);
            GUILayout.EndHorizontal();
            GUILayout.Label("선택: " + (string.IsNullOrEmpty(source) ? "전체" : source));
            scroll = GUILayout.BeginScrollView(scroll);
            int count = 0;
            foreach (var item in manager.Items)
            {
                if (!string.IsNullOrEmpty(source) && item.source_id != source) continue;
                count++;
                if (GUILayout.Button((item.is_read ? "✓ " : "● ") + item.title, GUILayout.MinHeight(36)))
                { selected = item; manager.MarkRead(item); }
                GUILayout.Label((manager.useWebView ? item.course_name + " · 수신 " : item.source_name + " · ") + item.published_at, wrapped);
            }
            if (count == 0) GUILayout.Label(manager.HasLoaded ? "오늘 등록된 알림이 없습니다." : "연결을 완료하면 알림이 표시됩니다.", wrapped);
            if (selected != null && manager.Items.Exists(n => n.id == selected.id && n.source_id == selected.source_id))
            {
                GUILayout.Space(16);
                GUILayout.Label(selected.title, wrapped);
                GUILayout.Label(selected.message, wrapped);
                if (Uri.TryCreate(selected.url, UriKind.Absolute, out var link) &&
                    (link.Scheme == "https" || link.Scheme == "http") && GUILayout.Button("원문 열기"))
                { Application.OpenURL(link.AbsoluteUri); }
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
    }
}

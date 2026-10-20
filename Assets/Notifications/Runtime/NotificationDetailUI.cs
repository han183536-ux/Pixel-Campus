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
        private GUIStyle wrapped, appTitle, appButton, caption;
        public bool IsNotificationPanelOpen => showNotifications;
        private bool showNotifications, savedCameraMask;
        private int cameraMask;
        private bool trackingPanelPointer, panelPointerMoved, pullingPanel, pointerFromHandle;
        private int panelFingerId = -1, suppressPanelClicksUntil = -1, suppressCampusClicksUntil = -1;
        private Vector2 panelPointerStart;
        private float panelStartScroll, panelPull;
        public bool SuppressCampusInput => Time.frameCount <= suppressCampusClicksUntil;
        private Rect NotificationPanelRect
        {
            get
            {
                var content = CampusScreenLayout.ContentArea;
                float padding = 12 * CampusScreenLayout.Scale;
                return new Rect(content.x + padding, content.y + padding, Mathf.Max(1, content.width - 2 * padding),
                    Mathf.Max(1, content.height - 2 * padding));
            }
        }

        public void CloseNotifications()
        {
            showNotifications = false;
            scroll = Vector2.zero;
            ResetPanelGesture();
            suppressCampusClicksUntil = Time.frameCount + 1;
        }

        private void ResetPanelGesture()
        {
            trackingPanelPointer = panelPointerMoved = pullingPanel = pointerFromHandle = false;
            panelFingerId = -1;
            panelPull = 0;
        }

        private void BeginPanelGesture(Vector2 position)
        {
            var panel = NotificationPanelRect;
            ResetPanelGesture();
            if (!panel.Contains(position)) return;
            trackingPanelPointer = true;
            panelPointerStart = position;
            panelStartScroll = scroll.y;
            pointerFromHandle = position.y < panel.y + 40 * CampusScreenLayout.Scale;
        }

        private void MovePanelGesture(Vector2 position)
        {
            if (!trackingPanelPointer) return;
            var delta = position - panelPointerStart;
            float scale = CampusScreenLayout.Scale;
            if (delta.sqrMagnitude < 64 * scale * scale && !panelPointerMoved) return;
            panelPointerMoved = true;
            suppressPanelClicksUntil = Time.frameCount + 1;
            bool vertical = Mathf.Abs(delta.y) > Mathf.Abs(delta.x) * 1.2f;
            // A new downward gesture at the top dismisses; an existing list scroll
            // remains a scroll even when it subsequently reaches the top.
            pullingPanel = vertical && delta.y > 0 && (pointerFromHandle || panelStartScroll <= .5f);
            panelPull = pullingPanel ? delta.y : 0;
            if (!pullingPanel && !pointerFromHandle && vertical)
                scroll.y = Mathf.Max(0, panelStartScroll - delta.y);
        }

        private void EndPanelGesture(Vector2 position, bool cancelled)
        {
            if (!trackingPanelPointer) return;
            MovePanelGesture(position);
            bool close = !cancelled && pullingPanel && panelPull >= 72 * CampusScreenLayout.Scale;
            if (panelPointerMoved) suppressPanelClicksUntil = Time.frameCount + 1;
            ResetPanelGesture();
            if (close) CloseNotifications();
        }

        private void Update()
        {
            var school = manager != null && manager.useWebView ? manager.GetComponent<SmartleadWebView>() : null;
            if (!showNotifications || school == null || !school.IsAuthenticated || school.IsSchoolPageVisible)
            { ResetPanelGesture(); return; }
            if (Input.touchCount > 1) { ResetPanelGesture(); return; }
            if (Input.touchCount == 1)
            {
                var touch = Input.GetTouch(0);
                var position = new Vector2(touch.position.x, Screen.height - touch.position.y);
                if (touch.phase == TouchPhase.Began) { BeginPanelGesture(position); panelFingerId = touch.fingerId; }
                if (touch.fingerId != panelFingerId) return;
                if (touch.phase == TouchPhase.Moved) MovePanelGesture(position);
                else if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                    EndPanelGesture(position, touch.phase == TouchPhase.Canceled);
                return;
            }
            // Ignore touch-generated mouse events; desktop uses the left button.
            if (panelFingerId >= 0) { ResetPanelGesture(); return; }
            var mouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            if (Input.GetMouseButtonDown(0)) BeginPanelGesture(mouse);
            if (Input.GetMouseButton(0)) MovePanelGesture(mouse);
            if (Input.GetMouseButtonUp(0)) EndPanelGesture(mouse, false);
            else if (trackingPanelPointer && !Input.GetMouseButton(0)) ResetPanelGesture();
        }
        public void OpenCourseNotifications(string courseKey, string courseTitle)
        {
            var school = manager != null ? manager.GetComponent<SmartleadWebView>() : null;
            if (school == null || !school.IsAuthenticated || school.IsSchoolPageVisible) return;
            school.SelectCourse(courseKey, courseTitle);
            scroll = Vector2.zero;
            ResetPanelGesture();
            showNotifications = true;
        }
        public void SelectSource(string id) { source = id; selected = null; scroll = Vector2.zero; }
        private float PanelWidth => Mathf.Min(440, Screen.width * .42f);
        private void LateUpdate()
        {
            if (mapCamera == null) return;
            if (manager != null && manager.useWebView)
            {
                var school = manager.GetComponent<SmartleadWebView>();
                mapCamera.rect = CampusScreenLayout.CameraRect;
                if (!savedCameraMask) { cameraMask = mapCamera.cullingMask; savedCameraMask = true; }
                mapCamera.enabled = true;
                mapCamera.cullingMask = school != null && school.IsAuthenticated && !school.IsSchoolPageVisible ? cameraMask : 0;
                return;
            }
            mapCamera.enabled = true;
            if (savedCameraMask) mapCamera.cullingMask = cameraMask;
            float start = (PanelWidth + 24) / Screen.width;
            mapCamera.rect = new Rect(start, 0, 1 - start, 1);
        }
        private void DrawPortraitCampus(SmartleadWebView school)
        {
            if (school == null) return;
            float scale = CampusScreenLayout.Scale;
            var safe = CampusScreenLayout.SafeArea;
            float padding = 12 * scale;
            if (appTitle == null)
            {
                appTitle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, richText = false };
                appButton = new GUIStyle(GUI.skin.button) { richText = false };
                caption = new GUIStyle(GUI.skin.label) { wordWrap = true, richText = false };
            }
            appTitle.fontSize = Mathf.RoundToInt(17 * scale);
            appButton.fontSize = Mathf.RoundToInt(13 * scale);
            caption.fontSize = Mathf.RoundToInt(11 * scale);
            wrapped.fontSize = Mathf.RoundToInt(12 * scale);
            bool campus = school.IsAuthenticated && !school.IsSchoolPageVisible;
            GUI.Box(new Rect(safe.x, safe.y, safe.width, CampusScreenLayout.HeaderHeight), GUIContent.none);
            float buttonWidth = 62 * scale, buttonHeight = 38 * scale;
            float buttonY = safe.y + 10 * scale;
            float right = safe.xMax - padding;
            if (!campus)
            {
                string title = school.IsAuthenticated ? "학교 알림 기록" : "SmartLEAD 로그인";
                GUI.Label(new Rect(safe.x + padding, safe.y + 3 * scale, safe.width - buttonWidth - 3 * padding, 28 * scale), title, appTitle);
                GUI.Label(new Rect(safe.x + padding, safe.y + 31 * scale, safe.width - buttonWidth - 3 * padding, 24 * scale),
                    school.IsAuthenticated ? "확인 후 캠퍼스로 돌아가세요." : "로그인하면 픽셀 캠퍼스로 이동합니다.", caption);
                if (school.IsAuthenticated)
                {
                    if (GUI.Button(new Rect(right - buttonWidth, buttonY, buttonWidth, buttonHeight), "캠퍼스", appButton)) school.CloseSchoolPage();
                }
                else if (GUI.Button(new Rect(right - buttonWidth, buttonY, buttonWidth, buttonHeight), "재접속", appButton)) school.Refresh();
                GUI.Label(new Rect(safe.x + padding, safe.y + CampusScreenLayout.HeaderHeight + 20 * scale, safe.width - 2 * padding, 90 * scale), school.Status, wrapped);
                return;
            }
            GUI.Label(new Rect(safe.x + padding, safe.y, safe.width - buttonWidth - 3 * padding, CampusScreenLayout.HeaderHeight), "PIXEL CAMPUS", appTitle);
            if (GUI.Button(new Rect(right - buttonWidth, buttonY, buttonWidth, buttonHeight), "계정", appButton))
            {
                CloseNotifications();
                school.OpenAccountPage();
            }
            if (!showNotifications) return;
            var panel = NotificationPanelRect;
            panel.y += panelPull;
            var background = GUI.backgroundColor;
            GUI.backgroundColor = school.SelectedDeadlineColor;
            GUI.Box(panel, GUIContent.none);
            GUI.backgroundColor = background;
            CampusScreenLayout.DeadlineStripe(new Rect(panel.x, panel.y, panel.width, 4 * scale), school.SelectedDeadlineColor);
            GUI.Label(new Rect(panel.x + 10 * scale, panel.y + 7 * scale, panel.width - 20 * scale, 30 * scale),
                "아래로 밀어 닫기", caption);
            bool enabled = GUI.enabled;
            GUI.enabled = enabled && !panelPointerMoved && Time.frameCount > suppressPanelClicksUntil;
            if (panelPointerMoved) GUIUtility.hotControl = 0;
            GUILayout.BeginArea(new Rect(panel.x + 6 * scale, panel.y + 42 * scale, panel.width - 12 * scale,
                Mathf.Max(1, panel.height - 48 * scale)));
            scroll = GUILayout.BeginScrollView(scroll);
            school.Draw(wrapped);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
            GUI.enabled = enabled;
        }
        private void OnGUI()
        {
            if (manager == null) return;
            if (wrapped == null) wrapped = new GUIStyle(GUI.skin.label) { wordWrap = true, richText = false };
            if (manager.useWebView)
            {
                DrawPortraitCampus(manager.GetComponent<SmartleadWebView>());
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




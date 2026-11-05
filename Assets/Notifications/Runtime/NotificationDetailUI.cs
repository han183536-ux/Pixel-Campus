using System;
using UnityEngine;

namespace WebNotifications
{
    public class NotificationDetailUI : MonoBehaviour
    {
        private enum CampusTab { Courses, Website, Map, Assignments, Account }
        private static readonly string[] TabLabels = { "과목", "홈페이지", "지도", "과제", "계정" };
        private CampusTab tab = CampusTab.Map;
        public NotificationManager manager;
        public NotificationSourceRegistry registry;
        public Camera mapCamera;
        private string source = "";
        private NotificationData selected;
        private Vector2 scroll, tabScroll;
        private GUIStyle wrapped, appTitle, appButton;
        public bool IsNotificationPanelOpen => showNotifications || tab == CampusTab.Website;
        public bool IsMapTabActive => tab == CampusTab.Map;
        private bool showNotifications, savedCameraMask;
        private int cameraMask;
        private bool trackingPanelPointer, panelPointerMoved, pullingPanel, pointerFromHandle;
        private int panelFingerId = -1, suppressPanelClicksUntil = -1, suppressCampusClicksUntil = -1;
        private Vector2 panelPointerStart;
        private float panelStartScroll, panelPull;
        public bool SuppressCampusInput => Time.frameCount <= suppressCampusClicksUntil;
        private Rect TabContentArea => tab == CampusTab.Website ? CampusScreenLayout.ContentArea : CampusScreenLayout.CampusArea;
        private Rect TabListRect
        {
            get
            {
                var content = TabContentArea;
                float padding = 12 * CampusScreenLayout.Scale;
                return new Rect(content.x + padding, content.y + padding, Mathf.Max(1, content.width - 2 * padding),
                    Mathf.Max(1, content.height - 2 * padding));
            }
        }

        public void CloseNotifications()
        {
            if (tab == CampusTab.Website)
            {
                tab = CampusTab.Map;
                tabScroll = Vector2.zero;
                GetComponent<SmartleadWebView>()?.SetWebsiteNoticesVisible(false);
            }
            showNotifications = false;
            scroll = Vector2.zero;
            ResetPanelGesture();
            suppressCampusClicksUntil = Time.frameCount + 1;
        }

        private void SelectTab(SmartleadWebView school, CampusTab next)
        {
            if (tab == next && !showNotifications) return;
            CloseNotifications();
            tab = next;
            tabScroll = Vector2.zero;
            if (next == CampusTab.Website) school.SetWebsiteNoticesVisible(true);
        }

        public bool NavigateBack(SmartleadWebView school)
        {
            if (IsNotificationPanelOpen) { CloseNotifications(); return true; }
            if (IsMapTabActive || !school.IsAuthenticated || school.IsSchoolPageVisible) return false;
            SelectTab(school, CampusTab.Map);
            return true;
        }

        private void ResetPanelGesture()
        {
            trackingPanelPointer = panelPointerMoved = pullingPanel = pointerFromHandle = false;
            panelFingerId = -1;
            panelPull = 0;
        }

        private void BeginPanelGesture(Vector2 position)
        {
            var panel = TabListRect;
            ResetPanelGesture();
            if (!panel.Contains(position)) return;
            trackingPanelPointer = true;
            panelPointerStart = position;
            panelStartScroll = showNotifications ? scroll.y : tabScroll.y;
            pointerFromHandle = showNotifications && position.y < panel.y + 40 * CampusScreenLayout.Scale;
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
            pullingPanel = showNotifications && vertical && delta.y > 0 && (pointerFromHandle || panelStartScroll <= .5f);
            panelPull = pullingPanel ? delta.y : 0;
            if (!pullingPanel && !pointerFromHandle && vertical)
            {
                float positionY = Mathf.Max(0, panelStartScroll - delta.y);
                if (showNotifications) scroll.y = positionY;
                else tabScroll.y = positionY;
            }
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
            bool showingList = tab == CampusTab.Website || school != null && school.IsAuthenticated &&
                !school.IsSchoolPageVisible && (showNotifications || !IsMapTabActive);
            if (!showingList)
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
                var map = GetComponent<CampusMapView>();
                if (map != null && school != null && school.IsAuthenticated && !school.IsSchoolPageVisible)
                {
                    var area = CampusMapView.Viewport;
                    mapCamera.rect = new Rect(area.x / Screen.width, (Screen.height - area.yMax) / Screen.height,
                        area.width / Screen.width, area.height / Screen.height);
                }
                if (!savedCameraMask) { cameraMask = mapCamera.cullingMask; savedCameraMask = true; }
                mapCamera.enabled = true;
                mapCamera.cullingMask = IsMapTabActive && school != null && school.IsAuthenticated && !school.IsSchoolPageVisible &&
                    (map == null || !map.IsMapVisible) ? cameraMask : 0;
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
                appTitle = new GUIStyle(CampusTheme.Heading) { wordWrap = false, clipping = TextClipping.Clip };
                appButton = new GUIStyle(GUI.skin.button) { padding = new RectOffset(3, 3, 4, 4) };
            }
            appTitle.fontSize = Mathf.RoundToInt(17 * scale);
            appButton.fontSize = Mathf.RoundToInt(12 * scale);
            var caption = CampusTheme.Caption;
            wrapped.fontSize = Mathf.RoundToInt(12 * scale);
            bool campus = school.IsAuthenticated && !school.IsSchoolPageVisible;
            var contentArea = CampusScreenLayout.ContentArea;
            float headerBottom = !campus || tab == CampusTab.Website ? contentArea.y : safe.y;
            CampusTheme.Fill(new Rect(0, 0, Screen.width, headerBottom), Color.white);
            if (headerBottom > safe.y)
                CampusTheme.Fill(new Rect(0, headerBottom - 1, Screen.width, 1), CampusTheme.Line);
            CampusTheme.Fill(new Rect(0, safe.yMax, Screen.width, Screen.height - safe.yMax), CampusTheme.Paper);
            float buttonWidth = 62 * scale, buttonHeight = 38 * scale;
            float buttonY = safe.y + 10 * scale;
            float right = safe.xMax - padding;
            if (tab == CampusTab.Website)
            {
                GUI.Label(new Rect(safe.x + padding, safe.y, safe.width - 2 * padding, CampusScreenLayout.HeaderHeight), "학교 공지", appTitle);
                DrawTabList(school);
                return;
            }
            // Desktop WebViews draw into contentArea as textures. Keep the app background
            // outside that area while showing a school page or an assignment document.
            if (school.IsAssignmentDetailVisible)
            {
                GUI.Label(new Rect(safe.x + padding, safe.y + 3 * scale, safe.width - 2 * buttonWidth - 4 * padding, 28 * scale), school.AssignmentDetailTitle, appTitle);
                GUI.Label(new Rect(safe.x + padding, safe.y + 31 * scale, safe.width - 2 * buttonWidth - 4 * padding, 24 * scale), school.IsAssignmentSubmissionVisible ? "파일 선택 후 학교 화면에서 저장하세요." : "픽셀 캠퍼스 · 과제 상세", caption);
                if (!school.IsAssignmentSubmissionVisible && GUI.Button(new Rect(right - 2 * buttonWidth - 6 * scale, buttonY, buttonWidth, buttonHeight), "새로고침", appButton)) school.RefreshAssignmentDetail();
                if (GUI.Button(new Rect(right - buttonWidth, buttonY, buttonWidth, buttonHeight), "뒤로", appButton)) school.CloseAssignmentDetail();
                if (!string.IsNullOrEmpty(school.AssignmentRenderStatus))
                {
                    var content = CampusScreenLayout.ContentArea;
                    GUI.Label(new Rect(content.x + padding, content.y + 20 * scale, content.width - 2 * padding, 110 * scale), school.AssignmentRenderStatus, wrapped);
                }
                return;
            }
            if (!campus)
            {
                string title = school.IsLecturePageVisible ? "동영상 강의" : school.IsAuthenticated ?
                    tab == CampusTab.Account ? "학교 계정" : "학교 알림 기록" : "SmartLEAD 로그인";
                GUI.Label(new Rect(safe.x + padding, safe.y + 3 * scale, safe.width - buttonWidth - 3 * padding, 28 * scale), title, appTitle);
                GUI.Label(new Rect(safe.x + padding, safe.y + 31 * scale, safe.width - buttonWidth - 3 * padding, 24 * scale),
                    school.IsLecturePageVisible ? "시청 후 학교의 출석 상태를 확인하세요." : school.IsAuthenticated ? "확인 후 캠퍼스로 돌아가세요." : "로그인하면 픽셀 캠퍼스로 이동합니다.", caption);
                if (school.IsAuthenticated)
                {
                    if (GUI.Button(new Rect(right - buttonWidth, buttonY, buttonWidth, buttonHeight), "뒤로", appButton)) school.CloseSchoolPage();
                }
                else if (GUI.Button(new Rect(right - buttonWidth, buttonY, buttonWidth, buttonHeight), "재접속", appButton)) school.Refresh();
                GUI.Label(new Rect(safe.x + padding, safe.y + CampusScreenLayout.HeaderHeight + 20 * scale, safe.width - 2 * padding, 90 * scale), school.Status, wrapped);
                return;
            }
            if (!showNotifications)
            {
                if (!IsMapTabActive) DrawTabList(school);
                return;
            }
            CampusTheme.Fill(TabContentArea, CampusTheme.Paper);
            var panel = TabListRect;
            float offset = Mathf.Min(panelPull, Mathf.Max(0, panel.height - 100 * scale));
            panel.y += offset;
            panel.height -= offset;
            GUI.Box(panel, GUIContent.none);
            var deadline = school.SelectedDeadlineColor;
            CampusTheme.Fill(new Rect(panel.x + 12 * scale, panel.y + 43 * scale, panel.width - 24 * scale, 2 * scale),
                deadline == Color.white ? CampusTheme.Line : deadline);
            GUI.Label(new Rect(panel.x + 12 * scale, panel.y + 5 * scale, panel.width - 92 * scale, 36 * scale),
                "과제와 알림", CampusTheme.Heading);
            bool enabled = GUI.enabled;
            GUI.enabled = enabled && !panelPointerMoved && Time.frameCount > suppressPanelClicksUntil;
            if (panelPointerMoved) GUIUtility.hotControl = 0;
            if (GUI.Button(new Rect(panel.xMax - 72 * scale, panel.y + 5 * scale, 60 * scale, 34 * scale), "닫기"))
                CloseNotifications();
            GUILayout.BeginArea(new Rect(panel.x + 12 * scale, panel.y + 52 * scale, panel.width - 24 * scale,
                Mathf.Max(1, panel.height - 62 * scale)));
            scroll = GUILayout.BeginScrollView(scroll);
            school.Draw(wrapped);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
            GUI.enabled = enabled;
        }

        private void DrawTabList(SmartleadWebView school)
        {
            CampusTheme.Fill(TabContentArea, CampusTheme.Paper);
            bool enabled = GUI.enabled;
            GUI.enabled = enabled && !panelPointerMoved && Time.frameCount > suppressPanelClicksUntil;
            if (panelPointerMoved) GUIUtility.hotControl = 0;
            GUILayout.BeginArea(TabListRect);
            tabScroll = GUILayout.BeginScrollView(tabScroll);
            switch (tab)
            {
                case CampusTab.Courses: school.DrawCourseTab(wrapped); break;
                case CampusTab.Website: GetComponent<UniversityNoticeFeed>()?.Draw(wrapped); break;
                case CampusTab.Assignments: school.DrawAssignmentTab(wrapped); break;
                case CampusTab.Account:
                    GUILayout.Label("계정", CampusTheme.Heading);
                    if (GUILayout.Button("SmartLEAD 계정 열기", CampusTheme.Primary, GUILayout.MinHeight(44 * CampusScreenLayout.Scale)))
                        school.OpenAccountPage();
                    if (GUILayout.Button("로그아웃", GUILayout.MinHeight(44 * CampusScreenLayout.Scale)))
                    {
                        SelectTab(school, CampusTab.Map);
                        school.Logout();
                    }
                    break;
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
            GUI.enabled = enabled;
        }

        private void DrawBottomNavigation(SmartleadWebView school)
        {
            var area = CampusScreenLayout.NavigationArea;
            float s = CampusScreenLayout.Scale, width = area.width / TabLabels.Length;
            CampusTheme.Fill(area, Color.white);
            CampusTheme.Fill(new Rect(area.x, area.y, area.width, 1), CampusTheme.Line);
            bool enabled = GUI.enabled;
            bool campus = school.IsAuthenticated && !school.IsSchoolPageVisible;
            bool canSwitch = !school.IsAssignmentDetailVisible && (campus || !school.IsAuthenticated || tab == CampusTab.Website);
            for (int i = 0; i < TabLabels.Length; i++)
            {
                var next = (CampusTab)i;
                GUI.enabled = enabled && canSwitch && (campus || next == CampusTab.Website || next == CampusTab.Map || next == CampusTab.Account);
                var rect = new Rect(area.x + i * width + 4 * s, area.y + 8 * s, Mathf.Max(1, width - 8 * s), area.height - 16 * s);
                if (GUI.Button(rect, TabLabels[i], tab == next ? CampusTheme.Primary : appButton))
                    SelectTab(school, next);
            }
            GUI.enabled = enabled;
        }
        private void OnGUI()
        {
            if (manager == null) return;
            var previous = CampusTheme.Enter();
            try { DrawScreen(); }
            finally { GUI.skin = previous; }
        }

        private void DrawScreen()
        {
            if (wrapped == null) wrapped = new GUIStyle(GUI.skin.label) { wordWrap = true, richText = false };
            wrapped.fontSize = GUI.skin.label.fontSize;
            if (manager.useWebView)
            {
                var school = manager.GetComponent<SmartleadWebView>();
                if (school != null)
                {
                    DrawPortraitCampus(school);
                    DrawBottomNavigation(school);
                }
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




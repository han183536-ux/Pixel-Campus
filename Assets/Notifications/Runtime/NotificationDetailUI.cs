using System;
using UnityEngine;

namespace WebNotifications
{
    public class NotificationDetailUI : MonoBehaviour
    {
        private enum CampusTab { Website, Map, Assignments, Account, Courses }
        private static readonly (CampusTab tab, string label, CampusTheme.TabGlyph glyph)[] NavigationTabs =
        {
            (CampusTab.Courses, "과목", CampusTheme.TabGlyph.Courses),
            (CampusTab.Assignments, "과제", CampusTheme.TabGlyph.Assignments),
            (CampusTab.Map, "지도", CampusTheme.TabGlyph.Map),
            (CampusTab.Website, "공지", CampusTheme.TabGlyph.Notices),
            (CampusTab.Account, "계정", CampusTheme.TabGlyph.Account)
        };
        private CampusTab tab = CampusTab.Map;
        public NotificationManager manager;
        public NotificationSourceRegistry registry;
        public Camera mapCamera;
        private string source = "";
        private NotificationData selected;
        private Vector2 scroll, tabScroll, outlineActivityScroll;
        private GUIStyle appTitle, appButton;
        public bool IsNotificationPanelOpen => showNotifications || !IsMapTabActive;
        public bool IsMapTabActive => tab == CampusTab.Map;
        private bool showNotifications, savedCameraMask;
        private int cameraMask;
        private bool trackingPanelPointer, panelPointerMoved, pullingPanel, pointerFromHandle;
        private int panelFingerId = -1, suppressPanelClicksUntil = -1, suppressCampusClicksUntil = -1;
        private Vector2 panelPointerStart;
        private float panelStartScroll, panelPull;
        public bool SuppressCampusInput => Time.frameCount <= suppressCampusClicksUntil;
        internal bool ShowsMapBackdrop(SmartleadWebView school) => tab == CampusTab.Website ||
            school.IsAuthenticated && !school.IsSchoolPageVisible;

        private Rect PanelRect
        {
            get
            {
                var content = CampusScreenLayout.CampusArea;
                float padding = 12 * CampusScreenLayout.Scale;
                return new Rect(content.x + padding, content.y, Mathf.Max(1, content.width - 2 * padding),
                    Mathf.Max(1, content.height - padding));
            }
        }

        public void CloseNotifications()
        {
            if (!showNotifications)
            {
                if (tab == CampusTab.Website) GetComponent<SmartleadWebView>()?.SetWebsiteNoticesVisible(false);
                tab = CampusTab.Map;
                tabScroll = Vector2.zero;
            }
            ResetPanelSelection();
        }

        private void ResetPanelSelection()
        {
            if (showNotifications) GetComponent<SmartleadWebView>()?.CloseCourseOutline();
            showNotifications = false;
            scroll = outlineActivityScroll = Vector2.zero;
            ResetPanelGesture();
            suppressCampusClicksUntil = Time.frameCount + 1;
        }

        private void SelectTab(SmartleadWebView school, CampusTab next)
        {
            bool leavingSchoolPage = school.IsAuthenticated && school.IsSchoolPageVisible;
            if (tab == next && !showNotifications && !leavingSchoolPage)
            {
                if (next == CampusTab.Map && ShowsMapBackdrop(school)) GetComponent<CampusMapView>()?.ShowEntireMap();
                return;
            }
            if (leavingSchoolPage)
            {
                if (school.IsAssignmentDetailVisible) school.CloseAssignmentDetail(returnToCampus: true);
                else school.CloseSchoolPage();
            }
            if ((tab == CampusTab.Website) != (next == CampusTab.Website))
                school.SetWebsiteNoticesVisible(next == CampusTab.Website);
            ResetPanelSelection();
            tab = next;
            tabScroll = Vector2.zero;
        }

        public bool NavigateBack(SmartleadWebView school)
        {
            if (!IsNotificationPanelOpen || !ShowsMapBackdrop(school)) return false;
            BackWithinPanel(school);
            return true;
        }

        private void BackWithinPanel(SmartleadWebView school)
        {
            if (showNotifications && school.IsOutlineActivityOpen)
            {
                school.CloseOutlineActivity();
                outlineActivityScroll = Vector2.zero;
                ResetPanelGesture();
                return;
            }
            CloseNotifications();
        }

        private ref Vector2 PanelScroll(SmartleadWebView school)
        {
            if (!showNotifications) return ref tabScroll;
            if (school.IsOutlineActivityOpen) return ref outlineActivityScroll;
            return ref scroll;
        }

        private void ResetPanelGesture()
        {
            trackingPanelPointer = panelPointerMoved = pullingPanel = pointerFromHandle = false;
            panelFingerId = -1;
            panelPull = 0;
        }

        private void BeginPanelGesture(Vector2 position, SmartleadWebView school)
        {
            var panel = PanelRect;
            ResetPanelGesture();
            if (!panel.Contains(position)) return;
            trackingPanelPointer = true;
            panelPointerStart = position;
            panelStartScroll = PanelScroll(school).y;
            pointerFromHandle = position.y < panel.y + 62 * CampusScreenLayout.Scale;
        }

        private void MovePanelGesture(Vector2 position, SmartleadWebView school)
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
            pullingPanel = vertical && delta.y > 0 && (pointerFromHandle || showNotifications && panelStartScroll <= .5f);
            panelPull = pullingPanel ? delta.y : 0;
            if (!pullingPanel && !pointerFromHandle && vertical)
            {
                PanelScroll(school).y = Mathf.Max(0, panelStartScroll - delta.y);
            }
        }

        private void EndPanelGesture(Vector2 position, bool cancelled, SmartleadWebView school)
        {
            if (!trackingPanelPointer) return;
            MovePanelGesture(position, school);
            bool close = !cancelled && pullingPanel && panelPull >= 72 * CampusScreenLayout.Scale;
            if (panelPointerMoved) suppressPanelClicksUntil = Time.frameCount + 1;
            ResetPanelGesture();
            if (close) BackWithinPanel(school);
        }

        private void Update()
        {
            var school = manager != null && manager.useWebView ? manager.GetComponent<SmartleadWebView>() : null;
            bool showingList = school != null && ShowsMapBackdrop(school) && IsNotificationPanelOpen;
            if (!showingList)
            { ResetPanelGesture(); return; }
            if (Input.touchCount > 1) { ResetPanelGesture(); return; }
            if (Input.touchCount == 1)
            {
                var touch = Input.GetTouch(0);
                var position = new Vector2(touch.position.x, Screen.height - touch.position.y);
                if (touch.phase == TouchPhase.Began) { BeginPanelGesture(position, school); panelFingerId = touch.fingerId; }
                if (touch.fingerId != panelFingerId) return;
                if (touch.phase == TouchPhase.Moved) MovePanelGesture(position, school);
                else if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                    EndPanelGesture(position, touch.phase == TouchPhase.Canceled, school);
                return;
            }
            // Ignore touch-generated mouse events; desktop uses the left button.
            if (panelFingerId >= 0) { ResetPanelGesture(); return; }
            var mouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            if (Input.GetMouseButtonDown(0)) BeginPanelGesture(mouse, school);
            if (Input.GetMouseButton(0)) MovePanelGesture(mouse, school);
            if (Input.GetMouseButtonUp(0)) EndPanelGesture(mouse, false, school);
            else if (trackingPanelPointer && !Input.GetMouseButton(0)) ResetPanelGesture();
        }
        public void OpenCourseNotifications(string courseKey, string courseTitle, bool openOutline = false)
        {
            var school = manager != null ? manager.GetComponent<SmartleadWebView>() : null;
            if (school == null || !school.IsAuthenticated || school.IsSchoolPageVisible) return;
            school.SelectCourse(courseKey, courseTitle, openOutline);
            scroll = outlineActivityScroll = Vector2.zero;
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
                if (map != null && map.IsMapVisible)
                {
                    var area = CampusMapView.Viewport;
                    mapCamera.rect = new Rect(area.x / Screen.width, (Screen.height - area.yMax) / Screen.height,
                        area.width / Screen.width, area.height / Screen.height);
                }
                if (!savedCameraMask) { cameraMask = mapCamera.cullingMask; savedCameraMask = true; }
                mapCamera.enabled = true;
                mapCamera.cullingMask = school != null && ShowsMapBackdrop(school) &&
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
            if (ShowsMapBackdrop(school))
            {
                if (IsNotificationPanelOpen) DrawCampusPanel(school);
                return;
            }
            DrawSchoolWebViewHeader(school);
        }

        private void DrawSchoolWebViewHeader(SmartleadWebView school)
        {
            float scale = CampusScreenLayout.Scale;
            var safe = CampusScreenLayout.SafeArea;
            float padding = 12 * scale;
            if (appTitle == null)
            {
                appTitle = new GUIStyle(CampusTheme.Heading) { wordWrap = false, clipping = TextClipping.Clip };
                appButton = new GUIStyle(GUI.skin.button) { padding = new RectOffset(3, 3, 4, 4) };
            }
            appTitle.fontSize = Mathf.RoundToInt(21 * scale);
            appButton.fontSize = Mathf.RoundToInt(12 * scale);
            var caption = CampusTheme.Caption;
            var contentArea = CampusScreenLayout.ContentArea;
            float headerBottom = contentArea.y;
            CampusTheme.Fill(new Rect(0, 0, Screen.width, headerBottom), Color.white);
            if (headerBottom > safe.y)
                CampusTheme.Fill(new Rect(0, headerBottom - 1, Screen.width, 1), CampusTheme.Line);
            CampusTheme.Fill(new Rect(0, safe.yMax, Screen.width, Screen.height - safe.yMax), CampusTheme.Paper);
            float buttonWidth = 66 * scale, buttonHeight = 44 * scale;
            float buttonY = safe.y + 7 * scale;
            float right = safe.xMax - padding;
            // Desktop WebViews draw into contentArea as textures. Keep the app background
            // outside that area while showing a school page or an assignment document.
            if (school.IsAssignmentDetailVisible)
            {
                bool submitting = school.IsAssignmentSubmissionVisible;
                var back = new Rect(safe.x + 4 * scale, buttonY, buttonHeight, buttonHeight);
                float titleX = back.xMax + 5 * scale;
                float titleWidth = Mathf.Max(1, right - titleX);
                string courseName = school.AssignmentCourseName;
                GUI.Label(new Rect(titleX, safe.y + (submitting ? 3 * scale : 0),
                    titleWidth, submitting ? 28 * scale : CampusScreenLayout.HeaderHeight),
                    new GUIContent(string.IsNullOrEmpty(courseName) ? school.AssignmentDetailTitle : courseName, courseName), appTitle);
                if (submitting)
                    GUI.Label(new Rect(titleX, safe.y + 31 * scale, titleWidth, 24 * scale),
                        "파일 선택 후 학교 화면에서 저장하세요.", caption);
                if (CampusTheme.BackButton(back))
                    school.CloseAssignmentDetail();
                if (!string.IsNullOrEmpty(school.AssignmentRenderStatus))
                {
                    var content = CampusScreenLayout.ContentArea;
                    GUI.Label(new Rect(content.x + padding, content.y + 20 * scale, content.width - 2 * padding, 110 * scale), school.AssignmentRenderStatus, CampusTheme.Body);
                }
                return;
            }
            string title = school.IsLecturePageVisible ? "동영상 강의" : school.IsAuthenticated ?
                tab == CampusTab.Account ? "학교 계정" : "학교 알림 기록" : "SmartLEAD 로그인";
            GUI.Label(new Rect(safe.x + padding, safe.y + 3 * scale, safe.width - buttonWidth - 3 * padding, 28 * scale), title, appTitle);
            GUI.Label(new Rect(safe.x + padding, safe.y + 29 * scale, safe.width - buttonWidth - 3 * padding, 27 * scale),
                school.IsLecturePageVisible ? "시청 후 학교의 출석 상태를 확인하세요." :
                school.IsAuthenticated ? "확인 후 캠퍼스로 돌아가세요." : school.Status, caption);
            if (school.IsAuthenticated)
            {
                if (GUI.Button(new Rect(right - buttonWidth, buttonY, buttonWidth, buttonHeight), "뒤로", appButton)) school.CloseSchoolPage();
            }
            else if (GUI.Button(new Rect(right - buttonWidth, buttonY, buttonWidth, buttonHeight), "재접속", appButton)) school.Refresh();
        }

        private void DrawCampusPanel(SmartleadWebView school)
        {
            float scale = CampusScreenLayout.Scale;
            bool courseAlerts = showNotifications;
            bool activityDetails = courseAlerts && school.IsOutlineActivityOpen;
            var panelTab = tab;
            CampusTheme.Fill(CampusMapView.Viewport, CampusTheme.MapScrim);
            var panel = PanelRect;
            float offset = Mathf.Min(panelPull, Mathf.Max(0, panel.height - 100 * scale));
            panel.y += offset;
            panel.height -= offset;
            GUI.Box(panel, GUIContent.none, CampusTheme.GlassSheet);
            CampusTheme.Fill(new Rect(panel.center.x - 16 * scale, panel.y + 9 * scale, 32 * scale, 3 * scale), CampusTheme.Line);
            CampusTheme.Fill(new Rect(panel.x + 16 * scale, panel.y + 61 * scale, panel.width - 32 * scale, 1), CampusTheme.Line);
            string title = activityDetails ? "자료 상세" : courseAlerts ? "과목 상세" : panelTab switch
            {
                CampusTab.Website => "학교 공지",
                CampusTab.Assignments => "과제",
                CampusTab.Courses => "수강 과목",
                _ => "계정"
            };
            float titleInset = (activityDetails ? 56 : 16) * scale;
            GUI.Label(new Rect(panel.x + titleInset, panel.y + 16 * scale, panel.width - titleInset - 16 * scale, 44 * scale),
                title, CampusTheme.Heading);
            bool enabled = GUI.enabled;
            GUI.enabled = enabled && !panelPointerMoved && Time.frameCount > suppressPanelClicksUntil;
            if (panelPointerMoved) GUIUtility.hotControl = 0;
            if (activityDetails && CampusTheme.BackButton(new Rect(panel.x + 8 * scale, panel.y + 16 * scale, 44 * scale, 44 * scale)))
            {
                BackWithinPanel(school);
                GUI.enabled = enabled;
                return;
            }
            var contentRect = new Rect(panel.x + 14 * scale, panel.y + 67 * scale, panel.width - 28 * scale,
                Mathf.Max(1, panel.height - 80 * scale));
            GUILayout.BeginArea(contentRect);
            ref Vector2 position = ref PanelScroll(school);
            position = GUILayout.BeginScrollView(position);
            if (courseAlerts) school.Draw(CampusTheme.Body, contentRect.width);
            else DrawTabList(school, panelTab, contentRect.width);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
            GUI.enabled = enabled;
        }

        private void DrawTabList(SmartleadWebView school, CampusTab panelTab, float width)
        {
            switch (panelTab)
            {
                case CampusTab.Courses: school.DrawCourseTab(width); break;
                case CampusTab.Website: GetComponent<UniversityNoticeFeed>()?.Draw(CampusTheme.Body, width); break;
                case CampusTab.Assignments: school.DrawAssignmentTab(width); break;
                case CampusTab.Account:
                    if (CampusTheme.Action("SmartLEAD 계정 열기", CampusTheme.Primary))
                        school.OpenAccountPage();
                    if (school.HasSavedLogin && CampusTheme.Action("이 기기의 저장된 로그인 삭제", GUI.skin.button))
                        school.ForgetSavedLogin();
                    if (CampusTheme.Action("로그아웃", GUI.skin.button))
                    {
                        SelectTab(school, CampusTab.Map);
                        school.Logout();
                    }
                    break;
            }
        }

        private void DrawBottomNavigation(SmartleadWebView school)
        {
            var area = CampusScreenLayout.NavigationArea;
            float s = CampusScreenLayout.Scale;
            bool mapBackdrop = ShowsMapBackdrop(school);
            if (mapBackdrop)
            {
                area = new Rect(area.x + 12 * s, area.y + 3 * s, area.width - 24 * s, area.height - 7 * s);
                GUI.Box(area, GUIContent.none, CampusTheme.GlassNavigation);
            }
            else
            {
                CampusTheme.Fill(area, Color.white);
                CampusTheme.Fill(new Rect(area.x, area.y, area.width, 1), CampusTheme.Line);
            }
            float width = area.width / NavigationTabs.Length;
            bool enabled = GUI.enabled;
            for (int i = 0; i < NavigationTabs.Length; i++)
            {
                var (next, label, glyph) = NavigationTabs[i];
                GUI.enabled = enabled && (school.IsAuthenticated || next == CampusTab.Website || next == CampusTab.Map || next == CampusTab.Account);
                float verticalInset = (mapBackdrop ? 4 : 8) * s;
                var rect = new Rect(area.x + i * width + 4 * s, area.y + verticalInset,
                    Mathf.Max(1, width - 8 * s), area.height - 2 * verticalInset);
                if (CampusTheme.TabButton(rect, label, glyph, tab == next))
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
            var wrapped = CampusTheme.Body;
            GUILayout.BeginArea(new Rect(12, 12, width, Screen.height - 24), GUI.skin.box);
            GUILayout.Label("오늘의 알림", CampusTheme.Heading);
            var login = manager.GetComponent<NotificationLoginUI>();
            if (login != null && !manager.useSampleData) login.Draw(wrapped);
            bool loginUnavailable = login != null && login.LoginUnavailable;
            GUI.enabled = !manager.IsLoading && !manager.IsAuthenticating && !loginUnavailable;
            if (GUILayout.Button("새로고침")) manager.Refresh();
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
                GUILayout.Label(item.source_name + " · " + item.published_at, wrapped);
            }
            if (count == 0 && !loginUnavailable)
                GUILayout.Label(manager.IsLoading ? "알림을 불러오는 중…" : manager.HasLoaded ?
                    "표시할 알림이 없습니다." : "알림을 가져오지 못했습니다. 다시 확인해 주세요.", wrapped);
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




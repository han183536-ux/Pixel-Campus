using UnityEngine;

namespace WebNotifications
{
    /// <summary>Offline campus image with pointer-anchored zoom and bounded panning.</summary>
    public sealed class CampusMapView : MonoBehaviour
    {
        private const string ViewKey = "PixelCampus.MapView.GeographicCampus.";
        private const string AttributionUrl = "https://www.openstreetmap.org/copyright";
        private static readonly GUIContent MapAttribution = new GUIContent("© OpenStreetMap contributors", "OpenStreetMap 저작권 및 라이선스");
        // The texture includes the surrounding streets, so only the campus itself must fit at overview zoom.
        private static readonly Vector2 CampusFootprint = new Vector2(.86f, .46f);
        private SmartleadWebView school;
        private NotificationDetailUI detail;
        private Texture2D map;
        private RenderTexture blurredMap;
        private Material blurMaterial;
        private GUIStyle attributionStyle;
        private bool blurUnavailable;
        private static readonly int BlurStep = Shader.PropertyToID("_BlurStep");
        private float zoom = 1f;
        private Vector2 pan, previousPointer;
        private int fingerId = -1;
        private bool dragging, pinching, initialized;
        private Vector2 lastImageSize, savedPan;
        private float savedZoom, saveAfter;
        private bool viewDirty;
        private Vector2 gestureOrigin;
        private int suppressMarkerClicksUntil = -1;
        public bool CanSelectCourse => Time.frameCount > suppressMarkerClicksUntil;
        internal bool IsOverview => zoom <= 1.05f;
        public bool IsMapVisible => IsCampusVisible;
        private bool IsCampusVisible => school != null && (detail != null ? detail.ShowsMapBackdrop(school) :
            school.IsAuthenticated && !school.IsSchoolPageVisible);
        public static Rect Viewport => new Rect(0, 0, Screen.width, Screen.height);
        private static Rect FitArea
        {
            get
            {
                var area = CampusScreenLayout.CampusArea;
                float inset = 10 * CampusScreenLayout.Scale;
                return new Rect(area.x + inset, area.y + inset, Mathf.Max(1, area.width - 2 * inset),
                    Mathf.Max(1, area.height - 2 * inset));
            }
        }
        internal static Rect ZoomControlsRect
        {
            get
            {
                var area = CampusScreenLayout.CampusArea;
                float scale = Mathf.Min(CampusScreenLayout.Scale, area.height / 170);
                return new Rect(area.xMax - 64 * scale, area.yMax - 156 * scale, 52 * scale, 146 * scale);
            }
        }

        internal static Rect AttributionRect
        {
            get
            {
                var area = CampusScreenLayout.CampusArea;
                float scale = CampusScreenLayout.Scale;
                return new Rect(area.x + 12 * scale, area.yMax - 27 * scale,
                    Mathf.Max(1, Mathf.Min(180 * scale, area.width - 24 * scale)), 23 * scale);
            }
        }

        // Coordinates use the source image's top-left corner, independent of screen size.
        public Vector2 ImageToScreen(Vector2 normalized)
        {
            return FitArea.center + pan + Vector2.Scale(normalized - Vector2.one * .5f, ImageSize);
        }

        private void Awake()
        {
            school = GetComponent<SmartleadWebView>();
            detail = GetComponent<NotificationDetailUI>();
            map = Resources.Load<Texture2D>("CampusMap");
        }

        private Vector2 ImageSize
        {
            get
            {
                if (map == null) return Vector2.one;
                var area = FitArea;
                var view = Viewport;
                float cover = Mathf.Max(view.width / map.width, view.height / map.height);
                float campusFit = Mathf.Min(area.width / (map.width * CampusFootprint.x),
                    area.height / (map.height * CampusFootprint.y));
                return new Vector2(map.width, map.height) * Mathf.Min(cover, campusFit) * zoom;
            }
        }

        private void ClampPan()
        {
            var size = ImageSize;
            var center = FitArea.center;
            var view = Viewport;
            pan.x = ClampPanAxis(pan.x, center.x, size.x, view.width);
            pan.y = ClampPanAxis(pan.y, center.y, size.y, view.height);
        }

        private static float ClampPanAxis(float offset, float center, float imageSize, float viewSize)
        {
            float halfImage = imageSize * .5f;
            if (imageSize <= viewSize)
                return Mathf.Clamp(0, halfImage - center, viewSize - center - halfImage);
            return Mathf.Clamp(offset, viewSize - center - halfImage, halfImage - center);
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private void InitializeView()
        {
            float storedZoom = PlayerPrefs.GetFloat(ViewKey + "Zoom", 0);
            float x = PlayerPrefs.GetFloat(ViewKey + "PanX", 0);
            float y = PlayerPrefs.GetFloat(ViewKey + "PanY", 0);
            if (IsFinite(storedZoom) && storedZoom >= 1f && storedZoom <= 8f && IsFinite(x) && IsFinite(y))
            {
                zoom = storedZoom;
                var size = ImageSize;
                pan = new Vector2(Mathf.Clamp(x, -.5f, .5f) * size.x, Mathf.Clamp(y, -.5f, .5f) * size.y);
            }
            initialized = true;
            lastImageSize = ImageSize;
            ClampPan();
            SaveView();
        }

        internal void ShowEntireMap()
        {
            if (map == null) return;
            if (!initialized) InitializeView();
            ResetGesture();
            zoom = 1f;
            pan = Vector2.zero;
            lastImageSize = ImageSize;
            SaveView();
        }

        private void SaveView()
        {
            if (!initialized || map == null) return;
            var size = ImageSize;
            if (size.x <= 0 || size.y <= 0) return;
            var normalized = new Vector2(pan.x / size.x, pan.y / size.y);
            if (savedZoom == zoom && (savedPan - normalized).sqrMagnitude < .00000001f) return;
            PlayerPrefs.SetFloat(ViewKey + "Zoom", zoom);
            PlayerPrefs.SetFloat(ViewKey + "PanX", normalized.x);
            PlayerPrefs.SetFloat(ViewKey + "PanY", normalized.y);
            PlayerPrefs.Save();
            savedZoom = zoom;
            savedPan = normalized;
            viewDirty = false;
        }

        private void LateUpdate()
        {
            if (!initialized) return;
            // Store image-relative offsets so the same place survives a change in resolution.
            var size = ImageSize;
            if (lastImageSize.x > 0 && lastImageSize.y > 0 && size != lastImageSize)
            {
                pan = new Vector2(pan.x * size.x / lastImageSize.x, pan.y * size.y / lastImageSize.y);
                ClampPan();
            }
            lastImageSize = size;
            var normalized = new Vector2(pan.x / size.x, pan.y / size.y);
            bool changed = savedZoom != zoom || (savedPan - normalized).sqrMagnitude >= .00000001f;
            if (!changed) { viewDirty = false; return; }
            if (dragging || pinching || !viewDirty) saveAfter = Time.unscaledTime + .5f;
            viewDirty = true;
            if (Time.unscaledTime >= saveAfter) SaveView();
        }

        private void ZoomAt(float factor, Vector2 position)
        {
            float next = Mathf.Clamp(zoom * factor, 1f, 8f);
            var anchor = position - FitArea.center;
            pan = anchor - (anchor - pan) * (next / zoom);
            zoom = next;
            ClampPan();
            lastImageSize = ImageSize;
        }

        private static bool CanStart(Vector2 point) => Viewport.Contains(point) &&
            !CampusScreenLayout.NavigationArea.Contains(point) && !ZoomControlsRect.Contains(point) && !AttributionRect.Contains(point);
        private void TrackDrag(Vector2 point)
        {
            if ((point - gestureOrigin).sqrMagnitude > Mathf.Pow(8f * CampusScreenLayout.Scale, 2))
                suppressMarkerClicksUntil = Time.frameCount + 2;
        }
        private static Vector2 GuiPoint(Vector2 point) => new Vector2(point.x, Screen.height - point.y);
        private void ResetGesture() { dragging = pinching = false; fingerId = -1; }
        private void OnDisable() { SaveView(); ResetGesture(); }
        private void OnApplicationFocus(bool focused) { if (!focused) { SaveView(); ResetGesture(); } }
        private void OnApplicationPause(bool paused) { if (paused) { SaveView(); ResetGesture(); } }
        private void OnApplicationQuit() { SaveView(); }

        private void Update()
        {
            if (!IsMapVisible || map == null)
            { ResetGesture(); return; }
            if (!initialized) InitializeView();
            if (detail != null && (detail.IsNotificationPanelOpen || detail.SuppressCampusInput))
            { ResetGesture(); return; }
            ClampPan();
            if (Input.touchCount >= 2)
            {
                suppressMarkerClicksUntil = Time.frameCount + 2;
                var a = Input.GetTouch(0);
                var b = Input.GetTouch(1);
                var first = GuiPoint(a.position);
                var second = GuiPoint(b.position);
                if (!pinching && (!CanStart(first) || !CanStart(second))) { ResetGesture(); return; }
                dragging = false; fingerId = -1;
                if (pinching && a.phase != TouchPhase.Began && b.phase != TouchPhase.Began)
                {
                    var oldFirst = GuiPoint(a.position - a.deltaPosition);
                    var oldSecond = GuiPoint(b.position - b.deltaPosition);
                    var oldCenter = (oldFirst + oldSecond) * .5f;
                    var center = (first + second) * .5f;
                    float distance = Vector2.Distance(oldFirst, oldSecond);
                    if (distance > 1f) ZoomAt(Vector2.Distance(first, second) / distance, oldCenter);
                    pan += center - oldCenter;
                    ClampPan();
                }
                pinching = true;
                return;
            }
            if (Input.touchCount == 1)
            {
                var touch = Input.GetTouch(0);
                var point = GuiPoint(touch.position);
                if (touch.phase == TouchPhase.Began || pinching)
                {
                    dragging = CanStart(point); fingerId = touch.fingerId;
                    previousPointer = gestureOrigin = point; pinching = false;
                }
                if (dragging && fingerId == touch.fingerId)
                {
                    TrackDrag(point);
                    pan += point - previousPointer;
                    previousPointer = point;
                    ClampPan();
                }
                if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled) ResetGesture();
                return;
            }
            // Do not turn the last touch into a synthetic mouse drag.
            if (fingerId >= 0 || pinching) { ResetGesture(); return; }
            var mouse = GuiPoint(Input.mousePosition);
            if (Input.GetMouseButtonDown(0)) { dragging = CanStart(mouse); previousPointer = gestureOrigin = mouse; }
            if (dragging && Input.GetMouseButton(0))
            {
                TrackDrag(mouse);
                pan += mouse - previousPointer;
                previousPointer = mouse;
                ClampPan();
            }
            if (!Input.GetMouseButton(0)) dragging = false;
            if (CanStart(mouse) && Mathf.Abs(Input.mouseScrollDelta.y) > .001f)
                ZoomAt(Mathf.Pow(1.15f, Input.mouseScrollDelta.y), mouse);
        }

        private void OnGUI()
        {
            if (!IsCampusVisible) return;
            var previous = CampusTheme.Enter();
            int depth = GUI.depth;
            GUI.depth = 10;
            try { DrawMap(); }
            finally { GUI.depth = depth; GUI.skin = previous; }
        }

        private void DrawMap()
        {
            var area = Viewport;
            CampusTheme.Fill(area, CampusTheme.MapGround);
            if (map == null)
            {
                GUI.Box(area, "캠퍼스 지도를 불러올 수 없습니다.");
                return;
            }
            ClampPan();
            GUI.BeginGroup(area);
            var size = ImageSize;
            var origin = FitArea.center - area.position + pan - size * .5f;
            if (Event.current.type == EventType.Repaint)
            {
                bool dimmed = detail != null && detail.IsNotificationPanelOpen;
                bool hasMargins = size.x < area.width - 1 || size.y < area.height - 1;
                var blur = dimmed || hasMargins ? GetBlurredMap() : null;
                if (hasMargins && blur != null)
                {
                    var backdrop = new Rect(Vector2.zero, area.size);
                    GUI.DrawTexture(backdrop, blur, ScaleMode.ScaleAndCrop, false);
                    CampusTheme.Fill(backdrop, new Color(.94f, .95f, .93f, .58f));
                }
                GUI.DrawTexture(new Rect(origin, size), dimmed && blur != null ? (Texture)blur : map,
                    ScaleMode.StretchToFill, true);
            }
            GUI.EndGroup();

            GetComponent<SmartleadCourseCubes>()?.DrawMapOverlay(this);
            if (detail == null || !detail.IsNotificationPanelOpen)
            {
                DrawZoomControls();
                DrawAttribution();
            }
        }

        private void DrawAttribution()
        {
            if (attributionStyle == null)
                attributionStyle = new GUIStyle(GUI.skin.box)
                {
                    alignment = TextAnchor.MiddleCenter, wordWrap = false, padding = new RectOffset(4, 4, 0, 0)
                };
            attributionStyle.fontSize = Mathf.RoundToInt(10 * CampusScreenLayout.Scale);
            if (GUI.Button(AttributionRect, MapAttribution, attributionStyle) && CanSelectCourse)
                Application.OpenURL(AttributionUrl);
        }

        private void DrawZoomControls()
        {
            var panel = ZoomControlsRect;
            float scale = panel.width / 52;
            GUI.Box(panel, GUIContent.none, CampusTheme.GlassNavigation);
            var button = new Rect(panel.x + 4 * scale, panel.y + 4 * scale, 44 * scale, 44 * scale);
            bool enabled = GUI.enabled;
            GUI.enabled = enabled && zoom < 8f;
            if (GUI.Button(button, new GUIContent("+", "지도 확대"), CampusTheme.NavigationSelected))
                ZoomAt(1.6f, FitArea.center);
            button.y += 47 * scale;
            GUI.enabled = enabled && zoom > 1f;
            if (GUI.Button(button, new GUIContent("−", "지도 축소"), CampusTheme.NavigationSelected))
                ZoomAt(1f / 1.6f, FitArea.center);
            button.y += 47 * scale;
            GUI.enabled = enabled;
            if (GUI.Button(button, new GUIContent("전체", "전체 캠퍼스 보기"), CampusTheme.Navigation))
                ShowEntireMap();
        }

        private RenderTexture GetBlurredMap()
        {
            if (blurUnavailable) return null;
            if (blurredMap != null && blurredMap.IsCreated()) return blurredMap;
            if (blurMaterial == null)
            {
                var shader = Resources.Load<Shader>("CampusMapBlur");
                if (shader == null || !shader.isSupported) { blurUnavailable = true; return null; }
                blurMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            int width = Mathf.Min(map.width, 720);
            int height = Mathf.Max(1, Mathf.RoundToInt((float)map.height / map.width * width));
            if (blurredMap == null)
                blurredMap = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
                {
                    name = "Campus Map Backdrop",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave
                };
            if (!blurredMap.Create()) { blurUnavailable = true; return null; }

            var active = RenderTexture.active;
            var horizontal = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            try
            {
                horizontal.filterMode = FilterMode.Bilinear;
                horizontal.wrapMode = TextureWrapMode.Clamp;
                blurMaterial.SetVector(BlurStep, new Vector4(1.7f / width, 0, 0, 0));
                Graphics.Blit(map, horizontal, blurMaterial);
                blurMaterial.SetVector(BlurStep, new Vector4(0, 1.7f / height, 0, 0));
                Graphics.Blit(horizontal, blurredMap, blurMaterial);
            }
            finally
            {
                RenderTexture.active = active;
                RenderTexture.ReleaseTemporary(horizontal);
            }
            return blurredMap;
        }

        private void OnDestroy()
        {
            if (blurredMap != null) { blurredMap.Release(); Destroy(blurredMap); }
            if (blurMaterial != null) Destroy(blurMaterial);
        }
    }
}

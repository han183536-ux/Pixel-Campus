using UnityEngine;

namespace WebNotifications
{
    /// <summary>Offline campus image with pointer-anchored zoom and bounded panning.</summary>
    public sealed class CampusMapView : MonoBehaviour
    {
        private const string ViewKey = "PixelCampus.MapView.";
        private SmartleadWebView school;
        private NotificationDetailUI detail;
        private Texture2D map;
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
        public bool IsMapVisible => IsCampusVisible;
        private bool IsCampusVisible => school != null && school.IsAuthenticated && !school.IsSchoolPageVisible &&
            (detail == null || detail.IsMapTabActive);
        public static Rect Viewport => CampusScreenLayout.CampusArea;

        // Coordinates use the source image's top-left corner, independent of screen size.
        public Vector2 ImageToScreen(Vector2 normalized)
        {
            return Viewport.center + pan + Vector2.Scale(normalized - Vector2.one * .5f, ImageSize);
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
                var area = Viewport;
                float fit = Mathf.Min(area.width / map.width, area.height / map.height);
                return new Vector2(map.width, map.height) * fit * zoom;
            }
        }

        private void ClampPan()
        {
            var excess = Vector2.Max(Vector2.zero, (ImageSize - Viewport.size) * .5f);
            pan.x = Mathf.Clamp(pan.x, -excess.x, excess.x);
            pan.y = Mathf.Clamp(pan.y, -excess.y, excess.y);
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private void InitializeView()
        {
            var fitSize = ImageSize;
            float storedZoom = PlayerPrefs.GetFloat(ViewKey + "Zoom", 0);
            float x = PlayerPrefs.GetFloat(ViewKey + "PanX", 0);
            float y = PlayerPrefs.GetFloat(ViewKey + "PanY", 0);
            if (IsFinite(storedZoom) && storedZoom >= 1f && storedZoom <= 8f && IsFinite(x) && IsFinite(y))
            {
                zoom = storedZoom;
                var size = ImageSize;
                pan = new Vector2(Mathf.Clamp(x, -.5f, .5f) * size.x, Mathf.Clamp(y, -.5f, .5f) * size.y);
            }
            else
            {
                // Fill the viewport only on the first visit; later visits restore the saved view.
                zoom = Mathf.Clamp(Mathf.Max(Viewport.width / fitSize.x, Viewport.height / fitSize.y), 1f, 8f);
            }
            initialized = true;
            lastImageSize = ImageSize;
            ClampPan();
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
            var anchor = position - Viewport.center;
            pan = anchor - (anchor - pan) * (next / zoom);
            zoom = next;
            ClampPan();
            lastImageSize = ImageSize;
        }

        private static bool CanStart(Vector2 point) => Viewport.Contains(point);
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
            if (!IsMapVisible || map == null || detail != null && (detail.IsNotificationPanelOpen || detail.SuppressCampusInput))
            { ResetGesture(); return; }
            if (!initialized)
            {
                InitializeView();
            }
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
            if (!IsCampusVisible || detail != null && detail.IsNotificationPanelOpen) return;
            var previous = CampusTheme.Enter();
            try { DrawMap(); }
            finally { GUI.skin = previous; }
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
            var origin = area.size * .5f + pan - size * .5f;
            GUI.DrawTexture(new Rect(origin, size), map, ScaleMode.StretchToFill, true);
            GUI.EndGroup();

            GetComponent<SmartleadCourseCubes>()?.DrawMapOverlay(this);
        }
    }
}

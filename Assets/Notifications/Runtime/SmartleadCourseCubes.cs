using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WebNotifications
{
    // Local classroom notes are independent of the SmartLEAD authentication session.
    internal static class CourseClassrooms
    {
        [Serializable] private class Catalog { public string term; public Entry[] entries; }
        [Serializable] private class Entry { public string code, room, note; }
        private static Catalog catalog;
        public static string Term
        {
            get
            {
                var now = DateTime.UtcNow.AddHours(9);
                return (now.Month <= 2 ? now.Year - 1 : now.Year) + "-" + (now.Month >= 3 && now.Month <= 8 ? "1" : "2");
            }
        }
        private static string PreferenceKey(string code) => "PixelCampus.Classroom." + Term + "." + code;
        private static Entry Find(string code)
        {
            if (catalog == null)
            {
                var asset = Resources.Load<TextAsset>("CourseClassrooms");
                catalog = asset != null ? JsonUtility.FromJson<Catalog>(asset.text) : new Catalog();
            }
            return catalog.term == Term && catalog.entries != null ? Array.Find(catalog.entries, entry => entry.code == code) : null;
        }
        public static string Room(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return "";
            return PlayerPrefs.HasKey(PreferenceKey(code)) ? PlayerPrefs.GetString(PreferenceKey(code)) : Find(code)?.room ?? "";
        }
        public static string Label(string code)
        {
            var room = Room(code);
            return string.IsNullOrWhiteSpace(room) ? "강의실 미확인" : "강의실 " + room;
        }
        public static string Source(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return "과목·분반을 확인해야 강의실을 저장할 수 있습니다.";
            if (PlayerPrefs.HasKey(PreferenceKey(code))) return Term + "학기 · 직접 입력 · 이 기기에 저장됨";
            return Find(code)?.note ?? "수강신청 시간표에서 확인한 강의실을 직접 입력할 수 있습니다.";
        }
        public static void Save(string code, string room)
        {
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(room)) return;
            PlayerPrefs.SetString(PreferenceKey(code), room.Trim());
            PlayerPrefs.Save();
        }
        public static void Reset(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return;
            PlayerPrefs.DeleteKey(PreferenceKey(code));
            PlayerPrefs.Save();
        }
    }

    public sealed class CourseCubeNotice
    {
        public int courseId, notifications, messages, pendingAssignments, unknownAssignments, pendingLectures, unknownLectures;
        public long nextDue;
        public bool assignmentsKnown, assignmentsFailed, notificationsKnown, lecturesKnown, lecturesFailed;
        public string courseName, courseCode, nextTaskTitle;
        public readonly List<CampusScreenLayout.DeadlinePeriod> deadlines = new List<CampusScreenLayout.DeadlinePeriod>();
        public Color DeadlineColor => CampusScreenLayout.DeadlineColor(
            CampusScreenLayout.CourseStage(deadlines, DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        public string DeadlineStatus => deadlines.Count == 0 ? "" : CampusScreenLayout.StageLabel(
            CampusScreenLayout.CourseStage(deadlines, DateTimeOffset.UtcNow.ToUnixTimeSeconds())) + "\n";
        public string Key => CourseKey(courseId, courseName, courseCode);
        public static string CourseKey(int id, string name, string code)
        {
            if (!string.IsNullOrWhiteSpace(code)) return "code:" + code.Trim();
            if (id > 0) return "id:" + id;
            return string.IsNullOrWhiteSpace(name) ? null : "name:" + name.Trim();
        }
        public string Summary
        {
            get
            {
                if (pendingAssignments + unknownAssignments + pendingLectures > 0)
                    return (pendingAssignments > 0 ? "미제출 과제 " + pendingAssignments : unknownAssignments > 0 ? "제출 확인 필요 " + unknownAssignments : "동영상 출석 " + pendingLectures) +
                        (pendingAssignments > 0 && unknownAssignments > 0 ? " · 확인 필요 " + unknownAssignments : "") +
                        (pendingAssignments + unknownAssignments > 0 && pendingLectures > 0 ? " · 동영상 " + pendingLectures : "") +
                        "\n" + (string.IsNullOrWhiteSpace(nextTaskTitle) ? "" : nextTaskTitle.Length > 18 ? nextTaskTitle.Substring(0, 18) + "…\n" : nextTaskTitle + "\n") +
                        (nextDue <= 0 && pendingLectures > 0 ? "출석 기간 확인 필요" : CampusScreenLayout.RemainingTime(nextDue)) +
                        (notifications + messages > 0 ? " · 알림 " + (notifications + messages) : "");
                if (!assignmentsKnown) return assignmentsFailed ? "과제 확인 필요" : "과제 확인 중…";
                if (notifications + messages > 0) return "알림 " + (notifications + messages) + "건";
                if (!lecturesKnown) return lecturesFailed ? "동영상 출석 확인 필요" : "동영상 출석 확인 중…";
                return notificationsKnown ? "알림 없음" : "알림 확인 중…";
            }
        }
    }

    public class SmartleadCourseCubes : MonoBehaviour
    {
        private sealed class Course
        {
            public CourseCubeNotice data;
            public GameObject cube;
            public Material material;
            public string building;
            public Vector2 mapPosition;
            public RenderTexture preview;
            public Color previewColor;
        }
        private readonly Dictionary<string, Course> courses = new Dictionary<string, Course>(StringComparer.Ordinal);
        private readonly List<Course> ordered = new List<Course>();
        private GameObject cubeRoot;
        private bool visible = true, dragging;
        private Vector2 scroll, touchStart;
        private float touchScroll, contentHeight;
        private int touchId = -1, suppressListClicksUntil = -1;
        public int CourseCount => ordered.Count;
        private Camera previewCamera;
        private bool listOpen;
        private GUIStyle mapLabel, mapButton;
        private static string Compact(string value) => (value ?? "").Replace(" ", "").ToLowerInvariant();
        private static void AssignBuilding(Course course)
        {
            string name = Compact(course.data.courseName);
            string code = course.data.courseCode ?? "";
            if (name.Contains("게임프로그래밍") || name.Contains("3차원모델링") || name.Contains("3d모델링") ||
                name.Contains("캡스톤") || code.StartsWith("603103-") || code.StartsWith("601104-") || code.StartsWith("603147-"))
            { course.building = "공학관"; course.mapPosition = new Vector2(.175f, .35f); }
            else if (name.Contains("커리어잉글") || name.Contains("careerenglish") || code.StartsWith("903180-"))
            { course.building = "기초교육관"; course.mapPosition = new Vector2(.445f, .035f); }
            else if (name.Contains("일본문화콘텐츠") || code.StartsWith("903190-"))
            { course.building = "자연과학관"; course.mapPosition = new Vector2(.345f, .445f); }
            else course.building = null;
        }

        public void SetOverview(IEnumerable<CourseCubeNotice> items)
        {
            var wanted = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in items)
            {
                if (item?.Key == null || !wanted.Add(item.Key)) continue;
                if (!courses.TryGetValue(item.Key, out var course))
                {
                    course = new Course();
                    courses.Add(item.Key, course);
                    CreateCube(course, item.Key);
                }
                course.data = item;
                AssignBuilding(course);
                course.cube.name = "SmartLEAD Course · " + item.courseName;
            }
            var removed = new List<string>();
            foreach (var entry in courses) if (!wanted.Contains(entry.Key)) removed.Add(entry.Key);
            foreach (var key in removed) { ReleaseCourse(courses[key]); courses.Remove(key); }
            ordered.Clear(); ordered.AddRange(courses.Values);
            ordered.Sort((a, b) => {
                int taskOrder = (b.data.pendingAssignments + b.data.unknownAssignments + b.data.pendingLectures > 0).CompareTo(a.data.pendingAssignments + a.data.unknownAssignments + a.data.pendingLectures > 0);
                if (taskOrder != 0) return taskOrder;
                if (a.data.pendingAssignments + a.data.unknownAssignments + a.data.pendingLectures > 0)
                {
                    int deadline = CampusScreenLayout.CompareDeadlines(a.data.nextDue, b.data.nextDue);
                    if (deadline != 0) return deadline;
                }
                return StringComparer.Ordinal.Compare(a.data.courseName, b.data.courseName);
            });
        }
        public void Clear() { SetOverview(Array.Empty<CourseCubeNotice>()); scroll = Vector2.zero; listOpen = false; }
        public void SetVisible(bool value) { visible = value; if (cubeRoot != null) cubeRoot.SetActive(value); }

        private void CreateCube(Course course, string key)
        {
            if (cubeRoot == null) { cubeRoot = new GameObject("SmartLEAD Course Cubes"); cubeRoot.transform.SetParent(transform, false); cubeRoot.SetActive(visible); }
            course.cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            course.cube.transform.SetParent(cubeRoot.transform, false);
            var collider = course.cube.GetComponent<Collider>();
            if (collider != null) { collider.enabled = false; Release(collider); }
            course.material = new Material(Shader.Find("Standard") ?? Shader.Find("Unlit/Color")) { color = Color.white };
            var renderer = course.cube.GetComponent<Renderer>();
            renderer.sharedMaterial = course.material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            course.cube.SetActive(false);
        }

        private void EnsureMapPreview(Course course)
        {
            int pixels = Mathf.Clamp(Mathf.CeilToInt(64 * CampusScreenLayout.Scale), 64, 256);
            Color color = course.data.DeadlineColor;
            if (course.preview != null && course.preview.IsCreated() && course.preview.width == pixels && course.previewColor == color) return;
            if (previewCamera == null)
            {
                var cameraObject = new GameObject("Course Cube Preview Camera");
                cameraObject.transform.SetParent(transform, false);
                previewCamera = cameraObject.AddComponent<Camera>();
                previewCamera.enabled = false;
                previewCamera.orthographic = true;
                previewCamera.orthographicSize = .95f;
                previewCamera.nearClipPlane = .1f;
                previewCamera.farClipPlane = 20;
                previewCamera.clearFlags = CameraClearFlags.SolidColor;
                previewCamera.backgroundColor = Color.clear;
                previewCamera.cullingMask = 1 << 31;
                previewCamera.transform.position = new Vector3(10000, 10000, -8);
                var lightObject = new GameObject("Course Cube Preview Light");
                lightObject.transform.SetParent(cameraObject.transform, false);
                lightObject.transform.rotation = Quaternion.Euler(35, -35, 0);
                var light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
                light.cullingMask = 1 << 31;
                light.intensity = 1.2f;
            }
            if (course.preview == null || course.preview.width != pixels)
            {
                ReleasePreview(course);
                course.preview = new RenderTexture(pixels, pixels, 16, RenderTextureFormat.ARGB32) { name = "Course Cube Icon" };
                course.preview.Create();
            }
            course.material.color = color;
            if (!course.preview.IsCreated()) course.preview.Create();
            course.cube.layer = 31;
            course.cube.transform.position = new Vector3(10000, 10000, 0);
            course.cube.transform.localScale = Vector3.one;
            course.cube.transform.rotation = Quaternion.Euler(20, 32, 0);
            var previous = RenderTexture.active;
            previewCamera.targetTexture = course.preview;
            course.cube.SetActive(true);
            try { previewCamera.Render(); }
            finally
            {
                course.cube.SetActive(false);
                previewCamera.targetTexture = null;
                RenderTexture.active = previous;
            }
            course.previewColor = color;
        }

        private Rect ListToggle
        {
            get { var area = CampusMapView.Viewport; float s = CampusScreenLayout.Scale;
                return new Rect(area.x + 12 * s, area.yMax - 86 * s, 124 * s, 36 * s); }
        }
        private Rect ListPanel
        {
            get { var area = CampusMapView.Viewport; float s = CampusScreenLayout.Scale;
                float height = Mathf.Min(area.height * .65f, 460 * s);
                return new Rect(area.x + 12 * s, area.yMax - 94 * s - height, area.width - 24 * s, height); }
        }
        public bool BlocksMapPointer(Vector2 point) => visible &&
            (ListToggle.Contains(point) || listOpen && ListPanel.Contains(point));

        public void DrawMapOverlay(CampusMapView map)
        {
            var detail = GetComponent<NotificationDetailUI>();
            if (!visible || !map.IsMapVisible || detail != null && detail.IsNotificationPanelOpen) return;
            float s = CampusScreenLayout.Scale;
            if (mapLabel == null)
            {
                mapLabel = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperCenter, wordWrap = true, richText = false };
                mapButton = new GUIStyle(GUI.skin.button) { wordWrap = true, richText = false };
            }
            mapLabel.fontSize = Mathf.RoundToInt(11 * s);
            mapButton.fontSize = Mathf.RoundToInt(12 * s);
            var area = CampusMapView.Viewport;
            bool allowClick = map.CanSelectCourse && (detail == null || !detail.SuppressCampusInput);
            bool overlayPointer = BlocksMapPointer(Event.current.mousePosition) || map.IsControlPointer(Event.current.mousePosition);
            // Stable slots prevent deadline reordering from moving subjects between markers.
            var mapCourses = new List<Course>(ordered);
            mapCourses.Sort((a, b) => StringComparer.Ordinal.Compare(a.data.Key, b.data.Key));
            var occupied = new List<Rect>();
            GUI.BeginGroup(area);
            foreach (var course in mapCourses)
            {
                if (course.building == null) continue;
                int slot = 0, count = 0;
                foreach (var other in mapCourses)
                    if (other.building == course.building) { if (other == course) slot = count; count++; }
                var anchor = map.ImageToScreen(course.mapPosition) - area.position;
                if (!new Rect(Vector2.zero, area.size).Contains(anchor)) continue;
                float groupHalfWidth = (count * 108 - 6) * s * .5f;
                float centerX = area.width >= groupHalfWidth * 2
                    ? Mathf.Clamp(anchor.x, groupHalfWidth, area.width - groupHalfWidth) : anchor.x;
                var point = new Vector2(centerX + (slot - (count - 1) * .5f) * 108 * s, anchor.y);
                var tile = new Rect(point.x - 51 * s, point.y - 36 * s, 102 * s, 94 * s);
                // At overview zoom, neighboring buildings are closer than readable cards.
                // Keep a connector to the true building while separating overlapping cards.
                foreach (var placed in occupied)
                    if (tile.Overlaps(placed)) { tile.y = placed.yMax + 6 * s; }
                occupied.Add(tile);
                point.y = tile.y + 36 * s;
                if (!new Rect(point.x - 23 * s, tile.y, 46 * s, 46 * s).Overlaps(new Rect(Vector2.zero, area.size))) continue;
                var previousMatrix = GUI.matrix;
                var previousColor = GUI.color;
                var end = new Vector2(point.x, tile.y + 23 * s);
                var delta = end - anchor;
                GUI.color = new Color32(25, 62, 94, 210);
                GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, anchor);
                GUI.DrawTexture(new Rect(anchor.x, anchor.y, delta.magnitude, 2 * s), Texture2D.whiteTexture);
                GUI.matrix = previousMatrix;
                GUI.color = previousColor;
                if (Event.current.type == EventType.Repaint) EnsureMapPreview(course);
                if (course.preview != null)
                    GUI.DrawTexture(new Rect(point.x - 23 * s, tile.y, 46 * s, 46 * s), course.preview);
                string title = string.IsNullOrWhiteSpace(course.data.courseName) ? "과목 " + course.data.courseId : course.data.courseName;
                GUI.Box(new Rect(tile.x, tile.y + 44 * s, tile.width, 50 * s), title + "\n" + course.data.DeadlineStatus.Trim(), mapLabel);
                CampusScreenLayout.DeadlineStripe(new Rect(tile.x, tile.y + 44 * s, tile.width, 3 * s), course.data.DeadlineColor);
                bool enabled = GUI.enabled;
                GUI.enabled = enabled && allowClick && !overlayPointer;
                if (GUI.Button(tile, GUIContent.none, GUIStyle.none)) detail?.OpenCourseNotifications(course.data.Key, title);
                GUI.enabled = enabled;
            }
            GUI.EndGroup();
            if (GUI.Button(ListToggle, listOpen ? "과목 목록 닫기" : "전체 과목 · " + ordered.Count, mapButton))
            { listOpen = !listOpen; scroll = Vector2.zero; }
            if (!listOpen) return;
            var panel = ListPanel;
            GUI.Box(panel, GUIContent.none);
            var listArea = new Rect(panel.x + 8 * s, panel.y + 8 * s, panel.width - 16 * s, panel.height - 16 * s);
            var school = GetComponent<SmartleadWebView>();
            float agendaTop = ordered.Count * 92 * s + 8 * s;
            contentHeight = agendaTop + (school != null ? school.AgendaHeight : 0);
            scroll = GUI.BeginScrollView(listArea, scroll, new Rect(0, 0, listArea.width - 18 * s, contentHeight));
            bool wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && allowClick && !dragging && Time.frameCount > suppressListClicksUntil;
            for (int index = 0; index < ordered.Count; index++)
            {
                var course = ordered[index];
                var tile = new Rect(0, index * 92 * s, listArea.width - 18 * s, 86 * s);
                if (GUI.Button(tile, course.data.courseName + " · " + (course.building ?? "건물 미지정") + "\n" + course.data.Summary, mapButton))
                    detail?.OpenCourseNotifications(course.data.Key, course.data.courseName);
                CampusScreenLayout.DeadlineStripe(new Rect(tile.x, tile.y, 3 * s, tile.height), course.data.DeadlineColor);
            }
            if (school != null) school.DrawUpcomingAgenda(new Rect(0, agendaTop, listArea.width - 18 * s, school.AgendaHeight));
            GUI.enabled = wasEnabled;
            GUI.EndScrollView();
        }
        private void Update()
        {
            var map = GetComponent<CampusMapView>();
            var detail = GetComponent<NotificationDetailUI>();
            if (!visible || map == null || !map.IsMapVisible || !listOpen ||
                detail != null && (detail.IsNotificationPanelOpen || detail.SuppressCampusInput) || Input.touchCount != 1)
            { touchId = -1; dragging = false; return; }
            var touch = Input.GetTouch(0);
            var point = new Vector2(touch.position.x, Screen.height - touch.position.y);
            if (touch.phase == TouchPhase.Began && ListPanel.Contains(point))
            { touchId = touch.fingerId; touchStart = point; touchScroll = scroll.y; dragging = false; }
            if (touch.fingerId != touchId) return;
            if ((point - touchStart).sqrMagnitude > Mathf.Pow(8 * CampusScreenLayout.Scale, 2)) dragging = true;
            if (dragging)
            {
                scroll.y = Mathf.Clamp(touchScroll - point.y + touchStart.y, 0,
                    Mathf.Max(0, contentHeight - ListPanel.height + 16 * CampusScreenLayout.Scale));
                suppressListClicksUntil = Time.frameCount + 2;
            }
            if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled) { touchId = -1; dragging = false; }
        }
        private static void Release(UnityEngine.Object value) { if (value == null) return; if (Application.isPlaying) Destroy(value); else DestroyImmediate(value); }
        private static void ReleasePreview(Course course)
        { if (course.preview != null) { course.preview.Release(); Release(course.preview); course.preview = null; } }
        private static void ReleaseCourse(Course course) { ReleasePreview(course); Release(course.cube); Release(course.material); }
        private void OnDestroy()
        {
            foreach (var course in ordered) ReleaseCourse(course);
            if (previewCamera != null) Release(previewCamera.gameObject);
            Release(cubeRoot);
        }
    }
}

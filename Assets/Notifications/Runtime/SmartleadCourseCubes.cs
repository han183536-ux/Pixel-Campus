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
        }
        private readonly Dictionary<string, Course> courses = new Dictionary<string, Course>(StringComparer.Ordinal);
        private readonly List<Course> ordered = new List<Course>();
        private GameObject cubeRoot;
        private Camera mapCamera;
        private GUIStyle label;
        private bool visible = true, dragging;
        private Vector2 scroll, touchStart;
        private float touchScroll, cellWidth, cellHeight, contentHeight, gridHeight;
        private int columns = 3, touchId = -1;
        private float HeaderHeight => 30f * CampusScreenLayout.Scale;
        public int CourseCount => ordered.Count;

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
                course.cube.name = "SmartLEAD Course · " + item.courseName;
            }
            var removed = new List<string>();
            foreach (var entry in courses) if (!wanted.Contains(entry.Key)) removed.Add(entry.Key);
            foreach (var key in removed) { Release(courses[key].cube); Release(courses[key].material); courses.Remove(key); }
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
            Layout();
        }
        public void Clear() { SetOverview(Array.Empty<CourseCubeNotice>()); scroll = Vector2.zero; }
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
        }
        private void Update()
        {
            var detail = GetComponent<NotificationDetailUI>();
            if (!visible || mapCamera == null || detail != null && (detail.IsNotificationPanelOpen || detail.SuppressCampusInput)) return;
            if (Input.touchCount != 1) { touchId = -1; dragging = false; return; }
            var touch = Input.GetTouch(0);
            if (PixelPlayerMovement.BlocksCampusPointer(new Vector2(touch.position.x, Screen.height - touch.position.y)))
            { touchId = -1; dragging = false; return; }
            if (touch.phase == TouchPhase.Began && mapCamera.pixelRect.Contains(touch.position))
            { touchId = touch.fingerId; touchStart = touch.position; touchScroll = scroll.y; dragging = false; }
            if (touch.fingerId != touchId) return;
            if ((touch.position - touchStart).sqrMagnitude > Mathf.Pow(8 * CampusScreenLayout.Scale, 2)) dragging = true;
            if (dragging) scroll.y = Mathf.Clamp(touchScroll + touch.position.y - touchStart.y, 0,
                Mathf.Max(0, contentHeight - mapCamera.pixelHeight + HeaderHeight));
        }
        private void LateUpdate() { Layout(); }
        private void Layout()
        {
            var detail = GetComponent<NotificationDetailUI>();
            mapCamera = detail != null && detail.mapCamera != null ? detail.mapCamera : Camera.main;
            if (mapCamera == null || mapCamera.pixelHeight < 1) return;
            float scale = CampusScreenLayout.Scale;
            columns = Mathf.Clamp(Mathf.FloorToInt(mapCamera.pixelWidth / (120 * scale)), 2, 5);
            cellWidth = (mapCamera.pixelWidth - 18 * scale) / columns;
            cellHeight = 182 * scale;
            gridHeight = Mathf.CeilToInt(ordered.Count / (float)columns) * cellHeight;
            var school = GetComponent<SmartleadWebView>();
            contentHeight = gridHeight + 12 * scale + (school != null ? school.AgendaHeight : 0);
            scroll.y = Mathf.Clamp(scroll.y, 0, Mathf.Max(0, contentHeight - mapCamera.pixelHeight + HeaderHeight));
            var distance = Mathf.Max(mapCamera.nearClipPlane + 4f, 8f);
            var worldPerPixel = mapCamera.orthographic ? 2f * mapCamera.orthographicSize / mapCamera.pixelHeight :
                2f * distance * Mathf.Tan(mapCamera.fieldOfView * Mathf.Deg2Rad * .5f) / mapCamera.pixelHeight;
            for (int index = 0; index < ordered.Count; index++)
            {
                float top = HeaderHeight + index / columns * cellHeight - scroll.y;
                var cube = ordered[index].cube;
                ordered[index].material.color = ordered[index].data.DeadlineColor;
                bool shown = visible && (detail == null || !detail.IsNotificationPanelOpen) && top >= HeaderHeight - 1 && top + 46 * scale < mapCamera.pixelHeight;
                cube.SetActive(shown);
                if (!shown) continue;
                float x = (index % columns + .5f) * cellWidth / mapCamera.pixelWidth;
                float y = 1f - (top + 25 * scale) / mapCamera.pixelHeight;
                cube.transform.position = mapCamera.ViewportToWorldPoint(new Vector3(x, y, distance));
                cube.transform.rotation = mapCamera.transform.rotation * Quaternion.Euler(20f, 32f, 0f);
                cube.transform.localScale = Vector3.one * Mathf.Min(cellWidth * .26f, 34 * scale) * worldPerPixel;
            }
        }
        private void OnGUI()
        {
            if (!visible || mapCamera == null) return;
            var detail = GetComponent<NotificationDetailUI>();
            if (detail != null && (detail.IsNotificationPanelOpen || detail.SuppressCampusInput)) return;
            float scale = CampusScreenLayout.Scale;
            if (label == null) label = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter, wordWrap = true, richText = false };
            label.fontSize = Mathf.RoundToInt(10 * scale);
            var viewport = mapCamera.pixelRect;
            float top = Screen.height - viewport.yMax;
            GUI.Label(new Rect(viewport.x, top + 4 * scale, viewport.width, HeaderHeight), "전체 " + ordered.Count + "과목 · 과제·동영상 마감 우선", label);
            var area = new Rect(viewport.x, top + HeaderHeight, viewport.width, Mathf.Max(1, viewport.height - HeaderHeight));
            bool movementPointer = PixelPlayerMovement.BlocksCampusPointer(Event.current.mousePosition);
            scroll = GUI.BeginScrollView(area, scroll, new Rect(0, 0, columns * cellWidth, Mathf.Max(area.height, contentHeight)));
            bool enabled = GUI.enabled;
            GUI.enabled = enabled && !dragging && !movementPointer;
            for (int index = 0; index < ordered.Count; index++)
            {
                var item = ordered[index].data;
                var tile = new Rect(index % columns * cellWidth + 3 * scale, index / columns * cellHeight, cellWidth - 6 * scale, cellHeight);
                var title = string.IsNullOrWhiteSpace(item.courseName) ? "과목 " + item.courseId : item.courseName;
                GUI.Label(new Rect(tile.x, tile.y + 49 * scale, tile.width, tile.height - 49 * scale),
                    title + "\n" + CourseClassrooms.Label(item.courseCode) + "\n" + item.DeadlineStatus + item.Summary, label);
                if (detail != null && GUI.Button(tile, GUIContent.none, GUIStyle.none)) detail.OpenCourseNotifications(item.Key, title);
            }
            var school = GetComponent<SmartleadWebView>();
            if (school != null) school.DrawUpcomingAgenda(new Rect(8 * scale, gridHeight + 12 * scale,
                columns * cellWidth - 16 * scale, school.AgendaHeight));
            GUI.enabled = enabled;
            GUI.EndScrollView();
        }
        private static void Release(UnityEngine.Object value) { if (value == null) return; if (Application.isPlaying) Destroy(value); else DestroyImmediate(value); }
        private void OnDestroy() { foreach (var course in ordered) Release(course.material); Release(cubeRoot); }
    }
}

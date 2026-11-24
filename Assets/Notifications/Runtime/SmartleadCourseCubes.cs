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
        }
        private readonly Dictionary<string, Course> courses = new Dictionary<string, Course>(StringComparer.Ordinal);
        private readonly List<Course> ordered = new List<Course>();
        private GameObject cubeRoot;
        private bool visible = true;
        public int CourseCount => ordered.Count;
        private GUIStyle mapLabel;
        private static string Compact(string value) => (value ?? "").Replace(" ", "").ToLowerInvariant();
        private static void AssignBuilding(Course course)
        {
            string name = Compact(course.data.courseName);
            string code = course.data.courseCode ?? "";
            // Normalized centers of the OpenStreetMap building footprints in CampusMap.png.
            if (name.Contains("게임프로그래밍") || name.Contains("3차원모델링") || name.Contains("3d모델링") ||
                name.Contains("캡스톤") || code.StartsWith("603103-") || code.StartsWith("601104-") || code.StartsWith("603147-"))
            { course.building = "공학관"; course.mapPosition = new Vector2(.196835f, .474732f); }
            else if (name.Contains("커리어잉글") || name.Contains("careerenglish") || code.StartsWith("903180-"))
            { course.building = "기초교육관"; course.mapPosition = new Vector2(.425858f, .304804f); }
            else if (name.Contains("일본문화콘텐츠") || code.StartsWith("903190-"))
            { course.building = "자연과학관"; course.mapPosition = new Vector2(.314112f, .516375f); }
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
        public void Clear() { SetOverview(Array.Empty<CourseCubeNotice>()); }
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

        public void DrawMapOverlay(CampusMapView map)
        {
            var detail = GetComponent<NotificationDetailUI>();
            if (!visible || !map.IsMapVisible || detail != null && detail.IsNotificationPanelOpen) return;
            float s = CampusScreenLayout.Scale;
            bool compact = map.IsOverview;
            float tileWidth = (compact ? 62 : 86) * s;
            float tileHeight = (compact ? 60 : 92) * s;
            float gap = 8 * s;
            if (mapLabel == null)
            {
                mapLabel = new GUIStyle(GUI.skin.box)
                {
                    alignment = TextAnchor.MiddleCenter, wordWrap = true, padding = new RectOffset(5, 5, 3, 3)
                };
            }
            mapLabel.fontSize = Mathf.RoundToInt(11 * s);
            // Keep marker hit targets in the safe area above the floating navigation.
            var area = CampusScreenLayout.CampusArea;
            var viewport = new Rect(Vector2.zero, area.size);
            var zoomControls = CampusMapView.ZoomControlsRect;
            zoomControls.position -= area.position;
            var attribution = CampusMapView.AttributionRect;
            attribution.position -= area.position;
            bool allowClick = map.CanSelectCourse && (detail == null || !detail.SuppressCampusInput);
            // Stable slots prevent deadline reordering from moving subjects between markers.
            var mapCourses = ordered.FindAll(course => course.building != null);
            mapCourses.Sort((a, b) =>
            {
                int buildingOrder = StringComparer.Ordinal.Compare(a.building, b.building);
                return buildingOrder != 0 ? buildingOrder : StringComparer.Ordinal.Compare(a.data.Key, b.data.Key);
            });
            var markers = new List<(Course course, Rect tile)>();
            var occupied = new List<Rect> { zoomControls, attribution };
            for (int start = 0; start < mapCourses.Count;)
            {
                var building = mapCourses[start];
                int end = start + 1;
                while (end < mapCourses.Count && mapCourses[end].building == building.building) end++;
                var anchor = map.ImageToScreen(building.mapPosition) - area.position;
                if (viewport.Contains(anchor))
                {
                    int count = end - start;
                    int maxColumns = Mathf.Max(1, Mathf.FloorToInt((viewport.width + gap) / (tileWidth + gap)));
                    int maxRows = Mathf.Max(1, Mathf.FloorToInt((viewport.height + gap) / (tileHeight + gap)));
                    int columns = Mathf.Min(count, Mathf.Min(maxColumns, Mathf.Max(2, (count + maxRows - 1) / maxRows)));
                    int rows = (count + columns - 1) / columns;
                    float width = columns * (tileWidth + gap) - gap;
                    float height = rows * (tileHeight + gap) - gap;
                    var preferred = new Rect(anchor.x - width * .5f, anchor.y - height * .5f - 4 * s, width, height);
                    var group = PlaceMarkerGroup(preferred, viewport, occupied, gap);
                    occupied.Add(group);
                    for (int slot = 0; slot < count; slot++)
                    {
                        int row = slot / columns;
                        int rowCount = Mathf.Min(columns, count - row * columns);
                        float rowWidth = rowCount * (tileWidth + gap) - gap;
                        float x = group.center.x - rowWidth * .5f + slot % columns * (tileWidth + gap);
                        var tile = new Rect(x, group.y + row * (tileHeight + gap), tileWidth, tileHeight);
                        if (tile.Overlaps(viewport)) markers.Add((mapCourses[start + slot], tile));
                    }
                }
                start = end;
            }
            GUI.BeginGroup(area);
            foreach (var (course, tile) in markers)
            {
                string title = string.IsNullOrWhiteSpace(course.data.courseName) ? "과목 " + course.data.courseId : course.data.courseName;
                int notices = course.data.notifications + course.data.messages;
                float iconSize = (compact ? 46 : 48) * s;
                var icon = new Rect(tile.center.x - iconSize * .5f, tile.y + 8 * s, iconSize, iconSize);
                CampusTheme.DrawNoticeIcon(icon, notices, s, Color.white, mapMarker: true);
                if (!compact)
                {
                    var labelArea = new Rect(tile.x, tile.y + 59 * s, tile.width, 33 * s);
                    GUI.Box(labelArea, title, mapLabel);
                    if (course.data.DeadlineColor != Color.white)
                        CampusTheme.Fill(new Rect(labelArea.x + 16 * s, labelArea.y, labelArea.width - 32 * s, 2 * s), course.data.DeadlineColor);
                }
                bool enabled = GUI.enabled;
                GUI.enabled = enabled && allowClick && !tile.Overlaps(zoomControls) && !tile.Overlaps(attribution);
                var marker = new GUIContent("", title + " · " + course.data.Summary.Replace("\n", " "));
                if (GUI.Button(tile, marker, GUIStyle.none)) detail?.OpenCourseNotifications(course.data.Key, title);
                GUI.enabled = enabled;
            }
            GUI.EndGroup();
        }

        private static Rect PlaceMarkerGroup(Rect preferred, Rect viewport, List<Rect> occupied, float gap)
        {
            float maxX = Mathf.Max(viewport.xMin, viewport.xMax - preferred.width);
            float maxY = Mathf.Max(viewport.yMin, viewport.yMax - preferred.height);
            Rect Clamp(float x, float y) => new Rect(Mathf.Clamp(x, viewport.xMin, maxX),
                Mathf.Clamp(y, viewport.yMin, maxY), preferred.width, preferred.height);
            bool Overlaps(Rect candidate) => occupied.Exists(rect => candidate.Overlaps(rect));
            var closest = Clamp(preferred.x, preferred.y);
            if (!Overlaps(closest)) return closest;

            // Nearby free positions are bounded by the edges of other groups and map controls.
            var xs = new List<float> { preferred.x };
            var ys = new List<float> { preferred.y };
            foreach (var rect in occupied)
            {
                xs.Add(rect.xMin - preferred.width - gap);
                xs.Add(rect.xMax + gap);
                ys.Add(rect.yMin - preferred.height - gap);
                ys.Add(rect.yMax + gap);
            }
            float distance = float.PositiveInfinity;
            foreach (float x in xs)
            foreach (float y in ys)
            {
                var candidate = Clamp(x, y);
                float squared = (candidate.position - preferred.position).sqrMagnitude;
                if (squared >= distance || Overlaps(candidate)) continue;
                distance = squared;
                closest = candidate;
            }
            return closest;
        }
        private static void Release(UnityEngine.Object value) { if (value == null) return; if (Application.isPlaying) Destroy(value); else DestroyImmediate(value); }
        private static void ReleaseCourse(Course course) { Release(course.cube); Release(course.material); }
        private void OnDestroy()
        {
            foreach (var course in ordered) ReleaseCourse(course);
            Release(cubeRoot);
        }
    }
}

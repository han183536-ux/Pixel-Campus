using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WebNotifications
{
    public sealed class CourseCubeNotice
    {
        public int courseId;
        public string courseName, courseCode;
    }

    // One real 3D cube per identified course in the map camera's viewport.
    public class SmartleadCourseCubes : MonoBehaviour
    {
        private sealed class Course
        {
            public string key, name, code;
            public int notifications, messages;
            public GameObject cube;
            public Material material;
        }
        private static readonly Color[] Palette = {
            new Color(.15f, .78f, .85f), new Color(1f, .57f, .22f),
            new Color(.66f, .43f, .95f), new Color(.35f, .82f, .42f),
            new Color(1f, .38f, .49f), new Color(.32f, .57f, 1f),
            new Color(.97f, .82f, .22f), new Color(.95f, .42f, .78f)
        };
        private readonly List<CourseCubeNotice> notificationItems = new List<CourseCubeNotice>();
        private readonly List<CourseCubeNotice> messageItems = new List<CourseCubeNotice>();
        private readonly Dictionary<string, Course> courses = new Dictionary<string, Course>(StringComparer.Ordinal);
        private readonly Dictionary<string, Color> colors = new Dictionary<string, Color>(StringComparer.Ordinal);
        private readonly List<Course> ordered = new List<Course>();
        private GameObject cubeRoot;
        private Camera mapCamera;
        private GUIStyle label;
        private bool visible = true;
        private int unknownCount, page, columns = 1, rows = 1;
        private float cellWidth, cellHeight;
        private const float HeaderHeight = 46f;
        public int CourseCount => ordered.Count;

        public void SetNotifications(IEnumerable<CourseCubeNotice> items)
        {
            notificationItems.Clear();
            if (items != null) notificationItems.AddRange(items);
            Synchronize();
        }

        public void SetMessages(IEnumerable<CourseCubeNotice> items)
        {
            messageItems.Clear();
            if (items != null) messageItems.AddRange(items);
            Synchronize();
        }

        public void Clear()
        {
            notificationItems.Clear(); messageItems.Clear(); page = 0;
            Synchronize();
        }

        public void SetVisible(bool value)
        {
            visible = value;
            if (cubeRoot != null) cubeRoot.SetActive(value);
        }

        private static string Key(CourseCubeNotice item)
        {
            if (item == null) return null;
            if (!string.IsNullOrWhiteSpace(item.courseCode)) return "code:" + item.courseCode.Trim();
            if (item.courseId > 0) return "id:" + item.courseId;
            if (!string.IsNullOrWhiteSpace(item.courseName)) return "name:" + item.courseName.Trim();
            return null;
        }

        private void Synchronize()
        {
            var wanted = new Dictionary<string, Course>(StringComparer.Ordinal);
            unknownCount = 0;
            AddItems(wanted, notificationItems, false);
            AddItems(wanted, messageItems, true);
            foreach (var existing in courses)
                if (!wanted.ContainsKey(existing.Key))
                {
                    Release(existing.Value.cube);
                    Release(existing.Value.material);
                }
            ordered.Clear();
            foreach (var entry in wanted)
            {
                var course = entry.Value;
                if (courses.TryGetValue(entry.Key, out var previous))
                {
                    course.cube = previous.cube;
                    course.material = previous.material;
                }
                ordered.Add(course);
            }
            ordered.Sort((a, b) => StringComparer.Ordinal.Compare(a.key, b.key));
            courses.Clear();
            foreach (var course in ordered)
            {
                if (course.cube == null) CreateCube(course);
                course.cube.name = "SmartLEAD Course · " + course.name + " " + course.code;
                courses.Add(course.key, course);
            }
            Layout();
        }

        private void AddItems(Dictionary<string, Course> wanted, List<CourseCubeNotice> items, bool isMessage)
        {
            foreach (var item in items)
            {
                var key = Key(item);
                if (key == null) { unknownCount++; continue; }
                if (!wanted.TryGetValue(key, out var course))
                {
                    course = new Course { key = key,
                        name = string.IsNullOrWhiteSpace(item.courseName) ?
                            item.courseId > 0 ? "온라인 강의실 ID " + item.courseId : "과목 " + item.courseCode : item.courseName.Trim(),
                        code = item.courseCode?.Trim() ?? "" };
                    wanted.Add(key, course);
                }
                else if (!string.IsNullOrWhiteSpace(item.courseName)) course.name = item.courseName.Trim();
                if (isMessage) course.messages++; else course.notifications++;
            }
        }

        private void CreateCube(Course course)
        {
            if (cubeRoot == null)
            {
                cubeRoot = new GameObject("SmartLEAD Course Cubes");
                cubeRoot.transform.SetParent(transform, false);
                cubeRoot.SetActive(visible);
            }
            if (!colors.TryGetValue(course.key, out var color))
            {
                var index = colors.Count;
                color = index < Palette.Length ? Palette[index] : Color.HSVToRGB(Mathf.Repeat(index * .618034f, 1f), .65f, .95f);
                colors.Add(course.key, color);
            }
            course.cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            course.cube.transform.SetParent(cubeRoot.transform, false);
            var collider = course.cube.GetComponent<Collider>();
            if (collider != null) { collider.enabled = false; Release(collider); }
            var shader = Shader.Find("Standard") ?? Shader.Find("Unlit/Color");
            course.material = new Material(shader) { color = color };
            if (course.material.HasProperty("_Glossiness")) course.material.SetFloat("_Glossiness", .15f);
            if (course.material.HasProperty("_EmissionColor"))
            {
                course.material.EnableKeyword("_EMISSION");
                course.material.SetColor("_EmissionColor", color * .2f);
            }
            var renderer = course.cube.GetComponent<Renderer>();
            renderer.sharedMaterial = course.material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private void LateUpdate() { Layout(); }

        private void Layout()
        {
            var detail = GetComponent<NotificationDetailUI>();
            mapCamera = detail != null && detail.mapCamera != null ? detail.mapCamera : Camera.main;
            if (mapCamera == null || mapCamera.pixelWidth < 1 || mapCamera.pixelHeight < 1) return;
            columns = Mathf.Clamp(Mathf.FloorToInt(mapCamera.pixelWidth / 190f), 1, 4);
            rows = Mathf.Clamp(Mathf.FloorToInt((mapCamera.pixelHeight - HeaderHeight) / 150f), 1, 3);
            rows = Mathf.Min(rows, Mathf.Max(1, Mathf.CeilToInt(ordered.Count / (float)columns)));
            var perPage = columns * rows;
            page = Mathf.Clamp(page, 0, Mathf.Max(0, Mathf.CeilToInt(ordered.Count / (float)perPage) - 1));
            cellWidth = mapCamera.pixelWidth / (float)columns;
            cellHeight = Mathf.Max(1, (mapCamera.pixelHeight - HeaderHeight - 18f) / rows);
            var distance = Mathf.Max(mapCamera.nearClipPlane + 4f, 8f);
            var worldPerPixel = mapCamera.orthographic ? 2f * mapCamera.orthographicSize / mapCamera.pixelHeight :
                2f * distance * Mathf.Tan(mapCamera.fieldOfView * Mathf.Deg2Rad * .5f) / mapCamera.pixelHeight;
            var size = Mathf.Max(12f, Mathf.Min(cellWidth * .36f, cellHeight * .35f, 100f)) * worldPerPixel;
            for (int index = 0; index < ordered.Count; index++)
            {
                var slot = index - page * perPage;
                var shown = visible && slot >= 0 && slot < perPage;
                var cube = ordered[index].cube;
                cube.SetActive(shown);
                if (!shown) continue;
                float x = (slot % columns + .5f) / columns;
                float y = 1f - (HeaderHeight + (slot / columns + .36f) * cellHeight) / mapCamera.pixelHeight;
                cube.transform.position = mapCamera.ViewportToWorldPoint(new Vector3(x, y, distance));
                cube.transform.rotation = mapCamera.transform.rotation * Quaternion.Euler(20f, 32f, 0f);
                cube.transform.localScale = Vector3.one * size;
            }
        }

        private void OnGUI()
        {
            if (!visible || mapCamera == null || ordered.Count == 0) return;
            if (label == null) label = new GUIStyle(GUI.skin.label) {
                alignment = TextAnchor.UpperCenter, wordWrap = true, richText = false, fontSize = 13
            };
            var viewport = mapCamera.pixelRect;
            float left = viewport.x, top = Screen.height - viewport.yMax;
            GUI.Label(new Rect(left + 8, top + 8, viewport.width - 16, 24), "과목별 알림 · " + ordered.Count + "개 과목", label);
            var perPage = columns * rows;
            var pages = Mathf.CeilToInt(ordered.Count / (float)perPage);
            if (pages > 1)
            {
                if (GUI.Button(new Rect(left + 8, top + 8, 32, 24), "‹")) { page = (page + pages - 1) % pages; Layout(); }
                if (GUI.Button(new Rect(viewport.xMax - 40, top + 8, 32, 24), "›")) { page = (page + 1) % pages; Layout(); }
                GUI.Label(new Rect(left + 8, top + 30, viewport.width - 16, 18), (page + 1) + " / " + pages, label);
            }
            for (int slot = 0; slot < perPage && page * perPage + slot < ordered.Count; slot++)
            {
                var course = ordered[page * perPage + slot];
                var rect = new Rect(left + slot % columns * cellWidth + 6,
                    top + HeaderHeight + (slot / columns + .64f) * cellHeight, cellWidth - 12, cellHeight * .36f);
                var title = course.name + (string.IsNullOrEmpty(course.code) ? "" : "\n" + course.code);
                GUI.Label(rect, title + "\n알림 " + course.notifications + "건 · 쪽지 " + course.messages + "건", label);
            }
            if (unknownCount > 0)
                GUI.Label(new Rect(left + 8, top + viewport.height - 18, viewport.width - 16, 18),
                    "과목 미확인 " + unknownCount + "건", label);
        }

        private static void Release(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }

        private void OnDestroy()
        {
            foreach (var course in ordered) Release(course.material);
            Release(cubeRoot);
        }
    }
}

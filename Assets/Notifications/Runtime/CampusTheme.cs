using UnityEngine;

namespace WebNotifications
{
    internal static class CampusTheme
    {
        private const int CornerRadius = 12;
        private const int CornerSlice = CornerRadius + 1;
        internal enum TabGlyph { Courses, Notices, Map, Assignments, Account }
        public static readonly Color Paper = new Color32(242, 243, 248, 255);
        public static readonly Color Ink = new Color32(22, 24, 32, 255);
        public static readonly Color Accent = new Color32(0, 84, 184, 255);
        public static readonly Color Selection = new Color32(228, 239, 255, 255);
        public static readonly Color CourseMarker = new Color32(181, 211, 252, 255);
        public static readonly Color Muted = new Color32(81, 88, 103, 255);
        public static readonly Color Line = new Color32(224, 227, 235, 255);
        public static readonly Color MapGround = new Color32(240, 242, 238, 255);
        public static readonly Color MapScrim = new Color32(13, 21, 35, 121);

        private static GUISkin skin;
        private static Texture2D surface, hover, pressed, focus, accent, accentHover, accentPressed, accentFocus;
        public static GUIStyle Primary { get; private set; }
        public static GUIStyle Secondary { get; private set; }
        public static GUIStyle Heading { get; private set; }
        public static GUIStyle LargeTitle { get; private set; }
        public static GUIStyle SectionTitle { get; private set; }
        public static GUIStyle Body { get; private set; }
        public static GUIStyle Caption { get; private set; }
        public static GUIStyle GlassSheet { get; private set; }
        public static GUIStyle GlassNavigation { get; private set; }
        public static GUIStyle Navigation { get; private set; }
        public static GUIStyle NavigationSelected { get; private set; }
        private static GUIStyle badge, mapBadge, mapMarkerFace, mapMarkerActive, mapMarkerBack, mapMarkerShadow;
        private static Texture2D noticeGlyph, dot, chevron, deadlineFade, searchGlyph, filterGlyph;
        private static Texture2D[] tabGlyphs;
        private static GUIStyle cardTitle, rowButton, sectionBadge, fileBadgeLabel, tabLabel, tabSelectedLabel, searchField;

        // Enter only during OnGUI and restore the previous skin after the screen is drawn.
        public static GUISkin Enter()
        {
            var previous = GUI.skin;
            if (skin == null) Initialize(previous);
            GUI.skin = skin;
            float scale = CampusScreenLayout.Scale;
            int padding = Mathf.RoundToInt(CampusScreenLayout.ContentPadding);
            var boxPadding = skin.box.padding;
            boxPadding.left = boxPadding.right = boxPadding.top = boxPadding.bottom = padding;
            skin.label.fontSize = skin.button.fontSize =
                skin.textField.fontSize = skin.textArea.fontSize = Mathf.RoundToInt(15 * scale);
            searchField.fontSize = skin.textField.fontSize;
            searchField.padding.left = Mathf.RoundToInt(36 * scale);
            Primary.fontSize = Secondary.fontSize = skin.button.fontSize;
            Heading.fontSize = Mathf.RoundToInt(23 * scale);
            LargeTitle.fontSize = Mathf.RoundToInt(31 * scale);
            SectionTitle.fontSize = Mathf.RoundToInt(18 * scale);
            Body.fontSize = Mathf.RoundToInt(16 * scale);
            Caption.fontSize = Mathf.RoundToInt(14 * scale);
            cardTitle.fontSize = Mathf.RoundToInt(17 * scale);
            Navigation.fontSize = NavigationSelected.fontSize = Mathf.RoundToInt(13 * scale);
            tabLabel.fontSize = tabSelectedLabel.fontSize = Mathf.RoundToInt(12 * scale);
            sectionBadge.fontSize = fileBadgeLabel.fontSize = Mathf.RoundToInt(12 * scale);
            badge.fontSize = Mathf.RoundToInt(11 * scale);
            mapBadge.fontSize = Mathf.RoundToInt(12 * scale);
            return previous;
        }

        private static void Initialize(GUISkin original)
        {
            skin = Object.Instantiate(original);
            skin.hideFlags = HideFlags.HideAndDontSave;
            var regular = Resources.Load<Font>("CampusFonts/Pretendard-Regular");
            var semibold = Resources.Load<Font>("CampusFonts/Pretendard-SemiBold");
            var bold = Resources.Load<Font>("CampusFonts/Pretendard-Bold");
            if (regular != null) skin.font = regular;
            surface = Tile(new Color32(255, 255, 255, 223), Line);
            hover = Tile(Paper, Line);
            pressed = Tile(Selection, Line);
            focus = Tile(Color.white, Accent, 2);
            accent = Tile(Accent, Accent);
            accentHover = Tile(new Color32(20, 72, 150, 255), Accent);
            accentPressed = Tile(Ink, Ink);
            accentFocus = Tile(Accent, Ink, 2);
            var clear = Tile(Color.clear, Color.clear);
            var selected = Tile(Selection, Selection);
            noticeGlyph = CreateNoticeGlyph();
            dot = Tile(Color.white, Color.white);
            chevron = CreateChevron();
            deadlineFade = CreateDeadlineFade();
            searchGlyph = CreateTabGlyph(new[] { 4, 10, 5, 7, 7, 5, 10, 4, 13, 5, 15, 7, 16, 10,
                15, 13, 13, 15, 10, 16, 7, 15, 5, 13, 4, 10 }, new[] { 14, 14, 20, 20 });
            filterGlyph = CreateTabGlyph(new[] { 4, 5, 20, 5, 14, 12, 14, 18, 10, 20, 10, 12, 4, 5 });
            tabGlyphs = new[]
            {
                CreateTabGlyph(new[] { 4, 4, 10, 4, 12, 6, 14, 4, 20, 4, 20, 18, 14, 18, 12, 20, 10, 18, 4, 18, 4, 4 },
                    new[] { 12, 6, 12, 20 }),
                CreateTabGlyph(new[] { 5, 16, 7, 13, 7, 8, 9, 5, 12, 4, 15, 5, 17, 8, 17, 13, 19, 16, 5, 16 },
                    new[] { 10, 19, 12, 20, 14, 19 }),
                CreateTabGlyph(new[] { 4, 6, 9, 4, 15, 7, 20, 5, 20, 18, 15, 20, 9, 17, 4, 19, 4, 6 },
                    new[] { 9, 4, 9, 17 }, new[] { 15, 7, 15, 20 }),
                CreateTabGlyph(new[] { 7, 4, 17, 4, 19, 6, 19, 20, 5, 20, 5, 6, 7, 4 },
                    new[] { 8, 13, 11, 16, 16, 10 }),
                CreateTabGlyph(new[] { 12, 4, 15, 5, 16, 8, 15, 11, 12, 12, 9, 11, 8, 8, 9, 5, 12, 4 },
                    new[] { 4, 20, 5, 17, 8, 15, 16, 15, 19, 17, 20, 20 })
            };

            skin.label.font = skin.font;
            SetLabelTextColor(skin.label, Ink);
            skin.label.richText = false;
            skin.button = Control(original.button, Ink, surface, hover, pressed, focus);
            skin.button.font = semibold != null ? semibold : skin.font;
            skin.button.wordWrap = true;
            skin.box = Control(original.box, Ink, surface, surface, surface, surface);
            skin.box.font = skin.font;
            var field = Tile(new Color32(232, 235, 242, 255), Line);
            skin.textField = Control(original.textField, Ink, field, field, focus, focus);
            skin.textArea = Control(original.textArea, Ink, field, field, focus, focus);
            skin.textField.font = skin.textArea.font = skin.font;
            searchField = new GUIStyle(skin.textField) { alignment = TextAnchor.MiddleLeft };
            skin.settings.cursorColor = Ink;
            skin.settings.selectionColor = Selection;

            Primary = Control(skin.button, Color.white, accent, accentHover, accentPressed, accentFocus);
            Secondary = Control(skin.button, Accent, selected, pressed, pressed, focus);
            Navigation = Control(skin.button, Muted, clear, hover, pressed, focus);
            NavigationSelected = Control(skin.button, Accent, selected, selected, pressed, focus);
            badge = Control(skin.button, Color.white, accent, accent, accent, accent);
            badge.padding = new RectOffset();
            var navy = new Color32(21, 60, 111, 255);
            var face = Tile(navy, Color.white, 2);
            var activeFace = Tile(Accent, Color.white, 2);
            var backFace = Tile(new Color32(212, 231, 255, 255), navy);
            var shadowColor = new Color32(12, 25, 47, 100);
            var markerShadow = Tile(shadowColor, shadowColor);
            var countFace = Tile(Color.white, navy);
            mapMarkerFace = Control(skin.box, Color.white, face, face, face, face);
            mapMarkerActive = Control(skin.box, Color.white, activeFace, activeFace, activeFace, activeFace);
            mapMarkerBack = Control(skin.box, Color.white, backFace, backFace, backFace, backFace);
            mapMarkerShadow = Control(skin.box, Color.white, markerShadow, markerShadow, markerShadow, markerShadow);
            mapBadge = Control(skin.button, navy, countFace, countFace, countFace, countFace);
            mapBadge.padding = new RectOffset();
            Body = new GUIStyle(skin.label)
            {
                wordWrap = true, alignment = TextAnchor.MiddleLeft, padding = new RectOffset()
            };
            Heading = new GUIStyle(Body) { font = bold != null ? bold : skin.font };
            if (bold == null) Heading.fontStyle = FontStyle.Bold;
            LargeTitle = new GUIStyle(Heading);
            SectionTitle = new GUIStyle(Heading);
            Caption = new GUIStyle(Body);
            SetLabelTextColor(Caption, Muted);
            tabLabel = new GUIStyle(Caption) { alignment = TextAnchor.MiddleCenter, wordWrap = false };
            tabSelectedLabel = new GUIStyle(tabLabel);
            SetLabelTextColor(tabSelectedLabel, Accent);
            fileBadgeLabel = new GUIStyle(tabLabel) { fontStyle = FontStyle.Bold };
            SetLabelTextColor(fileBadgeLabel, Color.white);
            var chip = Tile(Selection, Line);
            sectionBadge = Control(skin.button, Accent, chip, chip, chip, chip);
            sectionBadge.padding = new RectOffset();
            cardTitle = new GUIStyle(Body) { font = semibold != null ? semibold : skin.font, clipping = TextClipping.Clip };
            if (semibold == null) cardTitle.fontStyle = FontStyle.Bold;
            rowButton = Control(skin.button, Ink, surface, hover, selected, focus);
            var rim = new Color32(255, 255, 255, 214);
            var sheet = Tile(new Color32(245, 249, 255, 207), rim);
            GlassSheet = Control(skin.box, Ink, sheet, sheet, sheet, sheet);
            var nav = Tile(new Color32(247, 251, 255, 207), rim);
            GlassNavigation = Control(skin.box, Ink, nav, nav, nav, nav);

            skin.verticalScrollbar = new GUIStyle(original.verticalScrollbar);
            skin.verticalScrollbarThumb = new GUIStyle(original.verticalScrollbarThumb);
            skin.verticalScrollbar.fixedWidth = 8;
            skin.verticalScrollbarThumb.fixedWidth = 6;
            var thumb = Tile(Line, Line);
            SetState(skin.verticalScrollbar.normal, Ink, clear);
            SetState(skin.verticalScrollbarThumb.normal, Ink, thumb);
            SetState(skin.verticalScrollbarThumb.hover, Ink, thumb);
            SetState(skin.verticalScrollbarThumb.active, Ink, thumb);
        }

        private static GUIStyle Control(GUIStyle template, Color text, Texture2D normal, Texture2D hover,
            Texture2D active, Texture2D focused)
        {
            var style = new GUIStyle(template)
            {
                border = new RectOffset(CornerSlice, CornerSlice, CornerSlice, CornerSlice),
                padding = new RectOffset(10, 10, 6, 6),
                richText = false,
                fixedHeight = 0
            };
            SetState(style.normal, text, normal);
            SetState(style.hover, text, hover);
            SetState(style.active, text, active);
            SetState(style.focused, text, focused);
            SetState(style.onNormal, text, active);
            SetState(style.onHover, text, hover);
            SetState(style.onActive, text, active);
            SetState(style.onFocused, text, focused);
            return style;
        }

        private static void SetState(GUIStyleState state, Color text, Texture2D texture)
        {
            state.textColor = text;
#if UNITY_EDITOR
            state.scaledBackgrounds = System.Array.Empty<Texture2D>();
#endif
            state.background = texture;
        }

        private static void SetLabelTextColor(GUIStyle style, Color color)
        {
            style.normal.textColor = style.hover.textColor = style.active.textColor = style.focused.textColor = color;
            style.onNormal.textColor = style.onHover.textColor = style.onActive.textColor = style.onFocused.textColor = color;
        }

        // Nine-slice tiles retain their corner shape when stretched to fit a row or panel.
        private static Texture2D Tile(Color fill, Color outline, int border = 1)
        {
            const int radius = CornerRadius;
            int size = 2 * radius + 3;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x - Mathf.Clamp(x, radius, size - radius - 1);
                float dy = y - Mathf.Clamp(y, radius, size - radius - 1);
                float distance = Mathf.Sqrt(dx * dx + dy * dy);
                var color = Color.Lerp(fill, outline, Mathf.Clamp01(distance - (radius - border) + .5f));
                color.a *= Mathf.Clamp01(radius + .5f - distance);
                pixels[y * size + x] = color;
            }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }

        public static void Fill(Rect area, Color color)
        {
            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(area, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private static Texture2D CreateNoticeGlyph()
        {
            const int size = 48;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var point = new Vector2(x, y);
                // Rounded document outline with three text strokes.
                var edge = new Vector2(Mathf.Abs(x - 23.5f) - 10, Mathf.Abs(y - 23.5f) - 14);
                float outline = Mathf.Abs(Vector2.Max(edge, Vector2.zero).magnitude +
                    Mathf.Min(Mathf.Max(edge.x, edge.y), 0) - 4);
                float strokes = Mathf.Min(StrokeDistance(point, 18, 27, 30),
                    Mathf.Min(StrokeDistance(point, 18, 27, 24), StrokeDistance(point, 18, 23, 18)));
                pixels[y * size + x] = new Color(1, 1, 1, Mathf.Clamp01(1.7f - Mathf.Min(outline, strokes)));
            }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private static float StrokeDistance(Vector2 point, float start, float end, float y)
            => Vector2.Distance(point, new Vector2(Mathf.Clamp(point.x, start, end), y));

        private static Texture2D CreateTabGlyph(params int[][] paths)
        {
            const int size = 48;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var point = new Vector2((x + .5f) / 2, (size - y - .5f) / 2);
                float distanceSquared = float.PositiveInfinity;
                foreach (var path in paths)
                {
                    for (int i = 2; i < path.Length; i += 2)
                    {
                        var start = new Vector2(path[i - 2], path[i - 1]);
                        var edge = new Vector2(path[i], path[i + 1]) - start;
                        var offset = point - start;
                        float fraction = Mathf.Clamp01(Vector2.Dot(offset, edge) / edge.sqrMagnitude);
                        distanceSquared = Mathf.Min(distanceSquared, (offset - fraction * edge).sqrMagnitude);
                    }
                }
                pixels[y * size + x] = new Color(1, 1, 1, Mathf.Clamp01(2.7f - 2 * Mathf.Sqrt(distanceSquared)));
            }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private static Texture2D CreateChevron()
        {
            const int size = 24;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float offset = Mathf.Abs(y - 11.5f);
                float distance = Mathf.Abs(x - (15.5f - offset)) * .7071f;
                pixels[y * size + x] = new Color(1, 1, 1,
                    Mathf.Clamp01(1.8f - distance) * Mathf.Clamp01(7.5f - offset));
            }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }

        public static void DrawNoticeIcon(Rect area, int count, float scale, Color tint, bool mapMarker = false)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (mapMarker)
                GUI.Box(new Rect(area.position + new Vector2(1, 3) * scale, area.size), GUIContent.none, mapMarkerShadow);
            int layers = Mathf.Clamp(count, 1, 3);
            float layerOffset = (mapMarker ? 3 : 4) * scale;
            for (int layer = layers - 1; layer > 0; layer--)
            {
                var back = new Rect(area.x + layer * layerOffset, area.y - layer * layerOffset, area.width, area.height);
                GUI.Box(back, GUIContent.none, mapMarker ? mapMarkerBack : Secondary);
            }
            var face = mapMarker ? (count > 0 ? mapMarkerActive : mapMarkerFace) : GUI.skin.box;
            GUI.Box(area, GUIContent.none, face);
            var previous = GUI.color;
            GUI.color = previous * (mapMarker ? Color.white : tint);
            float inset = (mapMarker ? 6 : 7) * scale;
            GUI.DrawTexture(new Rect(area.x + inset, area.y + inset,
                area.width - 2 * inset, area.height - 2 * inset), noticeGlyph);
            GUI.color = previous;
            if (count > 0)
            {
                var number = mapMarker ? new Rect(area.xMax - 19 * scale, area.y - 6 * scale, 27 * scale, 20 * scale) :
                    new Rect(area.xMax - 10 * scale, area.y - 7 * scale, 29 * scale, 19 * scale);
                GUI.Label(number, count > 99 ? "99+" : count.ToString(), mapMarker ? mapBadge : badge);
            }
        }

        public static void ScreenTitle(string title, string subtitle)
        {
            float scale = CampusScreenLayout.Scale;
            GUILayout.Space(4 * scale);
            GUILayout.Label(title, LargeTitle);
            if (!string.IsNullOrWhiteSpace(subtitle)) GUILayout.Label(subtitle, Caption);
            GUILayout.Space(12 * scale);
        }

        public static void Section(string title)
        {
            float scale = CampusScreenLayout.Scale;
            GUILayout.Space(14 * scale);
            GUILayout.Label(title, SectionTitle);
            GUILayout.Space(4 * scale);
        }

        public static void StatusCard(string message)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label(message, Body);
            GUILayout.EndVertical();
        }

        public static bool Action(string label, GUIStyle style = null)
            => GUILayout.Button(label, style ?? Secondary, GUILayout.MinHeight(44 * CampusScreenLayout.Scale));

        public static bool Option(string label, bool value)
            => GUILayout.Toggle(value, label + (value ? " · 켬" : " · 끔"), value ? Secondary : GUI.skin.button,
                GUILayout.MinHeight(44 * CampusScreenLayout.Scale));

        private static Texture2D CreateDeadlineFade()
        {
            const int samples = 64;
            var texture = new Texture2D(samples, 1, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            var pixels = new Color[samples];
            for (int i = 0; i < samples; i++)
            {
                float progress = i / (samples - 1f);
                float opacity = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.04f, .85f, progress));
                pixels[i] = new Color(1, 1, 1, opacity);
            }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }

        public static string SearchField(string text, string placeholder, int maxLength)
        {
            float scale = CampusScreenLayout.Scale;
            var target = GUILayoutUtility.GetRect(1, 44 * scale, GUILayout.ExpandWidth(true));
            string value = GUI.TextField(target, text, maxLength, searchField);
            if (Event.current.type == EventType.Repaint)
            {
                DrawControlGlyph(searchGlyph, new Vector2(target.x + 18 * scale, target.center.y), 20 * scale, Muted);
                if (value.Length == 0)
                {
                    int inset = searchField.padding.left;
                    GUI.Label(new Rect(target.x + inset, target.y, Mathf.Max(0, target.width - inset), target.height),
                        placeholder, Caption);
                }
            }
            return value;
        }

        public static bool FilterButton(bool expanded)
        {
            float scale = CampusScreenLayout.Scale;
            float size = 44 * scale;
            var target = GUILayoutUtility.GetRect(size, size, GUILayout.Width(size));
            bool clicked = GUI.Button(target, new GUIContent("", expanded ? "필터 닫기" : "필터 열기"),
                expanded ? NavigationSelected : GUI.skin.button);
            if (Event.current.type == EventType.Repaint)
                DrawControlGlyph(filterGlyph, target.center, 22 * scale, expanded ? Accent : Muted);
            return clicked;
        }

        private static void DrawControlGlyph(Texture2D glyph, Vector2 center, float size, Color tint)
        {
            var previous = GUI.color;
            if (!GUI.enabled) tint.a *= .45f;
            GUI.color = previous * tint;
            GUI.DrawTexture(new Rect(center - Vector2.one * size * .5f, Vector2.one * size), glyph);
            GUI.color = previous;
        }

        public static bool BackButton(Rect target)
        {
            bool clicked = GUI.Button(target, new GUIContent("", "뒤로"), Navigation);
            if (Event.current.type == EventType.Repaint)
            {
                float size = 24 * CampusScreenLayout.Scale;
                var icon = new Rect(target.center - Vector2.one * size * .5f, Vector2.one * size);
                var previousMatrix = GUI.matrix;
                var previousColor = GUI.color;
                GUIUtility.RotateAroundPivot(180, icon.center);
                GUI.color = Accent;
                GUI.DrawTexture(icon, chevron);
                GUI.color = previousColor;
                GUI.matrix = previousMatrix;
            }
            return clicked;
        }

        public static bool TabButton(Rect target, string label, TabGlyph glyph, bool selected)
        {
            bool clicked = GUI.Button(target, new GUIContent("", label), selected ? NavigationSelected : Navigation);
            if (Event.current.type == EventType.Repaint)
            {
                float s = CampusScreenLayout.Scale;
                float iconSize = 23 * s;
                float top = target.center.y - 19 * s;
                var previous = GUI.color;
                var tint = selected ? Accent : Muted;
                if (!GUI.enabled) tint.a *= .45f;
                GUI.color = previous * tint;
                GUI.DrawTexture(new Rect(target.center.x - iconSize * .5f, top, iconSize, iconSize), tabGlyphs[(int)glyph]);
                GUI.color = previous;
                GUI.Label(new Rect(target.x, top + iconSize, target.width, 15 * s), label,
                    selected ? tabSelectedLabel : tabLabel);
            }
            return clicked;
        }

        public static bool ListCard(string title, string caption, float width, Color marker, bool expanded = false,
            bool deadlineGradient = false, bool courseCard = false, string section = null,
            bool disclosure = false, string fileBadge = null, Color fileTint = default)
        {
            float s = CampusScreenLayout.Scale;
            float padding = CampusScreenLayout.ContentPadding;
            bool hasCaption = !string.IsNullOrWhiteSpace(caption);
            bool hasMarker = marker != Color.white;
            bool hasSection = !string.IsNullOrEmpty(section);
            bool hasFileBadge = !string.IsNullOrEmpty(fileBadge);
            float markerWidth = (courseCard ? 4 : 8) * s;
            float fileBadgeWidth = 42 * s, sectionWidth = 34 * s, arrowSize = 18 * s;
            float inset = padding + (hasFileBadge ? fileBadgeWidth + padding : hasMarker ? markerWidth + padding : 0);
            float trailing = 2 * padding + arrowSize + (hasSection ? sectionWidth + padding : 0);
            float textWidth = Mathf.Max(1, width - inset - trailing);
            float titleHeight = cardTitle.CalcHeight(new GUIContent(title), textWidth);
            float captionHeight = hasCaption ? 3 * s + Caption.CalcHeight(new GUIContent(caption), textWidth) : 0;
            float height = Mathf.Max((courseCard ? 44 : 56) * s, 2 * padding + titleHeight + captionHeight);
            var area = GUILayoutUtility.GetRect(1, height, GUILayout.ExpandWidth(true));
            bool clicked = GUI.Button(area, new GUIContent("", title), rowButton);
            if (Event.current.type == EventType.Repaint)
            {
                var previousColor = GUI.color;
                float top = area.center.y - (titleHeight + captionHeight) * .5f;
                if (hasMarker)
                {
                    bool highlighted = GUI.enabled && area.Contains(Event.current.mousePosition);
                    float interiorRadius = Mathf.Max(1, CornerRadius - 2 * s);
                    var cardInterior = new Rect(area.x + 2 * s, area.y + 2 * s,
                        Mathf.Max(1, area.width - 4 * s), Mathf.Max(1, area.height - 4 * s));
                    if (highlighted)
                    {
                        GUI.DrawTexture(cardInterior, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, marker, 0, interiorRadius);
                    }
                    else if (deadlineGradient)
                    {
                        var tint = new Color(marker.r, marker.g, marker.b, .27f);
                        GUI.DrawTexture(cardInterior, deadlineFade, ScaleMode.StretchToFill, true, 0, tint, 0, interiorRadius);
                    }
                    var markerColor = highlighted ? Color.white : marker;
                    if (courseCard)
                    {
                        float stripeHeight = Mathf.Min(32 * s, area.height - 2 * padding);
                        var stripe = new Rect(area.x + padding, area.center.y - stripeHeight * .5f, markerWidth, stripeHeight);
                        GUI.DrawTexture(stripe, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, markerColor, 0, stripe.width * .5f);
                    }
                    else
                    {
                        float markerTop = top + Mathf.Max(0, (cardTitle.lineHeight - markerWidth) * .5f);
                        var markerArea = new Rect(area.x + padding, markerTop, markerWidth, markerWidth);
                        GUI.color = markerColor;
                        GUI.DrawTexture(markerArea, dot);
                    }
                }
                GUI.color = expanded ? Accent : Muted;
                var arrow = new Rect(area.xMax - padding - arrowSize, area.center.y - arrowSize * .5f, arrowSize, arrowSize);
                var previousMatrix = GUI.matrix;
                if (disclosure || expanded)
                    GUIUtility.RotateAroundPivot(disclosure && expanded ? -90 : 90, arrow.center);
                GUI.DrawTexture(arrow, chevron);
                GUI.matrix = previousMatrix;
                GUI.color = previousColor;

                float available = Mathf.Max(1, area.width - inset - trailing);
                GUI.Label(new Rect(area.x + inset, top, available, titleHeight), title, cardTitle);
                if (hasCaption)
                    GUI.Label(new Rect(area.x + inset, top + titleHeight + 3 * s, available, captionHeight - 3 * s), caption, Caption);
                if (hasSection)
                    GUI.Label(new Rect(arrow.x - padding - sectionWidth, area.center.y - 13 * s, sectionWidth, 26 * s),
                        new GUIContent(section, "분반 " + section), sectionBadge);
                if (hasFileBadge)
                {
                    var badgeArea = new Rect(area.x + padding, area.center.y - 16 * s, fileBadgeWidth, 32 * s);
                    GUI.DrawTexture(badgeArea, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, fileTint, 0, CornerRadius);
                    GUI.Label(badgeArea, fileBadge, fileBadgeLabel);
                }
            }
            GUILayout.Space(padding);
            return clicked;
        }
    }
}

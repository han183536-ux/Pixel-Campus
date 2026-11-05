using UnityEngine;

namespace WebNotifications
{
    internal static class CampusTheme
    {
        public static readonly Color Paper = new Color32(243, 247, 241, 255);
        public static readonly Color Ink = new Color32(29, 52, 43, 255);
        public static readonly Color Green = new Color32(37, 104, 78, 255);
        public static readonly Color Muted = new Color32(75, 100, 88, 255);
        public static readonly Color Line = new Color32(190, 207, 195, 255);
        public static readonly Color MapGround = new Color32(225, 235, 218, 255);

        private static GUISkin skin;
        private static Texture2D surface, hover, pressed, focus, accent, accentHover, accentPressed, accentFocus;
        public static GUIStyle Primary { get; private set; }
        public static GUIStyle Heading { get; private set; }
        public static GUIStyle Caption { get; private set; }
        private static GUIStyle cardTitle;

        // Enter only during OnGUI and restore the previous skin after the screen is drawn.
        public static GUISkin Enter()
        {
            var previous = GUI.skin;
            if (skin == null) Initialize(previous);
            GUI.skin = skin;
            float scale = CampusScreenLayout.Scale;
            skin.label.fontSize = skin.button.fontSize =
                skin.textField.fontSize = skin.textArea.fontSize = Mathf.RoundToInt(13 * scale);
            Primary.fontSize = skin.button.fontSize;
            Heading.fontSize = Mathf.RoundToInt(22 * scale);
            Caption.fontSize = Mathf.RoundToInt(12 * scale);
            cardTitle.fontSize = Mathf.RoundToInt(16 * scale);
            return previous;
        }

        private static void Initialize(GUISkin original)
        {
            skin = Object.Instantiate(original);
            skin.hideFlags = HideFlags.HideAndDontSave;
            surface = Tile(Color.white, Line);
            hover = Tile(Paper, Green);
            pressed = Tile(MapGround, Green);
            focus = Tile(Color.white, Green, 2);
            accent = Tile(Green, Green);
            accentHover = Tile(new Color32(31, 88, 66, 255), Green);
            accentPressed = Tile(Ink, Ink);
            accentFocus = Tile(Green, Ink, 2);

            skin.label.normal.textColor = Ink;
            skin.label.richText = false;
            skin.button = Control(original.button, Ink, surface, hover, pressed, focus);
            skin.box = Control(original.box, Ink, surface, surface, surface, surface);
            skin.box.padding = new RectOffset(12, 12, 10, 10);
            skin.textField = Control(original.textField, Ink, surface, hover, focus, focus);
            skin.textArea = Control(original.textArea, Ink, surface, hover, focus, focus);
            skin.settings.cursorColor = Ink;
            skin.settings.selectionColor = MapGround;

            Primary = Control(skin.button, Color.white, accent, accentHover, accentPressed, accentFocus);
            Heading = new GUIStyle(skin.label) { fontStyle = FontStyle.Bold, wordWrap = true, alignment = TextAnchor.MiddleLeft };
            Caption = new GUIStyle(skin.label) { wordWrap = true, alignment = TextAnchor.MiddleLeft };
            Caption.normal.textColor = Muted;
            cardTitle = new GUIStyle(Heading) { clipping = TextClipping.Clip };

            skin.verticalScrollbar = new GUIStyle(original.verticalScrollbar);
            skin.verticalScrollbarThumb = new GUIStyle(original.verticalScrollbarThumb);
            skin.verticalScrollbar.normal.background = surface;
            skin.verticalScrollbarThumb.normal.background = pressed;
        }

        private static GUIStyle Control(GUIStyle template, Color text, Texture2D normal, Texture2D hover,
            Texture2D active, Texture2D focused)
        {
            var style = new GUIStyle(template)
            {
                border = new RectOffset(8, 8, 8, 8),
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

        // Nine-slice tiles retain their corner shape when stretched to fit a row or panel.
        private static Texture2D Tile(Color fill, Color outline, int border = 1)
        {
            const int size = 17, radius = 7;
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
                color.a = Mathf.Clamp01(radius + .5f - distance);
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

        public static bool DeadlineButton(string text, Color deadline, GUIStyle style, params GUILayoutOption[] options)
        {
            var content = new GUIContent(text);
            var area = GUILayoutUtility.GetRect(content, style, options);
            bool clicked = GUI.Button(area, content, style);
            if (Event.current.type == EventType.Repaint && deadline != Color.white)
                Fill(new Rect(area.x + 3, area.y + 7, 3 * CampusScreenLayout.Scale, Mathf.Max(1, area.height - 14)), deadline);
            return clicked;
        }

        public static bool ListCard(string title, string caption, Color marker, bool tint = false)
        {
            float s = CampusScreenLayout.Scale;
            var area = GUILayoutUtility.GetRect(1, 80 * s, GUILayout.ExpandWidth(true));
            var previous = GUI.backgroundColor;
            if (tint) GUI.backgroundColor = Color.Lerp(Color.white, marker, .28f);
            bool clicked = GUI.Button(area, GUIContent.none);
            GUI.backgroundColor = previous;
            if (Event.current.type == EventType.Repaint)
            {
                if (marker != Color.white)
                    Fill(new Rect(area.x + 4 * s, area.y + 10 * s, 4 * s, area.height - 20 * s), marker);
                float x = area.x + 16 * s, width = Mathf.Max(1, area.width - 28 * s);
                GUI.Label(new Rect(x, area.y + 6 * s, width, 40 * s), title, cardTitle);
                GUI.Label(new Rect(x, area.y + 45 * s, width, 29 * s), caption, Caption);
            }
            GUILayout.Space(8 * s);
            return clicked;
        }
    }
}

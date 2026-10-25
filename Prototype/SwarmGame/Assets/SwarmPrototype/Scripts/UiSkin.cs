using UnityEngine;

namespace SwarmPrototype
{
    // Shared look for every IMGUI panel: dark wood-ish back, thin gold frame,
    // cream text, green headings. Styles are made on first use inside OnGUI.
    public static class UiSkin
    {
        public static readonly Color Gold = new Color(0.78f, 0.62f, 0.25f);
        public static readonly Color GoldBright = new Color(0.95f, 0.8f, 0.35f);
        public static readonly Color Back = new Color(0.07f, 0.06f, 0.05f, 0.9f);
        public static readonly Color BackLight = new Color(0.16f, 0.13f, 0.09f, 0.95f);
        public static readonly Color Text = new Color(0.95f, 0.93f, 0.85f);
        public static readonly Color Muted = new Color(0.62f, 0.58f, 0.5f);
        public static readonly Color Heading = new Color(0.55f, 0.95f, 0.4f);

        static GUIStyle title, header, cell, bold, number, small, buttonLabel;
        static Texture2D circle;

        public static GUIStyle Title => title ?? (title = Make(14, FontStyle.Bold, TextAnchor.MiddleLeft, Heading));
        public static GUIStyle Header => header ?? (header = Make(12, FontStyle.Bold, TextAnchor.MiddleLeft, Heading));
        public static GUIStyle Cell => cell ?? (cell = Make(12, FontStyle.Normal, TextAnchor.MiddleLeft, Text));
        public static GUIStyle Bold => bold ?? (bold = Make(12, FontStyle.Bold, TextAnchor.MiddleLeft, Text));
        public static GUIStyle Number => number ?? (number = Make(16, FontStyle.Bold, TextAnchor.MiddleLeft, Text));
        public static GUIStyle Small => small ?? (small = Make(10, FontStyle.Normal, TextAnchor.MiddleLeft, Muted));
        static GUIStyle ButtonLabel => buttonLabel ?? (buttonLabel = Make(12, FontStyle.Bold, TextAnchor.MiddleCenter, GoldBright));

        static GUIStyle Make(int size, FontStyle style, TextAnchor anchor, Color color)
        {
            var s = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = style, alignment = anchor };
            s.normal.textColor = color;
            return s;
        }

        // Framed panel background.
        public static void Panel(Rect r)
        {
            Fill(new Rect(r.x - 2f, r.y - 2f, r.width + 4f, r.height + 4f), Gold);
            Fill(r, Back);
        }

        // Thin horizontal rule in the frame color.
        public static void Rule(float x, float y, float width) => Fill(new Rect(x, y, width, 1f), Gold);

        public static void Fill(Rect r, Color color)
        {
            GUI.color = color;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        // A framed button in the same style. `active` tints it, `enabled` false grays it.
        public static bool Button(Rect r, string text, bool active = false, bool enabled = true)
        {
            bool hover = enabled && r.Contains(Event.current.mousePosition);
            Fill(new Rect(r.x - 1f, r.y - 1f, r.width + 2f, r.height + 2f), enabled ? (active ? GoldBright : Gold) : Muted);
            Fill(r, hover || active ? BackLight : Back);
            var style = ButtonLabel;
            Color old = style.normal.textColor;
            style.normal.textColor = enabled ? (hover ? Color.white : GoldBright) : Muted;
            GUI.Label(r, text, style);
            style.normal.textColor = old;
            return enabled && GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        public static void Icon(Rect r, Color color)
        {
            GUI.color = color;
            GUI.DrawTexture(r, Circle());
            GUI.color = Color.white;
        }

        static Texture2D Circle()
        {
            if (circle != null) return circle;
            const int size = 48;
            circle = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float radius = size * 0.5f - 1f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(size * 0.5f, size * 0.5f));
                    circle.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(radius - d + 0.5f)));
                }
            circle.Apply();
            return circle;
        }
    }
}

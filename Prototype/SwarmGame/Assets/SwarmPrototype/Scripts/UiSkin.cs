using UnityEngine;

namespace SwarmPrototype
{
    // Shared look for every IMGUI panel in a match, matching the hub: dark
    // green-grey rounded surfaces with a faint border, cream text, muted
    // captions, a gold primary button and keycap hints. Styles are made on
    // first use inside OnGUI; the Archivo faces come from Resources/Fonts.
    public static class UiSkin
    {
        public static readonly Color Surface = new Color(0.106f, 0.157f, 0.149f, 0.96f);      // #1B2826
        public static readonly Color SurfaceSoft = new Color(0.149f, 0.208f, 0.196f, 0.97f);  // #263532 hover / active
        public static readonly Color Border = new Color(1f, 1f, 1f, 0.08f);
        public static readonly Color Wash = new Color(1f, 1f, 1f, 0.07f);                     // highlighted row
        public static readonly Color Text = new Color(0.949f, 0.925f, 0.863f);                // #F2ECDC
        public static readonly Color Muted = new Color(0.612f, 0.690f, 0.675f);               // #9CB0AC
        public static readonly Color Faint = new Color(0.498f, 0.592f, 0.580f);               // #7F9794
        public static readonly Color Gold = new Color(0.902f, 0.761f, 0.467f);                // #E6C277
        public static readonly Color GoldHover = new Color(0.941f, 0.812f, 0.541f);           // #F0CF8A
        public static readonly Color OnGold = new Color(0.086f, 0.145f, 0.165f);              // #16252A
        public static readonly Color Danger = new Color(0.682f, 0.275f, 0.220f);              // #AE4638
        public static readonly Color Keycap = new Color(1f, 1f, 1f, 0.06f);

        // Older names still used by the shop and map preview.
        public static Color GoldBright => GoldHover;
        public static Color Back => Surface;
        public static Color BackLight => SurfaceSoft;
        public static Color Heading => Muted;

        public const float Radius = 14f;

        static GUIStyle title, header, cell, bold, number, big, small, tiny, buttonLabel, goldLabel, keyLabel;
        static Texture2D circle;
        static Font regular, semiBold, bold700;
        static bool fontsLoaded;

        public static GUIStyle Title => title ?? (title = Make(14, Bold700, TextAnchor.MiddleLeft, Text));
        public static GUIStyle Header => header ?? (header = Make(11, Bold700, TextAnchor.MiddleLeft, Muted));
        public static GUIStyle Cell => cell ?? (cell = Make(13, Regular, TextAnchor.MiddleLeft, Text));
        public static GUIStyle Bold => bold ?? (bold = Make(13, SemiBold, TextAnchor.MiddleLeft, Text));
        public static GUIStyle Number => number ?? (number = Make(16, Bold700, TextAnchor.MiddleLeft, Text));
        public static GUIStyle Big => big ?? (big = Make(22, Bold700, TextAnchor.MiddleLeft, Text));
        public static GUIStyle Small => small ?? (small = Make(11, Regular, TextAnchor.MiddleLeft, Muted));
        public static GUIStyle Tiny => tiny ?? (tiny = Make(10, Bold700, TextAnchor.MiddleLeft, Gold));
        static GUIStyle ButtonLabel => buttonLabel ?? (buttonLabel = Make(13, SemiBold, TextAnchor.MiddleCenter, Text));
        static GUIStyle GoldLabel => goldLabel ?? (goldLabel = Make(14, Bold700, TextAnchor.MiddleCenter, OnGold));
        static GUIStyle KeyLabel => keyLabel ?? (keyLabel = Make(10, SemiBold, TextAnchor.MiddleCenter, Muted));

        static Font Regular { get { LoadFonts(); return regular; } }
        static Font SemiBold { get { LoadFonts(); return semiBold; } }
        static Font Bold700 { get { LoadFonts(); return bold700; } }

        static void LoadFonts()
        {
            if (fontsLoaded) return;
            fontsLoaded = true;
            regular = Resources.Load<Font>("Fonts/Archivo-Regular");
            semiBold = Resources.Load<Font>("Fonts/Archivo-SemiBold");
            bold700 = Resources.Load<Font>("Fonts/Archivo-Bold");
        }

        static GUIStyle Make(int size, Font font, TextAnchor anchor, Color color)
        {
            var s = new GUIStyle(GUI.skin.label) { fontSize = size, alignment = anchor, fontStyle = FontStyle.Normal };
            if (font != null) s.font = font;
            s.normal.textColor = color;
            s.wordWrap = false;
            return s;
        }

        // ---- Surfaces ---------------------------------------------------------------------

        public static void Rounded(Rect r, Color color, float radius)
        {
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, color, 0f, radius);
        }

        public static void Outline(Rect r, Color color, float radius, float width = 1f)
        {
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, color, width, radius);
        }

        // Standard panel: dark rounded surface with a faint border.
        public static void Panel(Rect r) => Panel(r, Radius);

        public static void Panel(Rect r, float radius)
        {
            Rounded(r, Surface, radius);
            Outline(r, Border, radius);
        }

        // Fully rounded pill for labels that float over the world.
        public static void Pill(Rect r, Color color) => Rounded(r, color, r.height * 0.5f);

        // Thin horizontal rule in the border color.
        public static void Rule(float x, float y, float width) => Fill(new Rect(x, y, width, 1f), Border);

        public static void Fill(Rect r, Color color)
        {
            GUI.color = color;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        // ---- Controls ---------------------------------------------------------------------

        // Secondary button: rounded surface, lighter on hover; `active` keeps it lit, `enabled` false mutes it.
        public static bool Button(Rect r, string text, bool active = false, bool enabled = true)
        {
            bool hover = enabled && r.Contains(Event.current.mousePosition);
            Rounded(r, hover || active ? SurfaceSoft : Surface, 10f);
            Outline(r, active ? new Color(1f, 1f, 1f, 0.16f) : Border, 10f);
            var style = ButtonLabel;
            Color old = style.normal.textColor;
            style.normal.textColor = enabled ? (hover || active ? Color.white : Text) : Faint;
            GUI.Label(r, text, style);
            style.normal.textColor = old;
            return enabled && GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        // Primary gold button with an optional icon on the left and keycap on the right.
        public static bool GoldButton(Rect r, string text, string icon = null, string key = null, bool enabled = true)
        {
            bool hover = enabled && r.Contains(Event.current.mousePosition);
            Rounded(r, enabled ? (hover ? GoldHover : Gold) : new Color(0.165f, 0.259f, 0.278f), 12f);
            float x = r.x + 16f;
            if (icon != null)
            {
                GUI.color = enabled ? OnGold : Faint;
                GUI.DrawTexture(new Rect(x, r.center.y - 10f, 20f, 20f), Icons.Get(icon));
                GUI.color = Color.white;
                x += 28f;
            }
            var style = GoldLabel;
            Color old = style.normal.textColor;
            style.normal.textColor = enabled ? OnGold : Faint;
            float w = style.CalcSize(new GUIContent(text)).x;
            GUI.Label(new Rect(x, r.y, w + 4f, r.height), text, style);
            style.normal.textColor = old;
            if (key != null) Key(new Vector2(x + w + 12f, r.center.y), key, new Color(0f, 0f, 0f, 0.14f), OnGold);
            return enabled && GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        // Square icon button, like the trio at the top-right. `badge` > 0 draws a mint counter.
        public static bool IconButton(Rect r, string icon, int badge = 0, bool active = false)
        {
            bool hover = r.Contains(Event.current.mousePosition);
            Rounded(r, hover || active ? SurfaceSoft : Surface, 12f);
            Outline(r, Border, 12f);
            GUI.color = hover || active ? Color.white : Text;
            float s = r.height * 0.42f;
            GUI.DrawTexture(new Rect(r.center.x - s * 0.5f, r.center.y - s * 0.5f, s, s), Icons.Get(icon));
            GUI.color = Color.white;
            if (badge > 0)
            {
                string count = badge > 99 ? "99+" : badge.ToString();
                float bw = Mathf.Max(20f, KeyLabel.CalcSize(new GUIContent(count)).x + 10f);
                var b = new Rect(r.xMax - bw * 0.5f - 6f, r.y - 4f, bw, 20f);
                Pill(b, new Color(0.498f, 0.820f, 0.659f));
                var style = KeyLabel;
                Color old = style.normal.textColor;
                style.normal.textColor = OnGold;
                GUI.Label(b, count, style);
                style.normal.textColor = old;
            }
            return GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        // Keycap hint such as [Space], centred on `centre`'s y and starting at its x. Returns its width.
        public static float Key(Vector2 at, string key, Color? fill = null, Color? text = null)
        {
            var style = KeyLabel;
            float w = style.CalcSize(new GUIContent(key)).x + 12f;
            var r = new Rect(at.x, at.y - 10f, w, 20f);
            Rounded(r, fill ?? Keycap, 5f);
            Outline(r, Border, 5f);
            Color old = style.normal.textColor;
            style.normal.textColor = text ?? Muted;
            GUI.Label(r, key, style);
            style.normal.textColor = old;
            return w;
        }

        // Label with an icon on its left. Returns the x after the label.
        public static float IconLabel(float x, float midY, string icon, float iconSize, string text, GUIStyle style, Color? tint = null)
        {
            GUI.color = tint ?? Color.white;
            GUI.DrawTexture(new Rect(x, midY - iconSize * 0.5f, iconSize, iconSize), Icons.Get(icon));
            GUI.color = Color.white;
            x += iconSize + 8f;
            float w = style.CalcSize(new GUIContent(text)).x;
            GUI.Label(new Rect(x, midY - 12f, w + 4f, 24f), text, style);
            return x + w;
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

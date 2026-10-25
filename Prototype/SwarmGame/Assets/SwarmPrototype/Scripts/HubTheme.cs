using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace SwarmPrototype
{
    // Design tokens from the Commoners UI handoff: colors, Archivo type faces, text helpers.
    // Layout numbers in the screens are the handoff's 1600x900 pixel values, mapped 1:1 onto the canvas.
    public static class HubTheme
    {
        public static readonly Color Base = Hex("0D1D23");
        public static readonly Color GroundCenter = Hex("3A6764"), GroundMid = Hex("28494C"), GroundFar = Hex("162C33"), GroundEdge = Hex("0E1E24");
        public static readonly Color Panel = Rgba(14, 30, 36, 0.78f), PanelStrong = Rgba(14, 30, 36, 0.86f), PanelBorder = Rgba(156, 194, 188, 0.12f);
        public static readonly Color PanelBorderStrong = Rgba(156, 194, 188, 0.14f), PanelBorderBright = Rgba(156, 194, 188, 0.16f), ModalSurface = Hex("19333A");
        public static readonly Color Cream = Hex("F2ECDC"), Secondary = Hex("A9BFBB"), Tertiary = Hex("93AAA7"), Faint = Hex("7F9794");
        public static readonly Color Soft = Hex("DCE6E2"), Pale = Hex("CFE0DC"), Mint = Hex("9FD3C6"), Teal = Hex("9CC2BC"), Parchment = Hex("D9CDA8");
        public static readonly Color Gold = Hex("E6C277"), GoldHover = Hex("F0CF8A"), OnGold = Hex("16252A");
        public static readonly Color Segment = Hex("2C4D53"), Bar = Rgba(12, 27, 32, 0.7f), Track = Rgba(6, 16, 20, 0.5f), TrackSoft = Rgba(6, 16, 20, 0.45f);
        public static readonly Color InputFill = Rgba(6, 16, 20, 0.6f), InputBorder = Rgba(156, 194, 188, 0.2f), DotRing = Hex("12262C");
        public static readonly Color Online = Hex("7FD1A8"), InMatch = Hex("E6C277"), Offline = Hex("5E7472");
        public static readonly Color Danger = Hex("AE4638"), DangerHover = Hex("C25546"), DangerText = Hex("F2A090"), OnDanger = Hex("FBF4EE");
        public static readonly Color DisabledFill = Hex("2A4247"), DisabledText = Hex("7F9794");
        public static readonly Color HoverWash = Rgba(242, 236, 220, 0.08f), RowWash = Rgba(242, 236, 220, 0.045f), OpenWash = Rgba(242, 236, 220, 0.05f);
        public static readonly Color GoldWash = Rgba(230, 194, 119, 0.12f), GoldWashStrong = Rgba(230, 194, 119, 0.16f), OnlineWash = Rgba(127, 209, 168, 0.12f);
        public static readonly Color Ridge = Hex("0B1A1F"), Silhouette = Hex("0A171C");

        public static Color Hex(string value)
        {
            ColorUtility.TryParseHtmlString("#" + value, out var color);
            return color;
        }
        public static Color Rgba(int r, int g, int b, float a) => new Color(r / 255f, g / 255f, b / 255f, a);
        public static Color Alpha(Color color, float alpha) => new Color(color.r, color.g, color.b, alpha);

        // Archivo static cuts stand in for the variable font's width axis:
        // Logo = Expanded ExtraBold (125% / 800), Wide = SemiExpanded Bold (112% / 700),
        // WideHeavy = SemiExpanded ExtraBold (115% / 800), Body = Regular, BodyMedium = SemiBold, BodyBold = Bold.
        public enum Face { Logo, Wide, WideHeavy, Body, BodyMedium, BodyBold }
        static readonly string[] files =
        {
            "ArchivoExpanded-ExtraBold", "ArchivoSemiExpanded-Bold", "ArchivoSemiExpanded-ExtraBold",
            "Archivo-Regular", "Archivo-SemiBold", "Archivo-Bold"
        };
        static readonly TMP_FontAsset[] fonts = new TMP_FontAsset[6];
        static TMP_FontAsset fallback;

        public static TMP_FontAsset Font(Face face)
        {
            int index = (int)face;
            if (fonts[index] != null) return fonts[index];
            var source = Resources.Load<Font>("Fonts/" + files[index]);
            if (source != null)
            {
                int sampling = face == Face.Logo ? 96 : 64;
                var asset = TMP_FontAsset.CreateFontAsset(source, sampling, sampling / 8, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
                if (asset != null)
                {
                    asset.name = files[index];
                    fonts[index] = asset;
                    return asset;
                }
            }
            if (fallback == null) fallback = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            fonts[index] = fallback;
            return fallback;
        }

        public static TMP_Text Text(Transform parent, string name, string value, float size, Color color, Face face = Face.Body,
            float tracking = 0, TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft, bool wrap = true)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = Font(face);
            text.text = value;
            text.fontSize = size;
            text.color = color;
            text.alignment = alignment;
            text.enableWordWrapping = wrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.characterSpacing = tracking * 100;
            text.raycastTarget = false;
            text.richText = false;
            return text;
        }

        // Uppercase label: 11px, 700, 112% width, 0.16em tracking, tertiary.
        public static TMP_Text Label(Transform parent, string value, Color? color = null, float size = 11, float tracking = 0.16f)
            => Text(parent, value, value, size, color ?? Tertiary, Face.Wide, tracking, TextAlignmentOptions.MidlineLeft, false);
    }
}

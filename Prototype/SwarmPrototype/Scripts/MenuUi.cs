using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace SwarmPrototype
{
    // Code-built Canvas vocabulary for the character home and its panels.
    public static class MenuUi
    {
        public static readonly Color Ink = Hex("102B35"), Panel = Hex("163943"), Gold = Hex("EDC879");
        public static readonly Color Paper = Hex("F5F0DF"), Muted = Hex("A8BFBB"), Teal = Hex("6FD6BC");
        static Sprite rounded;

        public static Color Hex(string value)
        {
            ColorUtility.TryParseHtmlString("#" + value, out var color);
            return color;
        }

        public static RectTransform Rect(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size, Vector2? pivot = null)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot ?? anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        public static RectTransform Fill(Transform parent, string name)
        {
            var rect = Rect(parent, name, Vector2.zero, Vector2.zero, Vector2.zero);
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }

        public static Image Image(RectTransform rect, Color color, bool roundedCorners = false)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            if (roundedCorners) { image.sprite = Rounded(); image.type = UnityEngine.UI.Image.Type.Sliced; }
            return image;
        }

        public static TMP_Text Text(Transform parent, string value, float size, Color color, Vector2 position, Vector2 bounds,
            TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft, bool bold = false, Vector2? anchor = null)
        {
            var rect = Rect(parent, value.Length > 30 ? value.Substring(0, 30) : value, anchor ?? Vector2.up, position, bounds);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            // Archivo cuts from HubTheme so the in-match social panel matches the home screens.
            text.font = HubTheme.Font(bold ? HubTheme.Face.BodyBold : HubTheme.Face.Body);
            text.text = value;
            text.fontSize = size;
            text.color = color;
            text.alignment = alignment;
            text.enableWordWrapping = true;
            text.raycastTarget = false;
            return text;
        }

        public static TMP_Text Label(Transform parent, string value, Vector2 position, Vector2 size, Color? color = null)
        {
            var text = Text(parent, value, 13, color ?? Muted, position, size, bold: true);
            text.characterSpacing = 3;
            return text;
        }

        public static Button Button(Transform parent, string name, string caption, Vector2 anchor, Vector2 position,
            Vector2 size, UnityAction clicked, bool primary = false)
        {
            var rect = Rect(parent, name, anchor, position, size);
            var image = Image(rect, primary ? Gold : Panel, true);
            image.raycastTarget = true;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = Hex("FFF5DB");
            colors.selectedColor = Hex("FFF5DB");
            colors.pressedColor = Hex("83B8A5");
            colors.disabledColor = new Color(0.6f, 0.65f, 0.64f, 0.5f);
            colors.fadeDuration = 0.12f;
            button.colors = colors;
            if (clicked != null) button.onClick.AddListener(clicked);
            var label = Text(rect, caption, primary && size.x >= 180 ? 25 : 16, primary ? Ink : Paper, Vector2.zero, size,
                TextAlignmentOptions.Center, true, Vector2.one * 0.5f);
            label.characterSpacing = primary ? 3 : 0.5f;
            return button;
        }

        public static void Line(Transform parent, Vector2 position, float width)
        {
            Image(Rect(parent, "Rule", Vector2.up, position, new Vector2(width, 1)), new Color(1, 1, 1, 0.12f));
        }

        public static TMP_InputField InputField(Transform parent, string name, string hint, string value, Vector2 position,
            Vector2 size, int limit, UnityAction<string> changed)
        {
            var rect = Rect(parent, name, Vector2.up, position, size);
            var background = Image(rect, Ink, true);
            background.raycastTarget = true;
            var area = Fill(rect, "Text area");
            area.offsetMin = new Vector2(12, 5); area.offsetMax = new Vector2(-12, -5);
            area.gameObject.AddComponent<RectMask2D>();
            var text = Text(area, "", 16, Paper, Vector2.zero, size - new Vector2(24, 10));
            var placeholder = Text(area, hint, 15, Muted, Vector2.zero, size - new Vector2(24, 10));
            text.richText = placeholder.richText = false;
            var input = rect.gameObject.AddComponent<TMP_InputField>();
            input.targetGraphic = background; input.textViewport = area; input.textComponent = text; input.placeholder = placeholder;
            input.characterLimit = limit; input.richText = false; input.lineType = TMP_InputField.LineType.SingleLine;
            input.SetTextWithoutNotify(value);
            if (changed != null) input.onValueChanged.AddListener(changed);
            return input;
        }

        static Sprite Rounded()
        {
            if (rounded != null) return rounded;
            const int side = 32;
            const float radius = 8;
            var texture = new Texture2D(side, side, TextureFormat.RGBA32, false) { name = "Menu rounded corners", filterMode = FilterMode.Bilinear };
            for (int y = 0; y < side; y++)
                for (int x = 0; x < side; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    var nearest = new Vector2(Mathf.Clamp(p.x, radius, side - radius), Mathf.Clamp(p.y, radius, side - radius));
                    float alpha = Mathf.Clamp01(radius - Vector2.Distance(p, nearest) + 0.5f);
                    texture.SetPixel(x, y, new Color(1, 1, 1, alpha));
                }
            texture.Apply();
            rounded = Sprite.Create(texture, new Rect(0, 0, side, side), Vector2.one * 0.5f, 100, 0, SpriteMeshType.FullRect, new Vector4(9, 9, 9, 9));
            return rounded;
        }
    }

    [RequireComponent(typeof(CanvasRenderer))]
    public class MenuBackdrop : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = rectTransform.rect;
            const int columns = 28, rows = 18;
            for (int y = 0; y <= rows; y++)
                for (int x = 0; x <= columns; x++)
                {
                    float u = x / (float)columns, v = y / (float)rows;
                    float glow = Mathf.Exp(-8f * ((u - 0.52f) * (u - 0.52f) + (v - 0.48f) * (v - 0.48f)));
                    vh.AddVert(new Vector3(r.x + u * r.width, r.y + v * r.height), Color.Lerp(MenuUi.Hex("0C202B"), MenuUi.Hex("2B6967"), glow), Vector2.zero);
                }
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < columns; x++)
                {
                    int i = y * (columns + 1) + x;
                    vh.AddTriangle(i, i + columns + 1, i + 1);
                    vh.AddTriangle(i + 1, i + columns + 1, i + columns + 2);
                }
            Vector2 center = new Vector2(r.x + r.width * 0.52f, r.y + r.height * 0.48f);
            Ring(vh, center, r.height * 0.32f, new Color(0.8f, 0.94f, 0.83f, 0.075f), 1);
            Ring(vh, center, r.height * 0.42f, new Color(0.8f, 0.94f, 0.83f, 0.04f), 1);
            for (int i = 0; i < 12; i++)
            {
                float angle = i * Mathf.PI / 6;
                Vector2 p = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * r.height * 0.42f;
                Ring(vh, p, i % 3 == 0 ? 3 : 1.5f, new Color(0.93f, 0.78f, 0.47f, 0.3f), 1);
            }
        }

        static void Ring(VertexHelper vh, Vector2 center, float radius, Color color, float thickness)
        {
            int start = vh.currentVertCount;
            const int segments = 128;
            for (int i = 0; i <= segments; i++)
            {
                float angle = i * Mathf.PI * 2 / segments;
                var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                vh.AddVert(center + dir * radius, color, Vector2.zero);
                vh.AddVert(center + dir * (radius + thickness), color, Vector2.zero);
                if (i == segments) continue;
                int index = start + i * 2;
                vh.AddTriangle(index, index + 1, index + 2);
                vh.AddTriangle(index + 1, index + 3, index + 2);
            }
        }
    }

    public class MenuSafeArea : MonoBehaviour
    {
        Rect last;
        Vector2 lastSize;
        void LateUpdate()
        {
            var area = Screen.safeArea;
            var size = new Vector2(Screen.width, Screen.height);
            if (area == last && size == lastSize) return;
            last = area;
            lastSize = size;
            if (size.x <= 0 || size.y <= 0) return;
            var rect = (RectTransform)transform;
            rect.anchorMin = new Vector2(area.xMin / size.x, area.yMin / size.y);
            rect.anchorMax = new Vector2(area.xMax / size.x, area.yMax / size.y);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}

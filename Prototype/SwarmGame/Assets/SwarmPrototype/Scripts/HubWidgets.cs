using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SwarmPrototype
{
    // Canvas building blocks for the splash and hub, mirroring the handoff's shared components:
    // panels, gold / ghost / danger / nav / icon buttons, segmented controls, inputs, avatars, sliders.
    // Layout uses uGUI layout groups so the design's flex columns and lists carry over directly.
    public static class HubWidgets
    {
        public enum ButtonStyle { Gold, Ghost, Danger, DangerText, Nav, NavActive, Icon, Segment, SegmentActive, Link }

        // ---- Layout -------------------------------------------------------------------------

        public static RectTransform Rect(Transform parent, string name)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            return rect;
        }

        // Absolute box in design pixels from the parent's top-left corner.
        public static RectTransform Fixed(Transform parent, string name, float left, float top, float width, float height)
        {
            var rect = Rect(parent, name);
            rect.anchoredPosition = new Vector2(left, -top);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        // Box inset from every edge of the parent.
        public static RectTransform Inset(Transform parent, string name, float left, float top, float right, float bottom)
        {
            var rect = Rect(parent, name);
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
            return rect;
        }

        public static RectTransform Fill(Transform parent, string name) => Inset(parent, name, 0, 0, 0, 0);

        public static RectTransform Centered(Transform parent, string name, float width, float height, float offsetX = 0, float offsetY = 0)
        {
            var rect = Rect(parent, name);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(offsetX, offsetY);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        public static RectTransform Column(Transform parent, string name, float spacing = 0, RectOffset padding = null,
            TextAnchor align = TextAnchor.UpperLeft, bool controlWidth = true, bool controlHeight = true)
        {
            var rect = Rect(parent, name);
            var group = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            group.spacing = spacing;
            group.padding = padding ?? new RectOffset();
            group.childAlignment = align;
            // Children are always sized by the group; `controlWidth` only decides whether they stretch across it.
            group.childControlWidth = group.childControlHeight = true;
            group.childForceExpandWidth = controlWidth;
            group.childForceExpandHeight = false;
            return rect;
        }

        public static RectTransform Row(Transform parent, string name, float spacing = 0, RectOffset padding = null,
            TextAnchor align = TextAnchor.MiddleLeft, bool controlWidth = true, bool controlHeight = true)
        {
            var rect = Rect(parent, name);
            var group = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
            group.spacing = spacing;
            group.padding = padding ?? new RectOffset();
            group.childAlignment = align;
            // Children are always sized by the group; `controlHeight` only decides whether they stretch to the row height.
            group.childControlWidth = group.childControlHeight = true;
            group.childForceExpandWidth = false;
            group.childForceExpandHeight = controlHeight;
            // A horizontal group reports its tallest child's flexible height (1 when it force-expands,
            // or when it holds a Spacer), which would let a column hand this row every spare pixel.
            // Pin it; Size() can still raise it later.
            rect.gameObject.AddComponent<LayoutElement>().flexibleHeight = 0;
            return rect;
        }

        public static RectOffset Pad(int left, int top, int right, int bottom) => new RectOffset(left, right, top, bottom);
        public static RectOffset Pad(int vertical, int horizontal) => new RectOffset(horizontal, horizontal, vertical, vertical);

        public static LayoutElement Size(Component target, float width = -1, float height = -1, float flexWidth = 0, float flexHeight = 0, float minWidth = -1, float minHeight = -1)
        {
            var element = target.GetComponent<LayoutElement>();
            if (element == null) element = target.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = width;
            element.preferredHeight = height;
            element.flexibleWidth = flexWidth;
            element.flexibleHeight = flexHeight;
            element.minWidth = minWidth;
            element.minHeight = minHeight;
            return element;
        }

        public static void FitContent(Component target, bool horizontal, bool vertical)
        {
            var fitter = target.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = horizontal ? ContentSizeFitter.FitMode.PreferredSize : ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = vertical ? ContentSizeFitter.FitMode.PreferredSize : ContentSizeFitter.FitMode.Unconstrained;
        }

        public static RectTransform Spacer(Transform parent, float flex = 1)
        {
            var rect = Rect(parent, "Spacer");
            Size(rect, flexWidth: flex, flexHeight: flex);
            return rect;
        }

        public static void Clear(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i).gameObject;
                child.SetActive(false);
                UnityEngine.Object.Destroy(child);
            }
        }

        // ---- Surfaces -----------------------------------------------------------------------

        public static Image Background(RectTransform rect, Color color, float radius = 0, bool raycast = false)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycast;
            if (radius > 0) { image.sprite = HubArt.Rounded(radius); image.type = Image.Type.Sliced; }
            return image;
        }

        public static Image Border(RectTransform rect, Color color, float radius, float width = 1)
        {
            var edge = Fill(rect, "Border");
            var image = edge.gameObject.AddComponent<Image>();
            image.sprite = HubArt.Outline(radius, width);
            image.type = Image.Type.Sliced;
            image.color = color;
            image.raycastTarget = false;
            var element = edge.gameObject.AddComponent<LayoutElement>();
            element.ignoreLayout = true;
            return image;
        }

        public static Image Shadow(RectTransform rect, float radius, float blur, Color color, float offsetY = -6)
        {
            var shade = Inset(rect, "Shadow", -blur, -blur - offsetY, -blur, -blur + offsetY);
            shade.SetAsFirstSibling();
            var image = shade.gameObject.AddComponent<Image>();
            image.sprite = HubArt.Shadow(radius, blur);
            image.type = Image.Type.Sliced;
            image.color = color;
            image.raycastTarget = false;
            shade.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            return image;
        }

        public static RectTransform Panel(Transform parent, string name, float radius = 14, Color? fill = null, Color? border = null, float borderWidth = 1)
        {
            var rect = Rect(parent, name);
            Background(rect, fill ?? HubTheme.Panel, radius, true);
            Border(rect, border ?? HubTheme.PanelBorder, radius, borderWidth);
            return rect;
        }

        public static Image Icon(Transform parent, string name, string icon, float size, Color color)
        {
            var rect = Rect(parent, name);
            rect.sizeDelta = new Vector2(size, size);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = HubArt.Icon(icon);
            image.color = color;
            image.preserveAspect = true;
            image.raycastTarget = false;
            Size(rect, size, size, minWidth: size, minHeight: size);
            return image;
        }

        public static Image Disc(Transform parent, string name, float size, Color color)
        {
            var rect = Rect(parent, name);
            rect.sizeDelta = new Vector2(size, size);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = HubArt.Disc();
            image.color = color;
            image.raycastTarget = false;
            Size(rect, size, size, minWidth: size, minHeight: size);
            return image;
        }

        public static Image Ring(Transform parent, string name, float size, float width, Color color)
        {
            var rect = Rect(parent, name);
            rect.sizeDelta = new Vector2(size, size);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = HubArt.Ring(size, width);
            image.color = color;
            image.raycastTarget = false;
            Size(rect, size, size, minWidth: size, minHeight: size);
            return image;
        }

        public static Image Rule(Transform parent, string name, Color color, bool horizontal = true)
        {
            var rect = Rect(parent, name);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            if (horizontal) Size(rect, height: 1, flexWidth: 1, minHeight: 1);
            else Size(rect, width: 1, flexHeight: 1, minWidth: 1);
            return image;
        }

        // Rounded square avatar with the initial and an optional presence dot at the bottom right.
        public static RectTransform Avatar(Transform parent, string name, float size, float radius, Color fill, string initial, float fontSize,
            Color initialColor, Color? border = null, Color? dot = null, float dotSize = 10, float dotRing = 2, HubTheme.Face face = HubTheme.Face.BodyBold)
        {
            var rect = Rect(parent, name);
            rect.sizeDelta = new Vector2(size, size);
            Size(rect, size, size, minWidth: size, minHeight: size);
            Background(rect, fill, radius);
            if (border != null) Border(rect, border.Value, radius);
            var label = HubTheme.Text(rect, "Initial", initial, fontSize, initialColor, face, 0, TextAlignmentOptions.Center, false);
            Stretch(label.rectTransform);
            if (dot != null)
            {
                // CSS: right -3px, bottom -3px, with the ring drawn as a border outside the dot.
                var center = new Vector2(3 - dotSize / 2 - dotRing, -3 + dotSize / 2 + dotRing);
                var ring = Disc(rect, "Presence ring", dotSize + dotRing * 2, HubTheme.DotRing);
                Place(ring.rectTransform, new Vector2(1, 0), center);
                var inner = Disc(rect, "Presence", dotSize, dot.Value);
                Place(inner.rectTransform, new Vector2(1, 0), center);
            }
            return rect;
        }

        public static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        // Pin a fixed-size child at an anchor inside its parent (ignores layout groups).
        public static void Place(RectTransform rect, Vector2 anchor, Vector2 offset)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchorMin = rect.anchorMax = anchor;
            rect.anchoredPosition = offset;
            var element = rect.GetComponent<LayoutElement>();
            if (element == null) element = rect.gameObject.AddComponent<LayoutElement>();
            element.ignoreLayout = true;
        }

        // ---- Buttons ------------------------------------------------------------------------

        public class Spec
        {
            public float Height = 44, PaddingX = 12, Width = -1, Radius = 9, FontSize = 12, Tracking = 0.14f, IconSize = 18, Gap = 10, FlexWidth = 0;
            public HubTheme.Face Face = HubTheme.Face.WideHeavy;
            public string Icon;
            public bool Shadow;
        }

        public static Button Button(Transform parent, string name, string label, ButtonStyle style, UnityEngine.Events.UnityAction onClick, Spec spec = null)
        {
            spec = spec ?? new Spec();
            Color fill, hover, text, hoverText = default, border = default, hoverBorder = default;
            bool hasBorder = false, textHover = false;
            switch (style)
            {
                case ButtonStyle.Gold: fill = HubTheme.Gold; hover = HubTheme.GoldHover; text = HubTheme.OnGold; break;
                case ButtonStyle.Ghost:
                    fill = Color.clear; hover = HubTheme.HoverWash; text = HubTheme.Cream;
                    hasBorder = true; border = HubTheme.InputBorder; hoverBorder = HubTheme.Rgba(156, 194, 188, 0.32f); break;
                case ButtonStyle.Danger: fill = HubTheme.Danger; hover = HubTheme.DangerHover; text = HubTheme.OnDanger; break;
                case ButtonStyle.DangerText: fill = Color.clear; hover = HubTheme.Rgba(242, 160, 144, 0.12f); text = HubTheme.DangerText; break;
                case ButtonStyle.Nav: fill = Color.clear; hover = HubTheme.HoverWash; text = HubTheme.Secondary; hoverText = HubTheme.Cream; textHover = true; break;
                case ButtonStyle.NavActive: fill = HubTheme.Rgba(242, 236, 220, 0.1f); hover = HubTheme.Rgba(242, 236, 220, 0.14f); text = HubTheme.Cream; break;
                case ButtonStyle.Icon: fill = Color.clear; hover = HubTheme.GoldWashStrong; text = HubTheme.Secondary; hoverText = HubTheme.Gold; textHover = true; break;
                case ButtonStyle.Segment: fill = Color.clear; hover = HubTheme.HoverWash; text = HubTheme.Tertiary; hoverText = HubTheme.Cream; textHover = true; break;
                case ButtonStyle.SegmentActive: fill = HubTheme.Segment; hover = HubTheme.Segment; text = HubTheme.Cream; break;
                default: fill = Color.clear; hover = HubTheme.GoldWash; text = HubTheme.Gold; break;
            }

            bool fixedWidth = spec.Width > 0, flex = spec.FlexWidth > 0;
            var rect = Row(parent, name, spec.Gap, Pad(0, (int)spec.PaddingX), TextAnchor.MiddleCenter);
            rect.sizeDelta = new Vector2(fixedWidth ? spec.Width : spec.Height, spec.Height);
            // Fixed: explicit width. Flex: preferred 0 so siblings split the space evenly (CSS minmax(0, 1fr)).
            // Auto: the layout group's preferred width (padding + icon + label).
            Size(rect, fixedWidth ? spec.Width : flex ? 0 : -1, spec.Height, spec.FlexWidth, 0, fixedWidth ? spec.Width : flex ? 0 : -1, spec.Height);
            if (spec.Shadow) Shadow(rect, spec.Radius, 18, HubTheme.Rgba(6, 18, 22, 0.45f));
            var image = Background(rect, fill, spec.Radius, true);
            Image edge = hasBorder ? Border(rect, border, spec.Radius) : null;

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.None;
            if (onClick != null) button.onClick.AddListener(onClick);

            Image iconImage = null;
            if (spec.Icon != null) iconImage = Icon(rect, "Icon", spec.Icon, spec.IconSize, text);
            TMP_Text caption = null;
            if (!string.IsNullOrEmpty(label))
            {
                caption = HubTheme.Text(rect, "Label", label, spec.FontSize, text, spec.Face, spec.Tracking, TextAlignmentOptions.Center, false);
                // CSS letter-spacing adds a trailing gap; offset it so centred captions sit centred.
                caption.margin = new Vector4(spec.Tracking * spec.FontSize, 0, 0, 0);
            }
            // Outside a layout group nothing else sizes an auto-width button, so fit it to its content.
            if (!fixedWidth && !flex && parent.GetComponent<LayoutGroup>() == null) FitContent(rect, true, false);

            var tint = rect.gameObject.AddComponent<HubHover>();
            tint.Fill = image; tint.Normal = fill; tint.Hover = hover;
            tint.Pressed = Color.Lerp(hover, Color.black, 0.12f);
            tint.Border = edge; tint.NormalBorder = border; tint.HoverBorder = hoverBorder;
            tint.Label = caption; tint.IconImage = iconImage;
            tint.NormalText = text; tint.HoverText = textHover ? hoverText : text;
            tint.DisabledFill = style == ButtonStyle.Gold || style == ButtonStyle.Danger ? HubTheme.DisabledFill : fill;
            tint.DisabledText = HubTheme.DisabledText;
            return button;
        }

        public static Button IconButton(Transform parent, string name, string icon, UnityEngine.Events.UnityAction onClick, float size = 44,
            Color? color = null, Color? fill = null, float iconSize = 18, float radius = 10)
        {
            var button = Button(parent, name, null, ButtonStyle.Icon, onClick, new Spec { Height = size, Width = size, Icon = icon, IconSize = iconSize, Radius = radius, PaddingX = 0 });
            var tint = button.GetComponent<HubHover>();
            if (color != null) { tint.NormalText = color.Value; tint.IconImage.color = color.Value; }
            if (fill != null) { tint.Normal = fill.Value; tint.Fill.color = fill.Value; tint.Hover = Color.Lerp(fill.Value, HubTheme.GoldWashStrong, 0.5f); tint.Hover.a = Mathf.Max(fill.Value.a, 0.16f); }
            return button;
        }

        // ---- Inputs -------------------------------------------------------------------------

        public static TMP_InputField Input(Transform parent, string name, string placeholder, string value, Action<string> changed, Action<string> submitted = null,
            float height = 44, float radius = 10, Color? fill = null, Color? border = null, string icon = null, int limit = 120)
        {
            var rect = Rect(parent, name);
            rect.sizeDelta = new Vector2(200, height);
            Size(rect, height: height, flexWidth: 1, minHeight: height);
            var image = Background(rect, fill ?? HubTheme.InputFill, radius, true);
            var edge = Border(rect, border ?? HubTheme.InputBorder, radius);
            float left = 14;
            if (icon != null)
            {
                var glyph = Icon(rect, "Icon", icon, 16, HubTheme.Faint);
                Place(glyph.rectTransform, new Vector2(0, 0.5f), new Vector2(13 + 8, 0));
                left = 38;
            }
            var area = Inset(rect, "Text Area", left, 0, 14, 0);
            area.gameObject.AddComponent<RectMask2D>();
            var hint = HubTheme.Text(area, "Placeholder", placeholder, 14, HubTheme.Faint, HubTheme.Face.Body, 0, TextAlignmentOptions.MidlineLeft, false);
            Stretch(hint.rectTransform);
            var text = HubTheme.Text(area, "Text", "", 14, HubTheme.Cream, HubTheme.Face.Body, 0, TextAlignmentOptions.MidlineLeft, false);
            Stretch(text.rectTransform);
            var field = rect.gameObject.AddComponent<TMP_InputField>();
            field.targetGraphic = image;
            field.textViewport = area;
            field.textComponent = text;
            field.placeholder = hint;
            field.characterLimit = limit;
            field.caretColor = HubTheme.Gold;
            field.selectionColor = HubTheme.Alpha(HubTheme.Gold, 0.35f);
            field.customCaretColor = true;
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.text = value ?? "";
            field.onValueChanged.AddListener(v => changed?.Invoke(v));
            if (submitted != null) field.onSubmit.AddListener(v => submitted(v));
            field.onSelect.AddListener(_ => edge.color = HubTheme.Alpha(HubTheme.Gold, 0.6f));
            field.onDeselect.AddListener(_ => edge.color = border ?? HubTheme.InputBorder);
            return field;
        }

        // Vertical scroll list. Returns the content column.
        public static RectTransform ScrollList(Transform parent, string name, out ScrollRect scroll, float spacing = 0, RectOffset padding = null)
        {
            var rect = Rect(parent, name);
            Size(rect, flexHeight: 1, flexWidth: 1, minHeight: 0);
            var hit = Background(rect, Color.clear, 0, true);
            hit.raycastTarget = true;
            var viewport = Fill(rect, "Viewport");
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = Column(viewport, "Content", spacing, padding);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one; content.pivot = new Vector2(0.5f, 1);
            content.offsetMin = content.offsetMax = Vector2.zero;
            FitContent(content, false, true);
            scroll = rect.gameObject.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 28;
            return content;
        }

        // Gold range slider, 0..1.
        public static Slider Range(Transform parent, string name, float value, Action<float> changed, float height = 44)
        {
            var rect = Rect(parent, name);
            Size(rect, height: height, flexWidth: 1, minHeight: height);
            var track = Rect(rect, "Track");
            track.anchorMin = new Vector2(0, 0.5f); track.anchorMax = new Vector2(1, 0.5f); track.pivot = new Vector2(0.5f, 0.5f);
            track.offsetMin = new Vector2(0, -2); track.offsetMax = new Vector2(0, 2);
            Background(track, HubTheme.Rgba(156, 194, 188, 0.2f), 2, true);
            var fillArea = Fill(rect, "Fill Area");
            fillArea.anchorMin = new Vector2(0, 0.5f); fillArea.anchorMax = new Vector2(1, 0.5f);
            fillArea.offsetMin = new Vector2(0, -2); fillArea.offsetMax = new Vector2(0, 2);
            var fill = Rect(fillArea, "Fill");
            Stretch(fill);
            Background(fill, HubTheme.Gold, 2);
            var handleArea = Fill(rect, "Handle Area");
            handleArea.offsetMin = new Vector2(9, 0); handleArea.offsetMax = new Vector2(-9, 0);
            var handle = Rect(handleArea, "Handle");
            handle.anchorMin = new Vector2(0, 0.5f); handle.anchorMax = new Vector2(0, 0.5f); handle.pivot = new Vector2(0.5f, 0.5f);
            handle.sizeDelta = new Vector2(18, 18);
            var knob = handle.gameObject.AddComponent<Image>();
            knob.sprite = HubArt.Disc(); knob.color = HubTheme.Gold; knob.raycastTarget = true;
            var slider = rect.gameObject.AddComponent<Slider>();
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = knob;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0; slider.maxValue = 1;
            slider.value = value;
            var colors = slider.colors;
            colors.highlightedColor = colors.selectedColor = HubTheme.Hex("FFF1CF");
            colors.pressedColor = HubTheme.Hex("FFF8E6");
            slider.colors = colors;
            slider.onValueChanged.AddListener(v => changed?.Invoke(v));
            return slider;
        }

        // Short message that fades after a moment (placeholder destinations, friend notes).
        public static void Toast(Transform parent, string text, float seconds = 2.6f)
        {
            var existing = parent.Find("Toast");
            if (existing != null) UnityEngine.Object.Destroy(existing.gameObject);
            var rect = Centered(parent, "Toast", 420, 40, 0, 354);
            Background(rect, HubTheme.Rgba(12, 27, 32, 0.92f), 10);
            Border(rect, HubTheme.PanelBorderBright, 10);
            var label = HubTheme.Text(rect, "Text", text, 13, HubTheme.Cream, HubTheme.Face.BodyMedium, 0, TextAlignmentOptions.Center, false);
            Stretch(label.rectTransform);
            rect.gameObject.AddComponent<HubFade>().Seconds = seconds;
        }
    }

    // Hover / press / focus / disabled tinting for the handoff's button styles.
    public class HubHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler
    {
        public Image Fill, Border, IconImage;
        public TMP_Text Label;
        public Color Normal, Hover, Pressed, NormalBorder, HoverBorder, NormalText, HoverText, DisabledFill, DisabledText;
        Selectable selectable;
        bool hovered, pressed, selected;

        void Awake() => selectable = GetComponent<Selectable>();
        public void OnPointerEnter(PointerEventData e) => hovered = true;
        public void OnPointerExit(PointerEventData e) { hovered = false; pressed = false; }
        public void OnPointerDown(PointerEventData e) => pressed = true;
        public void OnPointerUp(PointerEventData e) => pressed = false;
        public void OnSelect(BaseEventData e) => selected = true;
        public void OnDeselect(BaseEventData e) => selected = false;

        void Update()
        {
            bool enabledNow = selectable == null || selectable.IsInteractable();
            bool lit = enabledNow && (hovered || selected);
            Color fill = !enabledNow ? DisabledFill : pressed ? Pressed : lit ? Hover : Normal;
            Color text = !enabledNow ? DisabledText : lit ? HoverText : NormalText;
            float k = Mathf.Clamp01(Time.unscaledDeltaTime * 14);
            if (Fill != null) Fill.color = Color.Lerp(Fill.color, fill, k);
            if (Border != null) Border.color = Color.Lerp(Border.color, lit ? HoverBorder : NormalBorder, k);
            if (Label != null) Label.color = Color.Lerp(Label.color, text, k);
            if (IconImage != null) IconImage.color = Color.Lerp(IconImage.color, text, k);
        }
    }

    public class HubFade : MonoBehaviour
    {
        public float Seconds = 2.6f;
        CanvasGroup group;
        float start;
        void Awake() { group = gameObject.AddComponent<CanvasGroup>(); group.blocksRaycasts = false; start = Time.unscaledTime; }
        void Update()
        {
            float t = Time.unscaledTime - start;
            group.alpha = t < 0.2f ? t / 0.2f : t > Seconds - 0.4f ? Mathf.Clamp01((Seconds - t) / 0.4f) : 1;
            if (t >= Seconds) Destroy(gameObject);
        }
    }
}

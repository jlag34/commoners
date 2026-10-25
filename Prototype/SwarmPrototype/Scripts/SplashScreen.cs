using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static SwarmPrototype.HubWidgets;

namespace SwarmPrototype
{
    // Splash (handoff screens/Main.dc.html): title, tagline, START, and two swarms marching in place on a ridge.
    public static class SplashScreen
    {
        public static Button Build(RectTransform root, UnityEngine.Events.UnityAction start)
        {
            // Ground: radial-gradient(ellipse 58% 68% at 50% 44%).
            var gradient = Fill(root, "Ground").gameObject.AddComponent<RadialGradient>();
            gradient.Center = new Vector2(0.5f, 0.56f);
            gradient.Radius = new Vector2(0.58f, 0.68f);
            gradient.Colors = new[] { HubTheme.GroundCenter, HubTheme.Hex("26494C"), HubTheme.Hex("152B32"), HubTheme.Hex("0C1B21") };
            gradient.Stops = new[] { 0f, 0.36f, 0.7f, 1f };
            gradient.raycastTarget = false;

            // Compass rings: 1040 box at left 280, top -136, so the centre sits 384px from the top.
            var compass = Centered(root, "Compass", 1040, 1040, 0, 450 - 384).gameObject.AddComponent<CompassRings>();
            compass.raycastTarget = false;
            compass.color = new Color(1, 1, 1, 0.55f);
            compass.SecondsPerTurn = 180;
            compass.Rings.Add(new CompassRings.Ring { Radius = 500, Dash = 2, Gap = 7, Width = 1, Color = HubTheme.Alpha(HubTheme.Teal, 0.18f) });
            compass.Rings.Add(new CompassRings.Ring { Radius = 380, Dash = 1, Gap = 9, Width = 1, Color = HubTheme.Alpha(HubTheme.Teal, 0.14f) });
            foreach (var p in new[] { new Vector2(0, 500), new Vector2(0, -500), new Vector2(-500, 0), new Vector2(500, 0) })
                compass.Dots.Add(new CompassRings.Dot { Position = p, Radius = 4, Width = 1, Color = HubTheme.Alpha(HubTheme.Gold, 0.5f) });
            foreach (var p in new[] { new Vector2(-354, 354), new Vector2(354, 354), new Vector2(-354, -354), new Vector2(354, -354) })
                compass.Dots.Add(new CompassRings.Dot { Position = p, Radius = 2.5f, Filled = true, Color = HubTheme.Alpha(HubTheme.Teal, 0.4f) });

            // Warm glow resting on the ridge, clipped to its 200px band like the CSS box.
            var glowBox = Inset(root, "Ridge glow", 0, 900 - 96 - 200, 0, 96);
            glowBox.gameObject.AddComponent<RectMask2D>();
            var glow = Fill(glowBox, "Glow").gameObject.AddComponent<RadialGradient>();
            glow.Center = new Vector2(0.5f, 0);
            glow.Radius = new Vector2(0.46f, 1f);
            glow.Colors = new[] { HubTheme.Alpha(HubTheme.Gold, 0.17f), HubTheme.Alpha(HubTheme.Gold, 0) };
            glow.Stops = new[] { 0f, 0.7f };
            glow.raycastTarget = false;

            // Marching swarms, then the ridge so their feet sink a few pixels into the ground.
            var (back, front) = Swarms();
            foreach (var unit in back) Figure(root, unit, 0.55f);
            foreach (var unit in front) Figure(root, unit, 1);
            var ridge = Inset(root, "Ridge", 0, 900 - 220, 0, 0).gameObject.AddComponent<RidgeGraphic>();
            ridge.color = HubTheme.Ridge;
            ridge.raycastTarget = false;

            // Title stack, centred in the area above the bottom 230px (centre line 335px from the top).
            var stack = Inset(root, "Title", 0, 0, 0, 230);
            var badge = Centered(stack, "Badge", 76, 76, 0, 335 - 160);
            Place(Ring(badge, "Edge", 76, 1.5f, HubTheme.Alpha(HubTheme.Gold, 0.55f)).rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero);
            Place(Ring(badge, "Inset", 73, 6, HubTheme.Alpha(HubTheme.Gold, 0.08f)).rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero);
            Stretch(HubTheme.Text(badge, "C", "C", 40, HubTheme.Gold, HubTheme.Face.Logo, 0, TextAlignmentOptions.Center, false).rectTransform);
            badge.gameObject.AddComponent<RiseIn>();

            var title = HubTheme.Text(stack, "Wordmark", "COMMONERS", 132, HubTheme.Cream, HubTheme.Face.Logo, 0.11f, TextAlignmentOptions.Center, false);
            title.rectTransform.anchorMin = title.rectTransform.anchorMax = title.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            title.rectTransform.anchoredPosition = new Vector2(0, 335 - 298);
            title.rectTransform.sizeDelta = new Vector2(1400, 140);
            title.margin = new Vector4(0.11f * 132, 0, 0, 0);
            title.gameObject.AddComponent<RiseIn>();

            var tagline = Centered(stack, "Tagline", 700, 24, 0, 335 - 403);
            var row = Row(tagline, "Row", 22, null, TextAnchor.MiddleCenter, false, false);
            Stretch(row);
            Size(Rule(row, "Left rule", HubTheme.Alpha(HubTheme.Gold, 0.6f)), 64, 1, 0, 0, 64, 1);
            HubTheme.Text(row, "Tagline text", "SMALL BEGINNINGS. MIGHTY SWARMS.", 15, HubTheme.Soft, HubTheme.Face.Wide, 0.32f, TextAlignmentOptions.Center, false);
            Size(Rule(row, "Right rule", HubTheme.Alpha(HubTheme.Gold, 0.6f)), 64, 1, 0, 0, 64, 1);
            tagline.gameObject.AddComponent<RiseIn>().Delay = 0.25f;

            var startButton = Button(stack, "Start", "START", ButtonStyle.Gold, start,
                new Spec { Width = 320, Height = 72, Radius = 10, FontSize = 22, Tracking = 0.24f, Shadow = true });
            var startRect = (RectTransform)startButton.transform;
            startRect.anchorMin = startRect.anchorMax = startRect.pivot = new Vector2(0.5f, 0.5f);
            startRect.anchoredPosition = new Vector2(0, 335 - 512);
            startRect.gameObject.AddComponent<RiseIn>().Delay = 0.5f;

            Footer(root);
            return startButton;
        }

        static void Footer(RectTransform root)
        {
            var left = Fixed(root, "Local play", 48, 900 - 36 - 16, 200, 16);
            var row = Row(left, "Row", 10, null, TextAnchor.MiddleLeft, false, false);
            Stretch(row);
            Disc(row, "Dot", 8, HubTheme.Online);
            HubTheme.Text(row, "Label", "LOCAL PLAY", 12, HubTheme.Hex("9FC4BC"), HubTheme.Face.Wide, 0.18f, TextAlignmentOptions.MidlineLeft, false);
            var right = Fixed(root, "Preview build", 1600 - 48 - 200, 900 - 36 - 16, 200, 16);
            Stretch(HubTheme.Text(right, "Label", "PREVIEW BUILD", 12, HubTheme.Tertiary, HubTheme.Face.Wide, 0.18f, TextAlignmentOptions.MidlineRight, false).rectTransform);
        }

        struct Unit { public float Left, Bottom, W, H, Delay; public bool Flip, Fork; }

        // Same seeded layout as the prototype (one LCG shared by both rows) so the composition matches the design.
        static (Unit[] back, Unit[] front) Swarms()
        {
            int seed = 11;
            float Rnd() { seed = (seed * 9301 + 49297) % 233280; return seed / 233280f; }
            Unit[] Row(bool back)
            {
                int count = back ? 15 : 13;
                var units = new Unit[count * 2];
                int n = 0;
                for (int side = 0; side < 2; side++)
                    for (int i = 0; i < count; i++)
                    {
                        float t = i / (float)(count - 1);
                        bool leader = !back && i == count - 1;
                        float scale = leader ? 1.55f : (back ? 0.78f : 1f) + Rnd() * 0.18f;
                        int w = Mathf.RoundToInt(28 * scale * 1.2f), h = Mathf.RoundToInt(36 * scale * 1.2f);
                        float span = back ? 600 : 560;
                        float x = 56 + t * span + (back ? 18 : 0) + Rnd() * 14;
                        if (side == 1) x = 1600 - x - w;
                        float delay = -Rnd() * 0.9f;
                        bool fork = !leader && Rnd() > 0.38f;
                        units[n++] = new Unit { Left = Mathf.Round(x), Bottom = back ? 120 : 115, W = w, H = h, Flip = side == 1, Delay = delay, Fork = fork };
                    }
                return units;
            }
            var backRow = Row(true);
            return (backRow, Row(false));
        }

        static void Figure(RectTransform root, Unit unit, float opacity)
        {
            var rect = Rect(root, unit.Fork ? "Forkbearer" : "Swordhand");
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0);
            rect.anchoredPosition = new Vector2(unit.Left + unit.W / 2, unit.Bottom);
            rect.sizeDelta = new Vector2(unit.W, unit.H);
            if (unit.Flip) rect.localScale = new Vector3(-1, 1, 1);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = HubArt.Silhouette(unit.Fork);
            image.color = new Color(1, 1, 1, opacity);
            image.raycastTarget = false;
            rect.gameObject.AddComponent<Bob>().Delay = unit.Delay;
        }
    }
}

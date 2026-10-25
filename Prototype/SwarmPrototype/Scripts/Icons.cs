using System.Collections.Generic;
using UnityEngine;

namespace SwarmPrototype
{
    // Procedural icons for the shop, banner and floating rewards, so the UI needs
    // no image assets. Each is a few shapes painted into a 64 x 64 texture.
    public static class Icons
    {
        const int Size = 64;
        static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();

        public static Texture2D Get(string name)
        {
            if (cache.TryGetValue(name, out var tex) && tex != null) return tex;
            var p = new Painter();
            Paint(p, name);
            tex = p.Bake();
            cache[name] = tex;
            return tex;
        }

        static readonly Color Steel = new Color(0.82f, 0.86f, 0.92f);
        static readonly Color Gold = new Color(1f, 0.8f, 0.25f);
        static readonly Color GoldDark = new Color(0.75f, 0.52f, 0.1f);
        static readonly Color Wood = new Color(0.45f, 0.3f, 0.15f);
        static readonly Color Red = new Color(0.9f, 0.2f, 0.2f);
        static readonly Color Green = new Color(0.4f, 0.9f, 0.4f);
        static readonly Color Blue = new Color(0.4f, 0.7f, 1f);
        static readonly Color Grey = new Color(0.6f, 0.62f, 0.66f);
        static readonly Color Dark = new Color(0.12f, 0.1f, 0.1f);
        static readonly Color Orange = new Color(1f, 0.55f, 0.2f);
        static readonly Color Bone = new Color(0.92f, 0.9f, 0.82f);

        static void Paint(Painter p, string name)
        {
            switch (name)
            {
                case "coin":
                    p.Circle(32, 32, 26, GoldDark); p.Circle(32, 32, 22, Gold); p.Ring(32, 32, 15, 3, GoldDark); p.Circle(24, 40, 5, new Color(1f, 0.95f, 0.7f));
                    break;
                case "skull":
                    p.Circle(32, 36, 22, Bone); p.Rect(20, 8, 24, 16, Bone); p.Circle(23, 38, 6, Dark); p.Circle(41, 38, 6, Dark); p.Poly(new[] { new Vector2(32, 24), new Vector2(28, 32), new Vector2(36, 32) }, Dark);
                    p.Rect(25, 8, 3, 10, Dark); p.Rect(31, 8, 3, 10, Dark); p.Rect(37, 8, 3, 10, Dark);
                    break;
                case "eliteMarker":
                    // Minimap marker: skull on a dark disc with an orange ring, readable at 18 px.
                    p.Circle(32, 32, 30, new Color(0.35f, 0.1f, 0f)); p.Ring(32, 32, 28, 5, Orange); p.Circle(32, 32, 24, Dark);
                    p.Circle(32, 34, 13, Bone); p.Rect(25, 14, 14, 10, Bone); p.Circle(27, 36, 4, Dark); p.Circle(37, 36, 4, Dark);
                    p.Rect(28, 14, 2, 6, Dark); p.Rect(31, 14, 2, 6, Dark); p.Rect(34, 14, 2, 6, Dark);
                    break;
                case "lesser": Gem(p, GameState.EssenceColors[0]); break;
                case "greater": Gem(p, GameState.EssenceColors[1]); break;
                case "elite": Gem(p, GameState.EssenceColors[2]); break;
                case "strength": Sword(p, 12, 12, 54, 54, Steel); break;
                case "attackSpeed":
                    p.Poly(new[] { new Vector2(36, 60), new Vector2(18, 32), new Vector2(31, 32), new Vector2(26, 6), new Vector2(46, 36), new Vector2(33, 36) }, Gold);
                    break;
                case "health":
                    p.Circle(22, 40, 12, Red); p.Circle(42, 40, 12, Red); p.Poly(new[] { new Vector2(10, 36), new Vector2(54, 36), new Vector2(32, 8) }, Red);
                    break;
                case "armor":
                    p.Poly(new[] { new Vector2(32, 6), new Vector2(54, 16), new Vector2(50, 40), new Vector2(32, 58), new Vector2(14, 40), new Vector2(10, 16) }, Grey);
                    p.Poly(new[] { new Vector2(32, 12), new Vector2(48, 19), new Vector2(45, 38), new Vector2(32, 52), new Vector2(19, 38), new Vector2(16, 19) }, Blue);
                    p.Rect(30, 18, 4, 28, Steel); p.Rect(22, 30, 20, 4, Steel);
                    break;
                case "moveSpeed":
                    Chevron(p, 14, Green); Chevron(p, 32, Green);
                    break;
                case "lifesteal":
                    p.Circle(32, 24, 16, Red); p.Poly(new[] { new Vector2(17, 28), new Vector2(47, 28), new Vector2(32, 60) }, Red); p.Circle(26, 22, 4, new Color(1f, 0.6f, 0.6f));
                    break;
                case "evasion":
                    p.Poly(Ellipse(32, 32, 28, 15), Bone); p.Circle(32, 32, 11, Blue); p.Circle(32, 32, 5, Dark); p.Line(10, 10, 54, 54, 4, Red);
                    break;
                case "regen":
                    p.Rect(26, 10, 12, 44, Green); p.Rect(10, 26, 44, 12, Green);
                    break;
                case "towerRegen": Tower(p, Grey); p.Rect(44, 38, 6, 20, Green); p.Rect(37, 45, 20, 6, Green); break;
                case "towerDamage": Tower(p, Grey); Sword(p, 34, 30, 58, 58, Orange); break;
                case "towerRange": Tower(p, Grey); p.Ring(32, 24, 24, 3, Blue); p.Ring(32, 24, 16, 3, Blue); break;
                case "crit":
                    Star(p, 32, 32, 28, 12, 8, Gold); Star(p, 32, 32, 14, 6, 8, Orange);
                    break;
                case "splash":
                    p.Ring(32, 32, 26, 4, Blue); p.Ring(32, 32, 16, 4, Blue); p.Circle(32, 32, 6, Blue);
                    break;
                case "unitCap":
                    Figure(p, 32, 30, 1.15f, Steel); Figure(p, 14, 22, 0.85f, Grey); Figure(p, 50, 22, 0.85f, Grey);
                    p.Rect(44, 44, 12, 4, Green); p.Rect(48, 40, 4, 12, Green);
                    break;
                case "spawnRate":
                    p.Poly(new[] { new Vector2(14, 56), new Vector2(50, 56), new Vector2(32, 32) }, Bone); p.Poly(new[] { new Vector2(14, 8), new Vector2(50, 8), new Vector2(32, 32) }, Bone);
                    p.Poly(new[] { new Vector2(22, 8), new Vector2(42, 8), new Vector2(32, 22) }, Gold); p.Rect(12, 54, 40, 4, Wood); p.Rect(12, 6, 40, 4, Wood);
                    break;
                case "bounty":
                    p.Circle(22, 22, 14, GoldDark); p.Circle(22, 22, 11, Gold); p.Circle(42, 28, 14, GoldDark); p.Circle(42, 28, 11, Gold); p.Circle(32, 44, 14, GoldDark); p.Circle(32, 44, 11, Gold);
                    break;
                case "reincarnate":
                    p.Ring(32, 32, 20, 6, Orange); p.Rect(30, 6, 14, 14, Dark); p.Poly(new[] { new Vector2(44, 22), new Vector2(58, 12), new Vector2(54, 28) }, Orange);
                    break;
                default:
                    p.Circle(32, 32, 24, Grey);
                    break;
            }
        }

        static void Gem(Painter p, Color c)
        {
            Color dark = c * 0.6f; dark.a = 1f;
            Color light = Color.Lerp(c, Color.white, 0.5f);
            p.Poly(new[] { new Vector2(32, 60), new Vector2(56, 36), new Vector2(44, 10), new Vector2(20, 10), new Vector2(8, 36) }, dark);
            p.Poly(new[] { new Vector2(32, 54), new Vector2(50, 36), new Vector2(41, 15), new Vector2(23, 15), new Vector2(14, 36) }, c);
            p.Poly(new[] { new Vector2(23, 15), new Vector2(32, 36), new Vector2(14, 36) }, light);
        }

        static void Sword(Painter p, float x0, float y0, float x1, float y1, Color blade)
        {
            // Blade from bottom-left to top-right, guard and grip at the bottom-left.
            Vector2 a = new Vector2(x0, y0), b = new Vector2(x1, y1);
            Vector2 dir = (b - a).normalized, side = new Vector2(-dir.y, dir.x);
            Vector2 guard = a + dir * 14f;
            p.Line(guard.x, guard.y, b.x, b.y, 7, blade);
            p.Poly(new[] { b + dir * 6f, b + side * 3.5f, b - side * 3.5f }, blade);
            p.Line(guard.x - side.x * 9f, guard.y - side.y * 9f, guard.x + side.x * 9f, guard.y + side.y * 9f, 5, Gold);
            p.Line(a.x, a.y, guard.x, guard.y, 6, Wood);
        }

        static void Tower(Painter p, Color c)
        {
            p.Rect(18, 6, 28, 38, c); p.Rect(14, 40, 36, 8, c);
            p.Rect(14, 48, 8, 8, c); p.Rect(28, 48, 8, 8, c); p.Rect(42, 48, 8, 8, c);
            p.Rect(28, 6, 8, 14, Dark);
        }

        static void Chevron(Painter p, float x, Color c)
        {
            p.Line(x, 10, x + 18, 32, 7, c); p.Line(x + 18, 32, x, 54, 7, c);
        }

        static Vector2[] Ellipse(float cx, float cy, float rx, float ry)
        {
            var pts = new Vector2[24];
            for (int i = 0; i < pts.Length; i++)
            {
                float a = i * Mathf.PI * 2f / pts.Length;
                pts[i] = new Vector2(cx + Mathf.Cos(a) * rx, cy + Mathf.Sin(a) * ry);
            }
            return pts;
        }

        // A stick figure standing on `cy` - its height, centred on `cx`.
        static void Figure(Painter p, float cx, float cy, float k, Color c)
        {
            p.Circle(cx, cy + 16f * k, 7f * k, c);
            p.Poly(new[] { new Vector2(cx - 9f * k, cy - 14f * k), new Vector2(cx + 9f * k, cy - 14f * k), new Vector2(cx + 7f * k, cy + 7f * k), new Vector2(cx - 7f * k, cy + 7f * k) }, c);
        }

        static void Star(Painter p, float cx, float cy, float outer, float inner, int points, Color c)
        {
            var pts = new Vector2[points * 2];
            for (int i = 0; i < pts.Length; i++)
            {
                float r = i % 2 == 0 ? outer : inner;
                float a = i * Mathf.PI / points - Mathf.PI * 0.5f;
                pts[i] = new Vector2(cx + Mathf.Cos(a) * r, cy + Mathf.Sin(a) * r);
            }
            p.Poly(pts, c);
        }

        // Tiny rasterizer: y runs up, like Texture2D.
        class Painter
        {
            readonly Color[] px = new Color[Size * Size];

            public void Circle(float cx, float cy, float r, Color c)
            {
                for (int y = 0; y < Size; y++)
                    for (int x = 0; x < Size; x++)
                    {
                        float d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                        Blend(x, y, c, Mathf.Clamp01(r - d + 0.5f));
                    }
            }

            public void Ring(float cx, float cy, float r, float thickness, Color c)
            {
                for (int y = 0; y < Size; y++)
                    for (int x = 0; x < Size; x++)
                    {
                        float d = Mathf.Abs(Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy)) - r);
                        Blend(x, y, c, Mathf.Clamp01(thickness * 0.5f - d + 0.5f));
                    }
            }

            public void Rect(float x0, float y0, float w, float h, Color c)
            {
                for (int y = Mathf.Max(0, (int)y0); y < Mathf.Min(Size, (int)(y0 + h)); y++)
                    for (int x = Mathf.Max(0, (int)x0); x < Mathf.Min(Size, (int)(x0 + w)); x++)
                        Blend(x, y, c, 1f);
            }

            public void Line(float x0, float y0, float x1, float y1, float thickness, Color c)
            {
                Vector2 a = new Vector2(x0, y0), b = new Vector2(x1, y1);
                for (int y = 0; y < Size; y++)
                    for (int x = 0; x < Size; x++)
                    {
                        Vector2 q = new Vector2(x + 0.5f, y + 0.5f);
                        float t = Mathf.Clamp01(Vector2.Dot(q - a, b - a) / Mathf.Max(0.0001f, (b - a).sqrMagnitude));
                        float d = Vector2.Distance(q, a + (b - a) * t);
                        Blend(x, y, c, Mathf.Clamp01(thickness * 0.5f - d + 0.5f));
                    }
            }

            public void Poly(Vector2[] pts, Color c)
            {
                for (int y = 0; y < Size; y++)
                    for (int x = 0; x < Size; x++)
                        if (Inside(pts, new Vector2(x + 0.5f, y + 0.5f))) Blend(x, y, c, 1f);
            }

            static bool Inside(Vector2[] pts, Vector2 q)
            {
                bool inside = false;
                for (int i = 0, j = pts.Length - 1; i < pts.Length; j = i++)
                {
                    if ((pts[i].y > q.y) != (pts[j].y > q.y) &&
                        q.x < (pts[j].x - pts[i].x) * (q.y - pts[i].y) / (pts[j].y - pts[i].y) + pts[i].x)
                        inside = !inside;
                }
                return inside;
            }

            void Blend(int x, int y, Color c, float a)
            {
                if (a <= 0f) return;
                int i = y * Size + x;
                Color d = px[i];
                float outA = a + d.a * (1f - a);
                Color rgb = (c * a + d * d.a * (1f - a)) / Mathf.Max(0.0001f, outA);
                px[i] = new Color(rgb.r, rgb.g, rgb.b, outA);
            }

            public Texture2D Bake()
            {
                var t = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                t.SetPixels(px);
                t.Apply();
                return t;
            }
        }
    }
}

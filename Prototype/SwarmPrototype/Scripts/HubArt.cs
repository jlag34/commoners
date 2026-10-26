using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SwarmPrototype
{
    // Every sprite the splash and home hub need is painted in code at startup: rounded panels, outlines,
    // soft shadows, the handoff's line icons and the splash silhouettes. Painter works in SVG-style
    // y-down design units so the icon paths from the handoff can be transcribed almost literally.
    public static class HubArt
    {
        static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();
        const int IconScale = 4;

        static Sprite Cached(string key, Func<Sprite> make)
        {
            if (cache.TryGetValue(key, out var sprite) && sprite != null) return sprite;
            sprite = make();
            cache[key] = sprite;
            return sprite;
        }

        // 9-sliced rounded rectangle fill. 1 texel = 1 canvas unit.
        public static Sprite Rounded(float radius) => Cached("fill:" + radius, () =>
        {
            int r = Mathf.CeilToInt(radius), side = r * 2 + 8;
            var p = new Painter(side, side, 1);
            p.RoundedBox(0, 0, side, side, radius, Color.white);
            return p.Sliced(r + 3);
        });

        // 9-sliced rounded outline of the given stroke width.
        public static Sprite Outline(float radius, float width) => Cached("line:" + radius + ":" + width, () =>
        {
            int r = Mathf.CeilToInt(Mathf.Max(radius, width)) + 1, side = r * 2 + 8;
            var p = new Painter(side, side, 1);
            p.RoundedBorder(0, 0, side, side, radius, width, Color.white);
            return p.Sliced(r + 3);
        });

        // 9-sliced soft shadow: the sprite is `blur` larger than the box on every side.
        public static Sprite Shadow(float radius, float blur) => Cached("shadow:" + radius + ":" + blur, () =>
        {
            int pad = Mathf.CeilToInt(blur), r = Mathf.CeilToInt(radius), side = (pad + r) * 2 + 8;
            var p = new Painter(side, side, 1);
            p.SoftBox(pad, pad, side - pad * 2, side - pad * 2, radius, blur, Color.white);
            return p.Sliced(pad + r + 3);
        });

        public static Sprite Disc() => Cached("disc", () =>
        {
            var p = new Painter(64, 64, 1);
            p.Disc(32, 32, 31.5f, Color.white);
            return p.Simple();
        });

        // Hollow circle at an exact pixel size (not sliced), so thin rings stay crisp.
        public static Sprite Ring(float size, float width) => Cached("ring:" + size + ":" + width, () =>
        {
            int side = Mathf.CeilToInt(size) * 2;
            var p = new Painter(side, side, 2);
            p.Ring(size / 2, size / 2, size / 2 - width / 2, width, Color.white);
            return p.Simple();
        });

        // Dashed rounded square (the empty party slot).
        public static Sprite DashedBox(float size, float radius, float width) => Cached("dashed:" + size + ":" + radius + ":" + width, () =>
        {
            int side = Mathf.CeilToInt(size) * IconScale;
            var p = new Painter(side, side, IconScale);
            p.DashedRoundedBorder(width / 2, width / 2, size - width, size - width, radius, width, width * 2.6f, width * 2.2f, Color.white);
            return p.Simple();
        });

        // Line icons from the handoff, transcribed from their 24x24 SVG paths.
        public static Sprite Icon(string name) => Cached("icon:" + name, () =>
        {
            var p = new Painter(24 * IconScale, 24 * IconScale, IconScale);
            var c = Color.white;
            switch (name)
            {
                case "gear":
                    p.Ring(12, 12, 3, 1.8f, c);
                    p.Ring(12, 12, 7.4f, 1.8f, c);
                    for (int i = 0; i < 8; i++)
                    {
                        float a = i * Mathf.PI / 4;
                        p.Stroke(12 + Mathf.Cos(a) * 6.8f, 12 + Mathf.Sin(a) * 6.8f, 12 + Mathf.Cos(a) * 9.6f, 12 + Mathf.Sin(a) * 9.6f, 2.8f, c);
                    }
                    break;
                case "add-friend":
                    p.Ring(9, 8, 4, 1.8f, c);
                    p.Arc(9, 21, 7, 180, 360, 1.8f, c);
                    p.Stroke(19, 8, 19, 14, 1.8f, c);
                    p.Stroke(16, 11, 22, 11, 1.8f, c);
                    break;
                case "search":
                    p.Ring(11, 11, 7, 2, c);
                    p.Stroke(16.5f, 16.5f, 20, 20, 2, c);
                    break;
                case "check":
                    p.Polyline(new[] { new Vector2(5, 12.5f), new Vector2(9.5f, 17), new Vector2(19, 7.5f) }, 2.2f, c, false);
                    break;
                case "close":
                    p.Stroke(6, 6, 18, 18, 2, c);
                    p.Stroke(18, 6, 6, 18, 2, c);
                    break;
                case "close-small":
                    p.Stroke(6, 6, 18, 18, 2.4f, c);
                    p.Stroke(18, 6, 6, 18, 2.4f, c);
                    break;
                case "message":
                    p.Polyline(new[] { new Vector2(4, 5), new Vector2(20, 5), new Vector2(20, 16), new Vector2(9, 16), new Vector2(4, 20) }, 1.8f, c, true);
                    break;
                case "plus":
                    p.Stroke(12, 5, 12, 19, 2, c);
                    p.Stroke(5, 12, 19, 12, 2, c);
                    break;
                case "dots":
                    p.Disc(5, 12, 1.8f, c); p.Disc(12, 12, 1.8f, c); p.Disc(19, 12, 1.8f, c);
                    break;
                case "send":
                    p.Polyline(new[] { new Vector2(4, 12), new Vector2(20, 4), new Vector2(14, 20), new Vector2(11.5f, 13.5f) }, 1.9f, c, true);
                    p.Stroke(11.5f, 13.5f, 20, 4, 1.9f, c);
                    break;
                case "rotate":
                    p.Arc(12, 12, 9, 180, 325, 1.8f, c);
                    p.Polyline(new[] { new Vector2(21, 3), new Vector2(21, 8), new Vector2(16, 8) }, 1.8f, c, false);
                    p.Arc(12, 12, 9, 0, 145, 1.8f, c);
                    p.Polyline(new[] { new Vector2(3, 21), new Vector2(3, 16), new Vector2(8, 16) }, 1.8f, c, false);
                    break;
                case "crown":
                    // 16x12 artwork centred in the 24 box.
                    var crown = new[] { new Vector2(1, 11), new Vector2(2, 3), new Vector2(5.5f, 6.5f), new Vector2(8, 1), new Vector2(10.5f, 6.5f), new Vector2(14, 3), new Vector2(15, 11) };
                    for (int i = 0; i < crown.Length; i++) crown[i] += new Vector2(4, 6);
                    p.Polygon(crown, HubTheme.Gold);
                    p.Polyline(crown, 1, HubTheme.DotRing, true);
                    break;
            }
            return p.Simple();
        });

        // Splash marching silhouettes: 28x36 artwork, fork or sword.
        public static Sprite Silhouette(bool fork) => Cached("silhouette:" + fork, () =>
        {
            var p = new Painter(28 * IconScale, 36 * IconScale, IconScale);
            var c = HubTheme.Silhouette;
            p.Disc(12, 7, 4.6f, c);
            var body = new List<Vector2> { new Vector2(6.5f, 13) };
            for (int i = 1; i <= 8; i++) body.Add(Painter.Quad(new Vector2(6.5f, 13), new Vector2(12, 10.5f), new Vector2(17.5f, 13), i / 8f));
            body.Add(new Vector2(19, 25)); body.Add(new Vector2(5, 25));
            p.Polygon(body.ToArray(), c);
            p.RoundedBox(7, 24, 3.6f, 10, 1, c);
            p.RoundedBox(13.4f, 24, 3.6f, 10, 1, c);
            if (fork)
            {
                p.Stroke(21.2f, 7, 19.6f, 35, 1.6f, c);
                p.Polyline(new[] { new Vector2(18.4f, 1.8f), new Vector2(18.8f, 7.2f), new Vector2(23.6f, 7.2f), new Vector2(24, 1.8f) }, 1.6f, c, false);
                p.Stroke(21.2f, 1.4f, 21.2f, 7.2f, 1.6f, c);
            }
            else
            {
                p.Stroke(17.5f, 20, 26, 3.5f, 2, c);
                p.Stroke(15.6f, 17.4f, 19.8f, 21.2f, 2, c);
            }
            return p.Simple();
        });

        // Anti-aliased software rasteriser. Coordinates are design units, y down; `scale` texels per unit.
        public class Painter
        {
            readonly int w, h;
            readonly float s;
            readonly Color[] px;

            public Painter(int width, int height, float scale) { w = width; h = height; s = scale; px = new Color[width * height]; }

            void Blend(int x, int y, Color c, float a)
            {
                if (x < 0 || y < 0 || x >= w || y >= h) return;
                a = Mathf.Clamp01(a) * c.a;
                if (a <= 0) return;
                int i = (h - 1 - y) * w + x;
                var d = px[i];
                float outA = a + d.a * (1 - a);
                px[i] = new Color(
                    (c.r * a + d.r * d.a * (1 - a)) / outA,
                    (c.g * a + d.g * d.a * (1 - a)) / outA,
                    (c.b * a + d.b * d.a * (1 - a)) / outA, outA);
            }

            public static Vector2 Quad(Vector2 a, Vector2 b, Vector2 c, float t) => (1 - t) * (1 - t) * a + 2 * (1 - t) * t * b + t * t * c;

            public void Disc(float cx, float cy, float r, Color c)
            {
                cx *= s; cy *= s; r *= s;
                for (int y = Mathf.FloorToInt(cy - r - 1); y <= cy + r + 1; y++)
                    for (int x = Mathf.FloorToInt(cx - r - 1); x <= cx + r + 1; x++)
                        Blend(x, y, c, r - Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy)) + 0.5f);
            }

            public void Ring(float cx, float cy, float r, float width, Color c)
            {
                cx *= s; cy *= s; r *= s; width *= s;
                float outer = r + width / 2 + 1;
                for (int y = Mathf.FloorToInt(cy - outer); y <= cy + outer; y++)
                    for (int x = Mathf.FloorToInt(cx - outer); x <= cx + outer; x++)
                    {
                        float d = Mathf.Abs(Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy)) - r);
                        Blend(x, y, c, width / 2 - d + 0.5f);
                    }
            }

            public void Stroke(float x0, float y0, float x1, float y1, float width, Color c)
            {
                var a = new Vector2(x0, y0) * s; var b = new Vector2(x1, y1) * s; width *= s;
                float pad = width / 2 + 1;
                int minX = Mathf.FloorToInt(Mathf.Min(a.x, b.x) - pad), maxX = Mathf.CeilToInt(Mathf.Max(a.x, b.x) + pad);
                int minY = Mathf.FloorToInt(Mathf.Min(a.y, b.y) - pad), maxY = Mathf.CeilToInt(Mathf.Max(a.y, b.y) + pad);
                for (int y = minY; y <= maxY; y++)
                    for (int x = minX; x <= maxX; x++)
                        Blend(x, y, c, width / 2 - SegmentDistance(new Vector2(x + 0.5f, y + 0.5f), a, b) + 0.5f);
            }

            public void Polyline(Vector2[] pts, float width, Color c, bool closed)
            {
                for (int i = 0; i < pts.Length - 1; i++) Stroke(pts[i].x, pts[i].y, pts[i + 1].x, pts[i + 1].y, width, c);
                if (closed) Stroke(pts[pts.Length - 1].x, pts[pts.Length - 1].y, pts[0].x, pts[0].y, width, c);
            }

            // Angles in degrees, SVG convention (0 = +x, clockwise on screen).
            public void Arc(float cx, float cy, float r, float fromDeg, float toDeg, float width, Color c)
            {
                int steps = Mathf.Max(6, Mathf.CeilToInt(Mathf.Abs(toDeg - fromDeg) / 6));
                var pts = new Vector2[steps + 1];
                for (int i = 0; i <= steps; i++)
                {
                    float a = Mathf.Deg2Rad * Mathf.Lerp(fromDeg, toDeg, i / (float)steps);
                    pts[i] = new Vector2(cx + Mathf.Cos(a) * r, cy + Mathf.Sin(a) * r);
                }
                Polyline(pts, width, c, false);
            }

            public void Polygon(Vector2[] pts, Color c)
            {
                var p = new Vector2[pts.Length];
                float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                for (int i = 0; i < pts.Length; i++)
                {
                    p[i] = pts[i] * s;
                    minX = Mathf.Min(minX, p[i].x); maxX = Mathf.Max(maxX, p[i].x);
                    minY = Mathf.Min(minY, p[i].y); maxY = Mathf.Max(maxY, p[i].y);
                }
                for (int y = Mathf.FloorToInt(minY) - 1; y <= maxY + 1; y++)
                    for (int x = Mathf.FloorToInt(minX) - 1; x <= maxX + 1; x++)
                    {
                        var q = new Vector2(x + 0.5f, y + 0.5f);
                        bool inside = false;
                        float d = float.MaxValue;
                        for (int i = 0, j = p.Length - 1; i < p.Length; j = i++)
                        {
                            if ((p[i].y > q.y) != (p[j].y > q.y) && q.x < (p[j].x - p[i].x) * (q.y - p[i].y) / (p[j].y - p[i].y) + p[i].x) inside = !inside;
                            d = Mathf.Min(d, SegmentDistance(q, p[i], p[j]));
                        }
                        Blend(x, y, c, inside ? Mathf.Min(1, d + 0.5f) : Mathf.Max(0, 0.5f - d));
                    }
            }

            public void RoundedBox(float x, float y, float width, float height, float radius, Color c)
            {
                x *= s; y *= s; width *= s; height *= s; radius *= s;
                for (int py = Mathf.FloorToInt(y) - 1; py <= y + height + 1; py++)
                    for (int px2 = Mathf.FloorToInt(x) - 1; px2 <= x + width + 1; px2++)
                        Blend(px2, py, c, 0.5f - BoxDistance(new Vector2(px2 + 0.5f, py + 0.5f), x, y, width, height, radius));
            }

            public void RoundedBorder(float x, float y, float width, float height, float radius, float stroke, Color c)
            {
                x *= s; y *= s; width *= s; height *= s; radius *= s; stroke *= s;
                for (int py = Mathf.FloorToInt(y) - 1; py <= y + height + 1; py++)
                    for (int px2 = Mathf.FloorToInt(x) - 1; px2 <= x + width + 1; px2++)
                    {
                        float d = BoxDistance(new Vector2(px2 + 0.5f, py + 0.5f), x, y, width, height, radius);
                        Blend(px2, py, c, stroke / 2 - Mathf.Abs(d + stroke / 2) + 0.5f);
                    }
            }

            public void SoftBox(float x, float y, float width, float height, float radius, float blur, Color c)
            {
                x *= s; y *= s; width *= s; height *= s; radius *= s; blur *= s;
                for (int py = 0; py < h; py++)
                    for (int px2 = 0; px2 < w; px2++)
                    {
                        float d = BoxDistance(new Vector2(px2 + 0.5f, py + 0.5f), x, y, width, height, radius);
                        float t = Mathf.Clamp01((d + blur) / (2 * blur));
                        Blend(px2, py, c, 1 - t * t * (3 - 2 * t));
                    }
            }

            public void DashedRoundedBorder(float x, float y, float width, float height, float radius, float stroke, float dash, float gap, Color c)
            {
                // Perimeter as a polyline (straight runs and quarter arcs), then resample it at a fixed step and
                // stroke the samples that fall inside a dash. Bounded for-loops only: an earlier version walked the
                // path with float accumulation and could spin forever when a tiny remainder was absorbed by `+=`.
                var path = new List<Vector2>();
                void Corner(float cx, float cy, float a0)
                {
                    for (int i = 0; i <= 8; i++)
                    {
                        float a = Mathf.Deg2Rad * (a0 + i * 90f / 8);
                        path.Add(new Vector2(cx + Mathf.Cos(a) * radius, cy + Mathf.Sin(a) * radius));
                    }
                }
                Corner(x + width - radius, y + radius, -90);
                Corner(x + width - radius, y + height - radius, 0);
                Corner(x + radius, y + height - radius, 90);
                Corner(x + radius, y + radius, 180);
                path.Add(path[0]);
                var cumulative = new float[path.Count];
                for (int i = 1; i < path.Count; i++) cumulative[i] = cumulative[i - 1] + Vector2.Distance(path[i - 1], path[i]);
                float total = cumulative[path.Count - 1], period = Mathf.Max(0.01f, dash + gap);
                Vector2 PointAt(float s)
                {
                    for (int i = 1; i < path.Count; i++)
                        if (s <= cumulative[i])
                        {
                            float len = cumulative[i] - cumulative[i - 1];
                            return len <= 1e-5f ? path[i] : Vector2.Lerp(path[i - 1], path[i], (s - cumulative[i - 1]) / len);
                        }
                    return path[path.Count - 1];
                }
                bool On(float s) => s % period < dash;
                const float step = 0.25f;
                int samples = Mathf.Clamp(Mathf.CeilToInt(total / step), 1, 4096);
                var prev = PointAt(0);
                bool prevOn = On(0);
                for (int i = 1; i <= samples; i++)
                {
                    float s = Mathf.Min(total, i * step);
                    var point = PointAt(s);
                    bool on = On(s);
                    if (on && prevOn) Stroke(prev.x, prev.y, point.x, point.y, stroke, c);
                    prev = point;
                    prevOn = on;
                }
            }

            static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
            {
                var ab = b - a;
                float t = ab.sqrMagnitude < 1e-6f ? 0 : Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                return Vector2.Distance(p, a + ab * t);
            }

            static float BoxDistance(Vector2 p, float x, float y, float width, float height, float radius)
            {
                var half = new Vector2(width / 2, height / 2);
                var q = new Vector2(Mathf.Abs(p.x - (x + half.x)), Mathf.Abs(p.y - (y + half.y))) - half + Vector2.one * radius;
                return Vector2.Max(q, Vector2.zero).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0) - radius;
            }

            Texture2D Bake(string name)
            {
                var texture = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = name, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                texture.SetPixels(px);
                texture.Apply();
                return texture;
            }

            public Sprite Sliced(int border)
            {
                var texture = Bake("Hub sliced");
                return Sprite.Create(texture, new Rect(0, 0, w, h), Vector2.one * 0.5f, 100, 0, SpriteMeshType.FullRect, Vector4.one * border);
            }

            public Sprite Simple()
            {
                var texture = Bake("Hub sprite");
                return Sprite.Create(texture, new Rect(0, 0, w, h), Vector2.one * 0.5f, 100, 0, SpriteMeshType.FullRect);
            }
        }
    }

    // CSS radial-gradient(ellipse rx ry at cx cy, stops...). Center and radius are fractions of the rect.
    [RequireComponent(typeof(CanvasRenderer))]
    public class RadialGradient : MaskableGraphic
    {
        public Vector2 Center = new Vector2(0.5f, 0.5f), Radius = new Vector2(0.5f, 0.5f);
        public Color[] Colors = { Color.white, Color.black };
        public float[] Stops = { 0, 1 };

        Color At(float t)
        {
            if (t <= Stops[0]) return Colors[0];
            for (int i = 1; i < Stops.Length; i++)
                if (t <= Stops[i]) return Color.Lerp(Colors[i - 1], Colors[i], Mathf.InverseLerp(Stops[i - 1], Stops[i], t));
            return Colors[Colors.Length - 1];
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = rectTransform.rect;
            var c = new Vector2(r.x + r.width * Center.x, r.y + r.height * Center.y);
            var rad = new Vector2(Mathf.Max(1, r.width * Radius.x), Mathf.Max(1, r.height * Radius.y));
            float tMax = 1;
            foreach (var corner in new[] { new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin), new Vector2(r.xMin, r.yMax), new Vector2(r.xMax, r.yMax) })
                tMax = Mathf.Max(tMax, new Vector2((corner.x - c.x) / rad.x, (corner.y - c.y) / rad.y).magnitude);
            const int rings = 36, segments = 64;
            vh.AddVert(c, At(0) * color, Vector2.zero);
            for (int i = 1; i <= rings; i++)
            {
                float t = tMax * i / rings;
                var tint = At(t) * color;
                for (int j = 0; j < segments; j++)
                {
                    float a = j * Mathf.PI * 2 / segments;
                    vh.AddVert(c + new Vector2(Mathf.Cos(a) * rad.x * t, Mathf.Sin(a) * rad.y * t), tint, Vector2.zero);
                }
            }
            for (int j = 0; j < segments; j++) vh.AddTriangle(0, 1 + j, 1 + (j + 1) % segments);
            for (int i = 1; i < rings; i++)
                for (int j = 0; j < segments; j++)
                {
                    int a = 1 + (i - 1) * segments + j, b = 1 + (i - 1) * segments + (j + 1) % segments;
                    vh.AddTriangle(a, a + segments, b);
                    vh.AddTriangle(b, a + segments, b + segments);
                }
        }
    }

    // The slowly turning dashed compass rings behind both screens.
    [RequireComponent(typeof(CanvasRenderer))]
    public class CompassRings : MaskableGraphic
    {
        public struct Ring { public float Radius, Dash, Gap, Width; public Color Color; }
        public struct Dot { public Vector2 Position; public float Radius, Width; public Color Color; public bool Filled; }
        public readonly List<Ring> Rings = new List<Ring>();
        public readonly List<Dot> Dots = new List<Dot>();
        public float SecondsPerTurn = 180;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            foreach (var ring in Rings)
            {
                float period = ring.Dash + ring.Gap;
                int count = Mathf.Max(1, Mathf.RoundToInt(2 * Mathf.PI * ring.Radius / period));
                float step = Mathf.PI * 2 / count, dashAngle = step * ring.Dash / period;
                for (int i = 0; i < count; i++) Strip(vh, Vector2.zero, ring.Radius, i * step, i * step + dashAngle, ring.Width, ring.Color * color);
            }
            foreach (var dot in Dots)
            {
                if (dot.Filled) Fan(vh, dot.Position, dot.Radius, dot.Color * color);
                else Strip(vh, dot.Position, dot.Radius, 0, Mathf.PI * 2, dot.Width, dot.Color * color);
            }
        }

        static void Strip(VertexHelper vh, Vector2 center, float radius, float from, float to, float width, Color tint)
        {
            int segments = Mathf.Max(1, Mathf.CeilToInt((to - from) * radius / 6));
            int start = vh.currentVertCount;
            for (int i = 0; i <= segments; i++)
            {
                float a = Mathf.Lerp(from, to, i / (float)segments);
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                vh.AddVert(center + dir * (radius - width / 2), tint, Vector2.zero);
                vh.AddVert(center + dir * (radius + width / 2), tint, Vector2.zero);
                if (i == 0) continue;
                int k = start + (i - 1) * 2;
                vh.AddTriangle(k, k + 1, k + 2);
                vh.AddTriangle(k + 1, k + 3, k + 2);
            }
        }

        static void Fan(VertexHelper vh, Vector2 center, float radius, Color tint)
        {
            int start = vh.currentVertCount;
            const int segments = 16;
            vh.AddVert(center, tint, Vector2.zero);
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2 / segments;
                vh.AddVert(center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius, tint, Vector2.zero);
                vh.AddTriangle(start, start + 1 + i, start + 1 + (i + 1) % segments);
            }
        }

        void Update()
        {
            if (SecondsPerTurn > 0) rectTransform.Rotate(0, 0, -360 * Time.unscaledDeltaTime / SecondsPerTurn);
        }
    }

    // The splash ridge: dark ground under a gently curved horizon with a faint gold edge.
    [RequireComponent(typeof(CanvasRenderer))]
    public class RidgeGraphic : MaskableGraphic
    {
        public Color Edge = HubTheme.Alpha(HubTheme.Gold, 0.18f);

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = rectTransform.rect;
            const int steps = 64;
            Vector2[] a = { new Vector2(0, 102), new Vector2(260, 96), new Vector2(520, 100), new Vector2(800, 101) };
            Vector2[] b = { new Vector2(800, 101), new Vector2(1080, 102), new Vector2(1340, 96), new Vector2(1600, 102) };
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                var p = t < 0.5f ? Cubic(a, t * 2) : Cubic(b, (t - 0.5f) * 2);
                float x = r.x + p.x / 1600f * r.width, y = r.yMax - p.y / 220f * r.height;
                vh.AddVert(new Vector3(x, y), color, Vector2.zero);
                vh.AddVert(new Vector3(x, r.yMin), color, Vector2.zero);
                vh.AddVert(new Vector3(x, y + 0.5f), Edge, Vector2.zero);
                vh.AddVert(new Vector3(x, y - 0.5f), Edge, Vector2.zero);
                if (i == 0) continue;
                int k = (i - 1) * 4;
                vh.AddTriangle(k, k + 4, k + 1); vh.AddTriangle(k + 1, k + 4, k + 5);
                vh.AddTriangle(k + 2, k + 6, k + 3); vh.AddTriangle(k + 3, k + 6, k + 7);
            }
        }

        static Vector2 Cubic(Vector2[] p, float t)
        {
            float u = 1 - t;
            return u * u * u * p[0] + 3 * u * u * t * p[1] + 3 * u * t * t * p[2] + t * t * t * p[3];
        }
    }

    // CSS cm-bob: translateY(-3px) at 50%, ease-in-out, looping.
    public class Bob : MonoBehaviour
    {
        public float Amplitude = 3, Period = 0.9f, Delay;
        RectTransform rect;
        Vector2 origin;
        void Awake() { rect = (RectTransform)transform; origin = rect.anchoredPosition; }
        void Update()
        {
            float phase = (Time.unscaledTime + Delay) / Period * Mathf.PI * 2;
            rect.anchoredPosition = origin + Vector2.up * Amplitude * 0.5f * (1 - Mathf.Cos(phase));
        }
    }

    // CSS cm-rise: fade in while drifting up 14px over 1.1s with an ease-out curve.
    public class RiseIn : MonoBehaviour
    {
        public float Delay, Duration = 1.1f, Distance = 14;
        CanvasGroup group;
        RectTransform rect;
        Vector2 origin;
        float start;
        void Awake()
        {
            rect = (RectTransform)transform;
            origin = rect.anchoredPosition;
            // Not `??`: Unity's missing-component result is a fake null that `??` does not catch.
            group = GetComponent<CanvasGroup>();
            if (group == null) group = gameObject.AddComponent<CanvasGroup>();
            start = Time.unscaledTime;
            Apply(0);
        }
        void Update()
        {
            float t = Mathf.Clamp01((Time.unscaledTime - start - Delay) / Duration);
            Apply(1 - Mathf.Pow(1 - t, 3));
            if (t >= 1) enabled = false;
        }
        void Apply(float eased)
        {
            group.alpha = eased;
            rect.anchoredPosition = origin + Vector2.down * Distance * (1 - eased);
        }
    }
}

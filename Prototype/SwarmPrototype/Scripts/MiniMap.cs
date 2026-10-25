using UnityEngine;

namespace SwarmPrototype
{
    // Bottom-left overview: terrain from the layout, live dots for every unit,
    // tower and monster, and the camera's view as a white outline. Left-click
    // jumps the camera there, right-click sets the rally point there.
    public class MiniMap
    {
        const float Size = 200f;
        const float Pad = 4f;

        public Rect Panel { get; private set; }
        public Rect Map { get; private set; }

        MapLayout layout;
        Spawner[] bases;

        struct PingEntry { public Vector3 world; public Color color; public float start; }
        readonly System.Collections.Generic.List<PingEntry> pings = new System.Collections.Generic.List<PingEntry>();
        const float PingLife = 3f;

        public void Init(MapLayout layout, Spawner[] bases)
        {
            this.layout = layout;
            this.bases = bases;
            pings.Clear();
        }

        public void Ping(Vector3 world, Color color) => pings.Add(new PingEntry { world = world, color = color, start = Time.time });

        // Expanding rings that repeat three times, so a glance catches them.
        void DrawPings()
        {
            pings.RemoveAll(p => Time.time - p.start > PingLife);
            foreach (var p in pings)
            {
                float t = ((Time.time - p.start) % 1f);
                float r = 4f + t * 14f;
                Color c = p.color;
                c.a = 1f - t;
                Vector2 centre = At(p.world);
                Ring(centre, r, c);
                UiSkin.Fill(new Rect(centre.x - 2f, centre.y - 2f, 4f, 4f), p.color);
            }
        }

        static void Ring(Vector2 centre, float radius, Color color)
        {
            const int segments = 16;
            Vector2 prev = centre + new Vector2(radius, 0f);
            for (int i = 1; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                Vector2 next = centre + new Vector2(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius);
                Line(prev, next, color, 2f);
                prev = next;
            }
        }

        public bool Covers(Vector2 mouse) => Panel.Contains(new Vector2(mouse.x, Screen.height - mouse.y));

        // GUI-space point for a world position.
        Vector2 At(Vector3 world)
        {
            float scale = Map.width / GameState.MapSize;
            return new Vector2(Map.center.x + world.x * scale, Map.center.y - world.z * scale);
        }

        // World position (y = 0) for a GUI-space point on the map.
        public Vector3 WorldAt(Vector2 gui)
        {
            float scale = GameState.MapSize / Map.width;
            return new Vector3((gui.x - Map.center.x) * scale, 0f, (Map.center.y - gui.y) * scale);
        }

        // Returns a world point if the map was clicked this frame, with the button used.
        public bool Clicked(out Vector3 world, out int button)
        {
            world = default;
            button = -1;
            var e = Event.current;
            if (e.type != EventType.MouseDown || !Map.Contains(e.mousePosition)) return false;
            world = WorldAt(e.mousePosition);
            button = e.button;
            e.Use();
            return true;
        }

        public void Draw(Camera cam)
        {
            if (layout == null) return;
            Panel = new Rect(12f, Screen.height - Size - Pad * 2f - 28f, Size + Pad * 2f, Size + Pad * 2f);
            Map = new Rect(Panel.x + Pad, Panel.y + Pad, Size, Size);
            UiSkin.Panel(Panel);
            layout.DrawPreview(Map, team => bases == null || (team < bases.Length && bases[team] != null && !bases[team].IsDead));

            // Dead camps show as empty circles.
            foreach (var camp in Camp.All)
            {
                if (camp == null || !camp.Cleared) continue;
                Vector2 p = At(camp.Center);
                UiSkin.Fill(new Rect(p.x - 3f, p.y - 3f, 6f, 6f), new Color(0.3f, 0.45f, 0.22f));
            }

            foreach (var c in Combatant.All)
            {
                if (c == null || c.IsDead) continue;
                Vector2 p = At(c.transform.position);
                Color color = GameState.ColorOf(c.Team);
                if (c is Spawner) continue; // the layout already draws bases
                if (c is Tower)
                {
                    if (!Map.Contains(p)) continue;
                    UiSkin.Fill(new Rect(p.x - 2f, p.y - 2f, 4f, 4f), color);
                    continue;
                }
                var unit = c as Unit;
                if (unit != null && unit.IsElite) continue; // drawn on top, below
                float s = c.Team == GameState.NeutralTeam ? 2.5f : 3f;
                if (!Map.Contains(p)) continue;
                UiSkin.Fill(new Rect(p.x - s * 0.5f, p.y - s * 0.5f, s, s), color);
            }

            DrawView(cam);
            DrawElites();
            DrawPings();
        }

        // Skull marker over every live elite, pulsing so it catches the eye; clamped to the
        // map edge so an elite just outside the drawn area still shows.
        void DrawElites()
        {
            var icon = Icons.Get("eliteMarker");
            float size = 18f + Mathf.Sin(Time.time * 5f) * 2f;
            foreach (var elite in EliteSpawner.Alive)
            {
                if (elite == null || elite.IsDead) continue;
                Vector2 p = At(elite.transform.position);
                p.x = Mathf.Clamp(p.x, Map.x + size * 0.5f, Map.xMax - size * 0.5f);
                p.y = Mathf.Clamp(p.y, Map.y + size * 0.5f, Map.yMax - size * 0.5f);
                GUI.DrawTexture(new Rect(p.x - size * 0.5f, p.y - size * 0.5f, size, size), icon);
            }
        }

        // Outline of what the camera sees on the ground.
        void DrawView(Camera cam)
        {
            if (cam == null) return;
            var corners = new Vector2[4];
            var screen = new[] { new Vector3(0f, 0f), new Vector3(Screen.width, 0f), new Vector3(Screen.width, Screen.height), new Vector3(0f, Screen.height) };
            var ground = new Plane(Vector3.up, Vector3.zero);
            for (int i = 0; i < 4; i++)
            {
                Ray ray = cam.ScreenPointToRay(screen[i]);
                if (!ground.Raycast(ray, out float t)) t = 500f;
                Vector3 hit = ray.GetPoint(Mathf.Min(t, 500f));
                corners[i] = At(hit);
                corners[i].x = Mathf.Clamp(corners[i].x, Map.x, Map.xMax);
                corners[i].y = Mathf.Clamp(corners[i].y, Map.y, Map.yMax);
            }
            for (int i = 0; i < 4; i++) Line(corners[i], corners[(i + 1) % 4], new Color(1f, 1f, 1f, 0.85f), 1.5f);
        }

        static void Line(Vector2 a, Vector2 b, Color color, float width)
        {
            Vector2 d = b - a;
            float length = d.magnitude;
            if (length < 0.5f) return;
            float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            Matrix4x4 old = GUI.matrix;
            GUIUtility.RotateAroundPivot(angle, a);
            UiSkin.Fill(new Rect(a.x, a.y - width * 0.5f, length, width), color);
            GUI.matrix = old;
        }
    }
}

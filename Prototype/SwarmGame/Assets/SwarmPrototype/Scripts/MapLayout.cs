using System.Collections.Generic;
using UnityEngine;

namespace SwarmPrototype
{
    // Where everything goes for a given player count: bases on a grid, roads
    // between neighbours, camps between bases, outside the boundary roads and
    // behind every base, and a map big enough to hold it all. Pure data, so the
    // start screen can draw a preview from the same answer the match uses.
    public class MapLayout
    {
        public int players, rows, cols;
        public Vector3[] bases;
        public List<(int a, int b)> lanes = new List<(int, int)>();
        public List<Camp.Site> camps = new List<Camp.Site>();
        public float mapSize;

        public static readonly int[] Sizes = { 2, 4, 6, 8, 9 };

        // Ring radius plus a margin, so no monster in a ring is in range of a tower.
        const float TowerClearance = 7f;
        const float CampSpacing = 16f;

        public static (int rows, int cols) Grid(int players)
        {
            switch (players)
            {
                case 2: return (1, 2);
                case 4: return (2, 2);
                case 6: return (2, 3);
                case 8: return (2, 4);
                case 9: return (3, 3);
                default:
                    int c = Mathf.CeilToInt(Mathf.Sqrt(players));
                    return (Mathf.CeilToInt(players / (float)c), c);
            }
        }

        public static MapLayout Build(int players, GameConfig config)
        {
            var layout = new MapLayout { players = players };
            (layout.rows, layout.cols) = Grid(players);
            float spacing = config.baseSpacing;

            // Bases: row 0 is the south edge, column 0 the west, so you start south-west.
            var list = new List<Vector3>();
            for (int r = 0; r < layout.rows && list.Count < players; r++)
                for (int c = 0; c < layout.cols && list.Count < players; c++)
                    list.Add(new Vector3((c - (layout.cols - 1) * 0.5f) * spacing, 0f, (r - (layout.rows - 1) * 0.5f) * spacing));
            layout.bases = list.ToArray();

            // Roads: neighbours one spacing apart on an axis.
            for (int i = 0; i < layout.bases.Length; i++)
                for (int j = i + 1; j < layout.bases.Length; j++)
                {
                    Vector3 d = layout.bases[j] - layout.bases[i];
                    bool alongX = Mathf.Approximately(d.z, 0f) && Mathf.Approximately(Mathf.Abs(d.x), spacing);
                    bool alongZ = Mathf.Approximately(d.x, 0f) && Mathf.Approximately(Mathf.Abs(d.z), spacing);
                    if (alongX || alongZ) layout.lanes.Add((i, j));
                }

            // Camps between every four bases: the contested ones.
            float halfW = (layout.cols - 1) * 0.5f * spacing, halfH = (layout.rows - 1) * 0.5f * spacing;
            for (int r = 0; r < layout.rows - 1; r++)
                for (int c = 0; c < layout.cols - 1; c++)
                {
                    var p = new Vector3((c + 0.5f - (layout.cols - 1) * 0.5f) * spacing, 0f, (r + 0.5f - (layout.rows - 1) * 0.5f) * spacing);
                    layout.Add(p, new[] { "medium", "hard" }, config);
                }

            // Outside each boundary road: easy pickings near the lane. In a single
            // row (1v1) both sides are outside and these are the contested camps.
            bool line = layout.rows == 1 || layout.cols == 1;
            foreach (var (a, b) in layout.lanes)
            {
                Vector3 mid = (layout.bases[a] + layout.bases[b]) * 0.5f;
                Vector3 dir = (layout.bases[b] - layout.bases[a]).normalized;
                Vector3 normal = new Vector3(-dir.z, 0f, dir.x);
                foreach (float side in new[] { -1f, 1f })
                {
                    Vector3 p = mid + normal * side * spacing * 0.3f;
                    bool inside = Mathf.Abs(p.x) < halfW - 1f && Mathf.Abs(p.z) < halfH - 1f;
                    if (inside) continue;
                    layout.Add(p, line ? new[] { "medium", "hard" } : new[] { "easy" }, config);
                }
            }

            // Behind every base, away from the map's middle: a safe farm for its owner.
            float behind = 34f;
            foreach (var b in layout.bases)
            {
                Vector3 d = new Vector3(b.x, 0f, b.z);
                if (d.sqrMagnitude < 1f) continue;
                layout.Add(b + d.normalized * behind, new[] { "easy" }, config);
            }

            float extent = 0f;
            foreach (var b in layout.bases) extent = Mathf.Max(extent, Mathf.Abs(b.x), Mathf.Abs(b.z));
            extent += config.plateauSize * 0.5f + config.rampLength;
            foreach (var c in layout.camps) extent = Mathf.Max(extent, Mathf.Abs(c.position.x) + 8f, Mathf.Abs(c.position.z) + 8f);
            layout.mapSize = Mathf.Ceil((extent + 12f) * 2f);
            return layout;
        }

        void Add(Vector3 p, string[] tiers, GameConfig config)
        {
            if (BaseTerrain.NearBase(p, config, bases, 8f) || InTowerRange(p, config)) return;
            foreach (var c in camps)
                if (Vector3.Distance(c.position, p) < CampSpacing) return;
            camps.Add(new Camp.Site { position = p, tiers = tiers });
        }

        bool InTowerRange(Vector3 point, GameConfig config)
        {
            float towerOffset = config.plateauSize * 0.5f - 2.2f;
            float safe = config.secondaryTower.range + TowerClearance;
            float mainSafe = config.mainTower.range + TowerClearance;
            Vector3 flat = new Vector3(point.x, 0f, point.z);
            foreach (var b in bases)
            {
                if (Vector3.Distance(flat, new Vector3(b.x, 0f, b.z)) < mainSafe) return true;
                foreach (var corner in new[] { new Vector3(-1f, 0f, -1f), new Vector3(1f, 0f, -1f), new Vector3(1f, 0f, 1f), new Vector3(-1f, 0f, 1f) })
                {
                    Vector3 tower = b + corner * towerOffset;
                    if (Vector3.Distance(flat, new Vector3(tower.x, 0f, tower.z)) < safe) return true;
                }
            }
            return false;
        }

        // Draws the layout into `area` (GUI space): bases as team squares, roads, camps as dots.
        // `baseAlive` (by team index) turns a destroyed base into a dim ruin; null means all alive.
        public void DrawPreview(Rect area, System.Predicate<int> baseAlive = null)
        {
            float scale = Mathf.Min(area.width, area.height) / mapSize;
            Vector2 centre = area.center;
            Vector2 At(Vector3 p) => new Vector2(centre.x + p.x * scale, centre.y - p.z * scale);

            UiSkin.Fill(area, new Color(0.3f, 0.45f, 0.22f));
            foreach (var (a, b) in lanes)
            {
                Vector2 pa = At(bases[a]), pb = At(bases[b]);
                float w = Mathf.Max(2f, 7f * scale);
                var r = new Rect(Mathf.Min(pa.x, pb.x) - w * 0.5f, Mathf.Min(pa.y, pb.y) - w * 0.5f, Mathf.Abs(pb.x - pa.x) + w, Mathf.Abs(pb.y - pa.y) + w);
                UiSkin.Fill(r, new Color(0.55f, 0.45f, 0.32f));
            }
            foreach (var c in camps)
            {
                Vector2 p = At(c.position);
                float s = Mathf.Max(5f, 9f * scale);
                Color col = System.Array.IndexOf(c.tiers, "hard") >= 0 ? new Color(0.95f, 0.35f, 0.3f) : System.Array.IndexOf(c.tiers, "medium") >= 0 ? new Color(1f, 0.7f, 0.25f) : new Color(0.55f, 0.9f, 0.5f);
                UiSkin.Icon(new Rect(p.x - s * 0.5f, p.y - s * 0.5f, s, s), col);
            }
            for (int i = 0; i < bases.Length; i++)
            {
                Vector2 p = At(bases[i]);
                float s = Mathf.Max(8f, 24f * scale);
                if (baseAlive != null && !baseAlive(i))
                {
                    // Ruins: a dark smudge where the castle stood, no team colour.
                    float ru = s * 0.6f;
                    UiSkin.Fill(new Rect(p.x - ru * 0.5f, p.y - ru * 0.5f, ru, ru), new Color(0.2f, 0.18f, 0.16f, 0.8f));
                    continue;
                }
                UiSkin.Fill(new Rect(p.x - s * 0.5f - 1f, p.y - s * 0.5f - 1f, s + 2f, s + 2f), new Color(0.72f, 0.68f, 0.6f));
                UiSkin.Fill(new Rect(p.x - s * 0.5f, p.y - s * 0.5f, s, s), new Color(0.42f, 0.6f, 0.3f));
                float t = Mathf.Max(4f, s * 0.35f);
                UiSkin.Fill(new Rect(p.x - t * 0.5f, p.y - t * 0.5f, t, t), GameState.TeamColors[i]);
            }
        }
    }
}

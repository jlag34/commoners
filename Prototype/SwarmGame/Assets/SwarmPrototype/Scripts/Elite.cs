using System.Collections.Generic;
using UnityEngine;

namespace SwarmPrototype
{
    // Spawns a rare elite every intervalMin..Max seconds on a random road midpoint
    // that doesn't already have one. Fixed clock, so up to one per road can be
    // alive if nobody kills them. Scaled by the match clock like the camps. Pays
    // as difficulty "elite".
    public class EliteSpawner : MonoBehaviour
    {
        public static readonly List<Unit> Alive = new List<Unit>();
        // Which road each living elite was spawned on.
        static readonly Dictionary<Unit, int> roadOf = new Dictionary<Unit, int>();

        GameConfig config;
        Vector3[] spots;
        float nextSpawnTime;

        public static void Create(GameConfig config, MapLayout layout, Transform parent)
        {
            var go = new GameObject("Elite Spawner");
            go.transform.SetParent(parent, false);
            var spawner = go.AddComponent<EliteSpawner>();
            spawner.config = config;
            // Midpoints of the roads between neighbouring bases.
            var list = new System.Collections.Generic.List<Vector3>();
            foreach (var (a, b) in layout.lanes) list.Add((layout.bases[a] + layout.bases[b]) * 0.5f);
            if (list.Count == 0) list.Add(Vector3.zero);
            spawner.spots = list.ToArray();
            spawner.Schedule();
            Alive.Clear();
            roadOf.Clear();
        }

        void Schedule() => nextSpawnTime = Time.time + Random.Range(config.eliteIntervalMin, config.eliteIntervalMax);

        void Update()
        {
            if (GameState.Over) return;
            // Destroyed units compare equal to null.
            Alive.RemoveAll(u => u == null || u.IsDead);
            if (Time.time < nextSpawnTime) return;
            Schedule();

            // Roads with no elite on them right now.
            var free = new List<int>();
            for (int i = 0; i < spots.Length; i++) free.Add(i);
            foreach (var u in Alive)
                if (roadOf.TryGetValue(u, out int road)) free.Remove(road);
            if (free.Count == 0) return;
            Spawn(free[Random.Range(0, free.Count)]);
        }

        void Spawn(int road)
        {
            if (config.elites == null || config.elites.Length == 0) return;
            EliteConfig e = config.elites[Random.Range(0, config.elites.Length)];
            float scale = 1f + config.campScalePerMinute * GameState.MatchMinutes;
            UnitStats stats = e.unit.Clone();
            stats.maxHealth *= scale;
            stats.attack *= scale;
            int bounty = Mathf.RoundToInt(e.bounty * scale);
            if (!ColorUtility.TryParseHtmlString(e.tint, out Color tint)) tint = GameState.NeutralColor;

            Vector3 spot = spots[road];
            var elite = Unit.CreateElite(e, stats, spot, bounty, tint, transform);
            Alive.Add(elite);
            roadOf[elite] = road;
            SpawnPoof.Spawn(spot, tint, transform);
            string what = EliteAbilities.Describe(e.abilities);
            GameState.Toast?.Invoke($"Elite {e.name} ({what}) has appeared on a road!");
            GameState.Ping?.Invoke(spot, new Color(1f, 0.55f, 0.15f));
        }
    }
}

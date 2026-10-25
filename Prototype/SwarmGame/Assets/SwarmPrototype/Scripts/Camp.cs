using System.Collections.Generic;
using UnityEngine;

namespace SwarmPrototype
{
    // A neutral monster camp. Several dot the map (see Sites); each rolls a
    // template from config.camps matching the tiers its site allows. Monsters pay
    // their bounty to whoever kills them, the last one pays the clear bonus, and a
    // new random camp appears config.campRespawnDelay seconds later. Every spawn
    // is scaled by the match clock: 1 + campScalePerMinute * minutes on HP,
    // damage and gold.
    public class Camp : MonoBehaviour
    {
        public static readonly List<Camp> All = new List<Camp>();

        public string Name { get; private set; }
        public string Difficulty { get; private set; }
        public Vector3 Center { get; private set; }
        public int Alive { get; private set; }
        public int Total { get; private set; }
        public float Scale { get; private set; } = 1f;
        public bool Cleared => Alive <= 0;
        public float RespawnIn => Mathf.Max(0f, respawnAt - Time.time);

        GameConfig config;
        string[] tiers;
        Transform monsters;
        int clearBonus;
        float respawnAt;

        public struct Site
        {
            public Vector3 position;
            public string[] tiers;
        }

        public static Camp Create(GameConfig config, Site site, Transform parent)
        {
            if (config.camps == null || config.camps.Length == 0) return null;
            var go = new GameObject("Camp");
            go.transform.SetParent(parent, false);
            var camp = go.AddComponent<Camp>();
            camp.config = config;
            camp.Center = site.position;
            camp.tiers = site.tiers;
            Dress(go.transform, site.position, 6.5f, All.Count);
            camp.Spawn();
            All.Add(camp);
            return camp;
        }

        CampConfig Roll()
        {
            var pool = new List<CampConfig>();
            foreach (var c in config.camps)
                if (System.Array.IndexOf(tiers, c.difficulty) >= 0) pool.Add(c);
            if (pool.Count == 0) pool.AddRange(config.camps);
            return pool[Random.Range(0, pool.Count)];
        }

        void Spawn()
        {
            CampConfig pick = Roll();
            Scale = 1f + config.campScalePerMinute * GameState.MatchMinutes;
            Name = pick.name;
            Difficulty = pick.difficulty;
            Total = Alive = pick.count;
            clearBonus = Mathf.RoundToInt(pick.clearBonus * Scale);
            gameObject.name = "Camp: " + pick.name;

            UnitStats stats = pick.unit.Clone();
            stats.maxHealth *= Scale;
            stats.attack *= Scale;
            int bounty = Mathf.RoundToInt(pick.bounty * Scale);
            if (!ColorUtility.TryParseHtmlString(pick.tint, out Color tint)) tint = GameState.NeutralColor;

            monsters = new GameObject("Monsters").transform;
            monsters.SetParent(transform, false);

            // Monsters stand in a ring facing outward; bigger monsters stand further apart.
            // Roomy ring so a camp doesn't read as one blob.
            float ring = 2.4f + pick.count * 0.55f * pick.scale;
            for (int i = 0; i < pick.count; i++)
            {
                float angle = i * Mathf.PI * 2f / pick.count;
                Vector3 spot = Center + new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * ring;
                var monster = Unit.CreateMonster(stats, spot, pick.scale, bounty, tint, this, pick.difficulty, monsters);
                monster.transform.rotation = Quaternion.LookRotation(spot - Center);
                SpawnPoof.Spawn(spot, tint, monsters);
            }
        }

        void Update()
        {
            if (!Cleared || GameState.Over || config.campRespawnDelay <= 0f) return;
            if (Time.time < respawnAt) return;
            // Old holder keeps any corpses still playing their death clip.
            if (monsters != null) Destroy(monsters.gameObject, 3f);
            Spawn();
        }

        public void OnMonsterDied(int killerTeam)
        {
            Alive--;
            if (Alive > 0) return;
            GameState.AddGold(killerTeam, clearBonus, Center);
            respawnAt = Time.time + config.campRespawnDelay;
        }

        // Stones and a worn circle so the camp reads as a place.
        static void Dress(Transform parent, Vector3 center, float radius, int seed)
        {
            var random = new System.Random(11 + seed);
            string[] stones = { "stone_largeA", "stone_largeB", "stone_tallA", "stone_smallA" };
            for (int i = 0; i < 5; i++)
            {
                float angle = i * Mathf.PI * 2f / 5f + (float)random.NextDouble() * 0.6f;
                var holder = new GameObject("Camp Stone").transform;
                holder.SetParent(parent, false);
                holder.position = center + new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * (radius + (float)random.NextDouble() * 1.5f);
                holder.rotation = Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f);
                var stone = Visuals.SpawnModel(stones[random.Next(stones.Length)], holder);
                if (stone != null) Visuals.FitHeight(stone, 0.8f + (float)random.NextDouble() * 1.2f);
            }

            var dirt = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            dirt.name = "Camp Ground";
            Object.DestroyImmediate(dirt.GetComponent<Collider>());
            dirt.transform.SetParent(parent, false);
            dirt.transform.position = center + Vector3.up * 0.015f;
            dirt.transform.localScale = new Vector3(radius * 2f, 0.01f, radius * 2f);
            var r = dirt.GetComponent<Renderer>();
            r.sharedMaterial = Visuals.Plain(new Color(0.5f, 0.42f, 0.3f));
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        void OnDestroy() => All.Remove(this);
    }
}

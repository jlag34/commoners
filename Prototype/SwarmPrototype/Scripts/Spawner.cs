using UnityEngine;

namespace SwarmPrototype
{
    // A player's base: a 5 x 5 tower. Spawns a unit on a timer. With no rally point
    // set, units gather in clockwise rings around the tower, nearest free slot first;
    // with one set, they walk to it. Losing the tower loses the match.
    public class Spawner : Combatant
    {
        // Footprint of the tower in meters (width and length).
        public const float Footprint = 5f;
        const float Height = 3f;
        // Gap between gathered units and between rings.
        const float UnitSpacing = 1.1f;

        // null until the player (or bot) sets one.
        public Vector3? RallyPoint { get; set; }
        public int AliveUnits { get; set; }
        public Color TeamColor { get; private set; }

        // Bought upgrade levels, one per config.upgrades entry, and the unit stats
        // they add up to. Units read Stats live, so a purchase applies to every
        // unit on the team at once, alive or not yet spawned.
        public UpgradeConfig[] Upgrades => config.upgrades;
        public UnitStats Stats { get; private set; }
        public TowerBonus Towers { get; private set; } = new TowerBonus();
        public EconomyBonus Economy { get; private set; } = new EconomyBonus();
        public float SpawnInterval => config.spawnInterval / (1f + Economy.spawnRate);
        // Units alive at once; 0 means no cap.
        public int MaxUnits => config.maxUnitsPerTeam <= 0 ? 0 : config.maxUnitsPerTeam + Economy.unitCap;
        int[] levels;

        public int Level(int upgrade) => levels[upgrade];
        // Bumps on every purchase so units know when to refresh their gear.
        public int Version { get; private set; }

        // Dev only: force a level without paying (screenshots, tests).
        public void DebugSetLevel(string id, int level)
        {
            for (int i = 0; i < Upgrades.Length; i++)
                if (Upgrades[i].id == id) levels[i] = Mathf.Max(0, level);
            Version++;
            Recompute();
        }

        public int LevelOf(string id)
        {
            for (int i = 0; i < Upgrades.Length; i++) if (Upgrades[i].id == id) return levels[i];
            return 0;
        }
        public int TotalLevels
        {
            get { int total = 0; foreach (int l in levels) total += l; return total; }
        }
        public bool IsMaxed(int upgrade) => Upgrades[upgrade].maxLevel > 0 && levels[upgrade] >= Upgrades[upgrade].maxLevel;
        public int Cost(int upgrade) => Upgrades[upgrade].CostAt(levels[upgrade] + 1);
        public int SkullCost(int upgrade)
        {
            var u = Upgrades[upgrade];
            if (u.skullCost <= 0) return 0;
            return Mathf.Max(1, Mathf.RoundToInt(u.skullCost * Mathf.Pow(u.costGrowth, levels[upgrade])));
        }
        public int EssenceTier(int upgrade) => string.IsNullOrEmpty(Upgrades[upgrade].essence) ? -1 : GameState.EssenceIndex(Upgrades[upgrade].essence);
        public int EssenceCost(int upgrade) => EssenceTier(upgrade) < 0 ? 0 : Mathf.Max(1, Upgrades[upgrade].essenceCost);
        public bool CanAfford(int upgrade) =>
            !IsMaxed(upgrade) && GameState.CanAfford(Team, Cost(upgrade), SkullCost(upgrade), EssenceTier(upgrade), EssenceCost(upgrade));

        // What the unit stats and tower bonuses would be after buying one more level of `upgrade`.
        public void After(int upgrade, out UnitStats units, out TowerBonus towers, out EconomyBonus economy)
        {
            var next = (int[])levels.Clone();
            next[upgrade]++;
            Compute(next, out units, out towers, out economy);
        }

        public bool Buy(int upgrade)
        {
            if (IsDead || IsMaxed(upgrade) || !GameState.TrySpend(Team, Cost(upgrade), SkullCost(upgrade), EssenceTier(upgrade), EssenceCost(upgrade))) return false;
            levels[upgrade]++;
            Version++;
            Recompute();
            return true;
        }

        void Recompute()
        {
            Compute(levels, out var units, out var towers, out var economy);
            Stats = units;
            Towers = towers;
            Economy = economy;
        }

        void Compute(int[] levels, out UnitStats s, out TowerBonus t, out EconomyBonus e)
        {
            UnitStats basis = config.unit;
            s = basis.Clone();
            t = new TowerBonus();
            e = new EconomyBonus();
            for (int i = 0; i < Upgrades.Length; i++)
            {
                var u = Upgrades[i];
                float amount = levels[i] * u.amountPerLevel;
                switch (u.stat)
                {
                    case "attack": s.attack += amount; break;
                    case "attackSpeed": s.attacksPerSecond += basis.attacksPerSecond * amount; break;
                    case "health": s.maxHealth += amount; break;
                    case "armor": s.armor += amount; break;
                    case "moveSpeed": s.moveSpeed += basis.moveSpeed * amount; break;
                    case "evasion": s.evasion = Mathf.Clamp01(s.evasion + amount); break;
                    case "lifesteal": s.lifesteal += amount; break;
                    case "regen": s.regen += amount; break;
                    case "crit": s.critChance = Mathf.Clamp01(s.critChance + amount); break;
                    case "splash": s.splash += amount; break;
                    case "towerDamage": t.damage += amount; break;
                    case "towerRange": t.range += amount; break;
                    case "towerRegen": t.regen += amount; break;
                    case "spawnRate": e.spawnRate += amount; break;
                    case "bounty": e.bounty += amount; break;
                    case "reincarnate": e.reincarnate += amount; break;
                    case "unitCap": e.unitCap += Mathf.RoundToInt(amount); break;
                    default: Debug.LogWarning($"SwarmPrototype: unknown upgrade stat '{u.stat}'"); break;
                }
            }
        }

        GameConfig config;
        Transform unitParent;
        float nextSpawnTime;

        public override float HealthBarHeight => 4.5f;
        public override bool IsBuilding => true;
        public override float FeetY => Foot.y;

        public static Spawner Create(int team, Vector3 position, GameConfig config, Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = GameState.TeamNames[team] + " Base";
            go.transform.SetParent(parent, false);
            go.transform.position = position + Vector3.up * (Height * 0.5f);
            go.transform.localScale = new Vector3(Footprint, Height, Footprint);
            Color color = GameState.TeamColors[team];
            go.GetComponent<Renderer>().material.color = Color.Lerp(color, Color.black, 0.3f);
            BaseVisual.Build(go, color);
            BaseTerrain.MakeObstacle(go);

            var units = new GameObject(go.name + " Units").transform;
            units.SetParent(parent, false);

            var spawner = go.AddComponent<Spawner>();
            spawner.Init(team, color, config, units);
            return spawner;
        }

        void Init(int team, Color color, GameConfig config, Transform unitParent)
        {
            Team = team;
            TeamColor = color;
            this.config = config;
            this.unitParent = unitParent;
            MaxHealth = Health = config.mainTower.health;
            Armor = config.mainTower.armor;
            TowerGun.Attach(this, config.mainTower, Height + 3f);
            Radius = Footprint * 0.5f;
            levels = new int[config.upgrades.Length];
            Recompute();
            RallyPoint = null;
            nextSpawnTime = Time.time + 0.5f;
        }

        // Ground level at the foot of the tower.
        public Vector3 Foot => transform.position - Vector3.up * (Height * 0.5f);

        void Update()
        {
            if (GameState.Over) return;
            if (Towers.regen > 0f) Heal(Towers.regen * Time.deltaTime);
            if (Time.time < nextSpawnTime) return;
            nextSpawnTime = Time.time + SpawnInterval;
            if (MaxUnits <= 0 || AliveUnits < MaxUnits) Spawn();
        }

        // Reincarnate: a unit that just died comes straight back at the tower.
        public void Reincarnate()
        {
            if (IsDead || GameState.Over) return;
            Spawn();
            if (Team == GameState.PlayerTeam) GameState.Toast?.Invoke("A unit reincarnated");
        }

        void Spawn()
        {
            Vector3 position = RallyPoint.HasValue ? SpawnTowards(RallyPoint.Value) : NextRingSlot();
            Unit.Create(this, Stats, position, TeamColor, unitParent);
            SpawnPoof.Spawn(position, TeamColor, unitParent);
            AliveUnits++;
        }

        // Units come out of the side facing the rally point.
        Vector3 SpawnTowards(Vector3 rally)
        {
            Vector3 direction = rally - transform.position;
            direction.y = 0f;
            direction = direction.sqrMagnitude < 0.01f ? Vector3.forward : direction.normalized;
            Vector3 side = Vector3.Cross(Vector3.up, direction);
            return Foot + direction * (Radius + 1.2f) + side * Random.Range(-1.5f, 1.5f);
        }

        // Rings around the tower, innermost first. Each ring starts at the south side
        // (facing the camera) and runs clockwise as seen from above. Returns the first
        // slot nothing is standing in, or a random spot on the plateau if every ring is full.
        Vector3 NextRingSlot()
        {
            Vector3 center = Foot;
            float firstRing = Radius + 0.5f + 0.4f;
            float lastRing = config.plateauSize * 0.5f - 1.6f;
            for (float ring = firstRing; ring <= lastRing; ring += UnitSpacing)
            {
                int slots = Mathf.Max(1, Mathf.FloorToInt(2f * Mathf.PI * ring / UnitSpacing));
                for (int i = 0; i < slots; i++)
                {
                    // Angle grows north -> east -> south -> west: clockwise from above.
                    float angle = Mathf.PI + i * (2f * Mathf.PI / slots);
                    Vector3 slot = center + new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * ring;
                    if (IsFree(slot)) return slot;
                }
            }
            Vector2 random = Random.insideUnitCircle.normalized * Random.Range(firstRing, lastRing);
            return center + new Vector3(random.x, 0f, random.y);
        }

        // Nothing standing there, and not inside a corner tower.
        static bool IsFree(Vector3 slot)
        {
            foreach (var c in All)
            {
                if (c.IsDead || c is Spawner) continue;
                float minGap = c.IsBuilding ? c.Radius + 0.7f : UnitSpacing * 0.85f;
                Vector3 offset = c.transform.position - slot;
                offset.y = 0f;
                if (offset.sqrMagnitude < minGap * minGap) return false;
            }
            return true;
        }

        protected override void OnHit(Combatant attacker)
        {
            if (Team == GameState.PlayerTeam) GameState.OnPlayerBuildingHit(transform.position);
        }

        protected override void Die(int killerTeam)
        {
            GameState.OnBaseDestroyed(this, killerTeam);
            Destroy(gameObject);
        }
    }
}

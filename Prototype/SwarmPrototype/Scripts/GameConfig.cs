using System;
using UnityEngine;

namespace SwarmPrototype
{
    // All tunable numbers live in Resources/swarm_config.json so balance can change
    // without touching code. The defaults below are only a fallback.
    [Serializable]
    public class UnitStats
    {
        public float maxHealth = 100f;
        public float attack = 10f;
        public float attacksPerSecond = 1f;
        public float moveSpeed = 5f;
        public float armor = 1f;
        public float attackRange = 0.6f;
        public float aggroRange = 10f;
        // Chance (0..1) that a hit does nothing.
        public float evasion = 0f;
        // Fraction of damage dealt healed back.
        public float lifesteal = 0f;
        // HP per second while alive.
        public float regen = 0f;
        // Chance (0..1) a hit does double damage.
        public float critChance = 0f;
        // Fraction of damage also dealt to enemies next to the target.
        public float splash = 0f;

        public UnitStats Clone() => (UnitStats)MemberwiseClone();
    }

    // One buyable upgrade. `stat` picks what it changes (see Spawner.Compute):
    // attack and armor and health add `amountPerLevel` flat; attackSpeed and
    // moveSpeed add it as a fraction of the base; evasion adds it as a chance.
    // The price of level n is baseCost * costGrowth^(n-1). maxLevel 0 = no cap.
    // Team-wide bonuses for the towers (main and secondary), from the Special tab.
    // Team-wide economy bonuses from the Economy tab.
    [Serializable]
    public class EconomyBonus
    {
        // Spawn interval is divided by (1 + this).
        public float spawnRate = 0f;
        // Extra gold on every kill reward, as a fraction.
        public float bounty = 0f;
        // Chance (0..1) that a unit of yours that dies comes straight back at the tower.
        public float reincarnate = 0f;
        // Extra units on top of maxUnitsPerTeam.
        public int unitCap = 0;
    }

    [Serializable]
    public class TowerBonus
    {
        // Added as a fraction of base damage.
        public float damage = 0f;
        // Meters added to range.
        public float range = 0f;
        // HP per second.
        public float regen = 0f;
    }

    [Serializable]
    public class UpgradeConfig
    {
        public string id = "strength";
        public string name = "Strength";
        public string stat = "attack";
        // Shop tab this appears on.
        public string tab = "stats";
        public float amountPerLevel = 1f;
        public int baseCost = 15;
        public float costGrowth = 1.15f;
        public int maxLevel = 0;
        // Skulls (the neutral-kill currency) for level 1; grows by costGrowth like gold. 0 = none.
        public int skullCost = 0;
        // Essence tier needed per level: "", "lesser", "greater" or "elite".
        public string essence = "";
        public int essenceCost = 1;

        public UpgradeConfig() { }

        public UpgradeConfig(string id, string name, string stat, float amountPerLevel, float costGrowth, int maxLevel, string tab = "stats", int baseCost = 15)
        {
            this.id = id;
            this.name = name;
            this.stat = stat;
            this.amountPerLevel = amountPerLevel;
            this.costGrowth = costGrowth;
            this.maxLevel = maxLevel;
            this.tab = tab;
            this.baseCost = baseCost;
        }

        public int CostAt(int level)
        {
            float raw = baseCost * Mathf.Pow(costGrowth, Mathf.Max(0, level - 1));
            return Mathf.Max(1, Mathf.RoundToInt(raw));
        }
    }

    // A tower: the main one (the base itself) or a secondary corner tower.
    // attack 0 makes it a passive building.
    [Serializable]
    public class TowerConfig
    {
        public float health = 600f;
        public float armor = 3f;
        public float attack = 8f;
        public float attacksPerSecond = 1f;
        public float range = 12f;
        // Gold to whoever destroys it (the main tower pays goldPerBaseKill instead).
        public int bounty = 50;
    }

    // What killing one neutral of a given difficulty pays besides its gold bounty:
    // skulls, and a chance at each essence tier.
    [Serializable]
    public class TierReward
    {
        public string difficulty = "easy";
        public int skulls = 1;
        public float lesserChance = 0.1f;
        public float greaterChance = 0f;
        public float eliteChance = 0f;
    }

    // The rare elite: one at a time, every eliteIntervalMin..Max seconds, on a
    // random road. Pays like difficulty "elite".
    [Serializable]
    public class EliteConfig
    {
        public string name = "Ancient Treant";
        public string tint = "#4b6b2f";
        public float scale = 2.2f;
        public int bounty = 120;
        public float leash = 25f;
        // See EliteAbilities: poison, execute, stun, knockback, splash, heal, frenzy.
        public string[] abilities = { "heal" };
        public UnitStats unit = new UnitStats { maxHealth = 1500f, attack = 45f, attacksPerSecond = 0.9f, moveSpeed = 4.2f, armor = 6f, attackRange = 1.1f, aggroRange = 12f };
    }

    // A neutral monster camp template. Each camp site rolls one from the templates
    // whose `difficulty` (easy / medium / hard) the site allows.
    [Serializable]
    public class CampConfig
    {
        public string name = "Wolves";
        public string difficulty = "medium";
        // Hex color for the monsters' tabards, e.g. "#6b8e23".
        public string tint = "#8a7a3a";
        public int count = 5;
        // Gold to whoever lands the killing blow on each monster, and on the last one.
        public int bounty = 15;
        public int clearBonus = 30;
        // Visual and collider size relative to a normal unit.
        public float scale = 1f;
        public UnitStats unit = new UnitStats();
    }

    [Serializable]
    public class GameConfig
    {
        public float spawnInterval = 5f;
        // Units alive at once before the Unit Count upgrade. 0 or less means no cap.
        public int maxUnitsPerTeam = 10;
        public int startingGold = 0;
        public int goldPerUnitKill = 5;
        public int goldPerBaseKill = 200;
        public TowerConfig mainTower = new TowerConfig { health = 2000f, armor = 2f, attack = 15f, attacksPerSecond = 1f, range = 10f, bounty = 0 };
        public TowerConfig secondaryTower = new TowerConfig();
        public float baseSpacing = 80f;
        public float mapSize = 140f;
        public float plateauSize = 24f;
        public float plateauHeight = 2.5f;
        public float entranceWidth = 6f;
        public float rampLength = 9f;
        public float enemyFirstAttackDelay = 20f;
        public float enemyAttackInterval = 30f;
        public int enemyAttackMinUnits = 7;
        public int enemyDefenders = 3;
        public UnitStats unit = new UnitStats();
        public CampConfig[] camps = { new CampConfig() };
        // Seconds after the last monster dies before a new (random) camp appears. 0 = never.
        public float campRespawnDelay = 30f;
        // Monsters spawned at minute m have HP, damage and gold scaled by 1 + m * this.
        public float campScalePerMinute = 0.05f;
        public TierReward[] tiers =
        {
            new TierReward { difficulty = "easy", skulls = 1, lesserChance = 0.1f },
            new TierReward { difficulty = "medium", skulls = 4, lesserChance = 0.25f, greaterChance = 0.04f },
            new TierReward { difficulty = "hard", skulls = 10, lesserChance = 0.2f, greaterChance = 0.35f, eliteChance = 0.05f },
            new TierReward { difficulty = "elite", skulls = 50, greaterChance = 1f, eliteChance = 1f },
        };
        // A random one of these appears every eliteIntervalMin..Max seconds.
        public float eliteIntervalMin = 120f;
        public float eliteIntervalMax = 180f;
        public EliteConfig[] elites =
        {
            new EliteConfig { name = "Ancient Treant", tint = "#4b6b2f", abilities = new[] { "heal", "knockback" } },
            new EliteConfig { name = "Venom Hydra", tint = "#3fae5a", scale = 2f, abilities = new[] { "poison" }, unit = new UnitStats { maxHealth = 1300f, attack = 30f, attacksPerSecond = 1.2f, moveSpeed = 4.5f, armor = 4f, attackRange = 1.1f, aggroRange = 12f } },
            new EliteConfig { name = "Dark Knight", tint = "#3a2a52", scale = 1.8f, abilities = new[] { "execute" }, unit = new UnitStats { maxHealth = 1200f, attack = 40f, attacksPerSecond = 0.8f, moveSpeed = 5f, armor = 5f, attackRange = 1f, aggroRange = 12f } },
            new EliteConfig { name = "Thunder Ogre", tint = "#4a6fd1", scale = 2.1f, abilities = new[] { "stun", "frenzy" }, unit = new UnitStats { maxHealth = 1500f, attack = 35f, attacksPerSecond = 0.9f, moveSpeed = 4.2f, armor = 5f, attackRange = 1.1f, aggroRange = 12f } },
            new EliteConfig { name = "Siege Golem", tint = "#7d8a99", scale = 2.4f, bounty = 150, abilities = new[] { "splash" }, unit = new UnitStats { maxHealth = 1900f, attack = 38f, attacksPerSecond = 0.6f, moveSpeed = 3.6f, armor = 8f, attackRange = 1.3f, aggroRange = 12f } },
        };

        public TierReward RewardFor(string difficulty)
        {
            foreach (var t in tiers) if (t.difficulty == difficulty) return t;
            return new TierReward { difficulty = difficulty, skulls = 0, lesserChance = 0f };
        }

        // What the tower shop sells, in display order. Applies to every unit on the team.
        public UpgradeConfig[] upgrades =
        {
            new UpgradeConfig("strength", "Strength", "attack", 1f, 1.15f, 0),
            new UpgradeConfig("attackSpeed", "Atk Speed", "attackSpeed", 0.1f, 1.15f, 0),
            new UpgradeConfig("health", "Health", "health", 10f, 1.15f, 0),
            new UpgradeConfig("armor", "Armor", "armor", 1f, 1.2f, 0),
            new UpgradeConfig("moveSpeed", "Move Speed", "moveSpeed", 0.08f, 1.15f, 10),
            new UpgradeConfig("lifesteal", "Lifesteal", "lifesteal", 0.03f, 1.25f, 10, "greater", 40) { skullCost = 10, essence = "greater" },
            new UpgradeConfig("evasion", "Evasion", "evasion", 0.04f, 1.3f, 8, "greater", 40) { skullCost = 10, essence = "greater" },
            new UpgradeConfig("regen", "Unit Regen", "regen", 1f, 1.2f, 10, "greater", 30) { skullCost = 5, essence = "lesser" },
            new UpgradeConfig("towerRegen", "Tower Regen", "towerRegen", 3f, 1.2f, 10, "greater", 30) { skullCost = 4, essence = "lesser" },
            new UpgradeConfig("towerDamage", "Tower Damage", "towerDamage", 0.15f, 1.2f, 10, "greater", 35) { skullCost = 5, essence = "lesser" },
            new UpgradeConfig("towerRange", "Tower Range", "towerRange", 1f, 1.3f, 5, "greater", 40) { skullCost = 5, essence = "lesser" },
            new UpgradeConfig("crit", "Crit Chance", "crit", 0.05f, 1.25f, 8, "greater", 40) { skullCost = 10, essence = "greater" },
            new UpgradeConfig("splash", "Splash", "splash", 0.1f, 1.3f, 5, "greater", 50) { skullCost = 20, essence = "elite" },
            new UpgradeConfig("spawnRate", "Spawn Rate", "spawnRate", 0.1f, 1.25f, 10, "greater", 25) { skullCost = 20, essence = "elite" },
            new UpgradeConfig("bounty", "Bounty", "bounty", 0.1f, 1.25f, 10, "greater", 30) { skullCost = 5, essence = "lesser" },
            new UpgradeConfig("reincarnate", "Reincarnate", "reincarnate", 0.06f, 1.3f, 5, "greater", 40) { skullCost = 10, essence = "greater" },
        };

        public static GameConfig Load()
        {
            var config = new GameConfig();
            var asset = Resources.Load<TextAsset>("swarm_config");
            if (asset != null)
                JsonUtility.FromJsonOverwrite(asset.text, config);
            else
                Debug.LogWarning("SwarmPrototype: Resources/swarm_config.json not found, using defaults.");
            return config;
        }
    }
}

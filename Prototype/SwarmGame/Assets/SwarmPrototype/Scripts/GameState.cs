using UnityEngine;

namespace SwarmPrototype
{
    public static class GameState
    {
        public const int PlayerTeam = 0;
        // Most players a match can have; TeamCount is the number in this match.
        public const int MaxTeams = 9;
        public static int TeamCount { get; private set; } = 4;
        // Monsters: not a player, hostile to everyone, outside the per-team arrays.
        public const int NeutralTeam = MaxTeams;

        // Warcraft-style player colors; the player is always blue.
        public static readonly Color[] TeamColors =
        {
            new Color(0.25f, 0.55f, 1f),
            new Color(0.9f, 0.25f, 0.2f),
            new Color(0.2f, 0.8f, 0.75f),
            new Color(0.65f, 0.35f, 0.9f),
            new Color(1f, 0.85f, 0.2f),
            new Color(1f, 0.5f, 0.15f),
            new Color(0.3f, 0.75f, 0.25f),
            new Color(1f, 0.45f, 0.75f),
            new Color(0.6f, 0.6f, 0.6f),
        };
        public static readonly string[] TeamNames = { "You", "Red Bot", "Teal Bot", "Purple Bot", "Yellow Bot", "Orange Bot", "Green Bot", "Pink Bot", "Gray Bot" };
        public static readonly Color NeutralColor = new Color(0.6f, 0.52f, 0.25f);

        public static Color ColorOf(int team) => team == NeutralTeam ? NeutralColor : TeamColors[team];

        // Every base in the match, indexed by team. Set by Bootstrap.
        public static Spawner[] Bases { get; private set; } = new Spawner[MaxTeams];

        // Reloaded if a script recompile during play wiped the statics.
        static GameConfig config;
        public static GameConfig Config => config ?? (config = GameConfig.Load());
        public static int[] Gold { get; private set; } = new int[MaxTeams];
        public static int[] Kills { get; private set; } = new int[MaxTeams];
        public static int[] Deaths { get; private set; } = new int[MaxTeams];
        // Second currency, from neutral kills.
        public static int[] Skulls { get; private set; } = new int[MaxTeams];
        // Essences per team per tier (see EssenceTiers).
        public static readonly string[] EssenceTiers = { "lesser", "greater", "elite" };
        public static readonly string[] EssenceLabels = { "Lesser", "Greater", "Elite" };
        public static readonly Color[] EssenceColors = { new Color(0.55f, 0.95f, 0.55f), new Color(0.4f, 0.7f, 1f), new Color(1f, 0.55f, 0.2f) };
        public static int[][] Essence { get; private set; } = NewEssence();
        static int[][] NewEssence()
        {
            var e = new int[MaxTeams][];
            for (int i = 0; i < MaxTeams; i++) e[i] = new int[EssenceTiers.Length];
            return e;
        }
        public static int EssenceIndex(string tier) => System.Array.IndexOf(EssenceTiers, tier);

        // Short messages for the player's HUD: "+1 Greater Essence", "Elite spawned".
        public static System.Action<string> Toast;
        // Something the player earned, where it happened: kind is "gold", "skulls" or an essence tier.
        public static System.Action<Vector3, string, int> Reward;
        // A spot worth looking at: elite spawns, your buildings under attack.
        public static System.Action<Vector3, Color> Ping;

        static float nextAttackAlert;
        // One of the player's buildings took a hit: ping and shout, at most every few seconds.
        public static void OnPlayerBuildingHit(Vector3 where)
        {
            if (Time.time < nextAttackAlert) return;
            nextAttackAlert = Time.time + 4f;
            Ping?.Invoke(where, new Color(1f, 0.25f, 0.2f));
            Toast?.Invoke("Your base is under attack!");
        }

        // Side length of the current map, set by Bootstrap from the layout.
        public static float MapSize { get; set; } = 140f;
        public static bool Over { get; private set; }
        public static int Winner { get; private set; } = -1;
        public static int WavesSent { get; private set; }

        static float startTime;
        public static float MatchMinutes => (Time.time - startTime) / 60f;

        public static void Reset(GameConfig config, int teams)
        {
            GameState.config = config;
            TeamCount = Mathf.Clamp(teams, 2, MaxTeams);
            Gold = new int[MaxTeams];
            for (int i = 0; i < MaxTeams; i++) Gold[i] = config.startingGold;
            Kills = new int[MaxTeams];
            Deaths = new int[MaxTeams];
            Skulls = new int[MaxTeams];
            Essence = NewEssence();
            Bases = new Spawner[MaxTeams];
            Over = false;
            Winner = -1;
            WavesSent = 0;
            startTime = Time.time;
        }

        public static void RegisterBase(Spawner spawner) => Bases[spawner.Team] = spawner;

        static bool ValidTeam(int team) => team >= 0 && team < TeamCount;

        // Kill rewards: scaled by the team's Bounty upgrade. Returns what was paid.
        public static int AddGold(int team, int amount)
        {
            if (!ValidTeam(team)) return 0;
            var home = Bases[team];
            float bonus = home != null ? home.Economy.bounty : 0f;
            int paid = Mathf.RoundToInt(amount * (1f + bonus));
            Gold[team] += paid;
            return paid;
        }

        // Same, and shows the player a floating "+N" where it happened.
        public static int AddGold(int team, int amount, Vector3 where)
        {
            int paid = AddGold(team, amount);
            if (paid > 0 && team == PlayerTeam) Reward?.Invoke(where, "gold", paid);
            return paid;
        }

        // A neutral monster died: gold bounty, skulls and essence rolls to the killer.
        public static void OnMonsterKilled(int killerTeam, int bounty, string difficulty, Vector3 where)
        {
            AddGold(killerTeam, bounty, where);
            if (!ValidTeam(killerTeam)) return;
            TierReward reward = Config.RewardFor(difficulty);
            if (reward.skulls > 0)
            {
                Skulls[killerTeam] += reward.skulls;
                if (killerTeam == PlayerTeam) Reward?.Invoke(where, "skulls", reward.skulls);
            }
            float[] chances = { reward.lesserChance, reward.greaterChance, reward.eliteChance };
            for (int tier = 0; tier < chances.Length; tier++)
            {
                if (chances[tier] <= 0f || Random.value >= chances[tier]) continue;
                Essence[killerTeam][tier]++;
                if (killerTeam == PlayerTeam)
                {
                    Reward?.Invoke(where, EssenceTiers[tier], 1);
                    Toast?.Invoke($"+1 {EssenceLabels[tier]} Essence");
                }
            }
        }

        public static bool CanAfford(int team, int gold, int skulls, int essenceTier, int essenceCount)
        {
            if (!ValidTeam(team)) return false;
            if (Gold[team] < gold || Skulls[team] < skulls) return false;
            return essenceTier < 0 || essenceCount <= 0 || Essence[team][essenceTier] >= essenceCount;
        }

        public static bool TrySpend(int team, int gold, int skulls, int essenceTier, int essenceCount)
        {
            if (!CanAfford(team, gold, skulls, essenceTier, essenceCount)) return false;
            Gold[team] -= gold;
            Skulls[team] -= skulls;
            if (essenceTier >= 0 && essenceCount > 0) Essence[team][essenceTier] -= essenceCount;
            return true;
        }

        public static void OnEnemyWave()
        {
            WavesSent++;
        }

        public static void OnUnitKilled(int killerTeam, int victimTeam, Vector3 where)
        {
            if (ValidTeam(victimTeam)) Deaths[victimTeam]++;
            if (!ValidTeam(killerTeam)) return;
            AddGold(killerTeam, Config.goldPerUnitKill, where);
            Kills[killerTeam]++;
        }

        // Free-for-all: you lose when your tower falls, win when every other tower has.
        public static void OnBaseDestroyed(Spawner destroyed, int killerTeam)
        {
            AddGold(killerTeam, Config.goldPerBaseKill, destroyed.transform.position);
            if (Over) return;
            if (destroyed.Team == PlayerTeam)
            {
                Over = true;
                Winner = killerTeam;
                return;
            }
            foreach (var b in Bases)
                if (b != null && b.Team != PlayerTeam && !b.IsDead) return;
            Over = true;
            Winner = PlayerTeam;
        }
    }
}

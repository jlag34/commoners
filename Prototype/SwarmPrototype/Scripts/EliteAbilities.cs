using UnityEngine;

namespace SwarmPrototype
{
    // What makes each elite more than a big bag of hit points. Named in
    // EliteConfig.abilities; numbers live here.
    //   poison    hits poison the target: PoisonDps true damage per second for PoisonDuration.
    //   execute   each hit has ExecuteChance to kill a unit outright.
    //   stun      each hit has StunChance to freeze the target for StunDuration.
    //   knockback hits throw the target KnockbackDistance away and stagger it briefly.
    //   splash    hits also strike every enemy within SplashRadius for full damage.
    //   heal      every HealInterval seconds it heals HealFraction of its max health.
    //   frenzy    attacks faster the lower its health, up to FrenzyBonus faster.
    public static class EliteAbilities
    {
        public const float PoisonDps = 8f;
        public const float PoisonDuration = 5f;
        public const float ExecuteChance = 0.12f;
        public const float StunChance = 0.3f;
        public const float StunDuration = 1.5f;
        public const float KnockbackDistance = 5f;
        public const float KnockbackStagger = 0.5f;
        public const float SplashRadius = 3f;
        public const float HealInterval = 6f;
        public const float HealFraction = 0.08f;
        public const float FrenzyBonus = 1f;

        public static string Describe(string[] abilities)
        {
            if (abilities == null || abilities.Length == 0) return "";
            return string.Join(", ", abilities);
        }

        // Called by an elite after a hit on `target` landed for `damage`.
        public static void OnHit(Unit elite, Combatant target, float damage)
        {
            if (elite.Abilities == null) return;
            var victim = target as Unit;
            foreach (var ability in elite.Abilities)
            {
                switch (ability)
                {
                    case "poison":
                        if (victim != null) victim.Poison(PoisonDps, PoisonDuration, elite.Team);
                        break;
                    case "execute":
                        if (victim != null && !victim.IsElite && Random.value < ExecuteChance)
                        {
                            if (victim.Team == GameState.PlayerTeam) FloatingText.Add(victim.transform.position, "Executed!", null, new Color(1f, 0.25f, 0.2f), 16f, 2f, 1.5f);
                            victim.TakeTrueDamage(victim.Health, elite.Team);
                        }
                        break;
                    case "stun":
                        if (victim != null && Random.value < StunChance) victim.Stun(StunDuration);
                        break;
                    case "knockback":
                        if (victim != null) victim.Knockback(elite.transform.position, KnockbackDistance, KnockbackStagger);
                        break;
                    case "splash":
                        Vector3 centre = target.transform.position;
                        foreach (var other in Combatant.All.ToArray())
                        {
                            if (other == target || other.Team == elite.Team || other.IsDead || other.IsBuilding) continue;
                            if (other.EdgeDistanceFrom(centre) > SplashRadius) continue;
                            other.TakeDamage(damage, elite.Team, elite);
                        }
                        break;
                }
            }
        }

        // Called every physics step for a living elite.
        public static void Tick(Unit elite, ref float nextHealTime)
        {
            if (elite.Abilities == null) return;
            foreach (var ability in elite.Abilities)
            {
                if (ability == "heal" && Time.time >= nextHealTime)
                {
                    nextHealTime = Time.time + HealInterval;
                    elite.Heal(elite.MaxHealth * HealFraction);
                    SpawnPoof.Spawn(elite.transform.position - Vector3.up * (elite.HealthBarHeight * 0.4f), new Color(0.5f, 1f, 0.5f), elite.transform.parent);
                }
            }
        }

        // Attack-speed multiplier for frenzy: 1 at full health, 1 + FrenzyBonus near death.
        public static float AttackSpeedMultiplier(Unit elite)
        {
            if (elite.Abilities == null || System.Array.IndexOf(elite.Abilities, "frenzy") < 0) return 1f;
            float missing = 1f - elite.Health / Mathf.Max(1f, elite.MaxHealth);
            return 1f + FrenzyBonus * missing;
        }
    }
}

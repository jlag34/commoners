using UnityEngine;

namespace SwarmPrototype
{
    // Shoots the nearest enemy unit in range on a timer. Sits on the main tower
    // and on every secondary tower. Damage lands on fire; the bolt is cosmetic.
    public class TowerGun : MonoBehaviour
    {
        const float RetargetInterval = 0.3f;

        Combatant owner;
        TowerConfig config;
        Combatant target;
        float nextShotTime;
        float nextScanTime;
        float muzzleHeight;

        public static void Attach(Combatant owner, TowerConfig config, float muzzleHeight)
        {
            if (config.attack <= 0f || config.range <= 0f) return;
            var gun = owner.gameObject.AddComponent<TowerGun>();
            gun.owner = owner;
            gun.config = config;
            gun.muzzleHeight = muzzleHeight;
            gun.nextShotTime = Time.time + Random.value;
        }

        // Team-wide tower bonuses bought in the shop, read live from the team's main tower.
        TowerBonus Bonus
        {
            get
            {
                var home = owner.Team >= 0 && owner.Team < GameState.Bases.Length ? GameState.Bases[owner.Team] : null;
                return home != null ? home.Towers : empty;
            }
        }
        static readonly TowerBonus empty = new TowerBonus();

        public float Range => config.range + Bonus.range;
        public float Attack => config.attack * (1f + Bonus.damage);

        void Update()
        {
            if (GameState.Over || owner.IsDead) return;
            if (Time.time >= nextScanTime)
            {
                nextScanTime = Time.time + RetargetInterval;
                if (target == null || target.IsDead || owner.EdgeDistanceFrom(target.transform.position) > Range) target = Nearest();
            }
            if (target == null || Time.time < nextShotTime) return;
            nextShotTime = Time.time + 1f / Mathf.Max(0.05f, config.attacksPerSecond);
            Bolt.Fire(owner.transform.position + Vector3.up * muzzleHeight, target, owner.Team, transform.parent);
            target.TakeDamage(Attack, owner.Team, owner);
        }

        Combatant Nearest()
        {
            Combatant best = null;
            float bestDistance = Range;
            Vector3 position = owner.transform.position;
            foreach (var c in Combatant.All)
            {
                if (c.Team == owner.Team || c.IsDead || c.IsBuilding) continue;
                float d = owner.EdgeDistanceFrom(c.transform.position) - c.Radius;
                if (d <= bestDistance)
                {
                    bestDistance = d;
                    best = c;
                }
            }
            return best;
        }
    }

    // A glowing ball that flies from a tower to its target, then vanishes.
    public class Bolt : MonoBehaviour
    {
        const float Speed = 30f;

        static Material[] materials = new Material[GameState.MaxTeams + 1];

        Transform target;
        Vector3 lastKnown;

        public static void Fire(Vector3 from, Combatant target, int team, Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Bolt";
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.position = from;
            go.transform.localScale = Vector3.one * 0.35f;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = Material(team);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var bolt = go.AddComponent<Bolt>();
            bolt.target = target.transform;
            bolt.lastKnown = target.transform.position + Vector3.up;
        }

        // One glowing material per shooting team.
        static Material Material(int team)
        {
            int key = Mathf.Clamp(team, 0, materials.Length - 1);
            if (materials[key] == null)
            {
                Color color = GameState.ColorOf(key);
                var m = Visuals.Plain(color);
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", color * 1.5f);
                materials[key] = m;
            }
            return materials[key];
        }

        void Update()
        {
            if (target != null) lastKnown = target.position + Vector3.up * 0.8f;
            Vector3 to = lastKnown - transform.position;
            float step = Speed * Time.deltaTime;
            if (to.magnitude <= step)
            {
                Destroy(gameObject);
                return;
            }
            transform.position += to.normalized * step;
        }
    }

    // A secondary tower in a base's corner. Worth gold to whoever knocks it down.
    public class Tower : Combatant
    {
        const float Width = 1.6f;
        const float Height = 2.6f;

        int bounty;

        public override float HealthBarHeight => 3.4f;
        public override bool IsBuilding => true;
        public override float FeetY => transform.position.y - Height * 0.5f;

        public static Tower Create(int team, Vector3 position, TowerConfig config, Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = GameState.TeamNames[team] + " Tower";
            go.transform.SetParent(parent, false);
            go.transform.position = position + Vector3.up * (Height * 0.5f);
            go.transform.localScale = new Vector3(Width, Height, Width);
            go.GetComponent<Renderer>().enabled = false;

            Color color = GameState.ColorOf(team);
            BaseVisual.BuildCornerTower(go, color, Width, Height);
            BaseTerrain.MakeObstacle(go);

            var tower = go.AddComponent<Tower>();
            tower.Team = team;
            tower.MaxHealth = tower.Health = config.health;
            tower.Armor = config.armor;
            tower.Radius = Width * 0.5f;
            tower.bounty = config.bounty;
            TowerGun.Attach(tower, config, Height);
            return tower;
        }

        void Update()
        {
            if (GameState.Over) return;
            var home = Team < GameState.Bases.Length ? GameState.Bases[Team] : null;
            if (home != null && home.Towers.regen > 0f) Heal(home.Towers.regen * Time.deltaTime);
        }

        protected override void OnHit(Combatant attacker)
        {
            if (Team == GameState.PlayerTeam) GameState.OnPlayerBuildingHit(transform.position);
        }

        protected override void Die(int killerTeam)
        {
            GameState.AddGold(killerTeam, bounty, transform.position);
            // Leave a bit of rubble where it stood.
            var rubble = new GameObject("Rubble").transform;
            rubble.SetParent(transform.parent, false);
            rubble.position = transform.position - Vector3.up * (Height * 0.5f);
            var stone = Visuals.SpawnModel("stone_smallA", rubble);
            if (stone != null) Visuals.FitHeight(stone, 0.7f);
            Destroy(gameObject);
        }
    }
}

using UnityEngine;

namespace SwarmPrototype
{
    // Simple bot: lets its swarm gather round its tower, and on a timer sends a
    // wave at a random living opponent's base (through whichever entrance the
    // route picks), keeping a few units home to defend. New units always gather at home.
    // Spends its gold in the shop as it comes in, cheapest upgrade first, so its
    // build stays even across all stats.
    public class EnemyBrain : MonoBehaviour
    {
        Spawner self;
        Spawner[] everyone;
        GameConfig config;
        float nextAttackTime;
        float nextShopTime;
        const float ShopInterval = 1f;

        public void Init(Spawner self, Spawner[] everyone, GameConfig config)
        {
            this.self = self;
            this.everyone = everyone;
            this.config = config;
            nextAttackTime = Time.time + config.enemyFirstAttackDelay;
        }

        void Update()
        {
            if (self == null || self.IsDead || GameState.Over) return;

            if (Time.time >= nextShopTime)
            {
                nextShopTime = Time.time + ShopInterval;
                Shop();
            }

            if (Time.time < nextAttackTime) return;

            var home = new System.Collections.Generic.List<Unit>();
            foreach (var c in Combatant.All)
                if (c is Unit u && u.Team == self.Team && !u.HasOrder && !u.IsDead) home.Add(u);

            // Not enough yet: check again shortly rather than waiting a whole interval.
            if (home.Count - config.enemyDefenders < config.enemyAttackMinUnits)
            {
                nextAttackTime = Time.time + 2f;
                return;
            }

            Spawner opponent = PickOpponent();
            if (opponent == null) return;

            // Keep the defenders nearest the base, send the rest.
            Vector3 basePosition = self.transform.position;
            home.Sort((a, b) => (a.transform.position - basePosition).sqrMagnitude.CompareTo((b.transform.position - basePosition).sqrMagnitude));
            for (int i = config.enemyDefenders; i < home.Count; i++)
                home[i].AttackMove(opponent.Foot);

            GameState.OnEnemyWave();
            nextAttackTime = Time.time + config.enemyAttackInterval;
        }

        // Any living base that isn't ours, picked at random so no one player gets ganged up on.
        Spawner PickOpponent()
        {
            var alive = new System.Collections.Generic.List<Spawner>();
            foreach (var b in everyone)
                if (b != null && b != self && !b.IsDead) alive.Add(b);
            return alive.Count == 0 ? null : alive[Random.Range(0, alive.Count)];
        }

        // Buys the cheapest upgrade it can afford, lowest level breaking ties, until
        // it runs out of gold. With equal base prices this rotates through the stats.
        void Shop()
        {
            while (true)
            {
                int pick = -1;
                for (int i = 0; i < self.Upgrades.Length; i++)
                {
                    // Only things it can pay for in full (gold, skulls and essence).
                    if (!self.CanAfford(i)) continue;
                    if (pick < 0 || self.Cost(i) < self.Cost(pick) || (self.Cost(i) == self.Cost(pick) && self.Level(i) < self.Level(pick)))
                        pick = i;
                }
                if (pick < 0) return;
                if (!self.Buy(pick)) return;
            }
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace SwarmPrototype
{
    // Anything with a team that can be attacked: units and bases.
    public abstract class Combatant : MonoBehaviour
    {
        public static readonly List<Combatant> All = new List<Combatant>();

        public int Team { get; protected set; }
        public float MaxHealth { get; protected set; }
        public float Health { get; protected set; }
        public float Armor { get; protected set; }
        public float Radius { get; protected set; }
        public bool IsDead => Health <= 0f;
        public abstract float HealthBarHeight { get; }
        // Towers and bases: units prefer other units over these, and show their bar always.
        public virtual bool IsBuilding => false;
        // Ground height under this thing, so "same level" works for any shape or size.
        public virtual float FeetY => transform.position.y;

        protected virtual void OnEnable() => All.Add(this);
        protected virtual void OnDisable() => All.Remove(this);

        // Flat armor reduction: armor shrugs off many small hits, big hits punch through.
        // Returns the damage actually dealt (0 if dodged).
        public float TakeDamage(float rawDamage, int attackerTeam, Combatant attacker = null)
        {
            if (IsDead || Dodges()) return 0f;
            float dealt = Mathf.Min(Health, Mathf.Max(1f, rawDamage - Armor));
            Health -= dealt;
            if (Health <= 0f)
            {
                Health = 0f;
                Die(attackerTeam);
                return dealt;
            }
            if (attacker != null) OnHit(attacker);
            return dealt;
        }

        // Damage that ignores armor and dodge (poison, executions).
        public float TakeTrueDamage(float amount, int attackerTeam)
        {
            if (IsDead || amount <= 0f) return 0f;
            float dealt = Mathf.Min(Health, amount);
            Health -= dealt;
            if (Health <= 0f)
            {
                Health = 0f;
                Die(attackerTeam);
            }
            return dealt;
        }

        public void Heal(float amount)
        {
            if (IsDead || amount <= 0f) return;
            Health = Mathf.Min(MaxHealth, Health + amount);
        }

        // Called after surviving a hit; units use it to fight back.
        protected virtual void OnHit(Combatant attacker) { }

        protected abstract void Die(int killerTeam);

        // Chance to ignore a hit entirely; units with evasion override this.
        protected virtual bool Dodges() => false;

        // Distance from a point to this combatant's edge, ignoring height.
        public float EdgeDistanceFrom(Vector3 point)
        {
            Vector3 offset = point - transform.position;
            offset.y = 0f;
            return Mathf.Max(0f, offset.magnitude - Radius);
        }
    }
}

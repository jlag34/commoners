using UnityEngine;
using UnityEngine.AI;

namespace SwarmPrototype
{
    // A basic melee swarm unit. Walks to its base's rally point (or holds where it
    // spawned if there is none), fights any enemy that comes within aggro range. Uses a real Rigidbody so units collide and
    // can body block each other; the NavMesh only supplies the route (corners),
    // physics does the moving.
    [RequireComponent(typeof(Rigidbody))]
    public class Unit : Combatant
    {
        const float ScanInterval = 0.25f;
        const float LeashMultiplier = 1.5f;
        const float ArriveDistance = 2.5f;
        const float CrowdedArriveDistance = 8f;
        const float RepathInterval = 0.5f;
        const float CornerReach = 0.7f;
        // Holding units are much heavier so walking units can't shove through them.
        const float MovingMass = 1f;
        const float HoldingMass = 25f;
        // Taller than this apart counts as different levels (top of a cliff vs. below it).
        const float LevelTolerance = 1.5f;
        // A forced move gives up and lets the unit fight if it's stuck this long.
        const float ForceMoveGiveUp = 2f;
        // Camp monsters drop their target and walk home once this far from camp.
        const float CampLeash = 14f;
        // Within this of a target's edge, stop following the NavMesh route and
        // steer straight for it, slipping sideways around anyone in the way.
        const float CloseRange = 7f;
        // Chasing something but not moving, with no unit in the way (a wall, a
        // cliff, no route): give up on it for a while and go back to orders.
        const float ChaseGiveUp = 2f;
        const float IgnoreDuration = 6f;
        // Idle units (settled, nothing to fight) look this far for enemy buildings,
        // so a swarm parked inside a base finishes off its corner towers.
        const float IdleBuildingAggro = 18f;
        // Boxed in on all sides this long: stop being polite and push through.
        const float WaitPatience = 0.6f;
        // How often a close-range chase rechecks for a tree or rock in the way.
        const float LineCheckInterval = 0.25f;

        static PhysicMaterial slippery;

        // Model from Resources/SwarmModels. Its red tabard and grey armor take the team color.
        const string ModelName = "Warrior";
        const float ModelHeight = 2.1f;
        const float ModelTeamHue = 0f;

        Spawner home;
        // Stats at spawn time; used by monsters and once the home tower is gone.
        UnitStats spawnStats;
        float appliedMoveSpeed;
        // Neutral monster: gold for whoever kills it, and the camp spot it guards.
        int bounty;
        Vector3? campSpot;
        Camp camp;
        string difficulty = "easy";
        float leash = CampLeash;
        public bool IsElite { get; private set; }
        public string DisplayName { get; private set; }
        public string[] Abilities { get; private set; }
        float nextHealTime;
        // Status effects.
        float poisonUntil, poisonDps;
        int poisonTeam;
        float stunUntil;
        public bool Stunned => Time.time < stunUntil;
        float scale = 1f;

        public bool IsMonster => campSpot.HasValue;

        // One-line state for the F1 overlay.
        public static bool DebugDraw;

        public string DebugState
        {
            get
            {
                string t = target == null ? "-" : target.name;
                string g = order.HasValue ? "order" : home != null && home.RallyPoint.HasValue ? "rally" : home != null ? "spawn" : "none";
                string p = !hasPath ? "none/" + lastPathNote : $"{path.status}:{path.corners.Length}c@{corner}";
                return $"{t} | {g} | {(settled ? "settled" : "moving")}{(forceMove ? " force" : "")} | path:{p} wait:{waitTime:0.0} stuck:{chaseStuckTime:0.0} v:{HorizontalSpeed():0.0}";
            }
        }
        string lastPathNote = "";
        bool unreachable;
        float chaseStuckTime;
        float waitTime;
        // Where we appeared: with no rally point set, this is home after a fight.
        Vector3 spawnSpot;
        Combatant ignoredTarget;
        float ignoreUntil;

        public Vector3 Velocity
        {
            get
            {
#if UNITY_6000_0_OR_NEWER
                return body.linearVelocity;
#else
                return body.velocity;
#endif
            }
        }
        // Standing still on purpose (at the rally point or fighting): others flow round us.
        public bool Holding { get; private set; }
        // Heavier units shove lighter ones aside; equals go round each other.
        public float Weight => scale * scale;
        UnitAnimator visual;
        GameObject model;
        UnitGear gear;
        int gearVersion = -1;
        Rigidbody body;
        Combatant target;
        Vector3 lastRally;
        float nextScanTime;
        float nextAttackTime;
        float blockedTime;
        bool settled;
        // Made on demand: Unity forbids creating a NavMeshPath in a field initializer,
        // and a script recompile during play wipes it (it isn't serialized).
        NavMeshPath path;
        Vector3 pathGoal = new Vector3(float.MaxValue, 0f, 0f);
        int corner;
        bool hasPath;
        float nextRepathTime;
        Combatant lineTarget;
        bool lineBlocked;
        float nextLineCheckTime;
        // Move command: walk to the rally point ignoring enemies until arrived.
        bool forceMove;
        float forceStuckTime;
        // Attack-move order of its own (bot waves); null means follow the base's rally point.
        Vector3? order;

        public bool HasOrder => order.HasValue;

        // Drop everything and walk to the base's rally point; fight again once there.
        public void MoveToRally()
        {
            forceMove = true;
            forceStuckTime = 0f;
            order = null;
            target = null;
            settled = false;
            blockedTime = 0f;
        }

        // Walk to `point`, fighting anything met on the way.
        public void AttackMove(Vector3 point)
        {
            order = point;
            forceMove = false;
            settled = false;
            blockedTime = 0f;
        }

        public override float HealthBarHeight => 2.4f * scale;
        public override float FeetY => transform.position.y - scale;

        // Live team stats, so bought upgrades apply to units already on the field.
        UnitStats stats => home != null ? home.Stats : spawnStats;

        public static Unit Create(Spawner home, UnitStats stats, Vector3 position, Color color, Transform parent) =>
            Build(home, home.Team, stats, position, color, parent, 1f);

        // A camp monster: no home tower, hostile to every team, returns to `camp` after a fight.
        public static Unit CreateMonster(UnitStats stats, Vector3 position, float scale, int bounty, Color tint, Camp camp, string difficulty, Transform parent, EliteConfig elite = null)
        {
            var unit = Build(null, GameState.NeutralTeam, stats, position, tint, parent, scale, elite);
            unit.bounty = bounty;
            unit.campSpot = position;
            unit.order = position;
            unit.camp = camp;
            unit.difficulty = difficulty;
            return unit;
        }

        // The roaming elite: a lone, huge monster with a long leash and a name.
        public static Unit CreateElite(EliteConfig elite, UnitStats stats, Vector3 position, int bounty, Color tint, Transform parent)
        {
            var unit = CreateMonster(stats, position, elite.scale, bounty, tint, null, "elite", parent, elite);
            unit.IsElite = true;
            unit.DisplayName = elite.name;
            unit.Abilities = elite.abilities;
            unit.leash = elite.leash;
            unit.name = "Elite " + elite.name;
            return unit;
        }

        static Unit Build(Spawner home, int team, UnitStats stats, Vector3 position, Color color, Transform parent, float scale, EliteConfig elite = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = home != null ? "Unit" : "Monster";
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * scale;
            go.transform.position = position + Vector3.up * scale;
            go.GetComponent<Renderer>().material.color = color;
            // No friction, so units slide along walls and each other instead of sticking.
            if (slippery == null)
                slippery = new PhysicMaterial("Unit") { dynamicFriction = 0f, staticFriction = 0f, frictionCombine = PhysicMaterialCombine.Minimum };
            go.GetComponent<Collider>().sharedMaterial = slippery;

            var body = go.AddComponent<Rigidbody>();
            body.mass = MovingMass * scale * scale;
            body.constraints = RigidbodyConstraints.FreezeRotation;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.Continuous;

            var unit = go.AddComponent<Unit>();
            unit.scale = scale;
            unit.Init(home, team, stats);
            unit.Dress(color, elite);
            return unit;
        }

        // Swaps the capsule look for the warrior model if it's in Resources, and adds a
        // team-colored ring at the feet so teams read at a glance either way.
        // `elite` non-null dresses the model as that elite. Gear and elite pieces must go on
        // before PopIn, which zeroes the holder's scale: a piece placed under a zero-scale
        // parent gets a degenerate transform and never shows.
        void Dress(Color color, EliteConfig elite)
        {
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "Team Ring";
            DestroyImmediate(ring.GetComponent<Collider>());
            ring.transform.SetParent(transform, false);
            ring.transform.localPosition = new Vector3(0f, -0.97f, 0f);
            ring.transform.localScale = new Vector3(1.15f, 0.02f, 1.15f);
            var ringRenderer = ring.GetComponent<Renderer>();
            ringRenderer.sharedMaterial = TeamRingMaterial(Team, color);
            ringRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            model = Visuals.SpawnModel(ModelName, transform);
            if (model == null)
            {
                // A small block on the front so you can see which way a unit faces.
                var nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
                DestroyImmediate(nose.GetComponent<Collider>());
                nose.transform.SetParent(transform, false);
                nose.transform.localPosition = new Vector3(0f, 0.4f, 0.45f);
                nose.transform.localScale = new Vector3(0.3f, 0.2f, 0.3f);
                nose.GetComponent<Renderer>().material.color = Color.Lerp(color, Color.white, 0.5f);
                return;
            }

            GetComponent<Renderer>().enabled = false;
            var holder = new GameObject("Model").transform;
            holder.SetParent(transform, false);
            holder.localPosition = new Vector3(0f, -1f, 0f);
            model.transform.SetParent(holder, false);
            Visuals.FitHeight(model, ModelHeight * scale);
            Visuals.TeamColor(model, color, ModelTeamHue, 0.6f);
            foreach (var r in model.GetComponentsInChildren<Renderer>())
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            visual = UnitAnimator.Attach(model, Visuals.LoadClips(ModelName), body, stats.moveSpeed);
            if (home != null)
            {
                gear = UnitGear.Attach(model, color);
                SyncGear();
            }
            if (elite != null) EliteVisual.Dress(model, elite, color, transform.parent);
            PopIn.Attach(holder);
        }

        static Material[] ringMaterials = new Material[GameState.MaxTeams + 1];

        static Material TeamRingMaterial(int team, Color color)
        {
            if (ringMaterials[team] == null)
            {
                ringMaterials[team] = Visuals.Plain(color);
                ringMaterials[team].EnableKeyword("_EMISSION");
                ringMaterials[team].SetColor("_EmissionColor", color * 0.6f);
            }
            return ringMaterials[team];
        }

        void Init(Spawner home, int team, UnitStats stats)
        {
            this.home = home;
            spawnStats = stats;
            Team = team;
            MaxHealth = Health = stats.maxHealth;
            Armor = stats.armor;
            Radius = 0.5f * scale;
            body = GetComponent<Rigidbody>();
            lastRally = transform.position;
            spawnSpot = transform.position - Vector3.up * scale;
            nextScanTime = Time.time + Random.value * ScanInterval;
        }

        public void Poison(float dps, float duration, int team)
        {
            poisonDps = Mathf.Max(poisonDps, dps);
            poisonUntil = Mathf.Max(poisonUntil, Time.time + duration);
            poisonTeam = team;
        }

        public void Stun(float duration)
        {
            stunUntil = Mathf.Max(stunUntil, Time.time + duration);
        }

        // Thrown `distance` away from `from`, then staggered so the throw isn't cancelled.
        public void Knockback(Vector3 from, float distance, float stagger)
        {
            Vector3 dir = transform.position - from;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) dir = Random.insideUnitSphere;
            dir.Normalize();
            Hold(false);
            // Covers `distance` in about `stagger` seconds, friction-free.
            Vector3 v = dir * (distance / Mathf.Max(0.1f, stagger)) + Vector3.up * 1.5f;
            SetHorizontalVelocity(v);
            Stun(stagger);
        }

        void StatusEffects()
        {
            if (Time.time < poisonUntil && poisonDps > 0f)
            {
                float dealt = TakeTrueDamage(poisonDps * Time.fixedDeltaTime, poisonTeam);
                if (IsDead) return;
                // Little green puffs now and then so poison is visible.
                if (Random.value < Time.fixedDeltaTime * 2f) SpawnPoof.Spawn(transform.position - Vector3.up * scale, new Color(0.3f, 0.9f, 0.3f), transform.parent);
            }
        }

        void FixedUpdate()
        {
            SyncStats();
            StatusEffects();
            if (IsDead) return;
            if (Stunned)
            {
                // Knockback keeps its velocity; a plain stun stands still.
                if (HorizontalSpeed() < 1f) Stop();
                return;
            }
            if (IsElite) EliteAbilities.Tick(this, ref nextHealTime);
            if (forceMove) target = null;
            else if (Time.time >= nextScanTime)
            {
                nextScanTime = Time.time + ScanInterval;
                Retarget();
            }
            if (target == null || target.IsDead) target = null;

            // Monsters don't chase across the map: past the leash they head home
            // and ignore everything for a moment so they actually get there.
            if (target != null && campSpot.HasValue && FlatDistance(transform.position, campSpot.Value) > leash)
            {
                ignoredTarget = target;
                ignoreUntil = Time.time + IgnoreDuration;
                target = null;
                nextScanTime = Time.time + 1.5f;
            }

            if (target != null)
            {
                float gap = target.EdgeDistanceFrom(transform.position) - Radius;
                if (gap <= stats.attackRange && SameLevel(target))
                {
                    Stop();
                    Hold(true);
                    Face(target.transform.position);
                    TryAttack();
                }
                else
                {
                    Hold(false);
                    bool blockedByUnit = Chase(target);
                    bool stuck = !blockedByUnit && HorizontalSpeed() < stats.moveSpeed * 0.3f;
                    chaseStuckTime = stuck ? chaseStuckTime + Time.fixedDeltaTime : 0f;
                    if (!hasPath) chaseStuckTime += Time.fixedDeltaTime * 2f;
                    if (unreachable) chaseStuckTime = ChaseGiveUp + 1f;
                    if (chaseStuckTime > ChaseGiveUp)
                    {
                        ignoredTarget = target;
                        ignoreUntil = Time.time + IgnoreDuration;
                        target = null;
                        chaseStuckTime = 0f;
                    }
                }
                settled = false;
                return;
            }

            // Orders first, then the team rally point, then back to where we spawned
            // (so a unit that chased something out of the base walks home again).
            Vector3? goal = order;
            if (!goal.HasValue && home != null) goal = home.RallyPoint ?? spawnSpot;
            if (!goal.HasValue)
            {
                forceMove = false;
                settled = true;
                Hold(true);
                Stop();
                return;
            }

            Vector3 rally = goal.Value;
            if ((rally - lastRally).sqrMagnitude > 0.01f)
            {
                lastRally = rally;
                settled = false;
                blockedTime = 0f;
            }

            // Distance along the route, so a rally point just below a cliff isn't "close".
            float distance = RemainingDistance(rally);

            // Units crowding a rally point can't all reach the exact spot. Once a unit
            // is close and stuck behind others for a moment, it counts as arrived.
            if (!settled)
            {
                if (distance < ArriveDistance) settled = true;
                else if (distance < CrowdedArriveDistance && blockedTime > 0.5f) settled = true;
            }

            Hold(settled);
            if (settled)
            {
                forceMove = false;
                Stop();
                return;
            }

            Combatant blocker = MoveTowards(rally);
            // Reached the edge of the pile already gathered there: that's close enough.
            if (!settled && distance < CrowdedArriveDistance && blocker is Unit ahead && ahead.Holding && ahead.Team == Team)
                settled = true;
            float speed = HorizontalSpeed();
            blockedTime = speed < stats.moveSpeed * 0.3f ? blockedTime + Time.fixedDeltaTime : 0f;
            if (forceMove)
            {
                forceStuckTime = speed < stats.moveSpeed * 0.3f ? forceStuckTime + Time.fixedDeltaTime : 0f;
                if (forceStuckTime > ForceMoveGiveUp) forceMove = false;
            }
        }

        // Pulls upgrades bought since last step into this unit. Extra max health
        // is added as current health too, so buying Health heals by the same amount.
        // Refresh sword / armor / size whenever the team has bought something.
        void SyncGear()
        {
            if (gear == null || home == null || home.Version == gearVersion) return;
            gearVersion = home.Version;
            gear.Apply(home.LevelOf("strength"), home.LevelOf("armor"), home.LevelOf("health"), home.LevelOf("moveSpeed"));
        }

        void SyncStats()
        {
            SyncGear();
            UnitStats s = stats;
            if (s.maxHealth != MaxHealth)
            {
                Health += s.maxHealth - MaxHealth;
                MaxHealth = s.maxHealth;
            }
            Armor = s.armor;
            if (s.regen > 0f) Heal(s.regen * Time.fixedDeltaTime);
            if (visual != null && s.moveSpeed != appliedMoveSpeed)
            {
                appliedMoveSpeed = s.moveSpeed;
                visual.SetMoveSpeed(s.moveSpeed);
            }
        }

        protected override bool Dodges() => stats.evasion > 0f && Random.value < stats.evasion;

        // Shot by something we weren't fighting (a tower out past aggro range, say):
        // go for it, unless the player has ordered a move.
        protected override void OnHit(Combatant attacker)
        {
            if (forceMove || target != null || attacker.IsDead || attacker.Team == Team || Ignored(attacker)) return;
            if (IsMonster && !MonsterCanTarget(attacker)) return;
            target = attacker;
            settled = false;
        }

        void Retarget()
        {
            float leash = stats.aggroRange * LeashMultiplier;
            if (target != null && !target.IsDead && target.EdgeDistanceFrom(transform.position) <= leash)
                return;

            target = null;
            float best = float.MaxValue;
            Vector3 position = transform.position;
            bool idle = settled && !forceMove;
            foreach (var other in All)
            {
                if (other.Team == Team || other.IsDead || Ignored(other)) continue;
                if (IsMonster && !MonsterCanTarget(other)) continue;
                float distance = other.EdgeDistanceFrom(position);
                float range = idle && other.IsBuilding && !IsMonster ? Mathf.Max(stats.aggroRange, IdleBuildingAggro) : stats.aggroRange;
                if (distance > range) continue;
                // Prefer fighting units over hitting a building, and enemy players over monsters.
                float score = distance;
                if (other.IsBuilding) score += 4f;
                else if (other.Team == GameState.NeutralTeam) score += 2f;
                if (score < best)
                {
                    best = score;
                    target = other;
                }
            }
        }

        bool Ignored(Combatant c) => c == ignoredTarget && Time.time < ignoreUntil;

        // Monsters guard their camp: they never go for buildings and never chase
        // anything that's already outside the leash (so a tower can't kite them
        // up and down a ramp with every shot).
        bool MonsterCanTarget(Combatant c) =>
            !c.IsBuilding && FlatDistance(c.transform.position, campSpot.Value) <= leash;

        const float CritMultiplier = 2f;
        const float SplashRadius = 1.6f;

        void TryAttack()
        {
            if (Time.time < nextAttackTime) return;
            float rate = stats.attacksPerSecond * (IsElite ? EliteAbilities.AttackSpeedMultiplier(this) : 1f);
            nextAttackTime = Time.time + 1f / Mathf.Max(0.05f, rate);
            if (visual != null) visual.OnAttack();

            float damage = stats.attack;
            if (stats.critChance > 0f && Random.value < stats.critChance) damage *= CritMultiplier;
            Combatant struck = target;
            float dealt = target.TakeDamage(damage, Team, this);
            if (IsElite && dealt > 0f) EliteAbilities.OnHit(this, struck, damage);

            if (stats.splash > 0f)
            {
                Vector3 center = struck.transform.position;
                // Snapshot: a kill in the loop removes from All.
                foreach (var other in All.ToArray())
                {
                    if (other == null || other == struck || other.Team == Team || other.IsDead || other.IsBuilding) continue;
                    if (other.EdgeDistanceFrom(center) > SplashRadius) continue;
                    dealt += other.TakeDamage(damage * stats.splash, Team, this);
                }
            }
            if (stats.lifesteal > 0f) Heal(dealt * stats.lifesteal);
        }

        // Far from the target, follow the route. Close in, head straight for its edge;
        // if an ally (or anyone) is in the way, slide round the side of them that is
        // away from us. Units stuck behind the front line keep flowing round it
        // until they find a free spot, so a swarm wraps whatever it is fighting.
        // Returns true if a unit was in the way (so standing still isn't "stuck").
        bool Chase(Combatant target)
        {
            Vector3 toTarget = target.transform.position - transform.position;
            toTarget.y = 0f;
            float distance = toTarget.magnitude;
            // Far away, up on a plateau we're not on (route goes up the ramp), or
            // a tree or rock in the way (route goes round it): follow the route.
            if (distance > target.Radius + Radius + CloseRange || !SameLevel(target) || LineBlocked(target, toTarget, distance))
                return MoveTowards(target.transform.position) != null;

            bool blocked = Steer(target.transform.position, target) != null;
            Face(target.transform.position);
            return blocked;
        }

        // True if the straight run to `target`'s edge crosses a hole in the NavMesh
        // (a tree, a rock, a cliff). Buildings sit in their own carved hole, so they
        // aren't checked. Cached for LineCheckInterval.
        bool LineBlocked(Combatant target, Vector3 toTarget, float distance)
        {
            if (target.IsBuilding) return false;
            if (target == lineTarget && Time.time < nextLineCheckTime) return lineBlocked;
            lineTarget = target;
            nextLineCheckTime = Time.time + LineCheckInterval;
            lineBlocked = false;
            float reach = distance - target.Radius - Radius - 0.5f;
            if (reach <= 0f) return false;
            Vector3 feet = transform.position - Vector3.up * scale;
            if (!NavMesh.SamplePosition(feet, out var from, 1f, NavMesh.AllAreas)) return false;
            lineBlocked = NavMesh.Raycast(from.position, from.position + toTarget / distance * reach, out _, NavMesh.AllAreas);
            return lineBlocked;
        }

        // Walks the NavMesh route toward `goal`. Returns whoever was in the way, if anyone.
        Combatant MoveTowards(Vector3 goal)
        {
            Vector3 waypoint = NextWaypoint(goal);
            Combatant blocker = Steer(waypoint, null);
            Vector3 direction = waypoint - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.0001f) body.MoveRotation(Quaternion.LookRotation(direction));
            return blocker;
        }

        // Moves toward `point` without driving into anyone of our weight or more. A
        // unit in our lane ahead makes us slide round the side of them that's away
        // from us; if that side is blocked too we try the other, and if both are
        // we wait. So piles get walked round, not shoved, and only heavier units
        // (ogres) push through. Returns the unit that was in the way, if any.
        Combatant Steer(Vector3 point, Combatant ignore)
        {
            Vector3 dir = point - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f)
            {
                Stop();
                return null;
            }
            dir.Normalize();

            Combatant blocker = Ahead(dir, ignore, out float across);
            // Someone already moving our way at a decent clip isn't in the way: follow them.
            if (blocker is Unit mover && Vector3.Dot(mover.Velocity, dir) > stats.moveSpeed * 0.5f) blocker = null;
            if (blocker != null)
            {
                Vector3 side = Vector3.Cross(Vector3.up, dir);
                // Dead ahead: pick a side once per unit so it doesn't dither.
                float slide = Mathf.Abs(across) < 0.05f ? ((GetInstanceID() & 1) == 0 ? 1f : -1f) : (across > 0f ? -1f : 1f);
                Vector3 first = (dir * 0.25f + side * slide).normalized;
                Vector3 second = (dir * 0.25f - side * slide).normalized;
                if (Ahead(first, ignore, out _) == null && Walkable(first)) dir = first;
                else if (Ahead(second, ignore, out _) == null && Walkable(second)) dir = second;
                else if (waitTime < WaitPatience)
                {
                    // Boxed in: wait a moment for the pile to move, then shove through.
                    waitTime += Time.fixedDeltaTime;
                    Stop();
                    return blocker;
                }
            }
            else waitTime = 0f;
            SetHorizontalVelocity(dir * stats.moveSpeed);
            return blocker;
        }

        // True if a step along `dir` still lands on the NavMesh at about our level, so
        // a sidestep round a blocker never walks off a ramp or a cliff edge.
        bool Walkable(Vector3 dir)
        {
            Vector3 feet = transform.position - Vector3.up * scale + dir * (Radius + 0.6f);
            return NavMesh.SamplePosition(feet, out var hit, 0.6f, NavMesh.AllAreas) && Mathf.Abs(hit.position.y - feet.y) < 0.6f;
        }

        // The nearest unit within touching distance in front of us along `dir` that we
        // can't just push: anything of our weight or heavier. `across` is how far to
        // the right (+) or left (-) of our line it is.
        Combatant Ahead(Vector3 dir, Combatant ignore, out float across)
        {
            across = 0f;
            Combatant best = null;
            float nearest = float.MaxValue;
            Vector3 side = Vector3.Cross(Vector3.up, dir);
            Vector3 position = transform.position;
            foreach (var other in All)
            {
                if (other == this || other == ignore || other.IsDead || !(other is Unit unit)) continue;
                if (unit.Weight < Weight * 0.6f) continue;
                Vector3 offset = other.transform.position - position;
                offset.y = 0f;
                float reach = Radius + other.Radius + 0.4f;
                float d2 = offset.sqrMagnitude;
                if (d2 > reach * reach || d2 >= nearest) continue;
                float along = Vector3.Dot(offset, dir);
                // Only things in the cone in front of us count, not neighbours beside us.
                if (along <= Mathf.Sqrt(d2) * 0.45f) continue;
                float lateral = Vector3.Dot(offset, side);
                if (Mathf.Abs(lateral) > Radius + other.Radius) continue;
                nearest = d2;
                best = other;
                across = lateral;
            }
            return best;
        }

        // The next corner of the NavMesh route to `goal`, re-planned every half second
        // or when the goal moves. Falls back to walking straight if there's no route.
        Vector3 NextWaypoint(Vector3 goal)
        {
            if (Time.time >= nextRepathTime || (goal - pathGoal).sqrMagnitude > 1f)
                Repath(goal);
            if (!hasPath) return goal;

            var corners = path.corners;
            while (corner < corners.Length - 1 && FlatDistance(transform.position, corners[corner]) < CornerReach)
                corner++;
            return corners[corner];
        }

        void Repath(Vector3 goal)
        {
            pathGoal = goal;
            nextRepathTime = Time.time + RepathInterval + Random.value * 0.2f;
            hasPath = false;
            unreachable = false;
            if (path == null) path = new NavMeshPath();
            Vector3 feet = transform.position - Vector3.up * scale;
            // Off the mesh (shoved onto a wall top, say): route from the nearest point
            // on it, so the first corner walks us back onto walkable ground.
            if (!NavMesh.SamplePosition(feet, out var from, 2f, NavMesh.AllAreas) &&
                !NavMesh.SamplePosition(feet, out from, 8f, NavMesh.AllAreas)) { lastPathNote = "no mesh under me"; Diagnostics.Count(lastPathNote); return; }
            // Aim at the goal's ground level so a tower on a plateau samples the plateau, not the cliff foot.
            if (target != null && (goal - target.transform.position).sqrMagnitude < 0.01f) goal.y = target.FeetY;
            // Prefer a point on the same level as the goal; only then look further afield.
            if (!NavMesh.SamplePosition(goal, out var to, 1.5f, NavMesh.AllAreas) &&
                !NavMesh.SamplePosition(goal, out to, 5f, NavMesh.AllAreas)) { lastPathNote = "no mesh at goal"; Diagnostics.Count(lastPathNote); return; }
            if (!NavMesh.CalculatePath(from.position, to.position, NavMesh.AllAreas, path)) { lastPathNote = "calc failed"; Diagnostics.Count(lastPathNote); return; }
            if (path.status == NavMeshPathStatus.PathInvalid || path.corners.Length == 0) { lastPathNote = "invalid"; Diagnostics.Count(lastPathNote); return; }
            // A partial route stops at the nearest reachable point. If that's not right
            // next to the goal (a tower on a plateau seen from the cliff foot, say),
            // there is no way there: don't walk at a wall, give the target up.
            if (path.status == NavMeshPathStatus.PathPartial)
            {
                Vector3 end = path.corners[path.corners.Length - 1];
                float shortBy = Vector3.Distance(end, to.position);
                if (shortBy > 1.5f)
                {
                    lastPathNote = "unreachable";
                    unreachable = true;
                    Diagnostics.Count(lastPathNote);
                    Diagnostics.Partial(this, goal, to.position, end, shortBy);
                    return;
                }
            }
            hasPath = true;
            Diagnostics.Count(path.status == NavMeshPathStatus.PathComplete ? "ok" : "partial-ok");
            if (DebugDraw)
                for (int i = 0; i < path.corners.Length - 1; i++)
                    Debug.DrawLine(path.corners[i] + Vector3.up * 0.2f, path.corners[i + 1] + Vector3.up * 0.2f, Color.cyan, RepathInterval);
            corner = path.corners.Length > 1 ? 1 : 0;
        }

        float RemainingDistance(Vector3 goal)
        {
            if (Time.time >= nextRepathTime || (goal - pathGoal).sqrMagnitude > 1f)
                Repath(goal);
            if (!hasPath) return FlatDistance(transform.position, goal);
            var corners = path.corners;
            float total = FlatDistance(transform.position, corners[corner]);
            for (int i = corner; i < corners.Length - 1; i++)
                total += Vector3.Distance(corners[i], corners[i + 1]);
            return total;
        }

        static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        // Feet within LevelTolerance of the other thing's feet: same plateau or both below.
        bool SameLevel(Combatant other) => Mathf.Abs(other.FeetY - FeetY) < LevelTolerance;

        void Hold(bool holding)
        {
            Holding = holding;
            body.mass = (holding ? HoldingMass : MovingMass) * scale * scale;
        }

        void Face(Vector3 point)
        {
            Vector3 direction = point - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.0001f)
                body.MoveRotation(Quaternion.LookRotation(direction));
        }

        void Stop() => SetHorizontalVelocity(Vector3.zero);

        // Keeps the vertical part so gravity and ramps still work.
        void SetHorizontalVelocity(Vector3 velocity)
        {
#if UNITY_6000_0_OR_NEWER
            velocity.y = body.linearVelocity.y;
            body.linearVelocity = velocity;
#else
            velocity.y = body.velocity.y;
            body.velocity = velocity;
#endif
        }

        float HorizontalSpeed()
        {
#if UNITY_6000_0_OR_NEWER
            Vector3 velocity = body.linearVelocity;
#else
            Vector3 velocity = body.velocity;
#endif
            velocity.y = 0f;
            return velocity.magnitude;
        }

        protected override void Die(int killerTeam)
        {
            if (IsMonster)
            {
                GameState.OnMonsterKilled(killerTeam, bounty, difficulty, transform.position);
                if (camp != null) camp.OnMonsterDied(killerTeam);
                if (IsElite && killerTeam >= 0 && killerTeam < GameState.TeamCount)
                    GameState.Toast?.Invoke($"{GameState.TeamNames[killerTeam]} slew the {DisplayName}");
            }
            else GameState.OnUnitKilled(killerTeam, Team, transform.position);
            if (home != null)
            {
                home.AliveUnits--;
                if (home.Economy.reincarnate > 0f && Random.value < home.Economy.reincarnate) home.Reincarnate();
            }
            if (visual != null)
            {
                // Leave the model behind to play its death clip.
                var corpse = visual.transform.parent;
                corpse.SetParent(transform.parent, true);
                visual.PlayDeath();
                Destroy(corpse.gameObject, 2.5f);
            }
            Destroy(gameObject);
        }
    }
}

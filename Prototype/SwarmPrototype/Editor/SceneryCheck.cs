using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

// Dev-only: checks that trees and rocks block units. Logs how many blockers sit in a
// NavMesh hole and whether routes past them stay outside, walks a group past a tree,
// starts a close-range chase across a rock, then lets the bots play for two minutes
// counting any unit standing inside a blocker. Writes screenshot-scenery.png next to Prototype.
//   Unity -batchmode -projectPath <proj> -executeMethod SwarmPrototype.SceneryCheck.Run -logFile <log>
namespace SwarmPrototype
{
    public static class SceneryCheck
    {
        const string Flag = "SwarmPrototype.SceneryCheck";
        const int Mask = 1 << Visuals.SceneryLayer;
        const float WalkTime = 10f, ChaseTime = 10f, MatchTime = 120f;

        static int frame, phase;
        static float phaseStart;
        static bool shot;
        static Collider[] blockers;
        static readonly List<Unit> testUnits = new List<Unit>();
        static Vector3 walkGoal;
        static int insideFrames;
        static readonly HashSet<Unit> insideUnits = new HashSet<Unit>();
        static float chaseContact = -1f;

        public static void Run()
        {
            SessionState.SetBool(Flag, true);
            EditorApplication.isPlaying = true;
        }

        [InitializeOnLoadMethod]
        static void Hook()
        {
            if (SessionState.GetBool(Flag, false)) EditorApplication.update += Tick;
        }

        static void Tick()
        {
            if (!EditorApplication.isPlaying || GameState.Bases == null || GameState.Bases[0] == null) return;
            if (++frame < 5) return;
            float elapsed = Time.time - phaseStart;
            switch (phase)
            {
                case 0:
                    blockers = Object.FindObjectsOfType<Collider>().Where(c => c.gameObject.layer == Visuals.SceneryLayer).ToArray();
                    CheckMesh();
                    StartWalk();
                    Next();
                    break;
                case 1:
                    Watch(testUnits);
                    if (!shot && elapsed > 2.5f) { Shoot(); shot = true; }
                    if (elapsed < WalkTime) break;
                    // A crowd settles up to 8m short of a shared goal (Unit.CrowdedArriveDistance).
                    int arrived = testUnits.Count(u => u != null && Flat(u.transform.position, walkGoal) < 8f);
                    string left = string.Join(" ", testUnits.Where(u => u != null).Select(u => $"{Flat(u.transform.position, walkGoal):F1}m[{u.DebugState}]"));
                    Debug.Log($"[Scenery] walk past a tree: {arrived}/{testUnits.Count} arrived ({left}), {insideFrames} unit-frames inside a blocker");
                    Clear();
                    StartChase();
                    Next();
                    break;
                case 2:
                    Watch(testUnits);
                    if (chaseContact < 0f && testUnits.All(u => u != null && !u.IsDead) &&
                        Flat(testUnits[0].transform.position, testUnits[1].transform.position) < testUnits[0].Radius + testUnits[1].Radius + 1f)
                        chaseContact = elapsed;
                    if (elapsed < ChaseTime && chaseContact < 0f) break;
                    Debug.Log($"[Scenery] chase across a rock: " + (chaseContact < 0f ? "never met" : $"met after {chaseContact:F1}s") +
                              $", {insideFrames} unit-frames inside a blocker");
                    Clear();
                    Time.timeScale = 4f;
                    Next();
                    break;
                case 3:
                    Watch(Combatant.All.OfType<Unit>());
                    if (elapsed < MatchTime) break;
                    Debug.Log($"[Scenery] {MatchTime:0}s of bot play: {insideFrames} unit-frames inside a blocker, {insideUnits.Count} units ever inside");
                    SessionState.EraseBool(Flag);
                    EditorApplication.update -= Tick;
                    if (Application.isBatchMode) EditorApplication.Exit(0);
                    else EditorApplication.isPlaying = false;
                    break;
            }
        }

        static void Next()
        {
            phase++;
            phaseStart = Time.time;
            insideFrames = 0;
            insideUnits.Clear();
        }

        // Every blocker on the baked map should be a hole, and a route from one side to
        // the other should never pass through it.
        static void CheckMesh()
        {
            float half = GameState.MapSize * 0.5f;
            int onMap = 0, leaks = 0, routes = 0, through = 0;
            foreach (var b in blockers)
            {
                Vector3 c = b.bounds.center;
                if (Mathf.Abs(c.x) > half - 2f || Mathf.Abs(c.z) > half - 2f) continue;
                onMap++;
                if (NavMesh.SamplePosition(new Vector3(c.x, 0f, c.z), out _, 0.25f, NavMesh.AllAreas)) leaks++;
                float reach = Mathf.Max(b.bounds.extents.x, b.bounds.extents.z) + 3f;
                if (!OnGround(c + Vector3.right * reach, out var from) || !OnGround(c - Vector3.right * reach, out var to)) continue;
                var path = new NavMeshPath();
                if (!NavMesh.CalculatePath(from, to, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) continue;
                routes++;
                if (Crosses(path.corners)) through++;
            }
            Debug.Log($"[Scenery] {blockers.Length} blockers, {onMap} on the map: {leaks} with walkable ground at their centre; " +
                      $"{routes} routes past them, {through} pass through a blocker");
        }

        static bool Crosses(Vector3[] corners)
        {
            for (int i = 0; i < corners.Length - 1; i++)
            {
                float length = Flat(corners[i], corners[i + 1]);
                for (float d = 0f; d <= length; d += 0.25f)
                {
                    Vector3 p = Vector3.Lerp(corners[i], corners[i + 1], length > 0f ? d / length : 0f);
                    if (Physics.CheckSphere(p + Vector3.up * 0.5f, 0.05f, Mask, QueryTriggerInteraction.Ignore)) return true;
                }
            }
            return false;
        }

        // A tree well inside the map, away from camps and bases, with open ground either side.
        static Collider Site(bool rock, out Vector3 a, out Vector3 b)
        {
            float half = GameState.MapSize * 0.5f;
            foreach (var blocker in blockers)
            {
                if ((blocker is BoxCollider) != rock) continue;
                Vector3 c = blocker.bounds.center;
                c.y = 0f;
                if (Mathf.Abs(c.x) > half - 10f || Mathf.Abs(c.z) > half - 10f) continue;
                if (Camp.All.Any(camp => Flat(camp.Center, c) < 25f)) continue;
                if (GameState.Bases.Any(s => s != null && Flat(s.transform.position, c) < 25f)) continue;
                float reach = Mathf.Max(blocker.bounds.extents.x, blocker.bounds.extents.z) + (rock ? 1.5f : 4f);
                if (!OnGround(c + Vector3.right * reach, out a) | !OnGround(c - Vector3.right * reach, out b)) continue;
                if (Physics.CheckSphere(a + Vector3.up * 0.5f, 1.2f, Mask) || Physics.CheckSphere(b + Vector3.up * 0.5f, 1.2f, Mask)) continue;
                return blocker;
            }
            a = b = Vector3.zero;
            return null;
        }

        static void StartWalk()
        {
            var tree = Site(false, out var from, out walkGoal);
            if (tree == null) { Debug.LogWarning("[Scenery] no tree to walk past"); return; }
            var home = GameState.Bases[0];
            for (int i = 0; i < 6; i++)
            {
                var unit = Unit.Create(home, home.Stats, from + new Vector3(i / 3 * 1.2f, 1f, (i % 3 - 1) * 1.2f), GameState.TeamColors[0], null);
                unit.AttackMove(walkGoal);
                testUnits.Add(unit);
            }
            var cam = Camera.main;
            var rts = cam.GetComponent<RtsCamera>();
            if (rts != null) rts.enabled = false;
            Vector3 middle = tree.bounds.center;
            middle.y = 0f;
            cam.transform.rotation = Quaternion.Euler(62f, 0f, 0f);
            cam.transform.position = middle + new Vector3(0f, 22f, -22f / Mathf.Tan(62f * Mathf.Deg2Rad));
            Debug.Log($"[Scenery] walking 6 units past a tree at {middle} (blocker radius {((CapsuleCollider)tree).radius:F2})");
        }

        static void StartChase()
        {
            var rock = Site(true, out var a, out var b);
            if (rock == null) { Debug.LogWarning("[Scenery] no rock to chase round"); return; }
            for (int team = 0; team < 2; team++)
            {
                var home = GameState.Bases[team];
                var unit = Unit.Create(home, home.Stats, (team == 0 ? a : b) + Vector3.up, GameState.TeamColors[team], null);
                testUnits.Add(unit);
            }
            Debug.Log($"[Scenery] two enemies {Flat(a, b):F1}m apart either side of a rock at {rock.bounds.center} (size {rock.bounds.size})");
        }

        static void Clear()
        {
            foreach (var u in testUnits) if (u != null) Object.Destroy(u.gameObject);
            testUnits.Clear();
        }

        // Counts units whose centre is inside a tree or rock collider.
        static void Watch(IEnumerable<Unit> units)
        {
            foreach (var u in units)
            {
                if (u == null || u.IsDead) continue;
                if (!Physics.CheckSphere(new Vector3(u.transform.position.x, u.FeetY + 0.5f, u.transform.position.z), 0.05f, Mask, QueryTriggerInteraction.Ignore)) continue;
                insideFrames++;
                insideUnits.Add(u);
            }
        }

        static bool OnGround(Vector3 point, out Vector3 onMesh)
        {
            point.y = 0f;
            bool ok = NavMesh.SamplePosition(point, out var hit, 0.3f, NavMesh.AllAreas) && hit.position.y < 0.5f;
            onMesh = hit.position;
            return ok;
        }

        static float Flat(Vector3 a, Vector3 b)
        {
            a.y = b.y = 0f;
            return Vector3.Distance(a, b);
        }

        static void Shoot()
        {
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
            PlayShot.CaptureTo(Camera.main, Path.Combine(dir, "screenshot-scenery.png"));
        }
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace SwarmPrototype
{
    // Logs what the NavMesh and the units' pathing are actually doing, so stuck
    // units can be diagnosed from the editor log instead of guessed at.
    public class Diagnostics : MonoBehaviour
    {
        const float ReportInterval = 5f;

        static readonly Dictionary<string, int> pathResults = new Dictionary<string, int>();
        float nextReport;

        static int partialLogged;

        // Details of the first few unreachable goals, to see what units are trying to reach.
        public static void Partial(Unit unit, Vector3 goal, Vector3 sampled, Vector3 end, float shortBy)
        {
            if (partialLogged++ >= 40) return;
            Debug.Log($"[Partial] {unit.name} team={unit.Team} at={unit.transform.position} state=[{unit.DebugState}] goal={goal} sampled={sampled} end={end} shortBy={shortBy:0.0}");
        }

        public static void Count(string result)
        {
            pathResults.TryGetValue(result, out int n);
            pathResults[result] = n + 1;
        }

        public static void Create(GameConfig config, Vector3[] bases, Transform parent)
        {
            var go = new GameObject("Diagnostics");
            go.transform.SetParent(parent, false);
            go.AddComponent<Diagnostics>();
            pathResults.Clear();

            var tri = NavMesh.CalculateTriangulation();
            Debug.Log($"[NavMesh] vertices={tri.vertices.Length} triangles={tri.indices.Length / 3}");

            float half = config.plateauSize * 0.5f;
            Vector3 up = Vector3.up * config.plateauHeight;
            Vector3 b0 = bases[0], b1 = bases[1];
            // South ramp bottom of base 0 -> top of base 0's plateau.
            TestPath("ramp bottom -> plateau top", b0 + new Vector3(0f, 0f, -(half + config.rampLength + 2f)), b0 + up + new Vector3(0f, 0f, -half + 3f));
            // Base 0 plateau -> base 1 plateau.
            TestPath("base 0 top -> base 1 top", b0 + up + new Vector3(4f, 0f, 0f), b1 + up + new Vector3(-4f, 0f, 0f));
            // Road between base 0 and 1 -> base 1's near corner tower approach point.
            Vector3 road = (b0 + b1) * 0.5f;
            Vector3 towerFoot = b1 + up + new Vector3(-(half - 2.2f) + 1.5f, 0f, half - 2.2f);
            TestPath("road -> corner tower", road, towerFoot);
            // Ground outside base 1's corner -> that same tower approach point.
            Vector3 outsideCorner = b1 + new Vector3(-half - 1.5f, 0f, half + 1.5f);
            TestPath("outside corner -> corner tower", outsideCorner, towerFoot);
        }

        static void TestPath(string label, Vector3 from, Vector3 to)
        {
            bool fromOk = NavMesh.SamplePosition(from, out var f, 3f, NavMesh.AllAreas);
            bool toOk = NavMesh.SamplePosition(to, out var t, 5f, NavMesh.AllAreas);
            if (!fromOk || !toOk)
            {
                Debug.Log($"[NavMesh] {label}: sample from={fromOk} to={toOk} (from {from}, to {to})");
                return;
            }
            var path = new NavMeshPath();
            bool ok = NavMesh.CalculatePath(f.position, t.position, NavMesh.AllAreas, path);
            float shortBy = path.corners.Length > 0 ? Vector3.Distance(path.corners[path.corners.Length - 1], t.position) : -1f;
            Debug.Log($"[NavMesh] {label}: calc={ok} status={path.status} corners={path.corners.Length} shortBy={shortBy:0.0} from={f.position} to={t.position}");
        }

        void Update()
        {
            if (Time.time < nextReport) return;
            nextReport = Time.time + ReportInterval;
            // Quiet unless a path failed somewhere (or F1 debug is on).
            bool trouble = false;
            foreach (var kv in pathResults) if (kv.Key != "ok") trouble = true;
            if (pathResults.Count == 0 || (!trouble && !Unit.DebugDraw)) { pathResults.Clear(); return; }
            var parts = new List<string>();
            foreach (var kv in pathResults) parts.Add($"{kv.Key}={kv.Value}");
            Debug.Log("[Paths] " + string.Join("  ", parts));
            pathResults.Clear();
        }
    }
}

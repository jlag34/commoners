using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

// Dev-only load test: fills the middle of the map with geared units (a mixed spread of armor
// tiers, AI off, idling) at a few crowd sizes and logs what a frame of them costs from the
// default RTS camera: frame time with the view rendered each frame, draw calls (shadows
// included), triangles, skinned renderers. Writes screenshot-stress-<n>.png next to Prototype.
//   Unity -batchmode -projectPath <proj> -executeMethod SwarmPrototype.StressShot.Run -logFile <log>
namespace SwarmPrototype
{
    public static class StressShot
    {
        const string Flag = "SwarmPrototype.StressShot";
        static readonly int[] Crowds = { 100, 300, 650 };
        // Upgrade levels for armor tiers 0-3 and how many units in ten wear each.
        static readonly int[] Levels = { 0, 1, 6, 11 };
        static readonly int[] TierShare = { 4, 3, 2, 1 };
        const int Settle = 20, Measure = 60;

        static int frame, crowd, phase;
        static Transform holder;
        static RenderTexture target;
        static Texture2D probe;
        static readonly List<float> times = new List<float>();
        static ProfilerRecorder drawCalls, triangles, setPass;

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
            frame++;
            if (frame < 5) return;
            var cam = Camera.main;
            if (target == null) Begin(cam);
            if (phase == 0)
            {
                Build(Crowds[crowd]);
                phase = 1;
                frame = 5;
                times.Clear();
                return;
            }
            // Render the game view every frame, waiting for the GPU, so animation culling,
            // skinning and drawing all count as they would on screen.
            var watch = Stopwatch.StartNew();
            cam.targetTexture = target;
            cam.Render();
            RenderTexture.active = target;
            probe.ReadPixels(new Rect(0, 0, 1, 1), 0, 0, false);
            RenderTexture.active = null;
            cam.targetTexture = null;
            float render = (float)watch.Elapsed.TotalMilliseconds;
            if (frame < 5 + Settle) return;
            times.Add(Time.unscaledDeltaTime * 1000f);
            if (times.Count == 1) Report(cam, render);
            if (times.Count < Measure) return;
            times.Sort();
            Debug.Log($"[Stress] {Crowds[crowd]} units: frame median {times[times.Count / 2]:F1} ms, " +
                      $"90th {times[times.Count * 9 / 10]:F1} ms");
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
            PlayShot.CaptureTo(cam, Path.Combine(dir, $"screenshot-stress-{Crowds[crowd]}.png"));
            crowd++;
            phase = 0;
            if (crowd < Crowds.Length) return;
            SessionState.EraseBool(Flag);
            EditorApplication.update -= Tick;
            if (Application.isBatchMode) EditorApplication.Exit(0);
            else EditorApplication.isPlaying = false;
        }

        static void Begin(Camera cam)
        {
            target = new RenderTexture(1920, 1080, 24);
            probe = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            triangles = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            var rts = cam.GetComponent<RtsCamera>();
            if (rts != null) rts.enabled = false;
            // The RTS camera's default view (height 36, pitch 62) over the map's middle.
            cam.transform.rotation = Quaternion.Euler(62f, 0f, 0f);
            cam.transform.position = new Vector3(0f, 36f, -36f / Mathf.Tan(62f * Mathf.Deg2Rad));
            Debug.Log($"[Stress] camera fov {cam.fieldOfView} on {SystemInfo.graphicsDeviceName}, " +
                      $"skinning {(SystemInfo.supportsComputeShaders ? "compute-capable" : "cpu")}, " +
                      $"shadows {QualitySettings.shadows} distance {QualitySettings.shadowDistance}");
        }

        static void Build(int count)
        {
            if (holder != null) Object.Destroy(holder.gameObject);
            holder = new GameObject("Stress Crowd").transform;
            int teams = Mathf.Min(GameState.TeamCount, Levels.Length);
            int side = Mathf.CeilToInt(Mathf.Sqrt(count));
            int[] tiers = new int[count];
            for (int i = 0; i < count; i++)
            {
                int slot = i % 10, tier = 0;
                for (int sum = TierShare[0]; slot >= sum; sum += TierShare[++tier]) { }
                tiers[i] = tier;
            }
            for (int tier = 0; tier < Levels.Length; tier++)
            {
                // One home per tier, its upgrades set to that tier's levels.
                var home = GameState.Bases[tier % teams];
                home.DebugSetLevel("strength", Levels[tier]);
                home.DebugSetLevel("armor", Levels[tier]);
                home.DebugSetLevel("health", Levels[tier]);
                for (int i = 0; i < count; i++)
                {
                    if (tiers[i] != tier) continue;
                    var spot = new Vector3((i % side - side * 0.5f) * 1.25f, 0f, (i / side - side * 0.5f) * 1.25f);
                    var unit = Unit.Create(home, home.Stats, spot, GameState.TeamColors[tier % teams], holder);
                    unit.transform.rotation = Quaternion.Euler(0f, 180f + (i * 37) % 60 - 30, 0f);
                    foreach (var pop in unit.GetComponentsInChildren<PopIn>()) pop.Finish();
                    unit.enabled = false;
                }
            }
        }

        static void Report(Camera cam, float renderMs)
        {
            var renderers = holder.GetComponentsInChildren<Renderer>().Where(r => r.enabled && r.gameObject.activeInHierarchy).ToList();
            var skinned = renderers.OfType<SkinnedMeshRenderer>().ToList();
            long tris = 0, verts = 0;
            foreach (var r in skinned)
            {
                if (r.sharedMesh == null) continue;
                tris += r.sharedMesh.triangles.Length / 3;
                verts += r.sharedMesh.vertexCount;
            }
            int units = Crowds[crowd];
            Debug.Log($"[Stress] {units} units: {renderers.Count} renderers ({skinned.Count} skinned, " +
                      $"{renderers.Count / (float)units:F1} per unit), unit triangles {tris / 1000}k, skinned vertices {verts / 1000}k; " +
                      $"frame draw calls {drawCalls.LastValue}, set-pass {setPass.LastValue}, triangles drawn {triangles.LastValue / 1000}k; " +
                      $"one view render {renderMs:F1} ms");
        }
    }
}

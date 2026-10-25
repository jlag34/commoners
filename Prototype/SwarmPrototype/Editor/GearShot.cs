using System.IO;
using UnityEditor;
using UnityEngine;

// Dev-only: renders the four gear tiers side by side, close up and from the game camera.
//   Unity -batchmode -projectPath <proj> -executeMethod SwarmPrototype.GearShot.Run -logFile <log>
namespace SwarmPrototype
{
    public static class GearShot
    {
        const string Flag = "SwarmPrototype.GearShot";
        static readonly int[] Levels = { 0, 1, 6, 11 };
        static int frame;
        static bool built;

        public static void Run()
        {
            SessionState.SetBool(Flag, true);
            EditorApplication.isPlaying = true;
        }

        // Dropping a file named `gearshot.trigger` next to the Assets folder runs the
        // showcase on the next domain reload (e.g. when play stops), from an open editor.
        static string TriggerPath => Path.Combine(Application.dataPath, "../gearshot.trigger");

        [InitializeOnLoadMethod]
        static void Hook()
        {
            if (!SessionState.GetBool(Flag, false) && File.Exists(TriggerPath))
            {
                File.Delete(TriggerPath);
                Debug.Log("GearShot: trigger file found, starting showcase");
                EditorApplication.delayCall += Run;
            }
            if (!SessionState.GetBool(Flag, false)) return;
            EditorApplication.update += Tick;
        }

        static void Tick()
        {
            if (!EditorApplication.isPlaying || GameState.Bases == null || GameState.Bases[0] == null) return;
            frame++;
            if (frame < 5) return;
            if (!built) { Build(); return; }
            if (frame == 6) Debug.Log("GearShot: units=" + Combatant.All.Count + " elites=" + EliteSpawner.Alive.Count);
            // A few frames for the animation graph to pose the models, then shoot.
            if (frame < 12) return;
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
            var cam = Camera.main;
            var rts = cam.GetComponent<RtsCamera>();
            if (rts != null) rts.enabled = false;

            Vector3 centre = new Vector3(-30f, 0f, 0f);
            cam.transform.position = centre + new Vector3(0f, 3.2f, -6.5f);
            cam.transform.LookAt(centre + Vector3.up * 1.1f);
            cam.fieldOfView = 32f;
            PlayShot.CaptureTo(cam, Path.Combine(dir, "screenshot-gear-closeup.png"));

            // Side view of the tier-0 unit for the pickaxe.
            Vector3 first = centre + new Vector3(-1.5f * 1.6f, 0f, 0f);
            cam.transform.position = first + new Vector3(-3.2f, 1.6f, -1.2f);
            cam.transform.LookAt(first + Vector3.up * 1.0f);
            cam.fieldOfView = 30f;
            PlayShot.CaptureTo(cam, Path.Combine(dir, "screenshot-gear-side.png"));
            foreach (var u in Combatant.All)
                if (u is Unit unit && unit.Team == 0) { Debug.Log("[GearShot] tier0 unit at " + unit.transform.position + " bounds: " + string.Join(" | ", System.Array.ConvertAll(unit.GetComponentsInChildren<Renderer>(), r => r.name + "=" + r.bounds.center + "/" + r.bounds.size))); break; }

            cam.fieldOfView = 40f;
            float pitch = 62f, height = 13f;
            cam.transform.rotation = Quaternion.Euler(pitch, 0f, 0f);
            cam.transform.position = centre + new Vector3(0f, height, -height / Mathf.Tan(pitch * Mathf.Deg2Rad));
            PlayShot.CaptureTo(cam, Path.Combine(dir, "screenshot-gear-topdown.png"));

            // The elite lineup, further along the x axis.
            Vector3 eliteCentre = new Vector3(0f, 0f, 0f);
            cam.fieldOfView = 42f;
            cam.transform.position = eliteCentre + new Vector3(0f, 6f, -20f);
            cam.transform.LookAt(eliteCentre + Vector3.up * 2.2f);
            PlayShot.CaptureTo(cam, Path.Combine(dir, "screenshot-elites.png"));

            SessionState.EraseBool(Flag);
            EditorApplication.update -= Tick;
            Time.timeScale = 1f;
            if (Application.isBatchMode) EditorApplication.Exit(0);
            else EditorApplication.isPlaying = false;
        }

        static void Build()
        {
            built = true;
            var holder = new GameObject("Gear Showcase").transform;
            for (int t = 0; t < GameState.TeamCount && t < Levels.Length; t++)
            {
                var home = GameState.Bases[t];
                if (home == null) continue;
                home.DebugSetLevel("strength", Levels[t]);
                home.DebugSetLevel("armor", Levels[t]);
                home.DebugSetLevel("health", Levels[t]);
                home.DebugSetLevel("moveSpeed", Levels[t]);
                Vector3 spot = new Vector3(-30f + (t - 1.5f) * 1.6f, 0f, 0f);
                var unit = Unit.Create(home, home.Stats, spot, GameState.TeamColors[t], holder);
                unit.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
                foreach (var pop in unit.GetComponentsInChildren<PopIn>()) pop.Finish();
            }
            // One of each elite in a row at the map's middle.
            var elites = GameState.Config.elites;
            for (int i = 0; i < elites.Length; i++)
            {
                var e = elites[i];
                if (!ColorUtility.TryParseHtmlString(e.tint, out Color tint)) tint = GameState.NeutralColor;
                Vector3 spot = new Vector3((i - (elites.Length - 1) * 0.5f) * 3.4f, 0f, 0f);
                var elite = Unit.CreateElite(e, e.unit.Clone(), spot, e.bounty, tint, holder);
                elite.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
                foreach (var pop in elite.GetComponentsInChildren<PopIn>()) pop.Finish();
            }
            // Freeze the match so the four don't start fighting each other.
            Time.timeScale = 0f;
            Debug.Log("GearShot: built showcase");
        }
    }
}

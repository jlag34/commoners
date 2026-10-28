using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Dev-only: proves units still animate. Spawns the original Warrior (a camp monster, no
// gear) next to player units in each UnitGear look, two of them switched live with SetLook,
// plus an armored commoner. Drives each through idle, walk, run and attack with AI off and
// logs how far the right fist and the main body mesh move in each phase (zero = the model
// is frozen). Renders a sheet of frames to screenshot-anim.png: a row per phase.
//   Unity -batchmode -projectPath <proj> -executeMethod SwarmPrototype.AnimCheck.Run -logFile <log>
namespace SwarmPrototype
{
    public static class AnimCheck
    {
        const string Flag = "SwarmPrototype.AnimCheck";
        const int PhaseFrames = 60, Shots = 4, ShotW = 960, ShotH = 540;
        static readonly (string name, float speed, bool attack)[] Phases =
            { ("idle", 0f, false), ("walk", 1.6f, false), ("run", 4.5f, false), ("attack", 0f, true) };

        class Subject
        {
            public string name;
            public Unit unit;
            public Rigidbody body;
            public Vector3 anchor;
            public Transform model, fist;
            public SkinnedMeshRenderer skin;
            public Vector3 fistMin, fistMax;
            public float meshMove;
            public Vector3[] restVerts;
        }

        static readonly List<Subject> subjects = new List<Subject>();
        static int frame, phase, phaseFrame;
        static Texture2D sheet;
        static Camera cam;
        static readonly List<string> report = new List<string>();

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
            if (subjects.Count == 0) { Build(); return; }
            var (name, speed, attack) = Phases[phase];
            foreach (var s in subjects) Drive(s, speed, attack && phaseFrame % 36 == 0);
            // Render every frame so animation culling sees the units as on screen.
            cam.Render();
            if (phaseFrame >= 10 && phaseFrame % 5 == 0) foreach (var s in subjects) Sample(s);
            int shotEvery = (PhaseFrames - 10) / Shots;
            if (phaseFrame >= 10 && (phaseFrame - 10) % shotEvery == 0 && (phaseFrame - 10) / shotEvery < Shots)
                Shoot((phaseFrame - 10) / shotEvery, phase);
            phaseFrame++;
            if (phaseFrame < PhaseFrames) return;
            foreach (var s in subjects)
            {
                if (phase == 0)
                    Debug.Log($"[AnimCheck] {s.name} draws: " + string.Join(", ", s.unit.GetComponentsInChildren<Renderer>()
                        .Where(r => r.enabled && r.gameObject.activeInHierarchy).Select(r => r.name)));
                Vector3 range = s.fistMax - s.fistMin;
                report.Add($"[AnimCheck] {s.name,-24} {name,-6} fist range {range.magnitude:F3} m, {s.skin?.name} max move {s.meshMove:F3} m");
                s.fistMin = Vector3.one * float.MaxValue;
                s.fistMax = Vector3.one * float.MinValue;
                s.meshMove = 0f;
                s.restVerts = null;
            }
            phase++;
            phaseFrame = 0;
            if (phase < Phases.Length) return;
            foreach (var line in report) Debug.Log(line);
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
            File.WriteAllBytes(Path.Combine(dir, "screenshot-anim.png"), sheet.EncodeToPNG());
            Debug.Log("[AnimCheck] wrote " + Path.Combine(dir, "screenshot-anim.png"));
            SessionState.EraseBool(Flag);
            EditorApplication.update -= Tick;
            Time.captureDeltaTime = 0f;
            if (Application.isBatchMode) EditorApplication.Exit(0);
            else EditorApplication.isPlaying = false;
        }

        static void Build()
        {
            // Fixed 30 fps steps so every run animates the same amount.
            Time.captureDeltaTime = 1f / 30f;
            var holder = new GameObject("Anim Check").transform;
            var home = GameState.Bases[0];
            home.DebugSetLevel("strength", 0);
            home.DebugSetLevel("armor", 0);
            home.DebugSetLevel("health", 0);
            Vector3 centre = new Vector3(-30f, 0f, 0f);
            Vector3 Spot(int i) => centre + Vector3.right * (i - 2) * 1.6f;
            var look = UnitGear.Look;
            Add("camp monster", Unit.CreateMonster(home.Stats, Spot(0), 1f, 0, GameState.NeutralColor, null, "easy", holder));
            // Built as the commoner, then switched back to the plain Warrior.
            UnitGear.Look = UnitGear.UnitLook.Commoner;
            var warrior = Unit.Create(home, home.Stats, Spot(1), GameState.TeamColors[0], holder);
            warrior.GetComponentInChildren<UnitGear>().SetLook(UnitGear.UnitLook.Warrior);
            Add("Warrior (live switch)", warrior);
            UnitGear.Look = UnitGear.UnitLook.WarriorWithWeapon;
            Add("WarriorWithWeapon", Unit.Create(home, home.Stats, Spot(2), GameState.TeamColors[0], holder));
            // Built as the Warrior, then switched to the commoner.
            UnitGear.Look = UnitGear.UnitLook.Warrior;
            var commoner = Unit.Create(home, home.Stats, Spot(3), GameState.TeamColors[0], holder);
            commoner.GetComponentInChildren<UnitGear>().SetLook(UnitGear.UnitLook.Commoner);
            Add("Commoner (live switch)", commoner);
            UnitGear.Look = UnitGear.UnitLook.Commoner;
            home.DebugSetLevel("strength", 6);
            home.DebugSetLevel("armor", 6);
            Add("Commoner, iron tier", Unit.Create(home, home.Stats, Spot(4), GameState.TeamColors[0], holder));
            UnitGear.Look = look;

            cam = Camera.main;
            var rts = cam.GetComponent<RtsCamera>();
            if (rts != null) rts.enabled = false;
            cam.fieldOfView = 30f;
            cam.transform.position = centre + new Vector3(0f, 2.2f, -12f);
            cam.transform.LookAt(centre + Vector3.up * 1.05f);
            cam.targetTexture = new RenderTexture(ShotW, ShotH, 24);
            sheet = new Texture2D(ShotW * Shots, ShotH * Phases.Length, TextureFormat.RGB24, false);
            Debug.Log("[AnimCheck] clips in Warrior: " + string.Join(", ", Visuals.LoadClips("Warrior").Select(c => c.name)));
        }

        static void Add(string name, Unit unit)
        {
            foreach (var pop in unit.GetComponentsInChildren<PopIn>()) pop.Finish();
            unit.enabled = false;
            var s = new Subject { name = name, unit = unit, body = unit.GetComponent<Rigidbody>(), anchor = unit.transform.position };
            s.body.useGravity = false;
            s.body.interpolation = RigidbodyInterpolation.None;
            // Side on to the camera, facing +x, so legs and arms swing in view.
            unit.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            s.model = unit.GetComponentInChildren<Animator>()?.transform;
            s.fist = s.model?.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "Fist.R");
            s.fistMin = Vector3.one * float.MaxValue;
            s.fistMax = Vector3.one * float.MinValue;
            subjects.Add(s);
            var animator = s.model != null ? s.model.GetComponent<Animator>() : null;
            Debug.Log($"[AnimCheck] {name}: animator {(animator != null ? animator.cullingMode.ToString() : "none")}, " +
                      $"UnitAnimator {(s.model != null && s.model.GetComponent<UnitAnimator>() != null)}");
        }

        static void Drive(Subject s, float speed, bool swing)
        {
            // A treadmill: the unit keeps its running velocity but stays on its spot.
            s.body.position = s.anchor;
            s.unit.transform.position = s.anchor;
#if UNITY_6000_0_OR_NEWER
            s.body.linearVelocity = s.unit.transform.forward * speed;
#else
            s.body.velocity = s.unit.transform.forward * speed;
#endif
            if (swing) s.model?.GetComponent<UnitAnimator>()?.OnAttack();
        }

        static void Sample(Subject s)
        {
            if (s.fist != null)
            {
                Vector3 p = s.unit.transform.InverseTransformPoint(s.fist.position);
                s.fistMin = Vector3.Min(s.fistMin, p);
                s.fistMax = Vector3.Max(s.fistMax, p);
            }
            // The biggest body mesh that is drawn, picked once a look switch's leftovers are gone.
            if (s.skin == null)
                s.skin = s.unit.GetComponentsInChildren<SkinnedMeshRenderer>()
                    .Where(r => r.enabled && r.gameObject.activeInHierarchy && r.sharedMesh != null)
                    .OrderByDescending(r => r.sharedMesh.vertexCount).FirstOrDefault();
            if (s.skin == null) return;
            var mesh = new Mesh();
            s.skin.BakeMesh(mesh, true);
            var verts = mesh.vertices;
            Object.Destroy(mesh);
            if (s.restVerts == null) { s.restVerts = verts; return; }
            float move = 0f;
            for (int i = 0; i < verts.Length; i++) move = Mathf.Max(move, (verts[i] - s.restVerts[i]).magnitude);
            s.meshMove = Mathf.Max(s.meshMove, move);
        }

        static void Shoot(int column, int row)
        {
            RenderTexture.active = cam.targetTexture;
            sheet.ReadPixels(new Rect(0, 0, ShotW, ShotH), column * ShotW, (Phases.Length - 1 - row) * ShotH);
            sheet.Apply();
            RenderTexture.active = null;
        }
    }
}

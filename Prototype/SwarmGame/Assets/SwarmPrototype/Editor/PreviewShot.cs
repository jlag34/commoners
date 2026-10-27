using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Dev-only: boots the menu, enters the hub and saves the character preview in several
// weapon/armor combinations, front and back, then a few moments of the bare commoner's
// idle; composited over the hub's backdrop and cropped to the part the hub shows.
//   Unity -batchmode -projectPath <proj> -executeMethod SwarmPrototype.PreviewShot.Run -logFile <log>
// Writes screenshot-preview-w<weapon>a<armor>-<front|back>.png and screenshot-preview-idle<N>.png
// next to the project folder.
namespace SwarmPrototype
{
    public static class PreviewShot
    {
        const string Flag = "SwarmPrototype.PreviewShot";
        static readonly Color Backdrop = new Color(0.1f, 0.19f, 0.2f);
        // (weapon tier, armor tier)
        static readonly (int weapon, int armor)[] Combos = { (0, 0), (1, 0), (0, 1), (1, 1), (2, 2), (3, 3) };
        const int IdleShots = 4;
        static int tries, phase, combo;
        static double next;

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
            if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup < next) return;
            next = EditorApplication.timeSinceStartup + 0.6;
            var preview = Object.FindObjectOfType<HomeUnitPreview>();
            if (preview == null)
            {
                // Batch mode boots straight into a match; bring the menu up instead.
                if (Object.FindObjectOfType<MainMenu>() == null) Bootstrap.ShowMenu();
                var start = Object.FindObjectsOfType<Button>().FirstOrDefault(b => b.name == "Start");
                if (start != null) start.onClick.Invoke();
                if (++tries > 40) Finish("PreviewShot: no preview after 40 tries");
                return;
            }
            if (combo < Combos.Length)
            {
                // Each combination: dress, wait a beat for the entrance and the pose blend, then front and back.
                var (weapon, armor) = Combos[combo];
                string label = $"w{weapon}a{armor}";
                switch (phase++ % 3)
                {
                    case 0: preview.SetGear(weapon, armor); next += 1.0; break;
                    case 1: Save(preview, label + "-front"); preview.Rotate(180); break;
                    case 2: Save(preview, label + "-back"); preview.Rotate(-180); combo++; phase = 0; break;
                }
                return;
            }
            // Then the commoner's idle a second and a half apart.
            if (phase == 0) { preview.SetGear(0, 0); next += 1.0; }
            else Save(preview, "idle" + (phase - 1));
            next += 0.9;
            if (++phase > IdleShots) Finish("PreviewShot: done");
        }

        static void Save(HomeUnitPreview preview, string view)
        {
            var image = preview.GetComponent<RawImage>();
            var rt = image.texture as RenderTexture;
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            // Only the part of the square portrait the hub shows.
            var uv = image.uvRect;
            var shown = new Rect(uv.x * rt.width, uv.y * rt.height, uv.width * rt.width, uv.height * rt.height);
            var tex = new Texture2D((int)shown.width, (int)shown.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(shown, 0, 0);
            RenderTexture.active = previous;
            var px = tex.GetPixels();
            for (int i = 0; i < px.Length; i++) px[i] = Color.Lerp(Backdrop, px[i], px[i].a);
            tex.SetPixels(px);
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
            File.WriteAllBytes(Path.Combine(dir, $"screenshot-preview-{view}.png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        static void Finish(string message)
        {
            Debug.Log(message);
            SessionState.EraseBool(Flag);
            EditorApplication.update -= Tick;
            if (Application.isBatchMode) EditorApplication.Exit(0);
            else EditorApplication.isPlaying = false;
        }
    }
}

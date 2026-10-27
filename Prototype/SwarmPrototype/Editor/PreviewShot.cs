using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Dev-only: boots the menu, enters the hub and saves the character preview at every gear
// tier, front and back, composited over the hub's backdrop. For tuning gear materials.
//   Unity -batchmode -projectPath <proj> -executeMethod SwarmPrototype.PreviewShot.Run -logFile <log>
// Writes screenshot-preview-tier<N>-<front|back>.png next to the project folder.
namespace SwarmPrototype
{
    public static class PreviewShot
    {
        const string Flag = "SwarmPrototype.PreviewShot";
        static readonly Color Backdrop = new Color(0.1f, 0.19f, 0.2f);
        static int tries, phase, tier;
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
            // Each tier: dress, wait a beat for the entrance and the idle pose, then front and back.
            switch (phase++ % 3)
            {
                case 0: preview.SetTier(tier); next += 1.0; break;
                case 1: Save(preview, "front"); preview.Rotate(180); break;
                case 2: Save(preview, "back"); preview.Rotate(-180); tier++; break;
            }
            if (tier > 3) Finish("PreviewShot: done");
        }

        static void Save(HomeUnitPreview preview, string view)
        {
            var rt = preview.GetComponent<RawImage>().texture as RenderTexture;
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            RenderTexture.active = previous;
            var px = tex.GetPixels();
            for (int i = 0; i < px.Length; i++) px[i] = Color.Lerp(Backdrop, px[i], px[i].a);
            tex.SetPixels(px);
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
            File.WriteAllBytes(Path.Combine(dir, $"screenshot-preview-tier{tier}-{view}.png"), tex.EncodeToPNG());
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

using System.IO;
using UnityEditor;
using UnityEngine;

// Dev-only: enters play mode in batch mode and saves camera renders to disk.
//   Unity -batchmode -projectPath <proj> -executeMethod SwarmPrototype.PlayShot.Run -logFile <log>
namespace SwarmPrototype
{
    public static class PlayShot
    {
        const string Flag = "SwarmPrototype.PlayShot";
        static readonly float[] Times = { 25f, 130f };
        static int shot;

        public static void Run()
        {
            SessionState.SetBool(Flag, true);
            EditorApplication.isPlaying = true;
        }

        [InitializeOnLoadMethod]
        static void Hook()
        {
            if (!SessionState.GetBool(Flag, false)) return;
            EditorApplication.update += Tick;
        }

        static void Tick()
        {
            if (!EditorApplication.isPlaying) return;
            Time.timeScale = 4f;
            if (shot >= Times.Length)
            {
                SessionState.EraseBool(Flag);
                EditorApplication.Exit(0);
                return;
            }
            if (Time.time < Times[shot]) return;
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
            Capture(Path.Combine(dir, $"screenshot-rings-{Times[shot]:0}s.png"));
            shot++;
        }

        static void Capture(string path) => CaptureTo(Camera.main, path);

        public static void CaptureTo(Camera cam, string path)
        {
            if (cam == null) { Debug.LogError("PlayShot: no camera"); return; }
            var rt = new RenderTexture(1600, 900, 24);
            var previous = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = previous;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.Destroy(tex);
            Object.Destroy(rt);
            Debug.Log("PlayShot: wrote " + path + " units=" + Combatant.All.Count);
        }
    }
}

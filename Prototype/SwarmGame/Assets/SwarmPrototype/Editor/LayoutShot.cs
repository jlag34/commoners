using System.IO;
using UnityEditor;
using UnityEngine;

// Dev-only: bakes the icon sheet and renders a top-down overview of every map
// size, so layouts and icons can be checked without the editor open.
//   Unity -batchmode -projectPath <proj> -executeMethod SwarmPrototype.LayoutShot.Run -logFile <log>
namespace SwarmPrototype
{
    public static class LayoutShot
    {
        const string Flag = "SwarmPrototype.LayoutShot";
        static int frame;
        static int sizeIndex;
        static bool built;

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
            frame++;
            if (frame < 5) return;
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
            if (frame == 5)
            {
                IconSheet(Path.Combine(dir, "screenshot-icons.png"));
                return;
            }
            if (!built)
            {
                Bootstrap.Play(MapLayout.Sizes[sizeIndex]);
                built = true;
                frame = 10;
                return;
            }
            if (frame < 16) return;
            var cam = Camera.main;
            var rts = cam.GetComponent<RtsCamera>();
            if (rts != null) rts.enabled = false;
            float mapSize = MapLayout.Build(MapLayout.Sizes[sizeIndex], GameConfig.Load()).mapSize;
            cam.fieldOfView = 60f;
            cam.farClipPlane = 1000f;
            RenderSettings.fog = false;
            cam.transform.position = new Vector3(0f, mapSize * 0.95f, -mapSize * 0.05f);
            cam.transform.rotation = Quaternion.Euler(88f, 0f, 0f);
            PlayShot.CaptureTo(cam, Path.Combine(dir, $"screenshot-layout-{MapLayout.Sizes[sizeIndex]}p.png"));
            sizeIndex++;
            if (sizeIndex >= MapLayout.Sizes.Length)
            {
                SessionState.EraseBool(Flag);
                EditorApplication.Exit(0);
                return;
            }
            built = false;
        }

        static void IconSheet(string path)
        {
            string[] names = { "coin", "skull", "lesser", "greater", "elite", "strength", "attackSpeed", "health", "armor", "moveSpeed", "lifesteal", "evasion", "regen", "towerRegen", "towerDamage", "towerRange", "crit", "splash", "spawnRate", "bounty", "reincarnate" };
            const int cell = 72, cols = 7;
            int rows = Mathf.CeilToInt(names.Length / (float)cols);
            var sheet = new Texture2D(cols * cell, rows * cell, TextureFormat.RGBA32, false);
            var bg = new Color(0.1f, 0.09f, 0.08f, 1f);
            var fill = new Color[sheet.width * sheet.height];
            for (int i = 0; i < fill.Length; i++) fill[i] = bg;
            sheet.SetPixels(fill);
            for (int n = 0; n < names.Length; n++)
            {
                var icon = Icons.Get(names[n]);
                int ox = (n % cols) * cell + 4, oy = (rows - 1 - n / cols) * cell + 4;
                for (int y = 0; y < icon.height; y++)
                    for (int x = 0; x < icon.width; x++)
                    {
                        Color c = icon.GetPixel(x, y);
                        sheet.SetPixel(ox + x, oy + y, Color.Lerp(bg, new Color(c.r, c.g, c.b, 1f), c.a));
                    }
            }
            sheet.Apply();
            File.WriteAllBytes(path, sheet.EncodeToPNG());
            Debug.Log("LayoutShot: wrote " + path);
        }
    }
}

using UnityEngine;

namespace SwarmPrototype
{
    // Start screen: pick how many players, see the map it makes, press Play.
    public class MainMenu : MonoBehaviour
    {
        const string PrefKey = "SwarmPrototype.Players";

        int players;
        MapLayout layout;
        GameConfig config;

        void Awake()
        {
            config = GameConfig.Load();
            players = PlayerPrefs.GetInt(PrefKey, 4);
            if (System.Array.IndexOf(MapLayout.Sizes, players) < 0) players = 4;
            layout = MapLayout.Build(players, config);
        }

        void Update()
        {
            if (InputShim.ConfirmPressed) Play();
        }

        void Play()
        {
            PlayerPrefs.SetInt(PrefKey, players);
            Bootstrap.Play(players);
        }

        void OnGUI()
        {
            float w = 520f, h = 420f;
            var panel = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            UiSkin.Panel(panel);

            var title = UiSkin.Title;
            var old = title.alignment;
            title.alignment = TextAnchor.MiddleCenter;
            GUI.Label(new Rect(panel.x, panel.y + 10f, panel.width, 30f), "SWARM", title);
            title.alignment = old;

            GUI.Label(new Rect(panel.x + 20f, panel.y + 48f, 200f, 20f), "Players", UiSkin.Header);
            float bx = panel.x + 20f;
            foreach (int n in MapLayout.Sizes)
            {
                if (UiSkin.Button(new Rect(bx, panel.y + 72f, 44f, 30f), n.ToString(), n == players))
                {
                    players = n;
                    layout = MapLayout.Build(players, config);
                }
                bx += 50f;
            }

            var preview = new Rect(panel.x + 20f, panel.y + 112f, 280f, 280f);
            layout.DrawPreview(preview);

            float ix = preview.xMax + 16f, iy = preview.y;
            GUI.Label(new Rect(ix, iy, 200f, 20f), $"{players} players, free-for-all", UiSkin.Bold);
            GUI.Label(new Rect(ix, iy + 22f, 200f, 20f), $"{layout.rows} x {layout.cols} bases", UiSkin.Cell);
            GUI.Label(new Rect(ix, iy + 44f, 200f, 20f), $"{layout.camps.Count} monster camps", UiSkin.Cell);
            GUI.Label(new Rect(ix, iy + 66f, 200f, 20f), $"map {layout.mapSize:0} m", UiSkin.Cell);
            Legend(ix, iy + 100f, new Color(0.55f, 0.9f, 0.5f), "easy camp");
            Legend(ix, iy + 120f, new Color(1f, 0.7f, 0.25f), "medium camp");
            Legend(ix, iy + 140f, new Color(0.95f, 0.35f, 0.3f), "hard camp");
            GUI.Label(new Rect(ix, iy + 170f, 200f, 40f), "You are blue, bottom-left.", UiSkin.Small);

            if (UiSkin.Button(new Rect(preview.xMax + 16f, panel.yMax - 50f, 180f, 36f), "Play  (Enter)", true)) Play();
        }

        static void Legend(float x, float y, Color color, string text)
        {
            UiSkin.Icon(new Rect(x, y + 4f, 12f, 12f), color);
            GUI.Label(new Rect(x + 18f, y, 180f, 20f), text, UiSkin.Small);
        }
    }
}

using UnityEngine;

namespace SwarmPrototype
{
    // Esc (or the HUD's Menu button) during a match: a centered panel with
    // Resume, Restart and Quit to home. Local matches pause while it is open.
    public class PauseMenu
    {
        const float Width = 260f;
        const float ButtonHeight = 44f;
        const float Pad = 16f;

        public bool Open { get; private set; }
        public Rect Panel { get; private set; }

        public bool Covers(Vector2 mouse) => Open;

        public void Show()
        {
            Open = true;
            Time.timeScale = 0f;
        }

        public void Hide()
        {
            Open = false;
            Time.timeScale = 1f;
        }

        // Leaving the match: time must run again before the home screen animates.
        public void Restart() { Hide(); Bootstrap.Restart(); }
        public void QuitToHome() { Hide(); Bootstrap.ShowMenu(); }

        public void Draw()
        {
            if (!Open) return;
            UiSkin.Fill(new Rect(0f, 0f, UiSkin.Width, UiSkin.Height), new Color(0f, 0f, 0f, 0.45f));

            float height = Pad + 28f + 10f + (ButtonHeight + 8f) * 3 + Pad - 8f;
            Panel = new Rect((UiSkin.Width - Width) * 0.5f, (UiSkin.Height - height) * 0.5f, Width, height);
            UiSkin.Panel(Panel);

            float y = Panel.y + Pad;
            var title = UiSkin.Big;
            var old = title.alignment;
            title.alignment = TextAnchor.MiddleCenter;
            GUI.Label(new Rect(Panel.x, y, Width, 28f), "Paused", title);
            title.alignment = old;
            y += 38f;

            var row = new Rect(Panel.x + Pad, y, Width - Pad * 2f, ButtonHeight);
            if (UiSkin.GoldButton(row, "Resume", null, "Esc")) Hide();
            row.y += ButtonHeight + 8f;
            if (UiSkin.Button(row, "Restart match  (R)")) Restart();
            row.y += ButtonHeight + 8f;
            if (UiSkin.Button(row, "Quit to home  (M)")) QuitToHome();
        }
    }
}

using UnityEngine;

namespace SwarmPrototype
{
    // Warcraft 3 style stats table in the top-right: one row per player with
    // their name in team color, total upgrade level, kills and deaths. Tab hides it.
    public class ScoreBoard
    {
        const float Width = 250f;
        const float RowHeight = 18f;
        const float Pad = 8f;

        public bool Open { get; set; } = true;
        public Rect Panel { get; private set; }

        public bool Covers(Vector2 mouse) => Open && Panel.Contains(new Vector2(mouse.x, Screen.height - mouse.y));

        public void Draw(Spawner[] bases)
        {
            if (!Open) return;
            int rows = bases.Length;
            float height = Pad + RowHeight * (rows + 1) + Pad * 0.5f;
            Panel = new Rect(Screen.width - Width - 12f, 12f, Width, height);
            UiSkin.Panel(Panel);

            // Columns: name takes what's left; the three numbers are fixed width.
            const float numberWidth = 48f;
            float x = Panel.x + Pad;
            float nameWidth = Panel.width - Pad * 2f - numberWidth * 3f;
            float y = Panel.y + Pad * 0.5f;

            GUI.Label(new Rect(x, y, nameWidth, RowHeight), "Player", UiSkin.Header);
            GUI.Label(new Rect(x + nameWidth, y, numberWidth, RowHeight), "Level", UiSkin.Header);
            GUI.Label(new Rect(x + nameWidth + numberWidth, y, numberWidth, RowHeight), "Kills", UiSkin.Header);
            GUI.Label(new Rect(x + nameWidth + numberWidth * 2f, y, numberWidth, RowHeight), "Deaths", UiSkin.Header);
            y += RowHeight;
            UiSkin.Rule(Panel.x + 6f, y - 1f, Panel.width - 12f);
            y += 2f;

            for (int team = 0; team < rows; team++, y += RowHeight)
            {
                Spawner b = bases[team];
                Color color = GameState.TeamColors[team];
                bool alive = b != null && !b.IsDead;

                // Dead players fade out, like a leaver in WC3.
                var name = UiSkin.Bold;
                var cell = UiSkin.Cell;
                Color nameColor = name.normal.textColor, cellColor = cell.normal.textColor;
                name.normal.textColor = alive ? color : Color.Lerp(color, Color.gray, 0.6f);
                if (!alive) cell.normal.textColor = Color.gray;

                UiSkin.Fill(new Rect(x, y + 4f, 10f, 10f), color);
                GUI.Label(new Rect(x + 16f, y, nameWidth - 16f, RowHeight), GameState.TeamNames[team], name);
                GUI.Label(new Rect(x + nameWidth, y, numberWidth, RowHeight), (b != null ? b.TotalLevels : 0).ToString(), cell);
                GUI.Label(new Rect(x + nameWidth + numberWidth, y, numberWidth, RowHeight), GameState.Kills[team].ToString(), cell);
                GUI.Label(new Rect(x + nameWidth + numberWidth * 2f, y, numberWidth, RowHeight), GameState.Deaths[team].ToString(), cell);

                name.normal.textColor = nameColor;
                cell.normal.textColor = cellColor;
            }
        }
    }
}

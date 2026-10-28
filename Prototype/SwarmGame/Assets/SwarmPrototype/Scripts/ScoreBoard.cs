using UnityEngine;

namespace SwarmPrototype
{
    // Standings card top-right: one row per player with a
    // team swatch, name, total upgrade level and kills. Compact shows the first
    // four rows plus "+N more"; Tab (or the footer) expands to everyone; the X
    // hides the card and Tab brings it back.
    public class ScoreBoard
    {
        public enum Mode { Hidden, Compact, Full }

        const float Width = 270f;
        const float Edge = 14f;
        const float Top = 14f;
        const float RowHeight = 30f;
        const float HeaderHeight = 34f;
        const float FooterHeight = 34f;
        const float Pad = 12f;
        const int CompactRows = 4;

        public Mode State { get; private set; } = Mode.Compact;
        public bool Open => State != Mode.Hidden;
        public Rect Panel { get; private set; }

        public void Toggle() => State = State == Mode.Full ? Mode.Compact : Mode.Full;
        public void Hide() => State = Mode.Hidden;

        public bool Covers(Vector2 mouse) => Open && Panel.Contains(UiSkin.GuiPoint(mouse));

        public void Draw(Spawner[] bases)
        {
            if (!Open) { Panel = Rect.zero; return; }
            int total = bases.Length;
            int rows = State == Mode.Full ? total : Mathf.Min(CompactRows, total);
            bool footer = total > rows || State == Mode.Full;
            float height = HeaderHeight + rows * RowHeight + (footer ? FooterHeight : Pad * 0.5f) + 4f;
            Panel = new Rect(UiSkin.Width - Edge - Width, Top, Width, height);
            UiSkin.Panel(Panel);

            float x = Panel.x + Pad;
            float y = Panel.y;
            const float killsWidth = 36f, levelWidth = 44f;
            float killsX = Panel.xMax - Pad - 30f - killsWidth;
            float levelX = killsX - levelWidth;

            // Header: title, column glyphs, close.
            GUI.Label(new Rect(x + 4f, y, 160f, HeaderHeight), "STANDINGS", UiSkin.Header);
            var head = UiSkin.Header;
            var oldAlign = head.alignment;
            head.alignment = TextAnchor.MiddleCenter;
            GUI.Label(new Rect(levelX, y, levelWidth, HeaderHeight), "Lv", head);
            head.alignment = oldAlign;
            GUI.color = UiSkin.Muted;
            GUI.DrawTexture(new Rect(killsX + killsWidth * 0.5f - 7f, y + HeaderHeight * 0.5f - 7f, 14f, 14f), Icons.Get("swords"));
            GUI.color = Color.white;
            var close = new Rect(Panel.xMax - Pad - 24f, y + HeaderHeight * 0.5f - 12f, 24f, 24f);
            if (close.Contains(Event.current.mousePosition)) UiSkin.Rounded(close, UiSkin.Wash, 6f);
            GUI.color = UiSkin.Muted;
            GUI.DrawTexture(new Rect(close.x + 6f, close.y + 6f, 12f, 12f), Icons.Get("close"));
            GUI.color = Color.white;
            if (GUI.Button(close, GUIContent.none, GUIStyle.none)) { Hide(); return; }
            y += HeaderHeight;

            var cell = UiSkin.Bold;
            var numberStyle = UiSkin.Bold;
            for (int team = 0; team < rows; team++, y += RowHeight)
            {
                Spawner b = bases[team];
                Color color = GameState.TeamColors[team];
                bool alive = b != null && !b.IsDead;
                var row = new Rect(Panel.x + 6f, y, Panel.width - 12f, RowHeight);
                if (team == GameState.PlayerTeam) UiSkin.Rounded(row, UiSkin.Wash, 8f);

                Color nameColor = cell.normal.textColor;
                cell.normal.textColor = alive ? UiSkin.Text : UiSkin.Faint;
                UiSkin.Rounded(new Rect(x + 4f, y + RowHeight * 0.5f - 4f, 8f, 8f), alive ? color : Color.Lerp(color, UiSkin.Faint, 0.6f), 2f);
                GUI.Label(new Rect(x + 20f, y, levelX - x - 24f, RowHeight), GameState.TeamNames[team], cell);
                var old = numberStyle.alignment;
                numberStyle.alignment = TextAnchor.MiddleCenter;
                GUI.Label(new Rect(levelX, y, levelWidth, RowHeight), (b != null ? b.TotalLevels : 0).ToString(), numberStyle);
                GUI.Label(new Rect(killsX, y, killsWidth, RowHeight), GameState.Kills[team].ToString(), numberStyle);
                numberStyle.alignment = old;
                cell.normal.textColor = nameColor;
            }

            if (!footer) return;
            UiSkin.Rule(Panel.x + Pad, y + 2f, Panel.width - Pad * 2f);
            var foot = new Rect(Panel.x, y + 4f, Panel.width, FooterHeight - 4f);
            float fx = x + 4f, midY = foot.center.y;
            if (State == Mode.Compact)
            {
                for (int team = rows; team < total; team++, fx += 11f)
                    UiSkin.Rounded(new Rect(fx, midY - 4f, 8f, 8f), GameState.TeamColors[team], 2f);
                GUI.Label(new Rect(fx + 6f, foot.y, 120f, foot.height), $"+{total - rows} more", UiSkin.Small);
            }
            else GUI.Label(new Rect(fx, foot.y, 120f, foot.height), "Show fewer", UiSkin.Small);
            float keyWidth = UiSkin.Key(new Vector2(Panel.xMax - Pad - 44f, midY), "Tab");
            if (GUI.Button(foot, GUIContent.none, GUIStyle.none)) Toggle();
        }
    }
}

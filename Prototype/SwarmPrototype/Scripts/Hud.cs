using System.Collections.Generic;
using UnityEngine;

namespace SwarmPrototype
{
    // The match HUD chrome: resource pill top-left, match clock top-centre, help /
    // social / menu buttons top-right, the action bar at the bottom (units, Home,
    // Next enemy, Shop) and the help card. Hovering a resource explains where it
    // comes from. Toasts stack under the resource pill. The standings table is
    // ScoreBoard, the minimap is MiniMap.
    public class Hud
    {
        const float Edge = 14f;
        const float PillHeight = 44f;
        const float ToastLife = 4f;

        public Rect Panel { get; private set; }
        public Rect ClockPanel { get; private set; }
        public Rect Buttons { get; private set; }
        public Rect Bar { get; private set; }
        public Rect HelpPanel { get; private set; }

        // Set for one frame when the matching control was clicked.
        public bool MenuClicked { get; private set; }
        public bool HomeClicked { get; private set; }
        public bool EnemyClicked { get; private set; }
        public bool HelpOpen { get; private set; }

        readonly List<(string text, float until)> toasts = new List<(string, float)>();
        string tooltip;

        static readonly (string icon, float size, string help)[] Resources =
        {
            ("coin", 20f, "Gold: from killing anything. Buys every upgrade."),
            ("skull", 20f, "Skulls: every neutral monster you kill (1 easy, 4 medium, 10 hard, 50 elite). Greater upgrades cost skulls."),
            ("lesser", 14f, "Lesser Essence: a chance from any camp monster. Needed per level for the weaker Greater upgrades."),
            ("greater", 14f, "Greater Essence: a good chance from hard camps, guaranteed from elites. Needed for lifesteal, evasion, crit, reincarnate."),
            ("elite", 14f, "Elite Essence: guaranteed from the road elites, rare from hard camps. Needed for spawn rate and splash."),
        };

        static readonly (string action, string key)[] Controls =
        {
            ("Rally point", "Right-click"), ("Shop", "B  /  click tower"), ("Standings", "Tab"), ("Pan", "WASD  /  edge  /  middle-drag"),
            ("Zoom", "Scroll"), ("Home", "Space"), ("Next enemy", "E"), ("Minimap", "click look  /  right-click rally"),
            ("Social", "F6"), ("Restart", "R"), ("Menu", "Esc"),
        };

        public Hud()
        {
            GameState.Toast = text => toasts.Add((text, Time.time + ToastLife));
        }

        public bool Covers(Vector2 mouse)
        {
            var p = UiSkin.GuiPoint(mouse);
            return Panel.Contains(p) || ClockPanel.Contains(p) || Buttons.Contains(p) || Bar.Contains(p) || (HelpOpen && HelpPanel.Contains(p));
        }

        // Returns true when the Shop button was clicked this frame.
        public bool Draw(Spawner mine, bool shopOpen)
        {
            MenuClicked = HomeClicked = EnemyClicked = false;
            tooltip = null;
            Vector2 mouse = Event.current.mousePosition;

            DrawResources(mouse);
            DrawClock();
            DrawTopRight();
            bool shopClicked = DrawBar(mine, shopOpen);
            DrawTooltip();
            DrawToasts();
            if (HelpOpen) DrawHelp();
            return shopClicked;
        }

        void DrawResources(Vector2 mouse)
        {
            int team = GameState.PlayerTeam;
            int[] amounts = { GameState.Gold[team], GameState.Skulls[team], GameState.Essence[team][0], GameState.Essence[team][1], GameState.Essence[team][2] };

            // Measure first so the pill hugs its contents.
            float width = 16f;
            var widths = new float[Resources.Length];
            for (int i = 0; i < Resources.Length; i++)
            {
                widths[i] = Resources[i].size + 8f + UiSkin.Number.CalcSize(new GUIContent(amounts[i].ToString())).x + 16f;
                width += widths[i];
                if (i == 1) width += 17f; // divider between currencies and essences
            }
            Panel = new Rect(Edge, Edge, width, PillHeight);
            UiSkin.Panel(Panel);

            float x = Panel.x + 16f;
            float midY = Panel.center.y;
            for (int i = 0; i < Resources.Length; i++)
            {
                var slot = new Rect(x - 8f, Panel.y, widths[i], Panel.height);
                UiSkin.IconLabel(x, midY, Resources[i].icon, Resources[i].size, amounts[i].ToString(), UiSkin.Number);
                if (slot.Contains(mouse)) tooltip = Resources[i].help;
                x += widths[i];
                if (i == 1)
                {
                    UiSkin.Fill(new Rect(x - 4f, Panel.y + 14f, 1f, Panel.height - 28f), UiSkin.Border);
                    x += 17f;
                }
            }
        }

        // Match clock, top centre: minutes:seconds since the bases were placed. It stops with the pause menu.
        void DrawClock()
        {
            int seconds = Mathf.Max(0, Mathf.FloorToInt(GameState.MatchMinutes * 60f));
            string text = $"{seconds / 60}:{seconds % 60:00}";
            float w = UiSkin.Number.CalcSize(new GUIContent(text)).x + 40f;
            ClockPanel = new Rect(UiSkin.Width * 0.5f - w * 0.5f, Edge, w, 36f);
            UiSkin.Panel(ClockPanel, 12f);
            var style = UiSkin.Number;
            var old = style.alignment;
            style.alignment = TextAnchor.MiddleCenter;
            GUI.Label(ClockPanel, text, style);
            style.alignment = old;
        }

        void DrawTopRight()
        {
            const float size = 44f, gap = 8f;
            Buttons = new Rect(UiSkin.Width - Edge - size * 3f - gap * 2f, Edge, size * 3f + gap * 2f, size);
            var help = new Rect(Buttons.x, Buttons.y, size, size);
            var social = new Rect(help.xMax + gap, Buttons.y, size, size);
            var menu = new Rect(social.xMax + gap, Buttons.y, size, size);
            if (UiSkin.IconButton(help, "help", 0, HelpOpen)) HelpOpen = !HelpOpen;
            var overlay = SocialOverlay.Ensure();
            int attention = overlay != null && overlay.Data != null ? overlay.Data.Attention : 0;
            if (UiSkin.IconButton(social, "people", attention)) overlay?.Toggle("Friends");
            if (UiSkin.IconButton(menu, "menu")) MenuClicked = true;
        }

        bool DrawBar(Spawner mine, bool shopOpen)
        {
            int team = GameState.PlayerTeam;
            int units = mine != null ? mine.AliveUnits : 0;
            int cap = mine != null ? mine.MaxUnits : 0;

            const float height = 60f;
            const float unitsWidth = 150f, actionWidth = 150f, shopWidth = 130f, pad = 14f;
            float width = pad + unitsWidth + 1f + actionWidth * 2f + shopWidth + pad;
            Bar = new Rect(UiSkin.Width * 0.5f - width * 0.5f, UiSkin.Height - Edge - height, width, height);
            UiSkin.Panel(Bar, 16f);

            // Units alive / cap, then a segmented bar in the team color.
            float x = Bar.x + pad + 6f;
            float midY = Bar.center.y;
            string alive = units.ToString();
            var big = UiSkin.Big;
            float aw = big.CalcSize(new GUIContent(alive)).x;
            GUI.Label(new Rect(x, midY - 22f, aw + 4f, 30f), alive, big);
            if (cap > 0)
            {
                var mutedBig = UiSkin.Big;
                Color old = mutedBig.normal.textColor;
                mutedBig.normal.textColor = UiSkin.Muted;
                string capText = "/" + cap;
                float cw = mutedBig.CalcSize(new GUIContent(capText)).x;
                GUI.Label(new Rect(x + aw, midY - 22f, cw + 4f, 30f), capText, mutedBig);
                mutedBig.normal.textColor = old;
                if (units >= cap) GUI.Label(new Rect(x + aw + cw + 8f, midY - 18f, 40f, 22f), "MAX", UiSkin.Tiny);
            }
            int segments = cap > 0 ? Mathf.Min(cap, 20) : 10;
            float segWidth = (unitsWidth - 30f - (segments - 1) * 3f) / segments;
            float filled = cap > 0 ? (float)units / cap : 1f;
            Color teamColor = GameState.TeamColors[team];
            for (int i = 0; i < segments; i++)
            {
                bool on = (i + 0.5f) / segments <= filled;
                UiSkin.Rounded(new Rect(x + i * (segWidth + 3f), midY + 10f, segWidth, 5f), on ? teamColor : new Color(1f, 1f, 1f, 0.1f), 2f);
            }

            x = Bar.x + pad + unitsWidth;
            UiSkin.Fill(new Rect(x, Bar.y + 16f, 1f, height - 32f), UiSkin.Border);
            x += 1f;

            if (Action(new Rect(x, Bar.y, actionWidth, height), "home", "Home", "Space")) HomeClicked = true;
            x += actionWidth;
            if (Action(new Rect(x, Bar.y, actionWidth, height), "target", "Next enemy", "E")) EnemyClicked = true;
            x += actionWidth;
            return UiSkin.GoldButton(new Rect(x, Bar.y + 10f, shopWidth, height - 20f), shopOpen ? "Close" : "Shop", "bag", "B");
        }

        // Icon, caption and keycap on a transparent hit area; lights up on hover.
        static bool Action(Rect r, string icon, string label, string key)
        {
            bool hover = r.Contains(Event.current.mousePosition);
            float x = r.x + 18f;
            float midY = r.center.y;
            var style = UiSkin.Bold;
            Color old = style.normal.textColor;
            style.normal.textColor = hover ? Color.white : UiSkin.Text;
            x = UiSkin.IconLabel(x, midY, icon, 20f, label, style, hover ? Color.white : UiSkin.Text);
            style.normal.textColor = old;
            UiSkin.Key(new Vector2(x + 10f, midY), key);
            return GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        void DrawHelp()
        {
            const float width = 340f, row = 24f;
            float height = 16f + 24f + Controls.Length * row + 12f;
            HelpPanel = new Rect(UiSkin.Width - Edge - width, Buttons.yMax + 10f, width, height);
            UiSkin.Panel(HelpPanel);
            float x = HelpPanel.x + 16f, y = HelpPanel.y + 14f;
            GUI.Label(new Rect(x, y, 200f, 20f), "CONTROLS", UiSkin.Header);
            if (UiSkin.IconButton(new Rect(HelpPanel.xMax - 40f, y - 4f, 28f, 28f), "close")) HelpOpen = false;
            y += 28f;
            foreach (var (action, key) in Controls)
            {
                GUI.Label(new Rect(x, y, 140f, row), action, UiSkin.Cell);
                GUI.Label(new Rect(x + 130f, y, width - 150f, row), key, UiSkin.Small);
                y += row;
            }
        }

        void DrawTooltip()
        {
            if (tooltip == null) return;
            var style = UiSkin.Cell;
            style.wordWrap = true;
            float width = Mathf.Min(420f, style.CalcSize(new GUIContent(tooltip)).x + 24f);
            float height = style.CalcHeight(new GUIContent(tooltip), width - 24f) + 16f;
            var rect = new Rect(Panel.x, Panel.yMax + 8f, width, height);
            UiSkin.Panel(rect, 10f);
            GUI.Label(new Rect(rect.x + 12f, rect.y + 8f, width - 24f, height - 16f), tooltip, style);
            style.wordWrap = false;
        }

        // Short-lived messages under the resource pill (below the tooltip if one is showing).
        void DrawToasts()
        {
            toasts.RemoveAll(t => Time.time > t.until);
            float y = Panel.yMax + (tooltip != null ? 70f : 10f);
            foreach (var t in toasts)
            {
                float alpha = Mathf.Clamp01((t.until - Time.time) / 1f);
                var style = UiSkin.Bold;
                float width = style.CalcSize(new GUIContent(t.text)).x + 28f;
                var rect = new Rect(Panel.x, y, width, 30f);
                UiSkin.Pill(rect, new Color(UiSkin.Surface.r, UiSkin.Surface.g, UiSkin.Surface.b, 0.92f * alpha));
                Color old = style.normal.textColor;
                style.normal.textColor = new Color(old.r, old.g, old.b, alpha);
                GUI.Label(new Rect(rect.x + 14f, rect.y, width, 30f), t.text, style);
                style.normal.textColor = old;
                y += 34f;
            }
        }
    }
}

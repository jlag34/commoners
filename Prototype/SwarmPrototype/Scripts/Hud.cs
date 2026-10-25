using System.Collections.Generic;
using UnityEngine;

namespace SwarmPrototype
{
    // The resource bar in the top-left: gold, skulls, the three essences, units
    // alive and a Shop button. Hovering a resource explains where it comes from.
    // Also the toast messages under it and the faint controls line at the bottom.
    public class Hud
    {
        const float Height = 36f;
        const float ToastLife = 4f;

        public Rect Panel { get; private set; }

        readonly List<(string text, float until)> toasts = new List<(string, float)>();
        string tooltip;

        static readonly (string icon, string caption, string help)[] Resources =
        {
            ("coin", "gold", "Gold: from killing anything. Buys every upgrade."),
            ("skull", "skulls", "Skulls: every neutral monster you kill (1 easy, 4 medium, 10 hard, 50 elite). Greater upgrades cost skulls."),
            ("lesser", "lesser", "Lesser Essence: a chance from any camp monster. Needed per level for the weaker Greater upgrades."),
            ("greater", "greater", "Greater Essence: a good chance from hard camps, guaranteed from elites. Needed for lifesteal, evasion, crit, reincarnate."),
            ("elite", "elite", "Elite Essence: guaranteed from the road elites, rare from hard camps. Needed for spawn rate and splash."),
        };

        public Hud()
        {
            GameState.Toast = text => toasts.Add((text, Time.time + ToastLife));
        }

        public bool Covers(Vector2 mouse) => Panel.Contains(new Vector2(mouse.x, Screen.height - mouse.y));

        // Returns true when the Shop button was clicked this frame.
        public bool Draw(Spawner mine, bool shopOpen)
        {
            int team = GameState.PlayerTeam;
            int units = mine != null ? mine.AliveUnits : 0;
            tooltip = null;

            Panel = new Rect(12f, 12f, 560f, Height);
            UiSkin.Panel(Panel);
            Vector2 mouse = Event.current.mousePosition;

            float x = Panel.x + 10f;
            int[] amounts = { GameState.Gold[team], GameState.Skulls[team], GameState.Essence[team][0], GameState.Essence[team][1], GameState.Essence[team][2] };
            for (int i = 0; i < Resources.Length; i++)
            {
                float start = x;
                x = Stat(x, Resources[i].icon, amounts[i].ToString(), Resources[i].caption);
                if (new Rect(start, Panel.y, x - start, Panel.height).Contains(mouse)) tooltip = Resources[i].help;
            }
            UiSkin.Fill(new Rect(x, Panel.y + 8f, 1f, Height - 16f), UiSkin.Gold);
            x += 12f;
            float unitsStart = x;
            int cap = mine != null ? mine.MaxUnits : 0;
            x = StatDot(x, GameState.TeamColors[team], cap > 0 ? $"{units}/{cap}" : units.ToString(), "units");
            if (new Rect(unitsStart, Panel.y, x - unitsStart, Panel.height).Contains(mouse))
                tooltip = cap > 0 ? "Units alive / max. One spawns every few seconds until the cap; buy Unit Count in the shop to raise it." : "Units alive. One spawns every few seconds; no cap.";

            var shopRect = new Rect(Panel.xMax - 86f, Panel.y + 6f, 76f, Height - 12f);
            bool clicked = UiSkin.Button(shopRect, shopOpen ? "Close" : "Shop  (B)", shopOpen);

            DrawTooltip();
            DrawToasts();

            GUI.Label(new Rect(232f, Screen.height - 20f, Screen.width - 244f, 16f),
                "Right-click: rally point    B / click tower: shop    Tab: stats    WASD, edge, middle-drag: pan    Scroll: zoom    Space: home    E: next enemy    Minimap: click to look, right-click to rally    R: restart    M: menu", UiSkin.Small);
            return clicked;
        }

        void DrawTooltip()
        {
            if (tooltip == null) return;
            var style = UiSkin.Cell;
            float width = Mathf.Min(420f, style.CalcSize(new GUIContent(tooltip)).x + 16f);
            float height = style.CalcHeight(new GUIContent(tooltip), width - 16f) + 10f;
            var rect = new Rect(Panel.x, Panel.yMax + 6f, width, height);
            UiSkin.Panel(rect);
            GUI.Label(new Rect(rect.x + 8f, rect.y + 5f, width - 16f, height - 10f), tooltip, style);
        }

        // Short-lived messages under the banner (below the tooltip if one is showing).
        void DrawToasts()
        {
            toasts.RemoveAll(t => Time.time > t.until);
            float y = Panel.yMax + (tooltip != null ? 70f : 8f);
            foreach (var t in toasts)
            {
                float alpha = Mathf.Clamp01((t.until - Time.time) / 1f);
                float width = UiSkin.Bold.CalcSize(new GUIContent(t.text)).x + 16f;
                var rect = new Rect(Panel.x, y, width, 20f);
                UiSkin.Fill(rect, new Color(0f, 0f, 0f, 0.6f * alpha));
                var style = UiSkin.Bold;
                Color old = style.normal.textColor;
                style.normal.textColor = new Color(old.r, old.g, old.b, alpha);
                GUI.Label(new Rect(rect.x + 8f, rect.y, width, 20f), t.text, style);
                style.normal.textColor = old;
                y += 22f;
            }
        }

        // Icon, big number, muted caption on one line. Returns where the next one starts.
        float Stat(float x, string icon, string value, string caption)
        {
            float midY = Panel.y + Height * 0.5f;
            GUI.DrawTexture(new Rect(x, midY - 10f, 20f, 20f), Icons.Get(icon));
            return Text(x + 24f, midY, value, caption);
        }

        float StatDot(float x, Color color, string value, string caption)
        {
            float midY = Panel.y + Height * 0.5f;
            UiSkin.Icon(new Rect(x, midY - 7f, 14f, 14f), color);
            return Text(x + 20f, midY, value, caption);
        }

        float Text(float x, float midY, string value, string caption)
        {
            float valueWidth = UiSkin.Number.CalcSize(new GUIContent(value)).x;
            GUI.Label(new Rect(x, midY - 12f, valueWidth + 4f, 24f), value, UiSkin.Number);
            x += valueWidth + 4f;
            float captionWidth = UiSkin.Small.CalcSize(new GUIContent(caption)).x;
            GUI.Label(new Rect(x, midY - 8f, captionWidth + 4f, 16f), caption, UiSkin.Small);
            return x + captionWidth + 12f;
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace SwarmPrototype
{
    // The tower shop as a grid of icon tiles, Warcraft style: one tile per
    // upgrade, level badge in the corner, click to buy. Hovering a tile shows
    // what it does and what the next level costs; with nothing hovered the info
    // strip explains where skulls and essences come from. The Greater tab is one
    // row per essence tier (Lesser, Greater, Elite) with the tier's icon at the left.
    public class Shop
    {
        const float Tile = 56f;
        const float Gap = 6f;
        const int Columns = 4;
        const float RowLabel = 74f;
        const float Pad = 12f;
        const float HeaderHeight = 34f;
        const float TabHeight = 28f;
        const float InfoHeight = 62f;

        public bool Open { get; set; }

        Rect panel;
        string tab = "stats";
        readonly List<string> tabs = new List<string>();
        readonly List<int> inTab = new List<int>();

        // Screen-space (y up) rect of the panel, so clicks on it don't fall through to the map.
        public bool Covers(Vector2 mouse) => Open && panel.Contains(new Vector2(mouse.x, Screen.height - mouse.y));

        public void Draw(Spawner tower)
        {
            if (!Open || tower == null) return;

            tabs.Clear();
            inTab.Clear();
            for (int i = 0; i < tower.Upgrades.Length; i++)
            {
                string t = string.IsNullOrEmpty(tower.Upgrades[i].tab) ? "stats" : tower.Upgrades[i].tab;
                if (!tabs.Contains(t)) tabs.Add(t);
                if (t == tab) inTab.Add(i);
            }
            if (!tabs.Contains(tab) && tabs.Count > 0) { tab = tabs[0]; return; }

            // Stats: a plain grid. Anything essence-gated: one row per tier, as wide as the longest row.
            bool byTier = inTab.Exists(i => tower.EssenceTier(i) >= 0);
            int columns = Columns, rows;
            float labelWidth = 0f;
            if (byTier)
            {
                rows = GameState.EssenceTiers.Length;
                columns = 1;
                for (int t = 0; t < rows; t++)
                {
                    int tier = t;
                    columns = Mathf.Max(columns, inTab.FindAll(i => tower.EssenceTier(i) == tier).Count);
                }
                labelWidth = RowLabel;
            }
            else rows = Mathf.Max(1, Mathf.CeilToInt(inTab.Count / (float)Columns));
            float gridWidth = labelWidth + columns * Tile + (columns - 1) * Gap;
            float width = gridWidth + Pad * 2f;
            float height = HeaderHeight + TabHeight + rows * (Tile + Gap) + InfoHeight + Pad;
            panel = new Rect((Screen.width - width) * 0.5f, Screen.height - height - 30f, width, height);
            UiSkin.Panel(panel);

            float x = panel.x + Pad;
            float y = panel.y + 8f;
            GUI.Label(new Rect(x, y, 140f, 22f), "Tower Shop", UiSkin.Title);
            Currency(panel.xMax - 150f, y + 2f, "coin", GameState.Gold[tower.Team]);
            Currency(panel.xMax - 100f, y + 2f, "skull", GameState.Skulls[tower.Team]);
            if (UiSkin.Button(new Rect(panel.xMax - 30f, y + 1f, 18f, 18f), "x")) Open = false;

            y = panel.y + HeaderHeight;
            float tabX = x;
            foreach (var t in tabs)
            {
                string label = char.ToUpper(t[0]) + t.Substring(1);
                float w = UiSkin.Bold.CalcSize(new GUIContent(label)).x + 24f;
                if (UiSkin.Button(new Rect(tabX, y, w, TabHeight - 8f), label, t == tab)) tab = t;
                tabX += w + 6f;
            }

            y += TabHeight;
            int hovered = -1;
            Vector2 mouse = Event.current.mousePosition;
            if (byTier)
            {
                for (int tier = 0; tier < rows; tier++)
                {
                    float rowY = y + tier * (Tile + Gap);
                    GUI.DrawTexture(new Rect(x, rowY + 10f, 18f, 18f), Icons.Get(GameState.EssenceTiers[tier]));
                    GUI.Label(new Rect(x + 22f, rowY + 8f, labelWidth - 22f, 22f), GameState.EssenceLabels[tier], UiSkin.Bold);
                    int column = 0;
                    foreach (int i in inTab)
                    {
                        if (tower.EssenceTier(i) != tier) continue;
                        var rect = new Rect(x + labelWidth + column * (Tile + Gap), rowY, Tile, Tile);
                        if (rect.Contains(mouse)) hovered = i;
                        DrawTile(tower, i, rect, hovered == i);
                        column++;
                    }
                }
            }
            else
            {
                for (int n = 0; n < inTab.Count; n++)
                {
                    int i = inTab[n];
                    var rect = new Rect(x + (n % Columns) * (Tile + Gap), y + (n / Columns) * (Tile + Gap), Tile, Tile);
                    if (rect.Contains(mouse)) hovered = i;
                    DrawTile(tower, i, rect, hovered == i);
                }
            }

            var info = new Rect(panel.x + Pad, panel.yMax - InfoHeight - Pad * 0.5f, panel.width - Pad * 2f, InfoHeight);
            UiSkin.Rule(info.x, info.y - 4f, info.width);
            if (hovered >= 0) DrawInfo(tower, hovered, info);
            else DrawLegend(info);
        }

        static void Currency(float x, float y, string icon, int amount)
        {
            GUI.DrawTexture(new Rect(x, y, 18f, 18f), Icons.Get(icon));
            GUI.Label(new Rect(x + 22f, y - 3f, 60f, 24f), amount.ToString(), UiSkin.Number);
        }

        void DrawTile(Spawner tower, int i, Rect rect, bool hover)
        {
            UpgradeConfig u = tower.Upgrades[i];
            bool maxed = tower.IsMaxed(i);
            bool canAfford = !maxed && tower.CanAfford(i) && !GameState.Over;

            Color frame = maxed ? UiSkin.GoldBright : canAfford ? UiSkin.Gold : UiSkin.Muted;
            if (hover && canAfford) frame = Color.white;
            UiSkin.Fill(new Rect(rect.x - 2f, rect.y - 2f, rect.width + 4f, rect.height + 4f), frame);
            UiSkin.Fill(rect, hover ? UiSkin.BackLight : new Color(0.1f, 0.09f, 0.08f, 1f));

            GUI.color = canAfford || maxed ? Color.white : new Color(0.5f, 0.5f, 0.5f, 1f);
            GUI.DrawTexture(new Rect(rect.x + 6f, rect.y + 4f, rect.width - 12f, rect.height - 12f), Icons.Get(u.id));
            GUI.color = Color.white;

            // Level badge, bottom-right.
            string badge = maxed ? "MAX" : u.maxLevel > 0 ? $"{tower.Level(i)}/{u.maxLevel}" : tower.Level(i).ToString();
            var small = UiSkin.Small;
            float bw = small.CalcSize(new GUIContent(badge)).x + 8f;
            var badgeRect = new Rect(rect.xMax - bw - 2f, rect.yMax - 16f, bw, 14f);
            UiSkin.Fill(badgeRect, new Color(0f, 0f, 0f, 0.75f));
            Color old = small.normal.textColor;
            small.normal.textColor = maxed ? UiSkin.GoldBright : UiSkin.Text;
            GUI.Label(new Rect(badgeRect.x + 4f, badgeRect.y - 1f, bw, 16f), badge, small);
            small.normal.textColor = old;

            // Essence tier dot, top-right, so the gating reads at a glance.
            int tier = tower.EssenceTier(i);
            if (tier >= 0) GUI.DrawTexture(new Rect(rect.xMax - 16f, rect.y + 2f, 14f, 14f), Icons.Get(GameState.EssenceTiers[tier]));

            if (canAfford && GUI.Button(rect, GUIContent.none, GUIStyle.none)) tower.Buy(i);
        }

        void DrawInfo(Spawner tower, int i, Rect area)
        {
            UpgradeConfig u = tower.Upgrades[i];
            bool maxed = tower.IsMaxed(i);
            float x = area.x, y = area.y;
            GUI.Label(new Rect(x, y, 200f, 20f), u.name, UiSkin.Bold);

            string value;
            if (maxed) value = Describe(u.stat, tower.Stats, tower.Towers, tower.Economy, null, null, null) + "   (max level)";
            else
            {
                tower.After(i, out var nu, out var nt, out var ne);
                value = Describe(u.stat, tower.Stats, tower.Towers, tower.Economy, nu, nt, ne);
            }
            GUI.Label(new Rect(x, y + 20f, area.width, 20f), value, UiSkin.Cell);

            if (maxed) return;
            float cx = x;
            GUI.Label(new Rect(cx, y + 40f, 60f, 20f), $"Level {tower.Level(i) + 1}:", UiSkin.Small);
            cx += 58f;
            cx = Price(cx, y + 40f, "coin", tower.Cost(i));
            if (tower.SkullCost(i) > 0) cx = Price(cx, y + 40f, "skull", tower.SkullCost(i));
            int tier = tower.EssenceTier(i);
            if (tier >= 0) cx = Price(cx, y + 40f, GameState.EssenceTiers[tier], tower.EssenceCost(i), GameState.EssenceLabels[tier].ToLower());
            if (!tower.CanAfford(i)) GUI.Label(new Rect(cx + 4f, y + 40f, 120f, 20f), "can't afford yet", UiSkin.Small);
        }

        static float Price(float x, float y, string icon, int amount, string suffix = "")
        {
            GUI.DrawTexture(new Rect(x, y + 2f, 16f, 16f), Icons.Get(icon));
            string text = suffix == "" ? amount.ToString() : $"{amount} {suffix}";
            float w = UiSkin.Cell.CalcSize(new GUIContent(text)).x;
            GUI.Label(new Rect(x + 19f, y, w + 4f, 20f), text, UiSkin.Cell);
            return x + 19f + w + 14f;
        }

        void DrawLegend(Rect area)
        {
            float x = area.x, y = area.y;
            if (tab == "stats")
            {
                GUI.Label(new Rect(x, y, area.width, 20f), "Stats: gold only. Every unit on your team gets every level.", UiSkin.Small);
                GUI.Label(new Rect(x, y + 18f, area.width, 20f), "Hover a tile to see what it does. Click to buy.", UiSkin.Small);
                return;
            }
            GUI.Label(new Rect(x, y, area.width, 20f), "Greater: gold, skulls and one essence per level. Each row is an essence tier.", UiSkin.Small);
            float lx = x;
            lx = LegendIcon(lx, y + 20f, "skull", "any neutral kill");
            lx = LegendIcon(lx, y + 20f, "lesser", "any camp");
            lx = x;
            lx = LegendIcon(lx, y + 40f, "greater", "hard camps, elites");
            lx = LegendIcon(lx, y + 40f, "elite", "elites on the roads");
        }

        static float LegendIcon(float x, float y, string icon, string text)
        {
            GUI.DrawTexture(new Rect(x, y + 2f, 14f, 14f), Icons.Get(icon));
            float w = UiSkin.Small.CalcSize(new GUIContent(text)).x;
            GUI.Label(new Rect(x + 18f, y, w + 4f, 18f), text, UiSkin.Small);
            return x + 18f + w + 16f;
        }

        // "Damage 10 -> 11" for a stat; `next` null shows just the current value.
        public static string Describe(string stat, UnitStats now, TowerBonus towersNow, EconomyBonus econNow, UnitStats next, TowerBonus towersNext, EconomyBonus econNext)
        {
            var secondary = GameState.Config.secondaryTower;
            float baseInterval = GameState.Config.spawnInterval;
            int baseCap = GameState.Config.maxUnitsPerTeam;
            switch (stat)
            {
                case "attack": return "Damage " + Arrow(now.attack, next?.attack, "0");
                case "attackSpeed": return "Attacks per second " + Arrow(now.attacksPerSecond, next?.attacksPerSecond, "0.0");
                case "health": return "Unit health " + Arrow(now.maxHealth, next?.maxHealth, "0");
                case "armor": return "Armor (flat damage cut) " + Arrow(now.armor, next?.armor, "0");
                case "moveSpeed": return "Move speed " + Arrow(now.moveSpeed, next?.moveSpeed, "0.0");
                case "evasion": return "Dodge chance " + Percent(now.evasion, next?.evasion);
                case "lifesteal": return "Heal for " + Percent(now.lifesteal, next?.lifesteal) + " of damage dealt";
                case "crit": return "Double-damage chance " + Percent(now.critChance, next?.critChance);
                case "splash": return "Nearby enemies take " + Percent(now.splash, next?.splash) + " of each hit";
                case "regen": return "Unit regen " + Arrow(now.regen, next?.regen, "0") + " HP/s";
                case "towerRegen": return "Tower regen " + Arrow(towersNow.regen, towersNext?.regen, "0") + " HP/s";
                case "towerDamage": return "Tower damage " + Arrow(secondary.attack * (1f + towersNow.damage), towersNext != null ? secondary.attack * (1f + towersNext.damage) : (float?)null, "0");
                case "towerRange": return "Tower range " + Arrow(secondary.range + towersNow.range, towersNext != null ? secondary.range + towersNext.range : (float?)null, "0") + " m";
                case "spawnRate": return "A unit every " + Arrow(baseInterval / (1f + econNow.spawnRate), econNext != null ? baseInterval / (1f + econNext.spawnRate) : (float?)null, "0.0") + " s";
                case "bounty": return "Gold from kills +" + Percent(econNow.bounty, econNext?.bounty);
                case "unitCap": return "Max units " + Arrow(baseCap + econNow.unitCap, econNext != null ? baseCap + econNext.unitCap : (float?)null, "0");
                case "reincarnate": return "Dead units return " + Percent(econNow.reincarnate, econNext?.reincarnate) + " of the time";
                default: return stat;
            }
        }

        static string Percent(float now, float? next) => Arrow(now * 100f, next.HasValue ? next.Value * 100f : (float?)null, "0'%'");

        static string Arrow(float now, float? next, string format)
        {
            string text = now.ToString(format);
            if (next.HasValue) text += $" -> {next.Value.ToString(format)}";
            return text;
        }
    }
}

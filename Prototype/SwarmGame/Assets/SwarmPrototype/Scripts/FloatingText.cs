using System.Collections.Generic;
using UnityEngine;

namespace SwarmPrototype
{
    // "+5 (coin)" style popups anchored to the world: rise, fade, gone. Essence
    // drops are bigger and slower so they can't be missed.
    public static class FloatingText
    {
        struct Entry
        {
            public Vector3 world;
            public string text;
            public Texture2D icon;
            public Color color;
            public float size, rise, duration, start;
        }

        static readonly List<Entry> entries = new List<Entry>();
        static GUIStyle style;

        public static void Clear() => entries.Clear();

        public static void Reward(Vector3 where, string kind, int amount)
        {
            Vector2 jitter = Random.insideUnitCircle * 0.5f;
            where += new Vector3(jitter.x, 0f, jitter.y);
            switch (kind)
            {
                case "gold": Add(where, $"+{amount}", Icons.Get("coin"), new Color(1f, 0.85f, 0.3f), 14f, 2.2f, 1.4f); break;
                case "skulls": Add(where + Vector3.up * 0.6f, $"+{amount}", Icons.Get("skull"), new Color(0.9f, 0.9f, 0.85f), 14f, 2.2f, 1.6f); break;
                default:
                    int tier = GameState.EssenceIndex(kind);
                    if (tier < 0) return;
                    Add(where + Vector3.up * 1.2f, $"+{amount} {GameState.EssenceLabels[tier]} Essence", Icons.Get(kind), GameState.EssenceColors[tier], 20f, 3f, 3f);
                    break;
            }
        }

        public static void Add(Vector3 world, string text, Texture2D icon, Color color, float size, float rise, float duration)
        {
            entries.Add(new Entry { world = world, text = text, icon = icon, color = color, size = size, rise = rise, duration = duration, start = Time.time });
        }

        public static void Draw(Camera cam)
        {
            if (cam == null) return;
            if (style == null) style = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                var e = entries[i];
                float t = (Time.time - e.start) / e.duration;
                if (t >= 1f) { entries.RemoveAt(i); continue; }
                float ease = 1f - (1f - t) * (1f - t);
                Vector3 screen = cam.WorldToScreenPoint(e.world + Vector3.up * (1.6f + e.rise * ease));
                if (screen.z < 0f) continue;
                float alpha = t < 0.7f ? 1f : 1f - (t - 0.7f) / 0.3f;
                style.fontSize = Mathf.RoundToInt(e.size);
                float iconSize = e.icon != null ? e.size + 4f : 0f;
                float textWidth = style.CalcSize(new GUIContent(e.text)).x;
                float x = screen.x - (iconSize + 4f + textWidth) * 0.5f;
                float y = Screen.height - screen.y - iconSize * 0.5f;
                // Dark shadow behind the text so it reads on grass.
                style.normal.textColor = new Color(0f, 0f, 0f, 0.8f * alpha);
                GUI.Label(new Rect(x + iconSize + 5f, y + 1f, textWidth + 4f, iconSize), e.text, style);
                style.normal.textColor = new Color(e.color.r, e.color.g, e.color.b, alpha);
                GUI.Label(new Rect(x + iconSize + 4f, y, textWidth + 4f, iconSize), e.text, style);
                if (e.icon != null)
                {
                    GUI.color = new Color(1f, 1f, 1f, alpha);
                    GUI.DrawTexture(new Rect(x, y, iconSize, iconSize), e.icon);
                    GUI.color = Color.white;
                }
            }
        }
    }
}

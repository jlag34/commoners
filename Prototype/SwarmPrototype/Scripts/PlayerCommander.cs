using UnityEngine;

namespace SwarmPrototype
{
    // The player's controls and HUD: right-click the ground to move the rally
    // point (every unit drops what it's doing and walks there), left-click your
    // tower (or press B, or the HUD button) for the shop, R to restart. Draws
    // health bars with IMGUI so the prototype needs no Canvas setup; the banner
    // is in Hud.cs and the shop in Shop.cs.
    public class PlayerCommander : MonoBehaviour
    {
        Spawner playerBase;
        Spawner[] bases;
        Transform flag;
        Camera cam;
        GUIStyle bigLabel;
        readonly Shop shop = new Shop();
        readonly Hud hud = new Hud();
        readonly ScoreBoard scoreBoard = new ScoreBoard();
        readonly MiniMap miniMap = new MiniMap();
        bool debug;

        public void Init(Spawner[] bases, Transform flag, MapLayout layout)
        {
            miniMap.Init(layout, bases);
            this.bases = bases;
            playerBase = bases[GameState.PlayerTeam];
            FloatingText.Clear();
            GameState.Reward = FloatingText.Reward;
            GameState.Ping = miniMap.Ping;
            this.flag = flag;
            // No rally point at the start: the flag appears on the first right-click.
            flag.gameObject.SetActive(playerBase.RallyPoint.HasValue);
            if (playerBase.RallyPoint.HasValue) flag.position = playerBase.RallyPoint.Value;
        }

        void Update()
        {
            if (SocialOverlay.IsOpen || SocialOverlay.IsTyping) return;
            if (cam == null) cam = Camera.main;

            if (InputShim.RestartPressed)
            {
                Bootstrap.Restart();
                return;
            }
            if (InputShim.MenuPressed)
            {
                Bootstrap.ShowMenu();
                return;
            }

            if (InputShim.CancelPressed) shop.Open = false;
            if (InputShim.ShopPressed) shop.Open = !shop.Open;
            if (InputShim.StatsPressed) scoreBoard.Open = !scoreBoard.Open;
            if (InputShim.DebugPressed) debug = Unit.DebugDraw = !debug;
            if (InputShim.GearPressed) CycleGear();

            if (playerBase != null && cam != null && InputShim.LeftClickDown && !OverUi(InputShim.MousePosition))
            {
                Ray ray = cam.ScreenPointToRay(InputShim.MousePosition);
                // Click your tower to toggle the shop; click anything else to close it.
                shop.Open = HitsTower(ray, playerBase) && !shop.Open;
            }

            if (playerBase != null && cam != null && InputShim.RightClickDown && !OverUi(InputShim.MousePosition))
            {
                Ray ray = cam.ScreenPointToRay(InputShim.MousePosition);
                if (GroundPoint(ray, out Vector3 point)) SetRally(point);
            }
        }

        // F2 (dev): jump your units' Strength / Armor / Health / Move Speed through the gear
        // tiers (0, 1, 6, 11) without paying, to eyeball the progression.
        static readonly int[] GearLevels = { 0, 1, 6, 11 };
        int gearStep;
        void CycleGear()
        {
            if (playerBase == null) return;
            gearStep = (gearStep + 1) % GearLevels.Length;
            int level = GearLevels[gearStep];
            foreach (var id in new[] { "strength", "armor", "health", "moveSpeed" }) playerBase.DebugSetLevel(id, level);
            GameState.Toast?.Invoke($"Debug: gear level {level} (tier {UnitGear.Tier(level)})");
        }

        bool OverUi(Vector2 mouse) =>
            (UnityEngine.EventSystems.EventSystem.current != null && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
            || shop.Covers(mouse) || hud.Covers(mouse) || scoreBoard.Covers(mouse) || miniMap.Covers(mouse);

        // Moves the rally flag and sends every unit there.
        void SetRally(Vector3 point)
        {
            if (playerBase == null) return;
            float limit = GameState.MapSize * 0.5f;
            point.x = Mathf.Clamp(point.x, -limit, limit);
            point.z = Mathf.Clamp(point.z, -limit, limit);
            // Snap to the ground or plateau under that spot.
            if (Physics.Raycast(point + Vector3.up * 50f, Vector3.down, out var hit, 100f, ~0, QueryTriggerInteraction.Ignore)) point.y = hit.point.y;
            playerBase.RallyPoint = point;
            flag.gameObject.SetActive(true);
            flag.position = point;
            foreach (var c in Combatant.All)
                if (c is Unit u && u.Team == GameState.PlayerTeam) u.MoveToRally();
            RallyPulse.Spawn(point, GameState.TeamColors[GameState.PlayerTeam], transform);
        }

        // True if the ray lands on `tower`, looking through any units standing in front of it.
        static bool HitsTower(Ray ray, Spawner tower)
        {
            var hits = Physics.RaycastAll(ray, 500f, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var hit in hits)
            {
                var combatant = hit.collider.GetComponent<Combatant>();
                if (combatant == tower) return true;
                if (combatant is Unit) continue;
                return false;
            }
            return false;
        }

        // First thing the ray hits that isn't a unit or a base: low ground, a ramp or a plateau.
        static bool GroundPoint(Ray ray, out Vector3 point)
        {
            var hits = Physics.RaycastAll(ray, 500f, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var hit in hits)
            {
                if (hit.collider.GetComponent<Combatant>() != null) continue;
                point = hit.point;
                return true;
            }
            point = default;
            return false;
        }

        void OnGUI()
        {
            if (cam == null) return;
            DrawHealthBars();
            shop.Draw(playerBase);
            FloatingText.Draw(cam);
            miniMap.Draw(cam);
            if (miniMap.Clicked(out Vector3 target, out int button))
            {
                if (button == 1) SetRally(target);
                else
                {
                    var rts = cam.GetComponent<RtsCamera>();
                    if (rts != null) rts.FocusOn(target);
                }
            }
            if (hud.Draw(playerBase, shop.Open)) shop.Open = !shop.Open;
            scoreBoard.Draw(bases);
            DrawCampLabel();
            if (debug) DrawDebug();

            if (GameState.Over)
            {
                if (bigLabel == null)
                    bigLabel = new GUIStyle(GUI.skin.label) { fontSize = 48, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                string text = GameState.Winner == GameState.PlayerTeam ? "Victory" : "Defeat";
                GUI.Label(new Rect(0, Screen.height * 0.3f, Screen.width, 80), text, bigLabel);
                GUI.Label(new Rect(0, Screen.height * 0.3f + 70, Screen.width, 30), "Press R to play again", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter });
            }
        }

        // F1: each unit's brain state over its head, for chasing down stuck units.
        void DrawDebug()
        {
            var style = UiSkin.Small;
            foreach (var c in Combatant.All)
            {
                if (!(c is Unit u)) continue;
                Vector3 screen = cam.WorldToScreenPoint(u.transform.position + Vector3.up * (u.HealthBarHeight + 0.4f));
                if (screen.z < 0f) continue;
                string text = u.DebugState;
                float width = style.CalcSize(new GUIContent(text)).x + 8f;
                var rect = new Rect(screen.x - width * 0.5f, Screen.height - screen.y - 16f, width, 16f);
                UiSkin.Fill(rect, new Color(0f, 0f, 0f, 0.6f));
                GUI.Label(new Rect(rect.x + 4f, rect.y, width, 16f), text, style);
            }
        }

        // Name, tier, count and strength bonus floating over each monster camp, and the elite's name.
        void DrawCampLabel()
        {
            var style = UiSkin.Bold;
            var old = style.alignment;
            style.alignment = TextAnchor.MiddleCenter;
            foreach (var elite in EliteSpawner.Alive)
            {
                if (elite == null || elite.IsDead) continue;
                Vector3 screen = cam.WorldToScreenPoint(elite.transform.position + Vector3.up * (elite.HealthBarHeight + 1.2f));
                if (screen.z <= 0f) continue;
                string text = $"Elite  ·  {elite.DisplayName}  ·  {EliteAbilities.Describe(elite.Abilities)}";
                float width = style.CalcSize(new GUIContent(text)).x + 16f;
                var rect = new Rect(screen.x - width * 0.5f, Screen.height - screen.y - 10f, width, 20f);
                UiSkin.Fill(rect, new Color(0.35f, 0.1f, 0f, 0.7f));
                GUI.Label(rect, text, style);
            }
            foreach (var camp in Camp.All)
            {
                if (camp == null) continue;
                Vector3 screen = cam.WorldToScreenPoint(camp.Center + Vector3.up * 4f);
                if (screen.z < 0f || screen.x < -100f || screen.x > Screen.width + 100f || screen.y < -50f || screen.y > Screen.height + 50f) continue;
                int bonus = Mathf.RoundToInt((camp.Scale - 1f) * 100f);
                string text = camp.Cleared
                    ? $"Cleared  ·  respawns in {Mathf.CeilToInt(camp.RespawnIn)}s"
                    : $"{camp.Name}  ·  {camp.Difficulty}  ·  {camp.Alive}/{camp.Total}" + (bonus > 0 ? $"  ·  +{bonus}%" : "");
                float width = style.CalcSize(new GUIContent(text)).x + 16f;
                var rect = new Rect(screen.x - width * 0.5f, Screen.height - screen.y - 10f, width, 20f);
                UiSkin.Fill(rect, new Color(0f, 0f, 0f, 0.55f));
                GUI.Label(rect, text, style);
            }
            style.alignment = old;
        }

        void DrawHealthBars()
        {
            foreach (var c in Combatant.All)
            {
                bool isBase = c.IsBuilding;
                bool elite = c is Unit eu && eu.IsElite;
                // Units only show a bar once they've been hit, to cut clutter. The elite always does.
                if (!isBase && !elite && c.Health >= c.MaxHealth) continue;

                Vector3 screen = cam.WorldToScreenPoint(c.transform.position + Vector3.up * c.HealthBarHeight);
                if (screen.z < 0f) continue;

                float width = c is Spawner ? 110f : isBase || elite ? 70f : 30f;
                float height = c is Spawner ? 10f : isBase || elite ? 8f : 5f;
                var frame = new Rect(screen.x - width * 0.5f - 1f, Screen.height - screen.y - 1f, width + 2f, height + 2f);
                var back = new Rect(frame.x + 1f, frame.y + 1f, width, height);
                var fill = new Rect(back.x, back.y, width * Mathf.Clamp01(c.Health / c.MaxHealth), height);

                GUI.color = new Color(0f, 0f, 0f, 0.85f);
                GUI.DrawTexture(frame, Texture2D.whiteTexture);
                GUI.color = new Color(0.2f, 0.05f, 0.05f, 0.9f);
                GUI.DrawTexture(back, Texture2D.whiteTexture);
                GUI.color = c.Team == GameState.PlayerTeam ? new Color(0.35f, 0.9f, 0.3f)
                    : c.Team == GameState.NeutralTeam ? new Color(0.95f, 0.7f, 0.2f)
                    : new Color(0.95f, 0.3f, 0.25f);
                GUI.DrawTexture(fill, Texture2D.whiteTexture);
            }
            GUI.color = Color.white;
        }
    }
}

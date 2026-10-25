using UnityEngine;

namespace SwarmPrototype
{
    // Builds the whole prototype from code when you press Play, so there is no
    // scene or prefab setup. Set AutoStart to false once real scenes exist.
    public static class Bootstrap
    {
        const bool AutoStart = true;
        const int DefaultPlayers = 4;

        static GameObject root;
        static MainMenu menu;
        static int players = DefaultPlayers;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void OnGameStart()
        {
            if (!AutoStart) return;
            // Headless runs (screenshots, tests) skip the menu.
            if (Application.isBatchMode) Build(DefaultPlayers);
            else ShowMenu();
        }

        public static void ShowMenu()
        {
            Teardown();
            Visuals.SetupCamera(EnsureCamera());
            menu = new GameObject("Main Menu").AddComponent<MainMenu>();
        }

        public static void Play(int count)
        {
            players = count;
            Build(players);
        }

        public static void Restart() => Build(players);

        static void Teardown()
        {
            if (menu != null) Object.Destroy(menu.gameObject);
            if (root != null)
            {
                // Deactivate first so everything leaves Combatant.All right away.
                root.SetActive(false);
                Object.Destroy(root);
            }
        }

        static void Build(int count)
        {
            Teardown();
            var config = GameConfig.Load();
            GameState.Reset(config, count);
            var layout = MapLayout.Build(GameState.TeamCount, config);
            float mapSize = layout.mapSize;
            GameState.MapSize = mapSize;
            root = new GameObject("Swarm Prototype");

            CreateGround(mapSize);
            Visuals.SetupLighting(root.transform);
            Visuals.CreateSurroundings(root.transform, mapSize);

            var spots = layout.bases;
            Visuals.CreateLanes(root.transform, config, layout);
            Visuals.Scatter(root.transform, config, layout);

            Vector3 up = Vector3.up * config.plateauHeight;
            var bases = new Spawner[GameState.TeamCount];
            for (int team = 0; team < GameState.TeamCount; team++)
            {
                BaseTerrain.CreatePlateau(spots[team], team, config, root.transform);
                bases[team] = Spawner.Create(team, spots[team] + up, config, root.transform);
                GameState.RegisterBase(bases[team]);
            }
            // Bake after the map and bases exist but before any unit or monster spawns.
            BaseTerrain.BakeNavMesh(root.transform, mapSize);
            Diagnostics.Create(config, spots, root.transform);

            Camp.All.Clear();
            foreach (var site in layout.camps) Camp.Create(config, site, root.transform);
            EliteSpawner.Create(config, layout, root.transform);

            for (int team = 1; team < GameState.TeamCount; team++)
            {
                var brain = new GameObject(GameState.TeamNames[team] + " Brain").AddComponent<EnemyBrain>();
                brain.transform.SetParent(root.transform, false);
                brain.Init(bases[team], bases, config);
            }

            var commander = new GameObject("Player Commander").AddComponent<PlayerCommander>();
            commander.transform.SetParent(root.transform, false);
            commander.Init(bases, CreateFlag(GameState.TeamColors[GameState.PlayerTeam]), layout);

            var cam = EnsureCamera();
            Visuals.SetupCamera(cam);
            var rts = cam.GetComponent<RtsCamera>();
            if (rts == null) rts = cam.gameObject.AddComponent<RtsCamera>();
            // Start looking straight at your own tower; E cycles through the enemy towers.
            var enemySpots = new Vector3[GameState.TeamCount - 1];
            for (int team = 1; team < GameState.TeamCount; team++) enemySpots[team - 1] = spots[team] + up;
            rts.SetHome(spots[GameState.PlayerTeam] + up, enemySpots, mapSize);
        }

        static void CreateGround(float size)
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.SetParent(root.transform, false);
            ground.transform.localScale = new Vector3(size / 10f, 1f, size / 10f);
            Visuals.PaintGround(ground.GetComponent<Renderer>(), size);
        }

        static Transform CreateFlag(Color color)
        {
            var flag = new GameObject("Rally Flag").transform;
            flag.SetParent(root.transform, false);

            var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(pole.GetComponent<Collider>());
            pole.transform.SetParent(flag, false);
            pole.transform.localPosition = new Vector3(0f, 1.5f, 0f);
            pole.transform.localScale = new Vector3(0.15f, 1.5f, 0.15f);
            pole.GetComponent<Renderer>().material.color = Color.white;

            var cloth = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(cloth.GetComponent<Collider>());
            cloth.transform.SetParent(flag, false);
            cloth.transform.localPosition = new Vector3(0.6f, 2.6f, 0f);
            cloth.transform.localScale = new Vector3(1.1f, 0.7f, 0.05f);
            cloth.GetComponent<Renderer>().material.color = color;
            return flag;
        }

        static Camera EnsureCamera()
        {
            if (Camera.main != null) return Camera.main;
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = go.AddComponent<Camera>();
            go.AddComponent<AudioListener>();
            return cam;
        }
    }
}

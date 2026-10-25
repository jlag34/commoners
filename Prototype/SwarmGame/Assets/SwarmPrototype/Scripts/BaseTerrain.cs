using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace SwarmPrototype
{
    // The raised ground each base sits on: a square plateau with a low wall around
    // the rim and a ramp in the middle of each side (north, south, east, west), so
    // the only ways in are four narrow entrances. Also bakes the NavMesh units path on.
    public static class BaseTerrain
    {
        // Tall enough that a unit squeezed against it can't pop over the top.
        const float WallHeight = 1.8f;
        const float WallThickness = 0.6f;
        const float RailThickness = 0.4f;

        static NavMeshDataInstance navMesh;
        const string FootName = "Plateau Foot";
        const int NotWalkableArea = 1;
        // Unity's built-in "Ignore Raycast" layer, reused so no layer setup is needed.
        // Our click raycasts pass an explicit all-layers mask, so clicks still hit buildings.
        public const int BuildingLayer = 2;

        // True if `point` is on a base's plateau, on one of its ramps, or within
        // `margin` of either. Used to keep camps and scenery out of bases.
        public static bool NearBase(Vector3 point, GameConfig config, Vector3[] bases, float margin)
        {
            float half = config.plateauSize * 0.5f + margin;
            float rampReach = config.plateauSize * 0.5f + config.rampLength + margin;
            float rampHalf = config.entranceWidth * 0.5f + margin;
            foreach (var b in bases)
            {
                float dx = Mathf.Abs(point.x - b.x), dz = Mathf.Abs(point.z - b.z);
                if (dx < half && dz < half) return true;
                if (dx < rampHalf && dz < rampReach) return true;
                if (dz < rampHalf && dx < rampReach) return true;
            }
            return false;
        }

        // Marks a building as a carving obstacle on the building layer. The obstacle
        // box matches the object's own scaled cube.
        public static void MakeObstacle(GameObject building)
        {
            building.layer = BuildingLayer;
            var obstacle = building.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.center = Vector3.zero;
            obstacle.size = Vector3.one;
            obstacle.carving = true;
            obstacle.carveOnlyStationary = true;
        }

        public static void CreatePlateau(Vector3 center, int team, GameConfig config, Transform parent)
        {
            float size = config.plateauSize;
            float height = config.plateauHeight;
            float half = size * 0.5f;

            var plateau = new GameObject("Plateau").transform;
            plateau.SetParent(parent, false);
            plateau.position = center;

            var cliff = Block("Cliff", plateau, new Vector3(0f, height * 0.5f, 0f), new Vector3(size, height, size), Visuals.CliffMaterial);
            cliff.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

            // The NavMesh voxelizer sees the cliff block as a hollow shell, so the
            // ground inside it (2.5 m of headroom) would bake as a hidden walkable
            // island that goals near the base snap onto. This invisible slab is
            // marked Not Walkable at bake time and kills that layer.
            var foot = Block(FootName, plateau, new Vector3(0f, 0.06f, 0f), new Vector3(size + 1f, 0.04f, size + 1f), Visuals.CliffMaterial);
            foot.GetComponent<Renderer>().enabled = false;

            // Grass on top, just above the block so it doesn't flicker.
            var top = GameObject.CreatePrimitive(PrimitiveType.Quad);
            top.name = "Plateau Top";
            Object.DestroyImmediate(top.GetComponent<Collider>());
            top.transform.SetParent(plateau, false);
            top.transform.localPosition = new Vector3(0f, height + 0.01f, 0f);
            top.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            top.transform.localScale = new Vector3(size, size, 1f);
            // Slightly warmer than the low ground so the raised area reads from above.
            var grass = Visuals.GrassMaterial(size);
            grass.color = new Color(1f, 0.97f, 0.82f);
            top.GetComponent<Renderer>().sharedMaterial = grass;

            float gap = config.entranceWidth;
            float segment = (size - gap) * 0.5f;
            for (int side = 0; side < 4; side++)
            {
                var rotation = Quaternion.Euler(0f, side * 90f, 0f);
                Vector3 outward = rotation * Vector3.forward;
                Vector3 along = rotation * Vector3.right;

                // Rim wall in two pieces with the entrance gap between them.
                foreach (float sign in new[] { -1f, 1f })
                {
                    Vector3 position = outward * (half - WallThickness * 0.5f) + along * sign * (gap * 0.5f + segment * 0.5f) + Vector3.up * (height + WallHeight * 0.5f);
                    var wall = Block("Wall", plateau, position, new Vector3(segment, WallHeight, WallThickness), Visuals.WallMaterial);
                    wall.transform.localRotation = rotation;
                }

                CreateRamp(plateau, outward, rotation, half, config);
            }

            // A secondary tower in each corner, set in from the wall so units can get round it.
            foreach (var corner in new[] { new Vector3(-1f, 0f, -1f), new Vector3(1f, 0f, -1f), new Vector3(1f, 0f, 1f), new Vector3(-1f, 0f, 1f) })
                Tower.Create(team, center + corner * (half - 2.2f) + Vector3.up * height, config.secondaryTower, plateau);
        }

        // A tilted slab from the ground up to the plateau edge, `entranceWidth` wide.
        static void CreateRamp(Transform plateau, Vector3 outward, Quaternion rotation, float half, GameConfig config)
        {
            float length = config.rampLength;
            float height = config.plateauHeight;
            const float thickness = 1f;
            float slope = Mathf.Atan2(height, length) * Mathf.Rad2Deg;
            float slant = Mathf.Sqrt(length * length + height * height);

            // Middle of the ramp's walking surface, then pushed down half its thickness.
            Vector3 surfaceMiddle = outward * (half + length * 0.5f) + Vector3.up * (height * 0.5f);
            Quaternion tilt = rotation * Quaternion.Euler(slope, 0f, 0f);
            Vector3 position = surfaceMiddle - (tilt * Vector3.up) * (thickness * 0.5f);
            // A little longer than the slope so it tucks under the plateau and the ground.
            var ramp = Block("Ramp", plateau, position, new Vector3(config.entranceWidth, thickness, slant + 0.6f), Visuals.RampMaterial);
            ramp.transform.localRotation = tilt;

            // Walls along both edges of the ramp, as tall as the rim wall, so a crowd
            // squeezing up under tower fire can't shove anyone off the side. (They'd
            // land at the cliff foot, climb again, get shoved again, forever.)
            Vector3 along = rotation * Vector3.right;
            Vector3 railUp = tilt * Vector3.up;
            foreach (float sign in new[] { -1f, 1f })
            {
                Vector3 railPosition = surfaceMiddle + along * sign * (config.entranceWidth * 0.5f + RailThickness * 0.5f) + railUp * (WallHeight * 0.5f);
                var rail = Block("Ramp Rail", plateau, railPosition, new Vector3(RailThickness, WallHeight, slant + 0.6f), Visuals.WallMaterial);
                rail.transform.localRotation = tilt;
            }
        }

        static GameObject Block(string name, Transform parent, Vector3 localPosition, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go;
        }

        // Bakes walkable ground from every collider under `root`. Call once the map
        // is built and before any units exist, so units don't get baked in as obstacles.
        public static void BakeNavMesh(Transform root, float mapSize)
        {
            if (navMesh.valid) NavMesh.RemoveNavMeshData(navMesh);

            var bounds = new Bounds(Vector3.zero, new Vector3(mapSize + 10f, 40f, mapSize + 10f));
            var sources = new List<NavMeshBuildSource>();
            var markups = new List<NavMeshBuildMarkup>();
            foreach (var t in root.GetComponentsInChildren<Transform>())
                if (t.name == FootName)
                    markups.Add(new NavMeshBuildMarkup { root = t, overrideArea = true, area = NotWalkableArea });
            // Buildings sit on BuildingLayer and carve the mesh instead, so the hole
            // they make disappears when they're destroyed.
            NavMeshBuilder.CollectSources(root, ~(1 << BuildingLayer), NavMeshCollectGeometry.PhysicsColliders, 0, markups, sources);

            var settings = NavMesh.GetSettingsByID(0);
            settings.agentRadius = 0.5f;
            settings.agentHeight = 2f;
            settings.agentSlope = 40f;
            settings.agentClimb = 0.4f;
            var data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
            navMesh = NavMesh.AddNavMeshData(data);
        }
    }
}

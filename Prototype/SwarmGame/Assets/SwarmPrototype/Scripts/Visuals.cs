using System.Collections.Generic;
using UnityEngine;

namespace SwarmPrototype
{
    // Look and feel only: lighting, ground, scenery and loading models from
    // Resources/SwarmModels. Nothing here touches gameplay. Every model is optional;
    // if one is missing the caller falls back to plain shapes.
    public static class Visuals
    {
        const string ModelFolder = "SwarmModels/";
        const float LaneHalfWidth = 14f;

        static readonly Color SunColor = new Color(1f, 0.93f, 0.8f);
        static readonly Color SkyColor = new Color(0.62f, 0.75f, 0.88f);
        static readonly Color FogColor = new Color(0.7f, 0.78f, 0.82f);

        static readonly System.Collections.Generic.Dictionary<(Texture, Color), Texture2D> teamTextures =
            new System.Collections.Generic.Dictionary<(Texture, Color), Texture2D>();

        static readonly System.Collections.Generic.Dictionary<Material, Material> texturedMaterials =
            new System.Collections.Generic.Dictionary<Material, Material>();

        static Texture2D grassTexture;
        static Material grassMaterial;
        static Material dirtMaterial;

        // Instantiates Resources/SwarmModels/<name>, or returns null if it isn't there.
        public static GameObject SpawnModel(string name, Transform parent)
        {
            var prefab = Resources.Load<GameObject>(ModelFolder + name);
            if (prefab == null) return null;
            var go = Object.Instantiate(prefab, parent, false);
            go.name = name;
            foreach (var c in go.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            AssignTextures(go);
            return go;
        }

        // The FBX files don't always carry their texture link, so materials pick up
        // Resources/SwarmModels/Textures/<material name> when it exists.
        static void AssignTextures(GameObject model)
        {
            foreach (var r in model.GetComponentsInChildren<Renderer>())
            {
                var materials = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    var m = materials[i];
                    if (m == null) continue;
                    // Copy rather than edit the imported material asset.
                    if (!texturedMaterials.TryGetValue(m, out var textured))
                    {
                        textured = m;
                        var texture = m.mainTexture == null ? Resources.Load<Texture2D>(ModelFolder + "Textures/" + m.name) : null;
                        if (texture != null) textured = new Material(m) { mainTexture = texture, color = Color.white };
                        else if (NatureColor(m.name, out Color natural)) textured = new Material(m) { color = natural };
                        texturedMaterials[m] = textured;
                    }
                    if (textured != m)
                    {
                        materials[i] = textured;
                        changed = true;
                    }
                }
                if (changed) r.sharedMaterials = materials;
            }
        }

        // The nature kit's stylized teal leaves clash with the grass, so swap in
        // warmer greens and browns by material name.
        static bool NatureColor(string materialName, out Color color)
        {
            string n = materialName.ToLowerInvariant();
            if (n.StartsWith("leafsdark")) color = new Color(0.2f, 0.4f, 0.17f);
            else if (n.StartsWith("leafsfall")) color = new Color(0.75f, 0.5f, 0.2f);
            else if (n.StartsWith("leafs")) color = new Color(0.32f, 0.55f, 0.22f);
            else if (n.StartsWith("grass")) color = new Color(0.38f, 0.6f, 0.24f);
            else if (n.StartsWith("woodbark")) color = new Color(0.45f, 0.32f, 0.22f);
            else if (n.StartsWith("wood")) color = new Color(0.55f, 0.4f, 0.27f);
            else if (n.StartsWith("stone")) color = new Color(0.45f, 0.44f, 0.42f);
            else
            {
                color = default;
                return false;
            }
            return true;
        }

        // Gives a model its team's colors: pixels in the texture whose hue is near
        // `sourceHue` (0-1) are shifted to the team's hue. `metalTint` also nudges
        // grey pixels toward the team color. The recolored copy is cached per team.
        public static void TeamColor(GameObject model, Color team, float sourceHue, float metalTint)
        {
            foreach (var r in model.GetComponentsInChildren<Renderer>())
            {
                var materials = r.materials;
                foreach (var m in materials)
                {
                    if (m.mainTexture == null) continue;
                    m.mainTexture = TeamTexture(m.mainTexture, team, sourceHue, metalTint);
                }
                r.materials = materials;
            }
        }

        static Texture2D TeamTexture(Texture source, Color team, float sourceHue, float metalTint)
        {
            if (teamTextures.TryGetValue((source, team), out var cached)) return cached;

            // Copy through the GPU so the texture doesn't need to be import-readable.
            var rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(source, rt);
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, true);
            copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);

            Color.RGBToHSV(team, out float teamHue, out float teamSat, out _);
            var pixels = copy.GetPixels();
            for (int i = 0; i < pixels.Length; i++)
            {
                Color c = pixels[i];
                Color.RGBToHSV(c, out float h, out float sat, out float val);
                float distance = Mathf.Abs(Mathf.DeltaAngle(h * 360f, sourceHue * 360f));
                if (sat > 0.3f && distance < 30f)
                {
                    Color shifted = Color.HSVToRGB(teamHue, Mathf.Max(sat, teamSat * 0.8f), val);
                    shifted.a = c.a;
                    pixels[i] = shifted;
                }
                else if (metalTint > 0f && sat < 0.15f && val > 0.3f)
                {
                    Color tinted = Color.Lerp(c, c * team * 1.6f, metalTint);
                    tinted.a = c.a;
                    pixels[i] = tinted;
                }
            }
            copy.SetPixels(pixels);
            copy.Apply(true);
            copy.wrapMode = source.wrapMode;
            copy.filterMode = source.filterMode;
            teamTextures[(source, team)] = copy;
            return copy;
        }

        public static AnimationClip[] LoadClips(string name) => Resources.LoadAll<AnimationClip>(ModelFolder + name);

        // Scales a model so its bounding box is `height` tall and sits on y = 0 of its parent.
        public static void FitHeight(GameObject model, float height)
        {
            var renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;
            Bounds bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            if (bounds.size.y < 0.0001f) return;
            float scale = height / bounds.size.y;
            float pivot = model.transform.localPosition.y;
            float bottom = model.transform.parent.InverseTransformPoint(bounds.center - Vector3.up * bounds.extents.y).y;
            model.transform.localScale *= scale;
            model.transform.localPosition -= new Vector3(0f, pivot + (bottom - pivot) * scale, 0f);
        }

        // Tints every material whose name matches one of `keys` (case-insensitive).
        // With no keys, tints everything.
        public static void Tint(GameObject model, Color color, float amount, params string[] keys)
        {
            foreach (var r in model.GetComponentsInChildren<Renderer>())
            {
                var materials = r.materials;
                foreach (var m in materials)
                {
                    if (!Matches(m.name, keys)) continue;
                    m.color = Color.Lerp(m.color, color, amount);
                }
                r.materials = materials;
            }
        }

        static bool Matches(string materialName, string[] keys)
        {
            if (keys.Length == 0) return true;
            string lower = materialName.ToLowerInvariant();
            foreach (var k in keys)
                if (lower.Contains(k)) return true;
            return false;
        }

        public static Material Plain(Color color)
        {
            var m = new Material(Shader.Find("Standard")) { color = color };
            m.SetFloat("_Glossiness", 0.15f);
            return m;
        }

        public static void SetupLighting(Transform root)
        {
            Light sun = Object.FindFirstObjectByType<Light>();
            if (sun == null)
            {
                sun = new GameObject("Sun").AddComponent<Light>();
                sun.transform.SetParent(root, false);
            }
            sun.type = LightType.Directional;
            sun.color = SunColor;
            sun.intensity = 1.15f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.75f;
            sun.shadowBias = 0.03f;
            sun.shadowNormalBias = 0.3f;
            sun.transform.rotation = Quaternion.Euler(48f, -35f, 0f);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.68f, 0.78f);
            RenderSettings.ambientEquatorColor = new Color(0.55f, 0.55f, 0.48f);
            RenderSettings.ambientGroundColor = new Color(0.3f, 0.28f, 0.22f);

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = FogColor;
            RenderSettings.fogStartDistance = 70f;
            RenderSettings.fogEndDistance = 190f;

            QualitySettings.shadowDistance = 140f;
            QualitySettings.shadows = ShadowQuality.All;
            QualitySettings.shadowResolution = ShadowResolution.High;
        }

        public static void SetupCamera(Camera cam)
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = FogColor;
            cam.farClipPlane = 400f;
            // Narrow lens, like Warcraft 3: less perspective skew, reads more top-down.
            cam.fieldOfView = 40f;
        }

        static Material cliffMaterial, wallMaterial, rampMaterial;

        public static Material CliffMaterial => cliffMaterial != null ? cliffMaterial : (cliffMaterial = Plain(new Color(0.42f, 0.38f, 0.33f)));
        public static Material WallMaterial => wallMaterial != null ? wallMaterial : (wallMaterial = Plain(new Color(0.72f, 0.68f, 0.6f)));
        public static Material RampMaterial => rampMaterial != null ? rampMaterial : (rampMaterial = Plain(new Color(0.58f, 0.47f, 0.33f)));

        // Grass for a surface `size` meters across, tiled the same as the ground.
        public static Material GrassMaterial(float size)
        {
            var m = Plain(Color.white);
            m.mainTexture = GrassTexture();
            m.mainTextureScale = Vector2.one * (size / 12f);
            return m;
        }

        public static void PaintGround(Renderer ground, float size)
        {
            if (grassMaterial == null)
            {
                grassMaterial = Plain(Color.white);
                grassMaterial.mainTexture = GrassTexture();
            }
            ground.sharedMaterial = grassMaterial;
            // About one texture tile per 12 m.
            grassMaterial.mainTextureScale = Vector2.one * (size / 12f);
        }

        // Wider ground under the play area so the map doesn't end in a void.
        public static void CreateSurroundings(Transform root, float mapSize)
        {
            var outer = GameObject.CreatePrimitive(PrimitiveType.Plane);
            outer.name = "Surroundings";
            Object.DestroyImmediate(outer.GetComponent<Collider>());
            outer.transform.SetParent(root, false);
            outer.transform.position = new Vector3(0f, -0.02f, 0f);
            float size = mapSize * 4f;
            outer.transform.localScale = new Vector3(size / 10f, 1f, size / 10f);
            var m = Plain(Color.white);
            m.mainTexture = GrassTexture();
            m.mainTextureScale = Vector2.one * (size / 12f);
            outer.GetComponent<Renderer>().sharedMaterial = m;
        }

        // Worn dirt paths between every pair of bases that line up on an axis, so a
        // 2 x 2 grid gets a ring of four roads joining the facing ramps.
        public static void CreateLanes(Transform root, GameConfig config, MapLayout layout)
        {
            if (dirtMaterial == null) dirtMaterial = Plain(new Color(0.55f, 0.45f, 0.32f));
            float clearance = config.plateauSize + config.rampLength * 2f - 1f;
            foreach (var (i, j) in layout.lanes)
                {
                    Vector3 a = layout.bases[i], b = layout.bases[j];
                    bool alongX = Mathf.Approximately(a.z, b.z);
                    float length = Vector3.Distance(a, b) - clearance;
                    if (length <= 0f) continue;
                    var lane = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    lane.name = "Lane";
                    Object.DestroyImmediate(lane.GetComponent<Collider>());
                    lane.transform.SetParent(root, false);
                    lane.transform.position = (a + b) * 0.5f + Vector3.up * 0.01f;
                    lane.transform.rotation = Quaternion.Euler(90f, alongX ? 90f : 0f, 0f);
                    lane.transform.localScale = new Vector3(config.entranceWidth + 1.5f, length, 1f);
                    var r = lane.GetComponent<Renderer>();
                    r.sharedMaterial = dirtMaterial;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
        }

        // True where scenery must not go: on a base and its ramps, on a lane, or in a camp.
        static bool Blocked(float x, float z, GameConfig config, MapLayout layout, float laneHalfWidth)
        {
            if (BaseTerrain.NearBase(new Vector3(x, 0f, z), config, layout.bases, 3f)) return true;
            foreach (var (i, j) in layout.lanes)
            {
                Vector3 a = layout.bases[i], b = layout.bases[j];
                if (Mathf.Approximately(a.z, b.z) && Mathf.Abs(z - a.z) < laneHalfWidth && x > Mathf.Min(a.x, b.x) && x < Mathf.Max(a.x, b.x)) return true;
                if (Mathf.Approximately(a.x, b.x) && Mathf.Abs(x - a.x) < laneHalfWidth && z > Mathf.Min(a.z, b.z) && z < Mathf.Max(a.z, b.z)) return true;
            }
            foreach (var camp in layout.camps)
                if ((x - camp.position.x) * (x - camp.position.x) + (z - camp.position.z) * (z - camp.position.z) < CampClear * CampClear) return true;
            return false;
        }

        const float CampClear = 11f;

        // Trees and rocks around the edges and either side of the lanes. They have
        // no colliders, so they never block units.
        public static void Scatter(Transform root, GameConfig config, MapLayout layout)
        {
            var parent = new GameObject("Scenery").transform;
            parent.SetParent(root, false);

            var random = new System.Random(7);
            float half = layout.mapSize * 0.5f;
            // Same density whatever the map size.
            float area = (layout.mapSize / 140f) * (layout.mapSize / 140f);
            int treeTarget = Mathf.RoundToInt(260 * area), decorTarget = Mathf.RoundToInt(320 * area);
            int placed = 0;
            for (int tries = 0; tries < 16 * treeTarget && placed < treeTarget; tries++)
            {
                float x = (float)(random.NextDouble() * 2.0 - 1.0) * (half + 30f);
                float z = (float)(random.NextDouble() * 2.0 - 1.0) * (half + 30f);
                if (Blocked(x, z, config, layout, LaneHalfWidth)) continue;
                // Thinner inside the map so fights stay readable.
                bool edge = Mathf.Abs(x) > half - 12f || Mathf.Abs(z) > half - 8f;
                if (!edge && random.NextDouble() < 0.6) continue;

                bool rock = random.NextDouble() < 0.25;
                var prop = rock ? Rock(parent, random) : Tree(parent, random);
                prop.transform.position = new Vector3(x, 0f, z);
                prop.transform.rotation = Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f);
                placed++;
            }

            // Small bushes, grass and flowers everywhere except right on a lane or a base.
            for (int i = 0; i < decorTarget; i++)
            {
                float x = (float)(random.NextDouble() * 2.0 - 1.0) * half;
                float z = (float)(random.NextDouble() * 2.0 - 1.0) * half;
                if (Blocked(x, z, config, layout, 4f)) continue;
                var model = SpawnModel(DecorModels[random.Next(DecorModels.Length)], parent);
                if (model == null) break;
                var holder = new GameObject("Decor").transform;
                holder.SetParent(parent, false);
                model.transform.SetParent(holder, false);
                FitHeight(model, 0.4f + (float)random.NextDouble() * 0.6f);
                holder.position = new Vector3(x, 0f, z);
                holder.rotation = Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f);
                foreach (var r in model.GetComponentsInChildren<Renderer>())
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        static readonly string[] TreeModels = { "tree_oak", "tree_default", "tree_fat", "tree_pineRoundA", "tree_pineTallA" };
        static readonly string[] RockModels = { "stone_largeA", "stone_largeB", "stone_tallA", "stone_smallA" };
        static readonly string[] DecorModels = { "plant_bush", "plant_bushLarge", "grass_large", "grass_large", "flower_yellowA" };

        static GameObject Tree(Transform parent, System.Random random)
        {
            var holder = new GameObject("Tree");
            holder.transform.SetParent(parent, false);
            float height = 5f + (float)random.NextDouble() * 4f;
            var model = SpawnModel(TreeModels[random.Next(TreeModels.Length)], holder.transform);
            if (model != null)
            {
                FitHeight(model, height);
                return holder;
            }

            var trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(trunk.GetComponent<Collider>());
            trunk.transform.SetParent(holder.transform, false);
            trunk.transform.localPosition = new Vector3(0f, height * 0.2f, 0f);
            trunk.transform.localScale = new Vector3(0.5f, height * 0.2f, 0.5f);
            trunk.GetComponent<Renderer>().sharedMaterial = Plain(new Color(0.42f, 0.3f, 0.2f));

            var crown = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(crown.GetComponent<Collider>());
            crown.transform.SetParent(holder.transform, false);
            crown.transform.localPosition = new Vector3(0f, height * 0.62f, 0f);
            crown.transform.localScale = new Vector3(height * 0.55f, height * 0.6f, height * 0.55f);
            crown.GetComponent<Renderer>().sharedMaterial = Plain(Color.Lerp(new Color(0.2f, 0.42f, 0.18f), new Color(0.35f, 0.5f, 0.2f), (float)random.NextDouble()));
            return holder;
        }

        static GameObject Rock(Transform parent, System.Random random)
        {
            var holder = new GameObject("Rock");
            holder.transform.SetParent(parent, false);
            float size = 1f + (float)random.NextDouble() * 1.8f;
            var model = SpawnModel(RockModels[random.Next(RockModels.Length)], holder.transform);
            if (model != null)
            {
                FitHeight(model, size);
                return holder;
            }

            var stone = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(stone.GetComponent<Collider>());
            stone.transform.SetParent(holder.transform, false);
            stone.transform.localPosition = new Vector3(0f, size * 0.3f, 0f);
            stone.transform.localScale = new Vector3(size * 1.4f, size * 0.8f, size);
            stone.GetComponent<Renderer>().sharedMaterial = Plain(new Color(0.55f, 0.55f, 0.52f));
            return holder;
        }

        static Texture2D GrassTexture()
        {
            if (grassTexture != null) return grassTexture;
            const int size = 256;
            grassTexture = new Texture2D(size, size, TextureFormat.RGB24, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            var dark = new Color(0.27f, 0.42f, 0.2f);
            var light = new Color(0.42f, 0.58f, 0.27f);
            var dry = new Color(0.55f, 0.56f, 0.3f);
            var random = new System.Random(3);
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Tileable noise: sample on a torus so the edges wrap.
                    float u = x / (float)size * Mathf.PI * 2f, v = y / (float)size * Mathf.PI * 2f;
                    float n = Mathf.PerlinNoise(Mathf.Cos(u) * 1.5f + 10f, Mathf.Sin(u) * 1.5f + Mathf.Cos(v) * 1.5f + 10f) * 0.6f
                            + Mathf.PerlinNoise(Mathf.Sin(v) * 4f + 20f, Mathf.Cos(u) * 4f + 20f) * 0.4f;
                    Color c = Color.Lerp(dark, light, n);
                    float patch = Mathf.PerlinNoise(Mathf.Cos(v) * 1.2f + 40f, Mathf.Sin(u) * 1.2f + 40f);
                    if (patch > 0.62f) c = Color.Lerp(c, dry, (patch - 0.62f) * 2f);
                    // Blade speckle.
                    c *= 0.9f + (float)random.NextDouble() * 0.2f;
                    pixels[y * size + x] = c;
                }
            }
            grassTexture.SetPixels(pixels);
            grassTexture.Apply(true);
            return grassTexture;
        }
    }
}

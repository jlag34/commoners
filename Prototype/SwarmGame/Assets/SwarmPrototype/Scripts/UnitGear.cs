using System.Collections.Generic;
using UnityEngine;

namespace SwarmPrototype
{
    // Visible gear on a unit's model that tracks the team's upgrade levels, so a
    // swarm looks as strong as it is. Tiers by level: 0, 1-5, 6-10, 11+, after the
    // concept board's peon progression (basic -> armored -> champion).
    //   Strength -> the weapon: a pickaxe at tier 0, then a bronze / steel / glowing gold sword.
    //   Armor    -> tier 1 leather set; tier 2 iron plate, kettle helmet and a round
    //               shield; tier 3 gilded, glowing, with a tall shield.
    //   Health   -> the whole model grows 5% per tier.
    //   Speed    -> tier 2+ kicks up dust while running.
    // Armor sets are skinned meshes built in Blender on the Warrior's own armature
    // (art/blender/build_armor.py -> Resources/SwarmModels/Armor_*.fbx). Each piece is
    // re-bound to this model's bones by name, so it follows the animations. If a set
    // is missing the old primitive armor is built instead. Weapons are still primitives
    // parented to the weapon bone.
    public class UnitGear : MonoBehaviour
    {
        static readonly int[] TierStarts = { 1, 6, 11 };
        static readonly string[] ArmorSets = { null, "Armor_Leather", "Armor_Iron", "Armor_Gilded" };
        static readonly string[] ShieldSets = { null, null, "Shield_Iron_Round", "Shield_Gilded_Tall" };
        // Ears and nose without hair, shown under helmets in place of the Face mesh.
        const string BareHead = "Warrior_Head_Bare";
        const string ModelFolder = "SwarmModels/";
        static readonly Dictionary<(string, int, Color), Material> styledMaterials = new Dictionary<(string, int, Color), Material>();

        static readonly Color[] SwordTint = { Color.white, new Color(1f, 0.78f, 0.5f), new Color(0.72f, 0.85f, 1f), new Color(1f, 0.85f, 0.3f) };
        static readonly float[] SwordGlow = { 0f, 0f, 0.25f, 0.7f };
        static readonly float[] SwordScale = { 1f, 1.12f, 1.25f, 1.4f };
        static readonly Color[] ArmorColor = { Color.clear, new Color(0.45f, 0.3f, 0.18f), new Color(0.62f, 0.64f, 0.68f), new Color(1f, 0.8f, 0.25f) };
        static readonly float[] ArmorGlow = { 0f, 0f, 0f, 0.35f };
        static readonly Color WoodColor = new Color(0.42f, 0.28f, 0.15f);
        static readonly Color IronColor = new Color(0.5f, 0.52f, 0.55f);

        Renderer sword;
        Transform weaponBone, weaponEnd, head, headEnd, shoulderL, shoulderR, lowerArmL, fistL, torso, neck;
        readonly Dictionary<string, Transform> bones = new Dictionary<string, Transform>();
        // Base-body parts that armor covers: the model's own shoulder pads, and the hair under a helmet.
        readonly List<Renderer> shoulderPads = new List<Renderer>();
        Renderer hair;
        Color team = new Color(0.1f, 0.33f, 0.85f);
        Vector3 weaponBoneScale = Vector3.one;
        Vector3 swordScale = Vector3.one;
        Vector3 baseScale;
        // Grip-to-tip direction of the sword at bind pose, for replacing it with other weapons.
        Vector3 swordAxis;
        Material swordMaterial;
        Color swordBase = Color.white;
        readonly System.Collections.Generic.List<GameObject> armorPieces = new System.Collections.Generic.List<GameObject>();
        readonly System.Collections.Generic.List<GameObject> weaponPieces = new System.Collections.Generic.List<GameObject>();
        int strengthTier = -1, armorTier = -1, healthTier = -1, speedTier = 0;
        Vector3 lastDustAt;
        float nextDustTime;

        public static int Tier(int level)
        {
            int tier = 0;
            foreach (int start in TierStarts) if (level >= start) tier++;
            return tier;
        }

        public static UnitGear Attach(GameObject model, Color team)
        {
            var gear = model.AddComponent<UnitGear>();
            gear.baseScale = model.transform.localScale;
            gear.team = team;
            foreach (var t in model.GetComponentsInChildren<Transform>(true))
            {
                if (!gear.bones.ContainsKey(t.name)) gear.bones[t.name] = t;
                switch (t.name)
                {
                    case "Weapon.R": gear.weaponBone = t; gear.weaponBoneScale = t.localScale; break;
                    case "Weapon.R_end": gear.weaponEnd = t; break;
                    case "Head": gear.head = t; break;
                    case "Head_end": gear.headEnd = t; break;
                    case "UpperArm.L": gear.shoulderL = t; break;
                    case "UpperArm.R": gear.shoulderR = t; break;
                    case "LowerArm.L": gear.lowerArmL = t; break;
                    case "Fist.L": gear.fistL = t; break;
                    case "Torso": gear.torso = t; break;
                    case "Neck": gear.neck = t; break;
                }
            }
            foreach (var r in model.GetComponentsInChildren<Renderer>())
            {
                if (gear.sword == null && r.name.ToLowerInvariant().Contains("sword")) gear.sword = r;
                else if (r.name.StartsWith("ShoulderPad")) gear.shoulderPads.Add(r);
                else if (r.name == "Face") gear.hair = r;
            }
            if (gear.sword != null)
            {
                gear.swordScale = gear.sword.transform.localScale;
                // Instance so tinting one unit's sword doesn't tint them all.
                gear.swordMaterial = gear.sword.material;
                gear.swordBase = gear.swordMaterial.HasProperty("_Color") ? gear.swordMaterial.color : Color.white;
                if (gear.weaponBone != null) gear.swordAxis = gear.sword.bounds.center - gear.weaponBone.position;
            }
            gear.lastDustAt = model.transform.position;
            return gear;
        }

        public void Apply(int strengthLevel, int armorLevel, int healthLevel, int speedLevel = 0)
        {
            int s = Tier(strengthLevel), a = Tier(armorLevel), h = Tier(healthLevel);
            if (s != strengthTier) ApplySword(s);
            if (a != armorTier) ApplyArmor(a);
            if (h != healthTier) ApplyHealth(h);
            speedTier = Tier(speedLevel);
        }

        void Update()
        {
            if (speedTier < 2 || Time.time < nextDustTime) return;
            Vector3 here = transform.position;
            if ((here - lastDustAt).sqrMagnitude < 0.6f * 0.6f) return;
            lastDustAt = here;
            nextDustTime = Time.time + (speedTier >= 3 ? 0.12f : 0.2f);
            Fx.Dust(here, transform.parent != null ? transform.parent.parent : null);
        }

        // Tier 0 swaps the sword for a pickaxe: a basic peon fresh off the farm.
        void ApplySword(int tier)
        {
            strengthTier = tier;
            foreach (var piece in weaponPieces) Destroy(piece);
            weaponPieces.Clear();
            if (sword != null) sword.enabled = tier > 0;
            if (tier == 0)
            {
                BuildPickaxe();
                return;
            }
            if (swordMaterial != null)
            {
                swordMaterial.color = swordBase * SwordTint[tier];
                if (SwordGlow[tier] > 0f)
                {
                    swordMaterial.EnableKeyword("_EMISSION");
                    swordMaterial.SetColor("_EmissionColor", SwordTint[tier] * SwordGlow[tier]);
                }
                else swordMaterial.DisableKeyword("_EMISSION");
            }
            // A plain mesh sword can be scaled directly (animation never touches it).
            // A skinned one has to be scaled through the weapon bone instead.
            if (sword is MeshRenderer) sword.transform.localScale = swordScale * SwordScale[tier];
            else if (weaponBone != null) weaponBone.localScale = weaponBoneScale * SwordScale[tier];
        }

        void BuildPickaxe()
        {
            if (weaponBone == null) return;
            if (weaponBone.localScale != weaponBoneScale) weaponBone.localScale = weaponBoneScale;
            var wood = Visuals.Plain(WoodColor);
            var iron = Visuals.Plain(IronColor);
            var frame = WeaponFrame.From(sword, weaponBone);
            if (frame != null)
            {
                // Built in the sword's own space, so it sits in the hand exactly like the sword
                // did. The warrior idles blade-down, so the pick runs the other way: handle
                // through the fist, head up behind the shoulder, like a peon carrying it.
                weaponPieces.Add(frame.Bar(-0.25f, 0f, 0.8f, 0.06f, wood));
                weaponPieces.Add(frame.Bar(-0.55f, 0f, 0.1f, 0.09f, iron, acrossLength: 0.42f));
                weaponPieces.Add(frame.Bar(-0.55f, 0.3f, 0.07f, 0.07f, iron, acrossLength: 0.12f));
                return;
            }
            // No sword mesh to borrow a frame from: a plain stick in the hand.
            weaponPieces.Add(Piece(PrimitiveType.Cylinder, weaponBone, Vector3.zero, new Vector3(0.06f, 0.4f, 0.06f), wood));
        }

        void ApplyArmor(int tier)
        {
            armorTier = tier;
            foreach (var piece in armorPieces) Destroy(piece);
            armorPieces.Clear();
            foreach (var pad in shoulderPads) pad.enabled = tier == 0;
            if (hair != null) hair.enabled = true;
            if (tier == 0) return;
            if (!Dress(ArmorSets[tier], tier))
            {
                BuildPrimitiveArmor(tier);
                return;
            }
            if (ShieldSets[tier] != null) Dress(ShieldSets[tier], tier);
            // Helmets hide the hair but keep ears and nose, textured like the face they replace.
            if (tier >= 2 && hair != null && Dress(BareHead, tier, hair.sharedMaterials)) hair.enabled = false;
        }

        // Adds every skinned mesh in Resources/SwarmModels/<setName> to this model, bound to
        // its bones of the same names. Returns false if the set is missing or fits no bone.
        bool Dress(string setName, int tier, Material[] materials = null)
        {
            var prefab = Resources.Load<GameObject>(ModelFolder + setName);
            if (prefab == null) return false;
            bool any = false;
            foreach (var source in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var sourceBones = source.bones;
                var mapped = new Transform[sourceBones.Length];
                bool ok = sourceBones.Length > 0;
                for (int i = 0; i < sourceBones.Length && ok; i++)
                    ok = sourceBones[i] != null && bones.TryGetValue(sourceBones[i].name, out mapped[i]);
                if (!ok)
                {
                    Debug.LogWarning($"UnitGear: {setName}/{source.name} uses bones this rig lacks");
                    continue;
                }
                var piece = new GameObject(source.name);
                piece.transform.SetParent(transform, false);
                var r = piece.AddComponent<SkinnedMeshRenderer>();
                r.sharedMesh = source.sharedMesh;
                r.bones = mapped;
                r.rootBone = source.rootBone != null && bones.TryGetValue(source.rootBone.name, out var root) ? root : mapped[0];
                r.localBounds = source.localBounds;
                r.sharedMaterials = materials ?? Styled(source.sharedMaterials, tier);
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                armorPieces.Add(piece);
                any = true;
            }
            return any;
        }

        // The FBX materials only carry a name; the look is set here so the whole swarm
        // shares one material per (name, tier, team). Team_Blue takes the team color.
        Material[] Styled(Material[] imported, int tier)
        {
            var result = new Material[imported.Length];
            for (int i = 0; i < imported.Length; i++)
            {
                string name = imported[i] != null ? imported[i].name : "";
                var key = (name, tier, team);
                if (!styledMaterials.TryGetValue(key, out var m))
                {
                    m = Style(name, tier, team) ?? imported[i];
                    styledMaterials[key] = m;
                }
                result[i] = m;
            }
            return result;
        }

        static Material Style(string name, int tier, Color team)
        {
            Color color;
            float metallic = 0f, gloss = 0.25f;
            Color glow = Color.black;
            switch (name)
            {
                case "Leather_Brown": color = new Color(0.64f, 0.36f, 0.17f); gloss = 0.3f; break;
                case "Leather_Dark": color = new Color(0.38f, 0.2f, 0.1f); gloss = 0.25f; break;
                case "Wood_Brown": color = new Color(0.64f, 0.41f, 0.2f); gloss = 0.25f; break;
                case "Metal_Iron": color = new Color(0.8f, 0.82f, 0.86f); metallic = 0.3f; gloss = 0.55f; break;
                case "Metal_Dark": color = new Color(0.46f, 0.48f, 0.53f); metallic = 0.3f; gloss = 0.45f; break;
                case "Metal_Gold": color = new Color(1f, 0.74f, 0.18f); metallic = 0.35f; gloss = 0.6f; glow = color * ArmorGlow[Mathf.Clamp(tier, 0, 3)]; break;
                case "Team_Blue": color = team; gloss = 0.3f; break;
                default: return null;
            }
            var m = Visuals.Plain(color);
            m.name = name;
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Glossiness", gloss);
            if (glow.maxColorComponent > 0f)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", glow);
            }
            return m;
        }

        // The primitive armor from before the Blender sets, kept for when a set fails to load.
        void BuildPrimitiveArmor(int tier)
        {

            var material = Visuals.Plain(ArmorColor[tier]);
            if (ArmorGlow[tier] > 0f)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", ArmorColor[tier] * ArmorGlow[tier]);
            }
            float size = 0.3f + 0.04f * tier;
            // Pauldrons: rounded leather at tier 1, squarer plate above.
            var padType = tier == 1 ? PrimitiveType.Sphere : PrimitiveType.Cube;
            Vector3 padSize = tier == 1 ? new Vector3(size, size * 0.8f, size) : new Vector3(size * 0.95f, size * 0.55f, size * 0.95f);
            if (shoulderL != null) armorPieces.Add(Piece(padType, shoulderL, Vector3.zero, padSize, material));
            if (shoulderR != null) armorPieces.Add(Piece(padType, shoulderR, Vector3.zero, padSize, material));
            // Chest piece between the torso bone and the neck.
            if (torso != null)
            {
                Vector3 top = neck != null ? neck.position : torso.position + torso.up * 0.4f;
                Vector3 centre = Vector3.Lerp(torso.position, top, 0.5f);
                float w = 0.4f + 0.02f * tier;
                var chest = Piece(PrimitiveType.Cube, torso, centre, new Vector3(w, 0.26f, 0.28f + 0.01f * tier), material);
                chest.transform.rotation = torso.rotation;
                armorPieces.Add(chest);
            }
            if (tier >= 2 && head != null)
            {
                // A dome helmet over the hair: centred a little above the head bone's tip,
                // wide enough to cover the hair mesh.
                Vector3 up = (headEnd != null ? (headEnd.position - head.position) : head.up).normalized;
                Vector3 top = headEnd != null ? headEnd.position : head.position + up * 0.25f;
                Vector3 centre = top - up * 0.02f;
                float cap = 0.46f + 0.04f * tier;
                armorPieces.Add(Piece(PrimitiveType.Sphere, head, centre, new Vector3(cap, cap * 0.8f, cap), material));
            }
            if (tier >= 2 && lowerArmL != null) BuildShield(tier, material);
        }

        // Round shield on the left forearm at tier 2, a tall tower shield at tier 3.
        void BuildShield(int tier, Material material)
        {
            Vector3 along = fistL != null ? (fistL.position - lowerArmL.position) : lowerArmL.up;
            if (along.sqrMagnitude < 0.0001f) along = Vector3.up;
            along.Normalize();
            Vector3 normal = Vector3.Cross(along, Vector3.up);
            if (normal.sqrMagnitude < 0.01f) normal = Vector3.Cross(along, Vector3.forward);
            normal.Normalize();
            Vector3 centre = lowerArmL.position + along * 0.12f + normal * 0.2f;
            var wood = Visuals.Plain(WoodColor);
            if (tier == 2)
            {
                var disc = Piece(PrimitiveType.Cylinder, lowerArmL, centre, new Vector3(0.62f, 0.03f, 0.62f), wood);
                disc.transform.rotation = Quaternion.FromToRotation(Vector3.up, normal);
                var boss = Piece(PrimitiveType.Cylinder, lowerArmL, centre + normal * 0.03f, new Vector3(0.22f, 0.03f, 0.22f), material);
                boss.transform.rotation = disc.transform.rotation;
                armorPieces.Add(disc);
                armorPieces.Add(boss);
            }
            else
            {
                var plate = Piece(PrimitiveType.Cube, lowerArmL, centre, new Vector3(0.55f, 0.95f, 0.06f), material);
                plate.transform.rotation = Quaternion.LookRotation(normal, Vector3.up);
                var trim = Piece(PrimitiveType.Cube, lowerArmL, centre + normal * 0.035f, new Vector3(0.14f, 0.7f, 0.02f), wood);
                trim.transform.rotation = plate.transform.rotation;
                armorPieces.Add(plate);
                armorPieces.Add(trim);
            }
        }

        // A primitive of `worldSize` metres parented to `bone` at world position `at` (zero = the bone itself).
        static GameObject Piece(PrimitiveType type, Transform bone, Vector3 at, Vector3 worldSize, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = "Gear";
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(bone, false);
            if (at != Vector3.zero) go.transform.position = at;
            Vector3 lossy = bone.lossyScale;
            go.transform.localScale = new Vector3(worldSize.x / Mathf.Max(0.0001f, lossy.x), worldSize.y / Mathf.Max(0.0001f, lossy.y), worldSize.z / Mathf.Max(0.0001f, lossy.z));
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            return go;
        }

        void ApplyHealth(int tier)
        {
            healthTier = tier;
            transform.localScale = baseScale * (1f + 0.05f * tier);
        }
    }
}

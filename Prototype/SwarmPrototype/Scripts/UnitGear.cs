using UnityEngine;

namespace SwarmPrototype
{
    // Visible gear on a unit's model that tracks the team's upgrade levels, so a
    // swarm looks as strong as it is. Tiers by level: 0, 1-5, 6-10, 11+, after the
    // concept board's peon progression (basic -> armored -> champion).
    //   Strength -> the weapon: a pickaxe at tier 0, then a bronze / steel / glowing gold sword.
    //   Armor    -> tier 1 leather chest and pauldrons; tier 2 iron plate, helmet and a round
    //               shield; tier 3 gilded, glowing, with a tower shield.
    //   Health   -> the whole model grows 5% per tier.
    //   Speed    -> tier 2+ kicks up dust while running.
    // Pieces are primitives parented to the model's bones so they animate along.
    public class UnitGear : MonoBehaviour
    {
        static readonly int[] TierStarts = { 1, 6, 11 };

        static readonly Color[] SwordTint = { Color.white, new Color(1f, 0.78f, 0.5f), new Color(0.72f, 0.85f, 1f), new Color(1f, 0.85f, 0.3f) };
        static readonly float[] SwordGlow = { 0f, 0f, 0.25f, 0.7f };
        static readonly float[] SwordScale = { 1f, 1.12f, 1.25f, 1.4f };
        static readonly Color[] ArmorColor = { Color.clear, new Color(0.45f, 0.3f, 0.18f), new Color(0.62f, 0.64f, 0.68f), new Color(1f, 0.8f, 0.25f) };
        static readonly float[] ArmorGlow = { 0f, 0f, 0f, 0.35f };
        static readonly Color WoodColor = new Color(0.42f, 0.28f, 0.15f);
        static readonly Color IronColor = new Color(0.5f, 0.52f, 0.55f);

        Renderer sword;
        Transform weaponBone, weaponEnd, head, headEnd, shoulderL, shoulderR, lowerArmL, fistL, torso, neck;
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

        public static UnitGear Attach(GameObject model)
        {
            var gear = model.AddComponent<UnitGear>();
            gear.baseScale = model.transform.localScale;
            foreach (var t in model.GetComponentsInChildren<Transform>(true))
            {
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
                if (r.name.ToLowerInvariant().Contains("sword")) { gear.sword = r; break; }
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
            if (tier == 0) return;

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

using UnityEngine;

namespace SwarmPrototype
{
    // Gives each elite a silhouette of its own on top of the shared warrior model,
    // after the concept board: a leafy canopy for the treant, extra heads for the
    // hydra, lightning for the ogre, a stone body with a glowing core for the golem,
    // a hooded staff-carrier for the shaman and a horned, purple-bladed dark knight.
    // Picked by name, falling back to abilities, so config renames keep working.
    // Sizes are in metres for a 1x unit and scale up with the elite.
    public static class EliteVisual
    {
        public static void Dress(GameObject model, EliteConfig config, Color tint, Transform fxParent)
        {
            var b = new Bones(model, Mathf.Max(0.5f, config.scale));
            string name = (config.name ?? "").ToLowerInvariant();
            var abilities = config.abilities ?? new string[0];
            bool has(string a) => System.Array.IndexOf(abilities, a) >= 0;

            if (name.Contains("treant") || (has("heal") && has("knockback"))) Treant(b, tint);
            else if (name.Contains("hydra") || (has("poison") && !has("heal"))) Hydra(b, tint);
            else if (name.Contains("ogre") || has("stun")) Ogre(b, model, fxParent);
            else if (name.Contains("golem") || has("splash")) Golem(b, model);
            else if (name.Contains("shaman") || (has("poison") && has("heal"))) Shaman(b, model, tint, fxParent);
            else if (name.Contains("knight") || name.Contains("headsman") || has("execute")) DarkKnight(b, model);
        }

        static void Treant(Bones b, Color tint)
        {
            var bark = Visuals.Plain(new Color(0.36f, 0.24f, 0.12f));
            var leaf = Visuals.Plain(new Color(0.3f, 0.6f, 0.2f));
            var leafDark = Visuals.Plain(new Color(0.2f, 0.45f, 0.15f));
            if (b.head != null)
            {
                Vector3 top = b.HeadTop;
                b.Add(PrimitiveType.Cylinder, b.head, top, new Vector3(0f, 0.2f, 0f), new Vector3(0.22f, 0.3f, 0.22f), bark);
                b.Add(PrimitiveType.Sphere, b.head, top, new Vector3(0f, 0.45f, 0f), new Vector3(1.3f, 0.9f, 1.3f), leaf);
                b.Add(PrimitiveType.Sphere, b.head, top, new Vector3(0.4f, 0.6f, 0.1f), new Vector3(0.8f, 0.7f, 0.8f), leafDark);
                b.Add(PrimitiveType.Sphere, b.head, top, new Vector3(-0.45f, 0.55f, -0.1f), new Vector3(0.75f, 0.65f, 0.75f), leafDark);
            }
            // Branch stubs off the shoulders.
            if (b.shoulderL != null) b.Add(PrimitiveType.Cube, b.shoulderL, b.shoulderL.position, new Vector3(-0.2f, 0.15f, 0f), new Vector3(0.55f, 0.28f, 0.28f), bark);
            if (b.shoulderR != null) b.Add(PrimitiveType.Cube, b.shoulderR, b.shoulderR.position, new Vector3(0.2f, 0.15f, 0f), new Vector3(0.55f, 0.28f, 0.28f), bark);
            b.HideSword();
        }

        static void Hydra(Bones b, Color tint)
        {
            var scale = Visuals.Plain(Color.Lerp(tint, Color.black, 0.2f));
            var eye = Visuals.Plain(new Color(1f, 0.9f, 0.2f));
            eye.EnableKeyword("_EMISSION");
            eye.SetColor("_EmissionColor", new Color(1f, 0.8f, 0.1f) * 0.8f);
            // Two extra heads rearing up and out from the shoulders.
            foreach (var shoulder in new[] { b.shoulderL, b.shoulderR })
            {
                if (shoulder == null) continue;
                float side = shoulder == b.shoulderL ? -1f : 1f;
                var neck = b.Add(PrimitiveType.Capsule, shoulder, shoulder.position, new Vector3(side * 0.22f, 0.4f, 0.05f), new Vector3(0.24f, 0.5f, 0.24f), scale);
                neck.transform.rotation = Quaternion.Euler(0f, 0f, side * -30f);
                Vector3 headOff = new Vector3(side * 0.45f, 0.8f, 0.15f);
                b.Add(PrimitiveType.Sphere, shoulder, shoulder.position, headOff, new Vector3(0.4f, 0.34f, 0.5f), scale);
                b.Add(PrimitiveType.Sphere, shoulder, shoulder.position, headOff + new Vector3(side * 0.08f, 0.08f, 0.2f), Vector3.one * 0.09f, eye);
                b.Add(PrimitiveType.Sphere, shoulder, shoulder.position, headOff + new Vector3(side * -0.08f, 0.08f, 0.2f), Vector3.one * 0.09f, eye);
            }
            b.HideSword();
        }

        static void Ogre(Bones b, GameObject model, Transform fxParent)
        {
            var hide = Visuals.Plain(new Color(0.35f, 0.22f, 0.12f));
            var tusk = Visuals.Plain(new Color(0.9f, 0.88f, 0.8f));
            // A loincloth band, two tusks and lightning crackling around the body.
            if (b.torso != null && b.neck != null)
                b.Add(PrimitiveType.Cube, b.torso, Vector3.Lerp(b.torso.position, b.neck.position, 0.15f), Vector3.zero, new Vector3(0.62f, 0.16f, 0.42f), hide);
            if (b.head != null)
            {
                Vector3 jaw = b.head.position;
                Vector3 fwd = b.Forward;
                b.Add(PrimitiveType.Cube, b.head, jaw, fwd * 0.2f + new Vector3(0.09f, 0.08f, 0f), new Vector3(0.06f, 0.18f, 0.06f), tusk);
                b.Add(PrimitiveType.Cube, b.head, jaw, fwd * 0.2f + new Vector3(-0.09f, 0.08f, 0f), new Vector3(0.06f, 0.18f, 0.06f), tusk);
            }
            Aura.Attach(model, new Color(0.5f, 0.7f, 1f), 0.25f, 0.2f * b.k, 1.6f, b.k, fxParent, sparks: true);
        }

        static void Golem(Bones b, GameObject model)
        {
            var stone = Visuals.Plain(new Color(0.45f, 0.47f, 0.5f));
            var core = Visuals.Plain(new Color(1f, 0.55f, 0.15f));
            core.EnableKeyword("_EMISSION");
            core.SetColor("_EmissionColor", new Color(1f, 0.45f, 0.1f) * 1.2f);
            // Boxy stone body: slab pauldrons, a block helmet, cannon forearms and a glowing core.
            if (b.shoulderL != null) b.Add(PrimitiveType.Cube, b.shoulderL, b.shoulderL.position, Vector3.zero, new Vector3(0.6f, 0.42f, 0.6f), stone);
            if (b.shoulderR != null) b.Add(PrimitiveType.Cube, b.shoulderR, b.shoulderR.position, Vector3.zero, new Vector3(0.6f, 0.42f, 0.6f), stone);
            if (b.head != null) b.Add(PrimitiveType.Cube, b.head, b.HeadTop, new Vector3(0f, -0.12f, 0f), new Vector3(0.62f, 0.5f, 0.62f), stone);
            foreach (var arm in new[] { b.lowerArmL, b.lowerArmR })
            {
                if (arm == null) continue;
                Transform fist = arm == b.lowerArmL ? b.fistL : b.fistR;
                Vector3 along = fist != null ? (fist.position - arm.position) : arm.up;
                if (along.sqrMagnitude < 0.0001f) along = Vector3.up;
                var barrel = b.Add(PrimitiveType.Cylinder, arm, arm.position, along.normalized * 0.22f, new Vector3(0.34f, 0.26f, 0.34f), stone);
                barrel.transform.rotation = Quaternion.FromToRotation(Vector3.up, along.normalized);
            }
            if (b.torso != null && b.neck != null)
                b.Add(PrimitiveType.Sphere, b.torso, Vector3.Lerp(b.torso.position, b.neck.position, 0.5f), b.Forward * 0.17f, Vector3.one * 0.3f, core);
            b.HideSword();
            Visuals.Tint(model, new Color(0.5f, 0.52f, 0.55f), 0.6f);
        }

        static void Shaman(Bones b, GameObject model, Color tint, Transform fxParent)
        {
            var cloth = Visuals.Plain(Color.Lerp(tint, Color.black, 0.45f));
            var wood = Visuals.Plain(new Color(0.35f, 0.22f, 0.1f));
            var gem = Visuals.Plain(new Color(0.5f, 1f, 0.4f));
            gem.EnableKeyword("_EMISSION");
            gem.SetColor("_EmissionColor", new Color(0.4f, 1f, 0.3f) * 1.2f);
            // Hood, shoulder cloak and a staff with a glowing gem in place of the sword.
            if (b.head != null) b.Add(PrimitiveType.Sphere, b.head, b.HeadTop, new Vector3(0f, -0.04f, 0f), new Vector3(0.58f, 0.5f, 0.58f), cloth);
            if (b.shoulderL != null) b.Add(PrimitiveType.Sphere, b.shoulderL, b.shoulderL.position, Vector3.zero, new Vector3(0.36f, 0.3f, 0.36f), cloth);
            if (b.shoulderR != null) b.Add(PrimitiveType.Sphere, b.shoulderR, b.shoulderR.position, Vector3.zero, new Vector3(0.36f, 0.3f, 0.36f), cloth);
            b.HideSword();
            var frame = WeaponFrame.From(b.sword, b.weapon);
            if (frame != null)
            {
                // Blade axis points down at idle, so the staff rises the other way.
                frame.Bar(-0.45f * b.k, 0f, 1.3f * b.k, 0.06f * b.k, wood);
                frame.Ball(-1.15f * b.k, 0f, 0.26f * b.k, gem);
            }
            Aura.Attach(model, new Color(0.45f, 1f, 0.35f), 0.35f, 0.4f * b.k, 0.5f, b.k, fxParent, sparks: false);
        }

        static void DarkKnight(Bones b, GameObject model)
        {
            var plate = Visuals.Plain(new Color(0.16f, 0.14f, 0.2f));
            var purple = Visuals.Plain(new Color(0.7f, 0.35f, 1f));
            purple.EnableKeyword("_EMISSION");
            purple.SetColor("_EmissionColor", new Color(0.6f, 0.25f, 1f) * 1.4f);
            // Full plate: square pauldrons, chest, a closed helm with two horns, and a glowing purple blade.
            if (b.shoulderL != null) b.Add(PrimitiveType.Cube, b.shoulderL, b.shoulderL.position, Vector3.zero, new Vector3(0.42f, 0.26f, 0.42f), plate);
            if (b.shoulderR != null) b.Add(PrimitiveType.Cube, b.shoulderR, b.shoulderR.position, Vector3.zero, new Vector3(0.42f, 0.26f, 0.42f), plate);
            if (b.torso != null && b.neck != null)
            {
                var chest = b.Add(PrimitiveType.Cube, b.torso, Vector3.Lerp(b.torso.position, b.neck.position, 0.5f), Vector3.zero, new Vector3(0.44f, 0.28f, 0.3f), plate);
                chest.transform.rotation = b.torso.rotation;
            }
            if (b.head != null)
            {
                Vector3 top = b.HeadTop;
                b.Add(PrimitiveType.Sphere, b.head, top, new Vector3(0f, -0.04f, 0f), new Vector3(0.54f, 0.46f, 0.54f), plate);
                var hornL = b.Add(PrimitiveType.Cube, b.head, top, new Vector3(-0.22f, 0.22f, 0f), new Vector3(0.08f, 0.4f, 0.08f), plate);
                hornL.transform.rotation = Quaternion.Euler(0f, 0f, 25f);
                var hornR = b.Add(PrimitiveType.Cube, b.head, top, new Vector3(0.22f, 0.22f, 0f), new Vector3(0.08f, 0.4f, 0.08f), plate);
                hornR.transform.rotation = Quaternion.Euler(0f, 0f, -25f);
                // Glowing eye slit on the face.
                b.Add(PrimitiveType.Cube, b.head, b.head.position, b.Forward * 0.24f + new Vector3(0f, 0.06f, 0f), new Vector3(0.3f, 0.04f, 0.04f), purple);
            }
            if (b.sword != null)
            {
                var m = b.sword.material;
                m.color = new Color(0.3f, 0.15f, 0.45f);
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", new Color(0.6f, 0.25f, 1f) * 1.2f);
                if (b.sword is MeshRenderer) b.sword.transform.localScale *= 1.5f;
                else if (b.weapon != null) b.weapon.localScale *= 1.5f;
            }
        }

        // Bone lookup plus the shared piece builder. `k` scales metre sizes and offsets to the elite.
        class Bones
        {
            public readonly float k;
            public Transform head, headEnd, neck, torso, shoulderL, shoulderR, lowerArmL, lowerArmR, fistL, fistR, weapon, weaponEnd;
            public Renderer sword;
            readonly Transform model;

            public Bones(GameObject model, float k)
            {
                this.k = k;
                this.model = model.transform;
                foreach (var t in model.GetComponentsInChildren<Transform>(true))
                {
                    switch (t.name)
                    {
                        case "Head": head = t; break;
                        case "Head_end": headEnd = t; break;
                        case "Neck": neck = t; break;
                        case "Torso": torso = t; break;
                        case "UpperArm.L": shoulderL = t; break;
                        case "UpperArm.R": shoulderR = t; break;
                        case "LowerArm.L": lowerArmL = t; break;
                        case "LowerArm.R": lowerArmR = t; break;
                        case "Fist.L": fistL = t; break;
                        case "Fist.R": fistR = t; break;
                        case "Weapon.R": weapon = t; break;
                        case "Weapon.R_end": weaponEnd = t; break;
                    }
                }
                foreach (var r in model.GetComponentsInChildren<Renderer>())
                    if (r.name.ToLowerInvariant().Contains("sword")) { sword = r; break; }
            }

            public Vector3 HeadTop => headEnd != null ? headEnd.position : head.position + head.up * 0.25f * k;
            // The way the unit faces.
            public Vector3 Forward => model.forward;

            public void HideSword() { if (sword != null) sword.enabled = false; }

            // A primitive on `bone` at `anchor` (a bone position) plus `offset` metres (scaled by k),
            // `size` metres at 1x (scaled by k).
            public GameObject Add(PrimitiveType type, Transform bone, Vector3 anchor, Vector3 offset, Vector3 size, Material material)
            {
                var go = GameObject.CreatePrimitive(type);
                go.name = "Elite Gear";
                Object.Destroy(go.GetComponent<Collider>());
                go.transform.SetParent(bone, false);
                go.transform.position = anchor + offset * k;
                Vector3 lossy = bone.lossyScale;
                Vector3 worldSize = size * k;
                go.transform.localScale = new Vector3(worldSize.x / Mathf.Max(0.0001f, lossy.x), worldSize.y / Mathf.Max(0.0001f, lossy.y), worldSize.z / Mathf.Max(0.0001f, lossy.z));
                var r = go.GetComponent<Renderer>();
                r.sharedMaterial = material;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                return go;
            }
        }

        // Puffs that keep rising off a model: lightning sparks or plague fumes.
        class Aura : MonoBehaviour
        {
            Color color;
            float interval, size, rise, k;
            bool sparks;
            Transform fxParent;
            float next;

            public static void Attach(GameObject model, Color color, float interval, float size, float rise, float k, Transform fxParent, bool sparks)
            {
                var a = model.AddComponent<Aura>();
                a.color = color;
                a.interval = interval;
                a.size = size;
                a.rise = rise;
                a.k = k;
                a.sparks = sparks;
                a.fxParent = fxParent;
            }

            void Update()
            {
                if (Time.time < next) return;
                next = Time.time + interval * Random.Range(0.6f, 1.4f);
                Vector2 spread = Random.insideUnitCircle * 0.5f * k;
                Vector3 at = transform.position + new Vector3(spread.x, Random.Range(0.4f, 1.4f) * k, spread.y);
                Vector3 drift = sparks ? new Vector3(Random.Range(-1f, 1f), Random.Range(-0.5f, 1f), Random.Range(-1f, 1f)) * rise : Vector3.up * rise;
                Fx.Puff(at, color, size, sparks ? 0.18f : 0.9f, drift, fxParent);
            }
        }
    }
}

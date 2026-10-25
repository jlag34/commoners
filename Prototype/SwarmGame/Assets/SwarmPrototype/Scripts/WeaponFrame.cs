using UnityEngine;

namespace SwarmPrototype
{
    // The sword mesh's local space, measured so other weapons can be built in it:
    // `along` runs grip to tip and `across` is the blade's width direction, both
    // axis-aligned in the mesh's local frame. Pieces placed here follow the hand
    // through every animation exactly like the sword does, with no world-space
    // guessing about bone orientation.
    public class WeaponFrame
    {
        public Transform space;
        Vector3 grip, along, across, third, lossy;

        public static WeaponFrame From(Renderer sword, Transform weaponBone)
        {
            if (sword == null || weaponBone == null) return null;
            Mesh mesh = null;
            if (sword is MeshRenderer) { var mf = sword.GetComponent<MeshFilter>(); mesh = mf != null ? mf.sharedMesh : null; }
            if (mesh == null) return null;
            Bounds b = mesh.bounds;
            Vector3 e = b.extents;
            int a = e.x >= e.y && e.x >= e.z ? 0 : e.y >= e.z ? 1 : 2;
            int w = 0; float best = -1f;
            for (int i = 0; i < 3; i++) if (i != a && e[i] > best) { best = e[i]; w = i; }
            int t = 3 - a - w;
            var f = new WeaponFrame { space = sword.transform, lossy = sword.transform.lossyScale };
            f.along = Axis(a); f.across = Axis(w); f.third = Axis(t);
            // The end nearer the weapon bone is the grip.
            Vector3 bone = f.space.InverseTransformPoint(weaponBone.position);
            Vector3 tipA = b.center + f.along * e[a], tipB = b.center - f.along * e[a];
            if ((tipA - bone).sqrMagnitude < (tipB - bone).sqrMagnitude) f.along = -f.along;
            f.grip = b.center - f.along * e[a];
            return f;
        }

        static Vector3 Axis(int i) => i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward;

        // World metres along the local axes to a local offset.
        Vector3 Local(Vector3 metres) => new Vector3(metres.x / Mathf.Max(0.0001f, lossy.x), metres.y / Mathf.Max(0.0001f, lossy.y), metres.z / Mathf.Max(0.0001f, lossy.z));
        static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        // A box centred `alongM` metres up the blade axis and `acrossM` to the side,
        // `length` long along the blade (or `acrossLength` long across it), `thick` otherwise.
        public GameObject Bar(float alongM, float acrossM, float length, float thick, Material material, float acrossLength = 0f)
        {
            Vector3 size = acrossLength > 0f
                ? Abs(across * acrossLength + along * length + third * thick)
                : Abs(along * length + across * thick + third * thick);
            return Make(PrimitiveType.Cube, along * alongM + across * acrossM, size, material);
        }

        public GameObject Ball(float alongM, float acrossM, float diameter, Material material) =>
            Make(PrimitiveType.Sphere, along * alongM + across * acrossM, Vector3.one * diameter, material);

        GameObject Make(PrimitiveType type, Vector3 offsetMetres, Vector3 sizeMetres, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = "Weapon";
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(space, false);
            go.transform.localPosition = grip + Local(offsetMetres);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Local(sizeMetres);
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            return go;
        }
    }
}

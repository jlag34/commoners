using UnityEngine;

namespace SwarmPrototype
{
    // A ring that grows and fades where you click, to confirm the new rally point.
    public class RallyPulse : MonoBehaviour
    {
        const float DefaultDuration = 0.6f;
        const float DefaultStartSize = 0.6f;
        const float DefaultEndSize = 4f;

        static Material material;

        Renderer ring;
        Color color;
        float startTime;
        float duration, startSize, endSize;

        public static void Spawn(Vector3 point, Color color, Transform parent) =>
            Spawn(point, color, parent, DefaultStartSize, DefaultEndSize, DefaultDuration);

        public static void Spawn(Vector3 point, Color color, Transform parent, float startSize, float endSize, float duration)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "Rally Pulse";
            DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.position = point + Vector3.up * 0.05f;

            if (material == null)
            {
                // Legacy transparent shader so the fade works without extra setup.
                material = new Material(Shader.Find("Sprites/Default"));
            }
            var pulse = go.AddComponent<RallyPulse>();
            pulse.ring = go.GetComponent<Renderer>();
            pulse.ring.sharedMaterial = material;
            pulse.ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            pulse.color = color;
            pulse.startTime = Time.time;
            pulse.startSize = startSize;
            pulse.endSize = endSize;
            pulse.duration = duration;
            pulse.Update();
        }

        void Update()
        {
            float t = (Time.time - startTime) / duration;
            if (t >= 1f)
            {
                Destroy(gameObject);
                return;
            }
            float size = Mathf.Lerp(startSize, endSize, 1f - (1f - t) * (1f - t));
            transform.localScale = new Vector3(size, 0.02f, size);
            var c = color;
            c.a = 0.8f * (1f - t);
            var block = new MaterialPropertyBlock();
            block.SetColor("_Color", c);
            ring.SetPropertyBlock(block);
        }
    }
}

using UnityEngine;

namespace SwarmPrototype
{
    // Cheap one-off effects: a single soft ball that grows, drifts and fades.
    // Used for dust under fast runners and the sparks / fumes around elites,
    // where SpawnPoof's seven spheres would be too heavy per unit.
    public class Fx : MonoBehaviour
    {
        static Material material;

        Renderer ball;
        Color color;
        Vector3 start, drift;
        float size, life, startTime;

        public static void Puff(Vector3 at, Color color, float size, float life, Vector3 drift, Transform parent)
        {
            if (material == null) material = new Material(Shader.Find("Sprites/Default"));
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Fx";
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.position = at;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            var fx = go.AddComponent<Fx>();
            fx.ball = r;
            fx.color = color;
            fx.start = at;
            fx.drift = drift;
            fx.size = size;
            fx.life = life;
            fx.startTime = Time.time;
            fx.Update();
        }

        // Dust kicked up at a runner's feet.
        public static void Dust(Vector3 feet, Transform parent)
        {
            Vector2 spread = Random.insideUnitCircle * 0.3f;
            Puff(feet + new Vector3(spread.x, 0.15f, spread.y), new Color(0.76f, 0.68f, 0.55f, 0.7f), 0.35f, 0.45f, new Vector3(spread.x, 0.5f, spread.y), parent);
        }

        void Update()
        {
            float t = (Time.time - startTime) / life;
            if (t >= 1f)
            {
                Destroy(gameObject);
                return;
            }
            float ease = 1f - (1f - t) * (1f - t);
            transform.position = start + drift * ease;
            transform.localScale = Vector3.one * size * (0.5f + ease * 0.8f);
            Color c = color;
            c.a *= 1f - t;
            ball.material.color = c;
        }
    }
}

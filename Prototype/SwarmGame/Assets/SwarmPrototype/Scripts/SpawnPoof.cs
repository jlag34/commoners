using UnityEngine;

namespace SwarmPrototype
{
    // A puff of smoke where a unit appears: a few soft spheres that swell, drift
    // up and fade, plus a small team-colored ring on the ground. The unit itself
    // pops in through PopIn.
    public class SpawnPoof : MonoBehaviour
    {
        const float Duration = 0.55f;
        const int Puffs = 7;

        static Material material;

        struct Puff
        {
            public Transform transform;
            public Renderer renderer;
            public Vector3 start;
            public Vector3 drift;
            public float size;
        }

        Puff[] puffs;
        Color color;
        float startTime;

        public static void Spawn(Vector3 feet, Color team, Transform parent)
        {
            if (material == null) material = new Material(Shader.Find("Sprites/Default"));

            var go = new GameObject("Spawn Poof");
            go.transform.SetParent(parent, false);
            go.transform.position = feet;
            var poof = go.AddComponent<SpawnPoof>();
            poof.color = Color.Lerp(Color.white, team, 0.25f);
            poof.startTime = Time.time;
            poof.puffs = new Puff[Puffs];
            for (int i = 0; i < Puffs; i++)
            {
                var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                DestroyImmediate(ball.GetComponent<Collider>());
                ball.transform.SetParent(go.transform, false);
                var r = ball.GetComponent<Renderer>();
                r.sharedMaterial = material;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                Vector2 spread = Random.insideUnitCircle * 0.45f;
                poof.puffs[i] = new Puff
                {
                    transform = ball.transform,
                    renderer = r,
                    start = new Vector3(spread.x, 0.35f + Random.value * 0.4f, spread.y),
                    drift = new Vector3(spread.x, 1.1f + Random.value * 0.5f, spread.y),
                    size = 0.45f + Random.value * 0.35f,
                };
            }
            RallyPulse.Spawn(feet, team, parent, 0.3f, 2.2f, 0.45f);
            poof.Update();
        }

        void Update()
        {
            float t = (Time.time - startTime) / Duration;
            if (t >= 1f)
            {
                Destroy(gameObject);
                return;
            }
            float ease = 1f - (1f - t) * (1f - t);
            var block = new MaterialPropertyBlock();
            var c = color;
            c.a = 0.85f * (1f - t);
            block.SetColor("_Color", c);
            foreach (var p in puffs)
            {
                p.transform.localPosition = p.start + p.drift * ease;
                p.transform.localScale = Vector3.one * (p.size * (0.4f + 0.9f * ease));
                p.renderer.SetPropertyBlock(block);
            }
        }
    }

    // Scales a freshly spawned model up from nothing with a little overshoot.
    public class PopIn : MonoBehaviour
    {
        const float Duration = 0.3f;
        Vector3 fullScale;
        float startTime;

        public static void Attach(Transform target)
        {
            var pop = target.gameObject.AddComponent<PopIn>();
            pop.fullScale = target.localScale;
            pop.startTime = Time.time;
            target.localScale = Vector3.zero;
        }

        // Jump straight to full size (screenshots, tests).
        public void Finish()
        {
            transform.localScale = fullScale;
            Destroy(this);
        }

        void Update()
        {
            float t = (Time.time - startTime) / Duration;
            if (t >= 1f)
            {
                Finish();
                return;
            }
            // Back-out ease: overshoots to ~1.1 then settles.
            float u = t - 1f;
            float s = 1f + u * u * (2.7f * u + 1.7f);
            transform.localScale = fullScale * s;
        }
    }
}

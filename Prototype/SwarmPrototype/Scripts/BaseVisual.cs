using UnityEngine;

namespace SwarmPrototype
{
    // Dresses a base's 5 x 3 x 5 collider block as a slim keep that fits inside that
    // footprint: Kenney castle pieces from Resources/SwarmModels if they're there,
    // plain shapes otherwise. Team color goes on the roofs and banners.
    public static class BaseVisual
    {
        // Corner towers are CornerWidth wide and centered this far apart, so the
        // whole keep spans Footprint + CornerWidth = the collider's 5 m.
        const float CornerWidth = 1f;
        const float Footprint = Spawner.Footprint - CornerWidth;
        const float KeepWidth = 2.4f;

        public static void Build(GameObject baseBlock, Color color)
        {
            baseBlock.GetComponent<Renderer>().enabled = false;

            // Undo the block's scale so pieces can be placed in meters, feet on the ground.
            var holder = new GameObject("Keep").transform;
            holder.SetParent(baseBlock.transform, false);
            Vector3 s = baseBlock.transform.localScale;
            holder.localScale = new Vector3(1f / s.x, 1f / s.y, 1f / s.z);
            holder.localPosition = new Vector3(0f, -0.5f, 0f);

            if (!BuildFromModels(holder, color)) BuildFromShapes(holder, color);

            foreach (var r in holder.GetComponentsInChildren<Renderer>())
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }

        // Dresses a secondary tower's collider block as one hexagon tower.
        public static void BuildCornerTower(GameObject block, Color color, float width, float height)
        {
            var holder = new GameObject("Tower Model").transform;
            holder.SetParent(block.transform, false);
            Vector3 s = block.transform.localScale;
            holder.localScale = new Vector3(1f / s.x, 1f / s.y, 1f / s.z);
            holder.localPosition = new Vector3(0f, -0.5f, 0f);

            if (Stack(holder, Vector3.zero, width, color, "tower-hexagon-base", "tower-hexagon-roof") == null)
            {
                var stone = Visuals.Plain(new Color(0.68f, 0.65f, 0.6f));
                var roof = Visuals.Plain(Color.Lerp(color, Color.black, 0.15f));
                Part(PrimitiveType.Cylinder, holder, new Vector3(0f, height * 0.4f, 0f), new Vector3(width, height * 0.4f, width), stone);
                Part(PrimitiveType.Cylinder, holder, new Vector3(0f, height * 0.85f, 0f), new Vector3(width * 1.2f, 0.12f, width * 1.2f), roof);
            }
            foreach (var r in holder.GetComponentsInChildren<Renderer>())
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }

        // Kenney castle pieces: a tall central tower, hexagon towers on the corners
        // and a flag on top. Their blue roofs and banners take the team color.
        const float KenneyBlueHue = 0.64f;

        static bool BuildFromModels(Transform holder, Color color)
        {
            var keep = Stack(holder, Vector3.zero, KeepWidth, color, "tower-square-base-color", "tower-square-mid-windows", "tower-square-top-roof-high");
            if (keep == null) return false;

            float half = Footprint * 0.5f;
            var corners = new[] { new Vector3(-half, 0f, -half), new Vector3(half, 0f, -half), new Vector3(half, 0f, half), new Vector3(-half, 0f, half) };
            foreach (var corner in corners)
                Stack(holder, corner, CornerWidth, color, "tower-hexagon-base", "tower-hexagon-roof");

            Crystal(holder, new Vector3(0f, keep.Value, 0f), color, 1.1f);
            var flag = Piece("flag", holder, color);
            if (flag != null)
            {
                Visuals.FitHeight(flag, 1.4f);
                Center(flag, new Vector3(half, 0f, half) + Vector3.up * CornerTop(holder, half));
            }
            return true;
        }

        // Height of the corner tower stacked at (x, z), for parking the flag on one.
        static float CornerTop(Transform holder, float half)
        {
            float top = 0f;
            foreach (var r in holder.GetComponentsInChildren<Renderer>())
            {
                Vector3 local = holder.InverseTransformPoint(r.bounds.center);
                if (Mathf.Abs(local.x - half) < CornerWidth && Mathf.Abs(local.z - half) < CornerWidth)
                    top = Mathf.Max(top, holder.InverseTransformPoint(new Vector3(0f, r.bounds.max.y, 0f)).y);
            }
            return top;
        }

        // The team's power crystal: a glowing, slowly turning gem on the keep's top.
        static void Crystal(Transform holder, Vector3 at, Color color, float height)
        {
            var gem = Visuals.Plain(Color.Lerp(color, Color.white, 0.35f));
            gem.EnableKeyword("_EMISSION");
            gem.SetColor("_EmissionColor", color * 1.3f);
            var pivot = new GameObject("Crystal").transform;
            pivot.SetParent(holder, false);
            pivot.localPosition = at + Vector3.up * (height * 0.5f + 0.1f);
            pivot.gameObject.AddComponent<Spin>();
            float w = height * 0.38f;
            // Two squashed cubes on their corners make a rough octahedron.
            var upper = Part(PrimitiveType.Cube, pivot, Vector3.zero, new Vector3(w, height * 0.5f, w), gem);
            upper.transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
            var band = Part(PrimitiveType.Cube, pivot, Vector3.zero, new Vector3(w * 0.9f, height * 0.42f, w * 0.9f), gem);
            band.transform.localRotation = Quaternion.Euler(45f, 0f, 45f);
            var plinth = Visuals.Plain(new Color(0.5f, 0.48f, 0.45f));
            Part(PrimitiveType.Cube, holder, at + Vector3.up * 0.08f, new Vector3(w * 1.6f, 0.16f, w * 1.6f), plinth);
        }

        class Spin : MonoBehaviour
        {
            void Update() => transform.Rotate(0f, 40f * Time.deltaTime, 0f, Space.Self);
        }

        // Spawns pieces on top of each other at `position`, all at the scale that makes
        // the first one `width` wide. Returns the height of the top, or null if a piece is missing.
        static float? Stack(Transform holder, Vector3 position, float width, Color color, params string[] names)
        {
            float top = position.y;
            float scale = 0f;
            foreach (var name in names)
            {
                var piece = Piece(name, holder, color);
                if (piece == null) return null;
                if (scale == 0f) scale = FitWidth(piece, width);
                else piece.transform.localScale *= scale;
                Center(piece, new Vector3(position.x, top, position.z));
                top += Bounds(piece).size.y / holder.lossyScale.y;
            }
            return top;
        }

        static GameObject Piece(string name, Transform holder, Color color)
        {
            var piece = Visuals.SpawnModel(name, holder);
            if (piece != null) Visuals.TeamColor(piece, color, KenneyBlueHue, 0f);
            return piece;
        }

        static float FitWidth(GameObject piece, float width)
        {
            Vector3 size = Bounds(piece).size;
            float current = Mathf.Max(size.x, size.z) / piece.transform.parent.lossyScale.x;
            float scale = current > 0.0001f ? width / current : 1f;
            piece.transform.localScale *= scale;
            return scale;
        }

        // Moves a piece so its bounds sit centered on `bottomCenter` (holder space).
        static void Center(GameObject piece, Vector3 bottomCenter)
        {
            var parent = piece.transform.parent;
            Bounds b = Bounds(piece);
            Vector3 current = parent.InverseTransformPoint(new Vector3(b.center.x, b.min.y, b.center.z));
            piece.transform.localPosition += bottomCenter - current;
        }

        static Bounds Bounds(GameObject piece)
        {
            var renderers = piece.GetComponentsInChildren<Renderer>();
            Bounds bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            return bounds;
        }

        static void BuildFromShapes(Transform holder, Color color)
        {
            var stone = Visuals.Plain(new Color(0.68f, 0.65f, 0.6f));
            var darkStone = Visuals.Plain(new Color(0.5f, 0.48f, 0.45f));
            var roof = Visuals.Plain(Color.Lerp(color, Color.black, 0.15f));
            var wood = Visuals.Plain(new Color(0.4f, 0.28f, 0.18f));

            Part(PrimitiveType.Cube, holder, new Vector3(0f, 1.8f, 0f), new Vector3(2.8f, 3.6f, 2.8f), stone);
            Part(PrimitiveType.Cube, holder, new Vector3(0f, 3.8f, 0f), new Vector3(3.1f, 0.4f, 3.1f), darkStone);
            // Battlements.
            for (int i = 0; i < 4; i++)
            {
                float o = -1.2f + i * 0.8f;
                Part(PrimitiveType.Cube, holder, new Vector3(o, 4.25f, 1.35f), new Vector3(0.4f, 0.5f, 0.4f), darkStone);
                Part(PrimitiveType.Cube, holder, new Vector3(o, 4.25f, -1.35f), new Vector3(0.4f, 0.5f, 0.4f), darkStone);
                Part(PrimitiveType.Cube, holder, new Vector3(1.35f, 4.25f, o), new Vector3(0.4f, 0.5f, 0.4f), darkStone);
                Part(PrimitiveType.Cube, holder, new Vector3(-1.35f, 4.25f, o), new Vector3(0.4f, 0.5f, 0.4f), darkStone);
            }
            // Corner towers with team-colored caps.
            float half = Footprint * 0.5f;
            foreach (var corner in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(1, 1) })
            {
                var p = new Vector3(corner.x * half, 0f, corner.y * half);
                Part(PrimitiveType.Cylinder, holder, p + new Vector3(0f, 2.2f, 0f), new Vector3(CornerWidth, 2.2f, CornerWidth), stone);
                Part(PrimitiveType.Cylinder, holder, p + new Vector3(0f, 4.5f, 0f), new Vector3(CornerWidth, 0.15f, CornerWidth), roof);
                Part(PrimitiveType.Cube, holder, p + new Vector3(0f, 5f, 0f), new Vector3(0.6f, 0.9f, 0.6f), roof).transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
            }
            // Gate on every side and a big team banner on top.
            Part(PrimitiveType.Cube, holder, new Vector3(0f, 0.9f, 1.42f), new Vector3(1.1f, 1.8f, 0.1f), wood);
            Part(PrimitiveType.Cube, holder, new Vector3(0f, 0.9f, -1.42f), new Vector3(1.1f, 1.8f, 0.1f), wood);
            Crystal(holder, new Vector3(0f, 4.0f, 0f), color, 1.1f);
        }

        static GameObject Part(PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go;
        }
    }
}

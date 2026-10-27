using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SwarmPrototype
{
    // Presentation only: no combatants, physics or match state.
    public class HomeUnitPreview : MonoBehaviour, IDragHandler
    {
        const int PreviewLayer = 30;
        readonly List<Material> materials = new List<Material>();
        readonly List<(Light light, int mask)> sceneLights = new List<(Light, int)>();
        GameObject stage;
        Transform unit;
        GameObject model;
        UnitGear gear;
        // Upgrade levels behind the gear test bar: commoner, bronze, silver, gold.
        static readonly int[] TierLevels = { 0, 1, 6, 11 };
        static readonly Color PreviewTeam = new Color(0.13f, 0.36f, 0.86f);
        Camera previewCamera;
        RenderTexture texture;
        float turn = 155, entrance;

        public void Build()
        {
            stage = new GameObject("Home unit stage");
            foreach (var light in FindObjectsOfType<Light>())
            {
                sceneLights.Add((light, light.cullingMask));
                light.cullingMask &= ~(1 << PreviewLayer);
            }
            // Keep world-space staging independent of the Canvas scale.
            stage.transform.position = new Vector3(0, -1000, 0);
            var cameraObject = new GameObject("Unit portrait camera");
            cameraObject.transform.SetParent(stage.transform, false);
            previewCamera = cameraObject.AddComponent<Camera>();
            previewCamera.clearFlags = CameraClearFlags.SolidColor;
            previewCamera.backgroundColor = Color.clear;
            previewCamera.cullingMask = 1 << PreviewLayer;
            previewCamera.orthographic = true;
            previewCamera.orthographicSize = 2.05f;
            previewCamera.nearClipPlane = 0.1f;
            previewCamera.farClipPlane = 20;
            previewCamera.transform.localPosition = new Vector3(0, 2.5f, -7);
            previewCamera.transform.LookAt(stage.transform.position + Vector3.up * 1.35f);
            previewCamera.depth = -10;
            texture = new RenderTexture(1024, 1024, 24, RenderTextureFormat.ARGB32) { name = "Home unit portrait", antiAliasing = 2 };
            previewCamera.targetTexture = texture;
            var image = gameObject.AddComponent<RawImage>();
            image.texture = texture;
            image.raycastTarget = true;
            AddLight("Warm key", new Vector3(35, -35, 0), new Color(1, 0.9f, 0.7f), 0.85f);
            AddLight("Cool rim", new Vector3(30, 145, 0), new Color(0.4f, 0.86f, 0.8f), 0.55f);
            AddLight("Soft fill", new Vector3(10, 10, 0), new Color(0.8f, 0.9f, 1), 0.2f);
            Plinth("Stone footing", new Vector3(0, 0.04f, 0), new Vector3(2.4f, 0.12f, 2.4f), MenuUi.Hex("183C43"));
            Plinth("Brass rim", new Vector3(0, 0.18f, 0), new Vector3(2.2f, 0.035f, 2.2f), MenuUi.Gold);
            Plinth("Upper stone", new Vector3(0, 0.25f, 0), new Vector3(2.1f, 0.04f, 2.1f), MenuUi.Hex("36605D"));
            unit = new GameObject("Equipped unit").transform;
            unit.SetParent(stage.transform, false);
            unit.localPosition = new Vector3(0, 0.3f, 0);
            model = Visuals.SpawnModel("Warrior", unit);
            if (model != null)
            {
                Visuals.FitHeight(model, 2.75f);
                var animator = UnitAnimator.Attach(model, Visuals.LoadClips("Warrior"), null, 0);
                if (animator != null) model.GetComponent<Animator>().cullingMode = AnimatorCullingMode.AlwaysAnimate;
                // Starts as the bare commoner (tier 0): pitchfork-era weapon, no armor.
                gear = UnitGear.Attach(model, PreviewTeam);
                SetTier(0);
            }
            else
            {
                var capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                Destroy(capsule.GetComponent<Collider>());
                capsule.transform.SetParent(unit, false);
                capsule.transform.localPosition = Vector3.up;
                capsule.GetComponent<Renderer>().sharedMaterial = MakeMaterial(MenuUi.Gold);
            }
            foreach (var t in stage.GetComponentsInChildren<Transform>()) t.gameObject.layer = PreviewLayer;
        }

        // Dresses the preview in gear tier 0-3 (strength and armor together; size unchanged).
        public void SetTier(int tier)
        {
            if (gear == null) return;
            int level = TierLevels[Mathf.Clamp(tier, 0, TierLevels.Length - 1)];
            gear.Apply(level, level, 0, 0);
            foreach (var t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = PreviewLayer;
        }

        public void OnDrag(PointerEventData data) => turn -= data.delta.x * 0.45f;
        public void Rotate(float degrees) => turn += degrees;
        void OnEnable() { if (stage != null) stage.SetActive(true); }
        void OnDisable() { if (stage != null) stage.SetActive(false); }
        void Update()
        {
            if (unit == null) return;
            entrance = Mathf.MoveTowards(entrance, 1, Time.unscaledDeltaTime * 2.5f);
            unit.localScale = Vector3.one * Mathf.SmoothStep(0.85f, 1, entrance);
            unit.localRotation = Quaternion.Euler(0, turn + Mathf.Sin(Time.unscaledTime * 0.5f) * 2, 0);
        }

        Material MakeMaterial(Color color)
        {
            var material = Visuals.Plain(color);
            materials.Add(material);
            return material;
        }
        void Plinth(string label, Vector3 position, Vector3 scale, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(go.GetComponent<Collider>());
            go.name = label;
            go.transform.SetParent(stage.transform, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = MakeMaterial(color);
        }
        void AddLight(string label, Vector3 rotation, Color color, float intensity)
        {
            var go = new GameObject(label);
            go.transform.SetParent(stage.transform, false);
            go.transform.localRotation = Quaternion.Euler(rotation);
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = color;
            light.intensity = intensity;
            light.cullingMask = 1 << PreviewLayer;
            light.shadows = LightShadows.None;
        }
        void OnDestroy()
        {
            if (previewCamera != null) previewCamera.targetTexture = null;
            if (texture != null) { texture.Release(); Destroy(texture); }
            foreach (var material in materials) Destroy(material);
            foreach (var entry in sceneLights) if (entry.light != null) entry.light.cullingMask = entry.mask;
            if (stage != null) Destroy(stage);
        }
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.EventSystems;
using UnityEngine.Playables;
using UnityEngine.UI;

namespace SwarmPrototype
{
    // Presentation only: no combatants, physics or match state. The unit stands in a
    // relaxed menu idle from Warrior_Menu.fbx (art/blender/menu_pose.py): one looping clip
    // per weapon/shield combination, played in sync with identical legs, hips and head, so
    // switching gear only blends the arms. Without those clips it falls back to the
    // gameplay idle.
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
        // Indexed by (sword ? 2 : 0) + (shield ? 1 : 0); a shield comes with armor tier 1+.
        static readonly string[] MenuClips = { "menu_fork", "menu_fork_shield", "menu_sword", "menu_sword_shield" };
        const float PoseBlendSpeed = 4f;
        Camera previewCamera;
        RenderTexture texture;
        // Three-quarter view: the camera 20 degrees off his left, as the menu clips are posed.
        float turn = 200, entrance;
        PlayableGraph graph;
        AnimationMixerPlayable mixer;
        readonly float[] poseWeights = new float[4];
        int menuPose;
        float clipLength;
        SkinnedMeshRenderer face;
        int blinkShape = -1;
        float nextBlink, blinkStart = -1;
        bool doubleBlink;

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
            // Soft and mostly frontal: a warm key, a low fill that reaches under helmet brims,
            // and a cool rim kept dim so polished plate doesn't blow out.
            AddLight("Warm key", new Vector3(32, -30, 0), new Color(1, 0.92f, 0.76f), 0.72f);
            AddLight("Low fill", new Vector3(4, 12, 0), new Color(0.86f, 0.92f, 1), 0.38f);
            AddLight("Cool rim", new Vector3(28, 150, 0), new Color(0.4f, 0.86f, 0.8f), 0.4f);
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
                if (!PlayMenuIdle(Visuals.LoadClips("Warrior_Menu")))
                {
                    var animator = UnitAnimator.Attach(model, Visuals.LoadClips("Warrior"), null, 0);
                    if (animator != null) model.GetComponent<Animator>().cullingMode = AnimatorCullingMode.AlwaysAnimate;
                }
                // Starts as the bare commoner (tier 0): pitchfork-era weapon, no armor.
                gear = UnitGear.Attach(model, PreviewTeam);
                var head = model.transform.Find("Commoner_Head");
                face = head != null ? head.GetComponent<SkinnedMeshRenderer>() : null;
                if (face != null && face.sharedMesh != null) blinkShape = face.sharedMesh.GetBlendShapeIndex("Blink");
                nextBlink = Time.unscaledTime + 1.5f;
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

        // Dresses the preview in gear tier 0-3, weapon and armor together.
        public void SetTier(int tier) => SetGear(tier, tier);

        // Weapon tier 0-3 (pitchfork, bronze / steel / gilded sword) and armor tier 0-3 (none,
        // leather, iron, gilded, each with its shield), chosen independently; size unchanged.
        public void SetGear(int weaponTier, int armorTier)
        {
            if (gear == null) return;
            weaponTier = Mathf.Clamp(weaponTier, 0, TierLevels.Length - 1);
            armorTier = Mathf.Clamp(armorTier, 0, TierLevels.Length - 1);
            gear.Apply(TierLevels[weaponTier], TierLevels[armorTier], 0, 0);
            menuPose = (weaponTier > 0 ? 2 : 0) + (armorTier > 0 ? 1 : 0);
            foreach (var t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = PreviewLayer;
        }

        bool PlayMenuIdle(AnimationClip[] clips)
        {
            var found = new AnimationClip[MenuClips.Length];
            for (int i = 0; i < MenuClips.Length; i++)
            {
                foreach (var clip in clips)
                {
                    string n = clip.name.ToLowerInvariant();
                    if (n.Substring(n.LastIndexOf('|') + 1) == MenuClips[i]) found[i] = clip;
                }
                if (found[i] == null) return false;
            }
            var animator = model.GetComponent<Animator>();
            if (animator == null) animator = model.AddComponent<Animator>();
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            graph = PlayableGraph.Create("Home unit menu idle");
            graph.SetTimeUpdateMode(DirectorUpdateMode.UnscaledGameTime);
            mixer = AnimationMixerPlayable.Create(graph, found.Length);
            for (int i = 0; i < found.Length; i++)
            {
                var playable = AnimationClipPlayable.Create(graph, found[i]);
                playable.SetApplyFootIK(false);
                graph.Connect(playable, 0, mixer, i);
            }
            clipLength = found[0].length;
            poseWeights[menuPose] = 1;
            mixer.SetInputWeight(menuPose, 1);
            AnimationPlayableOutput.Create(graph, "Unit", animator).SetSourcePlayable(mixer);
            graph.Play();
            return true;
        }

        // Every clip runs on the same clock; only the weights move, so the legs and body
        // never jump when the gear changes.
        void UpdateMenuIdle()
        {
            if (!graph.IsValid()) return;
            for (int i = 0; i < poseWeights.Length; i++)
            {
                var playable = mixer.GetInput(i);
                // FBX clips import without looping; wrap by hand.
                if (clipLength > 0 && playable.GetTime() > clipLength) playable.SetTime(playable.GetTime() % clipLength);
                poseWeights[i] = Mathf.MoveTowards(poseWeights[i], i == menuPose ? 1 : 0, PoseBlendSpeed * Time.unscaledDeltaTime);
            }
            float total = 0;
            foreach (float w in poseWeights) total += w;
            for (int i = 0; i < poseWeights.Length; i++) mixer.SetInputWeight(i, total > 0 ? poseWeights[i] / total : 0);
        }

        // A blink every few seconds, now and then a double one: the Blink shape on the
        // commoner's head squashes the eyes shut.
        void UpdateBlink()
        {
            if (blinkShape < 0) return;
            const float duration = 0.16f;
            float now = Time.unscaledTime;
            if (blinkStart < 0 && now >= nextBlink)
            {
                blinkStart = now;
                doubleBlink = Random.value < 0.2f;
            }
            float weight = 0;
            if (blinkStart >= 0)
            {
                float t = (now - blinkStart) / duration;
                if (t >= 1 && doubleBlink) { blinkStart = now + 0.08f; doubleBlink = false; t = -1; }
                else if (t >= 1) { blinkStart = -1; nextBlink = now + Random.Range(2.5f, 6f); }
                if (t > 0 && t < 1) weight = 100 * (1 - Mathf.Abs(2 * t - 1));
            }
            face.SetBlendShapeWeight(blinkShape, weight);
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
            // No turntable sway: the idle carries the life, and the feet stay put on the plinth.
            unit.localRotation = Quaternion.Euler(0, turn, 0);
            UpdateMenuIdle();
            UpdateBlink();
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
            if (graph.IsValid()) graph.Destroy();
            if (previewCamera != null) previewCamera.targetTexture = null;
            if (texture != null) { texture.Release(); Destroy(texture); }
            foreach (var material in materials) Destroy(material);
            foreach (var entry in sceneLights) if (entry.light != null) entry.light.cullingMask = entry.mask;
            if (stage != null) Destroy(stage);
        }
    }
}

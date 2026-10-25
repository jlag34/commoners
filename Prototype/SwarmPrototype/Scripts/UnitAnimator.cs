using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace SwarmPrototype
{
    // Plays idle/run/attack/death clips on a unit's model with a small Playables
    // graph, so no AnimatorController asset is needed. Clips are picked from the
    // model's FBX by name. Missing clips are skipped.
    public class UnitAnimator : MonoBehaviour
    {
        const float BlendSpeed = 8f;
        const float RunThreshold = 0.6f;

        enum State { Idle, Run, Attack, Death }

        PlayableGraph graph;
        AnimationMixerPlayable mixer;
        AnimationClipPlayable[] playables = new AnimationClipPlayable[4];
        float[] weights = new float[4];
        Rigidbody body;
        State state;
        float attackUntil;
        float runSpeedScale = 1f;

        public static UnitAnimator Attach(GameObject model, AnimationClip[] clips, Rigidbody body, float moveSpeed)
        {
            // First name that matches wins, so list the best fit first.
            var source = new AnimationClip[4];
            source[(int)State.Idle] = Find(clips, "idle_weapon", "idle");
            source[(int)State.Run] = Find(clips, "run_weapon", "run", "sprint", "walk");
            source[(int)State.Attack] = Find(clips, "sword_attack", "attack-melee", "attack", "punch");
            source[(int)State.Death] = Find(clips, "death", "die");
            if (source[(int)State.Idle] == null && source[(int)State.Run] == null) return null;

            var animator = model.GetComponent<Animator>();
            if (animator == null) animator = model.AddComponent<Animator>();
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

            var self = model.AddComponent<UnitAnimator>();
            self.body = body;
            self.Build(animator, source);
            self.SetMoveSpeed(moveSpeed);
            return self;
        }

        // Clips are authored for a normal jog; speed them up or down to match.
        public void SetMoveSpeed(float moveSpeed) => runSpeedScale = Mathf.Clamp(moveSpeed / 4f, 0.7f, 1.6f);

        static AnimationClip Find(AnimationClip[] clips, params string[] names)
        {
            foreach (var wanted in names)
            {
                foreach (var clip in clips)
                {
                    string n = clip.name.ToLowerInvariant();
                    if (n.Contains("__preview__")) continue;
                    // Clip names look like "CharacterArmature|Run_Weapon"; match the part after the bar.
                    int bar = n.LastIndexOf('|');
                    if (bar >= 0) n = n.Substring(bar + 1);
                    if (n == wanted) return clip;
                }
                foreach (var clip in clips)
                {
                    string n = clip.name.ToLowerInvariant();
                    if (!n.Contains("__preview__") && n.Contains(wanted) && !n.Contains("attacking")) return clip;
                }
            }
            return null;
        }

        void Build(Animator animator, AnimationClip[] source)
        {
            graph = PlayableGraph.Create(name + " Animation");
            graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            mixer = AnimationMixerPlayable.Create(graph, source.Length);
            for (int i = 0; i < source.Length; i++)
            {
                // Fall back to idle (or run) when a clip is missing.
                var clip = source[i] != null ? source[i] : (source[0] != null ? source[0] : source[1]);
                playables[i] = AnimationClipPlayable.Create(graph, clip);
                playables[i].SetApplyFootIK(false);
                graph.Connect(playables[i], 0, mixer, i);
            }
            var output = AnimationPlayableOutput.Create(graph, "Unit", animator);
            output.SetSourcePlayable(mixer);
            // Random start so a swarm doesn't move in lockstep.
            playables[0].SetTime(Random.value * 2f);
            playables[1].SetTime(Random.value * 2f);
            weights[0] = 1f;
            mixer.SetInputWeight(0, 1f);
            graph.Play();
        }

        public void OnAttack()
        {
            if (state == State.Death || !graph.IsValid()) return;
            var attack = playables[(int)State.Attack];
            float length = attack.GetAnimationClip().length;
            attack.SetTime(0);
            attackUntil = Time.time + Mathf.Min(length, 1.2f);
        }

        // Called when the unit dies. The model keeps playing on its own and cleans itself up.
        public void PlayDeath()
        {
            state = State.Death;
            body = null;
            if (!graph.IsValid()) return;
            playables[(int)State.Death].SetTime(0);
            for (int i = 0; i < weights.Length; i++) weights[i] = i == (int)State.Death ? 1f : 0f;
            for (int i = 0; i < weights.Length; i++) mixer.SetInputWeight(i, weights[i]);
        }

        void Update()
        {
            if (!graph.IsValid()) return;
            if (state != State.Death)
            {
                float speed = 0f;
                if (body != null)
                {
#if UNITY_6000_0_OR_NEWER
                    Vector3 v = body.linearVelocity;
#else
                    Vector3 v = body.velocity;
#endif
                    v.y = 0f;
                    speed = v.magnitude;
                }
                if (Time.time < attackUntil) state = State.Attack;
                else state = speed > RunThreshold ? State.Run : State.Idle;
                playables[(int)State.Run].SetSpeed(runSpeedScale);
                // FBX clips import as non-looping by default, so wrap idle and run by hand.
                Loop(playables[(int)State.Idle]);
                Loop(playables[(int)State.Run]);
            }

            float total = 0f;
            for (int i = 0; i < weights.Length; i++)
            {
                float goal = i == (int)state ? 1f : 0f;
                weights[i] = Mathf.MoveTowards(weights[i], goal, BlendSpeed * Time.deltaTime);
                total += weights[i];
            }
            for (int i = 0; i < weights.Length; i++)
                mixer.SetInputWeight(i, total > 0f ? weights[i] / total : 0f);
        }

        static void Loop(AnimationClipPlayable playable)
        {
            float length = playable.GetAnimationClip().length;
            if (length > 0f && playable.GetTime() > length) playable.SetTime(playable.GetTime() % length);
        }

        void OnDestroy()
        {
            if (graph.IsValid()) graph.Destroy();
        }
    }
}

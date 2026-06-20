using System.Collections;
using System.Collections.Generic;
using Gsplat;
using Gsplat.Animation;
using UnityEngine;

namespace Plants
{
    /// <summary>
    /// Idle "waiting" animation: cross-dissolves a small set of standalone Gaussian splats
    /// (poppy → lavender → daffodil → …) so the touch prompt never sits on a dead static cloud.
    ///
    /// Each flower is its own <see cref="GsplatRenderer"/> + <see cref="GsplatRevealAnimator"/>
    /// sharing the same anchor. A transition is simply <c>next.Play()</c> (assemble in) running
    /// alongside <c>current.PlayReverse()</c> (dissolve out): it reuses the proven, private-buffer
    /// safe, dispatch-gated reveal pipeline (bloom sweep + scatter burst), so there is NO shader
    /// work, NO per-pair init cost, and a fully-shown flower (settled at progress 1) costs zero
    /// per-frame GPU. Cadence is a long hold then a quick morph, which masks the dissolve further.
    ///
    /// All flower GameObjects stay enabled for the cycler's lifetime — hidden flowers are parked at
    /// progress 0 (fully transparent when their animator's <c>startAt</c> is 0). Nothing is toggled
    /// active/inactive, so there is no 1-frame full-detail flash on transition. Two parked flowers
    /// each hold one pooled morph buffer; trivial for one waiting plant.
    ///
    /// The collider that starts the experience lives elsewhere (the title's TouchZone) and always
    /// listens, independent of which flower is showing, so the user can touch at any point in the
    /// cycle. Stop the cycle from the title director's <c>onSequenceStarted</c> event via
    /// <see cref="StopCycle"/>; the director then fades whatever flower is showing (it fades every
    /// renderer under the title poppy), so this needs no per-flower dismiss of its own.
    /// </summary>
    public class IdleSplatCycler : MonoBehaviour
    {
        [System.Serializable]
        public class Flower
        {
            [Tooltip("Standalone splat renderer for this flower (kept enabled; visibility is driven by its reveal animator's progress).")]
            public GsplatRenderer renderer;

            [Tooltip("Reveal animator on the same GameObject. Play() assembles in, PlayReverse() dissolves out. " +
                     "For a clean cross-dissolve set startAt=0, startGaussianScale=1, startPositionScale=1, startDesaturation=0.")]
            public GsplatRevealAnimator animator;
        }

        [Header("Flowers (cross-dissolved in order)")]
        [SerializeField] private List<Flower> flowers = new List<Flower>();

        [Header("Timing")]
        [Tooltip("Seconds a flower is held fully shown before the next transition.")]
        [SerializeField, Min(0f)] private float holdDuration = 5f;

        [Tooltip("Seconds for one cross-dissolve (the incoming assemble and outgoing dissolve overlap).")]
        [SerializeField, Min(0.01f)] private float morphDuration = 1f;

        [Header("Start")]
        [Tooltip("Index of the flower shown first. Its initial reveal is left to whoever shows it (e.g. the title director plays the poppy in); the cycler only parks the others hidden.")]
        [SerializeField] private int startIndex = 0;

        [Tooltip("Begin cycling automatically on enable. Turn off to drive via StartCycle() only.")]
        [SerializeField] private bool playOnEnable = true;

        [Header("Optional flavour")]
        [Tooltip("Degrees-as-fraction of hue drift applied to both clouds during a morph (0 = off). " +
                 "Outgoing drifts away from neutral, incoming settles to neutral, so the swap reads as a colour wash.")]
        [SerializeField, Range(0f, 0.5f)] private float hueDriftDuringMorph = 0f;

        static readonly int s_hueShiftId = Shader.PropertyToID("_GsplatHueShift");

        int m_current = -1;
        Coroutine m_loop;

        /// <summary>Renderer of the flower currently shown (so the title director can fade the right one on touch).</summary>
        public GsplatRenderer CurrentRenderer => Valid(m_current) ? flowers[m_current].renderer : null;

        /// <summary>Reveal animator of the flower currently shown.</summary>
        public GsplatRevealAnimator CurrentAnimator => Valid(m_current) ? flowers[m_current].animator : null;

        void OnEnable()
        {
            if (playOnEnable) StartCycle();
        }

        void OnDisable() => StopCycle();

        /// <summary>Park every flower except the start one hidden (progress 0) and begin cycling.
        /// The start flower is left untouched, so an externally-driven initial reveal is preserved.</summary>
        public void StartCycle()
        {
            if (flowers == null || flowers.Count == 0) return;
            StopLoop();

            m_current = Mathf.Clamp(startIndex, 0, flowers.Count - 1);
            for (int i = 0; i < flowers.Count; i++)
            {
                if (i == m_current) continue;
                var a = flowers[i]?.animator;
                if (a != null) a.ResetToStart(); // progress 0 = hidden (with startAt = 0)
            }

            if (isActiveAndEnabled && flowers.Count > 1)
                m_loop = StartCoroutine(Loop());
        }

        /// <summary>Stop cycling. The currently shown flower stays as-is (the title director dismisses it).</summary>
        public void StopCycle() => StopLoop();

        void StopLoop()
        {
            if (m_loop != null) { StopCoroutine(m_loop); m_loop = null; }
        }

        IEnumerator Loop()
        {
            while (true)
            {
                // Long hold: the shown flower is settled (progress 1) and costs zero per-frame GPU.
                float held = 0f;
                while (held < holdDuration) { held += Time.deltaTime; yield return null; }

                int next = (m_current + 1) % flowers.Count;
                if (next == m_current) continue;

                var from = flowers[m_current];
                var to = flowers[next];

                if (to?.animator != null) { to.animator.duration = morphDuration; to.animator.Play(); }        // 0 → 1, assemble
                if (from?.animator != null) { from.animator.duration = morphDuration; from.animator.PlayReverse(); } // 1 → 0, dissolve

                // Overlap window: both clouds draw and animate; optionally wash hue across the swap.
                float t = 0f, dur = Mathf.Max(morphDuration, 0.0001f);
                while (t < dur)
                {
                    t += Time.deltaTime;
                    if (hueDriftDuringMorph > 0f)
                    {
                        float k = Mathf.Clamp01(t / dur);
                        SetHue(from, Mathf.Lerp(0f, hueDriftDuringMorph, k));
                        SetHue(to, Mathf.Lerp(-hueDriftDuringMorph, 0f, k));
                    }
                    yield return null;
                }
                if (hueDriftDuringMorph > 0f) { SetHue(from, 0f); SetHue(to, 0f); }

                m_current = next;
            }
        }

        static void SetHue(Flower f, float hue)
        {
            var r = f?.renderer;
            if (r == null) return;
            var pb = r.PropertyBlock;
            if (pb != null) pb.SetFloat(s_hueShiftId, hue);
        }

        bool Valid(int i) => flowers != null && i >= 0 && i < flowers.Count && flowers[i] != null;
    }
}

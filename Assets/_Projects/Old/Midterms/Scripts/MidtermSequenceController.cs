using System.Collections;
using UnityEngine;
using UnityEngine.VFX;

namespace Midterms
{
    /// <summary>
    /// Orchestrates the midterm vertical-slice animation sequence:
    ///   Idle -> TreeWave (vertical, bottom-up) -> GroundWave (radial) -> LeavesReveal -> LivingIdle.
    /// Begin() is idempotent — calling more than once after the sequence has started is a no-op.
    /// </summary>
    public class MidtermSequenceController : MonoBehaviour
    {
        [Header("Intro VFX")]
        [Tooltip("VFX stopped at the start of Begin(). Sequence waits until all particles die.")]
        public VisualEffect introVfx;
        [Tooltip("VFX Graph float property set before Stop() to speed up particle decay. Leave empty to skip.")]
        public string vfxDecayPropertyName = "";
        public float vfxDecayPropertyValue = 3f;
        [Tooltip("Exponential base for playRate ramp after Stop(). playRate = base^secondsElapsed. 2 = doubles every second.")]
        public float vfxDecaySpeedBase = 2f;

        [Header("Animators")]
        public GsplatShockwaveAnimator treeWave;
        public GsplatShockwaveAnimator groundWave;
        public LeavesRevealAnimator leaves;

        [Header("Poem Canvas")]
        [Tooltip("CanvasGroup on the poem canvas to fade in after the sequence completes.")]
        public CanvasGroup poemCanvasGroup;
        [Tooltip("Seconds to wait after LivingIdle before the canvas starts fading in.")]
        public float poemCanvasDelay = 0.5f;
        [Tooltip("Duration of the canvas fade-in.")]
        public float poemCanvasFadeDuration = 1.5f;

        [Header("Pacing")]
        [Tooltip("Seconds of overlap: ground wave starts this many seconds before tree wave finishes.")]
        public float overlapTreeGround = 0.3f;
        [Tooltip("Seconds of overlap: leaves reveal starts this many seconds before ground wave finishes.")]
        public float overlapGroundLeaves = 0.3f;

        public enum Stage { Idle, TreeWave, GroundWave, LeavesReveal, LivingIdle }
        public Stage CurrentStage { get; private set; } = Stage.Idle;

        bool m_started;

        void Start()
        {
            if (leaves != null) leaves.Hide();
            if (poemCanvasGroup != null)
            {
                poemCanvasGroup.alpha = 0f;
                poemCanvasGroup.interactable = false;
                poemCanvasGroup.blocksRaycasts = false;
            }
        }

        public void Begin()
        {
            if (m_started) return;
            m_started = true;
            StartCoroutine(Run());
        }

        public void Reset()
        {
            StopAllCoroutines();
            m_started = false;
            CurrentStage = Stage.Idle;
            if (introVfx != null)   introVfx.Play();
            if (treeWave != null)   treeWave.ApplyInitialGreyscale();
            if (groundWave != null) groundWave.ApplyInitialGreyscale();
            if (leaves != null)     leaves.Hide();
            if (poemCanvasGroup != null)
            {
                poemCanvasGroup.alpha = 0f;
                poemCanvasGroup.interactable = false;
                poemCanvasGroup.blocksRaycasts = false;
            }
        }

        IEnumerator Run()
        {
            // Stop intro VFX and wait for all particles to die before the sequence begins.
            if (introVfx != null)
            {
                if (!string.IsNullOrEmpty(vfxDecayPropertyName))
                    introVfx.SetFloat(vfxDecayPropertyName, vfxDecayPropertyValue);
                introVfx.Stop();
                float vfxElapsed = 0f;
                while (introVfx.aliveParticleCount > 0)
                {
                    vfxElapsed += Time.deltaTime;
                    introVfx.playRate = Mathf.Pow(vfxDecaySpeedBase, vfxElapsed);
                    yield return null;
                }
                introVfx.playRate = 1f;
            }

            // Tree wave — ground wave starts overlapTreeGround seconds before tree finishes.
            CurrentStage = Stage.TreeWave;
            if (treeWave != null) treeWave.Play();
            yield return new WaitForSeconds(Mathf.Max(0f, (treeWave != null ? treeWave.duration : 0f) - overlapTreeGround));

            // Ground wave — leaves reveal starts overlapGroundLeaves seconds before ground finishes.
            CurrentStage = Stage.GroundWave;
            if (groundWave != null) groundWave.Play();
            yield return new WaitForSeconds(Mathf.Max(0f, (groundWave != null ? groundWave.duration : 0f) - overlapGroundLeaves));

            // Leaves reveal -> living idle
            CurrentStage = Stage.LeavesReveal;
            if (leaves != null)
            {
                leaves.Reveal();
                while (!leaves.IsRevealed) yield return null;
            }

            CurrentStage = Stage.LivingIdle;

            if (poemCanvasGroup != null)
            {
                yield return new WaitForSeconds(poemCanvasDelay);
                float elapsed = 0f;
                while (elapsed < poemCanvasFadeDuration)
                {
                    elapsed += Time.deltaTime;
                    poemCanvasGroup.alpha = Mathf.Clamp01(elapsed / poemCanvasFadeDuration);
                    yield return null;
                }
                poemCanvasGroup.alpha = 1f;
                poemCanvasGroup.interactable = true;
                poemCanvasGroup.blocksRaycasts = true;
            }
        }

        [ContextMenu("Begin (debug)")]
        void DebugBegin() => Begin();

        [ContextMenu("Reset (debug)")]
        void DebugReset() => Reset();
    }
}

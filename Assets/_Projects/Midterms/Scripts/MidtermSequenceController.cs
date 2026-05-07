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

        [Header("Animators")]
        public GsplatShockwaveAnimator treeWave;
        public GsplatShockwaveAnimator groundWave;
        public LeavesRevealAnimator leaves;

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
            // Leaves start hidden but GameObject stays active and ready.
            if (leaves != null) leaves.Hide();
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
        }

        IEnumerator Run()
        {
            // Stop intro VFX and wait for all particles to die before the sequence begins.
            if (introVfx != null)
            {
                if (!string.IsNullOrEmpty(vfxDecayPropertyName))
                    introVfx.SetFloat(vfxDecayPropertyName, vfxDecayPropertyValue);
                introVfx.Stop();
                while (introVfx.aliveParticleCount > 0) yield return null;
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
        }

        [ContextMenu("Begin (debug)")]
        void DebugBegin() => Begin();

        [ContextMenu("Reset (debug)")]
        void DebugReset() => Reset();
    }
}

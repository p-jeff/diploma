using System.Collections;
using UnityEngine;

namespace Midterms
{
    /// <summary>
    /// Orchestrates the midterm vertical-slice animation sequence:
    ///   Idle -> TreeWave (vertical, bottom-up) -> GroundWave (radial) -> LeavesReveal -> LivingIdle.
    /// Begin() is idempotent — calling more than once after the sequence has started is a no-op.
    /// </summary>
    public class MidtermSequenceController : MonoBehaviour
    {
        [Header("Animators")]
        public GsplatShockwaveAnimator treeWave;
        public GsplatShockwaveAnimator groundWave;
        public LeavesRevealAnimator leaves;

        [Header("Pacing")]
        [Tooltip("Pause between tree wave end and ground wave start.")]
        public float gapBeforeGround = 0.1f;
        [Tooltip("Pause between ground wave end and leaves reveal.")]
        public float gapBeforeLeaves = 0.2f;

        public enum Stage { Idle, TreeWave, GroundWave, LeavesReveal, LivingIdle }
        public Stage CurrentStage { get; private set; } = Stage.Idle;

        bool m_started;

        public void Begin()
        {
            if (m_started) return;
            m_started = true;
            StartCoroutine(Run());
        }

        IEnumerator Run()
        {
            // Tree wave
            CurrentStage = Stage.TreeWave;
            if (treeWave != null)
            {
                treeWave.Play();
                while (!treeWave.IsDone) yield return null;
            }

            yield return new WaitForSeconds(gapBeforeGround);

            // Ground wave
            CurrentStage = Stage.GroundWave;
            if (groundWave != null)
            {
                groundWave.Play();
                while (!groundWave.IsDone) yield return null;
            }

            yield return new WaitForSeconds(gapBeforeLeaves);

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
    }
}

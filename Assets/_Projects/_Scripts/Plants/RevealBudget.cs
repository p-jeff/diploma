using UnityEngine;

namespace Plants
{
    /// <summary>
    /// Garden-wide throttle for the EXPENSIVE first frame of a gsplat reveal.
    ///
    /// A <see cref="Gsplat.Animation.GsplatRevealAnimator"/> (and the two-capture
    /// <c>GsplatSplatMorph</c>) builds its morph scratch the first frame an instance becomes active
    /// at progress &lt; 1: an O(n) CPU pass over every gaussian plus several GraphicsBuffer uploads.
    /// Profiling the standalone flourish showed that when overlapping species cascades wake many
    /// scatter clones on the same frame, those builds pile up into a single ~46 ms spike
    /// (GsplatRevealAnimator.Update), blowing the 72 Hz (13.89 ms) budget.
    ///
    /// Reveal cascades therefore claim a slot here before they activate each fresh instance, so at
    /// most <see cref="PerFrame"/> heavy builds BEGIN per frame across the whole garden — no matter
    /// how the per-instance (<c>likedStaggerDelay</c>) and per-species (<c>flourishSpeciesStagger</c>)
    /// staggers happen to align, and no matter how asset uploads batch. An instance waiting for a
    /// slot stays inactive (invisible), so there is no full-detail flash and no change to the
    /// reveal's look — only its frame pacing under load.
    ///
    /// Set from <see cref="ExperienceManager"/> (its <c>revealBuildsPerFrame</c> field). Defaults to
    /// a safe 1 so a scene without an ExperienceManager still self-throttles.
    /// </summary>
    public static class RevealBudget
    {
        /// <summary>Max heavy reveal-builds that may BEGIN per frame, garden-wide. Clamped to ≥ 1.</summary>
        public static int PerFrame = 1;

        static int s_frame = -1;
        static int s_used;

        /// <summary>
        /// Try to claim one build slot for the current frame. Returns false once the frame's budget
        /// is spent — callers should <c>yield return null</c> and retry next frame.
        /// </summary>
        public static bool TryConsume()
        {
            int f = Time.frameCount;
            if (f != s_frame) { s_frame = f; s_used = 0; }   // new frame: refill
            if (s_used >= Mathf.Max(1, PerFrame)) return false;
            s_used++;
            return true;
        }
    }
}

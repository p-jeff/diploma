using System.Collections;
using Gsplat;
using Gsplat.Animation;
using UnityEngine;

namespace Plants
{
    /// <summary>
    /// Fire-and-forget fade-out component. Added at runtime to a spawned instance root;
    /// lerps _GsplatOpacityMul from 1 → 0.002 over <paramref name="duration"/> seconds,
    /// then destroys the root.
    ///
    /// SHADER GOTCHA: _GsplatOpacityMul ≤ 0.0001 is treated as 1.0 (full opacity) by
    /// the shader. Never set exactly 0. Final resting value is 0.002.
    /// </summary>
    public class GsplatInstanceFader : MonoBehaviour
    {
        static readonly int s_opacityMulId = Shader.PropertyToID("_GsplatOpacityMul");

        /// <summary>
        /// Add this component to <paramref name="root"/>, kick off a coroutine that fades
        /// every GsplatRenderer child to near-zero opacity, then destroys <paramref name="root"/>.
        /// </summary>
        public static void FadeOutAndDestroy(GameObject root, float duration)
        {
            if (root == null) return;
            var fader = root.AddComponent<GsplatInstanceFader>();
            fader.StartCoroutine(fader.FadeRoutine(root, duration));
        }

        private IEnumerator FadeRoutine(GameObject root, float duration)
        {
            // Optionally trigger reverse animation on any reveal animators.
            var animators = root.GetComponentsInChildren<GsplatRevealAnimator>(true);
            foreach (var a in animators)
            {
                if (a == null) continue;
                // Set duration to match fade so reverse aligns with opacity fade.
                a.duration = duration;
                a.PlayReverse();
            }

            var renderers = root.GetComponentsInChildren<GsplatRenderer>(true);

            float elapsed = 0f;
            float safeDuration = Mathf.Max(duration, 0.0001f);

            while (elapsed < safeDuration)
            {
                elapsed += Time.deltaTime;
                // Lerp 1 → 0.002 (never go to or below 0.0001 due to shader quirk)
                float t = Mathf.Clamp01(elapsed / safeDuration);
                float opacity = Mathf.Lerp(1f, 0.002f, t);

                foreach (var r in renderers)
                {
                    if (r == null) continue;
                    var pb = r.PropertyBlock;
                    if (pb == null) continue;
                    pb.SetFloat(s_opacityMulId, opacity);
                }

                yield return null;
            }

            // Ensure final value is set before destroy
            foreach (var r in renderers)
            {
                if (r == null) continue;
                var pb = r.PropertyBlock;
                if (pb != null) pb.SetFloat(s_opacityMulId, 0.002f);
            }

            Destroy(root);
        }
    }
}

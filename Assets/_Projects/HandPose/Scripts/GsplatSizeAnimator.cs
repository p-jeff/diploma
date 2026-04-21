using UnityEngine;

namespace Gsplat.Animation
{
    /// <summary>
    /// Animates the size/scale of a Gaussian splat renderer by driving SplatDownscaleFactor.
    /// Works on Quest 3 standalone — no shader modifications needed.
    /// </summary>
    public class GsplatSizeAnimator : MonoBehaviour
    {
        [Header("Scale Range")]
        [Tooltip("Base downscale factor (0 = full size, 1 = invisible)")]
        [Range(0f, 1f)]
        public float baseDownscale = 0f;

        [Tooltip("How much the downscale factor oscillates")]
        [Range(0f, 0.5f)]
        public float amplitude = 0.15f;

        [Header("Animation")]
        [Tooltip("Oscillation speed in Hz")]
        public float frequency = 0.5f;

        [Tooltip("Enable/disable the animation")]
        public bool animate = true;

        GsplatRenderer m_renderer;

        void Awake()
        {
            m_renderer = GetComponent<GsplatRenderer>();
        }

        void Update()
        {
            if (!animate || m_renderer == null)
                return;

            float t = Mathf.Sin(Time.time * frequency * 2f * Mathf.PI);
            // Map sin [-1,1] to [base - amplitude, base + amplitude], clamped to [0,1]
            float downscale = Mathf.Clamp01(baseDownscale + t * amplitude);
            m_renderer.SplatDownscaleFactor = downscale;
        }

        /// <summary>
        /// Set scale directly (0 = full size, 1 = invisible).
        /// Disables animation.
        /// </summary>
        public void SetScale(float downscaleFactor)
        {
            animate = false;
            if (m_renderer != null)
                m_renderer.SplatDownscaleFactor = Mathf.Clamp01(downscaleFactor);
        }

        /// <summary>
        /// Set scale directly as a 0-1 "size" value (1 = full size, 0 = invisible).
        /// More intuitive than downscale factor.
        /// </summary>
        public void SetSize(float size)
        {
            SetScale(1f - Mathf.Clamp01(size));
        }
    }
}

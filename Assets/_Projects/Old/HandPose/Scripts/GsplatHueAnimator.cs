using UnityEngine;

namespace Gsplat.Animation
{
    /// <summary>
    /// Animates hue shift and tint color of Gaussian splats.
    /// REQUIRES the modified Gsplat.shader (see Assets/_Claude/ShaderPatches/).
    /// Uses global shader properties so it works without modifying GsplatRendererImpl.
    /// Works on Quest 3 standalone (~15 ALU ops in fragment shader).
    /// </summary>
    public class GsplatHueAnimator : MonoBehaviour
    {
        [Header("Hue Shift")]
        [Tooltip("Base hue rotation (0-1 = full color wheel)")]
        [Range(0f, 1f)]
        public float baseHueShift = 0f;

        [Tooltip("Hue oscillation amplitude")]
        [Range(0f, 0.5f)]
        public float hueAmplitude = 0f;

        [Tooltip("Hue oscillation speed in Hz")]
        public float hueFrequency = 0.2f;

        [Header("Tint Color")]
        [Tooltip("Color multiplier applied to all splats")]
        public Color tintColor = Color.white;

        [Header("Opacity")]
        [Tooltip("Global opacity multiplier (0 = invisible, 1 = normal)")]
        [Range(0f, 1f)]
        public float opacity = 1f;

        [Header("Animation")]
        public bool animateHue = false;

        static readonly int s_hueShift = Shader.PropertyToID("_GsplatHueShift");
        static readonly int s_tintColor = Shader.PropertyToID("_GsplatTintColor");
        static readonly int s_opacityMul = Shader.PropertyToID("_GsplatOpacityMul");

        void OnEnable()
        {
            // Set defaults so shader works even without animation
            Shader.SetGlobalFloat(s_hueShift, 0f);
            Shader.SetGlobalColor(s_tintColor, Color.white);
            Shader.SetGlobalFloat(s_opacityMul, 1f);
        }

        void OnDisable()
        {
            // Reset to identity values
            Shader.SetGlobalFloat(s_hueShift, 0f);
            Shader.SetGlobalColor(s_tintColor, Color.white);
            Shader.SetGlobalFloat(s_opacityMul, 1f);
        }

        void Update()
        {
            float hue = baseHueShift;
            if (animateHue)
            {
                float t = Mathf.Sin(Time.time * hueFrequency * 2f * Mathf.PI);
                hue += t * hueAmplitude;
            }

            Shader.SetGlobalFloat(s_hueShift, hue);
            Shader.SetGlobalColor(s_tintColor, tintColor);
            Shader.SetGlobalFloat(s_opacityMul, opacity);
        }

        /// <summary>
        /// Set hue shift directly (0-1 maps to 0-360 degrees).
        /// Disables animation.
        /// </summary>
        public void SetHueShift(float hue)
        {
            animateHue = false;
            baseHueShift = hue % 1f;
        }

        /// <summary>
        /// Set tint color directly.
        /// </summary>
        public void SetTintColor(Color color)
        {
            tintColor = color;
        }

        /// <summary>
        /// Set opacity (0 = invisible, 1 = fully visible).
        /// </summary>
        public void SetOpacity(float value)
        {
            opacity = Mathf.Clamp01(value);
        }
    }
}

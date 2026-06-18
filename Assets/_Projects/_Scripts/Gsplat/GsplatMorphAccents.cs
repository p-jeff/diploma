using UnityEngine;

namespace Gsplat.Animation
{
    /// <summary>
    /// Colour accents for the morph. Unlike the geometry effects this touches no
    /// compute kernel — it drives the shader uniforms the modified Gsplat.shader already exposes
    /// (hue shift, tint), pulsing them with a sin(PI*t) envelope so they peak mid-transition
    /// and settle to the real colours at both endpoints.
    ///
    /// Written through the host renderer's MaterialPropertyBlock (the same path the existing
    /// GsplatShockwaveAnimator uses) so it binds reliably regardless of render pipeline.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(GsplatSplatMorph))]
    public class GsplatMorphAccents : MonoBehaviour
    {
        [Header("Hue sweep")]
        public bool hueEnabled = true;
        [Tooltip("Peak hue rotation at mid-morph (0..1 = full colour wheel).")]
        [Range(0f, 1f)] public float hueAmount = 0.12f;

        [Header("Tint pulse")]
        public bool tintEnabled = false;
        public Color tintColor = new Color(1f, 0.6f, 0.9f, 1f);

        GsplatSplatMorph m_morph;
        GsplatRenderer m_renderer;

        static readonly int s_hue          = Shader.PropertyToID("_GsplatHueShift");
        static readonly int s_tint         = Shader.PropertyToID("_GsplatTintColor");

        void Awake() => CacheRefs();

        void OnEnable() => CacheRefs();

        void CacheRefs()
        {
            if (m_morph == null) m_morph = GetComponent<GsplatSplatMorph>();
            if (m_renderer == null) m_renderer = GetComponent<GsplatRenderer>();
        }

        void Update()
        {
            CacheRefs();
            var pb = m_renderer != null ? m_renderer.PropertyBlock : null;
            if (pb == null)
                return; // renderer not initialized yet

            float t = m_morph != null ? m_morph.CurrentT : 0f;
            float bump = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t));

            pb.SetFloat(s_hue, hueEnabled ? hueAmount * bump : 0f);

            if (tintEnabled)
            {
                Color c = Color.Lerp(Color.white, tintColor, bump);
                pb.SetVector(s_tint, new Vector4(c.r, c.g, c.b, 1f));
            }
            else
            {
                pb.SetVector(s_tint, new Vector4(1f, 1f, 1f, 1f)); // identity (white)
            }
        }

        void OnDisable() => ResetUniforms();

        void ResetUniforms()
        {
            var pb = m_renderer != null ? m_renderer.PropertyBlock : null;
            if (pb == null)
                return;
            pb.SetFloat(s_hue, 0f);
            pb.SetVector(s_tint, new Vector4(1f, 1f, 1f, 1f));
        }
    }
}

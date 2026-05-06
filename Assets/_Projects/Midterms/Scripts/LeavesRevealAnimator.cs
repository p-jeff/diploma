using System.Collections;
using UnityEngine;

namespace Midterms
{
    /// <summary>
    /// Fades a leaves splat in via opacity, then plays a subtle "breathing" idle:
    /// small size oscillation + small hue drift. All per-renderer via PropertyBlock.
    /// </summary>
    [RequireComponent(typeof(Gsplat.GsplatRenderer))]
    public class LeavesRevealAnimator : MonoBehaviour
    {
        [Header("Reveal")]
        public float fadeInDuration = 1.2f;
        public AnimationCurve fadeCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

        [Header("Idle Breathing (after reveal)")]
        [Tooltip("Size oscillation amplitude as a fraction (0.02 = 2%).")]
        [Range(0f, 0.1f)] public float sizeAmplitude = 0.02f;
        [Tooltip("Hue oscillation amplitude (0..1).")]
        [Range(0f, 0.1f)] public float hueAmplitude = 0.01f;
        [Tooltip("Cycles per second.")]
        public float frequency = 0.25f;

        Gsplat.GsplatRenderer m_renderer;
        bool m_revealed;
        float m_baseDownscale;
        Coroutine m_running;

        static readonly int s_opacity   = Shader.PropertyToID("_GsplatOpacityMul");
        static readonly int s_hueShift  = Shader.PropertyToID("_GsplatHueShift");

        void Awake()
        {
            m_renderer = GetComponent<Gsplat.GsplatRenderer>();
            m_baseDownscale = m_renderer.SplatDownscaleFactor;
        }

        void Start()
        {
            // Begin invisible.
            StartCoroutine(DeferSetOpacity(0f));
        }

        IEnumerator DeferSetOpacity(float v)
        {
            while (m_renderer.PropertyBlock == null) yield return null;
            m_renderer.PropertyBlock.SetFloat(s_opacity, v);
        }

        public void Reveal()
        {
            if (m_running != null) StopCoroutine(m_running);
            m_running = StartCoroutine(RevealCoroutine());
        }

        IEnumerator RevealCoroutine()
        {
            while (m_renderer.PropertyBlock == null) yield return null;
            var pb = m_renderer.PropertyBlock;

            float t = 0f;
            while (t < fadeInDuration)
            {
                float u = fadeCurve.Evaluate(t / fadeInDuration);
                pb.SetFloat(s_opacity, u);
                t += Time.deltaTime;
                yield return null;
            }
            pb.SetFloat(s_opacity, 1f);
            m_revealed = true;
            m_running = null;
        }

        void Update()
        {
            if (!m_revealed) return;
            var pb = m_renderer.PropertyBlock;
            if (pb == null) return;

            float phase = Mathf.Sin(Time.time * frequency * 2f * Mathf.PI);

            // Size: animate the renderer's downscale factor (existing public field).
            m_renderer.SplatDownscaleFactor = Mathf.Clamp01(m_baseDownscale + phase * sizeAmplitude);

            // Hue: per-renderer drift.
            pb.SetFloat(s_hueShift, phase * hueAmplitude);
        }

        public bool IsRevealed => m_revealed;
    }
}

using System.Collections;
using UnityEngine;

namespace Midterms
{
    /// <summary>
    /// Fades a leaves splat in via opacity, then plays a "breathing" idle:
    /// size oscillation + hue drift + small radial puff. All per-renderer via PropertyBlock.
    ///
    /// During Reveal(), an outward puff displaces splats from the anchor and decays as
    /// opacity fades in, giving the leaves a sense of bursting into being.
    /// </summary>
    [RequireComponent(typeof(Gsplat.GsplatRenderer))]
    public class LeavesRevealAnimator : MonoBehaviour
    {
        [Header("Reveal")]
        public float fadeInDuration = 1.2f;
        public AnimationCurve fadeCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

        [Tooltip("Outward puff at start of reveal (meters). Decays to 0 as opacity reaches full.")]
        public float revealDisplaceAmount = 0.15f;

        [Tooltip("If null, uses transform.position as the puff/idle origin.")]
        public Transform anchorOverride;

        [Header("Idle Sparkle (after reveal)")]
        [Tooltip("Sparkle strength. 0 = off, ~0.5 = subtle twinkle, 1+ = strong.")]
        [Range(0f, 2f)] public float sparkleIntensity = 0.6f;
        [Tooltip("Twinkle speed (cycles per second). Higher = faster flickers.")]
        [Range(0f, 10f)] public float sparkleSpeed = 2.5f;

        Gsplat.GsplatRenderer m_renderer;
        bool m_revealed;
        Coroutine m_running;

        static readonly int s_opacity      = Shader.PropertyToID("_GsplatOpacityMul");
        static readonly int s_shockCenter  = Shader.PropertyToID("_GsplatShockCenter");
        static readonly int s_shockAxis    = Shader.PropertyToID("_GsplatShockAxis");
        static readonly int s_shockProgress= Shader.PropertyToID("_GsplatShockProgress");
        static readonly int s_shockBand    = Shader.PropertyToID("_GsplatShockBandWidth");
        static readonly int s_shockDisp    = Shader.PropertyToID("_GsplatShockDisplace");
        static readonly int s_sparkleI     = Shader.PropertyToID("_GsplatSparkleIntensity");
        static readonly int s_sparkleS     = Shader.PropertyToID("_GsplatSparkleSpeed");

        Vector3 Anchor => anchorOverride ? anchorOverride.position : transform.position;

        void Awake()
        {
            m_renderer = GetComponent<Gsplat.GsplatRenderer>();
        }

        void Start()
        {
            // Begin invisible.
            StartCoroutine(DeferInitial());
        }

        IEnumerator DeferInitial()
        {
            while (m_renderer.PropertyBlock == null) yield return null;
            var pb = m_renderer.PropertyBlock;
            pb.SetFloat(s_opacity, 0f);
            ConfigurePuff(pb);
            pb.SetFloat(s_shockDisp, 0f);
            pb.SetFloat(s_sparkleI, 0f);
        }

        // Sets up the wave uniforms so that _GsplatShockDisplace produces a near-uniform outward push.
        void ConfigurePuff(MaterialPropertyBlock pb)
        {
            pb.SetVector(s_shockCenter, Anchor);
            pb.SetVector(s_shockAxis, Vector4.zero);   // radial mode
            pb.SetFloat(s_shockProgress, 0f);
            // Big band so the gaussian pulse ~1 across the whole leaves cloud.
            pb.SetFloat(s_shockBand, 5f);
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
            ConfigurePuff(pb);

            float t = 0f;
            while (t < fadeInDuration)
            {
                float u = fadeCurve.Evaluate(t / fadeInDuration);
                pb.SetFloat(s_opacity, u);
                // Displace decays from revealDisplaceAmount to 0 as opacity grows.
                pb.SetFloat(s_shockDisp, revealDisplaceAmount * (1f - u));
                t += Time.deltaTime;
                yield return null;
            }
            pb.SetFloat(s_opacity, 1f);
            pb.SetFloat(s_shockDisp, 0f);
            m_revealed = true;
            m_running = null;
        }

        void Update()
        {
            if (!m_revealed) return;
            var pb = m_renderer.PropertyBlock;
            if (pb == null) return;

            pb.SetFloat(s_sparkleI, sparkleIntensity);
            pb.SetFloat(s_sparkleS, sparkleSpeed);
        }

        public bool IsRevealed => m_revealed;

        public void Hide()
        {
            m_revealed = false;
            if (m_running != null) StopCoroutine(m_running);
            m_running = null;
            StartCoroutine(DeferInitial());
        }

        [ContextMenu("Reveal")] void DebugReveal() => Reveal();
        [ContextMenu("Hide")]   void DebugHide()   => Hide();
    }
}

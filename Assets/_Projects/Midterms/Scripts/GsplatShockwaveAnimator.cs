using System.Collections;
using UnityEngine;

namespace Midterms
{
    /// <summary>
    /// Plays a single shockwave on one GsplatRenderer.
    /// Drives the renderer's MaterialPropertyBlock with _GsplatDesat and the four wave uniforms.
    /// REQUIRES the modified Gsplat.shader (saturation + shockwave additions).
    /// </summary>
    [RequireComponent(typeof(Gsplat.GsplatRenderer))]
    public class GsplatShockwaveAnimator : MonoBehaviour
    {
        public enum ShockMode { Vertical, Radial }

        [Header("Mode")]
        public ShockMode mode = ShockMode.Vertical;

        [Tooltip("World-space origin of the wave. If null, uses this transform's position.")]
        public Transform centerOverride;

        [Tooltip("For Vertical mode only — direction the wave travels.")]
        public Vector3 verticalAxis = Vector3.up;

        [Header("Timing")]
        public float duration = 1.5f;
        [Tooltip("Soft transition band (meters).")]
        public float bandWidth = 0.25f;
        [Tooltip("Distance the wave front travels (meters). Tree height for vertical, ground radius for radial.")]
        public float maxDistance = 3f;
        public AnimationCurve progressCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

        [Header("Displacement (wavefront pulse)")]
        [Tooltip("Peak world-space push at the wave front (meters). 0 = no displacement. Vertical mode pushes along axis; radial mode pushes outward.")]
        public float displaceAmount = 0f;

        [Header("Initial State")]
        [Tooltip("If true, on Start the splat is set fully greyscale and the wave is parked beyond the splat.")]
        public bool startGreyscale = true;

        Gsplat.GsplatRenderer m_renderer;
        bool m_done;
        Coroutine m_running;

        static readonly int s_desat        = Shader.PropertyToID("_GsplatDesat");
        static readonly int s_shockCenter  = Shader.PropertyToID("_GsplatShockCenter");
        static readonly int s_shockAxis    = Shader.PropertyToID("_GsplatShockAxis");
        static readonly int s_shockProgress= Shader.PropertyToID("_GsplatShockProgress");
        static readonly int s_shockBand    = Shader.PropertyToID("_GsplatShockBandWidth");
        static readonly int s_shockDisp    = Shader.PropertyToID("_GsplatShockDisplace");

        public bool IsDone => m_done;

        void Awake()
        {
            m_renderer = GetComponent<Gsplat.GsplatRenderer>();
        }

        void Start()
        {
            if (startGreyscale)
                ApplyInitialGreyscale();
        }

        void OnDisable()
        {
            if (m_running != null) StopCoroutine(m_running);
            m_running = null;
        }

        Vector3 Center => centerOverride ? centerOverride.position : transform.position;

        Vector4 AxisVec()
        {
            if (mode == ShockMode.Radial) return Vector4.zero;
            var n = verticalAxis.sqrMagnitude > 1e-6f ? verticalAxis.normalized : Vector3.up;
            return new Vector4(n.x, n.y, n.z, 0f);
        }

        bool TryGetBlock(out MaterialPropertyBlock pb)
        {
            pb = m_renderer != null ? m_renderer.PropertyBlock : null;
            return pb != null;
        }

        public void ApplyInitialGreyscale()
        {
            if (!TryGetBlock(out var pb))
            {
                // PropertyBlock isn't created until the renderer's first Update. Defer.
                StartCoroutine(DeferredApplyInitial());
                return;
            }
            WriteInitial(pb);
        }

        IEnumerator DeferredApplyInitial()
        {
            while (!TryGetBlock(out _)) yield return null;
            WriteInitial(m_renderer.PropertyBlock);
        }

        void WriteInitial(MaterialPropertyBlock pb)
        {
            pb.SetFloat(s_desat, 1f);
            pb.SetVector(s_shockCenter, Center);
            pb.SetVector(s_shockAxis, AxisVec());
            // Park the wave far before any splat -> all splats are "ahead" -> waveT=0 -> grey.
            pb.SetFloat(s_shockProgress, -1e6f);
            pb.SetFloat(s_shockBand, Mathf.Max(bandWidth, 0.0001f));
        }

        [Header("Debug")]
        public bool verboseLogging = true;

        public void Play()
        {
            if (verboseLogging)
                Debug.Log($"[Shockwave:{name}] Play() called. mode={mode} center={Center} maxDistance={maxDistance} duration={duration} enabled={isActiveAndEnabled} hasRenderer={m_renderer!=null}", this);

            if (!isActiveAndEnabled)
            {
                Debug.LogWarning($"[Shockwave:{name}] Component or GameObject is disabled — coroutine cannot run.", this);
                return;
            }
            if (m_running != null) StopCoroutine(m_running);
            m_done = false;
            m_running = StartCoroutine(PlayCoroutine());
        }

        IEnumerator PlayCoroutine()
        {
            int waitFrames = 0;
            while (!TryGetBlock(out _))
            {
                waitFrames++;
                if (waitFrames == 1 && verboseLogging)
                    Debug.Log($"[Shockwave:{name}] PropertyBlock not ready yet, waiting…", this);
                yield return null;
            }
            var pb = m_renderer.PropertyBlock;
            if (verboseLogging)
                Debug.Log($"[Shockwave:{name}] PropertyBlock acquired after {waitFrames} frames. Starting wave from progress=0 to {maxDistance} over {duration}s.", this);

            pb.SetFloat(s_desat, 1f);
            pb.SetVector(s_shockCenter, Center);
            pb.SetVector(s_shockAxis, AxisVec());
            pb.SetFloat(s_shockBand, Mathf.Max(bandWidth, 0.0001f));
            pb.SetFloat(s_shockDisp, displaceAmount);

            float t = 0f;
            float lastLog = -1f;
            while (t < duration)
            {
                float u = progressCurve.Evaluate(t / duration);
                float prog = u * maxDistance;
                pb.SetFloat(s_shockProgress, prog);
                if (verboseLogging && t - lastLog >= 0.25f)
                {
                    Debug.Log($"[Shockwave:{name}] t={t:F2}/{duration:F2} progress={prog:F3}", this);
                    lastLog = t;
                }
                t += Time.deltaTime;
                yield return null;
            }

            // Finalize: park the wave well past every splat, drop displacement, lock colored state.
            pb.SetFloat(s_shockProgress, maxDistance + 1e6f);
            pb.SetFloat(s_shockDisp, 0f);
            pb.SetFloat(s_desat, 0f);
            m_done = true;
            m_running = null;
            if (verboseLogging)
                Debug.Log($"[Shockwave:{name}] Wave complete. Desat=0 (locked colored).", this);
        }

        public void ForceColored()
        {
            if (!TryGetBlock(out var pb)) return;
            pb.SetFloat(s_desat, 0f);
            pb.SetFloat(s_shockProgress, maxDistance + 1e6f);
            m_done = true;
        }

        [ContextMenu("Play")]                void DebugPlay()    => Play();
        [ContextMenu("Reset to Greyscale")]  void DebugReset()   => ApplyInitialGreyscale();
        [ContextMenu("Force Colored")]       void DebugColored() => ForceColored();

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            Gizmos.color = mode == ShockMode.Vertical ? Color.cyan : Color.yellow;
            Vector3 c = centerOverride ? centerOverride.position : transform.position;
            if (mode == ShockMode.Radial)
                Gizmos.DrawWireSphere(c, maxDistance);
            else
                Gizmos.DrawLine(c, c + verticalAxis.normalized * maxDistance);
        }
#endif
    }
}

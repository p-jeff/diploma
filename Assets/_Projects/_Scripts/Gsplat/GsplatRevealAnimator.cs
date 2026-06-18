using System.Runtime.InteropServices;
using UnityEngine;

namespace Gsplat.Animation
{
    /// <summary>
    /// Self-contained "reveal" animation for a SINGLE Gaussian splat — no second capture required.
    ///
    /// Where <see cref="GsplatSplatMorph"/> blends two separate PLY captures (coarse -> detailed),
    /// this derives its progress-0 endpoint procedurally from the host splat itself: a smaller,
    /// half-desaturated copy of the same cloud. As <see cref="progress"/> climbs to 1 the splat
    /// grows to full size, regains its colour, blooms in along <see cref="revealAxis"/> (staggered
    /// spatial reveal, splats fading in as the wavefront reaches them) with a light scatter burst,
    /// landing exactly on the untouched host at progress 1.
    ///
    /// This is the spiritual replacement for the old GsplatShockwaveAnimator: same one-component,
    /// one-renderer, variable-duration <see cref="Play"/> workflow, but driving the geometric morph
    /// pipeline (GsplatMorph.compute, MORPH_BLOOM + MORPH_SCATTER) instead of a shader shockwave.
    ///
    /// Attach to the GsplatRenderer you want to reveal, assign <see cref="computeShader"/>
    /// (GsplatMorph.compute), then call <see cref="Play"/> or scrub <see cref="progress"/> live.
    /// </summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(-100)] // run before GsplatRenderer (order 0) so the blend is ready for its depth sort
    [RequireComponent(typeof(GsplatRenderer))]
    public class GsplatRevealAnimator : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("GsplatMorph.compute.")]
        public ComputeShader computeShader;

        [Header("Reveal")]
        [Tooltip("0 = small/desaturated start state, 1 = the full host splat. Scrub live or drive via Play().")]
        [Range(0f, 1f)] public float progress = 0f;

        [Tooltip("Remaps progress -> reveal t. Ease-in/out by default.")]
        public AnimationCurve curve = AnimationCurve.EaseInOut(0, 0, 1, 1);

        [Tooltip("Reveal t the cloud rests at when progress = 0. >0 means the resting state already looks part-bloomed (e.g. 0.1 = 'MorphBloom at 0.1') instead of fully hidden. Progress drives t from here up to 1.")]
        [Range(0f, 0.5f)] public float startAt = 0.1f;

        [Header("Start state (the t=0 endpoint these interpolate from)")]
        [Tooltip("Gaussian size at the t=0 endpoint, relative to full. Each splat grows from here to full size in log-space along its bloom-staggered t — this is the size growth you see in a MorphBloom reveal. 1 = no shrink.")]
        [Range(0.05f, 1f)] public float startGaussianScale = 0.6f;

        [Tooltip("How far the cloud collapses toward its centroid at the t=0 endpoint. 1 = flower stays in place (matches MorphBloom, where src/dst positions are registered); <1 = implodes inward and springs back out.")]
        [Range(0.05f, 1f)] public float startPositionScale = 1f;

        [Tooltip("How grey the t=0 endpoint is. 0 = full colour, 0.5 = half desaturated, 1 = fully greyscale. The finish (t=1) is always the fully saturated host.")]
        [Range(0f, 1f)] public float startDesaturation = 0.5f;

        [Header("Bloom (staggered spatial reveal)")]
        [Tooltip("Direction the reveal sweeps along (LOCAL space). Default +Z = world-up for this -90deg X-rotated renderer.")]
        public Vector3 revealAxis = Vector3.forward;

        [Tooltip("Reveal outward from the centre instead of along an axis.")]
        public bool radial = false;

        [Tooltip("Fade each splat in as the wavefront reaches it, so the cloud visibly assembles along the sweep.")]
        public bool reveal = true;

        [Tooltip("Fraction of the timeline each individual splat's transition occupies. Small = sharp travelling wave, large = soft overlap.")]
        [Range(0.05f, 1f)] public float bandFraction = 0.4f;

        [Tooltip("Reverse the sweep direction.")]
        public bool invert = false;

        [Header("Scatter (burst)")]
        [Tooltip("Peak per-splat displacement at mid-reveal (metres). 0 = no scatter.")]
        public float scatterAmount = 0.02f;

        [Tooltip("Spatial frequency of the scatter pattern. Higher = finer, more incoherent swarm.")]
        public float scatterFrequency = 8f;

        [Tooltip("Changes the random scatter pattern.")]
        public float scatterSeed = 0f;

        [Header("Playback")]
        [Tooltip("Seconds for Play() to drive progress 0 -> 1.")]
        public float duration = 1.5f;

        [Tooltip("On enable, park progress at 0 (small/desaturated/hidden) so a later Play() reveals from scratch.")]
        public bool resetOnEnable = true;

        [Tooltip("Allow scrubbing/playing the reveal to preview live in the editor without Play mode.")]
        public bool previewInEditMode = true;

        // ---- internals ----
        GsplatRenderer m_host;
        GsplatAssetUncompressed m_hostAsset;
        GsplatResourceUncompressed m_hostResource;

        GraphicsBuffer m_srcPos, m_srcScale, m_srcRot, m_srcColor;
        GraphicsBuffer m_dstPos, m_dstScale, m_dstRot, m_dstColor;

        int m_kernel;
        uint m_count;
        bool m_initialized;
        Vector3 m_hostCentroid;

        // Cached params the Src buffers were baked with, so we can rebuild only when they change.
        float m_bakedGScale = -1f, m_bakedPScale = -1f, m_bakedDesat = -1f;

        float m_playFrom, m_playTo, m_playStart, m_playDur;
        bool m_playing;

        // The morphed resting state is static, so we only dispatch the compute when something actually
        // changed (a tween is running, progress was scrubbed, the start-state was rebaked, or we just
        // borrowed a fresh private buffer). A dormant plant parked at progress 0 therefore costs zero
        // per-frame GPU work — it just keeps drawing the last morphed buffer.
        float m_lastDispatchT = float.NaN;
        bool m_forceDispatch;

        // Private-buffer morph: while actively morphing we render from a pooled PRIVATE resource so
        // our per-frame writes never touch the asset's SHARED "full detail" buffer that completed /
        // static same-asset instances read. Borrowed on activate, returned on settle/disable.
        const float k_settleEps = 1e-4f;
        bool m_active;
        GsplatResource m_privateResource;
        GsplatAssetUncompressed m_privateAsset;

        /// <summary>True once a Play() reveal has finished (or ForceColored was called).</summary>
        public bool IsDone { get; private set; }

        const string k_kwBloom   = "MORPH_BLOOM";
        const string k_kwSwirl   = "MORPH_SWIRL";
        const string k_kwScatter = "MORPH_SCATTER";

        static readonly int s_count    = Shader.PropertyToID("_Count");
        static readonly int s_t        = Shader.PropertyToID("_T");
        static readonly int s_srcPos   = Shader.PropertyToID("_SrcPos");
        static readonly int s_srcScale = Shader.PropertyToID("_SrcScale");
        static readonly int s_srcRot   = Shader.PropertyToID("_SrcRot");
        static readonly int s_srcColor = Shader.PropertyToID("_SrcColor");
        static readonly int s_dstPos   = Shader.PropertyToID("_DstPos");
        static readonly int s_dstScale = Shader.PropertyToID("_DstScale");
        static readonly int s_dstRot   = Shader.PropertyToID("_DstRot");
        static readonly int s_dstColor = Shader.PropertyToID("_DstColor");
        static readonly int s_outPos   = Shader.PropertyToID("_OutPos");
        static readonly int s_outScale = Shader.PropertyToID("_OutScale");
        static readonly int s_outRot   = Shader.PropertyToID("_OutRot");
        static readonly int s_outColor = Shader.PropertyToID("_OutColor");

        // Bloom uniforms (match GsplatMorph.compute / GsplatMorphBloom).
        static readonly int s_bloomOrigin = Shader.PropertyToID("_BloomOrigin");
        static readonly int s_bloomDir    = Shader.PropertyToID("_BloomDir");
        static readonly int s_bloomLength = Shader.PropertyToID("_BloomLength");
        static readonly int s_bloomBand   = Shader.PropertyToID("_BloomBand");
        static readonly int s_bloomInvert = Shader.PropertyToID("_BloomInvert");
        static readonly int s_bloomRadial = Shader.PropertyToID("_BloomRadial");
        static readonly int s_bloomReveal = Shader.PropertyToID("_BloomReveal");

        // Scatter uniforms.
        static readonly int s_scatterAmount = Shader.PropertyToID("_ScatterAmount");
        static readonly int s_scatterFreq   = Shader.PropertyToID("_ScatterFreq");
        static readonly int s_scatterSeed   = Shader.PropertyToID("_ScatterSeed");

        void Awake() => m_host = GetComponent<GsplatRenderer>();

        void OnEnable()
        {
            if (m_host == null) m_host = GetComponent<GsplatRenderer>();
            m_initialized = false;
            if (resetOnEnable)
            {
                progress = 0f;
                m_playing = false;
                IsDone = false;
            }
#if UNITY_EDITOR
            EnsureEditorPump();
#endif
        }

        void OnDisable()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update -= EditorTick;
#endif
            Deactivate();      // return the private buffer to the pool, revert to shared, free scratch
            DisposeBuffers();  // defensive: also free scratch if we were settled / never activated
            m_initialized = false;
        }

        void OnDestroy() => DisposeBuffers();

        void Update()
        {
            if (computeShader == null)
                return;

            // Advance any scripted tween first so the settle decision below sees the latest progress.
            if (m_playing)
            {
                float k = Mathf.Clamp01((Time.time - m_playStart) / Mathf.Max(m_playDur, 0.0001f));
                progress = Mathf.Lerp(m_playFrom, m_playTo, k);
                if (k >= 1f)
                {
                    m_playing = false;
                    IsDone = Mathf.Approximately(m_playTo, 1f);
                }
            }

            // Settled at progress 1: the morph output equals the untouched asset, which is exactly
            // what the SHARED buffer already holds. Stop dispatching, hand the private buffer + the
            // per-instance scratch back, and let the renderer draw the shared buffer for free. This
            // is both the perf win (no per-frame compute / buffer churn) and what keeps a revealed
            // splat revealed — once we're off the shared buffer, a grey same-asset copy can no
            // longer overwrite us.
            bool settled = !m_playing && progress >= 1f - k_settleEps;
            if (settled)
            {
                if (m_active) Deactivate();
                return;
            }

            // Active morph. We must own a private buffer before writing so we never touch the shared
            // one. Wait until the renderer has bound its asset (GsplatResource non-null).
            if (m_host == null || m_host.GsplatResource == null)
                return;
            if (!m_active) Activate();
            // Re-assert the override in case the renderer rebound its shared resource (asset reload).
            if (m_active && m_privateResource != null && m_host.GsplatResource != m_privateResource)
                m_host.SetResourceOverride(m_privateResource);

            if (!TryInitialize())
                return;

            // Rebake the start endpoint if the start-state knobs were scrubbed.
            if (!Mathf.Approximately(m_bakedGScale, startGaussianScale) ||
                !Mathf.Approximately(m_bakedPScale, startPositionScale) ||
                !Mathf.Approximately(m_bakedDesat, startDesaturation))
                BuildSourceBuffers();

            // progress 0..1 -> reveal t, floored at startAt so the resting state still reads as a
            // part-bloomed cloud ("MorphBloom at 0.1") rather than the fully-hidden t=0 endpoint.
            float p = curve != null ? curve.Evaluate(progress) : progress;
            float t = Mathf.Lerp(Mathf.Clamp01(startAt), 1f, p);

            // Static resting state: skip the dispatch entirely unless something changed. The renderer
            // keeps drawing the last morphed buffer, so a dormant plant does no per-frame GPU work.
            if (!m_playing && !m_forceDispatch && Mathf.Approximately(t, m_lastDispatchT))
                return;

            computeShader.SetInt(s_count, (int)m_count);
            computeShader.SetFloat(s_t, t);
            computeShader.SetBuffer(m_kernel, s_srcPos,   m_srcPos);
            computeShader.SetBuffer(m_kernel, s_srcScale, m_srcScale);
            computeShader.SetBuffer(m_kernel, s_srcRot,   m_srcRot);
            computeShader.SetBuffer(m_kernel, s_srcColor, m_srcColor);
            computeShader.SetBuffer(m_kernel, s_dstPos,   m_dstPos);
            computeShader.SetBuffer(m_kernel, s_dstScale, m_dstScale);
            computeShader.SetBuffer(m_kernel, s_dstRot,   m_dstRot);
            computeShader.SetBuffer(m_kernel, s_dstColor, m_dstColor);
            computeShader.SetBuffer(m_kernel, s_outPos,   m_hostResource.PositionBuffer);
            computeShader.SetBuffer(m_kernel, s_outScale, m_hostResource.ScaleBuffer);
            computeShader.SetBuffer(m_kernel, s_outRot,   m_hostResource.RotationBuffer);
            computeShader.SetBuffer(m_kernel, s_outColor, m_hostResource.ColorBuffer);

            ConfigureBloom();
            ConfigureScatter();

            computeShader.Dispatch(m_kernel, Mathf.CeilToInt(m_count / 256f), 1, 1);

            m_lastDispatchT = t;
            m_forceDispatch = false;
        }

        void ConfigureBloom()
        {
            // This component always runs the reveal as a bloom; swirl is never used here.
            computeShader.EnableKeyword(k_kwBloom);
            computeShader.DisableKeyword(k_kwSwirl);

            Vector3 axisN = revealAxis.sqrMagnitude > 1e-6f ? revealAxis.normalized : Vector3.up;
            Bounds b = m_hostAsset.Bounds;
            Vector3 origin;
            float length;
            if (radial)
            {
                origin = b.center;
                length = Mathf.Max(b.extents.magnitude, 1e-4f);
            }
            else
            {
                float projExtent = Mathf.Abs(axisN.x * b.extents.x) + Mathf.Abs(axisN.y * b.extents.y) + Mathf.Abs(axisN.z * b.extents.z);
                origin = b.center - axisN * projExtent;
                length = Mathf.Max(2f * projExtent, 1e-4f);
            }

            computeShader.SetVector(s_bloomOrigin, origin);
            computeShader.SetVector(s_bloomDir, axisN);
            computeShader.SetFloat(s_bloomLength, length);
            computeShader.SetFloat(s_bloomBand, bandFraction);
            computeShader.SetFloat(s_bloomInvert, invert ? 1f : 0f);
            computeShader.SetFloat(s_bloomRadial, radial ? 1f : 0f);
            computeShader.SetFloat(s_bloomReveal, reveal ? 1f : 0f);
        }

        void ConfigureScatter()
        {
            if (scatterAmount <= 0f)
            {
                computeShader.DisableKeyword(k_kwScatter);
                return;
            }
            computeShader.EnableKeyword(k_kwScatter);
            computeShader.SetFloat(s_scatterAmount, scatterAmount);
            computeShader.SetFloat(s_scatterFreq, scatterFrequency);
            computeShader.SetVector(s_scatterSeed, new Vector3(scatterSeed, scatterSeed * 2.137f + 11.3f, scatterSeed * 4.71f + 47.1f));
        }

        bool TryInitialize()
        {
            var hostAsset = m_host.GsplatAsset as GsplatAssetUncompressed;
            var hostRes   = m_host.GsplatResource as GsplatResourceUncompressed;

            if (hostAsset == null || hostRes == null || !hostRes.Uploaded ||
                hostAsset.Positions == null || hostAsset.Positions.Length == 0)
                return false;

            if (m_initialized && hostAsset == m_hostAsset && hostRes == m_hostResource)
                return true;

            DisposeBuffers();

            m_hostAsset = hostAsset;
            m_hostResource = hostRes;
            m_count = hostRes.UploadedCount;
            m_kernel = computeShader.FindKernel("Morph");

            int n = (int)m_count;
            m_hostCentroid = Centroid(m_hostAsset.Positions, n);

            // Dst = the untouched host (progress 1).
            m_dstPos   = MakeBuffer(m_hostAsset.Positions, n);
            m_dstScale = MakeBuffer(m_hostAsset.Scales, n);
            m_dstRot   = MakeBuffer(m_hostAsset.Rotations, n);
            m_dstColor = MakeBuffer(m_hostAsset.Colors, n);

            BuildSourceBuffers();

            m_initialized = true;
            return true;
        }

        /// <summary>
        /// Bakes the progress-0 endpoint procedurally from the host: positions shrunk toward the
        /// centroid, gaussians shrunk, colour pushed toward grey. Colour is desaturated in raw
        /// DC-SH space, which is an affine function of linear colour, so a luma-weighted lerp here
        /// equals a true greyscale lerp on the rendered colour.
        /// </summary>
        void BuildSourceBuffers()
        {
            int n = (int)m_count;
            var hostPos   = m_hostAsset.Positions;
            var hostScale = m_hostAsset.Scales;
            var hostRot   = m_hostAsset.Rotations;
            var hostColor = m_hostAsset.Colors;

            var sPos   = new Vector3[n];
            var sScale = new Vector3[n];
            var sRot   = new Vector4[n];
            var sColor = new Vector4[n];

            for (int i = 0; i < n; i++)
            {
                sPos[i]   = m_hostCentroid + (hostPos[i] - m_hostCentroid) * startPositionScale;
                sScale[i] = hostScale[i] * startGaussianScale;
                sRot[i]   = hostRot[i];

                Vector4 c = hostColor[i];
                float luma = c.x * 0.299f + c.y * 0.587f + c.z * 0.114f;
                sColor[i] = new Vector4(
                    Mathf.Lerp(c.x, luma, startDesaturation),
                    Mathf.Lerp(c.y, luma, startDesaturation),
                    Mathf.Lerp(c.z, luma, startDesaturation),
                    c.w);
            }

            m_srcPos?.Dispose();   m_srcPos   = MakeBuffer(sPos, n);
            m_srcScale?.Dispose(); m_srcScale = MakeBuffer(sScale, n);
            m_srcRot?.Dispose();   m_srcRot   = MakeBuffer(sRot, n);
            m_srcColor?.Dispose(); m_srcColor = MakeBuffer(sColor, n);

            m_bakedGScale = startGaussianScale;
            m_bakedPScale = startPositionScale;
            m_bakedDesat = startDesaturation;
            m_forceDispatch = true; // start endpoint changed: re-dispatch even if t is unchanged
        }

        static Vector3 Centroid(Vector3[] p, int n)
        {
            Vector3 c = Vector3.zero;
            for (int i = 0; i < n; i++) c += p[i];
            return n > 0 ? c / n : Vector3.zero;
        }

        static GraphicsBuffer MakeBuffer(Vector3[] data, int count)
        {
            var b = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, Marshal.SizeOf(typeof(Vector3)));
            b.SetData(data, 0, 0, count);
            return b;
        }

        static GraphicsBuffer MakeBuffer(Vector4[] data, int count)
        {
            var b = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, Marshal.SizeOf(typeof(Vector4)));
            b.SetData(data, 0, 0, count);
            return b;
        }

        /// <summary>Borrow a private resource and point the renderer at it, so our per-frame morph
        /// writes never touch the asset's shared "full detail" buffer.</summary>
        void Activate()
        {
            if (m_active) return;
            var asset = m_host != null ? m_host.GsplatAsset as GsplatAssetUncompressed : null;
            if (asset == null) return;
            m_privateAsset = asset;
            m_privateResource = GsplatMorphBufferPool.Acquire(asset);
            m_host.SetResourceOverride(m_privateResource);
            m_forceDispatch = true; // freshly-borrowed buffer is uninitialised: fill it once
            m_active = true;
        }

        /// <summary>Stop morphing: revert the renderer to the shared full-detail buffer, return our
        /// private resource to the pool, and free the per-instance src/dst scratch (the VRAM
        /// reclaim). Called when the reveal settles at progress 1 or the component is disabled.</summary>
        void Deactivate()
        {
            if (!m_active) return;
            m_active = false;
            if (m_host != null) m_host.ClearResourceOverride();
            if (m_privateResource != null)
            {
                GsplatMorphBufferPool.Release(m_privateAsset, m_privateResource);
                m_privateResource = null;
            }
            m_privateAsset = null;
            DisposeBuffers();
            m_initialized = false;
        }

        void DisposeBuffers()
        {
            m_srcPos?.Dispose();   m_srcPos = null;
            m_srcScale?.Dispose(); m_srcScale = null;
            m_srcRot?.Dispose();   m_srcRot = null;
            m_srcColor?.Dispose(); m_srcColor = null;
            m_dstPos?.Dispose();   m_dstPos = null;
            m_dstScale?.Dispose(); m_dstScale = null;
            m_dstRot?.Dispose();   m_dstRot = null;
            m_dstColor?.Dispose(); m_dstColor = null;
            m_bakedGScale = m_bakedPScale = m_bakedDesat = -1f;
        }

        // ---- scripted playback ----

        /// <summary>Reveal: drive progress 0 -> 1 over <see cref="duration"/> seconds.</summary>
        public void Play() => PlayTo(0f, 1f);

        /// <summary>Hide: drive progress back 1 -> 0 over <see cref="duration"/> seconds.</summary>
        public void PlayReverse() => PlayTo(progress, 0f);

        /// <summary>Snap back to the hidden/desaturated resting state (progress 0), cancelling any
        /// play in progress. Replacement for the old GsplatShockwaveAnimator.ApplyInitialGreyscale().</summary>
        public void ResetToStart()
        {
            m_playing = false;
            progress = 0f;
            IsDone = false;
#if UNITY_EDITOR
            m_edPlaying = false;
#endif
        }

        void PlayTo(float from, float to)
        {
            IsDone = false;
            progress = from;
            m_playFrom = from; m_playTo = to;
            m_playDur = duration;

            if (Application.isPlaying)
            {
                m_playStart = Time.time;
                m_playing = true;
                return;
            }
#if UNITY_EDITOR
            // Edit mode: tick the tween off the editor loop and pump the player loop each step so
            // this ExecuteAlways component re-dispatches the morph. Preview right in the Scene view.
            m_edStart = UnityEditor.EditorApplication.timeSinceStartup;
            m_edPlaying = true;
            EnsureEditorPump();
#else
            progress = to;
            IsDone = Mathf.Approximately(to, 1f);
#endif
        }

#if UNITY_EDITOR
        double m_edStart;
        bool m_edPlaying;

        // Pump the editor loop only while a tween is actually running. The resting state is static
        // (no idle animation), so there is nothing to drive once the tween settles; EditorTick
        // unsubscribes itself. Live scrubbing is handled by OnValidate.
        void EnsureEditorPump()
        {
            if (Application.isPlaying)
                return;
            if (m_edPlaying)
            {
                UnityEditor.EditorApplication.update -= EditorTick;
                UnityEditor.EditorApplication.update += EditorTick;
            }
        }

        void EditorTick()
        {
            if (this == null) { UnityEditor.EditorApplication.update -= EditorTick; return; }

            if (m_edPlaying)
            {
                float k = Mathf.Clamp01((float)(UnityEditor.EditorApplication.timeSinceStartup - m_edStart) / Mathf.Max(m_playDur, 0.0001f));
                progress = Mathf.Lerp(m_playFrom, m_playTo, k);
                if (k >= 1f)
                {
                    IsDone = Mathf.Approximately(m_playTo, 1f);
                    m_edPlaying = false;
                }
            }

            // Re-dispatch the morph this editor frame so the tween advances in the Scene view.
            UnityEditor.EditorApplication.QueuePlayerLoopUpdate();

            if (!m_edPlaying)
                UnityEditor.EditorApplication.update -= EditorTick;
        }
#endif

        /// <summary>Snap to the fully revealed host immediately.</summary>
        public void ForceColored()
        {
            m_playing = false;
            progress = 1f;
            IsDone = true;
        }

        [ContextMenu("Play (Reveal)")]      void CtxPlay()    => Play();
        [ContextMenu("Play (Hide)")]        void CtxHide()    => PlayReverse();
        [ContextMenu("Reset (progress 0)")] void CtxReset()   => ResetToStart();
        [ContextMenu("Force Revealed")]     void CtxColored() => ForceColored();

#if UNITY_EDITOR
        void OnValidate()
        {
            if (!Application.isPlaying && previewInEditMode)
            {
                EnsureEditorPump();
                UnityEditor.EditorApplication.QueuePlayerLoopUpdate(); // single-frame scrub preview
            }
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Vector3 c = transform.position;
            Vector3 axisN = revealAxis.sqrMagnitude > 1e-6f ? revealAxis.normalized : Vector3.up;
            if (radial)
                Gizmos.DrawWireSphere(c, 0.3f);
            else
                Gizmos.DrawRay(c, transform.TransformDirection(axisN) * 0.4f);
        }
#endif
    }
}

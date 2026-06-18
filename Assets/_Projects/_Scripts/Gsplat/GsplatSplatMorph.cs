using System.Runtime.InteropServices;
using UnityEngine;

namespace Gsplat.Animation
{
    /// <summary>
    /// Geometric morph between two uncompressed (PLY) Gaussian splats.
    ///
    /// Attach to the GsplatRenderer of the DETAILED endpoint (this becomes the morph
    /// host / target, progress = 1). Assign the coarse renderer as <see cref="sourceRenderer"/>
    /// (progress = 0). Every frame a compute shader overwrites this renderer's live
    /// position / scale / rotation / colour buffers with a per-splat interpolation of the
    /// two endpoints, so depth sort and rendering both operate on the blended state.
    ///
    /// The two captures usually have different splat counts (e.g. 1000 -> 6000). Each host
    /// (target) splat is paired once, on init, to its nearest source splat, so at progress 0
    /// the host collapses onto the coarse cloud and at progress 1 it springs out to full
    /// detail. Pairing is done after an optional centroid / scale alignment of the source.
    ///
    /// Drive <see cref="progress"/> directly (scrub it live in the inspector or animate it
    /// from a Timeline / AnimationClip), or call <see cref="Play"/> / <see cref="PlayReverse"/>.
    /// </summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(-100)] // run before GsplatRenderer (order 0) so the blend is ready for its depth sort
    [RequireComponent(typeof(GsplatRenderer))]
    public class GsplatSplatMorph : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Coarse endpoint (progress = 0). Must be an uncompressed PLY GsplatAsset. This component's own renderer is the detailed endpoint (progress = 1).")]
        public GsplatRenderer sourceRenderer;

        [Tooltip("GsplatMorph.compute.")]
        public ComputeShader computeShader;

        [Header("Morph")]
        [Tooltip("0 = source (coarse), 1 = this renderer's asset (detailed). Scrub live or drive from a Timeline.")]
        [Range(0f, 1f)] public float progress = 0f;

        [Tooltip("Remaps progress -> morph t. Leave linear for a 1:1 scrub; shape it for ease-in/out.")]
        public AnimationCurve curve = AnimationCurve.Linear(0, 0, 1, 1);

        [Header("Source alignment (applied once, on init)")]
        [Tooltip("Translate the source cloud so its centroid matches the target's before nearest-neighbour pairing. Helps when the two captures aren't registered to the same origin.")]
        public bool alignCentroids = true;

        [Tooltip("Uniformly rescale the source cloud (positions and gaussian sizes) so its spread roughly matches the target's before pairing.")]
        public bool matchScale = false;

        [Header("Setup")]
        [Tooltip("Disable the source renderer while morphing so it doesn't draw on top of the blended host.")]
        public bool hideSourceRenderer = true;

        [Header("Playback (optional, drives 'progress' over time)")]
        public float duration = 2f;

        // ---- internals ----
        GsplatRenderer m_host;
        GsplatAssetUncompressed m_hostAsset;
        GsplatResourceUncompressed m_hostResource;
        GsplatAssetUncompressed m_srcAsset;

        GraphicsBuffer m_srcPos, m_srcScale, m_srcRot, m_srcColor;
        GraphicsBuffer m_dstPos, m_dstScale, m_dstRot, m_dstColor;

        int m_kernel;
        uint m_count;
        bool m_initialized;
        bool m_sourceWasEnabled;
        Vector3 m_hostCentroid;

        /// <summary>The curve-mapped morph value actually fed to the kernel this frame (read by accents).</summary>
        public float CurrentT { get; private set; }

        // Keywords for the optional effect modifiers (must match GsplatMorph.compute).
        static readonly string[] k_effectKeywords = { "MORPH_BLOOM", "MORPH_SWIRL", "MORPH_SCATTER" };
        readonly System.Collections.Generic.List<GsplatMorphModifier> m_modifiers = new();

        float m_playFrom, m_playTo, m_playStart, m_playDur;
        bool m_playing;

        // Private-buffer morph: while actively morphing we render from a pooled PRIVATE resource so
        // our per-frame writes never touch the asset's SHARED "full detail" buffer (see
        // GsplatMorphBufferPool). Borrowed on activate, returned on settle/disable.
        const float k_settleEps = 1e-4f;
        bool m_active;
        bool m_sourceHidden;
        GsplatResource m_privateResource;
        GsplatAssetUncompressed m_privateAsset;

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

        void Awake() => m_host = GetComponent<GsplatRenderer>();

        void OnEnable()
        {
            if (m_host == null) m_host = GetComponent<GsplatRenderer>();
            m_initialized = false;
        }

        void OnDisable()
        {
            Deactivate();      // return the private buffer to the pool, revert to shared, free scratch
            DisposeBuffers();  // defensive
            if (m_sourceHidden && sourceRenderer != null)
            {
                sourceRenderer.enabled = m_sourceWasEnabled;
                m_sourceHidden = false;
            }
            m_initialized = false;
        }

        void OnDestroy() => DisposeBuffers();

        void Update()
        {
            if (computeShader == null || sourceRenderer == null)
                return;

            // Advance any scripted tween first so the settle decision below sees the latest progress.
            if (m_playing)
            {
                float k = Mathf.Clamp01((Time.time - m_playStart) / Mathf.Max(m_playDur, 0.0001f));
                progress = Mathf.Lerp(m_playFrom, m_playTo, k);
                if (k >= 1f) m_playing = false;
            }

            // Settled at progress 1 (= this renderer's own detailed asset, already in the SHARED
            // buffer): stop dispatching, return the private buffer + scratch, draw shared for free.
            bool settled = !m_playing && progress >= 1f - k_settleEps;
            if (settled)
            {
                if (m_active) Deactivate();
                return;
            }

            // Active morph: own a private buffer before writing so we never touch the shared one.
            if (m_host == null || m_host.GsplatResource == null)
                return;
            if (!m_active) Activate();
            // Re-assert the override in case the renderer rebound its shared resource (asset reload).
            if (m_active && m_privateResource != null && m_host.GsplatResource != m_privateResource)
                m_host.SetResourceOverride(m_privateResource);

            if (!TryInitialize())
                return;

            float t = curve != null ? curve.Evaluate(progress) : progress;

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

            CurrentT = t;
            ApplyModifiers(t);

            computeShader.Dispatch(m_kernel, Mathf.CeilToInt(m_count / 256f), 1, 1);
        }

        /// <summary>
        /// Reset all effect keywords, then let each enabled sibling modifier enable its keyword
        /// and push its uniforms. Disabled / removed effects compile out and cost nothing.
        /// </summary>
        void ApplyModifiers(float t)
        {
            foreach (var kw in k_effectKeywords)
                computeShader.DisableKeyword(kw);

            GetComponents(m_modifiers);
            if (m_modifiers.Count == 0)
                return;

            var ctx = new GsplatMorphContext
            {
                localBounds = m_hostAsset.Bounds,
                centroid = m_hostCentroid,
                count = (int)m_count
            };

            foreach (var m in m_modifiers)
            {
                if (m == null || !m.isActiveAndEnabled)
                    continue;
                computeShader.EnableKeyword(m.Keyword);
                m.Configure(computeShader, m_kernel, t, in ctx);
            }
        }

        bool TryInitialize()
        {
            var hostAsset = m_host.GsplatAsset as GsplatAssetUncompressed;
            var hostRes   = m_host.GsplatResource as GsplatResourceUncompressed;
            var srcAsset  = sourceRenderer.GsplatAsset as GsplatAssetUncompressed;

            if (hostAsset == null || hostRes == null || !hostRes.Uploaded || srcAsset == null ||
                srcAsset.Positions == null || srcAsset.Positions.Length == 0)
                return false;

            if (m_initialized && hostAsset == m_hostAsset && hostRes == m_hostResource && srcAsset == m_srcAsset)
                return true;

            DisposeBuffers();

            m_hostAsset = hostAsset;
            m_hostResource = hostRes;
            m_srcAsset = srcAsset;
            m_count = hostRes.UploadedCount;
            m_kernel = computeShader.FindKernel("Morph");

            BuildEndpointBuffers();

            // Capture the source's original enabled state only once (we keep it hidden across a
            // settle→re-activate cycle; OnDisable restores it), then hide it while we morph.
            if (hideSourceRenderer && sourceRenderer != null && !m_sourceHidden)
            {
                m_sourceWasEnabled = sourceRenderer.enabled;
                sourceRenderer.enabled = false;
                m_sourceHidden = true;
            }

            m_initialized = true;
            Debug.Log($"[GsplatSplatMorph] Initialized: {srcAsset.Positions.Length} src -> {m_count} target splats.", this);
            return true;
        }

        /// <summary>
        /// Builds the Src (paired/aligned source) and Dst (host) endpoint buffers, both laid
        /// out on the host's index range so the compute kernel can blend index-for-index.
        /// </summary>
        void BuildEndpointBuffers()
        {
            int n = (int)m_count;
            var srcPos    = m_srcAsset.Positions;
            var srcScale  = m_srcAsset.Scales;
            var srcRot    = m_srcAsset.Rotations;
            var srcColor  = m_srcAsset.Colors;
            int sn = srcPos.Length;

            // --- source -> target alignment (centroid, optional uniform scale) ---
            Vector3 srcCentroid = Centroid(srcPos, sn);
            Vector3 dstCentroid = Centroid(m_hostAsset.Positions, n);
            m_hostCentroid = dstCentroid;
            float sizeScale = 1f;
            if (matchScale)
            {
                float srcSpread = MeanRadius(srcPos, sn, srcCentroid);
                float dstSpread = MeanRadius(m_hostAsset.Positions, n, dstCentroid);
                sizeScale = srcSpread > 1e-6f ? dstSpread / srcSpread : 1f;
            }
            Vector3 offset = alignCentroids ? (dstCentroid - srcCentroid * sizeScale) : Vector3.zero;

            var alignedSrc = new Vector3[sn];
            for (int i = 0; i < sn; i++)
                alignedSrc[i] = srcPos[i] * sizeScale + offset;

            // --- pair each target splat to its nearest (aligned) source splat ---
            var pSrcPos   = new Vector3[n];
            var pSrcScale = new Vector3[n];
            var pSrcRot   = new Vector4[n];
            var pSrcColor = new Vector4[n];
            var hostPos = m_hostAsset.Positions;
            for (int i = 0; i < n; i++)
            {
                Vector3 p = hostPos[i];
                int best = 0;
                float bestD = float.MaxValue;
                for (int j = 0; j < sn; j++)
                {
                    float d = (alignedSrc[j] - p).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = j; }
                }
                pSrcPos[i]   = alignedSrc[best];
                pSrcScale[i] = srcScale[best] * sizeScale;
                pSrcRot[i]   = srcRot[best];
                pSrcColor[i] = srcColor[best];
            }

            m_srcPos   = MakeBuffer(pSrcPos);
            m_srcScale = MakeBuffer(pSrcScale);
            m_srcRot   = MakeBuffer(pSrcRot);
            m_srcColor = MakeBuffer(pSrcColor);

            m_dstPos   = MakeBuffer(m_hostAsset.Positions, n);
            m_dstScale = MakeBuffer(m_hostAsset.Scales, n);
            m_dstRot   = MakeBuffer(m_hostAsset.Rotations, n);
            m_dstColor = MakeBuffer(m_hostAsset.Colors, n);
        }

        static Vector3 Centroid(Vector3[] p, int n)
        {
            Vector3 c = Vector3.zero;
            for (int i = 0; i < n; i++) c += p[i];
            return n > 0 ? c / n : Vector3.zero;
        }

        static float MeanRadius(Vector3[] p, int n, Vector3 c)
        {
            float s = 0f;
            for (int i = 0; i < n; i++) s += (p[i] - c).magnitude;
            return n > 0 ? s / n : 1f;
        }

        static GraphicsBuffer MakeBuffer(Vector3[] data) => MakeBuffer(data, data.Length);

        static GraphicsBuffer MakeBuffer(Vector3[] data, int count)
        {
            var b = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, Marshal.SizeOf(typeof(Vector3)));
            b.SetData(data, 0, 0, count);
            return b;
        }

        static GraphicsBuffer MakeBuffer(Vector4[] data) => MakeBuffer(data, data.Length);

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
            m_active = true;
        }

        /// <summary>Stop morphing: revert the renderer to the shared full-detail buffer, return our
        /// private resource to the pool, and free the per-instance endpoint scratch. The hidden
        /// source renderer is left as-is (restored in OnDisable), since at the settled detailed
        /// state the coarse source should stay hidden.</summary>
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
        }

        // ---- optional scripted playback ----
        public void Play() => PlayTo(progress, 1f);
        public void PlayReverse() => PlayTo(progress, 0f);

        void PlayTo(float from, float to)
        {
            if (!Application.isPlaying)
            {
                progress = to; // no time loop in edit mode; just snap
                return;
            }
            m_playFrom = from; m_playTo = to;
            m_playStart = Time.time; m_playDur = duration;
            m_playing = true;
        }

        [ContextMenu("Play (Source -> Target)")] void CtxPlay() => Play();
        [ContextMenu("Play (Target -> Source)")] void CtxPlayRev() => PlayReverse();

#if UNITY_EDITOR
        void OnValidate()
        {
            // Keep the scrubbed result live in the editor: tick the player loop so this
            // component's Update (and the GsplatRenderer's) re-run after the slider moves.
            if (!Application.isPlaying)
                UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
        }
#endif
    }
}

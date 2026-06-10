using System.Runtime.InteropServices;
using UnityEngine;

namespace Gsplat.Animation
{
    /// <summary>
    /// Compute-shader based hand repulsion for Gaussian splats.
    ///
    /// Displaces splat positions directly in the GPU position buffer each frame,
    /// so depth sorting and rendering both operate on the displaced positions.
    /// Splats spring back to their rest positions with configurable stiffness and damping.
    ///
    /// Only works with uncompressed (PLY-imported) GsplatAssets.
    ///
    /// Setup:
    ///   1. Add this component to the same GameObject as GsplatRenderer.
    ///   2. Assign ComputeShader (GsplatRepulsionPhysics.compute).
    ///   3. Assign Target (hand joint Transform — palm, wrist, etc.).
    /// </summary>
    [DefaultExecutionOrder(-100)] // run before GsplatRenderer (order 0) so positions are ready for depth sort
    [RequireComponent(typeof(GsplatRenderer))]
    public class GsplatComputeRepulsion : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("The GsplatRepulsionPhysics compute shader asset.")]
        public ComputeShader computeShader;

        [Tooltip("Hand joint to repel from (e.g. palm anchor). Falls back to Camera.main if unset.")]
        public Transform target;

        [Header("Repulsion")]
        [Tooltip("Radius of influence in world-space metres.")]
        [Min(0f)]
        public float repulsionRadius = 0.25f;

        [Tooltip("Peak acceleration at the centre of the sphere (world-space m/s²).")]
        [Min(0f)]
        public float repulsionStrength = 8f;

        [Header("Spring / Damping")]
        [Tooltip("How strongly splats are pulled back to their rest position (stiffness, m/s² per metre).")]
        [Min(0f)]
        public float springStrength = 25f;

        [Tooltip("Fraction of velocity remaining after 1 second. 0.05 = heavy damping (springy snap-back), 0.5 = lighter.")]
        [Range(0.001f, 0.999f)]
        public float dampingPerSecond = 0.05f;

        [Header("Debug")]
        public bool enableSimulation = true;

        // ---- internals ----
        GsplatRenderer m_renderer;
        GsplatAssetUncompressed m_asset;
        GsplatResourceUncompressed m_resource;

        GraphicsBuffer m_restPositionBuffer; // original positions, never changed
        GraphicsBuffer m_velocityBuffer;     // per-splat velocity

        int m_kernel;
        bool m_initialized;
        uint m_splatCount;

        // shader property IDs
        static readonly int s_restPositionBuffer = Shader.PropertyToID("_RestPositionBuffer");
        static readonly int s_velocityBuffer     = Shader.PropertyToID("_VelocityBuffer");
        static readonly int s_positionBuffer     = Shader.PropertyToID("_PositionBuffer");
        static readonly int s_handLocalPos       = Shader.PropertyToID("_HandLocalPos");
        static readonly int s_repulsionRadius    = Shader.PropertyToID("_RepulsionRadius");
        static readonly int s_repulsionStrength  = Shader.PropertyToID("_RepulsionStrength");
        static readonly int s_springStrength     = Shader.PropertyToID("_SpringStrength");
        static readonly int s_dampingPerSecond   = Shader.PropertyToID("_DampingPerSecond");
        static readonly int s_deltaTime          = Shader.PropertyToID("_DeltaTime");
        static readonly int s_splatCount         = Shader.PropertyToID("_SplatCount");

        void Awake()
        {
            m_renderer = GetComponent<GsplatRenderer>();
        }

        void OnEnable()
        {
            m_initialized = false;
        }

        void Update()
        {
            if (!enableSimulation || computeShader == null)
                return;

            // (Re-)initialize whenever the asset changes or on first run
            if (!TryInitialize())
                return;

            Transform t = GetTarget();
            if (t == null)
            {
                Debug.LogWarning("[GsplatComputeRepulsion] No target transform — assign one or ensure Camera.main exists.", this);
                return;
            }

            // Convert hand world position to object-local space.
            // Radius/strength are scaled into local space assuming roughly uniform scale.
            Vector3 handWorld  = t.position;
            Vector3 handLocal  = transform.InverseTransformPoint(handWorld);
            float   localScale = transform.lossyScale.x; // assume roughly uniform
            float   localRadius    = repulsionRadius  / Mathf.Max(localScale, 0.0001f);
            float   localStrength  = repulsionStrength / Mathf.Max(localScale, 0.0001f);

            computeShader.SetVector(s_handLocalPos,      handLocal);
            computeShader.SetFloat(s_repulsionRadius,    localRadius);
            computeShader.SetFloat(s_repulsionStrength,  localStrength);
            computeShader.SetFloat(s_springStrength,     springStrength);
            computeShader.SetFloat(s_dampingPerSecond,   dampingPerSecond);
            computeShader.SetFloat(s_deltaTime,          Time.deltaTime);
            computeShader.SetInt(s_splatCount,           (int)m_splatCount);

            computeShader.SetBuffer(m_kernel, s_restPositionBuffer, m_restPositionBuffer);
            computeShader.SetBuffer(m_kernel, s_velocityBuffer,     m_velocityBuffer);
            computeShader.SetBuffer(m_kernel, s_positionBuffer,     m_resource.PositionBuffer);

            int groups = Mathf.CeilToInt(m_splatCount / 1024f);
            computeShader.Dispatch(m_kernel, groups, 1, 1);
        }

        /// <summary>
        /// Returns true if successfully initialized and ready to dispatch.
        /// Handles late initialization (asset may not be loaded on first frame).
        /// </summary>
        bool TryInitialize()
        {
            // Check if the asset or resource changed
            var asset    = m_renderer.GsplatAsset as GsplatAssetUncompressed;
            var resource = m_renderer.GsplatResource as GsplatResourceUncompressed;

            if (asset == null || resource == null || !resource.Uploaded)
                return false; // asset not loaded yet or wrong compression type

            if (m_initialized && asset == m_asset && resource == m_resource)
                return true; // already initialized for this asset

            // Asset changed or first init — rebuild buffers
            DisposeBuffers();

            m_asset    = asset;
            m_resource = resource;
            m_splatCount = resource.UploadedCount;
            m_kernel   = computeShader.FindKernel("SimulateRepulsion");

            // Rest position buffer: upload from the CPU asset data
            m_restPositionBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                (int)m_splatCount,
                Marshal.SizeOf(typeof(Vector3)));
            m_restPositionBuffer.SetData(asset.Positions, 0, 0, (int)m_splatCount);

            // Velocity buffer: zeroed
            m_velocityBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                (int)m_splatCount,
                Marshal.SizeOf(typeof(Vector3)));
            var zeros = new Vector3[m_splatCount];
            m_velocityBuffer.SetData(zeros);

            m_initialized = true;
            Debug.Log($"[GsplatComputeRepulsion] Initialized for {m_splatCount} splats.", this);
            return true;
        }

        Transform GetTarget()
        {
            if (target != null) return target;
            Camera cam = Camera.main;
            return cam != null ? cam.transform : null;
        }

        void OnDisable()
        {
            RestorePositions();
            DisposeBuffers();
            m_initialized = false;
        }

        void OnDestroy()
        {
            DisposeBuffers();
        }

        /// <summary>
        /// Write original positions back to PositionBuffer so the renderer
        /// is left in a clean state when this component is disabled.
        /// </summary>
        void RestorePositions()
        {
            if (!m_initialized || m_resource == null || m_asset == null)
                return;

            m_resource.PositionBuffer?.SetData(m_asset.Positions, 0, 0, (int)m_splatCount);
        }

        void DisposeBuffers()
        {
            m_restPositionBuffer?.Dispose();
            m_restPositionBuffer = null;
            m_velocityBuffer?.Dispose();
            m_velocityBuffer = null;
        }
    }
}

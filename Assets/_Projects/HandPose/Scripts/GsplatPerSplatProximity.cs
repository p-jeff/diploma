using UnityEngine;

namespace Gsplat.Animation
{
    /// <summary>
    /// Sets shader uniforms for GsplatProximityScale.shader (Gsplat/ProximityScale).
    /// Splats closer to the target appear bigger, farther ones smaller.
    /// Downscale factor: 0 = full size, 1 = invisible (same convention as SplatDownscaleFactor).
    /// Attach to any GameObject. Quest 3 safe (~5 ALU per splat in vertex shader).
    /// </summary>
    public class GsplatPerSplatProximity : MonoBehaviour
    {
        [Header("Target")]
        [Tooltip("Reference point for distance measurement. Falls back to Camera.main if unset.")]
        public Transform target;

        [Header("Distance Range")]
        [Tooltip("Distance at which nearDownscale applies")]
        public float nearDistance = 0.5f;

        [Tooltip("Distance at which farDownscale applies")]
        public float farDistance = 3f;

        [Header("Downscale (0 = full size, 1 = invisible)")]
        [Tooltip("Downscale for splats at nearDistance")]
        [Range(0f, 1f)]
        public float nearDownscale = 0f;

        [Tooltip("Downscale for splats at farDistance")]
        [Range(0f, 1f)]
        public float farDownscale = 0.8f;

        static readonly int s_target        = Shader.PropertyToID("_GsplatProximityTarget");
        static readonly int s_nearDist      = Shader.PropertyToID("_GsplatProximityNearDist");
        static readonly int s_farDist       = Shader.PropertyToID("_GsplatProximityFarDist");
        static readonly int s_nearDownscale = Shader.PropertyToID("_GsplatProximityNearDownscale");
        static readonly int s_farDownscale  = Shader.PropertyToID("_GsplatProximityFarDownscale");

        Transform GetTarget()
        {
            if (target != null) return target;
            Camera cam = Camera.main;
            return cam != null ? cam.transform : null;
        }

        float m_logTimer;

        void Update()
        {
            Transform t = GetTarget();
            if (t == null)
            {
                Debug.LogWarning("[GsplatPerSplatProximity] No target found (assign one or ensure Camera.main exists)", this);
                return;
            }

            Shader.SetGlobalVector(s_target,        t.position);
            Shader.SetGlobalFloat(s_nearDist,       nearDistance);
            Shader.SetGlobalFloat(s_farDist,        farDistance);
            Shader.SetGlobalFloat(s_nearDownscale,  nearDownscale);
            Shader.SetGlobalFloat(s_farDownscale,   farDownscale);

            m_logTimer -= Time.deltaTime;
            if (m_logTimer <= 0f)
            {
                m_logTimer = 2f;
                float dist = Vector3.Distance(t.position, transform.position);
                float range = Mathf.Max(farDistance - nearDistance, 0.0001f);
                float lerp = Mathf.Clamp01((dist - nearDistance) / range);
                float downscale = Mathf.Lerp(nearDownscale, farDownscale, lerp);
                Debug.Log($"[GsplatPerSplatProximity] target={t.name} dist={dist:F2}m  lerp={lerp:F2}  downscale={downscale:F2}  perSplatScale={1f - downscale:F2}", this);
            }
        }

        void OnDisable()
        {
            // Reset to neutral (no downscale = full size)
            Shader.SetGlobalFloat(s_nearDownscale, 0f);
            Shader.SetGlobalFloat(s_farDownscale,  0f);
        }
    }
}

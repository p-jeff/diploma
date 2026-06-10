using UnityEngine;

namespace Gsplat.Animation
{
    /// <summary>
    /// Scales Gaussian splats based on distance from a target transform.
    /// Closer = bigger. Quest 3 safe (pure C#, no shader changes).
    /// </summary>
    public class GsplatProximityEffect : MonoBehaviour
    {
        [Header("Target")]
        [Tooltip("Transform to measure distance from (e.g. camera, head, hand). Falls back to Camera.main if unset.")]
        public Transform target;

        [Header("Proximity Size")]
        [Tooltip("Distance at which splats are full size")]
        public float fullSizeDistance = 0.5f;

        [Tooltip("Distance at which splats are minimum size")]
        public float minSizeDistance = 3f;

        [Tooltip("Maximum size when closest (0 = invisible, 1 = full)")]
        [Range(0f, 1f)]
        public float maxSize = 1f;

        [Tooltip("Minimum size at max distance (0 = invisible, 1 = full)")]
        [Range(0f, 1f)]
        public float minSize = 0.3f;

        [Tooltip("Enable proximity-based sizing")]
        public bool enableProximitySize = true;

        GsplatRenderer m_renderer;

        void Awake()
        {
            m_renderer = GetComponent<GsplatRenderer>();
        }

        Transform GetTarget()
        {
            if (target != null) return target;
            Camera cam = Camera.main;
            return cam != null ? cam.transform : null;
        }

        void Update()
        {
            if (!enableProximitySize || m_renderer == null) return;

            Transform t = GetTarget();
            if (t == null) return;

            float dist = Vector3.Distance(t.position, transform.position);

            // Map distance to size: close = 1 (full), far = minSize
            float lerp = Mathf.InverseLerp(fullSizeDistance, minSizeDistance, dist);
            float size = Mathf.Lerp(maxSize, minSize, lerp);

            m_renderer.SplatDownscaleFactor = size;
        }

        void OnDisable()
        {
            if (m_renderer != null)
                m_renderer.SplatDownscaleFactor = 0f;
        }
    }
}

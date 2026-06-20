using UnityEngine;

namespace Plants
{
    /// <summary>
    /// An editor-only placement guide for canopy fruit: a wire circle you move and scale in the
    /// Scene view to choose exactly WHERE a tree's fruits hang. When a <see cref="Plant"/> (in
    /// CanopyFruit mode) has its <c>fruitRing</c> pointed at one of these, fruits are spread evenly
    /// within this circle's disc instead of auto-sampled across the canopy AABB (which scatters them
    /// through a tall band). The circle is a gizmo only — it never renders at runtime / in headset.
    ///
    /// Scale the transform to set the diameter (uniform scale S → circle diameter S); move it to set
    /// the centre/height; tilt it to tilt the disc.
    /// </summary>
    [DisallowMultipleComponent]
    public class FruitRing : MonoBehaviour
    {
        [Tooltip("Gizmo colour of the placement circle (Scene view only).")]
        [SerializeField] private Color gizmoColor = new Color(1f, 0.78f, 0.32f, 0.95f);

        [Tooltip("Gizmo circle smoothness (Scene view only).")]
        [SerializeField, Range(8, 128)] private int gizmoSegments = 48;

        /// <summary>World-space centre of the circle.</summary>
        public Vector3 Center => transform.position;

        /// <summary>Circle-plane normal (tilt the transform to tilt the disc). Falls back to up.</summary>
        public Vector3 Normal
        {
            get { var n = transform.up; return n.sqrMagnitude > 1e-6f ? n.normalized : Vector3.up; }
        }

        /// <summary>Radius in world metres: half the larger horizontal lossy scale, so a uniform
        /// scale of S gives a circle of diameter S regardless of the parent tree's scale.</summary>
        public float WorldRadius => 0.5f * Mathf.Max(0f, Mathf.Max(transform.lossyScale.x, transform.lossyScale.z));

        /// <summary>Two unit vectors spanning the circle plane (for sampling points on the disc).</summary>
        public void GetPlaneBasis(out Vector3 u, out Vector3 v)
        {
            Vector3 n = Normal;
            u = Vector3.Cross(n, Vector3.right);
            if (u.sqrMagnitude < 1e-4f) u = Vector3.Cross(n, Vector3.forward);
            u.Normalize();
            v = Vector3.Cross(n, u).normalized;
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            float r = WorldRadius;
            if (r <= 0f) return;

            GetPlaneBasis(out var u, out var v);
            Vector3 c = Center;
            Gizmos.color = gizmoColor;

            int seg = Mathf.Max(8, gizmoSegments);
            Vector3 prev = c + u * r;
            for (int i = 1; i <= seg; i++)
            {
                float a = (i / (float)seg) * Mathf.PI * 2f;
                Vector3 p = c + (u * Mathf.Cos(a) + v * Mathf.Sin(a)) * r;
                Gizmos.DrawLine(prev, p);
                prev = p;
            }

            // Small centre cross so the guide is easy to spot and grab.
            float k = r * 0.06f;
            Gizmos.DrawLine(c - u * k, c + u * k);
            Gizmos.DrawLine(c - v * k, c + v * k);
        }
#endif
    }
}

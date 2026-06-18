using UnityEngine;

namespace Gsplat.Animation
{
    /// <summary>
    /// Arc + twist mid-flight detour: splats spiral around <see cref="axis"/> and bulge outward
    /// as they travel, settling exactly onto their targets at the end. The detour peaks at the
    /// midpoint of each splat's transition and is zero at both endpoints.
    /// </summary>
    public class GsplatMorphSwirl : GsplatMorphModifier
    {
        [Tooltip("Axis the splats twist around (local space).")]
        public Vector3 axis = Vector3.up;

        [Tooltip("Peak twist around the axis at mid-morph (degrees).")]
        public float twistDegrees = 120f;

        [Tooltip("Peak outward bulge at mid-morph (metres).")]
        public float bulge = 0.05f;

        public override string Keyword => "MORPH_SWIRL";

        static readonly int s_axis   = Shader.PropertyToID("_SwirlAxis");
        static readonly int s_center = Shader.PropertyToID("_SwirlCenter");
        static readonly int s_angle  = Shader.PropertyToID("_SwirlAngle");
        static readonly int s_bulge  = Shader.PropertyToID("_SwirlBulge");

        Vector3 AxisN => axis.sqrMagnitude > 1e-6f ? axis.normalized : Vector3.up;

        public override void Configure(ComputeShader cs, int kernel, float t, in GsplatMorphContext ctx)
        {
            cs.SetVector(s_axis, AxisN);
            cs.SetVector(s_center, ctx.centroid);
            cs.SetFloat(s_angle, twistDegrees * Mathf.Deg2Rad);
            cs.SetFloat(s_bulge, bulge);
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawRay(transform.position, transform.TransformDirection(AxisN) * 0.4f);
        }
#endif
    }
}

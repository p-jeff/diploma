using UnityEngine;

namespace Gsplat.Animation
{
    /// <summary>
    /// Staggered spatial reveal: each splat starts its transition at a delay based on where it
    /// sits along <see cref="axis"/> (or its distance from centre in <see cref="radial"/> mode),
    /// so detail sweeps across the flower instead of blending all at once.
    /// </summary>
    public class GsplatMorphBloom : GsplatMorphModifier
    {
        [Tooltip("Direction the reveal sweeps along (LOCAL space). Default +Z = world-up (stem->petals) for this -90deg X-rotated renderer. Retarget if your capture is oriented differently.")]
        public Vector3 axis = Vector3.forward;

        [Tooltip("Reveal outward from the centre instead of along an axis.")]
        public bool radial = false;

        [Tooltip("Fade each splat in as the wavefront reaches it, so the flower visibly assembles along the sweep. Off = only reorders timing (subtle, since both endpoints are the same flower).")]
        public bool reveal = true;

        [Tooltip("Fraction of the timeline each individual splat's transition occupies. Small = sharp travelling wave, large = soft overlap.")]
        [Range(0.05f, 1f)] public float bandFraction = 0.4f;

        [Tooltip("Reverse the sweep direction.")]
        public bool invert = false;

        public override string Keyword => "MORPH_BLOOM";

        static readonly int s_origin = Shader.PropertyToID("_BloomOrigin");
        static readonly int s_dir    = Shader.PropertyToID("_BloomDir");
        static readonly int s_length = Shader.PropertyToID("_BloomLength");
        static readonly int s_band   = Shader.PropertyToID("_BloomBand");
        static readonly int s_invert = Shader.PropertyToID("_BloomInvert");
        static readonly int s_radial = Shader.PropertyToID("_BloomRadial");
        static readonly int s_reveal = Shader.PropertyToID("_BloomReveal");

        Vector3 AxisN => axis.sqrMagnitude > 1e-6f ? axis.normalized : Vector3.up;

        public override void Configure(ComputeShader cs, int kernel, float t, in GsplatMorphContext ctx)
        {
            var b = ctx.localBounds;
            Vector3 origin;
            float length;
            if (radial)
            {
                origin = b.center;
                length = Mathf.Max(b.extents.magnitude, 1e-4f);
            }
            else
            {
                Vector3 a = AxisN;
                float projExtent = Mathf.Abs(a.x * b.extents.x) + Mathf.Abs(a.y * b.extents.y) + Mathf.Abs(a.z * b.extents.z);
                origin = b.center - a * projExtent;
                length = Mathf.Max(2f * projExtent, 1e-4f);
            }

            cs.SetVector(s_origin, origin);
            cs.SetVector(s_dir, AxisN);
            cs.SetFloat(s_length, length);
            cs.SetFloat(s_band, bandFraction);
            cs.SetFloat(s_invert, invert ? 1f : 0f);
            cs.SetFloat(s_radial, radial ? 1f : 0f);
            cs.SetFloat(s_reveal, reveal ? 1f : 0f);
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Vector3 c = transform.position;
            if (radial)
                Gizmos.DrawWireSphere(c, 0.3f);
            else
                Gizmos.DrawRay(c, transform.TransformDirection(AxisN) * 0.4f);
        }
#endif
    }
}

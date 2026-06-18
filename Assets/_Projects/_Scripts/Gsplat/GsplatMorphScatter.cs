using UnityEngine;

namespace Gsplat.Animation
{
    /// <summary>
    /// Explode &amp; reassemble: a per-splat random offset pushes the cloud into a chaotic swarm
    /// around the midpoint of the transition and resolves back to zero at both endpoints, so the
    /// flower bursts apart then snaps into focus.
    /// </summary>
    public class GsplatMorphScatter : GsplatMorphModifier
    {
        [Tooltip("Peak displacement at mid-morph (metres).")]
        public float amount = 0.1f;

        [Tooltip("Spatial frequency of the scatter pattern. Higher = finer, more incoherent swarm.")]
        public float frequency = 8f;

        [Tooltip("Changes the random pattern.")]
        public float seed = 0f;

        public override string Keyword => "MORPH_SCATTER";

        static readonly int s_amount = Shader.PropertyToID("_ScatterAmount");
        static readonly int s_freq   = Shader.PropertyToID("_ScatterFreq");
        static readonly int s_seed   = Shader.PropertyToID("_ScatterSeed");

        public override void Configure(ComputeShader cs, int kernel, float t, in GsplatMorphContext ctx)
        {
            cs.SetFloat(s_amount, amount);
            cs.SetFloat(s_freq, frequency);
            cs.SetVector(s_seed, new Vector3(seed, seed * 2.137f + 11.3f, seed * 4.71f + 47.1f));
        }
    }
}

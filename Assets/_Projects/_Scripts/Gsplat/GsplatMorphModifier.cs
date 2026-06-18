using UnityEngine;

namespace Gsplat.Animation
{
    /// <summary>
    /// Per-frame context handed to every <see cref="GsplatMorphModifier"/> so it can size its
    /// effect to the splat cloud without owning any buffers itself.
    /// </summary>
    public struct GsplatMorphContext
    {
        public Bounds localBounds; // host asset bounds in the renderer's local space
        public Vector3 centroid;   // mean of host (target) splat positions, local space
        public int count;          // number of splats being morphed
    }

    /// <summary>
    /// Base class for a single optional morph effect. Each concrete effect is its own component;
    /// add the ones you want to the morph host and toggle them with their enable checkbox.
    ///
    /// <see cref="GsplatSplatMorph"/> drives these: every frame, before dispatching the compute
    /// kernel, it disables all effect keywords, then for each enabled modifier it enables
    /// <see cref="Keyword"/> and calls <see cref="Configure"/> so the modifier can push its uniforms.
    /// </summary>
    [RequireComponent(typeof(GsplatSplatMorph))]
    public abstract class GsplatMorphModifier : MonoBehaviour
    {
        /// <summary>The compute multi_compile keyword this effect gates (must match GsplatMorph.compute).</summary>
        public abstract string Keyword { get; }

        /// <summary>Push this effect's uniforms onto the kernel. Called only while enabled.</summary>
        public abstract void Configure(ComputeShader cs, int kernel, float t, in GsplatMorphContext ctx);
    }
}

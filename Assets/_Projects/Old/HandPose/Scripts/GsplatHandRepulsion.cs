using UnityEngine;

namespace Gsplat.Animation
{
    /// <summary>
    /// Pushes Gaussian splats away from a target point (e.g. a hand joint) in world space.
    /// Works by displacing each splat's projected centre in the vertex shader — Quest 3 safe.
    ///
    /// Requires: the GsplatRenderer's material to use "Gsplat/Repulsion" shader.
    ///
    /// Setup:
    ///   1. Add this component to any GameObject in the scene.
    ///   2. Set Target to a hand joint Transform (e.g. palm anchor from OVRHand or XR hand).
    ///      Falls back to Camera.main position if unset (useful for gaze-based testing).
    ///   3. Set RepulsionRadius (metres) — how far from the hand the effect reaches.
    ///   4. Set RepulsionStrength (metres) — max displacement at the hand centre.
    /// </summary>
    public class GsplatHandRepulsion : MonoBehaviour
    {
        [Header("Target")]
        [Tooltip("Hand joint to repel from (e.g. palm/wrist anchor). Falls back to Camera.main.")]
        public Transform target;

        [Header("Repulsion")]
        [Tooltip("Radius of influence in metres. Splats outside this are unaffected.")]
        [Min(0f)]
        public float repulsionRadius = 0.3f;

        [Tooltip("Maximum displacement at the centre of the repulsion sphere (metres).")]
        [Min(0f)]
        public float repulsionStrength = 0.4f;

        [Tooltip("Enable/disable the effect without removing the component.")]
        public bool enableRepulsion = true;

        static readonly int s_center   = Shader.PropertyToID("_GsplatRepulsionCenter");
        static readonly int s_radius   = Shader.PropertyToID("_GsplatRepulsionRadius");
        static readonly int s_strength = Shader.PropertyToID("_GsplatRepulsionStrength");

        Transform GetTarget()
        {
            if (target != null) return target;
            Camera cam = Camera.main;
            return cam != null ? cam.transform : null;
        }

        void Update()
        {
            if (!enableRepulsion)
            {
                Shader.SetGlobalFloat(s_radius, 0f);
                return;
            }

            Transform t = GetTarget();
            if (t == null)
            {
                Debug.LogWarning("[GsplatHandRepulsion] No target found — assign one or ensure Camera.main exists.", this);
                return;
            }

            Shader.SetGlobalVector(s_center,   t.position);
            Shader.SetGlobalFloat(s_radius,    repulsionRadius);
            Shader.SetGlobalFloat(s_strength,  repulsionStrength);
        }

        void OnDisable()
        {
            // Zero radius disables the branch in the shader with no cost
            Shader.SetGlobalFloat(s_radius, 0f);
        }
    }
}

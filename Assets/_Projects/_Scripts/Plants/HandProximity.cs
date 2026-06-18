using System.Collections.Generic;
using UnityEngine;

namespace Plants
{
    /// <summary>
    /// Scene singleton that reports how close the user's hands are to a collider. Wired
    /// once with the hand anchor transforms (e.g. Left/RightHandAnchor); plants query it
    /// each frame to drive their idle shimmer (see <see cref="Plant"/>).
    /// </summary>
    public class HandProximity : MonoBehaviour
    {
        [Tooltip("Hand transforms to measure from (usually Left/RightHandAnchor). The nearest is used.")]
        [SerializeField] private Transform[] hands = new Transform[0];

        public static HandProximity Instance { get; private set; }

        /// <summary>The wired hand anchor transforms, so other systems (e.g. HandReadyCue)
        /// can follow the hands without re-wiring them.</summary>
        public IReadOnlyList<Transform> Hands => hands;

        void Awake()
        {
            if (Instance != null && Instance != this)
                Debug.LogWarning($"[HandProximity] Multiple instances; '{name}' overriding existing.", this);
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// Distance from the nearest hand to the collider's surface (bounds approximation,
        /// so it works for box and mesh colliders alike). Returns false if there are no
        /// hands or no collider to measure against.
        /// </summary>
        public bool TryNearestDistance(Collider c, out float dist)
        {
            dist = float.MaxValue;
            if (c == null || hands == null) return false;

            bool any = false;
            foreach (var h in hands)
            {
                if (h == null) continue;
                Vector3 p = h.position;
                float d = Vector3.Distance(p, c.ClosestPointOnBounds(p));
                if (d < dist) dist = d;
                any = true;
            }
            return any;
        }
    }
}
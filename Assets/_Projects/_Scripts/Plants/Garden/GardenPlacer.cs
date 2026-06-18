using System.Collections.Generic;
using UnityEngine;

namespace Plants.Garden
{
    /// <summary>
    /// Scene-singleton placement engine for the dynamic garden layout.
    ///
    /// Owns a shared occupancy registry of every placed plant / scattered copy
    /// (each stored as its convex mesh collider + the world pose it was placed at)
    /// and finds well-spaced, non-overlapping poses for new ones inside a boundary
    /// (the scene's <c>SpreadCollider</c>), on the floor, away from the user.
    ///
    /// Overlap is tested by true 3D shape via <see cref="Physics.ComputePenetration"/>
    /// against the registry — so a small plant can sit near a tree's trunk without
    /// colliding with the canopy above it. Spread is even (best-candidate: each new
    /// pose is pushed as far from its nearest placed neighbour as the free space allows),
    /// which also makes large plants (bigger colliders) settle further apart than small
    /// ones, with breathing room.
    ///
    /// The registry stores the world pose at placement time (not a live transform), so a
    /// plant whose collider is later disabled (e.g. by <see cref="Plant.Like"/>) still
    /// blocks new placements.
    /// </summary>
    public class GardenPlacer : MonoBehaviour
    {
        [Header("Boundary")]
        [Tooltip("Box defining the garden footprint. Placements are sampled inside its world " +
                 "XZ bounds, on its floor (bounds.min.y). If unset, the scene object named " +
                 "'SpreadCollider' is found automatically.")]
        [SerializeField] private Collider boundary;

        [Tooltip("Name to search for when Boundary is unset.")]
        [SerializeField] private string boundaryObjectName = "SpreadCollider";

        [Header("User")]
        [Tooltip("Head/centre-eye transform copies keep clear of. Falls back to Camera.main.")]
        [SerializeField] private Transform userHead;

        [Tooltip("Radius (m) around the user's head that nothing may spawn inside.")]
        [SerializeField, Min(0f)] private float userKeepOut = 0.6f;

        [Header("Placement search")]
        [Tooltip("Random darts thrown per placement. More = better spacing, slightly more cost.")]
        [SerializeField, Min(1)] private int candidatesPerPlacement = 32;

        [Tooltip("Extra separation (m) the mesh-penetration test must clear between two footprints. " +
                 "0 = touching allowed; raise for more air.")]
        [SerializeField, Min(0f)] private float overlapMargin = 0.05f;

        // ── Singleton ────────────────────────────────────────────────────────────
        public static GardenPlacer Instance { get; private set; }

        void Awake()
        {
            if (Instance != null && Instance != this)
                Debug.LogWarning($"[GardenPlacer] Multiple instances; '{name}' overriding existing.", this);
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Lazily-resolved scene singleton: returns the existing instance, finds one,
        /// or creates a bare GameObject hosting one. Lets callers place without manual wiring.</summary>
        public static GardenPlacer GetOrCreate()
        {
            if (Instance != null) return Instance;
            var found = FindFirstObjectByType<GardenPlacer>();
            if (found != null) { Instance = found; return found; }
            var go = new GameObject("GardenPlacer");
            return go.AddComponent<GardenPlacer>();
        }

        // ── Registry ─────────────────────────────────────────────────────────────

        private struct Occupant
        {
            public object owner;       // key for removal (Plant, instance GO)
            public Collider shape;     // convex mesh collider whose geometry/scale defines the footprint
            public Vector3 position;   // world position to evaluate the collider at
            public Quaternion rotation;
            public float radius;       // cached XZ circumscribing radius (broad-phase)
        }

        private readonly List<Occupant> m_occupants = new List<Occupant>();

        /// <summary>Record a placed footprint so future placements avoid it.</summary>
        public void Register(object owner, Collider shape, Vector3 worldPos, Quaternion worldRot)
        {
            if (shape == null) return;
            m_occupants.Add(new Occupant
            {
                owner = owner,
                shape = shape,
                position = worldPos,
                rotation = worldRot,
                radius = FootprintRadius(shape),
            });
        }

        /// <summary>Drop every footprint owned by <paramref name="owner"/> (e.g. on destroy/reset).</summary>
        public void Remove(object owner)
        {
            for (int i = m_occupants.Count - 1; i >= 0; i--)
                if (ReferenceEquals(m_occupants[i].owner, owner)) m_occupants.RemoveAt(i);
        }

        /// <summary>Clear the whole registry (experience reset).</summary>
        public void Clear() => m_occupants.Clear();

        // ── Placement ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Find a world pose for <paramref name="root"/> such that its child
        /// <paramref name="footprint"/> collider lands inside the boundary, on the floor,
        /// clear of the user, and not overlapping any registered footprint. The root keeps
        /// its current scale; rotation is a random yaw (if <paramref name="randomYaw"/>) or
        /// the root's current yaw. <paramref name="footprintPose"/> returns where that
        /// collider would resolve in world space (for registration).
        ///
        /// Returns false only if the boundary is missing — otherwise always yields a pose
        /// (the least-overlapping one if the garden is full, logging a one-time warning).
        /// </summary>
        public bool TryFindRootPose(Transform root, Collider footprint, bool randomYaw,
                                    out Pose rootPose, out Pose footprintPose)
        {
            rootPose = default;
            footprintPose = default;
            if (root == null || footprint == null) return false;

            Collider b = ResolveBoundary();
            if (b == null)
            {
                Debug.LogWarning("[GardenPlacer] No boundary collider — cannot place.", this);
                return false;
            }

            Bounds region = b.bounds;

            // Collider's pose relative to the root, so we can predict where the footprint
            // lands for any candidate root pose.
            Matrix4x4 colInRoot = root.worldToLocalMatrix * footprint.transform.localToWorldMatrix;
            Vector3 rootScale = root.lossyScale;
            // Plants are authored sitting on the floor, so keep the root's height and only
            // reposition on the ground plane (XZ) — robust and floor-correct without depending
            // on a possibly-disabled collider's bounds.
            float rootY = root.position.y;

            Transform head = ResolveHead();
            float userR2 = userKeepOut * userKeepOut;
            float candRadius = FootprintRadius(footprint);

            // Preserve the root's authored orientation (gsplat roots carry a base rotation;
            // dropping it flips the visual upside down) and only add a spin around world-up.
            Quaternion baseRot = root.rotation;

            Pose bestRoot = default, bestFoot = default;
            float bestClearance = float.NegativeInfinity;  // nearest-neighbour distance (higher = better)
            float bestPenetration = float.PositiveInfinity; // overlap depth (lower = better) for fallback
            bool foundClean = false;

            for (int c = 0; c < candidatesPerPlacement; c++)
            {
                Quaternion rot = randomYaw
                    ? Quaternion.AngleAxis(Random.value * 360f, Vector3.up) * baseRot
                    : baseRot;

                float x = Mathf.Lerp(region.min.x, region.max.x, Random.value);
                float z = Mathf.Lerp(region.min.z, region.max.z, Random.value);
                Vector3 rootPos = new Vector3(x, rootY, z);

                // Where the footprint collider would sit for this candidate.
                Matrix4x4 colWorld = Matrix4x4.TRS(rootPos, rot, rootScale) * colInRoot;
                Vector3 colPos = colWorld.GetColumn(3);
                Quaternion colRot = colWorld.rotation;

                // User keep-out (horizontal).
                if (head != null)
                {
                    Vector3 d = colPos - head.position; d.y = 0f;
                    if (d.sqrMagnitude < userR2) continue;
                }

                // Overlap + nearest-neighbour distance against the registry.
                float maxPenetration = 0f;
                float nearest = float.PositiveInfinity;
                for (int i = 0; i < m_occupants.Count; i++)
                {
                    var o = m_occupants[i];
                    if (o.shape == null) continue;

                    Vector3 flat = colPos - o.position; flat.y = 0f;
                    float dist = flat.magnitude;
                    if (dist < nearest) nearest = dist;

                    // Broad-phase: skip the exact test when footprints can't possibly touch.
                    if (dist > o.radius + candRadius + overlapMargin) continue;

                    if (Physics.ComputePenetration(
                            footprint, colPos, colRot,
                            o.shape, o.position, o.rotation,
                            out _, out float pen))
                    {
                        float eff = pen + overlapMargin;
                        if (eff > maxPenetration) maxPenetration = eff;
                    }
                }

                if (maxPenetration <= 0f)
                {
                    // Clean: keep the candidate farthest from its nearest neighbour (even spread).
                    if (nearest > bestClearance)
                    {
                        bestClearance = nearest;
                        bestRoot = new Pose(rootPos, rot);
                        bestFoot = new Pose(colPos, colRot);
                        foundClean = true;
                    }
                }
                else if (!foundClean && maxPenetration < bestPenetration)
                {
                    // No clean spot yet — remember the least-overlapping fallback.
                    bestPenetration = maxPenetration;
                    bestRoot = new Pose(rootPos, rot);
                    bestFoot = new Pose(colPos, colRot);
                }
            }

            if (!foundClean)
                Debug.LogWarning($"[GardenPlacer] No clear spot for '{root.name}' " +
                                 $"({m_occupants.Count} placed) — using least-overlapping fallback. " +
                                 "Boundary may be too small for the garden.", root);

            rootPose = bestRoot;
            footprintPose = bestFoot;
            return true;
        }

        /// <summary>
        /// Convenience: find a pose, move <paramref name="root"/> to it, and register the
        /// footprint so later placements avoid it. Returns the registry owner key used
        /// (pass it to <see cref="Remove"/> on destroy), or null if placement failed.
        /// </summary>
        public object ApplyAndRegister(Transform root, Collider footprint, bool randomYaw, object owner = null)
        {
            if (!TryFindRootPose(root, footprint, randomYaw, out Pose rootPose, out Pose footPose))
                return null;

            root.SetPositionAndRotation(rootPose.position, rootPose.rotation);
            object key = owner ?? root.gameObject;
            Register(key, footprint, footPose.position, footPose.rotation);
            return key;
        }

        // ── Helpers ─────────────────────────────────────────────────────────────

        /// <summary>XZ circumscribing radius of a collider's footprint, computed from the mesh's
        /// local bounds × world scale so it stays valid even when the collider is disabled
        /// (Collider.bounds can read zero while disabled).</summary>
        public static float FootprintRadius(Collider c)
        {
            if (c is MeshCollider mc && mc.sharedMesh != null)
            {
                Vector3 e = Vector3.Scale(mc.sharedMesh.bounds.extents, c.transform.lossyScale);
                return new Vector2(e.x, e.z).magnitude;
            }
            Bounds b = c.bounds;
            return new Vector2(b.extents.x, b.extents.z).magnitude;
        }

        private Collider ResolveBoundary()
        {
            if (boundary != null) return boundary;
            if (!string.IsNullOrEmpty(boundaryObjectName))
            {
                var go = GameObject.Find(boundaryObjectName);
                if (go != null) boundary = go.GetComponent<Collider>();
            }
            return boundary;
        }

        private Transform ResolveHead()
        {
            if (userHead != null) return userHead;
            var cam = Camera.main;
            return cam != null ? cam.transform : null;
        }

        /// <summary>Expose the resolved boundary's floor Y (for callers that snap to ground).</summary>
        public bool TryGetFloorY(out float floorY)
        {
            Collider b = ResolveBoundary();
            floorY = b != null ? b.bounds.min.y : 0f;
            return b != null;
        }
    }
}

using Gsplat;
using UnityEngine;

namespace Plants
{
    /// <summary>
    /// Stateless helper: casts a ray from the head (centre-eye) forward and returns the nearest
    /// splat instance it hits, mapped to its owning <see cref="Plant"/>. This replaces the old
    /// gaze "cone" (angle-from-forward) test — the ray now collides with the fitted convex mesh
    /// colliders that every splat instance already carries (the collider host sits under the
    /// "Gaussian" source the scatterer clones). Non-plant colliders (floor, hands, …) are ignored
    /// so they never block the gaze. Used post-flourish by <see cref="ExperienceManager"/> to
    /// hover-highlight the looked-at instance and to pick which kept plant to ask for its context.
    /// </summary>
    public class GazeInstanceTargeter : MonoBehaviour
    {
        [Tooltip("Centre-eye / head transform the ray originates from. Falls back to Camera.main.")]
        [SerializeField] private Transform head;
        [Tooltip("Maximum gaze ray length (metres).")]
        [SerializeField, Min(0f)] private float maxRayDistance = 12f;
        [Tooltip("Radius of the gaze sphere-cast (metres). A fat cast catches the wispy / irregular " +
                 "splat colliders that a thin ray slips past, so you no longer have to aim dead-on. " +
                 "0 degrades to an ordinary thin ray.")]
        [SerializeField, Min(0f)] private float gazeRadius = 0.18f;
        [Tooltip("After the cast stops hitting a plant, keep reporting the last target for this many " +
                 "seconds. Bridges the brief drop-outs from head jitter at a collider edge so the " +
                 "highlight doesn't flicker and the context gesture still lands on it.")]
        [SerializeField, Min(0f)] private float stickyGrace = 0.25f;
        [Tooltip("Layers the gaze ray may hit. Splat instance colliders live on the Default layer.")]
        [SerializeField] private LayerMask layerMask = ~0;

        const int k_maxHits = 16;
        readonly RaycastHit[] m_hits = new RaycastHit[k_maxHits];

        // Sticky state: the last plant / instance the cast locked onto, and when. Lets the gaze hold a
        // stable target across the brief frames the (now forgiving) cast still misses — see TryGetTarget.
        Plant m_lastPlant;
        GameObject m_lastInstance;
        float m_lastHitTime = -999f;

        /// <summary>The head transform used, or Camera.main if none is wired.</summary>
        public Transform Head => head != null ? head : (Camera.main != null ? Camera.main.transform : null);

        /// <summary>Set the head transform (e.g. from the ExperienceManager that owns this).</summary>
        public void SetHead(Transform h) => head = h;

        /// <summary>
        /// Cast a ray from the head forward. Returns the nearest hit whose collider maps to a
        /// <see cref="Plant"/> (via <see cref="PlantTouchTrigger"/>), skipping any non-plant
        /// colliders in between so they don't block the gaze. Outputs the owning plant and the
        /// splat instance GameObject that was hit (its GsplatRenderer's GameObject).
        /// </summary>
        public bool TryGetTarget(out Plant plant, out GameObject instance)
        {
            plant = null;
            instance = null;

            Transform h = Head;
            if (h == null) return false;

            // Guard against the serialized fields deserialising to 0 when this component was
            // upgraded in-place from the old cone version: a 0 ("Nothing") mask or 0 distance
            // would silently make the gaze hit nothing.
            float dist = maxRayDistance > 0f ? maxRayDistance : 12f;
            int mask = layerMask.value != 0 ? layerMask.value : ~0;
            float radius = Mathf.Max(0f, gazeRadius);

            // A sphere-cast (fat ray) is far more forgiving than the old thin ray: it catches the
            // fitted convex colliders even when the gaze isn't dead-centre or the splat is wispy.
            // radius 0 degrades to an ordinary thin ray.
            int n = radius > 0f
                ? Physics.SphereCastNonAlloc(h.position, radius, h.forward, m_hits, dist,
                                             mask, QueryTriggerInteraction.Collide)
                : Physics.RaycastNonAlloc(h.position, h.forward, m_hits, dist,
                                          mask, QueryTriggerInteraction.Collide);

            float bestDist = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var c = m_hits[i].collider;
                if (c == null) continue;
                if (m_hits[i].distance >= bestDist) continue;

                var trigger = c.GetComponentInParent<PlantTouchTrigger>();
                if (trigger == null || trigger.Plant == null) continue;   // not a plant — ignore

                var renderer = c.GetComponentInParent<GsplatRenderer>();
                plant = trigger.Plant;
                instance = renderer != null ? renderer.gameObject : (c.transform.parent != null ? c.transform.parent.gameObject : c.gameObject);
                bestDist = m_hits[i].distance;
            }

            if (plant != null)
            {
                // Fresh hit — remember it as the sticky target and report it.
                m_lastPlant = plant;
                m_lastInstance = instance;
                m_lastHitTime = Time.time;
                return true;
            }

            // Missed this frame: keep reporting the last target for a short grace window so head
            // jitter at a collider edge doesn't drop the highlight (and the context gesture still
            // lands on it). The remembered instance must still be alive and active.
            if (Time.time - m_lastHitTime <= stickyGrace &&
                m_lastPlant != null && m_lastInstance != null && m_lastInstance.activeInHierarchy)
            {
                plant = m_lastPlant;
                instance = m_lastInstance;
                return true;
            }

            return false;
        }
    }
}

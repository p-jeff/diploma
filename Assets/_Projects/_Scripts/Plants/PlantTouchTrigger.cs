using UnityEngine;
using UnityEngine.Events;

namespace Plants
{
    [RequireComponent(typeof(Collider))]
    public class PlantTouchTrigger : MonoBehaviour
    {
        [Tooltip("The plant this trigger belongs to. Assign explicitly — no auto-find.")]
        [SerializeField] private Plant plant;

        /// <summary>The plant this trigger belongs to. Used by the gaze raycaster to map a ray
        /// hit on a splat instance's collider back to its owning plant.</summary>
        public Plant Plant => plant;

        [Tooltip("Gaze-only: still answers .Plant for the gaze raycaster, but a hand touch never " +
                 "routes Select(). Set by canopy fruit orbs so they're gaze targets without being touchable.")]
        [SerializeField] private bool gazeOnly = false;

        [Tooltip("Layers that count as the user's hands.")]
        [SerializeField] private LayerMask handLayers = ~0;

        [Tooltip("Tag a hand collider must have. Leave empty to skip tag check.")]
        [SerializeField] private string handTag = "";

        [SerializeField] private UnityEvent onTriggerEnter;

        /// <summary>Assign the owning plant at runtime (used by orbs/instances built in code).</summary>
        public void SetPlant(Plant p) => plant = p;

        /// <summary>Subscribe to the hand-touch event. Used by the title sequence, whose trigger has
        /// no plant — the touch just fires this event (the manager routing below no-ops on null).</summary>
        public void AddTouchListener(UnityAction action)
        {
            if (action != null) onTriggerEnter.AddListener(action);
        }

        /// <summary>Make this trigger gaze-only (no hand-touch routing) at runtime.</summary>
        public void SetGazeOnly(bool g) => gazeOnly = g;

        void Reset()
        {
            var c = GetComponent<Collider>();
            if (c) c.isTrigger = true;
        }

        void OnTriggerEnter(Collider other)
        {
            if (gazeOnly) return; // gaze-only orbs are never touch-selectable
            if (((1 << other.gameObject.layer) & handLayers) == 0) return;
            if (!string.IsNullOrEmpty(handTag) && !other.CompareTag(handTag)) return;
            
            onTriggerEnter.Invoke();

            if (plant == null) return;
            // Pass this trigger's transform so the manager can tell a touch on one of the plant's
            // spread preview instances (→ grow that instance's context) from a touch on the hero
            // body (→ select the plant). PlantManager has no per-instance step, so it just selects.
            if (ExperienceManager.Instance != null) ExperienceManager.Instance.Touch(plant, transform);
            else if (PlantManager.Instance != null) PlantManager.Instance.Select(plant);
            else plant.Show(); // fallback if no manager in scene

        }
    }
}

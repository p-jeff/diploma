using UnityEngine;
using UnityEngine.Events;

namespace Plants
{
    [RequireComponent(typeof(Collider))]
    public class PlantTouchTrigger : MonoBehaviour
    {
        [Tooltip("The plant this trigger belongs to. Assign explicitly — no auto-find.")]
        [SerializeField] private Plant plant;

        [Tooltip("Layers that count as the user's hands.")]
        [SerializeField] private LayerMask handLayers = ~0;

        [Tooltip("Tag a hand collider must have. Leave empty to skip tag check.")]
        [SerializeField] private string handTag = "";
        
        [SerializeField] private UnityEvent onTriggerEnter;

        void Reset()
        {
            var c = GetComponent<Collider>();
            if (c) c.isTrigger = true;
        }

        void OnTriggerEnter(Collider other)
        {
            if (((1 << other.gameObject.layer) & handLayers) == 0) return;
            if (!string.IsNullOrEmpty(handTag) && !other.CompareTag(handTag)) return;
            
            onTriggerEnter.Invoke();

            if (plant == null) return;
            plant.Selected();
        }
    }
}

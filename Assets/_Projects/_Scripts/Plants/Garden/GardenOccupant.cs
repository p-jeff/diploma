using UnityEngine;

namespace Plants.Garden
{
    /// <summary>
    /// Auto-added to a scattered copy (or any placed plant) so the footprint it reserved in
    /// <see cref="GardenPlacer"/> is released when the object is destroyed — e.g. when a plant
    /// is hidden, reset, or its ungrown previews are faded out. The owner key is this
    /// GameObject, matching how the scatterer registers it.
    /// </summary>
    public class GardenOccupant : MonoBehaviour
    {
        void OnDestroy()
        {
            if (GardenPlacer.Instance != null)
                GardenPlacer.Instance.Remove(gameObject);
        }
    }
}

using System.Collections.Generic;
using Plants.Garden;
using UnityEngine;

namespace Plants
{
    /// <summary>
    /// Runtime spawner: instantiates copies of a source object and asks the
    /// <see cref="GardenPlacer"/> to position each one — inside the garden boundary
    /// (the scene's SpreadCollider), on the floor, clear of the user, and not
    /// overlapping any already-placed plant or copy by true mesh-collider shape.
    /// Driven by <see cref="Plant"/>: on select it spreads grey previews, on like /
    /// flourish it spreads more, then reveals them in a staggered cascade.
    ///
    /// Each copy reserves its footprint in the placer's shared registry, so the whole
    /// garden auto-grows without overlap; the reservation is released automatically when
    /// the copy is destroyed (via <see cref="GardenOccupant"/>).
    /// </summary>
    public class PlantInstanceScatterer : MonoBehaviour
    {
        [Header("What to place")]
        [Tooltip("Prefab or scene object cloned for each instance. Usually the plant's gsplat visual root.")]
        [SerializeField] private GameObject source;

        /// <summary>The source object cloned per instance — the spectator client clones the same one
        /// to recreate a scatter instance the host spawned.</summary>
        public GameObject Source => source;

        [Tooltip("Parent for the spawned copies. Defaults to this object's transform.")]
        [SerializeField] private Transform parent;

        [Header("Footprint")]
        [Tooltip("Convex mesh collider whose true shape defines each copy's footprint for the " +
                 "overlap test. If unset, the parent Plant's SelectionCollider is used.")]
        [SerializeField] private Collider footprintOverride;

        [Header("Distribution")]
        [Tooltip("Default number of copies for the parameterless Spawn().")]
        [SerializeField, Min(0)] private int count = 8;

        /// <summary>The convex mesh collider used to measure footprints / test overlap.</summary>
        private Collider ResolveFootprint()
        {
            if (footprintOverride != null) return footprintOverride;
            var plant = GetComponentInParent<Plant>();
            return plant != null ? plant.SelectionCollider : null;
        }

        /// <summary>Instantiate the configured <c>count</c> of copies. See the overload.</summary>
        public List<GameObject> Spawn() => Spawn(count);

        /// <summary>
        /// Instantiate <paramref name="spawnCount"/> inactive copies of the source, each
        /// placed by the <see cref="GardenPlacer"/> so it avoids every other plant/copy and
        /// the user, and return them. The caller (Plant) owns activating/revealing and later
        /// destroying them. <paramref name="existing"/> is accepted for backward compatibility
        /// but is no longer needed — the placer's shared registry already tracks prior spreads.
        /// </summary>
        public List<GameObject> Spawn(int spawnCount, List<Vector3> existing = null)
        {
            var result = new List<GameObject>();

            if (source == null)
            {
                Debug.LogError("[PlantInstanceScatterer] Assign a Source to clone.", this);
                return result;
            }

            Collider footprint = ResolveFootprint();
            if (footprint == null)
            {
                Debug.LogError("[PlantInstanceScatterer] No footprint collider — assign a Footprint " +
                               "Override or place this scatterer under a Plant with a SelectionCollider.", this);
                return result;
            }

            GardenPlacer placer = GardenPlacer.GetOrCreate();
            Transform p = parent != null ? parent : transform;

            for (int i = 0; i < spawnCount; i++)
            {
                // Find a free pose for a clone of `source`, testing the hero footprint shape
                // against everything already placed. ComputePenetration reads the collider's
                // geometry regardless of its enabled state, so this still works after Like()
                // has disabled the selection collider.
                if (!placer.TryFindRootPose(source.transform, footprint, true,
                                            out Pose rootPose, out Pose footPose))
                    break; // no boundary configured — nothing we can do

                GameObject copy = Instantiate(source, p);
                copy.transform.SetPositionAndRotation(rootPose.position, rootPose.rotation);
                copy.name = $"{source.name}_Scatter_{i:00}";

                // Reserve this footprint so the next copies (and other plants) avoid it.
                // GardenOccupant releases the reservation when the copy is destroyed.
                placer.Register(copy, footprint, footPose.position, footPose.rotation);
                copy.AddComponent<GardenOccupant>();

                copy.SetActive(false); // caller's reveal cascade activates these one by one
                result.Add(copy);
            }

            return result;
        }
    }
}

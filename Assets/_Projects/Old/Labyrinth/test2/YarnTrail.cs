using System.Collections.Generic;
using Oculus.Interaction;
using UnityEngine;

namespace _Projects.test2
{
    /// <summary>
    /// Attach to the yarn ball alongside Oculus.Interaction.Grabbable.
    /// Assign the child LineRenderer in the Inspector.
    /// Grab state is detected automatically via GrabPoints.
    /// </summary>
    public class YarnTrail : MonoBehaviour
    {
        [SerializeField] LineRenderer lineRenderer;
        [SerializeField] float sampleDistance = 0.05f;

        private Grabbable grabbable;
        private bool wasGrabbed = false;
        private readonly List<Vector3> positions = new List<Vector3>();

        void Start()
        {
            grabbable = GetComponent<Grabbable>();
        }

        void Update()
        {
            if (grabbable == null || lineRenderer == null) return;

            bool isGrabbed = grabbable.GrabPoints.Count > 0;

            if (isGrabbed && !wasGrabbed)
            {
                positions.Clear();
                lineRenderer.positionCount = 0;
            }

            if (isGrabbed)
            {
                Vector3 current = transform.position;
                if (positions.Count == 0 ||
                    Vector3.Distance(current, positions[positions.Count - 1]) > sampleDistance)
                {
                    positions.Add(current);
                    lineRenderer.positionCount = positions.Count;
                    lineRenderer.SetPositions(positions.ToArray());
                }
            }

            wasGrabbed = isGrabbed;
        }
    }
}

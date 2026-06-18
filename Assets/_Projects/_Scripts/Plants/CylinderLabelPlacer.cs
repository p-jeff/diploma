using UnityEngine;

namespace Plants
{
    /// <summary>
    /// Places a label on a vertical cylinder centred on a plant's axis — on the
    /// camera-facing side, at a fixed radius and height — and yaw-billboards it to face
    /// the viewer. The alternative to the default "float straight above" placement, useful
    /// for tall plants (tree trunks) where a label high overhead reads poorly.
    ///
    /// By default it snaps into place once (via <see cref="Activate"/> / <see cref="Resnap"/>)
    /// and then holds still, so the poem is easy to stop and read. Enable
    /// <see cref="continuous"/> to make it orbit every frame and continuously follow the viewer.
    ///
    /// Added and driven at runtime by <see cref="PlantInfo"/>: it positions the label in
    /// world space, independent of its parent, so the rest of the PlantInfos hierarchy is
    /// left untouched.
    /// </summary>
    [DisallowMultipleComponent]
    public class CylinderLabelPlacer : MonoBehaviour
    {
        [Tooltip("Who to orbit toward / face. Defaults to Camera.main when unset.")]
        [SerializeField] private Transform target;

        [Tooltip("Flip 180° so the label's front faces the viewer (matches LookAtTarget.flipY).")]
        [SerializeField] private bool flip = true;

        [Tooltip("Keep orbiting every frame to follow the viewer. When false (default) the label " +
                 "is placed once and held still, so it stays readable while the viewer reads it.")]
        [SerializeField] private bool continuous = false;

        // World point of the cylinder axis at the label's height: (centreX, labelY, centreZ).
        private Vector3 m_axisPoint;
        private float m_radius;
        private bool m_placed;

        // Last good orbit direction, reused when the camera sits exactly on the axis (XZ).
        private Vector3 m_lastDir = Vector3.forward;

        private Transform Target => target != null ? target : (Camera.main != null ? Camera.main.transform : null);

        /// <summary>Place the label around <paramref name="axisPoint"/> at
        /// <paramref name="radius"/>, snapping to the camera-facing side immediately.</summary>
        public void Activate(Vector3 axisPoint, float radius)
        {
            m_axisPoint = axisPoint;
            m_radius = Mathf.Max(0f, radius);
            m_placed = true;
            Reposition();
        }

        /// <summary>Re-aim a placed label at the viewer's current position. Used when a held
        /// label (re)appears after the viewer may have moved. No-op until <see cref="Activate"/>.</summary>
        public void Resnap()
        {
            if (m_placed) Reposition();
        }

        /// <summary>Stop driving the transform (leaves it where it last was).</summary>
        public void Deactivate() => m_placed = false;

        private void LateUpdate()
        {
            // Only follow the viewer per-frame when explicitly opted in; otherwise the label
            // holds the pose it was placed with so it doesn't swim while being read.
            if (m_placed && continuous) Reposition();
        }

        private void Reposition()
        {
            var cam = Target;

            // Direction from the axis out to the camera, flattened to the XZ plane.
            Vector3 dir = m_lastDir;
            if (cam != null)
            {
                Vector3 d = cam.position - m_axisPoint;
                d.y = 0f;
                if (d.sqrMagnitude > 1e-6f) dir = d.normalized;
            }
            m_lastDir = dir;

            transform.position = m_axisPoint + dir * m_radius;

            if (cam != null)
            {
                // Yaw-only billboard, same convention as LookAtTarget (Y axis, optional flip).
                Quaternion look = Quaternion.LookRotation(dir, Vector3.up) * Quaternion.Euler(0f, flip ? 180f : 0f, 0f);
                transform.rotation = Quaternion.Euler(0f, look.eulerAngles.y, 0f);
            }
        }
    }
}

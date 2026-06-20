using UnityEngine;

namespace _Projects.HandPose
{
    public class FollowTransform : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField] private Transform target;

        [Header("Activation")]
        [SerializeField] private bool runOnEnable = true;
        [SerializeField] private bool runOnUpdate = true;

        [Header("Follow Options")]
        [SerializeField] private bool followPosition = true;
        [SerializeField] private bool followRotation = true;
        [SerializeField] private bool followScale;

        [Header("Offsets")]
        [SerializeField] private Vector3 positionOffset;
        [SerializeField] private Vector3 rotationOffset;
        [SerializeField] private Vector3 scaleOffset;

        [Tooltip("Apply positionOffset in world space (e.g. a +Y lift always floats straight up) " +
                 "instead of along the target's local axes. Use for cues that should float above the " +
                 "hand regardless of how the hand is tilted.")]
        [SerializeField] private bool worldSpaceOffset;

        private Vector3 PositionWithOffset()
            => target.position + (worldSpaceOffset ? positionOffset : target.TransformDirection(positionOffset));

        public void SetTarget(Transform t) { if (t != null) target = t; }

        private void Start()
        {
            if (!runOnEnable || target == null) return;

            if (followPosition)
                transform.position = PositionWithOffset();

            if (followRotation)
                transform.rotation = target.rotation * Quaternion.Euler(rotationOffset);

            if (followScale)
                transform.localScale = target.localScale + scaleOffset;
        }

        private void LateUpdate()
        {
            if (!runOnUpdate || target == null) return;

            if (followPosition)
                transform.position = PositionWithOffset();

            if (followRotation)
                transform.rotation = target.rotation * Quaternion.Euler(rotationOffset);

            if (followScale)
                transform.localScale = target.localScale + scaleOffset;
        }

    }
}
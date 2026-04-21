using UnityEngine;

namespace _Projects.HandPose
{
    public class FollowTransform : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField] private Transform target;

        [Header("Follow Options")]
        [SerializeField] private bool followPosition = true;
        [SerializeField] private bool followRotation = true;
        [SerializeField] private bool followScale;

        [Header("Offsets")]
        [SerializeField] private Vector3 positionOffset;
        [SerializeField] private Vector3 rotationOffset;
        [SerializeField] private Vector3 scaleOffset;

        private void LateUpdate()
        {
            if (target == null) return;

            if (followPosition)
                transform.position = target.position + target.TransformDirection(positionOffset);

            if (followRotation)
                transform.rotation = target.rotation * Quaternion.Euler(rotationOffset);

            if (followScale)
                transform.localScale = target.localScale + scaleOffset;
        }
    }
}
using UnityEngine;

public class  LookAtTarget : MonoBehaviour
{
    [Header("Target")]
    public Transform target;

    [Header("Axes")]
    public bool rotateX = false;
    public bool rotateY = true;
    public bool rotateZ = false;
    public bool flipY = true;

    [Header("Smoothing")]
    [Tooltip("How quickly the object rotates toward the target. Lower = more rubberbanding.")]
    [Range(0.1f, 20f)]
    public float rotationSpeed = 5f;

    [Tooltip("Angle deadzone in degrees — stops rotating when this close to target.")]
    [Range(0f, 45f)]
    public float deadzoneAngle = 2f;

    [Header("Update Mode")]
    public bool runOnStart = true;
    public bool runOnUpdate = true;

    void Start()
    {
        if (runOnStart && target != null)
            SnapToTarget();
    }

    void Update()
    {
        if (runOnUpdate && target != null)
            RotateTowardTarget(Time.deltaTime);
    }

    void SnapToTarget()
    {
        Quaternion desired = GetDesiredRotation();
        transform.rotation = desired;
    }

    void RotateTowardTarget(float deltaTime)
    {
        Quaternion desired = GetDesiredRotation();
        float angle = Quaternion.Angle(transform.rotation, desired);

        if (angle <= deadzoneAngle)
            return;

        transform.rotation = Quaternion.Slerp(transform.rotation, desired, rotationSpeed * deltaTime);
    }

    Quaternion GetDesiredRotation()
    {
        Vector3 direction = target.position - transform.position;

        if (direction == Vector3.zero)
            return transform.rotation;

        // Y-only (cylindrical billboard): flatten direction to the XZ plane so
        // LookRotation only ever produces a yaw. Without this, a camera above
        // or below the object causes pitch to bleed into the Y euler angle.
        if (!rotateX)
            direction.y = 0f;

        if (direction == Vector3.zero)
            return transform.rotation;

        Quaternion desired = Quaternion.LookRotation(direction, Vector3.up) * Quaternion.Euler(0f, flipY ? 180f : 0f, 0f);

        // Preserve axes that should not rotate.
        if (!rotateX || !rotateZ)
        {
            Vector3 e = desired.eulerAngles;
            Vector3 c = transform.rotation.eulerAngles;
            desired = Quaternion.Euler(
                rotateX ? e.x : c.x,
                rotateY ? e.y : c.y,
                rotateZ ? e.z : c.z
            );
        }

        return desired;
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawLine(transform.position - transform.up * 1f, transform.position + transform.up * 1f);
    }
#endif
}

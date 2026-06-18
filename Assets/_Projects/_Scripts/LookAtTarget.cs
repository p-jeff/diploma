using UnityEngine;

/// <summary>
/// Yaw-billboards this object to face a target (defaults to the main camera). It snaps
/// once each time the object is enabled rather than tracking every frame, so labels read
/// calmly in the headset instead of swimming as the viewer moves. Callers that reposition
/// the object at runtime can re-aim it explicitly via <see cref="Snap"/>.
/// </summary>
public class LookAtTarget : MonoBehaviour
{
    [Header("Target")]
    public Transform target;

    [Header("Axes")]
    public bool rotateX = false;
    public bool rotateY = true;
    public bool rotateZ = false;
    public bool flipY = true;

    // Lazily resolved fallback when target is not manually wired.
    private Transform m_resolvedTarget;

    /// <summary>Returns the wired target if set; otherwise resolves Camera.main once and caches it.</summary>
    private Transform ResolveTarget()
    {
        if (target != null) return target;
        if (m_resolvedTarget != null) return m_resolvedTarget;
        var cam = Camera.main;
        if (cam != null) m_resolvedTarget = cam.transform;
        return m_resolvedTarget;
    }

    // Snap to face the target whenever the object becomes enabled (incl. SetActive(true) at
    // runtime), then leave it fixed — no per-frame tracking.
    void OnEnable() => Snap();

    // Fallback for objects already active at scene load, in case Camera.main isn't
    // resolvable yet during OnEnable.
    void Start() => Snap();

    /// <summary>Immediately aim at the target, ignoring smoothing. Use when an object is
    /// repositioned at runtime and must billboard right away.</summary>
    public void Snap()
    {
        if (ResolveTarget() != null)
            transform.rotation = GetDesiredRotation();
    }

    Quaternion GetDesiredRotation()
    {
        Vector3 direction = ResolveTarget().position - transform.position;

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

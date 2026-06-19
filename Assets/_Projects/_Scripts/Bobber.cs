using UnityEngine;

/// <summary>
/// Gently bobs this transform up and down around its starting local position using a sine
/// wave — a soft "floating" affordance for prompts and labels (e.g. the title "Touch Me"
/// sprite). Captures its rest position when enabled, so it plays nicely with objects that
/// are repositioned or toggled at runtime, and restores that position when disabled.
///
/// The offset is applied along a LOCAL axis (default = up). For a yaw-billboarded prompt
/// (see <see cref="LookAtTarget"/>) the local up stays aligned with world up, so this reads
/// as a clean vertical bob no matter where the viewer stands.
/// </summary>
public class Bobber : MonoBehaviour
{
    [Tooltip("Peak offset from the rest position, in metres (local space).")]
    [SerializeField, Min(0f)] private float amplitude = 0.01f;
    [Tooltip("Seconds for one full up-and-down cycle.")]
    [SerializeField, Min(0.01f)] private float period = 2f;
    [Tooltip("Direction to bob along, in this object's LOCAL space (default = up).")]
    [SerializeField] private Vector3 axis = Vector3.up;
    [Tooltip("Randomise the starting phase so several bobbers don't move in lockstep.")]
    [SerializeField] private bool randomizePhase = false;

    private Vector3 m_restPos;
    private float m_phaseOffset;

    void OnEnable()
    {
        m_restPos = transform.localPosition;
        m_phaseOffset = randomizePhase ? Random.value * Mathf.PI * 2f : 0f;
    }

    void OnDisable()
    {
        // Leave the object exactly where it started so toggling/replaying never drifts it.
        transform.localPosition = m_restPos;
    }

    void Update()
    {
        float w = Mathf.Sin(Time.time / Mathf.Max(period, 0.0001f) * Mathf.PI * 2f + m_phaseOffset);
        transform.localPosition = m_restPos + axis.normalized * (amplitude * w);
    }
}

using UnityEngine;

namespace Plants
{
    /// <summary>
    /// A floating, billboarded "touch me" hand sprite that hovers above a plant and fades in only
    /// while the plant is touchable AND the viewer's head is within range — a from-a-distance
    /// invitation to come and touch. Icon only, no text.
    ///
    /// This component lives <b>on the "Touch Me Prompt" object itself</b> (a world-space Canvas +
    /// CanvasGroup + Image + <see cref="LookAtTarget"/>), so its settings sit with the visual they
    /// drive and are art-directable in the Inspector — and because that object lives on the base
    /// <c>Plant.prefab</c>, editing the sprite / scale / tilt / tuning once applies to every plant
    /// variant at a time. It builds nothing at runtime; it only drives its own transform: proximity
    /// gating, fade, bob and the beckon. The object stays <i>active</i> so this driver keeps running;
    /// visibility is toggled via the Canvas + CanvasGroup, not SetActive.
    ///
    /// Driven by <see cref="Plant"/>: <see cref="Arm"/> while dormant/available (alongside the touch
    /// glow) and <see cref="Disarm"/> once touched or liked, so it rides the exact same availability
    /// lifecycle. Proximity gating is done here every frame.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class TouchMePrompt : MonoBehaviour
    {
        [Header("Placement")]
        [Tooltip("Height above the plant's ground point where the sprite floats.")]
        [SerializeField, Min(0f)] private float height = 0.9f;

        [Header("Proximity")]
        [Tooltip("Fade the prompt in when the viewer's head is closer than this (metres).")]
        [SerializeField, Min(0f)] private float showDistance = 1.6f;
        [Tooltip("Extra distance beyond Show Distance before it fades back out (anti-flicker hysteresis).")]
        [SerializeField, Min(0f)] private float hideMargin = 0.4f;
        [Tooltip("Seconds to fade the sprite in / out.")]
        [SerializeField, Min(0f)] private float fadeDuration = 0.35f;

        [Header("Motion")]
        [Tooltip("Gentle vertical bob amplitude (metres).")]
        [SerializeField, Min(0f)] private float bobAmplitude = 0.03f;
        [Tooltip("Seconds for one full bob cycle.")]
        [SerializeField, Min(0.01f)] private float bobPeriod = 2f;

        [Header("Beckon")]
        [Tooltip("Side-to-side roll (degrees) layered on the billboard so the hand reads as waving " +
                 "\"come here\" rather than just floating. Waves around the prompt's own authored " +
                 "orientation. 0 = no roll.")]
        [SerializeField, Min(0f)] private float wiggleAngle = 7f;
        [Tooltip("Scale-pulse amplitude as a fraction of the authored scale (0.05 = ±5%), a gentle " +
                 "breathing beckon.")]
        [SerializeField, Range(0f, 0.5f)] private float scalePulse = 0.05f;
        [Tooltip("Seconds for one full beckon (roll + pulse) cycle.")]
        [SerializeField, Min(0.01f)] private float beckonPeriod = 1.4f;

        private Transform m_tf;
        private Canvas m_canvas;
        private CanvasGroup m_group;
        private Vector3 m_baseScale = Vector3.one;          // authored localScale; the pulse multiplies it
        private Transform m_image;                          // the sprite child we roll (billboarding is the parent's job)
        private Quaternion m_imageBaseRot = Quaternion.identity; // its authored resting rotation; the roll rides on this
        private Vector3 m_anchor;   // ground point the prompt floats above
        private bool m_armed;
        private float m_alpha;      // current eased alpha (0..1)
        private bool m_ready;

        private static Transform Head => Camera.main != null ? Camera.main.transform : null;

        void Awake()
        {
            m_tf = transform;
            m_group = GetComponent<CanvasGroup>();
            m_canvas = GetComponent<Canvas>();

            // The authored scale is the resting size; the pulse multiplies it. Facing the viewer is left
            // entirely to the LookAtTarget on this object — we never touch this object's rotation. The
            // beckon roll goes on the sprite child instead, so it rides under the billboard, not against it.
            m_baseScale = m_tf.localScale;
            m_image = transform.childCount > 0 ? transform.GetChild(0) : null;
            if (m_image != null) m_imageBaseRot = m_image.localRotation;

            // Never let the prompt eat a poke meant for the plant, and start hidden.
            m_group.interactable = false;
            m_group.blocksRaycasts = false;
            m_group.alpha = 0f;
            if (m_canvas != null) m_canvas.enabled = false; // hidden without disabling THIS driver
            m_ready = true;
        }

        // ── Driven by Plant ──────────────────────────────────────────────────────────

        /// <summary>Make the prompt eligible to appear: it fades in whenever the viewer is within
        /// range. <paramref name="groundPoint"/> is the plant's ground centre (see Plant.GroundCenter).</summary>
        public void Arm(Vector3 groundPoint)
        {
            m_anchor = groundPoint;
            m_armed = true;
        }

        /// <summary>Stop inviting touch: the prompt fades out and stays hidden until re-armed.</summary>
        public void Disarm() => m_armed = false;

        void OnDisable()
        {
            m_armed = false;
            m_alpha = 0f;
            if (m_group != null) m_group.alpha = 0f;
            if (m_canvas != null) m_canvas.enabled = false;
        }

        void LateUpdate()
        {
            if (!m_ready) return;

            Transform head = Head;

            // Target visibility: armed + head within range (hysteresis: easier to keep than to start).
            bool want = false;
            Vector3 floatPos = m_anchor + Vector3.up * height;
            if (m_armed && head != null)
            {
                float threshold = m_alpha > 0.001f ? showDistance + hideMargin : showDistance;
                want = Vector3.Distance(head.position, floatPos) <= threshold;
            }

            float dur = Mathf.Max(fadeDuration, 0.0001f);
            m_alpha = Mathf.MoveTowards(m_alpha, want ? 1f : 0f, Time.deltaTime / dur);

            // Hide by switching off the Canvas (stops rendering) while keeping this object — and so
            // this driver — active. Skip the transform work entirely while hidden.
            bool visible = m_alpha > 0.001f;
            if (m_canvas != null && m_canvas.enabled != visible) m_canvas.enabled = visible;
            if (!visible) return;

            // Float above the plant with a gentle bob.
            float bob = bobAmplitude * Mathf.Sin(Time.time / Mathf.Max(bobPeriod, 0.0001f) * Mathf.PI * 2f);
            m_tf.position = floatPos + Vector3.up * bob;

            // Beckon: a gentle scale-pulse + side-to-side roll so the hand reads as inviting, not just
            // floating. The roll uses cos against the pulse's sin (a quarter-cycle apart) so it waves
            // rather than throbbing in lockstep. The pulse rides on the authored localScale.
            float beckon = Time.time / Mathf.Max(beckonPeriod, 0.0001f) * Mathf.PI * 2f;
            m_tf.localScale = m_baseScale * (1f + scalePulse * Mathf.Sin(beckon));

            // Beckon roll: a gentle side-to-side wave on the sprite child's OWN local Z. It's parented to
            // the billboarded canvas, so it inherits the viewer-facing for free — no camera maths here,
            // and because it's a different transform than the one LookAtTarget rotates, it can't feed back.
            if (m_image != null)
                m_image.localRotation = m_imageBaseRot * Quaternion.Euler(0f, 0f, wiggleAngle * Mathf.Cos(beckon));

            m_group.alpha = m_alpha;
        }
    }
}

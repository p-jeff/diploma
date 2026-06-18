using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace Plants
{
    /// <summary>
    /// The finale "take a seat" interaction. A glowing marker on the floor (placed during scene
    /// setup over the user's real chair — see <see cref="SceneLockController"/>) invites the user
    /// to sit. When the user's head descends into the seated-head volume above the marker, the
    /// garden flourishes (<see cref="ExperienceManager.Sit"/>) and the experience switches into
    /// post-flourish gaze-explore mode. A short while after sitting, a restart button is revealed
    /// that soft-resets the whole experience back to the title sequence.
    ///
    /// Detection is a per-frame head-containment test (matching the manager's proximity reveal),
    /// not a physics trigger — the camera rig carries no collider/rigidbody, so a containment test
    /// is both simpler and more robust. The seated-head volume is a box defined in THIS transform's
    /// local space (so it travels with the chair when the grab handle moves it during setup).
    /// </summary>
    public class ChairSit : MonoBehaviour
    {
        [Header("Head")]
        [Tooltip("Head / centre-eye transform tested against the seated volume. Falls back to Camera.main if unset.")]
        [SerializeField] private Transform head;

        [Header("Seated-head volume (local space)")]
        [Tooltip("Centre of the seated-head detection box, in this object's local space. Lift it to " +
                 "roughly seated head height above the marker (e.g. ~1.1 m for an adult on a chair).")]
        [SerializeField] private Vector3 zoneCenter = new Vector3(0f, 1.1f, 0f);
        [Tooltip("Size of the seated-head detection box (local space). Generous on the horizontal so " +
                 "the sit registers wherever the head settles.")]
        [SerializeField] private Vector3 zoneSize = new Vector3(0.6f, 0.6f, 0.6f);

        [Header("Visuals")]
        [Tooltip("The glowing ground marker that invites the sit. Hidden once the user sits down (optional).")]
        [SerializeField] private GameObject groundMarker;
        [Tooltip("Hide the ground marker the moment the user sits (it has served its purpose).")]
        [SerializeField] private bool hideMarkerOnSit = true;
        [Tooltip("Give the ground marker a gentle breathing scale-pulse while waiting to be used (shader-agnostic).")]
        [SerializeField] private bool pulseMarker = true;
        [Tooltip("Pulse amplitude as a fraction of the marker's base scale (0.06 = ±6%).")]
        [SerializeField, Range(0f, 0.5f)] private float pulseAmount = 0.06f;
        [Tooltip("Pulse speed (cycles per second × 2π).")]
        [SerializeField, Min(0f)] private float pulseSpeed = 1.5f;

        [Header("Restart")]
        [Tooltip("Restart button revealed a short while after sitting. Poking it restarts the whole " +
                 "experience back to the title sequence. Held disabled until the delay elapses.")]
        [SerializeField] private GameObject restartButton;
        [Tooltip("Seconds to wait after sitting before the restart button appears.")]
        [SerializeField, Min(0f)] private float restartButtonDelay = 10f;
        [Tooltip("Title sequence to replay on restart (its Replay() resets the experience and plays " +
                 "the intro again). Auto-found in the scene if unset.")]
        [SerializeField] private TitleSequenceController titleSequence;

        [Header("Events")]
        [SerializeField] private UnityEvent onSit;

        private bool m_satDown;
        private Vector3 m_markerBaseScale = Vector3.one;
        private Coroutine m_restartRoutine;

        void Awake()
        {
            if (groundMarker != null) m_markerBaseScale = groundMarker.transform.localScale;
            if (restartButton != null) restartButton.SetActive(false);
            if (titleSequence == null)
#if UNITY_2023_1_OR_NEWER
                titleSequence = FindFirstObjectByType<TitleSequenceController>(FindObjectsInactive.Include);
#else
                titleSequence = FindObjectOfType<TitleSequenceController>(true);
#endif
        }

        /// <summary>Head transform: serialized field if set, else Camera.main.</summary>
        private Transform GetHead()
        {
            if (head != null) return head;
            var cam = Camera.main;
            return cam != null ? cam.transform : null;
        }

        void Update()
        {
            if (!Application.isPlaying) return;

            if (!m_satDown)
            {
                PulseMarker();

                // Only arm during the free-explore phase: the garden is revealed and not yet
                // flourished. This keeps a sit from firing during scene calibration or while the
                // (replayed) title sequence is still running.
                var em = ExperienceManager.Instance;
                if (em == null || !em.CanSit) return;

                Transform h = GetHead();
                if (h == null) return;

                // Head inside the seated-head box (in local space, so it tracks the placed chair)?
                Vector3 local = transform.InverseTransformPoint(h.position);
                if (new Bounds(zoneCenter, zoneSize).Contains(local))
                    SitDown();
            }
        }

        private void PulseMarker()
        {
            if (!pulseMarker || groundMarker == null) return;
            float s = 1f + Mathf.Sin(Time.time * pulseSpeed) * pulseAmount;
            groundMarker.transform.localScale = m_markerBaseScale * s;
        }

        /// <summary>The user took a seat: bloom the garden and arm the restart button.</summary>
        public void SitDown()
        {
            if (m_satDown) return;
            m_satDown = true;

            if (hideMarkerOnSit && groundMarker != null)
            {
                groundMarker.transform.localScale = m_markerBaseScale; // undo any pulse offset
                groundMarker.SetActive(false);
            }

            if (ExperienceManager.Instance != null) ExperienceManager.Instance.Sit();
            onSit.Invoke();

            if (restartButton != null) restartButton.SetActive(false);
            if (m_restartRoutine != null) StopCoroutine(m_restartRoutine);
            m_restartRoutine = StartCoroutine(ShowRestartAfterDelay());
        }

        private IEnumerator ShowRestartAfterDelay()
        {
            if (restartButtonDelay > 0f) yield return new WaitForSeconds(restartButtonDelay);
            if (restartButton != null) restartButton.SetActive(true);
            m_restartRoutine = null;
        }

        /// <summary>
        /// Wired to the restart button's poke event: reset the chair (re-arm sit detection, hide the
        /// button, restore the marker) and replay the title sequence (which soft-resets the garden).
        /// </summary>
        public void RestartExperience()
        {
            ResetForReplay();
            if (titleSequence != null) titleSequence.Replay();
        }

        /// <summary>Re-arm the chair for another run: clear the sat flag, hide the restart button,
        /// and restore the ground marker. Called by <see cref="RestartExperience"/> (and safe to call
        /// from the title sequence directly).</summary>
        public void ResetForReplay()
        {
            if (m_restartRoutine != null) { StopCoroutine(m_restartRoutine); m_restartRoutine = null; }
            m_satDown = false;
            if (restartButton != null) restartButton.SetActive(false);
            if (groundMarker != null)
            {
                groundMarker.transform.localScale = m_markerBaseScale;
                groundMarker.SetActive(true);
            }
        }

        // ── Editor ────────────────────────────────────────────────────────────────────

        void OnDrawGizmosSelected()
        {
            // Visualise the seated-head box so it can be lined up over a real chair.
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.4f, 0.85f, 1f, 0.35f);
            Gizmos.DrawWireCube(zoneCenter, zoneSize);
        }

        [ContextMenu("Debug Sit Down")]
        private void DebugSitDown() => SitDown();
    }
}

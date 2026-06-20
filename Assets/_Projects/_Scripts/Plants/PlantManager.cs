using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Plants
{
    /// <summary>
    /// Orchestrates the plant experience as a sequence of rounds (iterations).
    ///
    /// A round activates a set of 2-4 plants (a tutorial round may be just 1).
    /// The user touches plants to select/hear them (via <see cref="PlantTouchTrigger"/>),
    /// and performs the "like" gesture to like the selected plant. Liking disables
    /// the other plants in the round, activates the liked plant's manually-placed
    /// copies (<see cref="Plant"/> likedInstances), and advances to the next round.
    ///
    /// Setup:
    ///  1. Place one PlantManager in the scene.
    ///  2. Fill the Rounds list: each round references the (disabled) Plant objects
    ///     for that iteration.
    ///  3. Wire the like gesture's SelectorUnityEventWrapper._whenSelected → LikeSelected().
    /// </summary>
    public class PlantManager : MonoBehaviour
    {
        [Serializable]
        public class PlantRound
        {
            public List<Plant> plants = new List<Plant>();
        }

        [Header("Rounds")]
        [Tooltip("Each round activates its set of plants. Configure 1+ rounds; a single plant makes a tutorial round.")]
        [SerializeField] private List<PlantRound> rounds = new List<PlantRound>();

        [Tooltip("Disable every plant referenced by any round at Start, before opening round 0.")]
        [SerializeField] private bool autoDisableAllOnStart = true;

        [Header("Like Gesture")]
        [Tooltip("GameObject(s) holding the 'like' pose selectors. Fully disabled (so their animations stop too) until the selected plant's show animation finishes plus the delay below; then enabled so the user can like.")]
        [SerializeField] private List<GameObject> likeSelectors = new List<GameObject>();

        [Tooltip("Seconds after the selected plant's show animation finishes before the like gesture becomes available.")]
        [SerializeField, Min(0f)] private float likeEnableDelay = 3f;

        [Header("Events")]
        [Tooltip("Fired once after the final round has been liked and there are no more rounds.")]
        [SerializeField] private UnityEvent onAllRoundsComplete;

        public static PlantManager Instance { get; private set; }
        
        [SerializeField] private bool _likeOnStart = false; 
        
        private int m_roundIndex = -1;
        private Plant m_selected;
        private Coroutine m_enableLikeRoutine;

        public int CurrentRound => m_roundIndex;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning($"[PlantManager] Multiple instances; '{name}' overriding existing.", this);
            }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Start()
        {
            // Spectator: plants are passive shells driven by the host's replicated state — don't run
            // the round flow (which would toggle plants active/inactive and fight the snapshot).
            if (Plants.Net.SpectatorState.IsSpectator) return;

            if (autoDisableAllOnStart) DisableAllPlants();
            BeginRound(0);
            if (_likeOnStart) EnableLike();
        }

        // ── Round flow ───────────────────────────────────────────────────────────

        /// <summary>Deactivate the prior round's plants and activate the given round.</summary>
        public void BeginRound(int index)
        {
            // Hide and deactivate whatever was active before.
            if (IsValidRound(m_roundIndex))
            {
                foreach (var p in rounds[m_roundIndex].plants)
                    DeactivatePlant(p);
            }

            m_selected = null;
            DisableLikeAndCancelTimer();
            m_roundIndex = index;

            if (!IsValidRound(index))
            {
                // Past the end → session complete.
                onAllRoundsComplete?.Invoke();
                return;
            }

            foreach (var p in rounds[index].plants)
                ActivatePlant(p);
        }

        /// <summary>Select a plant (called by PlantTouchTrigger). Hides the previously selected plant.</summary>
        public void Select(Plant plant)
        {
            if (plant == null) return;
            if (plant == m_selected) return;
            if (plant.IsLiked) return;
            if (!InCurrentRound(plant)) return;

            if (m_selected != null) m_selected.Hide();
            plant.Show();
            m_selected = plant;

            // Re-gate the like: keep the selectors disabled until this plant's
            // show animation finishes, then wait likeEnableDelay before enabling.
            DisableLikeAndCancelTimer();
            m_enableLikeRoutine = StartCoroutine(EnableLikeAfterAnimation(plant));
        }

        /// <summary>Wire to the "Like" gesture. Likes the selected plant, disables the round's other plants, then advances.</summary>
        public void LikeSelected()
        {
            if (m_selected == null) return;

            var liked = m_selected;
            liked.Like();

            // Liking consumes the gesture for this round; turn the selectors off
            // until the next plant is selected.
            DisableLikeAndCancelTimer();

            if (IsValidRound(m_roundIndex))
            {
                foreach (var p in rounds[m_roundIndex].plants)
                {
                    if (p == null || p == liked) continue;
                    p.Hide();
                    p.gameObject.SetActive(false);
                }
            }

            m_selected = null;
            AdvanceRound();
        }

        /// <summary>Wire to the context ("IndexUp") gesture. Fades in the currently
        /// selected plant's context labels. No-op if nothing is selected.</summary>
        public void ShowContextSelected()
        {
            if (m_selected == null) return;
            m_selected.ShowContext();
        }

        /// <summary>Move to the next round, or fire completion if none remain.</summary>
        public void AdvanceRound()
        {
            // BeginRound deactivates the prior round's plants, but DeactivatePlant
            // skips liked plants — so the just-liked plant (and its spread copies)
            // stays in the scene while we open the next round.
            BeginRound(m_roundIndex + 1);
        }

        /// <summary>
        /// Return the whole experience to its initial state: un-like and restore
        /// every plant across all rounds, then re-run the Start sequence (disable
        /// all, open round 0). Wire this to a UI button or debug input to replay.
        /// Named ResetExperience (not Reset) to avoid Unity's editor Reset callback,
        /// which would otherwise fire this in edit mode and start coroutines.
        /// </summary>
        [ContextMenu("Reset Experience")]
        public void ResetExperience()
        {
            // Un-like and visually restore every plant (BeginRound skips liked
            // plants, so they must be cleared explicitly here).
            foreach (var round in rounds)
            {
                if (round?.plants == null) continue;
                foreach (var p in round.plants)
                    if (p != null) p.ResetState();
            }

            m_selected = null;
            m_roundIndex = -1; // so BeginRound(0) doesn't try to deactivate a stale round

            if (autoDisableAllOnStart) DisableAllPlants();
            BeginRound(0);
        }

        // ── Like gating ────────────────────────────────────────────────────────────

        /// <summary>Test helper: immediately enable the like selectors, bypassing the
        /// post-animation delay. Cancels any pending enable timer.</summary>
        [ContextMenu("Enable Like")]
        public void EnableLike()
        {
            if (m_enableLikeRoutine != null)
            {
                StopCoroutine(m_enableLikeRoutine);
                m_enableLikeRoutine = null;
            }
            SetLikeSelectorsActive(true);
        }

        /// <summary>Test helper: immediately disable the like selectors and cancel any
        /// pending enable timer.</summary>
        [ContextMenu("Disable Like")]
        public void DisableLike() => DisableLikeAndCancelTimer();

        /// <summary>Waits for the plant's show animation to finish, then likeEnableDelay,
        /// then enables the like pose-selector GameObjects.</summary>
        private IEnumerator EnableLikeAfterAnimation(Plant plant)
        {
            yield return new WaitUntil(() => plant == null || plant.ShowAnimationDone);
            if (likeEnableDelay > 0f) yield return new WaitForSeconds(likeEnableDelay);
            SetLikeSelectorsActive(true);
            m_enableLikeRoutine = null;
        }

        private void DisableLikeAndCancelTimer()
        {
            if (m_enableLikeRoutine != null)
            {
                StopCoroutine(m_enableLikeRoutine);
                m_enableLikeRoutine = null;
            }
            SetLikeSelectorsActive(false);
        }

        private void SetLikeSelectorsActive(bool active)
        {
            foreach (var go in likeSelectors)
                if (go != null) go.SetActive(active);
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private void DisableAllPlants()
        {
            foreach (var round in rounds)
            {
                if (round?.plants == null) continue;
                foreach (var p in round.plants)
                    if (p != null) p.gameObject.SetActive(false);
            }
        }

        private static void ActivatePlant(Plant p)
        {
            if (p == null || p.IsLiked) return;
            p.gameObject.SetActive(true);
        }

        private static void DeactivatePlant(Plant p)
        {
            if (p == null || p.IsLiked) return;
            p.Hide();
            p.gameObject.SetActive(false);
        }

        private bool InCurrentRound(Plant plant)
        {
            if (!IsValidRound(m_roundIndex)) return false;
            return rounds[m_roundIndex].plants.Contains(plant);
        }

        private bool IsValidRound(int index) => index >= 0 && index < rounds.Count;
    }
}

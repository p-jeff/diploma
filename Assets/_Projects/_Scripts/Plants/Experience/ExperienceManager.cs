using System;
using System.Collections;
using System.Collections.Generic;
using Gsplat;
using Oculus.Interaction;
using UnityEngine;
using UnityEngine.Events;

namespace Plants
{
    /// <summary>
    /// Scene-singleton director for the species-unlock experience layer.
    /// Works alongside PlantManager (which stays untouched) — PlantTouchTrigger
    /// routes to ExperienceManager first when it's present.
    /// </summary>
    public class ExperienceManager : MonoBehaviour
    {
        // ── Nested types ────────────────────────────────────────────────────────

        [Serializable]
        public class UnlockBatch
        {
            public List<Plant> plants = new List<Plant>();
        }

        // ── Serialized fields ────────────────────────────────────────────────────

        [Header("Unlock Batches")]
        [Tooltip("The garden roster, grouped into unlock batches. Batch 0 is always active at start; " +
                 "each subsequent batch unlocks on a like. This list IS the roster — the flourish and " +
                 "the story distribution walk it in order (batch 0 first). For the vertical slice, put " +
                 "the start heroes in batch 0 and the rest in batch 1, set Flourish After Likes = 1, and " +
                 "turn on Bloom Whole Roster On Flourish.")]
        [SerializeField] private List<UnlockBatch> unlockBatches = new List<UnlockBatch>();

        [Header("Flourish")]
        [Tooltip("Number of likes needed to trigger the garden flourish. Set to 1 for the vertical " +
                 "slice (the first like bursts the whole garden into bloom).")]
        [SerializeField] private int flourishAfterLikes = 8;
        [Tooltip("OFF (staged garden): the flourish blooms only the plants the user kept, and hides " +
                 "the rest. ON (vertical slice): the flourish blooms the WHOLE roster — every plant in " +
                 "the batches, activating any not yet unlocked so they rise from the ground.")]
        [SerializeField] private bool bloomWholeRosterOnFlourish = false;
        [Tooltip("Additional instances spawned per liked species during flourish.")]
        [SerializeField] private int flourishInstancesPerSpecies = 4;
        [Tooltip("Seconds between each liked species flourishing.")]
        [SerializeField] private float flourishSpeciesStagger = 1f;

        [Header("Head")]
        [Tooltip("Head/centre-eye transform used for proximity reveal. Falls back to Camera.main if unset.")]
        [SerializeField] private Transform head;

        [Header("UI")]
        [SerializeField] private TouchPrompt touchPrompt;

        [Header("Like Gesture")]
        [Tooltip("SelectorUnityEventWrapper objects that trigger LikeSelected. Enabled/disabled by the manager.")]
        [SerializeField] private List<SelectorUnityEventWrapper> likeGestureWrappers = new List<SelectorUnityEventWrapper>();
        [Tooltip("GameObjects containing like selectors (SetActive gating).")]
        [SerializeField] private List<GameObject> likeSelectorObjects = new List<GameObject>();

        [Header("Context Gesture")]
        [Tooltip("SelectorUnityEventWrapper objects that trigger the context reveal (grows the nearest ungrown preview).")]
        [SerializeField] private List<SelectorUnityEventWrapper> contextGestureWrappers = new List<SelectorUnityEventWrapper>();
        [Tooltip("GameObjects containing context selectors (SetActive gating).")]
        [SerializeField] private List<GameObject> contextSelectorObjects = new List<GameObject>();

        [Header("Timing")]
        [SerializeField, Min(0f)] private float likeEnableDelay = 0f;

        [Header("Proximity Reveal")]
        [Tooltip("Horizontal distance (m) from the head at which an ungrown context instance auto-reveals when you step close. 0 = off (gesture-only).")]
        [SerializeField, Min(0f)] private float revealRadius = 0.9f;

        [Header("Post-Flourish Gaze Explore")]
        [Tooltip("Raycaster used post-flourish to find the splat instance you're looking at " +
                 "(replaces the old gaze cone). Auto-added if unset.")]
        [SerializeField] private GazeInstanceTargeter gazeTargeter;
        [Tooltip("Brightness multiplier applied to the single splat instance under your gaze " +
                 "post-flourish (1 = no change). Restored when the gaze leaves it.")]
        [SerializeField, Min(1f)] private float gazeHighlightMultiplier = 1.6f;

        [Header("Audio (spatial SFX, played from the plant)")]
        [Tooltip("Reveal-animation sound. Plays on select (the hero reveal) and per instance during the flourish — emitted in 3D from the plant's own spatial AudioSource.")]
        [SerializeField] private AudioClip revealSfx;
        [Tooltip("Context sound. Plays when a context label grows in — emitted in 3D from the plant's spatial AudioSource.")]
        [SerializeField] private AudioClip contextSfx;
        [Tooltip("Like sound. Plays when a species is liked — emitted in 3D from the plant's spatial AudioSource.")]
        [SerializeField] private AudioClip likedSfx;
        [Tooltip("Volume multiplier (0..1) for the spatial SFX. Lower this if SFX are too loud; distance falloff itself is the plant AudioSource's own 3D rolloff.")]
        [SerializeField, Range(0f, 1f)] private float sfxVolume = 0.7f;

        [Header("Events")]
        [SerializeField] private UnityEvent onSpeciesSelected;
        [SerializeField] private UnityEvent onSpeciesLiked;
        [SerializeField] private UnityEvent onSpeciesCompleted;
        [SerializeField] private UnityEvent onGardenFlourish;

        [Header("Debug")]
        [Tooltip("Which round the 'Debug Jump To Round' / '+ Flourish' helpers fast-forward to: " +
                 "the number of plants to auto-like. Each like unlocks the next batch, exactly as in " +
                 "normal play. Capped at the total number of plants and at flourishAfterLikes.")]
        [SerializeField, Min(1)] private int debugJumpRound = 4;

        // ── Singleton ────────────────────────────────────────────────────────────

        public static ExperienceManager Instance { get; private set; }

        void Awake()
        {
            if (Instance != null && Instance != this)
                Debug.LogWarning($"[ExperienceManager] Multiple instances; '{name}' overriding existing.", this);
            Instance = this;

            // Auto-add EnvironmentMoment if not present (required for 180° environments).
            if (GetComponent<EnvironmentMoment>() == null)
                gameObject.AddComponent<EnvironmentMoment>();

            // Auto-add the gaze raycaster used by the post-flourish explore (gaze + context gesture).
            if (gazeTargeter == null) gazeTargeter = GetComponent<GazeInstanceTargeter>();
            if (gazeTargeter == null) gazeTargeter = gameObject.AddComponent<GazeInstanceTargeter>();
            gazeTargeter.SetHead(head);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ── Private state ─────────────────────────────────────────────────────────

        private Plant m_selected;
        private int m_likedCount;
        private int m_unlockedBatches;
        private bool m_flourished;
        private Plant m_exploringPlant;

        // Post-flourish gaze hover-highlight state.
        private GameObject m_gazeInstance;                 // splat instance currently highlighted
        private Plant m_gazePlant;                         // owning plant of the gazed instance
        private readonly List<GsplatRenderer> m_gazeRenderers = new List<GsplatRenderer>();
        private readonly List<float> m_gazeBrightnessOrig = new List<float>();
        private ContextFruit m_gazeFruit;                  // canopy orb under gaze (no GsplatRenderer)

        private Coroutine m_enableSelectorsRoutine;

        // ── Lifecycle ─────────────────────────────────────────────────────────────

        void Start()
        {
            // Deactivate every plant in all batches, then activate only batch 0.
            foreach (var batch in unlockBatches)
            {
                if (batch?.plants == null) continue;
                foreach (var p in batch.plants)
                    if (p != null) p.gameObject.SetActive(false);
            }

            // Batch 0 is the starting set (the heroes in the vertical slice); the rest stay hidden
            // until unlocked (staged garden) or until the whole-roster flourish reveals them.
            m_unlockedBatches = 0;
            ActivateBatch(0);
            m_unlockedBatches = 1;

            // Show touch prompt above first plant of batch 0.
            if (touchPrompt != null && unlockBatches.Count > 0 && unlockBatches[0]?.plants?.Count > 0)
            {
                var firstPlant = unlockBatches[0].plants[0];
                if (firstPlant != null) touchPrompt.Show(firstPlant.transform);
            }

            // Wire gesture wrappers.
            foreach (var w in likeGestureWrappers)
                if (w != null) w.WhenSelected.AddListener(LikeSelected);

            // Context gesture grows the nearest ungrown preview of the selected plant
            // (proximity / stepping close is the primary reveal; this is the manual
            // fallback — no gaze targeting).
            foreach (var w in contextGestureWrappers)
                if (w != null) w.WhenSelected.AddListener(GrowNearestContext);

            // Both selector lists start disabled.
            SetSelectorsActive(false);
        }

        /// <summary>Head/centre-eye transform: serialized field if set, else Camera.main.</summary>
        private Transform GetHead()
        {
            if (head != null) return head;
            var cam = Camera.main;
            return cam != null ? cam.transform : null;
        }

        // ── Per-frame proximity reveal ──────────────────────────────────────────

        void Update()
        {
            if (!Application.isPlaying) return;

            // Post-flourish the experience is in explore mode: hover-highlight the splat instance
            // the user is gazing at; the context gesture then asks that plant to speak again.
            if (m_flourished)
            {
                UpdateGazeHighlight();
                return;
            }

            if (m_selected == null || revealRadius <= 0f) return;

            var ungrown = m_selected.GetUngrownInstances();
            if (ungrown == null || ungrown.Count == 0) return;

            Transform h = GetHead();
            if (h == null) return;

            // Grow any ungrown instance the user has physically stepped close to
            // (horizontal distance from the head). This is the primary "ask" — no
            // gesture needed for the first reveal.
            Vector3 hp = h.position;
            float r2 = revealRadius * revealRadius;
            for (int i = 0; i < ungrown.Count; i++)
            {
                var go = ungrown[i];
                if (go == null) continue;
                Vector3 d = go.transform.position - hp;
                d.y = 0f;
                if (d.sqrMagnitude <= r2)
                    GrowInstanceWithContext(m_selected, go);
            }
        }

        // ── Public API ────────────────────────────────────────────────────────────

        /// <summary>
        /// Called by PlantTouchTrigger. Guards: null, inactive, already liked,
        /// already selected, or flourish done.
        /// </summary>
        public void Select(Plant p)
        {
            if (p == null) return;
            if (!p.gameObject.activeInHierarchy) return;
            if (p.IsLiked) return;
            if (p == m_selected) return;
            if (m_flourished) return;

            if (touchPrompt != null) touchPrompt.Hide();

            // Interrupt any environment moment.
            var moment = GetMoment();
            if (moment != null) moment.Interrupt();

            // Hide the currently selected (non-liked) plant — drops its grey previews.
            // Liked plants are never hidden here: they stay in the world and explorable.
            if (m_selected != null && !m_selected.IsLiked)
                m_selected.Hide();

            p.Show();

            // Trigger 180° environment if the plant has one.
            if (p.Data != null && p.Data.environmentPainting != null && moment != null)
            {
                Transform h = GetHead();
                Vector3 center = h != null ? h.position : p.transform.position;
                Vector3 forward = h != null ? Vector3.ProjectOnPlane(h.forward, Vector3.up).normalized : Vector3.forward;
                moment.Trigger(p.Data.environmentPainting, center, forward, p.AudioSource);
            }

            m_selected = p;
            p.PlaySfx(revealSfx, sfxVolume);   // hero reveal animation is kicking off in Show()
            onSpeciesSelected.Invoke();

            // Progression is like-driven now: selecting a plant never unlocks a batch.
            // The next batch is revealed when the user *likes* a plant (see LikeSelected).

            // Re-gate selectors: disable, wait for animation + delay, then enable.
            DisableSelectorsAndCancelTimer();
            m_enableSelectorsRoutine = StartCoroutine(EnableSelectorsAfterAnimation(p));
        }

        /// <summary>Look up the scene's EnvironmentMoment (on this object or a child).</summary>
        private EnvironmentMoment GetMoment()
        {
            var m = GetComponent<EnvironmentMoment>();
            if (m == null) m = GetComponentInChildren<EnvironmentMoment>();
            return m;
        }

        /// <summary>
        /// Grow one ungrown preview instance of <paramref name="plant"/>: play its reveal, float
        /// its context label (the plant's own <see cref="PlantData.contextInfos"/> by index), and
        /// fire the 180° environment moment if that context has a painting. Shared by the proximity
        /// reveal and the context gesture. Each plant tells its own self-contained, ordered story —
        /// there is no global narrative.
        /// </summary>
        private void GrowInstanceWithContext(Plant plant, GameObject go)
        {
            if (plant == null || go == null) return;

            if (!plant.GrowInstance(go)) return;

            plant.PlaySfx(contextSfx, sfxVolume);   // a context label is growing in

            // 180° painting for this plant's own context (by spawn index).
            int idx = plant.IndexOfSpawned(go);
            var data = plant.Data;
            Texture2D tex = (data != null && idx >= 0 && idx < data.contextInfos.Count)
                ? data.contextInfos[idx].environmentPainting : null;
            if (tex == null) return;

            var moment = GetMoment();
            if (moment == null) return;

            Transform h = GetHead();
            Vector3 center = h != null ? h.position : go.transform.position;
            Vector3 forward = h != null ? Vector3.ProjectOnPlane(h.forward, Vector3.up).normalized : Vector3.forward;
            moment.Trigger(tex, center, forward, plant.AudioSource);
        }

        /// <summary>
        /// Context gesture (no gaze): grow the ungrown preview of the selected plant that
        /// is nearest the head. Proximity (stepping close) is the primary reveal; this is
        /// the manual fallback to summon the closest one without walking right up to it.
        /// </summary>
        public void GrowNearestContext()
        {
            // After the garden has flourished, this gesture switches role: it explores the
            // finished garden (asks the plant you're GAZING at to speak) instead of growing previews.
            if (m_flourished) { ExploreGazed(); return; }
            if (m_selected == null) return;

            var ungrown = m_selected.GetUngrownInstances();
            if (ungrown == null || ungrown.Count == 0) return;

            Transform h = GetHead();
            Vector3 hp = h != null ? h.position : Vector3.zero;

            GameObject nearest = null;
            float best = float.MaxValue;
            foreach (var go in ungrown)
            {
                if (go == null) continue;
                float dsq = (go.transform.position - hp).sqrMagnitude;
                if (dsq < best) { best = dsq; nearest = go; }
            }

            if (nearest != null) GrowInstanceWithContext(m_selected, nearest);
        }

        // ── Explore (post-flourish, gaze-driven) ──────────────────────────────────────

        /// <summary>
        /// After the garden has flourished, the context gesture asks the splat instance the user is
        /// GAZING at to reveal its OWN single context: a ray from the centre-eye hits one instance's
        /// mesh collider, and that instance's owning liked plant shows the one context bound to that
        /// instance (not the whole plant's context group). Gaze a different instance to read a
        /// different context (no 180° environment).
        /// </summary>
        public void ExploreGazed()
        {
            Plant target = null;
            GameObject instance = null;
            if (gazeTargeter != null && gazeTargeter.TryGetTarget(out var p, out var go))
            {
                target = p;
                instance = go;
            }
            // If the on-demand ray missed this exact frame, fall back to the last hover target.
            if (target == null) { target = m_gazePlant; instance = m_gazeInstance; }

            if (target == null || !target.IsLiked) return;

            // Per-instance: hand off cleanly when switching plants, then reveal the gazed instance's
            // single context. Re-gesturing on a new instance of the same plant swaps the context.
            if (m_exploringPlant != null && m_exploringPlant != target)
                m_exploringPlant.EndReplay();

            m_exploringPlant = target;
            target.Replay(instance);
        }

        // ── Post-flourish gaze hover-highlight ────────────────────────────────────────

        /// <summary>
        /// Per-frame (post-flourish): raycast from the centre-eye and brighten the single splat
        /// instance under the gaze, restoring the previously highlighted one. The brightness lever
        /// is <see cref="GsplatRenderer.Brightness"/>, which the renderer re-applies every frame.
        /// </summary>
        private void UpdateGazeHighlight()
        {
            GameObject instance = null;
            Plant plant = null;
            if (gazeTargeter != null) gazeTargeter.TryGetTarget(out plant, out instance);

            m_gazePlant = plant;

            // Yellow "you can ask for context" hand cue — shown only while the gaze is on a plant.
            // Calls are idempotent (Show/Hide no-op when already in that state), so per-frame is fine.
            if (HandReadyCue.Instance != null)
            {
                if (plant != null) HandReadyCue.Instance.ShowContext();
                else HandReadyCue.Instance.Hide();
            }

            if (instance == m_gazeInstance) return;   // highlighted instance unchanged this frame

            ClearGazeHighlight();
            if (instance == null) return;

            var renderers = instance.GetComponentsInChildren<GsplatRenderer>(true);
            foreach (var r in renderers)
            {
                if (r == null) continue;
                m_gazeRenderers.Add(r);
                m_gazeBrightnessOrig.Add(r.Brightness);
                r.Brightness *= gazeHighlightMultiplier;
            }

            // Canopy fruit orbs carry no GsplatRenderer; boost the orb's own glow instead so a
            // gazed orb gives the same "this is the one" feedback as a brightened splat instance.
            m_gazeFruit = instance.GetComponent<ContextFruit>();
            if (m_gazeFruit != null) m_gazeFruit.SetHover(true);

            m_gazeInstance = instance;
        }

        /// <summary>Restore the brightness of the currently highlighted instance (if any).</summary>
        private void ClearGazeHighlight()
        {
            for (int i = 0; i < m_gazeRenderers.Count; i++)
                if (m_gazeRenderers[i] != null) m_gazeRenderers[i].Brightness = m_gazeBrightnessOrig[i];
            m_gazeRenderers.Clear();
            m_gazeBrightnessOrig.Clear();

            if (m_gazeFruit != null) { m_gazeFruit.SetHover(false); m_gazeFruit = null; }

            m_gazeInstance = null;
        }

        /// <summary>
        /// Commit the currently selected plant as liked. If the like threshold is
        /// reached, triggers the garden flourish.
        /// </summary>
        public void LikeSelected()
        {
            if (m_selected == null) return;
            if (m_selected.IsLiked) return;
            if (m_flourished) return;

            // Keep is being used: drop the hand cue immediately.
            HandReadyCue.Instance?.Hide();

            m_selected.LikeCommit();
            m_selected.PlaySfx(likedSfx, sfxVolume);   // emitted from the liked plant
            onSpeciesLiked.Invoke();

            // Interrupt any environment moment.
            var moment = GetMoment();
            if (moment != null) moment.Interrupt();

            // The liked plant stays in the world and stays explorable; we just release
            // it as the active hero. No pending completion — liked plants keep all
            // their instances.
            m_selected = null;

            // Retire the like gesture for this plant, but keep the context gesture
            // available so the user can still grow remaining previews.
            if (m_enableSelectorsRoutine != null)
            {
                StopCoroutine(m_enableSelectorsRoutine);
                m_enableSelectorsRoutine = null;
            }
            SetLikeSelectorsActive(false);
            SetContextSelectorsActive(true);

            m_likedCount++;

            // Like-driven progression: committing to a plant reveals the next batch. The flourish
            // ends the experience, so it takes priority over a normal unlock. In the vertical slice
            // flourishAfterLikes = 1, so the first like flourishes (and Bloom Whole Roster On Flourish
            // bursts the whole garden into bloom — see StartFlourish/FlourishRoutine).
            if (m_likedCount >= flourishAfterLikes)
                StartFlourish();
            else
                UnlockNextBatch();
        }

        // ── Batch unlocking ───────────────────────────────────────────────────────

        private void UnlockNextBatch()
        {
            if (m_unlockedBatches >= unlockBatches.Count) return;
            ActivateBatch(m_unlockedBatches);
            m_unlockedBatches++;
        }

        private void ActivateBatch(int index)
        {
            if (index < 0 || index >= unlockBatches.Count) return;
            var batch = unlockBatches[index];
            if (batch?.plants == null) return;
            foreach (var p in batch.plants)
                if (p != null) p.gameObject.SetActive(true);
        }

        // ── Flourish ──────────────────────────────────────────────────────────────

        private void StartFlourish()
        {
            m_flourished = true;

            // Staged garden only: hide the active, un-liked plants so the finale shows just the
            // kept ones. When blooming the whole roster (vertical slice) we KEEP them — they bloom.
            if (!bloomWholeRosterOnFlourish)
            {
                for (int b = 0; b < m_unlockedBatches && b < unlockBatches.Count; b++)
                {
                    var batch = unlockBatches[b];
                    if (batch?.plants == null) continue;
                    foreach (var p in batch.plants)
                    {
                        if (p == null || p.IsLiked) continue;
                        if (p.gameObject.activeSelf)
                        {
                            p.Hide();
                            p.gameObject.SetActive(false);
                        }
                    }
                }
            }

            if (touchPrompt != null) touchPrompt.Hide();

            StartCoroutine(FlourishRoutine());
        }

        private IEnumerator FlourishRoutine()
        {
            // Roster = the whole garden (every plant in the batches, in batch order) when blooming
            // the whole roster (vertical slice); otherwise just the plants the user kept.
            List<Plant> flourishing = bloomWholeRosterOnFlourish ? AllRosterPlants() : LikedPlants();

            foreach (var p in flourishing)
            {
                if (p == null) continue;

                if (bloomWholeRosterOnFlourish)
                {
                    // The whole roster blooms: activate any not-yet-unlocked species so they rise
                    // from the ground, then bloom (BloomForGarden handles kept vs un-selected).
                    if (!p.gameObject.activeSelf) p.gameObject.SetActive(true);
                    p.BloomForGarden(flourishInstancesPerSpecies, revealSfx, sfxVolume);
                }
                else
                {
                    p.Flourish(flourishInstancesPerSpecies, revealSfx, sfxVolume);
                }

                if (flourishSpeciesStagger > 0f)
                    yield return new WaitForSeconds(flourishSpeciesStagger);
            }

            onGardenFlourish.Invoke();
        }

        /// <summary>Every plant across all unlock batches, in batch order, deduped — the garden
        /// roster, used by the whole-roster flourish and the story distribution.</summary>
        private List<Plant> AllRosterPlants()
        {
            var roster = new List<Plant>();
            var seen = new HashSet<Plant>();
            foreach (var batch in unlockBatches)
            {
                if (batch?.plants == null) continue;
                foreach (var p in batch.plants)
                    if (p != null && seen.Add(p)) roster.Add(p);
            }
            return roster;
        }

        /// <summary>The plants the user has liked, in batch order.</summary>
        private List<Plant> LikedPlants()
        {
            var liked = new List<Plant>();
            foreach (var batch in unlockBatches)
            {
                if (batch?.plants == null) continue;
                foreach (var p in batch.plants)
                    if (p != null && p.IsLiked) liked.Add(p);
            }
            return liked;
        }

        // ── Selector gating ───────────────────────────────────────────────────────

        private IEnumerator EnableSelectorsAfterAnimation(Plant plant)
        {
            yield return new WaitUntil(() => plant == null || plant.ShowAnimationDone);
            if (likeEnableDelay > 0f) yield return new WaitForSeconds(likeEnableDelay);

            // Context can be explored as soon as the reveal animation is done.
            SetContextSelectorsActive(true);

            // Like is gated until the poem audio has finished, so you can never like a
            // plant out from under its own poem (that used to cancel the batch unlock
            // and strand the user on a droning plant).
            if (plant != null && plant.AudioSource != null && plant.AudioSource.isPlaying)
                yield return new WaitWhile(() => plant != null && plant.AudioSource != null && plant.AudioSource.isPlaying);

            SetLikeSelectorsActive(true);
            // Keep is now available: cue both hands so the user knows the gesture is live.
            HandReadyCue.Instance?.Show();
            m_enableSelectorsRoutine = null;
        }

        private void DisableSelectorsAndCancelTimer()
        {
            if (m_enableSelectorsRoutine != null)
            {
                StopCoroutine(m_enableSelectorsRoutine);
                m_enableSelectorsRoutine = null;
            }
            SetSelectorsActive(false);
            // Keep is no longer available (new selection / re-gate / deselect): drop the hand cue.
            HandReadyCue.Instance?.Hide();
        }

        private void SetSelectorsActive(bool active)
        {
            SetLikeSelectorsActive(active);
            SetContextSelectorsActive(active);
        }

        private void SetLikeSelectorsActive(bool active)
        {
            foreach (var go in likeSelectorObjects)
                if (go != null) go.SetActive(active);
        }

        private void SetContextSelectorsActive(bool active)
        {
            foreach (var go in contextSelectorObjects)
                if (go != null) go.SetActive(active);
        }

        // SFX are emitted from each plant's own spatial AudioSource (see Plant.PlaySfx);
        // the poem VO is the shared 2D source referenced by the plant (see Plant.audioSource).

        // ── Debug / ContextMenu helpers ───────────────────────────────────────────

        /// <summary>First active, un-liked, unselected plant across all batches (or null).</summary>
        private Plant FirstSelectable()
        {
            foreach (var batch in unlockBatches)
            {
                if (batch?.plants == null) continue;
                foreach (var p in batch.plants)
                {
                    if (p == null) continue;
                    if (!p.gameObject.activeInHierarchy) continue;
                    if (p.IsLiked) continue;
                    if (p == m_selected) continue;
                    return p;
                }
            }
            return null;
        }

        /// <summary>Select the first active, un-liked, unselected plant (editor/headset-free testing).</summary>
        [ContextMenu("Debug Select Next")]
        public void DebugSelectNext()
        {
            var p = FirstSelectable();
            if (p != null) Select(p);
        }

        /// <summary>
        /// Fast-forward progression by committing likes on successive selectable plants until
        /// <paramref name="targetLikes"/> have been liked (or nothing else is selectable). Drives the
        /// real <see cref="LikeSelected"/> path, so each like unlocks the next batch exactly as in
        /// normal play. The touch reveal is skipped, so liked source bodies stay in their dormant
        /// look — the flourish's spawned instances are the populated payoff. Returns likes committed.
        /// </summary>
        private int DebugLikeUpTo(int targetLikes)
        {
            int committed = 0;
            int safety = 0;
            while (m_likedCount < targetLikes && !m_flourished && safety++ < 128)
            {
                Plant p = FirstSelectable();
                if (p == null) break;

                // Drive the real like path: counts the like, retires/keeps the right selectors,
                // and unlocks the next batch (or flourishes once flourishAfterLikes is hit).
                m_selected = p;
                LikeSelected();
                committed++;
            }
            return committed;
        }

        /// <summary>Grow the first ungrown instance of the selected plant directly (bypasses proximity).</summary>
        [ContextMenu("Debug Grow")]
        public void DebugGrow()
        {
            if (m_selected == null) return;
            var ungrown = m_selected.GetUngrownInstances();
            if (ungrown == null || ungrown.Count == 0) return;
            GrowInstanceWithContext(m_selected, ungrown[0]);
        }

        /// <summary>Like the currently selected plant.</summary>
        [ContextMenu("Debug Like")]
        public void DebugLike() => LikeSelected();

        /// <summary>Clamp the configured debug round to something sane: at least 1, never more than
        /// the like threshold or the total plant count (so we don't loop past what exists).</summary>
        private int DebugRoundTarget()
        {
            int total = 0;
            foreach (var batch in unlockBatches)
                if (batch?.plants != null) total += batch.plants.Count;
            int target = Mathf.Max(1, debugJumpRound);
            if (flourishAfterLikes > 0) target = Mathf.Min(target, flourishAfterLikes);
            if (total > 0) target = Mathf.Min(target, total);
            return target;
        }

        /// <summary>Fast-forward to the configured debug round by auto-liking plants — leaves you
        /// mid-experience at that round (no flourish), with the next batch unlocked, ready to keep testing.</summary>
        [ContextMenu("Debug Jump To Round")]
        public void DebugJumpToRound()
        {
            if (m_flourished) return;
            DebugLikeUpTo(DebugRoundTarget());
        }

        /// <summary>Fast-forward to the configured debug round, THEN trigger the garden flourish so it
        /// always has liked plants to show. (The bare 'Force Flourish' below does nothing from a cold
        /// start because there are no liked species to flourish.)</summary>
        [ContextMenu("Debug Jump To Round + Flourish")]
        public void DebugJumpAndFlourish()
        {
            if (m_flourished) return;
            DebugLikeUpTo(DebugRoundTarget());
            if (!m_flourished) StartFlourish();
        }

        /// <summary>Force the garden flourish immediately. If nothing has been liked yet, auto-likes up
        /// to the debug round first so the flourish actually shows a populated garden.</summary>
        [ContextMenu("Debug Force Flourish")]
        public void DebugForceFlourish()
        {
            if (m_flourished) return;
            if (m_likedCount == 0) DebugLikeUpTo(DebugRoundTarget());
            if (!m_flourished) StartFlourish();
        }

        /// <summary>Explore the gazed liked plant (post-flourish) without the gesture.</summary>
        [ContextMenu("Debug Explore Gazed")]
        public void DebugExploreGazed() => ExploreGazed();
    }
}

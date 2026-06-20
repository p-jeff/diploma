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
                 "the story distribution walk it in order (batch 0 first). Liking progressively unlocks " +
                 "the next batch as the user explores; the flourish itself is now triggered by SITTING " +
                 "in the chair (see Sit()), not by a like count. Turn on Bloom Whole Roster On Flourish " +
                 "so sitting bursts the WHOLE roster into bloom.")]
        [SerializeField] private List<UnlockBatch> unlockBatches = new List<UnlockBatch>();

        [Header("Flourish")]
        [Tooltip("OFF (staged garden): the flourish blooms only the plants the user kept, and hides " +
                 "the rest. ON (vertical slice): the flourish blooms the WHOLE roster — every plant in " +
                 "the batches, activating any not yet unlocked so they rise from the ground.")]
        [SerializeField] private bool bloomWholeRosterOnFlourish = false;
        [Tooltip("Additional instances spawned per liked species during flourish. Fewer = fewer splat " +
                 "instances to depth-sort each frame (a major GPU cost in the bloomed garden).")]
        [SerializeField] private int flourishInstancesPerSpecies = 2;
        [Tooltip("Seconds between each liked species flourishing.")]
        [SerializeField] private float flourishSpeciesStagger = 1f;
        [Tooltip("Garden-wide cap on how many heavy gsplat reveal-builds may BEGIN per frame. Each " +
                 "newly-revealed instance does an O(n) morph-buffer build (CPU + GPU uploads) its " +
                 "first active frame; without a cap, overlapping flourish cascades pile many into one " +
                 "frame — the flourish stutter. 1 = smoothest; raise only if reveals feel too slow to " +
                 "populate the garden.")]
        [SerializeField, Min(1)] private int revealBuildsPerFrame = 1;
        [Tooltip("GPU sort throttle: sort each gsplat's gaussians once every N frames instead of every " +
                 "frame — the flourished garden's single biggest GPU cost (~72%). A >10° head turn still " +
                 "forces a re-sort (GsplatSettings.CameraRotationRefreshTreshold) so it stays correct " +
                 "while looking around. 0 = off (every frame); 3 is a good start.")]
        [SerializeField, Min(0)] private int gsplatSortRefreshRate = 3;

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
                 "normal play. Capped at the total number of plants.")]
        [SerializeField, Min(1)] private int debugJumpRound = 4;

        // ── Singleton ────────────────────────────────────────────────────────────

        public static ExperienceManager Instance { get; private set; }

        void Awake()
        {
            if (Instance != null && Instance != this)
                Debug.LogWarning($"[ExperienceManager] Multiple instances; '{name}' overriding existing.", this);
            Instance = this;

            // Garden-wide reveal-build throttle (anti-flourish-stutter): cap how many heavy gsplat
            // morph builds may begin per frame. Read here so it applies for the whole session.
            RevealBudget.PerFrame = revealBuildsPerFrame;

            // Garden-wide GPU sort throttle (cuts the flourished garden's biggest GPU cost): sort
            // gsplats every N frames instead of every frame, pushed to every renderer in the scene.
            // Runtime scatter clones pick it up in PlantInstanceScatterer.Spawn.
            GsplatSortThrottle.RefreshRate = (uint)Mathf.Max(0, gsplatSortRefreshRate);
            GsplatSortThrottle.ApplyToScene();

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

        void OnValidate()
        {
            if (revealBuildsPerFrame < 1) revealBuildsPerFrame = 1;
            RevealBudget.PerFrame = revealBuildsPerFrame;   // live-tune while playing

            if (gsplatSortRefreshRate < 0) gsplatSortRefreshRate = 0;
            GsplatSortThrottle.RefreshRate = (uint)gsplatSortRefreshRate;
            if (Application.isPlaying) GsplatSortThrottle.ApplyToScene();   // live-tune while playing
        }

        // ── Private state ─────────────────────────────────────────────────────────

        private Plant m_selected;
        private int m_likedCount;
        private int m_unlockedBatches;
        private bool m_flourished;
        private Plant m_exploringPlant;
        private bool m_listenersWired;
        private bool m_gardenOpen;

        /// <summary>True once the garden has bloomed (the user sat down). The post-flourish gaze
        /// explore is active while this is set.</summary>
        public bool IsFlourished => m_flourished;

        /// <summary>True only during the free-explore phase: the garden is open (revealed) and the
        /// user has not yet sat down to flourish. The chair gates its sit detection on this so it
        /// can't fire during calibration or while the title sequence is (re)playing.</summary>
        public bool CanSit => m_gardenOpen && !m_flourished;

        /// <summary>True once the user has committed to (liked) at least one plant this run. Reset to
        /// false by <see cref="BeginGarden"/> / <see cref="ResetAll"/>. The chair gates its
        /// "take a seat" invite on this so the finale isn't offered before the user has engaged.</summary>
        public bool HasLikedAny => m_likedCount > 0;

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
            WireGestureListeners();
            BeginGarden();
        }

        /// <summary>Subscribe the like / context gesture wrappers once. Guarded so a restart
        /// (which never destroys this component) can't double-subscribe.</summary>
        private void WireGestureListeners()
        {
            if (m_listenersWired) return;
            m_listenersWired = true;

            // Wire gesture wrappers.
            foreach (var w in likeGestureWrappers)
                if (w != null) w.WhenSelected.AddListener(LikeSelected);

            // Context gesture grows the nearest ungrown preview of the selected plant
            // (touching a preview is the primary reveal now; this hand-pose gesture is the
            // manual fallback — no gaze targeting).
            foreach (var w in contextGestureWrappers)
                if (w != null) w.WhenSelected.AddListener(GrowNearestContext);
        }

        /// <summary>
        /// Open the garden to its initial state: deactivate every batch, re-activate batch 0
        /// (the starting heroes) dormant, reset progression counters, show the touch prompt and
        /// disable the gesture selectors. Idempotent — safe to call on a cold start AND on a
        /// restart (Unity's Start() only runs once per component lifetime, so the title sequence
        /// calls this explicitly each time it reveals the garden).
        /// </summary>
        public void BeginGarden()
        {
            // Deactivate every plant in all batches, then activate only batch 0.
            foreach (var batch in unlockBatches)
            {
                if (batch?.plants == null) continue;
                foreach (var p in batch.plants)
                    if (p != null) p.gameObject.SetActive(false);
            }

            // Batch 0 is the starting set (the heroes in the vertical slice); the rest stay hidden
            // until unlocked by a like (staged garden) or until the sit-triggered whole-roster
            // flourish reveals them.
            m_selected = null;
            m_likedCount = 0;
            m_flourished = false;
            m_unlockedBatches = 0;
            ActivateBatch(0);
            m_unlockedBatches = 1;

            // Show touch prompt above first plant of batch 0.
            if (touchPrompt != null && unlockBatches.Count > 0 && unlockBatches[0]?.plants?.Count > 0)
            {
                var firstPlant = unlockBatches[0].plants[0];
                if (firstPlant != null) touchPrompt.Show(firstPlant.transform);
            }

            // Both selector lists start disabled.
            SetSelectorsActive(false);

            // The garden is now open for free exploration — the chair's sit detection may arm.
            m_gardenOpen = true;
        }

        /// <summary>Head/centre-eye transform: serialized field if set, else Camera.main.</summary>
        private Transform GetHead()
        {
            if (head != null) return head;
            var cam = Camera.main;
            return cam != null ? cam.transform : null;
        }

        // ── Per-frame update ─────────────────────────────────────────────────────

        void Update()
        {
            if (!Application.isPlaying) return;

            // Post-flourish the experience is in explore mode: hover-highlight the splat instance
            // the user is gazing at; the context gesture then asks that plant to speak again.
            // Pre-flourish there is no per-frame work — a context now grows when the user TOUCHES a
            // spread preview instance (see Touch), not by stepping close to it.
            if (m_flourished)
                UpdateGazeHighlight();
        }

        // ── Public API ────────────────────────────────────────────────────────────

        /// <summary>
        /// Entry point for every hand touch on a plant collider (routed from <see cref="PlantTouchTrigger"/>).
        /// During the regular pre-flourish steps, touching one of the SELECTED plant's spread preview
        /// instances grows THAT instance's context — the primary "ask", replacing the old step-close
        /// proximity reveal. Any other touch (a different plant's hero body, or the selected plant's own
        /// body) routes to <see cref="Select"/> as before. Post-flourish, exploration is gaze-driven, so
        /// hero touches fall through to Select (which no-ops while flourished).
        /// </summary>
        public void Touch(Plant plant, Transform touched)
        {
            if (plant == null) return;

            // Touch a spread preview of the active plant → grow that instance's own context.
            if (!m_flourished && plant == m_selected && !plant.IsLiked && touched != null)
            {
                var instance = plant.FindSpawnedInstance(touched);
                if (instance != null)
                {
                    // Hold context reveals until the poem VO finishes (same gate as the like
                    // gesture): touching a preview while the plant is still narrating does nothing,
                    // so the reading is never cut short.
                    if (PoemPlaying(plant)) return;
                    GrowInstanceWithContext(plant, instance);
                    return;
                }
            }

            // Otherwise it's a hero-body touch: select (or switch to) this plant.
            Select(plant);
        }

        /// <summary>True while <paramref name="p"/>'s poem VO is still narrating. Context reveals are
        /// held until it finishes so the reading isn't cut short (mirrors the like-gesture gate).</summary>
        private static bool PoemPlaying(Plant p) =>
            p != null && p.AudioSource != null && p.AudioSource.isPlaying;

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

            // Trigger 180° environment if the plant has one (parallax layers preferred, single
            // environmentPainting as fallback).
            if (p.Data != null && moment != null)
            {
                var layers = p.Data.environmentLayers;
                bool hasLayers = layers != null && layers.Count > 0;
                if (hasLayers || p.Data.environmentPainting != null)
                {
                    Transform h = GetHead();
                    Vector3 center = h != null ? h.position : p.transform.position;
                    Vector3 forward = h != null ? Vector3.ProjectOnPlane(h.forward, Vector3.up).normalized : Vector3.forward;
                    if (hasLayers) moment.Trigger(layers, center, forward, p.AudioSource);
                    else moment.Trigger(p.Data.environmentPainting, center, forward, p.AudioSource);
                }
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

            // 180° environment for this plant's own context (by interaction order — GrowInstance
            // above already assigned this instance its context index, so this returns the same one).
            // Parallax layers preferred, single environmentPainting as fallback.
            int idx = plant.ContextIndexFor(go);
            var data = plant.Data;
            PlantLabelContent ctx = (data != null && idx >= 0 && idx < data.contextInfos.Count)
                ? data.contextInfos[idx] : null;
            if (ctx == null) return;

            var layers = ctx.environmentLayers;
            bool hasLayers = layers != null && layers.Count > 0;
            if (!hasLayers && ctx.environmentPainting == null) return;

            var moment = GetMoment();
            if (moment == null) return;

            Transform h = GetHead();
            Vector3 center = h != null ? h.position : go.transform.position;
            Vector3 forward = h != null ? Vector3.ProjectOnPlane(h.forward, Vector3.up).normalized : Vector3.forward;
            if (hasLayers) moment.Trigger(layers, center, forward, plant.AudioSource);
            else moment.Trigger(ctx.environmentPainting, center, forward, plant.AudioSource);
        }

        /// <summary>
        /// Context gesture (no gaze): grow the ungrown preview of the selected plant that
        /// is nearest the head. Touching a preview is the primary reveal now; this is the
        /// manual fallback to summon the closest one without walking up to touch it.
        /// </summary>
        public void GrowNearestContext()
        {
            // After the garden has flourished, this gesture switches role: it explores the
            // finished garden (asks the plant you're GAZING at to speak) instead of growing previews.
            if (m_flourished) { ExploreGazed(); return; }
            if (m_selected == null) return;
            if (PoemPlaying(m_selected)) return;   // wait for the poem to finish before growing a context

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

            // Like-driven progression: committing to a plant reveals the next batch as the user
            // explores. There is no longer a "max like" that ends the experience — the garden
            // flourishes only when the user sits down in the chair (see Sit()).
            UnlockNextBatch();
        }

        // ── Sit-to-flourish ─────────────────────────────────────────────────────────

        /// <summary>
        /// The user took a seat in the chair. This is the finale trigger: it bursts the garden
        /// into bloom (the whole roster when Bloom Whole Roster On Flourish is on) and switches the
        /// experience into post-flourish gaze-explore mode. Idempotent — only the first sit blooms.
        /// Wired from <c>ChairSit</c>'s head-enter event.
        /// </summary>
        public void Sit()
        {
            if (m_flourished) return;
            StartFlourish();
        }

        // ── Soft reset (restart) ──────────────────────────────────────────────────────

        /// <summary>
        /// Tear the experience back down to "before the garden" for an in-place restart: stop all
        /// flourish/explore coroutines, drop the post-flourish gaze + replay state, restore every
        /// roster plant to pristine (un-liked, spread destroyed) and deactivate it, and clear all
        /// progression counters. Leaves the garden CLOSED — the title sequence re-opens it via
        /// <see cref="BeginGarden"/> when it finishes replaying. The gesture listeners stay wired
        /// (this component is never destroyed), so nothing is double-subscribed.
        /// </summary>
        public void ResetAll()
        {
            StopAllCoroutines();
            m_enableSelectorsRoutine = null;

            // Drop any post-flourish gaze highlight / in-progress explore replay.
            ClearGazeHighlight();
            if (m_exploringPlant != null) { m_exploringPlant.EndReplay(); m_exploringPlant = null; }
            m_gazePlant = null;

            // Interrupt any live 180° environment moment.
            var moment = GetMoment();
            if (moment != null) moment.Interrupt();

            HandReadyCue.Instance?.Hide();
            if (touchPrompt != null) touchPrompt.Hide();
            SetSelectorsActive(false);

            // Restore every roster plant to pristine, then deactivate it (BeginGarden re-opens batch 0).
            foreach (var p in AllRosterPlants())
            {
                if (p == null) continue;
                p.ResetState();
                p.gameObject.SetActive(false);
            }

            m_selected = null;
            m_likedCount = 0;
            m_unlockedBatches = 0;
            m_flourished = false;
            m_gardenOpen = false;
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
            m_gardenOpen = false;   // explore phase over; the chair won't re-trigger

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
                // and unlocks the next batch.
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
        /// the total plant count (so we don't loop past what exists).</summary>
        private int DebugRoundTarget()
        {
            int total = 0;
            foreach (var batch in unlockBatches)
                if (batch?.plants != null) total += batch.plants.Count;
            int target = Mathf.Max(1, debugJumpRound);
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

        /// <summary>Sit down (trigger the finale flourish) from the inspector — headset-free testing.</summary>
        [ContextMenu("Debug Sit")]
        public void DebugSit() => Sit();

        /// <summary>Soft-reset the experience (as the restart button does) — headset-free testing.</summary>
        [ContextMenu("Debug Reset All")]
        public void DebugResetAll() => ResetAll();
    }
}

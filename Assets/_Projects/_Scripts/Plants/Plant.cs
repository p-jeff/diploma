using System.Collections;
using System.Collections.Generic;
using Gsplat;
using Gsplat.Animation;
using Mirror;
using Plants.Garden;
using Plants.Net;
using UnityEngine;

namespace Plants
{
    /// <summary>How the poem label is placed relative to the plant when shown.</summary>
    public enum PoemPlacement
    {
        /// <summary>Floats straight above the plant's top.</summary>
        Above,
        /// <summary>Orbits a vertical cylinder around the plant's centre, facing the viewer.</summary>
        CylinderAroundCenter,
    }

    /// <summary>How this plant's per-context grow/gaze targets are spawned and placed.</summary>
    public enum ContextPlacementMode
    {
        /// <summary>One scattered splat clone per context; its label floats above that clone.</summary>
        PerInstance,
        /// <summary>Keep ONE hero body and hang each context as a cheap glowing orb ("fruit") in the
        /// canopy. For high-splat plants (trees) where N full splat clones would be too costly.</summary>
        CanopyFruit,
    }

    public class Plant : MonoBehaviour
    {
        [Header("Data")]
        [SerializeField] private PlantData plantData;

        private PlantData ResolveData() => plantData;

        /// <summary>Expose the resolved PlantData (used by EnvironmentMoment hook).</summary>
        public PlantData Data => ResolveData();

        [Header("Labels")]
        [Tooltip("The PlantInfo component on this plant's labels object. Owns the TMP fields and their fade.")]
        [SerializeField] private PlantInfo info;

        [Header("Splats")]
        [Tooltip("All GsplatRevealAnimator components to fire when PlayAnimation() is called. Assign via editor.")]
        [SerializeField] private List<GsplatRevealAnimator> splats = new List<GsplatRevealAnimator>();

        [Header("Audio")]
        [Tooltip("Poem VO source. Point this at the shared 2D 'Poem VO' source so narration plays at the head, not localised to the plant. Show() drives Play().")]
        [SerializeField] private AudioSource audioSource;
        [Tooltip("Spatial (3D) source for this plant's feedback SFX (reveal/context/like) — its own [BuildingBlock] Spatial Audio source.")]
        [SerializeField] private AudioSource sfxSource;

        [Header("Like")]
        [Tooltip("Collider that enables selection of this plant. Disabled when the plant is liked.")]
        [SerializeField] private Collider selectionCollider;

        [Header("Garden Layout")]
        [Tooltip("When active, register this plant's footprint with the GardenPlacer so scattered " +
                 "copies (and other plants) never overlap it. Leave on for the dynamic garden.")]
        [SerializeField] private bool registerFootprint = true;

        [Tooltip("Opt-in: on enable, ask the GardenPlacer to procedurally reposition this whole " +
                 "plant to a free spot in the garden (instead of keeping its authored position). " +
                 "Off by default — drive this from the placement/lock mechanism when ready.")]
        [SerializeField] private bool autoPlaceInGarden = false;

        [Tooltip("Auto-placement only: give the plant a random yaw when repositioned.")]
        [SerializeField] private bool placeRandomYaw = false;

        [Tooltip("Scatters the spread copies across a bounding box when this plant is liked. " +
                 "Preferred over the manual list below; if assigned, likedInstances is ignored.")]
        [SerializeField] private PlantInstanceScatterer scatterer;

        [Tooltip("Fallback for manual placement: pre-placed, disabled copies activated when liked. " +
                 "Only used when no Scatterer is assigned (e.g. quick single-plant tests).")]
        [SerializeField] private List<GameObject> likedInstances = new List<GameObject>();

        [Tooltip("Seconds between each liked instance revealing itself.")]
        [SerializeField, Min(0f)] private float likedStaggerDelay = 0.35f;

        [Tooltip("Clearance (m) above each instance's collider top at which its paired context " +
                 "label floats (Above placement). In cylinder placement it's the height above the " +
                 "instance origin instead (unchanged).")]
        [SerializeField] private float contextHeightOffset = 0.6f;

        [Tooltip("Height above the selection collider bounds top at which the poem canvas floats.")]
        [SerializeField] private float poemHeightOffset = 0.3f;

        [Header("Poem Placement")]
        [Tooltip("Above: the poem floats straight above the plant's top (uses Poem Height Offset). " +
                 "Cylinder Around Center: it orbits a vertical cylinder around the plant's centre, " +
                 "staying on the side facing the viewer — better for tall plants / tree trunks.")]
        [SerializeField] private PoemPlacement poemPlacement = PoemPlacement.Above;

        [Tooltip("Cylinder placement only: horizontal distance (m) from the plant's central axis " +
                 "at which the poem orbits.")]
        [SerializeField, Min(0f)] private float cylinderRadius = 0.5f;

        [Tooltip("Cylinder placement only: height (m) above the plant base (selection-collider bottom) " +
                 "at which the poem orbits.")]
        [SerializeField] private float cylinderHeightOffset = 1.0f;

        [Tooltip("Override the auto-derived poem height (above the collider top for Above, or " +
                 "Cylinder Height Offset above the base for Cylinder) with a fixed manual height. " +
                 "When on, the poem Y is placed at Manual Poem Height above the plant base for both " +
                 "placement modes; XZ centring is unchanged.")]
        [SerializeField] private bool manualPoemHeight = false;

        [Tooltip("Manual poem height (m) above the plant base (selection-collider bottom). " +
                 "Used only when Manual Poem Height is enabled.")]
        [SerializeField] private float manualPoemHeightValue = 1.0f;

        [Header("Context Placement")]
        [Tooltip("Per Instance (default): one scattered splat clone per context, label above it. " +
                 "Canopy Fruit: keep ONE hero body and hang each context as a cheap glowing orb in " +
                 "the canopy — for trees whose splat count makes N full clones too costly.")]
        [SerializeField] private ContextPlacementMode contextMode = ContextPlacementMode.PerInstance;

        [Tooltip("Canopy Fruit only: bottom of the fruit band as a fraction of plant height from the " +
                 "ground (0 = base, 1 = top). Orbs hang between Canopy Bottom and Canopy Top.")]
        [SerializeField, Range(0f, 1f)] private float canopyBottom = 0.5f;

        [Tooltip("Canopy Fruit only: top of the fruit band as a fraction of plant height from the " +
                 "ground. Keep below ~0.9 so orbs sit inside the foliage, not on stray splats above the crown.")]
        [SerializeField, Range(0f, 1f)] private float canopyTop = 0.85f;

        [Tooltip("Canopy Fruit only: horizontal inset (0..0.5) of the canopy footprint so orbs hang " +
                 "inside the foliage rather than at the bounding-box edge.")]
        [SerializeField, Range(0f, 0.5f)] private float canopyInset = 0.15f;

        [Tooltip("Canopy Fruit only: visual radius (m) of each glowing orb.")]
        [SerializeField, Min(0.005f)] private float fruitOrbRadius = 0.09f;

        [Tooltip("Canopy Fruit only: gaze-collider radius (m) per orb. A bit larger than the visual " +
                 "so the gaze snaps onto the tiny orb easily.")]
        [SerializeField, Min(0.01f)] private float fruitColliderRadius = 0.16f;

        [Tooltip("Canopy Fruit only: orb glow colour.")]
        [SerializeField] private Color fruitColor = new Color(1f, 0.78f, 0.32f, 1f);

        // Orb glow intensities (dormant preview vs ripe/revealed). Tuned in code; tweak if needed.
        private const float k_fruitDormantIntensity = 0.22f;
        private const float k_fruitRipeIntensity = 1.4f;

        [Header("Dormant Look")]
        [Tooltip("Desaturation of the unselected plant body (0 = full colour, 1 = grey). 0.5 = half grey / half colour. Cleared to full colour on select.")]
        [SerializeField, Range(0f, 1f)] private float idleDesat = 0.5f;

        [Header("Touch Glow")]
        [Tooltip("Colour of the ground glow that marks this plant as touchable (shown before interaction, faded once touched).")]
        [SerializeField] private Color glowColor = new Color(0.45f, 0.85f, 1f, 1f);
        [Tooltip("Radius (m) of the ground-glow disc.")]
        [SerializeField, Min(0f)] private float glowRadius = 0.6f;
        [Tooltip("Ground-glow disc component. Auto-created if not assigned.")]
        [SerializeField] private HeroGlow glow;

        [Header("Grow-In (Sprout)")]
        [Tooltip("Seconds for a newly unlocked plant to sprout up from the ground into its dormant state.")]
        [SerializeField, Min(0f)] private float sproutDuration = 1.2f;
        [Tooltip("Scale the plant starts at when it sprouts (relative to full), growing up from the ground.")]
        [SerializeField, Range(0.01f, 1f)] private float sproutStartScale = 0.05f;
        [Tooltip("Max random delay before a plant begins its sprout, so a batch of plants stagger organically.")]
        [SerializeField, Min(0f)] private float sproutMaxStartDelay = 0.25f;

        // Copies spawned at runtime by the scatterer when this plant was liked; destroyed on reset.
        private readonly List<GameObject> m_spawnedInstances = new List<GameObject>();

        // Instances that have been individually grown via GrowInstance().
        private readonly HashSet<GameObject> m_grown = new HashSet<GameObject>();


        [Tooltip("Seconds over which un-grown instances fade out when CompleteSpecies() is called.")]
        [SerializeField, Min(0f)] private float instanceFadeOutDuration = 1.5f;

        [Tooltip("Post-flourish explore (Replay): seconds to hold the poem text + grown context " +
                 "labels up for reading before they fade out. No audio plays during explore.")]
        [SerializeField, Min(0.1f)] private float replayHoldDuration = 12f;

        private bool m_liked;
        public bool IsLiked => m_liked;

        /// <summary>This plant's fitted convex mesh collider — used by the garden placer to
        /// measure its footprint and test overlap by true shape. Null on plants without one.</summary>
        public Collider SelectionCollider => selectionCollider;

        /// <summary>True once the show animation (splats + label fade-in) has finished.
        /// Reset whenever the plant is (re)shown, hidden, or liked.</summary>
        public bool ShowAnimationDone { get; private set; }

        /// <summary>The AudioSource used for the poem reading (null if none assigned).</summary>
        public AudioSource AudioSource => audioSource;

        /// <summary>All instances currently spawned by this plant (preview + grown).</summary>
        public IReadOnlyList<GameObject> SpawnedInstances => m_spawnedInstances;

        /// <summary>Returns the index of a spawned instance in the spawned list, or -1 if not found.</summary>
        public int IndexOfSpawned(GameObject instance)
        {
            if (instance == null) return -1;
            for (int i = 0; i < m_spawnedInstances.Count; i++)
                if (m_spawnedInstances[i] == instance) return i;
            return -1;
        }

        private Coroutine m_showRoutine;
        private Coroutine m_sproutRoutine;
        private Coroutine m_replayRoutine;

        // ── Networking (spectator replication) ──────────────────────────────────────
        // On the HOST, runtime-spawned instances (scatter clones, canopy fruit orbs) are tagged with
        // a NetPlant so the spectator client can recreate them. Hero bodies carry an authored NetPlant.

        private NetPlant m_net;

        /// <summary>This plant's network species id (its authored hero NetPlant id), or 0 if none.</summary>
        public ushort SpeciesId
        {
            get
            {
                if (m_net == null) m_net = GetComponent<NetPlant>();
                return m_net != null ? m_net.id : (ushort)0;
            }
        }

        /// <summary>The object the scatterer clones — exposed so the spectator client can clone the
        /// same source to recreate a scatter instance the host spawned.</summary>
        public GameObject ScatterCloneSource => scatterer != null ? scatterer.Source : null;

        /// <summary>Build a dormant canopy fruit orb wired to this plant. Used by both the host's
        /// <see cref="SpawnFruit"/> and the spectator client (which then sets ripe + pose from the
        /// snapshot via NetPlant.Apply).</summary>
        public GameObject BuildFruitOrb()
        {
            var go = new GameObject(name + "_Fruit");
            var fruit = go.AddComponent<ContextFruit>();
            fruit.Init(this, fruitOrbRadius, fruitColliderRadius, fruitColor,
                       k_fruitDormantIntensity, k_fruitRipeIntensity);
            return go;
        }

        /// <summary>Host only: tag a freshly-spawned instance with a NetPlant so it replicates to
        /// spectators. No-op unless a host server is running (so single-player / spectator never tags).</summary>
        private void TagNetInstance(GameObject go, NetKind kind)
        {
            if (go == null || !NetworkServer.active) return;
            var np = go.GetComponent<NetPlant>();
            if (np == null) np = go.AddComponent<NetPlant>();
            np.Configure(NetPlantRegistry.NextDynamicId(), kind, SpeciesId);
        }

        /// <summary>Host only: tag a batch of scatter clones for replication.</summary>
        private void TagScatterClones(List<GameObject> clones)
        {
            if (clones == null || !NetworkServer.active) return;
            foreach (var go in clones)
                TagNetInstance(go, NetKind.ScatterClone);
        }

        // ── Dormant look ───────────────────────────────────────────────────────────
        // While active, unselected and un-liked, the plant body reads desaturated.
        // Renderers are the plant's OWN splats (not scatter copies).
        private readonly List<GsplatRenderer> m_splatRenderers = new List<GsplatRenderer>();
        private bool m_idle;

        static readonly int s_desatId = Shader.PropertyToID("_GsplatDesat");

        void Awake()
        {
            if (info != null)
            {
                info.SetData(ResolveData());
                info.SetAlphaImmediate(0f);
            }

            CacheSplatRenderers();
            AssignAudioClip();

            if (glow == null) glow = GetComponent<HeroGlow>();
            if (glow == null) glow = gameObject.AddComponent<HeroGlow>();
        }

        private void CacheSplatRenderers()
        {
            m_splatRenderers.Clear();
            foreach (var s in splats)
            {
                if (s == null) continue;
                var r = s.GetComponent<GsplatRenderer>();
                if (r != null) m_splatRenderers.Add(r);
            }
        }

        void OnEnable()
        {
            // Spectator: this plant is a passive shell — its pose + reveal are driven entirely by
            // NetPlant.Apply from the host. Don't self-animate (sprout), reserve garden footprints,
            // or show the touch glow, all of which would fight the replicated state.
            if (Application.isPlaying && SpectatorState.IsSpectator) return;

            // A freshly activated (unselected) plant is dormant (desaturated).
            if (!m_liked) m_idle = true;

            // Join the dynamic garden FIRST, at the authored/full pose: either reposition into a
            // free spot, or just reserve the footprint where authored, so nothing scatters on top
            // of this plant. Doing this before the sprout means the reserved footprint is full-size.
            if (Application.isPlaying && selectionCollider != null)
            {
                if (autoPlaceInGarden) PlaceInGarden();
                else if (registerFootprint) RegisterFootprint();
            }

            // A freshly unlocked, un-liked plant sprouts up from the ground into its dormant state
            // (the touch glow is shown when the sprout finishes). In edit mode, or without a
            // selection collider to anchor the ground point, just show the dormant glow at once.
            if (!m_liked)
            {
                if (Application.isPlaying && selectionCollider != null) StartSprout();
                else ShowGlow();
            }
        }

        void OnDisable()
        {
            // Free this plant's footprint when it leaves the garden (deselected/reset).
            // Liked plants stay active, so they remain registered for the session.
            if (GardenPlacer.Instance != null) GardenPlacer.Instance.Remove(this);
        }

        /// <summary>Reserve this plant's footprint at its current pose so copies/other plants
        /// avoid it (no movement).</summary>
        private void RegisterFootprint()
        {
            if (selectionCollider == null) return;
            var placer = GardenPlacer.GetOrCreate();
            placer.Remove(this); // avoid duplicates on re-enable
            Transform t = selectionCollider.transform;
            placer.Register(this, selectionCollider, t.position, t.rotation);
        }

        /// <summary>
        /// Opt-in dynamic placement: ask the GardenPlacer to move this whole plant to a free,
        /// non-overlapping spot inside the garden boundary and register it. Safe to call from a
        /// placement/lock mechanism; no-op without a selection collider.
        /// </summary>
        public void PlaceInGarden()
        {
            if (selectionCollider == null) return;
            if (SpectatorState.IsSpectator) return;   // spectator never runs garden placement
            var placer = GardenPlacer.GetOrCreate();
            placer.Remove(this);
            placer.ApplyAndRegister(transform, selectionCollider, placeRandomYaw, this);
        }

        void Update()
        {
            if (!Application.isPlaying) return;
            if (SpectatorState.IsSpectator) return;   // spectator: look is driven by replicated reveal
            if (!m_idle) return;

            // Dormant plants render desaturated until touched. Static — no sparkle.
            foreach (var r in m_splatRenderers)
            {
                var pb = r != null ? r.PropertyBlock : null;
                if (pb == null) continue; // renderer not initialised yet
                pb.SetFloat(s_desatId, idleDesat);
            }
        }

        /// <summary>Leave the dormant state: restore full colour so the reveal owns the look.</summary>
        private void EndIdle()
        {
            m_idle = false;
            foreach (var r in m_splatRenderers)
            {
                var pb = r != null ? r.PropertyBlock : null;
                if (pb != null) pb.SetFloat(s_desatId, 0f);
            }
        }

        // ── Touch glow ─────────────────────────────────────────────────────────────
        // A soft ground disc marks a plant as touchable. It shows while the plant is
        // dormant/available and fades once the plant is touched (selected) or liked.

        /// <summary>Ground-level centre of the selection-collider footprint (glow anchor).</summary>
        public Vector3 GroundCenter
        {
            get
            {
                if (selectionCollider != null)
                {
                    Bounds b = selectionCollider.bounds;
                    return new Vector3(b.center.x, b.min.y, b.center.z);
                }
                return transform.position;
            }
        }

        /// <summary>Show the touch glow under this plant (invites interaction).</summary>
        private void ShowGlow()
        {
            if (glow != null) glow.Show(GroundCenter, glowColor, glowRadius);
        }

        /// <summary>Fade the touch glow out.</summary>
        private void HideGlow()
        {
            if (glow != null) glow.Hide();
        }

        // ── Grow-in (sprout) ─────────────────────────────────────────────────────────
        // A newly unlocked plant grows up out of the ground into its dormant state instead of
        // popping in. This is a transform + opacity animation layered on the dormant bud: the
        // reveal morph is deliberately LEFT at progress 0 (the half-grey, sparkling resting
        // state), so the touch reveal still plays 0→1 later. Scaling is about the ground point
        // (GroundCenter) so the base stays planted and the plant rises from it.

        static readonly int s_opacityMulId = Shader.PropertyToID("_GsplatOpacityMul");

        private void StartSprout()
        {
            if (m_sproutRoutine != null) StopCoroutine(m_sproutRoutine);
            m_sproutRoutine = StartCoroutine(SproutIn());
        }

        private IEnumerator SproutIn()
        {
            // Stagger a batch organically.
            if (sproutMaxStartDelay > 0f)
                yield return new WaitForSeconds(Random.Range(0f, sproutMaxStartDelay));

            // Keep the reveal at its dormant resting state so the touch reveal stays intact.
            ResetAnimation();

            // No touching a half-sprouted plant; the footprint was already reserved at full pose.
            bool colliderWas = selectionCollider != null && selectionCollider.enabled;
            if (selectionCollider != null) selectionCollider.enabled = false;

            Vector3 origPos = transform.position;
            Vector3 origScale = transform.localScale;
            Vector3 ground = GroundCenter;

            float dur = Mathf.Max(sproutDuration, 0.0001f);
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float e = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / dur));
                float f = Mathf.Lerp(Mathf.Clamp(sproutStartScale, 0.01f, 1f), 1f, e);

                // Scale the whole plant about the ground point so the base stays put and it rises.
                transform.localScale = origScale * f;
                transform.position = ground + (origPos - ground) * f;

                SetSplatOpacity(Mathf.Lerp(0.002f, 1f, e));
                yield return null;
            }

            // Land exactly on the authored pose, fully opaque.
            transform.localScale = origScale;
            transform.position = origPos;
            SetSplatOpacity(1f);

            if (selectionCollider != null) selectionCollider.enabled = colliderWas;

            // Now it's a touchable dormant plant: invite the touch.
            if (!m_liked) ShowGlow();

            m_sproutRoutine = null;
        }

        /// <summary>Set _GsplatOpacityMul on this plant's own splat renderers (not scatter copies).</summary>
        private void SetSplatOpacity(float opacity)
        {
            foreach (var r in m_splatRenderers)
            {
                var pb = r != null ? r.PropertyBlock : null;
                if (pb == null) continue;
                pb.SetFloat(s_opacityMulId, opacity);
            }
        }

        void OnValidate()
        {
            if (Application.isPlaying) return;
            if (info != null) info.SetData(ResolveData());
            AssignAudioClip();
        }

        /// <summary>Pull the clip from PlantData onto the AudioSource so it doesn't have
        /// to be wired per-plant. Show() drives Play().</summary>
        private void AssignAudioClip()
        {
            var data = ResolveData();
            if (audioSource != null && data != null && data.audioClip != null)
                audioSource.clip = data.audioClip;
        }

        /// <summary>Play a feedback SFX from this plant's spatial (3D) source so it is
        /// localised to the plant. <paramref name="volume"/> is a 0..1 multiplier.</summary>
        public void PlaySfx(AudioClip clip, float volume = 1f)
        {
            if (sfxSource == null || clip == null) return;
            // Play() (not PlayOneShot) so (a) the Meta XR Audio spatializer applies HRTF to
            // the source's voice, and (b) repeated SFX REPLACE rather than stack — these
            // clips run several seconds, so stacking was the wall-of-sound loudness culprit.
            sfxSource.clip = clip;
            sfxSource.volume = Mathf.Clamp01(volume);
            sfxSource.Play();
        }

        public void PlayAnimation()
        {
            foreach (var s in splats)
                if (s != null) s.Play();
        }

        public void ResetAnimation()
        {
            foreach (var s in splats)
                if (s != null) s.ResetToStart();
        }

        /// <summary>
        /// Make this plant the visible/active one: plays audio, runs the splat
        /// animation, then fades the labels in. Coordination of which plant is
        /// shown (and hiding the previous one) is owned by <see cref="PlantManager"/>.
        /// </summary>
        public void Show()
        {
            if (m_liked) return;

            EndIdle();   // selected: full colour; the reveal animation owns the look now.
            HideGlow();  // touched → the glow that invited the touch fades away.
            ShowAnimationDone = false;

            // Re-activate the label GO in case it was deactivated by default in the prefab.
            if (info != null) info.gameObject.SetActive(true);

            // Place the poem canvas relative to the selection collider bounds (see PlacePoem).
            PlacePoem();

            if (audioSource != null)
            {
                AssignAudioClip();
                audioSource.Play();
            }

            if (info != null) info.SetData(ResolveData());

            if (m_showRoutine != null) StopCoroutine(m_showRoutine);
            m_showRoutine = StartCoroutine(ShowAfterAnimation());
        }

        /// <summary>
        /// Place the poem canvas relative to the selection collider bounds, matching the configured
        /// placement. Above: centred (XZ) and floated to bounds.max.y + poemHeightOffset. Cylinder:
        /// orbit a vertical cylinder around the bounds centre at bounds.min.y + cylinderHeightOffset.
        /// When <see cref="manualPoemHeight"/> is on, the Y is instead <see cref="manualPoemHeightValue"/>
        /// above the plant base (bounds.min.y) for both modes; XZ centring is unchanged. Also refreshes
        /// the context top-lift. No-op without an info object or selection collider.
        /// </summary>
        private void PlacePoem()
        {
            if (info == null || selectionCollider == null) return;

            // Tell the labels whether contexts hang as canopy fruit (placed just above their orb,
            // no collider top-lift) or float above scattered instances (the default).
            info.SetFruitContext(IsFruitMode);

            // Use a geometry-derived world AABB, not selectionCollider.bounds directly: a liked
            // plant has its collider DISABLED (see LikeCommit), and a disabled collider reports a
            // zero-size bounds at its transform position. Reading that post-flourish (Replay)
            // collapses the poem height and the context top-lift, bunching the labels and making
            // them overlap. SelectionColliderWorldBounds() stays correct regardless of enabled state.
            Bounds b = SelectionColliderWorldBounds();
            if (poemPlacement == PoemPlacement.CylinderAroundCenter)
            {
                float y = manualPoemHeight ? b.min.y + manualPoemHeightValue : b.min.y + cylinderHeightOffset;
                Vector3 axis = new Vector3(b.center.x, y, b.center.z);
                info.PositionPoemCylinder(axis, cylinderRadius);
            }
            else
            {
                float y = manualPoemHeight ? b.min.y + manualPoemHeightValue : b.max.y + poemHeightOffset;
                Vector3 poemPos = new Vector3(b.center.x, y, b.center.z);
                info.PositionPoem(poemPos);
            }

            // How high the collider top sits above this plant's origin. Scattered instances
            // are clones pivoted like the plant root, so the same lift floats each instance's
            // context label above its body (Above placement); cylinder placement ignores it.
            info.SetContextTopLift(b.max.y - transform.position.y);
        }

        /// <summary>
        /// World-space AABB of the selection collider that is correct even when the collider is
        /// DISABLED (e.g. a liked plant post-flourish). A disabled <see cref="Collider"/> reports a
        /// zero-size <c>bounds</c>, so when the live bounds is degenerate we reconstruct the world
        /// AABB from the collider's geometry (MeshCollider sharedMesh / BoxCollider center+size)
        /// transformed by its localToWorldMatrix. Falls back to a tiny box at the collider position.
        /// </summary>
        private Bounds SelectionColliderWorldBounds()
        {
            var c = selectionCollider;
            if (c == null) return new Bounds(transform.position, Vector3.zero);

            Bounds live = c.bounds;
            if (live.size.sqrMagnitude > 1e-6f) return live;   // enabled & valid

            Transform t = c.transform;
            if (c is MeshCollider mc && mc.sharedMesh != null)
                return TransformBounds(t.localToWorldMatrix, mc.sharedMesh.bounds);
            if (c is BoxCollider bc)
                return TransformBounds(t.localToWorldMatrix, new Bounds(bc.center, bc.size));
            return new Bounds(t.position, Vector3.one * 0.1f);
        }

        /// <summary>Transform a local-space bounds by <paramref name="m"/> into a world-space AABB
        /// (expands the box so it still encloses the rotated/scaled geometry).</summary>
        private static Bounds TransformBounds(Matrix4x4 m, Bounds local)
        {
            Vector3 center = m.MultiplyPoint3x4(local.center);
            Vector3 e = local.extents;
            Vector3 ax = m.MultiplyVector(new Vector3(e.x, 0f, 0f));
            Vector3 ay = m.MultiplyVector(new Vector3(0f, e.y, 0f));
            Vector3 az = m.MultiplyVector(new Vector3(0f, 0f, e.z));
            Vector3 worldExtents = new Vector3(
                Mathf.Abs(ax.x) + Mathf.Abs(ay.x) + Mathf.Abs(az.x),
                Mathf.Abs(ax.y) + Mathf.Abs(ay.y) + Mathf.Abs(az.y),
                Mathf.Abs(ax.z) + Mathf.Abs(ay.z) + Mathf.Abs(az.z));
            return new Bounds(center, worldExtents * 2f);
        }

        private IEnumerator ShowAfterAnimation()
        {
            PlayAnimation();

            if (splats.Count > 0)
            {
                // Cap the wait so a splat that never reports IsDone can't stall the whole
                // flow. The reveal animator only completes for UNCOMPRESSED gsplat assets;
                // a compressed/packed placeholder renders fine but never initialises its
                // morph (IsDone stays false forever). Time out at the longest reveal
                // duration + margin and proceed regardless, so the poem still fades in and
                // the previews still spread.
                float timeout = 0f;
                foreach (var s in splats)
                    if (s != null) timeout = Mathf.Max(timeout, s.duration);
                timeout += 1f;

                float elapsed = 0f;
                bool allDone = false;
                while (!allDone && elapsed < timeout)
                {
                    allDone = true;
                    foreach (var s in splats)
                    {
                        if (s != null && !s.IsDone)
                        {
                            allDone = false;
                            break;
                        }
                    }
                    if (!allDone)
                    {
                        elapsed += Time.deltaTime;
                        yield return null;
                    }
                }
            }

            // Re-aim a cylinder-placed poem at the viewer now (it was positioned at Show(),
            // before the reveal), then fade it in so it appears facing them and holds still.
            if (info != null) info.ResnapPoemCylinder();

            if (info != null) info.FadePoem(1f);

            // Spread a grey, visible preview: one instance per context block. They stay
            // greyscale until the context gesture (ShowContext) colourises them.
            SpawnPreviewInstances();

            ShowAnimationDone = true;
            m_showRoutine = null;
        }

        /// <summary>How many preview instances to spread on selection = number of context blocks.</summary>
        private int InstanceCount()
        {
            var data = ResolveData();
            int n = data != null ? data.contextInfos.Count : 0;
            if (n <= 0 && info != null) n = info.ContextCount;
            return n;
        }

        /// <summary>World positions of the currently spawned instances (for spacing the next batch).</summary>
        private List<Vector3> SpawnedPositions()
        {
            var positions = new List<Vector3>(m_spawnedInstances.Count);
            foreach (var go in m_spawnedInstances)
                if (go != null) positions.Add(go.transform.position);
            return positions;
        }

        /// <summary>Transforms of the first <paramref name="count"/> spawned instances, used as
        /// context-label anchors (instance i ↔ context block i).</summary>
        private List<Transform> SpawnedAnchors(int count)
        {
            var anchors = new List<Transform>(count);
            for (int i = 0; i < m_spawnedInstances.Count && anchors.Count < count; i++)
                if (m_spawnedInstances[i] != null) anchors.Add(m_spawnedInstances[i].transform);
            return anchors;
        }

        /// <summary>True when this plant hangs its contexts as canopy fruit (1 hero body + N orbs)
        /// instead of scattering one splat clone per context.</summary>
        private bool IsFruitMode => contextMode == ContextPlacementMode.CanopyFruit;

        /// <summary>Spawn the on-select preview: N instances (N = context blocks), each
        /// activated and parked greyscale (reveal not played yet). In Canopy Fruit mode this
        /// hangs N dormant glowing orbs in the canopy instead of scattering N splat clones.</summary>
        private void SpawnPreviewInstances()
        {
            int n = InstanceCount();
            if (n <= 0) return;

            if (IsFruitMode)
            {
                SpawnFruit(n, ripe: false);
                return;
            }

            if (scatterer == null) return;

            var spawned = scatterer.Spawn(n, SpawnedPositions());
            TagScatterClones(spawned);
            m_spawnedInstances.AddRange(spawned);   // list is correct immediately (copies still inactive)

            // Activate + park-grey the previews over successive frames rather than all at once: each
            // one's first active frame does the heavy gsplat morph build, so waking N together hitches
            // the select. The garden-wide RevealBudget caps builds per frame (same gate the flourish
            // cascade uses), and the copies stay inactive/invisible until their slot — no flash.
            StartCoroutine(ActivatePreviewInstances(spawned));
        }

        /// <summary>Activate the spawned preview copies one global build-slot at a time
        /// (see <see cref="RevealBudget"/>), parking each at its grey resting state. They stay
        /// inactive — invisible — until their slot, so there is no full-detail flash and no select
        /// hitch from many gsplat morph builds landing on one frame.</summary>
        private IEnumerator ActivatePreviewInstances(List<GameObject> spawned)
        {
            foreach (var go in spawned)
            {
                if (go == null) continue;

                while (!RevealBudget.TryConsume())
                    yield return null;

                go.SetActive(true);
                var animators = go.GetComponentsInChildren<GsplatRevealAnimator>(true);
                foreach (var a in animators)
                    if (a != null) a.ResetToStart();
            }
        }

        /// <summary>
        /// Canopy-fruit spawn: hang <paramref name="count"/> cheap glowing orbs (one per context)
        /// in this plant's canopy, each a grow/gaze target carrying one context. The orbs are added
        /// to <see cref="m_spawnedInstances"/> so the existing grow / gaze / replay paths treat them
        /// exactly like scattered instances (index ↔ context). <paramref name="ripe"/> orbs glow
        /// bright immediately (bloom); dormant orbs wait to be grown.
        /// </summary>
        private void SpawnFruit(int count, bool ripe)
        {
            var points = SampleCanopyPoints(count);
            for (int i = 0; i < points.Count; i++)
            {
                var go = BuildFruitOrb();
                go.name = name + "_Fruit_" + i;
                go.transform.SetParent(transform, true); // follows the plant (e.g. scene-lock moves)
                go.transform.position = points[i];

                if (ripe)
                {
                    var fruit = go.GetComponent<ContextFruit>();
                    if (fruit != null) fruit.Ripen();
                }

                TagNetInstance(go, NetKind.FruitOrb);
                m_spawnedInstances.Add(go);
            }
        }

        /// <summary>
        /// Distribute <paramref name="count"/> world points within the top <see cref="canopyTopFraction"/>
        /// of the plant's selection-collider AABB (the canopy), inset horizontally by
        /// <see cref="canopyInset"/>, using Mitchell best-candidate sampling so the orbs spread out
        /// rather than clump.
        /// </summary>
        private List<Vector3> SampleCanopyPoints(int count)
        {
            var result = new List<Vector3>(Mathf.Max(0, count));
            if (count <= 0) return result;

            Bounds b = SelectionColliderWorldBounds();
            float height = Mathf.Max(0.01f, b.size.y);
            // Fruit band = [canopyBottom .. canopyTop] as fractions of height from the base. The top
            // is deliberately kept below the bounds apex: gsplat scans have sparse stray points above
            // the dense crown, so anchoring to b.max.y would float orbs above the visible foliage.
            float f0 = Mathf.Clamp01(Mathf.Min(canopyBottom, canopyTop));
            float f1 = Mathf.Clamp01(Mathf.Max(canopyBottom, canopyTop));
            float yMin = b.min.y + height * f0;
            float yMax = b.min.y + height * f1;

            float ix = Mathf.Max(0f, b.extents.x * (1f - canopyInset * 2f));
            float iz = Mathf.Max(0f, b.extents.z * (1f - canopyInset * 2f));

            const int candidates = 16;
            for (int i = 0; i < count; i++)
            {
                Vector3 best = b.center;
                float bestScore = -1f;
                for (int c = 0; c < candidates; c++)
                {
                    var p = new Vector3(
                        b.center.x + Random.Range(-ix, ix),
                        Random.Range(yMin, yMax),
                        b.center.z + Random.Range(-iz, iz));

                    float nearest = result.Count == 0 ? 1f : float.MaxValue;
                    for (int j = 0; j < result.Count; j++)
                        nearest = Mathf.Min(nearest, (result[j] - p).sqrMagnitude);

                    if (nearest > bestScore) { bestScore = nearest; best = p; }
                }
                result.Add(best);
            }
            return result;
        }

        /// <summary>Colourise already-active grey instances with a staggered reveal cascade
        /// (ripens canopy fruit orbs in the same cascade).</summary>
        private IEnumerator ColorizeInstances(List<GameObject> instances)
        {
            for (int i = 0; i < instances.Count; i++)
            {
                var go = instances[i];
                if (go == null) continue;

                var animators = go.GetComponentsInChildren<GsplatRevealAnimator>(true);
                for (int j = 0; j < animators.Length; j++)
                    if (animators[j] != null) animators[j].Play();

                var fruit = go.GetComponent<ContextFruit>();
                if (fruit != null) fruit.Ripen();

                if (likedStaggerDelay > 0f)
                    yield return new WaitForSeconds(likedStaggerDelay);
            }
        }

        /// <summary>
        /// Fade this plant out and stop its audio/animation. Safe to call on a
        /// plant that isn't currently shown.
        /// </summary>
        public void Hide()
        {
            if (m_liked) return;
            ShowAnimationDone = false;
            if (m_showRoutine != null)
            {
                StopCoroutine(m_showRoutine);
                m_showRoutine = null;
            }
            if (audioSource != null) audioSource.Stop();
            if (sfxSource != null) sfxSource.Stop();   // cut any in-flight (multi-second) SFX on deselect
            if (info != null)
            {
                info.FadePoem(0f);
                info.HideContext();
            }

            // A non-liked plant being hidden drops its grey preview spread.
            DestroySpawnedInstances();

            ResetAnimation();

            // Deselected but still in the round → dormant again; the glow invites re-touch.
            m_idle = true;
            ShowGlow();
        }

        /// <summary>Destroy and clear the runtime-spawned instance copies.</summary>
        private void DestroySpawnedInstances()
        {
            for (int i = 0; i < m_spawnedInstances.Count; i++)
                if (m_spawnedInstances[i] != null) Destroy(m_spawnedInstances[i]);
            m_spawnedInstances.Clear();
            m_grown.Clear();
        }

        /// <summary>
        /// Fade in this plant's context labels. Wired (via <see cref="PlantManager"/>) to
        /// the context "IndexUp" gesture. The poem is shown by <see cref="Show()"/>; the
        /// contexts stay hidden until this is called.
        /// </summary>
        public void ShowContext()
        {
            if (m_liked) return;

            // Colourise the grey preview instances in a cascade...
            StartCoroutine(ColorizeInstances(new List<GameObject>(m_spawnedInstances)));

            // ...and float each context block above its paired instance, facing the user.
            if (info != null)
                info.PlaceContextAt(SpawnedAnchors(InstanceCount()), contextHeightOffset);
        }

        public void Like()
        {
            if (m_liked) return;
            m_liked = true;
            EndIdle(); // liked: colour-locked.
            HideGlow();
            ShowAnimationDone = false;

            if (m_showRoutine != null)
            {
                StopCoroutine(m_showRoutine);
                m_showRoutine = null;
            }

            // Keep the audio playing through the like — don't Stop() it here.

            if (info != null)
            {
                info.SetAlphaImmediate(0f);
                info.gameObject.SetActive(false);
            }

            if (selectionCollider != null) selectionCollider.enabled = false;

            if (IsFruitMode)
            {
                // Canopy fruit: keep the single hero body, just ripen the orbs already hanging
                // in the canopy. No splat scatter (that's the whole point for high-splat trees).
                ForceColorSpawned();
            }
            else if (scatterer != null)
            {
                // The preview spread (one per context block) was already placed on select
                // and may have been colourised by the context gesture. Force-colour any
                // still-grey ones (liked without context)...
                ForceColorSpawned();

                // ...then double the spread with a fresh staggered reveal.
                int extra = InstanceCount();
                if (extra > 0)
                {
                    var more = scatterer.Spawn(extra, SpawnedPositions());
                    TagScatterClones(more);
                    m_spawnedInstances.AddRange(more);
                    StartCoroutine(RevealLikedInstances(more));
                }
            }
            else
            {
                // No scatterer (quick single-plant tests): reveal the manual fallback list.
                StartCoroutine(RevealLikedInstances(likedInstances));
            }
        }

        /// <summary>Lock every spawned instance to its coloured state immediately (and ripen any
        /// canopy fruit orbs).</summary>
        private void ForceColorSpawned()
        {
            foreach (var go in m_spawnedInstances)
            {
                if (go == null) continue;
                var animators = go.GetComponentsInChildren<GsplatRevealAnimator>(true);
                foreach (var a in animators)
                    if (a != null) a.ForceColored();
                var fruit = go.GetComponent<ContextFruit>();
                if (fruit != null) fruit.Ripen();
            }
        }

        /// <summary>
        /// Activates the spread copies one after another, each playing its splat
        /// reveal (greyscale → animated reveal → colour) as it appears.
        /// </summary>
        private IEnumerator RevealLikedInstances(List<GameObject> instances)
        {
            for (int i = 0; i < instances.Count; i++)
            {
                var go = instances[i];
                if (go == null) continue;

                // Garden-wide reveal-build throttle: wait for a slot before waking this instance.
                // Each clone does its heavy gsplat morph build (O(n) CPU + buffer uploads) the first
                // frame it is active, so overlapping flourish cascades waking many clones on one
                // frame are what spiked the frame (~46 ms). The clone stays inactive — invisible, no
                // full-detail flash — until its turn, so only frame pacing changes, not the look.
                while (!RevealBudget.TryConsume())
                    yield return null;

                go.SetActive(true);

                // Park greyscale then play the reveal wave, same frame so there's
                // no colour flash before the animation starts.
                var animators = go.GetComponentsInChildren<GsplatRevealAnimator>(true);
                for (int j = 0; j < animators.Length; j++)
                {
                    if (animators[j] == null) continue;
                    animators[j].ResetToStart();
                    animators[j].Play();
                }

                if (likedStaggerDelay > 0f)
                    yield return new WaitForSeconds(likedStaggerDelay);
            }
        }

        /// <summary>
        /// Restore this plant to its pristine, pre-interaction state: un-like it,
        /// stop audio/coroutines, hide its spread copies, re-enable the label object
        /// and selection collider, and reset the splats to greyscale. Used by
        /// <see cref="PlantManager.ResetExperience"/>.
        /// </summary>
        public void ResetState()
        {
            StopAllCoroutines();
            m_showRoutine = null;
            m_liked = false;

            if (audioSource != null) audioSource.Stop();

            // Destroy the copies the scatterer spawned for the previous like.
            for (int i = 0; i < m_spawnedInstances.Count; i++)
                if (m_spawnedInstances[i] != null) Destroy(m_spawnedInstances[i]);
            m_spawnedInstances.Clear();
            m_grown.Clear();

            // Hide the manually placed fallback copies that a previous like activated.
            for (int i = 0; i < likedInstances.Count; i++)
                if (likedInstances[i] != null) likedInstances[i].SetActive(false);

            if (selectionCollider != null) selectionCollider.enabled = true;

            if (info != null)
            {
                info.gameObject.SetActive(true);
                info.SetData(ResolveData());
                info.SetAlphaImmediate(0f);
            }

            ResetAnimation();

            // Pristine + available again → the dormant glow returns if we're active.
            if (gameObject.activeInHierarchy) ShowGlow();
        }

        // ── Experience layer additions ────────────────────────────────────────────

        /// <summary>
        /// Returns all spawned instances that have NOT yet been grown via GrowInstance,
        /// skipping destroyed/null entries.
        /// </summary>
        public List<GameObject> GetUngrownInstances()
        {
            var result = new List<GameObject>();
            foreach (var go in m_spawnedInstances)
            {
                if (go == null) continue;
                if (!m_grown.Contains(go)) result.Add(go);
            }
            return result;
        }

        /// <summary>
        /// Individually grow one preview instance: play its reveal animators, register it as grown,
        /// and float this plant's own context label for that slot (PlantData.contextInfos by spawn
        /// index) above it. Each plant tells its own self-contained, ordered story.
        /// </summary>
        /// <returns>True if the instance was successfully grown.</returns>
        public bool GrowInstance(GameObject instance)
        {
            if (instance == null) return false;
            if (!m_spawnedInstances.Contains(instance)) return false;
            if (m_grown.Contains(instance)) return false;

            int idx = m_spawnedInstances.IndexOf(instance);

            var animators = instance.GetComponentsInChildren<GsplatRevealAnimator>(true);
            foreach (var a in animators)
                if (a != null) a.Play();

            // Canopy fruit has no splat animator; ripen the orb so growing it lights it up.
            var fruit = instance.GetComponent<ContextFruit>();
            if (fruit != null) fruit.Ripen();

            m_grown.Add(instance);

            if (info != null) info.PlaceContextAt(idx, instance.transform, contextHeightOffset);

            return true;
        }

        /// <summary>
        /// Commit this plant as liked (Experience layer variant). Sets liked state,
        /// stops idle shimmer and any running show coroutine; fades the poem to 0 but
        /// KEEPS the info GameObject active (so grown context labels remain visible);
        /// disables the selection collider. Does NOT force-colour instances or spawn extras.
        /// </summary>
        public void LikeCommit()
        {
            if (m_liked) return;
            m_liked = true;
            EndIdle();
            HideGlow(); // liked: no longer touchable, drop the glow.

            if (m_showRoutine != null)
            {
                StopCoroutine(m_showRoutine);
                m_showRoutine = null;
            }

            // The poem has finished by the time a like is allowed; stop the source
            // defensively so nothing keeps droning if it somehow hasn't.
            if (audioSource != null) audioSource.Stop();

            // Fade poem and context labels out; keep info active so it can be reused
            // (the context gesture can recall the grown labels later).
            if (info != null)
            {
                info.FadePoem(0f);
                info.HideContext();
            }
            // info.gameObject remains active — intentional.

            if (selectionCollider != null) selectionCollider.enabled = false;
        }

        /// <summary>
        /// Fade out and destroy all spawned instances that have NOT been grown, then
        /// remove them from the spawned list.
        /// </summary>
        public void CompleteSpecies()
        {
            // Iterate backwards so removal doesn't disturb indices.
            for (int i = m_spawnedInstances.Count - 1; i >= 0; i--)
            {
                var go = m_spawnedInstances[i];
                if (go == null)
                {
                    m_spawnedInstances.RemoveAt(i);
                    continue;
                }
                if (!m_grown.Contains(go))
                {
                    GsplatInstanceFader.FadeOutAndDestroy(go, instanceFadeOutDuration);
                    m_spawnedInstances.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Spawn <paramref name="count"/> additional instances and reveal them with a
        /// staggered cascade (used during the garden flourish).
        /// </summary>
        public void Flourish(int count, AudioClip revealSfx = null, float sfxVolume = 1f)
        {
            // One spatial reveal SFX per species (not per instance): these clips are several
            // seconds long, so per-instance playback would stack into a wall of sound.
            if (revealSfx != null) PlaySfx(revealSfx, sfxVolume);

            // Colour any leftover grey previews so the finale is fully bloomed (ripens canopy orbs).
            ForceColorSpawned();

            // Canopy fruit: the preview orbs ARE the bloom — keep the single hero body, just ensure
            // they're ripe (done above). No splat scatter for high-splat trees. If this plant was
            // liked WITHOUT a preview spread (e.g. Debug Jump skips select), hang ripe orbs now so
            // the tree still blooms instead of showing nothing.
            if (IsFruitMode)
            {
                if (m_spawnedInstances.Count == 0)
                    SpawnFruit(OwnContextCount(), ripe: true);
                return;
            }

            if (scatterer != null && count > 0)
            {
                var more = scatterer.Spawn(count, SpawnedPositions());
                TagScatterClones(more);
                m_spawnedInstances.AddRange(more);

                // Re-enable each clone's fitted collider so the post-flourish gaze ray can hit it.
                // Flourish clones are spawned AFTER LikeCommit disabled the source plant's collider,
                // so they inherit a disabled collider; the earlier touch previews are unaffected.
                // Harmless if already enabled.
                foreach (var go in more)
                {
                    if (go == null) continue;
                    foreach (var col in go.GetComponentsInChildren<Collider>(true))
                        if (col != null) col.enabled = true;
                }

                StartCoroutine(RevealLikedInstances(more));
            }

            // No context text here — each scattered instance reveals its OWN single context only
            // when the user gazes that instance (ExploreGazed → Replay(instance)).
        }

        /// <summary>
        /// Reveal this plant as part of the whole-garden flourish (presentation slice).
        /// The chosen (already-liked) plant just colourises its previews and thickens its
        /// patch; an un-selected species reveals its hero body (NO poem audio) and scatters
        /// + reveals N instances, becoming explorable post-flourish. Waits for any in-progress
        /// sprout so a freshly activated species reveals at its full pose.
        /// </summary>
        public void BloomForGarden(int instanceCount, AudioClip revealSfx = null, float sfxVolume = 1f)
        {
            if (m_liked)
            {
                // The plant the user kept: thicken the patch (Flourish colours leftover previews).
                Flourish(instanceCount, revealSfx, sfxVolume);
                return;
            }
            StartCoroutine(BloomForGardenRoutine(instanceCount, revealSfx, sfxVolume));
        }

        private IEnumerator BloomForGardenRoutine(int instanceCount, AudioClip revealSfx, float sfxVolume)
        {
            // Let a freshly activated plant finish rising from the ground before it colourises.
            yield return new WaitWhile(() => m_sproutRoutine != null);

            m_liked = true;            // colour-locked + explorable post-flourish
            EndIdle();
            HideGlow();
            ShowAnimationDone = false;
            if (m_showRoutine != null) { StopCoroutine(m_showRoutine); m_showRoutine = null; }

            // Keep the info object available so the post-flourish gaze explore can recall the
            // poem, but do NOT play the poem VO here (many species bloom at once).
            if (info != null)
            {
                info.SetData(ResolveData());
                info.gameObject.SetActive(true);
                info.SetAlphaImmediate(0f);
            }

            // The hero body is not a gaze target post-flourish; the scattered instances are.
            if (selectionCollider != null) selectionCollider.enabled = false;
            if (revealSfx != null) PlaySfx(revealSfx, sfxVolume);

            PlayAnimation();           // reveal the hero body (grey → colour)

            // Canopy fruit: keep the single hero body and hang one ripe orb per context as the
            // gaze targets (one context per orb). No splat scatter for high-splat trees.
            if (IsFruitMode)
            {
                SpawnFruit(OwnContextCount(), ripe: true);
                yield break;
            }

            // Scatter one instance per context (so every context is reachable on gaze — one context
            // per instance), at least `instanceCount` for a visible patch.
            int spawnCount = Mathf.Max(instanceCount, OwnContextCount());
            if (scatterer != null && spawnCount > 0)
            {
                var more = scatterer.Spawn(spawnCount, SpawnedPositions());
                TagScatterClones(more);
                m_spawnedInstances.AddRange(more);

                // Enable each clone's collider so the post-flourish gaze ray can hit it.
                foreach (var go in more)
                {
                    if (go == null) continue;
                    foreach (var col in go.GetComponentsInChildren<Collider>(true))
                        if (col != null) col.enabled = true;
                }

                StartCoroutine(RevealLikedInstances(more));
            }

            // No context text here — each scattered instance reveals its OWN single context only
            // when the user gazes that instance (ExploreGazed → Replay(instance)).
        }

        /// <summary>This plant's own context count (PlantData.contextInfos), capped at the available
        /// label slots on its info — the number of distinct contexts it can show.</summary>
        private int OwnContextCount()
        {
            var data = ResolveData();
            int n = data != null ? data.contextInfos.Count : 0;
            if (info != null) n = Mathf.Min(n, info.ContextCount);
            return Mathf.Max(0, n);
        }

        // ── Explore (post-flourish revisit) ──────────────────────────────────────────
        // After the garden has flourished, the user can revisit a plant they kept: replay its
        // poem and re-float the context labels they grew. Liked plants keep their info object
        // active (see LikeCommit), so the labels can be recalled. No 180° environment here.

        /// <summary>True while a Replay() is narrating (used to avoid restarting it mid-poem).</summary>
        public bool IsReplaying => m_replayRoutine != null;

        /// <summary>
        /// Post-flourish: reveal the ONE context bound to <paramref name="gazedInstance"/> (the splat
        /// instance the user is looking at) above that instance, plus this plant's poem text. A single
        /// context per interaction — gaze a different instance to read a different one — NOT the
        /// plant's whole context group. The context shown is the instance's spawn index modulo this
        /// plant's context count, so every scattered copy maps onto one of the plant's own contexts.
        /// No-op unless the plant is liked.
        /// </summary>
        public void Replay(GameObject gazedInstance = null)
        {
            if (!m_liked || info == null) return;

            info.gameObject.SetActive(true);

            // Place the poem exactly like Show() does, relative to the selection collider.
            PlacePoem();

            // SetData reloads this plant's own sprites AND deactivates every context root, so the
            // previously gazed instance's label is cleared — only the one below stays visible.
            info.SetData(ResolveData());
            info.ResnapPoemCylinder();
            info.FadePoem(1f);   // poem TEXT (sprite) — asking is a silent reading moment

            // Float ONLY the gazed instance's own context above it (spawn index mod context count,
            // so decorative copies map back onto a real context). One context, not the group.
            int n = OwnContextCount();
            int idx = gazedInstance != null ? m_spawnedInstances.IndexOf(gazedInstance) : -1;
            if (n > 0 && idx >= 0)
                info.PlaceContextAt(idx % n, gazedInstance.transform, contextHeightOffset);

            // No audio: asking post-flourish shows the poem text + the one context to read.
            if (audioSource != null) audioSource.Stop();

            if (m_replayRoutine != null) StopCoroutine(m_replayRoutine);
            m_replayRoutine = StartCoroutine(ReplayCleanup());
        }

        private IEnumerator ReplayCleanup()
        {
            // Hold the poem text + grown context labels up long enough to read, then fade them out.
            yield return new WaitForSeconds(Mathf.Max(0.1f, replayHoldDuration));

            if (info != null)
            {
                info.FadePoem(0f);
                info.HideContext();
            }
            m_replayRoutine = null;
        }

        /// <summary>Stop an in-progress Replay and fade its poem/labels — used when the user moves
        /// to revisit a different plant (hands off the shared poem audio source cleanly).</summary>
        public void EndReplay()
        {
            if (m_replayRoutine != null) { StopCoroutine(m_replayRoutine); m_replayRoutine = null; }
            if (audioSource != null) audioSource.Stop();
            if (info != null)
            {
                info.FadePoem(0f);
                info.HideContext();
            }
        }

        [ContextMenu("Test Like")]
        private void TestLike() => Like();

        [ContextMenu("Test Show")]
        private void TestShow() => Show();

        [ContextMenu("Test Show Context")]
        private void TestShowContext() => ShowContext();

        [ContextMenu("Test Hide")]
        private void TestHide() => Hide();

        [ContextMenu("Test Play Animation")]
        private void TestPlayAnimation() => PlayAnimation();
    }
}

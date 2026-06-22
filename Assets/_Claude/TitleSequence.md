# Title Sequence

An intro that plays BEFORE the garden experience. Built 2026-06-18 on branch `midterm` in
`VerticalSlice.unity`, and saved as a reusable prefab `Assets/_Projects/_Prefabs/TitleSequence.prefab`
so it can be dropped into `Experience.unity` later.

## Flow
1. A standalone **poppy** Gaussian splat sits on the floor with a floating **"Touch Me"** label. The
   garden (ExperienceManager + its plants) is held hidden.
2. Touching the poppy → label fades out → poppy fades out → the **3D title card** appears: the `AOTC_v1`
   mesh ("An ode to curiosity") **fades in** (alpha), then the **rose** Gaussian blooms in; holds; both
   clear (mesh fades out + rose dissolves) before the poem. (Was a TMP text card pre-2026-06-21.)
3. **Wünschelrute** (Joseph von Eichendorff) poem text fades in while the VO plays
   (`Assets/_Projects/_Audio/VO/Wünschelrute.wav`); poem holds until the VO ends, then fades out.
4. Garden reveal: the ExperienceManager is enabled (its own `Start()` brings up the first plants + the
   real touch prompt) and the garden Gaussians fade in via `_GsplatOpacityMul` 0.002→1.

## Scripts
- `TitleSequenceController.cs` (`_Scripts/Plants/Experience/`, namespace `Plants`, Assembly-CSharp) —
  the coroutine director. Edits NOTHING in the existing experience; it only *gates* it:
  - **Awake** (runs before any `Start()`): `experienceManager.SetActive(false)` + disables the
    `hideDuringTitle` plants, and subscribes `Begin` to the touch trigger.
  - **Start** (`ArmTitle`): parks the poem alpha + the 3D title card (mesh alpha 0, rose parked hidden) at
    their hidden state, plays the poppy reveal, fades the "Touch Me" label in.
  - **Begin** (touch): runs the sequence above (incl. the 3D title card beat — see below). `RevealGarden`
    enables the manager, waits a frame, then
    opacity-fades every GsplatRenderer under `gardenFadeRoot` (inactive included, so the fade is armed
    before the manager activates the starting batch — no full-opacity flash).
  - ContextMenu: **Debug Begin** (run the sequence) and **Debug Skip To Garden** (jump straight to the
    garden) — for desktop testing without a headset.
- `PlantTouchTrigger.cs` — added one additive accessor `AddTouchListener(UnityAction)`. The title's
  trigger carries NO plant, so the touch only fires our event (the manager routing no-ops on null).

## 3D title card (mesh + rose Gaussian) — added 2026-06-21
The old TMP text card ("An ode to curiosity") was replaced by a 3D mesh + a rose Gaussian, grouped under
`3D_TitleCard` in the prefab. Driven by `ShowTitleCard` / `HideTitleCard` in the controller.
- **Mesh "animates" by ALPHA FADE, not scale** (deliberate — no scale animation). `TitleCardMat`
  (`_Resources/`) was converted from opaque URP/Lit to **Transparent** (`_Surface=1`,
  `_SrcBlend=5`/`_DstBlend=10`, `_ZWrite=0`, `_BlendModePreserveSpecular=0`, keyword
  `_SURFACE_TYPE_TRANSPARENT`, renderQueue 3000 — edited directly in the `.mat` YAML). The controller
  lerps `_BaseColor` alpha 0↔1 via a **MaterialPropertyBlock** per `MeshRenderer` (`SetMeshAlpha` /
  `FadeMesh`), so the RGB (black) is preserved and the shared material asset is never mutated.
  - *ZWrite-off note:* mid-fade the extruded letters show faint internal-overlap darkening; negligible on
    black. Flip `_ZWrite` to 1 on the material if it ever bothers you.
- **Rose reveal**: after the mesh fades in (and a `gapMeshToRose` pause), `titleRoseReveal.Play()` blooms
  the rose while its `_GsplatOpacityMul` fades off the hidden floor; the coroutine **waits for the bloom to
  finish** (`IsDone`) before the `titleHoldDuration` hold, so the rose is fully formed before the poem.
- **Title-card SFX** (added 2026-06-22): `ShowTitleCard` plays an optional `titleSfx` AudioSource the
  moment the card animates in (under the mesh fade + rose bloom). Wired in the prefab to a `TitleCardSfx`
  2D source (`spatialBlend 0`, playOnAwake off, child of the title root so a long clip isn't cut when
  `3D_TitleCard` hides). Currently `SFX_Plant Grow_03.wav`. Leave `titleSfx` unset for a silent card.
- **Controller fields** (all wired IN THE PREFAB — internal refs, travel with it): `titleCardRoot`
  (`3D_TitleCard`), `titleMesh` (`AOTC_v1`), `titleRoseReveal` (the rose `Gaussian`'s animator),
  `titleMeshFadeDuration` (1.1), `gapMeshToRose` (0.35), `titleSfx` (`TitleCardSfx`). `ArmTitle` parks the card hidden: mesh alpha 0 +
  rose `ResetToStart()` + `_GsplatOpacityMul` floored to 0.002. The opacity floor matters because the
  rose's `startAt=0.1` is **not** geometrically hidden — without it the rose would flash before its reveal.
- The old `titleCard` `TMP_Text` field was **fully removed** from the controller.

## Idle cross-dissolve (waiting animation)
While waiting to be touched, the title splat doesn't sit static — it cross-dissolves through a small set
of flowers (poppy → lavender → daffodil → …) so there's gentle life. Built 2026-06-18.
- **`IdleSplatCycler.cs`** (`_Scripts/Plants/Experience/`, ns `Plants`) on `TitlePoppy`. Holds a list of
  flowers (each = a `GsplatRenderer` + its `GsplatRevealAnimator`), cadence `holdDuration=5` then a quick
  `morphDuration=1` cross-dissolve. A transition is just `next.Play()` (assemble in) alongside
  `current.PlayReverse()` (dissolve out) — it **reuses the existing reveal pipeline** (bloom + scatter),
  so NO shader work, NO per-pair init, and a shown flower (settled at progress 1) costs zero per-frame GPU.
- **Why this approach** (not a geometric two-splat morph): cycling N splats with the geometric morph would
  re-pair (O(n·m) NN) per transition = a hitch every cycle. The cross-dissolve has none of that and scales
  to any number of flowers; downside is it's a clean dissolve, not a literal shape-shift (acceptable for an
  idle). Optional `hueDriftDuringMorph` knob (off by default) washes hue across the swap.
- **All flower GOs stay enabled** for the title's life; hidden ones are parked at progress 0 (fully
  transparent because their animators use `startAt=0`). Nothing toggles active → no 1-frame full-detail
  flash. The 2 hidden flowers each hold one pooled morph buffer (trivial). Does NOT reintroduce idle
  dispatch (parked flowers dispatch once then idle — consistent with the dispatch-gating rule).
- **Touch interrupts cleanly**: `TitleSequenceController.onSequenceStarted` → `cycler.StopCycle()` (wired
  as a persistent UnityEvent call in the prefab). The controller's `Begin` then fades **every** renderer
  under `TitlePoppy`, so whichever flower is showing is dismissed — no per-flower handling needed. The
  `TouchZone` collider is independent of the splats, so the user can touch at any point in the cycle.
- **All 3 reveal animators retuned** for a clean dissolve: `startAt=0`, `startGaussianScale=1`,
  `startPositionScale=1`, `startDesaturation=0`, `radial=true`, `scatterAmount=0.03`, `duration=1`. NOTE:
  this changes the poppy's INITIAL reveal too (was 3s grow-from-small-desaturated; now a 1s radial
  bloom+scatter at full scale/colour) — unavoidable, since the cycle needs the poppy's progress-0 state
  fully hidden. Scales are meant to be hand-tuned per flower so they read roughly equal.
- Test flowers: poppy/lavender/daffodil (narcissus). Wired into `TitleSequence.prefab` via unity-mcp.

## Scene structure (`SceneRoot/Content/TitleSequence`)
- `TitlePoppy` (root, pos (0,0,1.0), rot (0,35,0)) — replicates the garden poppy's transform.
  Hosts `IdleSplatCycler` (above).
  - `Gaussian` (rot (180,0,0) = gsplat upright flip): standalone `GsplatRenderer` → `mohnblume.ply`
    (NOT the Poppy prefab) + `GsplatRevealAnimator` (reveal=true, GsplatMorph.compute).
  - `Gaussian_Lavender`, `Gaussian_Daffodil`: clones of `Gaussian` with the lavender/daffodil PLY assets
    (the other two idle-cycle flowers).
  - `TouchZone` (pos (0,0.3,0)): BoxCollider trigger 0.6³ + **kinematic Rigidbody** (required for
    OnTriggerEnter to fire, matching the plant-collider model) + `PlantTouchTrigger` (no plant,
    handTag "Player").
- `3D_TitleCard` (local pos (0,1.5,2)) — the title beat (replaced the old `TitleCard` TMP text 2026-06-21):
  - `Gaussian` — standalone `GsplatRenderer` → `rose.ply` + `GsplatRevealAnimator` (reveal, `startAt`=0.1,
    `duration`=4s). Blooms in after the mesh.
  - `AOTC_v1` — the 3D title mesh "An ode to curiosity" (5 `Curve` sub-meshes, material `TitleCardMat`,
    no Animator). Alpha-fades in/out (see "3D title card" above).
- `TouchMeLabel`, `PoemCard` — world-space Canvas + TextMeshProUGUI. Faded via
  `TMP_Text.alpha`. Each canvas has **`LookAtTarget`** (rotateY, **flipY=false** — derived: flipY=true
  comes out mirrored for these canvases) to yaw-billboard at Camera.main. The controller calls
  `LookAtTarget.Snap()` on each label right before it fades in, so it faces wherever the user is at
  reveal time (LookAtTarget itself only snaps on enable, not per-frame).
- `WuenschelruteVO` — 2D AudioSource (spatialize off), clip = Wünschelrute.wav, playOnAwake off.
- `TitleCardSfx` — 2D AudioSource (spatialBlend 0, playOnAwake off), clip = `SFX_Plant Grow_03.wav`,
  played by `ShowTitleCard` when the title card animates in (wired to the controller's `titleSfx`).

## Reusing in Experience.unity
Drag the prefab under `SceneRoot/Content`. The internal wiring (poppy, labels, VO, controller→those) is
in the prefab. **Scene references can't live in a prefab**, so re-assign on the instance:
`experienceManager` (the Experience Manager GO), `hideDuringTitle` (the garden plant GOs), and
`gardenFadeRoot` (Content). In VerticalSlice these are wired as instance overrides already.

## Verification
The base intro flow (poppy → touch → poem → garden) is **headset-verified**. The **3D title card beat**
(mesh alpha fade + rose bloom) is structurally verified only (compiles clean, `TitleCardMat` transparent,
refs wired in the prefab) and **not yet headset-verified**. Tuning likely needed (all serialized on the
controller): `titleMeshFadeDuration`, `gapMeshToRose`, `titleHoldDuration`, the rose animator's `duration`
(4s — the title beat runs ~mesh-fade + 4s bloom + hold, so shorten it if the beat drags), label
sizes/heights, poppy position, other fade/hold durations.

Related: [[scene-lock-system]] (title starts after lock, since it lives under Content),
[[vertical-slice-scene]], [[ovroverlaycanvas-poem-prototype]] (upgrade path for crisp passthrough text),
[[final-models-wiring]] (the mohnblume poppy splat).

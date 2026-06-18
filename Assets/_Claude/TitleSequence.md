# Title Sequence

An intro that plays BEFORE the garden experience. Built 2026-06-18 on branch `midterm` in
`VerticalSlice.unity`, and saved as a reusable prefab `Assets/_Projects/_Prefabs/TitleSequence.prefab`
so it can be dropped into `Experience.unity` later.

## Flow
1. A standalone **poppy** Gaussian splat sits on the floor with a floating **"Touch Me"** label. The
   garden (ExperienceManager + its plants) is held hidden.
2. Touching the poppy → label fades out → poppy fades out → title card **"An ode to curiosity"** fades
   in / holds / out.
3. **Wünschelrute** (Joseph von Eichendorff) poem text fades in while the VO plays
   (`Assets/_Projects/_Audio/VO/Wünschelrute.wav`); poem holds until the VO ends, then fades out.
4. Garden reveal: the ExperienceManager is enabled (its own `Start()` brings up the first plants + the
   real touch prompt) and the garden Gaussians fade in via `_GsplatOpacityMul` 0.002→1.

## Scripts
- `TitleSequenceController.cs` (`_Scripts/Plants/Experience/`, namespace `Plants`, Assembly-CSharp) —
  the coroutine director. Edits NOTHING in the existing experience; it only *gates* it:
  - **Awake** (runs before any `Start()`): `experienceManager.SetActive(false)` + disables the
    `hideDuringTitle` plants, and subscribes `Begin` to the touch trigger.
  - **Start**: parks title/poem alpha at 0, plays the poppy reveal, fades the "Touch Me" label in.
  - **Begin** (touch): runs the sequence above. `RevealGarden` enables the manager, waits a frame, then
    opacity-fades every GsplatRenderer under `gardenFadeRoot` (inactive included, so the fade is armed
    before the manager activates the starting batch — no full-opacity flash).
  - ContextMenu: **Debug Begin** (run the sequence) and **Debug Skip To Garden** (jump straight to the
    garden) — for desktop testing without a headset.
- `PlantTouchTrigger.cs` — added one additive accessor `AddTouchListener(UnityAction)`. The title's
  trigger carries NO plant, so the touch only fires our event (the manager routing no-ops on null).

## Scene structure (`SceneRoot/Content/TitleSequence`)
- `TitlePoppy` (root, pos (0,0,1.0), rot (0,35,0)) — replicates the garden poppy's transform.
  - `Gaussian` (rot (180,0,0) = gsplat upright flip): standalone `GsplatRenderer` → `mohnblume.ply`
    (NOT the Poppy prefab) + `GsplatRevealAnimator` (reveal=true, duration 3, GsplatMorph.compute).
  - `TouchZone` (pos (0,0.3,0)): BoxCollider trigger 0.6³ + **kinematic Rigidbody** (required for
    OnTriggerEnter to fire, matching the plant-collider model) + `PlantTouchTrigger` (no plant,
    handTag "Player").
- `TouchMeLabel`, `TitleCard`, `PoemCard` — world-space Canvas + TextMeshProUGUI. Faded via
  `TMP_Text.alpha`. Each canvas has **`LookAtTarget`** (rotateY, **flipY=false** — derived: flipY=true
  comes out mirrored for these canvases) to yaw-billboard at Camera.main. The controller calls
  `LookAtTarget.Snap()` on each label right before it fades in, so it faces wherever the user is at
  reveal time (LookAtTarget itself only snaps on enable, not per-frame).
- `WuenschelruteVO` — 2D AudioSource (spatialize off), clip = Wünschelrute.wav, playOnAwake off.

## Reusing in Experience.unity
Drag the prefab under `SceneRoot/Content`. The internal wiring (poppy, labels, VO, controller→those) is
in the prefab. **Scene references can't live in a prefab**, so re-assign on the instance:
`experienceManager` (the Experience Manager GO), `hideDuringTitle` (the garden plant GOs), and
`gardenFadeRoot` (Content). In VerticalSlice these are wired as instance overrides already.

## Not yet headset-verified
Built and structurally verified (compiles, poppy asset assigned, refs wired, prefab saved). Runtime flow
is coroutine-driven and untested in play/headset. Tuning likely needed: label sizes/heights, poppy
position, fade/hold durations (all serialized on the controller).

Related: [[scene-lock-system]] (title starts after lock, since it lives under Content),
[[vertical-slice-scene]], [[ovroverlaycanvas-poem-prototype]] (upgrade path for crisp passthrough text),
[[final-models-wiring]] (the mohnblume poppy splat).

# Experience System

_Last verified against code: 2026-06-17_

An interactive layer built atop the plant system that unlocks species through liked
progression and lets users "grow" context instances by proximity or gesture.
The user progresses through batches of plants: touch to select + listen to a poem,
step close to grey previews (or gesture for the nearest one) to grow them with labels
and a 180° environment painting, then like to commit the species and unlock the next
batch. After 8 liked species, the garden "flourishes."

Interaction polish layered on top of that core loop:
- **Sprout fade-in** — a newly unlocked plant fades in per-splat (opacity) into its dormant
  state instead of `SetActive` popping in (`Plant.SproutIn`).
- **Keep-available hand cue** — when a plant's poem finishes and the *keep* gesture unlocks,
  **both hands** get a soft green silhouette outline (+ optional motes) so the user knows the
  gesture is now live (`HandReadyCue`). The outline sits in passthrough air at the hand edge (an
  inverted-hull pass masked by the hand depth-occluder), so it never washes the real skin.
- **Post-flourish explore (gaze)** — once the garden has flourished, the context (index-up) gesture
  switches role to *explore*: a **raycast from the centre-eye (CenterEyeAnchor)** hits the splat
  instance you're looking at and reveals **that instance's single context** — fading in the owning
  plant's **poem text + the one context bound to that instance, with NO audio** (a silent reading
  moment); gaze a different instance for a different context (one per instance, not the plant's
  whole group), held for `replayHoldDuration` then faded
  (`ExperienceManager.ExploreGazed` / `Plant.Replay`). While gazing,
  the single instance under the ray brightens (`GsplatRenderer.Brightness`) **and both hands show a
  yellow "ask for context" outline** (`HandReadyCue.ShowContext`). This replaces the old gaze *cone*
  (angle-from-forward) and the old splat-explode highlight.

## Components

| Script | Role |
|---|---|
| `ExperienceManager.cs` | Scene singleton. Owns species unlock batches, like counter, flourish state. Routes `Select()` / `GrowNearestContext()` / `LikeSelected()`. Per-frame proximity reveal in `Update` (pre-flourish) and gaze hover-highlight (post-flourish). Manages gesture-wrapper activation (selectors disabled until animation finishes). Auto-adds `EnvironmentMoment` **and `GazeInstanceTargeter`** on `Awake`. Post-flourish drives `ExploreGazed()`. |
| `GazeInstanceTargeter.cs` | Stateless raycaster (lives on the Experience Manager GO). `TryGetTarget(out Plant, out GameObject)` casts a ray from the head/CenterEyeAnchor forward against the splat instances' fitted convex mesh colliders, returning the nearest hit's owning `Plant` (via `PlantTouchTrigger.Plant`) and the hit instance. Non-plant colliders (floor/hands) are skipped so they never block the gaze. Replaced the old angle-cone version (`maxAngleDeg`/`aimHeightOffset` removed). Defensive: a 0 layer mask / 0 distance falls back to everything / 12 m. |
| `HeroGlow.cs` | Per-plant touch glow. Draws a soft additive disc on the ground. Self-contained: auto-generates a quad + `Custom/URP/GroundGlow` material at runtime. Auto-added by `Plant.Awake()` if not assigned. API: `Show(pos, color, radius)` / `Hide()`. **Floor-only idle touch-invite — unchanged by the new hand cue.** |
| `HandReadyCue.cs` | Scene singleton. Shows a "you can keep this now" cue on **both hands** the moment the keep gesture unlocks (poem audio done). Per hand: a thin green **silhouette line** standing off the hand edge, built from **two** extra materials added on the hand's `Custom/HandDepthOccluder` SkinnedMeshRenderer — `Custom/URP/HandOutlineMask` (inverted hull extruded by `_EdgeOffset`, depth-only, queue `Transparent-1`) writes a standoff depth wall, then `Custom/URP/HandOutline` (inverted hull extruded by `_EdgeOffset + _Width`, `Cull Front`, additive, queue `Transparent`) survives only in the thin `[offset, offset+width]` band beyond it. So thickness (`_Width`) and standoff gap (`_EdgeOffset`) are **decoupled**, and the green lives in passthrough air, never on the skin. Plus an optional authored `motesPrefab` ParticleSystem. `Show()` attaches + fades in + breathes + starts motes; `Hide()` fades out + detaches + stops emission. `EnsureAttached()` re-adds the materials if OVRHand's system-gesture swap clears them. Auto-resolves hand renderers by their occluder material (or a serialized `handRenderers` list). API: `Show()` (green keep cue) / **`ShowContext()` (yellow "ask for context" cue, post-flourish gaze)** / `Hide()`. Green and yellow reuse the same outline materials — only the colour switches (`m_activeColor`), and they never overlap in time (keep is pre-flourish, context is post-flourish). **This replaced the old additive disc that tinted the passthrough hand green. Motes prefab is currently unassigned (line-only) — see `LikeAvailableCue.md`.** |
| `EnvironmentMoment.cs` | 180° cylindrical painting trigger. Wraps a painting texture around a world-space cylinder centred on the head, oriented to face forward. `Trigger(tex, center, forward, audioSrc)` / `Interrupt()`. |
| `GsplatInstanceFader.cs` | Fire-and-forget fade component. Lerps `_GsplatOpacityMul` from 1 → 0.002 over duration, then destroys the instance. |
| `TouchMePrompt.cs` | Proximity **hand-sprite** "touch me" cue (no text), billboarded above a hero plant when the viewer's head is near. Authored as the `Touch Me Prompt` child on the base `Plant.prefab`; armed in `Plant.ShowGlow` / disarmed in `HideGlow`. **Replaced the old `TouchPrompt.cs` 3D-TMP *text* prompt, which was removed 2026-06-22 along with the `ExperienceManager.touchPrompt` field and its scene GOs in VerticalSlice/Experience.** |
| `HandPoseAnimation.cs` + `FollowTransform.cs` | **Hand-pose cue sprites** (`Like`/`Context` gesture hints). Per-hand floating Canvas icon that follows the index fingertip, floats above it, Y-only billboards to the viewer, and pops on pose recognition. Four GOs: `ContextLeft/Right` (scene) + `LikeLeft/Right` (`Like.prefab`). Orientation/placement design + the "wonky/sideways" fix is in **`HandPoseCueSprites.md`**. `FollowTransform.worldSpaceOffset` (new) lifts the cue straight up regardless of finger tilt. |
| `Plant.cs` (experience members) | **`GrowInstance(go)`** — plays reveal on one preview instance, marks it grown, floats THIS plant's own context label for that slot (`PlantData.contextInfos` by spawn index). Each plant tells its own self-contained, ordered narrative — there is **no** global story. **`LikeCommit()`** — sets liked, stops audio defensively, fades poem but keeps info active (grown labels remain visible). **`CompleteSpecies()`** — fades out ungrown instances (available but NOT called by ExperienceManager in the current flow). **`Flourish(count)`** / **`BloomForGarden(count)`** — colour the previews, spawn+reveal extra instances (re-enable each clone's collider for the gaze ray); `BloomForGarden` scatters **one instance per context** (`Mathf.Max(instanceCount, OwnContextCount())`) so every context is gaze-reachable. No context text shown at flourish. **`GetUngrownInstances()`** → list of not-yet-grown instances. **`SpawnedInstances`** → read-only list of all spawned copies. **`SproutIn()`** (private, from `OnEnable`) — grow-in animation for a newly unlocked plant. **`Replay(gazedInstance)` / `EndReplay()` / `IsReplaying`** — post-flourish revisit: fade in the poem **text** + float the **single** context bound to the gazed instance (spawn index mod context count), liked-only, **no audio**, held for `replayHoldDuration` then faded. One context per instance, not the plant's group. |
| `PlantInfo.cs` (experience members) | **`PlaceContextAt(int, Transform, float)`** — positions single context label above an instance (in Above placement, above the instance's collider top via `SetContextTopLift`, not its base) + snaps billboard + fades label in. **`SetContextTopLift(float)`** — set by `Plant` to the collider top above the instance origin so Above labels clear the body (cylinder placement ignores it). **`SetFruitContext(bool)`** — Canopy Fruit mode: place the label a small clearance above its orb (no top-lift). **`FadeContextLabel(int, float)`** — fade one label independently. |
| `PlantTouchTrigger.cs` | Routes to `ExperienceManager.Select()` if it exists, else `PlantManager.Select()`, else direct `plant.Show()` (fallback). |
| `PlantInstanceScatterer.cs` | Runtime spawner. Scatters copies of a source GO across a bounding area using Mitchell best-candidate sampling. Two modes: hand-placed `BoxCollider bounds` (legacy), or optional `boundsCollider` + `boundsColliderMargin` for mesh-collider-aware placement. |
| `ContextFruit.cs` | **Canopy-fruit context target (trees).** Self-building cheap glowing orb (sphere mesh + `Custom/URP/FruitOrb` additive material) that hangs in a tree's canopy and carries ONE context — replacing a full splat clone so a tree is **1 splat + N tiny orbs**. `Init(owner, visualR, colliderR, color, dormant, ripe)` builds the visual + a **child** trigger collider with a **gaze-only `PlantTouchTrigger`** (resolves to the tree for the gaze ray, never touch-routes). `Ripen()` dim→bright (grow/like/bloom); `SetHover(on)` the gaze hover-glow (orb-mode replacement for `GsplatRenderer.Brightness`). Spawned by `Plant.SpawnFruit` when `contextMode == CanopyFruit`. |

## State Machine

| State | Represents | `Plant.IsLiked` | `Plant.m_grown` | Selectable |
|---|---|---|---|---|
| Locked | In batch not yet unlocked | false | (empty) | false |
| Idle | Batch active, unselected, un-liked (dormant glow shown) | false | (empty) | true |
| Selected | User touched; `Show()` running; grey previews spread | false | (empty) | true |
| Grown (partial) | Show done; user grew 1+ via proximity or gesture | false | non-empty | true |
| Liked | `LikeCommit()` called; liked plant stays in world, explorable | true | (any grown) | false |
| Flourished | All N species liked; extra instances spawning per liked plant | true | (all) | false |

Notes:
- There is no "Liked-pending" or "Completed" state. After `LikeCommit()` the plant stays in the scene with all its grown instances.
- `CompleteSpecies()` (ungrown fade-out) exists on `Plant` but is NOT invoked by `ExperienceManager` in normal flow.
- **Idle is entered via a sprout**, not a pop: on `OnEnable` an un-liked plant in play mode runs `SproutIn()` (per-splat opacity fade `_GsplatOpacityMul` 0.002→1 at the authored full pose; a synchronous 1-frame `localScale` collapse hides the activation frame because the GsplatRenderer builds its PropertyBlock lazily in its own Update) and only shows its touch glow once grown. The reveal morph stays at `progress 0` throughout, so Idle is the usual half-grey dormant bud and the touch reveal still plays out in full.
- **Flourished is explorable (gaze)**: post-flourish the context gesture calls `ExploreGazed()`, which raycasts from the centre-eye and `Replay(instance)`s the liked plant whose splat instance you're looking at — showing the poem + the **one** context bound to that instance (not the whole species' group). Liked plants keep their `info` object active (`LikeCommit`), so the labels can be recalled. The looked-at instance brightens as a hover cue (single instance, not the whole species).

## Flow

> **⚠ Drift note — this trace predates 2026-06-17 and is stale in two places:**
> 1. **Flourish is now sit-triggered:** `ChairSit` → `ExperienceManager.Sit()` → `StartFlourish()`.
>    `flourishAfterLikes` was **removed**; `LikeSelected()` only commits the plant + `UnlockNextBatch()`.
>    Ignore the "`likedCount >= flourishAfterLikes` → `StartFlourish`" branch below. (See
>    `chair-finale-sit`.)
> 2. **Pre-flourish context reveals by TOUCHING a preview** (`Touch()` → `GrowInstanceWithContext`),
>    not by the per-frame `revealRadius` proximity loop shown below (that loop was removed; the
>    context hand-gesture remains as a fallback). (See `context-reveal-by-touch`.)
>
> The rest of the trace — Select, sprout, gaze-explore, the bloom routine — is current. Also note the
> **VS touch-lock**: `Touch()` no-ops once `m_likedCount >= 1` when `bloomWholeRosterOnFlourish` (see Gotchas).

```
Touch trigger ─▶ PlantTouchTrigger.OnTriggerEnter
                  └─▶ ExperienceManager.Select(plant)
                      [guards: null, inactive, already liked, already selected, flourished]
                      ├─ GetMoment().Interrupt()          ← cancel any 180° environment
                      ├─ prev selected (un-liked): Hide() ← drops grey previews
                      ├─ plant.Show()                     ← poem audio + splat reveal + poem fade-in
                      │                                      spawns N grey instances (N = context blocks)
                      ├─ if plant has environmentPainting → moment.Trigger(...)
                      ├─ DisableSelectors
                      ├─ StartCoroutine(EnableSelectorsAfterAnimation)
                      │   ├─ wait: ShowAnimationDone      ← reveal animation complete
                      │   ├─ wait: likeEnableDelay (if > 0)
                      │   ├─ SetContextSelectorsActive(true)
                      │   ├─ wait: AudioSource.isPlaying  ← like gated until poem done
                      │   ├─ SetLikeSelectorsActive(true)
                      │   └─ HandReadyCue.Instance?.Show() ← BOTH hands glow: keep is now live
                      └─ onSpeciesSelected.Invoke()

Batch activation (start + UnlockNextBatch): plant GameObject SetActive(true)
  └─▶ Plant.OnEnable (play mode, un-liked, has selectionCollider)
       ├─ register footprint at FULL pose (GardenPlacer)  ← reserve before sprouting
       └─ SproutIn(): collapse localScale (1-frame hide — PB not built yet so opacity is a no-op on
                        frame 0) + SetSplatOpacity(0.002); morph stays at progress 0
                      → disable selectionCollider for the duration
                      → random 0..sproutMaxStartDelay stagger (re-assert opacity each frame, ≥1 frame)
                      → snap localScale back to full (still ~0 opacity), then lerp _GsplatOpacityMul
                        0.002→1 over sproutDuration: the splats FADE IN at full pose — NO size animation
                      → re-enable collider, ShowGlow()

ExperienceManager.Update (per frame, while plant selected + revealRadius > 0)
  └─ for each ungrown instance of m_selected:
       if horizontal distance from head ≤ revealRadius:
           GrowInstanceWithContext(plant, go)
               ├─ plant.GrowInstance(go)      ← play reveal, mark grown, place context label
               └─ if contextInfo has environmentPainting → moment.Trigger(...)

Context gesture (IndexUp wired to contextGestureWrappers)
  └─▶ ExperienceManager.GrowNearestContext()
       ├─ if m_flourished → ExploreGazed(); return   ← gesture switches role post-flourish
       └─ find ungrown instance of m_selected nearest the head by distance
          └─ GrowInstanceWithContext(plant, nearest)
             (same path as proximity reveal — grows, places label, triggers environment)

Post-flourish gaze hover (ExperienceManager.Update while m_flourished)
  └─▶ UpdateGazeHighlight()
       ├─ GazeInstanceTargeter.TryGetTarget() ← raycast CenterEyeAnchor.forward vs instance mesh colliders
       ├─ gazing a plant? HandReadyCue.ShowContext() (yellow); else Hide()
       ├─ on instance change: restore previous instance's Brightness, boost new one ×gazeHighlightMultiplier
       └─ track m_gazePlant (owning liked plant of the gazed instance)

Explore (post-flourish only, same context gesture)
  └─▶ ExperienceManager.ExploreGazed()
       ├─ raycast for the gazed plant AND instance (fallback: last hover m_gazePlant/m_gazeInstance)
       ├─ if target null / not liked → ignore
       ├─ if switching plants → previous m_exploringPlant.EndReplay()  ← clear its text
       └─ m_exploringPlant = target; target.Replay(gazedInstance)
            └─ fade poem TEXT in + float the ONE context bound to that instance
               (spawn index mod context count) above it, NO audio,
               hold replayHoldDuration → FadePoem(0) + HideContext()  (no 180° env)
               gaze a different instance → its own context (one per instance, not the group)

Like gesture (wired to likeGestureWrappers, only active after poem finishes)
  └─▶ ExperienceManager.LikeSelected()
       [guards: selected != null, not liked, not flourished]
       ├─ HandReadyCue.Instance?.Hide()  ← cue consumed
       ├─ plant.LikeCommit()     ← sets liked, stops audio, fades poem, keeps info active
       ├─ GetMoment().Interrupt()
       ├─ m_selected = null
       ├─ SetLikeSelectorsActive(false); SetContextSelectorsActive(true)
       ├─ m_likedCount++
       └─ if likedCount >= flourishAfterLikes → StartFlourish()
          else UnlockNextBatch()   ← activates next batch of plants

Garden Flourish (after flourishAfterLikes likes):
  └─ StartFlourish:
       ├─ if !bloomWholeRosterOnFlourish → Hide()+SetActive(false) all un-liked active plants
       │                                   (staged finale; when ON they're kept — they bloom)
       └─ FlourishRoutine:
            ├─ flourishing = bloomWholeRosterOnFlourish ? AllRosterPlants() : LikedPlants()
            └─ for each plant (batch order):
                 ├─ if whole-roster: SetActive(true) (sprout fade-in) + plant.BloomForGarden(N)
                 └─ else:            plant.Flourish(N)
                 wait flourishSpeciesStagger
               └─ onGardenFlourish.Invoke()

  • Experience.unity:    flourishAfterLikes=8, bloomWholeRosterOnFlourish=false → only liked plants bloom.
  • VerticalSlice.unity: flourishAfterLikes=1, bloomWholeRosterOnFlourish=true  → first like blooms the
                         whole roster (batch 0 heroes first, then batch 1 fades in). See VerticalSliceScene.md.
```

### Context content is per-plant (no global story)

Each plant tells **its own** self-contained, ordered narrative — its `PlantData.contextInfos` (a list
of text+bg+optional-painting entries authored *about that plant*). The experience is **non-linear and
explorative**: plants appear, the user explores whichever they like in any order, likes one, and more
appear. There is **no** global narrative, no cursor, no cross-plant content.

- **Pre-flourish:** selecting a plant spreads one grey preview per context entry; growing a preview
  (`GrowInstanceWithContext` → `Plant.GrowInstance(go)`) reveals that plant's own `contextInfos[i]`
  by spawn index and fires its 180° painting if it has one.
- **Flourish:** a quiet bloom — `Plant.Flourish` / `BloomForGarden` colour the previews, spawn +
  reveal copies, and re-enable the clone colliders as gaze targets. **No context text is shown.** A
  bloomed (never-explored) species scatters **one instance per context** (`Mathf.Max(instanceCount,
  OwnContextCount())`) so every context has a gaze-targetable copy in the garden.
- **Post-flourish gaze = ONE context per instance:** `ExploreGazed()` reads the specific splat
  **instance** under the ray (not just its plant) → `Plant.Replay(gazedInstance)` floats the single
  context bound to that instance (its spawn index mod the plant's context count) above it, plus the
  poem text, held for `replayHoldDuration` then faded. Gaze a **different instance** to read a
  **different** context. It is NOT the plant's whole context group — each instance shows exactly one.

> A global `StorySequence` abstraction was tried and **removed** — it forced one ordered narrative across
> a non-linear garden and detached content from the plant showing it. Do not reintroduce it.

### Context placement mode: Per-Instance vs Canopy Fruit (trees)

`Plant.contextMode` (`ContextPlacementMode`) decides how a plant's per-context grow/gaze targets are
spawned — it **decouples context count from splat-instance count**:

- **`PerInstance`** (default, all non-trees): the original model — one scattered splat clone per
  context, its label floating above that clone. N contexts ⇒ ≥ N full splat copies in the garden.
- **`CanopyFruit`** (Date_Palm, Fig_Tree, Pear_Tree): keep **ONE** hero splat body and hang each
  context as a cheap glowing **orb** (`ContextFruit`, `Custom/URP/FruitOrb` additive sphere) at a
  procedurally-sampled point in the **canopy** (vertical band `canopyBottom`..`canopyTop` of the
  selection-collider height, inset by `canopyInset`, Mitchell-spaced). The band top is kept below the
  bounds apex on purpose (gsplat scans have stray points above the dense crown). So a tree is **1
  splat + N tiny orbs** instead of N splat clones — the whole point, since the final tree models have
  high splat counts.

How the orbs reuse the existing machinery (no new spawn/gaze/replay plumbing):
- **Spawn** — `Plant.SpawnFruit(n, ripe)` builds orbs, parents them under the plant (so they follow
  scene-lock moves), and adds them to `m_spawnedInstances`. They are the plant's "instances" for every
  downstream path. Branched at all four spawn sites: select preview (dormant orbs), `Like`/`Flourish`
  (ripen existing orbs, **no scatter**), `BloomForGardenRoutine` (reveal hero body + hang N ripe orbs,
  **no scatter**).
- **Gaze** — each orb's trigger collider lives on a **child** carrying a **gaze-only**
  `PlantTouchTrigger` (`gazeOnly = true` → answers `.Plant` for the ray but never routes a hand-touch
  `Select()`). `GazeInstanceTargeter` finds the owning tree via that trigger and, with no
  `GsplatRenderer` in the orb's parents, returns the collider's parent = the orb root (the
  `m_spawnedInstances` entry) — so `Replay(orb)`'s index lookup maps to the right context. **Requires
  the Plant component's own GameObject to have no `GsplatRenderer`** (verified true for all 3 trees).
- **Grow/ripen** — `GrowInstance` (and the flourish/like colour passes) call `ContextFruit.Ripen()`
  (dim → bright with a small overshoot). Dormant orbs are the unread-context cue; ripe = revealed.
- **Hover** — orbs carry no `GsplatRenderer`, so the post-flourish gaze hover boosts the orb's own glow
  via `ContextFruit.SetHover()` (driven by `ExperienceManager.UpdateGazeHighlight`) instead of
  `GsplatRenderer.Brightness`.
- **Label** — `PlantInfo.SetFruitContext(true)` (set by `PlacePoem`) makes context labels float a small
  clearance **above the orb** (no collider top-lift — the orb is already up in the canopy; adding the
  plant base→top height would shoot the label clear above the tree).

> Not yet headset-verified. Orb visuals (radius/colour/glow intensity) and canopy sampling
> (`canopyTopFraction`/`canopyInset`) are expected to need in-headset tuning.

## Inspector Wiring (Experience.unity)

### ExperienceManager (scene GO, singleton)

**Unlock Batches** (ordered, 7 total):
- Batch 0 (always active at start): Poppy
- Batch 1 (unlock on 1st like): Crocus, Narcissus
- Batch 2 (unlock on 2nd like): Lavender, Bamboo
- Batch 3 (3rd like): Hemp, Rhododendron
- Batch 4 (4th like): Date_Palm, Fern
- Batch 5 (5th like): Fig_Tree, Hibiscus
- Batch 6 (6th like): Pear_Tree, (1 plant; unlocks on 7th like)

**Flourish** defaults: `flourishAfterLikes = 8`, `flourishInstancesPerSpecies = 4`, `flourishSpeciesStagger = 1.0`.

**Head**: Optional `Transform`. Falls back to `Camera.main` if unset.

**UI**: none. The old `TouchPrompt` GameObject field was removed (2026-06-22) — the "touch me" cue is now the per-plant `TouchMePrompt` hand sprite authored on `Plant.prefab`, so there is nothing to wire here.

**Like Gesture**:
- `likeGestureWrappers` → RightSmallHeart + LeftSmallHeart `SelectorUnityEventWrapper.WhenSelected`
- `likeSelectorObjects` → the corresponding hand selector GameObjects (toggled active/inactive)

**Context Gesture**:
- `contextGestureWrappers` → ContextLeft/Right + IndexUp `SelectorUnityEventWrapper.WhenSelected`
- `contextSelectorObjects` → the corresponding selector GameObjects (must be a DISTINCT list from `likeSelectorObjects` so retiring the like gesture doesn't disable context)

**Timing**: `likeEnableDelay = 0` (default). The like gate is audio-completion, not a timer.

**Proximity Reveal**: `revealRadius = 0.9 m`. Set to 0 to disable (gesture-only mode).

**Audio**: three cleanly separated paths —
- **Poem VO → 2D, at the head.** A single shared `[Poem VO]` AudioSource (child of `[Experience Manager]`, `spatialBlend = 0`) plays the narration. Every plant's `audioSource` field points at it. **The VO starts AFTER the reveal animation finishes, not at select** — `Plant.Show()` only assigns the clip; the actual `Play()` is deferred to the end of `Plant.ShowAfterAnimation()` (together with the poem text fade-in), so the spoken poem no longer talks over the reveal/grow SFX. Gating and `EnvironmentMoment` read `plant.AudioSource` (= this shared source). The like-gate and context-grow gate (both wait on `AudioSource.isPlaying`) still hold — they just shift later with the poem.
  - **180° painting fade-out follows the poem.** The painting still fades IN at select (during the reveal — unchanged), but `EnvironmentMoment` now POLLS the poem for its fade-OUT instead of pre-computing a fixed hold: it waits (bounded by `defaultHoldSeconds`) for the VO to begin, holds while `isPlaying`, then `audioTailSeconds`, then fades out. Silent context paintings (no VO) fall back to the fixed `defaultHoldSeconds`. (Needed because the poem now starts late, after the reveal.)
- **Ambient music → 2D, room-filling.** Two looping score beds crossfade with garden state via the `GardenAmbience` singleton — an "empty garden" bed (title + explore) and a "blooming garden" bed (after the sit-flourish). Non-spatialised; separate from the poem VO and the plant SFX. See `AmbientMusic.md`.
- **SFX → 3D HRTF, from the plant.** Three feedback clips — `revealSfx` (on select + once **per species** during the flourish), `contextSfx` (a context label grows in), `likedSfx` (species liked) — play via `plant.PlaySfx(clip, sfxVolume)` on the plant's own `[BuildingBlock] Spatial Audio` source (`sfxSource`, `spatialBlend = 1`, Logarithmic rolloff `minDistance = 0.4`/`maxDistance = 15`, **`spatialize = true` → Meta XR Audio HRTF**). `PlaySfx` uses `AudioSource.Play()` (not `PlayOneShot`) so the spatializer applies HRTF to the voice **and** repeated SFX **replace rather than stack** — important because the clips are long (5–9 s); stacking was the loudness culprit. `Hide()` stops `sfxSource` so a long SFX is cut on deselect. `sfxVolume` (default 0.5) is the global level knob on `ExperienceManager`.
  - **Meta Spatial Audio requirements (must stay satisfied):** the 3 SFX clips are imported **Force To Mono** (the spatializer only accepts mono); each SFX source keeps a mono clip assigned; project audio **DSP Buffer Size = Best latency (256)**, Speaker Mode = Stereo, Spatializer/Ambisonic plugin = Meta XR Audio. The poem clips stay stereo — fine, since `[Poem VO]` is a plain 2D source (not a building block). NOTE: do **not** add stereo clips to the building-block sources, and don't click the Project Setup Tool's mono "Fix" while a stereo clip is assigned there (it would Force-To-Mono that clip).

**Events**: `onSpeciesSelected` / `onSpeciesLiked` / `onSpeciesCompleted` / `onGardenFlourish`.

### [Hand Ready Cue] (scene GO, singleton)

`HandReadyCue` component on a dedicated GameObject. Driven entirely by `ExperienceManager`
(`Show` green on keep-unlock, `Hide` on like / re-gate / deselect; **`ShowContext` yellow while
gazing at a plant post-flourish, `Hide` when the gaze leaves**) — no per-plant wiring.

- **Hand renderers**: leave `handRenderers` empty to auto-resolve — any `SkinnedMeshRenderer`
  wearing `Custom/HandDepthOccluder` is outlined. No scene wiring needed. (`hands` is only used to
  parent optional motes; falls back to `HandProximity.Instance.Hands`.)
- **Motes**: `motesPrefab` is **currently empty → outline-only**. Authoring/tuning the green-mote
  ParticleSystem is an optional next step — see `LikeAvailableCue.md`.
- Look/animation defaults: `color` green (keep cue), **`contextColor` yellow (ask-for-context cue)**,
  `outlineWidth 0.003` (thin line), `outlineOffset 0.006` (standoff gap), `outlineBlur 0.2`,
  `outlineSmoothing 1.5` (edge AA), `fadeDuration 0.35`, `pulseMinAlpha 0.45`, `pulsePeriod 1.4`.
  Width/offset/blur/smoothing/colour push to the materials live while shown, so they tune in play mode.

### Per Plant (all plants in scene)

- Assign `PlantData` asset (Poppy, Crocus, Narcissus, etc.)
- Assign `scatterer` (PlantInstanceScatterer with preset bounds for each plant)
- Tune `likedStaggerDelay` (default 0.35s; gap between instance reveals on like + flourish)
- Tune `contextHeightOffset` (default 0.6m; clearance above each instance's collider top where its label floats in Above placement — cylinder placement uses it as height above the instance origin)
- Tune `glowColor` / `glowRadius` (touch-glow disc shown while plant is idle/touchable)
- **Grow-In (Sprout)**: `sproutDuration` (1.2s), `sproutMaxStartDelay` (0.25s) — the per-splat
  fade-in (reveal morph rest floor 0→startAt, no transform scale-up) when this plant's batch unlocks.
  Liked plants and edit mode never sprout.

### PlantInfo (per plant's label object)

- The shared `PlantInfos.prefab` provides **8** context-label slots (`contextLabels` + parallel
  `contextLabelRoots`, one root per label) — the cap on how many contexts a plant can show. Max
  authored `PlantData.contextInfos` is currently 6 (Lavender/Rhododentron), so 8 has headroom. A
  plant with fewer contexts just leaves the extra slots unused (SetData deactivates them).
- Each root has a `LookAtTarget` component (runOnStart/runOnUpdate off; snapped by `PlaceContextAt()`)

### PlantTouchTrigger (on plant's collider child)

- Assign `plant` reference
- Set `handLayers` and `handTag` as needed

### ScatterBounds

Each plant prefab has a `PlantInstanceScatterer`. Two modes:
1. **BoxCollider `bounds`** (legacy, default): hand-placed box defines the scatter footprint.
2. **`boundsCollider` + `boundsColliderMargin`** (optional): scatter across the world-AABB of any Collider + outward margin (metres). Assign a plant's mesh `selectionCollider` here to switch that plant to dynamic bounds.

Verify bounds are set (not null) for all plants so scatter is non-degenerate.

## Tuning Parameters

| Field | Default | What it does |
|---|---|---|
| `ExperienceManager.likeEnableDelay` | 0 | Extra seconds after show animation before like selector enables (on top of audio-completion gate). 0 = off. |
| `ExperienceManager.revealRadius` | 0.9 | Horizontal distance (m) from head that auto-grows an ungrown preview. 0 = gesture-only. |
| `ExperienceManager.gazeHighlightMultiplier` | 1.6 | Brightness multiplier applied to the single splat instance under the post-flourish gaze (1 = no change; restored when the gaze leaves). |
| `GazeInstanceTargeter.maxRayDistance` | 12 | Max length (m) of the post-flourish gaze ray cast from the centre-eye. |
| `Plant.replayHoldDuration` | 12 | Post-flourish ask: seconds the poem text + grown context labels stay up to read before fading (no audio plays). |
| `HandReadyCue.contextColor` | yellow | Colour of the post-flourish "you can ask for context" hand outline (separate from the green keep cue). |
| `ExperienceManager.flourishAfterLikes` | 8 (1 in VerticalSlice) | Like threshold that triggers flourish. |
| `ExperienceManager.bloomWholeRosterOnFlourish` | false (true in VerticalSlice) | OFF = flourish blooms only liked plants (staged garden). ON = flourish blooms the whole roster (every plant in the batches, activating not-yet-unlocked ones so they sprout/fade in). Replaced the old `presentationMode`. |
| `ExperienceManager.flourishInstancesPerSpecies` | 4 (2 in VerticalSlice) | Extra splat copies spawned per species during flourish (the bloom density). |
| `ExperienceManager.flourishSpeciesStagger` | 1.0 | Seconds between each species' flourish reveal. |
| `Plant.instanceFadeOutDuration` | 1.5 | Fade duration when `CompleteSpecies()` destroys ungrown instances (available but currently unused in normal flow). |
| `Plant.likedStaggerDelay` | 0.35 | Gap (s) between each instance reveal when liked or flourishing. |
| `Plant.contextHeightOffset` | 0.6 | Clearance (m) above each instance's collider top where its context label floats (Above placement; cylinder placement uses it as height above the instance origin; **Canopy Fruit: clearance above the orb, no top-lift**). |
| `Plant.contextMode` | PerInstance | PerInstance (default) = one splat clone per context. CanopyFruit (trees) = 1 hero body + one glowing orb per context in the canopy. |
| `Plant.canopyBottom` / `canopyTop` | 0.5 / 0.85 | Canopy Fruit only: the vertical fruit band as fractions of plant height from the ground. Top is kept **below** ~0.9 on purpose — gsplat scans have sparse stray points above the dense crown, so anchoring to the bounds apex floats orbs above the visible foliage. Per-tree tunable (palms want a higher, tighter band than broadleaves). |
| `Plant.canopyInset` | 0.15 | Canopy Fruit only: horizontal inset (0..0.5) of the canopy footprint so orbs hang inside the foliage. |
| `Plant.fruitOrbRadius` | 0.09 (0.10 on trees) | Canopy Fruit only: visual radius (m) of each glowing orb. |
| `Plant.fruitColliderRadius` | 0.16 (0.18 on trees) | Canopy Fruit only: gaze-collider radius (m) per orb (larger than the visual so the gaze snaps on). |
| `Plant.fruitColor` | warm amber | Canopy Fruit only: orb glow colour. |
| `Plant.sproutDuration` | 1.2 | Seconds for a newly unlocked plant to fade in (per-splat) into its dormant state. |
| `Plant.sproutMaxStartDelay` | 0.25 | Max random pre-sprout delay (s) so a batch staggers organically. |
| `HandReadyCue.outlineWidth` | 0.003 | Thickness (m) of the green line (keep thin; independent of the gap). |
| `HandReadyCue.outlineOffset` | 0.006 | Standoff gap (m) of passthrough between the real hand edge and the line. |
| `HandReadyCue.outlineBlur` | 0.2 | Softens the line's outer edge (0 = crisp, 1 = very soft). |
| `HandReadyCue.outlineSmoothing` | 1.5 | Screen-space anti-aliasing of the edge (px) — takes the hardness off corners. |
| `HandReadyCue.fadeDuration` | 0.35 | Outline fade in/out time. |
| `HandReadyCue.pulseMinAlpha` | 0.45 | Outline alpha at the dim end of the breathing pulse. |
| `HandReadyCue.pulsePeriod` | 1.4 | Seconds per breathing pulse cycle. |
| `Plant.glowColor` | cyan-ish | Colour of the idle/touch-invite ground glow. |
| `Plant.glowRadius` | 0.6 | Radius (m) of the touch-glow disc. |
| `HeroGlow.fadeDuration` | 0.4 | Glow fade in/out time. |
| `HeroGlow.heightOffset` | 0.02 | Disc raised above ground to avoid z-fighting. |
| `HeroGlow.softness` | 0.6 | Edge softness of the glow disc (0 = hard, 1 = very soft). |
| `PlantInstanceScatterer.spacing` | 0.6 | Minimum distance between scatter copies (metres). |
| `PlantInstanceScatterer.spacingVariance` | 0.2 | Random ± applied to spacing so layout never looks regular. |
| `PlantInstanceScatterer.boundsColliderMargin` | 0.5 | Outward margin when using `boundsCollider` mode. |

## Audio & Events

- `Plant.audioSource` points at the shared `[Poem VO]` source (2D, child of `[Experience Manager]`); it plays the poem clip (from `PlantData.audioClip`) **at the end of the reveal** (`Plant.ShowAfterAnimation`, not at `Show()`/select) so the narration doesn't overlap the grow SFX — narration at the head, not localised to the plant. `Plant.sfxSource` is the plant's own `[BuildingBlock] Spatial Audio` source (3D) used for feedback SFX.
- Like is gated until `AudioSource.isPlaying` becomes false — you can never skip a poem. (With the poem now deferred, the gate just unblocks later.)
- **Ambient music** (`GardenAmbience`): a 2D, non-spatial score that crossfades an "empty" bed (pre-bloom) and a "blooming" bed (post-sit flourish). Separate from poem VO + SFX. `PlayBloom()` at the top of `StartFlourish()`, `PlayEmpty()` on start and `ResetAll()`. Full notes in `AmbientMusic.md`.
- `Plant.LikeCommit()` calls `audioSource.Stop()` defensively (poem should be done, but this guards against edge cases).
- `ExperienceManager` feedback SFX are **spatial 3D**, played from the plant's own source via `plant.PlaySfx(clip, sfxVolume)`: `revealSfx` on `Select()`; `contextSfx` on each successful grow in `GrowInstanceWithContext()`; `likedSfx` on `LikeSelected()`; and `revealSfx` once per species in `Plant.Flourish(count, revealSfx, sfxVolume)` (NOT per instance — long clips would stack). A grow plays only `contextSfx`, never the reveal sound. All emit from the plant's `[BuildingBlock] Spatial Audio` source, so they localise to the plant (grow/flourish sounds come from the owning plant's position, not each scatter copy). `PlaySfx` = `Play()`, so a new SFX replaces the previous on that source.
- `onSpeciesSelected` / `onSpeciesLiked` / `onSpeciesCompleted` / `onGardenFlourish` fire for external listeners.

**Audio status**: All 12 species have a poem VO clip assigned on their `PlantData` (in `Assets/_Projects/_Audio/VO/`). Hibiscus uses `hibiscus_poem.wav` (v1); `hibiscus_poem2.wav` and `Wünschelrute.wav` are unused.

## Debug / ContextMenu Helpers

All on `ExperienceManager.Instance` (right-click the component → context menu):
- **Debug Select Next**: Selects the first active, un-liked, unselected plant.
- **Debug Grow**: Grows the first ungrown instance of the selected plant (bypasses proximity).
- **Debug Like**: Calls `LikeSelected()` immediately.
- **Debug Jump To Round**: Fast-forwards to round `debugJumpRound` (default 4) by auto-liking that many plants through the real `LikeSelected` path — so batches unlock exactly as in normal play. Leaves you mid-experience at that round (next batch unlocked), no flourish. The touch reveal is skipped, so the liked *source* bodies stay in their dormant look; the flourish's spawned instances are the populated payoff.
- **Debug Jump To Round + Flourish**: Jumps to `debugJumpRound`, then triggers the flourish — the headline "see the flourish without playing through" button. Produces a populated garden (each liked species spawns `flourishInstancesPerSpecies` revealed instances).
- **Debug Force Flourish**: Triggers the flourish immediately. **If nothing has been liked yet it auto-likes up to `debugJumpRound` first**, so it always shows a populated garden — previously it silently did nothing from a cold start (no liked species → empty flourish).
- **Debug Explore Gazed**: Calls `ExploreGazed()` — replays the liked plant under the gaze ray (post-flourish explore, headset-free).

`debugJumpRound` (Debug header on `ExperienceManager`, default 4) is the round these jumps target. It's clamped to `[1, min(flourishAfterLikes, total plant count)]`.

Useful for headset-free iteration: select, grow, like, jump rounds, and flourish without hand input.

## Gotchas

- **Opacity shader quirk**: `_GsplatOpacityMul ≤ 0.0001` is treated as 1.0 (full opacity) by the Gaussian splat shader. `GsplatInstanceFader` fades to 0.002, never zero.
- **Fern is a placeholder**: The Fern splat model is reused for Fig_Tree, Hibiscus, Narcissus, and Pear_Tree (WP-C note). Real models needed before shipping.
- **Info stays active after LikeCommit**: Unlike `Like()` (which hides + deactivates the info object), `LikeCommit()` fades the poem but keeps info active so grown context labels remain visible.
- **Selectors are gated separately**: Context selectors enable after the show animation; like selectors enable additionally only after poem audio finishes. Both re-gate on each new selection.
- **Liked plants stay in the world**: A liked plant is never `Hide()`d or `SetActive(false)`d during normal play. It stays in the scene with all its grown instances for the rest of the experience.
- **PlantTouchTrigger routing**: Prefers `ExperienceManager.Select()` if it exists, then `PlantManager.Select()`, then direct `plant.Show()` as fallback.
- **VS touch-lock after the first like** *(added 2026-06-20)*: in the vertical slice (`bloomWholeRosterOnFlourish == true`), `ExperienceManager.Touch()` no-ops once `m_likedCount >= 1` (`TouchLockedByFirstLike => bloomWholeRosterOnFlourish && m_likedCount >= 1`). This disables **all** hero touch — both context-reveal-by-touch and hero selection — after the first like, so accidental hand-brushes can't re-select plants while the visitor walks to the chair to sit. Gaze (post-flourish hover + the gaze-explore gesture) never routes through `Touch()`, so it stays live. Reset to touchable by `BeginGarden`/`ResetAll` (both zero `m_likedCount`). The staged garden (`Experience.unity`, flag off) keeps touch for its multi-like progression. NOTE: this does **not** stop an accidental *like* (a hand-pose gesture, not a collision). See `VerticalSliceScene.md` → "Touch lock after the first like".
- **Selector list independence**: `likeSelectorObjects` and `contextSelectorObjects` must be distinct lists; retiring the like gesture after a like must not disable the context gesture.
- **HeroGlow auto-created**: `Plant.Awake()` calls `gameObject.AddComponent<HeroGlow>()` if none is already assigned or present as a component. No manual scene setup required.
- **Sprout leaves the morph at progress 0**: `SproutIn()` is a transform-scale + `_GsplatOpacityMul` fade *on top of* the dormant bud — it deliberately does **not** drive the `GsplatRevealAnimator`, calling `ResetAnimation()` so the morph rests at `progress 0`. Driving the reveal for the grow-in would colourise the plant and spend the touch reveal (whose payoff is `progress 0→1`). If a more organic gaussian "bloom" is wanted during the rise, the safe way is a morph *breath* (`progress 0→peak→0`) that returns to rest — not a forward reveal. (See `LikeAvailableCue.md` notes / the sprout-vs-morph discussion.)
- **Sprout reserves footprint first**: `OnEnable` registers the GardenPlacer footprint at the *full* pose before sprouting, and disables `selectionCollider` for the sprout duration, so a half-grown plant can't be touched and copies never scatter onto its final footprint.
- **An interrupted sprout MUST restore the collider (and pose)** *(bug fixed 2026-06-20)*: `SproutIn` captures the pre-sprout collider-enabled state into a **member** (`m_sproutRestoreColliderEnabled`, not a local) and restores it on clean completion; the shared `RestoreSproutPose()` helper — called from `OnDisable` **and** the top of `StartSprout` — restores both the full pose and that collider state when a sprout is interrupted mid-flight. Earlier the interrupt path restored only the pose, so a sprout interrupted by `ExperienceManager.BeginGarden`'s deactivate-all→reactivate left `selectionCollider` **off**; the next `SproutIn` then re-captured `false` and "restored" it to off → the plant sprouted + glowed normally but was **permanently untouchable** (gaze missed it too). This bit all three cold-start heroes in `VerticalSlice.unity`; the restart path self-healed because `ResetState()` re-enables the collider, which is why it only failed on the first run. Any future change that disables the collider during a synchronous sprout MUST keep this restore.
- **Hand cue is ExperienceManager-driven only**: the **green keep** cue — `HandReadyCue.Show()` fires at the end of `EnableSelectorsAfterAnimation` (keep unlocked); `Hide()` fires in `LikeSelected` (consumed) and `DisableSelectorsAndCancelTimer` (new selection / re-gate / deselect). The **yellow context** cue — `HandReadyCue.ShowContext()` / `Hide()` are driven each frame by `UpdateGazeHighlight` (post-flourish) based on whether the gaze is on a plant. The two cues reuse the same outline materials and never overlap in time (keep is pre-flourish, context is post-flourish), so a single recolourable outline suffices. The floor `HeroGlow` is untouched.
- **HandReadyCue rebuilds lazily**: it builds its per-hand outline materials on first `Show()`; if the hand renderers aren't resolvable yet (hands not tracked) it silently retries next `Show()`. If the `Custom/URP/HandOutline` shader isn't found the outline is skipped (logged), not magenta. The outline relies on the hand staying a depth-only occluder (writes depth, no colour) — if the hand shader changes, the masking that keeps green off the skin breaks.
- **Post-flourish gaze relies on instance colliders + `Brightness`**: the gaze ray hits the fitted convex *trigger* mesh colliders the scatterer clones with each splat instance (the collider host sits under the "Gaussian" source the scatterer clones, so clones carry it). `Physics` queries hit triggers (`QueryTriggerInteraction.Collide`). The hover highlight drives `GsplatRenderer.Brightness` — a per-renderer field the impl re-applies to `_Brightness` **every frame**, so a `MaterialPropertyBlock` override would be clobbered; the field is the correct lever. `Plant.Flourish` re-enables the clone colliders (they're cloned after `LikeCommit` disabled the source collider). The liked plant's own body stays un-gazeable (its `selectionCollider` is disabled), so only the scattered instances are explore targets. The `GazeInstanceTargeter` is auto-found on the Experience Manager GO (no new scene wiring); its `head` was already the CenterEyeAnchor.
- **Disabled-collider bounds collapse the poem/context heights**: a liked plant's `selectionCollider` is DISABLED, and a disabled `Collider` reports a **zero-size `bounds`** at its transform position (verified for both Box and Mesh colliders). `PlacePoem()` used to read `selectionCollider.bounds` for the poem height *and* the context top-lift (`m_contextTopLift`), so post-flourish (`Replay`) those collapsed to ≈ground — the poem dropped low and the context labels lost their lift and overlapped it. Fixed by `Plant.SelectionColliderWorldBounds()`: use the live bounds when it's non-degenerate (enabled — pre-flourish behaviour unchanged), otherwise reconstruct the world AABB from the collider geometry (MeshCollider `sharedMesh.bounds` / BoxCollider `center+size`) via `localToWorldMatrix`. The offsets (`poemHeightOffset` / `contextHeightOffset`) were never the problem.

## Deferred / Not Implemented

These were designed or discussed but are **not** in the current codebase:

- **Gaze targeting — pre-flourish only deferred**: pre-flourish the context gesture still grows whichever ungrown instance is closest to the head (`GrowNearestContext`), with no gaze check. Gaze is now used **post-flourish** for explore (see "Post-flourish explore (gaze)" above) — a centre-eye raycast against instance mesh colliders, replacing the old angle cone. The old splat-explode highlight (`_GsplatExplodeStrength`, still present as an unused shader uniform) was replaced by the per-instance `Brightness` hover boost.
- **Per-hero Gaussian tint** (`Plant.SetHeroTint` / `_GsplatTintColor`): designed in Stage 3 as a fragment multiply that would wash the selected plant's gaussians toward the glow colour. Not present in `Plant.cs`. The ground glow (`HeroGlow`) was implemented; the gaussian recolour was not.
- **Keep-available motes**: `HandReadyCue` supports an optional `motesPrefab` ParticleSystem per hand, but none is authored yet, so the cue is **glow-only** in the scene. Authoring/tuning is the next-session task — see `LikeAvailableCue.md`.
- **Save / share garden layout** (Feature D): designed only, **not built**. A `GardenLayout` snapshot of liked species + per-instance transforms captured on `onGardenFlourish`, serialised with `JsonUtility`, with a read-only playback mode to "walk someone else's garden." Pairs with the postcard takeaway (`Plan_PhotoPrinter.md`) and like-order capture (`Plan_EndPoem.md`). Full design in the approved plan file.

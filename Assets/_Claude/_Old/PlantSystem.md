# Plant Interaction System

VR plant experience for Meta Quest. The user walks up to plants, touches them to
hear a poem + see labels, and performs a "like" hand gesture on the one they want.
Liking spreads copies of that plant across the space and moves on to the next set.

The experience is a **sequence of rounds**. Each round activates a small set of
plants (2–4; a tutorial round can be 1). Touch to select/listen, like to commit,
advance to the next round.

## Components

| Script | Role |
|---|---|
| `PlantManager.cs` | Scene-level orchestrator. Owns selection state + round progression. Singleton (`PlantManager.Instance`). |
| `Plant.cs` | A single plant ("view"). Exposes `Show()` / `Hide()` / `ShowContext()` / `Like()`. Owns the instance-spread lifecycle (preview spawn on show, colourise on context, double on like). Knows nothing about other plants. |
| `PlantTouchTrigger.cs` | Trigger collider on a plant. On hand entry, routes to `PlantManager.Instance.Select(plant)`. |
| `PlantInfo.cs` | Lives on the plant's labels object. Holds the poem + context `PlantLabel`s; applies `PlantData` sprites. Poem stays in the `PlantInfos` canvas above the plant; each context label is its own billboard root (`contextLabelRoots`) repositioned above its paired instance by `PlaceContextAt`. `FadePoem` / `FadeContext` fade the two groups **independently**. |
| `PlantLabel.cs` | One label = a text `Image` + a background `Image`. Lives on `Label.prefab`; the background child carries the small Z offset (≈0.02) that puts it behind the text. Exposes `SetContent(PlantLabelContent)` and `SetAlpha(a)`. |
| `PlantData.cs` | ScriptableObject: `displayName` (id only), `audioClip`, `poem` (text+bg sprite pair), `contextInfos` (list of text+bg pairs). One asset per species (e.g. `Bamboo.asset`). |
| `GsplatShockwaveAnimator` | Per-splat reveal animation: greyscale → animated shockwave → colour. API: `Play()`, `IsDone`, `ApplyInitialGreyscale()`, `ForceColored()`. |

`HandPoseAnimation.cs` / `HandPoseTargetInjector.cs` are independent gesture FX,
wired in the editor to the like gesture's selector events. They are not part of
the plant data flow.

## Flow

```
Hand enters trigger ─▶ PlantTouchTrigger.OnTriggerEnter
                         └▶ PlantManager.Instance.Select(plant)
                              ├ hide previously selected plant (destroys its grey preview)
                              └ plant.Show()  (audio + splat reveal + POEM fade-in, then
                                               spawn N grey preview instances, N = context
                                               block count; context labels stay hidden)

Context gesture (IndexUp, editor-wired) ─▶ PlantManager.ShowContextSelected()
                                             └▶ m_selected.ShowContext()  (stagger-colourise the
                                                 preview instances + float each context label above
                                                 its paired instance, billboarding to the user)

Like gesture (editor-wired) ─▶ PlantManager.LikeSelected()
                                 ├ liked.Like()  (force-colour any still-grey preview, then spawn
                                 │                 another N to double the spread + reveal; lock
                                 │                 plant, hide labels, disable collider)
                                 ├ Hide()+SetActive(false) on the round's OTHER plants
                                 └ AdvanceRound() ─▶ BeginRound(index + 1)
```

### PlantManager

- **State**: `m_roundIndex` (current round, -1 before start), `m_selected` (currently
  shown plant). `CurrentRound` exposes the index.
- **`Start()`**: optionally `DisableAllPlants()` (if `autoDisableAllOnStart`), then
  `BeginRound(0)`.
- **`BeginRound(index)`**: deactivates the prior round's plants (`DeactivatePlant`,
  which *skips liked plants* so spread copies survive), clears selection, then
  activates the new round's plants. If `index` is past the end, fires
  `onAllRoundsComplete` and stops.
- **`Select(plant)`**: no-op if it's already selected, liked, or not in the current
  round. Otherwise hides the previous selection and `Show()`s the new one.
- **`LikeSelected()`**: likes `m_selected`, disables the round's other plants, then
  `AdvanceRound()`.
- **`ShowContextSelected()`**: fades in the selected plant's context labels
  (`m_selected.ShowContext()`); no-op if nothing is selected. Wired to the context
  ("IndexUp") gesture's `SelectorUnityEventWrapper._whenSelected`, the same way
  `LikeSelected()` is wired to the like gesture.
- **Liked plants are sticky**: `ActivatePlant` / `DeactivatePlant` both early-out on
  `p.IsLiked`, so once liked a plant (and its copies) is never re-touched by round
  transitions.

### Plant

- **Audio**: `PlantData.audioClip` is auto-assigned onto the plant's `AudioSource`
  in `Awake`/`OnValidate`/`Show` (`AssignAudioClip`), so you only fill the clip in
  the data asset, not per-plant.
- **`Show()`**: guard on `IsLiked`; assigns + plays `audioSource`, sets label data, runs
  `ShowAfterAnimation()` — plays all `splats`, waits for `IsDone`, fades the **poem**
  in (`info.FadePoem(1)`), then `SpawnPreviewInstances()`: `scatterer.Spawn(N)` (N = context
  block count) activated and parked **greyscale** so a visible-but-grey spread appears.
  Context labels stay hidden until the context gesture fires.
- **`ShowContext()`**: guard on `IsLiked`; `ColorizeInstances()` plays each preview instance's
  shockwave in a `likedStaggerDelay` cascade, and `info.PlaceContextAt(anchors, contextHeightOffset)`
  floats each context label above its paired instance (instance i ↔ block i) and snaps it to
  face the user. Called via `PlantManager.ShowContextSelected()`.
- **`Hide()`**: stops the show coroutine, stops audio, fades the poem to 0 + `HideContext()`,
  **destroys the grey preview instances**, resets splats to greyscale. Safe to call on a
  non-shown plant.
- **`Like()`**: sets `m_liked`, hides + deactivates the label object, disables `selectionCollider`,
  `ForceColorSpawned()` (colours any preview liked without context), then spawns **another N**
  (`scatterer.Spawn(N, existing)`) and `RevealLikedInstances()` to double the spread. Audio keeps playing.
- **`RevealLikedInstances()`**: activates each copy one at a time — sets active, parks every child
  `GsplatShockwaveAnimator` to greyscale and `Play()`s it the same frame (no colour flash), then
  waits `likedStaggerDelay` before the next, so the doubled batch cascades in.

## Inspector wiring (per scene)

1. One **PlantManager** GameObject. Fill **Rounds**: each round = the list of (disabled)
   Plant references for that iteration. Leave plants disabled in-editor (or rely on
   `autoDisableAllOnStart`).
2. Wire the like gesture's `SelectorUnityEventWrapper._whenSelected` →
   `PlantManager.LikeSelected()`, and the context ("IndexUp") gesture's
   `SelectorUnityEventWrapper._whenSelected` → `PlantManager.ShowContextSelected()`
   (both `ContextLeft/IndexUp` and `ContextRight/IndexUp`).
3. Per Plant: assign `info` (PlantInfo), `splats`, `audioSource`, `selectionCollider`,
   `plantData`, and a `scatterer` (PlantInstanceScatterer). The spread count is driven by
   the plant's context-block count (scatterer `count` is ignored on these paths), so the
   number of `contextInfos` on the `PlantData` sets how many instances appear. Tune
   `likedStaggerDelay` for the cascade gap and `contextHeightOffset` for how high the
   context labels float above their instances. On `PlantInfo`, wire `contextLabelRoots`
   parallel to `contextLabels`; each root needs its own `LookAtTarget` → `CenterEyeAnchor`
   (`runOnStart`/`runOnUpdate` off — it is snapped by `PlaceContextAt`).
4. Each Plant's child trigger collider has a **PlantTouchTrigger** with its `plant`
   reference set and `handLayers` / `handTag` configured.

## Extending this

- **More rounds / tutorial round**: just add entries to the Rounds list; a single-plant
  round works (select + like + advance all handle count 1).
- **Auto-spawn liked copies** (currently manual `likedInstances`): replace the loop
  in `RevealLikedInstances()` with instantiation around a set of anchor points.
- **Gesture navigation** (next/prev instead of touch): add input that calls a new
  manager method; `Select` already centralizes the show/hide swap.
- **Populating labels**: each `PlantInfos` holds a poem `PlantLabel` + a list of context
  `PlantLabel`s, placed by hand from `Label.prefab`. `PlantInfo.SetData` maps
  `PlantData.contextInfos[i]` → `contextLabels[i]` by index, so add as many context labels
  as the species has context infos. Retune the text↔background depth gap once on
  `Label.prefab`'s Background child to change every label.
- **Global vs scene singleton**: `PlantManager` is a plain scene singleton (no
  DontDestroyOnLoad). If the experience spans scenes, promote it or persist round state.

## Gotchas

- Touch-to-select only; there is no cycle/navigation gesture.
- Audio plays through the per-Plant `AudioSource` (Spatial Audio building block), but the
  **clip lives in `PlantData.audioClip`** and is auto-assigned by `Plant.AssignAudioClip`.
  `PlayOnAwake` is off; `Show()` drives `Play()`.
- A liked plant ignores `Show`/`Hide`/`Select`/round deactivation by design.
- `PlantManager` is required for the round flow; `PlantTouchTrigger` falls back to
  `plant.Show()` directly if no manager exists (single-plant testing).
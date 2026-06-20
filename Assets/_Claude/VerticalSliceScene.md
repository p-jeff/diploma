# Vertical Slice Scene

A short-presentation cut of the garden experience: start with **three plants**, let the
visitor explore them, **like** at least one, then **sit in the chair** to make the **whole
garden bloom at once**. It is **not** a special mode — it's the normal `ExperienceManager`
with a few explicit, visible inspector overrides, so the editor shows exactly what the slice does.

> **Flow note (2026-06-20):** the bloom is triggered by **sitting in the chair**
> (`ChairSit` → `ExperienceManager.Sit()` → `StartFlourish`), **not** by the like count —
> `flourishAfterLikes` was removed. Liking only commits the plant + `UnlockNextBatch()`, and in
> the slice it also **locks hero touch** (see *Touch lock after the first like* below) so
> accidental hand-brushes can't re-select plants on the way to the chair. See the
> `chair-finale-sit` design note for the seat finale.

- **Scene:** `Assets/_Projects/_Scenes/VerticalSlice.unity` (duplicated from
  `Experience.unity`, so it inherits the camera rig, passthrough, hand-tracking, scene-lock
  and managers). The slice roster is **9 species** — Bamboo, Hemp and Fig_Tree were trimmed.
- **Branch:** `midterm`. **Not yet headset-verified.**

---

## The flow

1. **Set up the space** with the existing anchor lock (grab the wireframe box, poke
   **LOCK IN**). Unchanged — see `SceneLockSystem.md`. Locking enables `Content`, which runs
   `ExperienceManager.Start()`.
2. **Three heroes appear:** Lavender, Rhododendron (`Rhododentron` GO), Date_Palm — these are
   **batch 0**. The other six species (batch 1) stay inactive.
3. **Explore one:** touch a hero → the normal per-plant flow runs (poem, preview spread, and
   growing previews reveals **that plant's own** context labels — its `PlantData.contextInfos`
   — plus its 180° environment). **Switching** between the three works as before.
4. **Like a hero → it commits, the rest unlocks, and hero touch LOCKS:** the liked plant stays in
   the world, the other six species (batch 1) activate, and **all hero touch is disabled from here
   on** (`TouchLockedByFirstLike`) so brushing a plant on the way to the chair can't re-select it.
   The chair's "take a seat" invite appears (`ChairSit.requireLikeBeforeInvite` — gated on ≥ 1 like).
5. **Sit in the chair → the whole garden blooms:** `ChairSit` detects the seated head and calls
   `ExperienceManager.Sit()` → `StartFlourish()`. The three heroes bloom first, then the other six
   species rise from the ground and colourise. A quiet bloom — no context text appears.
6. **Explore the bloomed garden (gaze):** the existing gaze interaction — look at a specific splat
   **instance** and do the context gesture; it floats the owning plant's poem text + **the one
   context bound to that instance**, held for `replayHoldDuration` then faded (silent — no spoken
   poem). Gaze a **different instance** to read a different context — one context per instance, not
   the plant's whole group. Each species bloomed one instance per context, so the whole garden is
   readable by wandering it. (Gaze never routes through `Touch()`, so it stays live despite the
   touch lock.)

---

## How the slice differs from `Experience.unity`

There is **no `presentationMode`** and **no global story**. The slice is the same
`ExperienceManager` with three visible overrides:

| Field (Experience Manager) | `Experience.unity` | `VerticalSlice.unity` |
| --- | --- | --- |
| `bloomWholeRosterOnFlourish` | false | **true** (blooms the whole roster, not just liked plants; this same flag also gates the slice's touch-lock-after-first-like) |
| `unlockBatches` (= the roster) | 12 species, staged | **batch 0** = 3 heroes, **batch 1** = the other 6 |
| `flourishInstancesPerSpecies` | 4 | 2 (sparser scatter) |

> `flourishAfterLikes` no longer exists — flourish is **sit-triggered** (`ChairSit` → `Sit()`) in
> both scenes; likes only `UnlockNextBatch()`.

The **roster is `unlockBatches`** — a single, ordered, visible source of truth. No runtime
`FindObjectsByType`, no hidden mode. `AllRosterPlants()` flattens the batches (batch order)
for the whole-roster bloom; `LikedPlants()` is the staged-garden subset.

### Flourish behaviour (`ExperienceManager`)

- **Trigger:** `ChairSit` (seated-head box) → `ExperienceManager.Sit()` → `StartFlourish()`.
  `LikeSelected()` only commits the plant + `UnlockNextBatch()`; it no longer flourishes
  (`flourishAfterLikes` removed). The chair only invites a sit after ≥ 1 like
  (`ChairSit.requireLikeBeforeInvite`).
- `StartFlourish()` → when `bloomWholeRosterOnFlourish` is **off** it hides the active, un-liked
  plants (staged finale); when **on** it keeps them (they're about to bloom).
- `FlourishRoutine()` picks `AllRosterPlants()` (whole roster) or `LikedPlants()`, then for each
  plant in batch order: activates it if inactive (rise from ground) and calls
  `Plant.BloomForGarden(count)` (whole roster) or `Plant.Flourish(count)` (liked-only).
  `flourishSpeciesStagger` paces the cascade.

After flourish, `Select()`'s existing `if (m_flourished) return;` guard drops straight into
gaze-explore — no extra wiring.

### Touch lock after the first like (slice only)

`ExperienceManager.Touch()` no-ops once `m_likedCount >= 1`, **gated on the vertical-slice marker**
`bloomWholeRosterOnFlourish` (the helper `TouchLockedByFirstLike =>
bloomWholeRosterOnFlourish && m_likedCount >= 1`). Effect: after the visitor likes their first
hero, **all** hand-touch is retired — both touch-a-preview-to-reveal-context and touch-a-hero-to-
select/switch — so accidental brushes can't re-select plants while they walk to the chair to sit.

- **Gaze is unaffected:** the post-flourish hover highlight and the gaze-explore gesture run through
  `Update()` / `ExploreGazed()`, never `Touch()`. So gaze still works after sitting.
- **Auto-resets:** `BeginGarden` / `ResetAll` zero `m_likedCount`, so the next run starts touchable.
- **Staged garden untouched:** `Experience.unity` has the flag **off**, so it keeps touch for its
  multi-like batch progression.
- This does **not** stop an accidental *like* (a hand-pose gesture, not a collision) — hardening the
  like gesture (cooldown / hold-to-confirm) is a separate, later fix.

### `Plant.BloomForGarden(int count, AudioClip sfx, float vol)`

Reveals a plant as part of the whole-garden bloom, reusing existing helpers:

- **Liked branch** (the kept plant): `ForceColorSpawned()` then `Flourish(count)`.
- **Unselected branch** (`BloomForGardenRoutine`): waits for any in-progress **sprout**, then
  `m_liked = true`, `EndIdle()`, `HideGlow()`, keeps the info object active (gaze-explorable)
  **without playing the poem audio**, `PlayAnimation()` to reveal the hero body, and scatters +
  reveals `count` instances with their colliders enabled as the post-flourish gaze targets.

`m_liked = true` on every bloomed plant is what makes the whole garden explorable via the
existing `ExploreGazed()` → `Replay()` path.

---

## Context content is per-plant

Each plant shows **its own** `PlantData.contextInfos` — there is no global story sequence (it was
tried and removed; see `ExperienceSystem.md` → "Context content is per-plant"). Pre-flourish you
grow a plant's own contexts on its previews; post-flourish gaze (`Replay`) re-floats them + the poem.

Post-flourish, exploration is **per-instance**: each bloomed species scatters one splat instance per
context (`Mathf.Max(instanceCount, OwnContextCount())`), and gazing a specific instance reveals just
**that instance's** single context (`Plant.Replay(gazedInstance)` — spawn index mod context count) —
not the plant's whole group. So you read the garden one beat at a time by looking at different
instances. The shared `PlantInfos.prefab` has **8** context slots (max authored is 6), so nothing
overflows.

---

## Scene configuration

On the **Experience Manager** object in `VerticalSlice.unity`:

- `unlockBatches`: **batch 0** = [Lavender, Rhododentron, Date_Palm]; **batch 1** = [Poppy,
  Crocus, Narcissus, Fern, Hibiscus, Pear_Tree].
- `bloomWholeRosterOnFlourish = true` (whole-roster bloom **and** the flag that gates the
  touch-lock-after-first-like)
- `flourishInstancesPerSpecies = 2`
- `flourishSpeciesStagger = 0.3`
- `likeEnableDelay = 3` (extra delay after the show animation before the like gesture arms, on top
  of the poem-completion gate)
- A `ChairSit` is wired in the scene — flourish is **sit-triggered** (the seat finale).

`gazeTargeter` is intentionally unassigned — `Awake()` auto-adds `GazeInstanceTargeter`.
Garden boundary is `SpreadCollider` (7×7 m, floor y = 0); heroes sit across the front at their
authored positions. Cluster them tighter for a more compact "three in front" start — bloom
behaviour is unaffected.

---

## Tuning levers

| Field | Effect |
| --- | --- |
| `flourishInstancesPerSpecies` | Scatter/bloom density per species. Lower if the headset hitches. |
| `flourishSpeciesStagger` | Delay between each species blooming (cascade speed). |
| `replayHoldDuration` (on each `Plant`) | How long the silent poem text + grown contexts hold during explore. |

---

## Testing

**Headset (natural path):** lock the space → confirm **all three** heroes are touchable (cold-start
regression — see below) → touch / explore / switch → like one (hero touch now **locks**) → **sit in
the chair** → the whole garden blooms → gaze + context gesture to read any plant you explored. Watch
for a frame hitch at the bloom instant; if it hitches, raise `flourishSpeciesStagger` and/or lower
`flourishInstancesPerSpecies`.

**Desktop (no lock-in):** enable the `Content` object in play mode to start the experience,
then drive it from the **Experience Manager** right-click **Debug** menu (**Debug Select Next**
→ **Debug Like**) to fire the whole-garden bloom without gestures.

**Regression:** `bloomWholeRosterOnFlourish` defaults to `false` in `Experience.unity`, so the full
staged experience is unchanged — including touch (the touch-lock is gated on that flag, so it only
applies in the slice).

**Cold-start touch regression (fixed 2026-06-20, headset-verified):** all three heroes must be
touchable on the **first** run. A sprout interrupted by `BeginGarden`'s deactivate→reactivate used
to leave `selectionCollider` disabled (then re-captured as off), so the heroes sprouted + glowed but
were untouchable. Fixed by `Plant.RestoreSproutPose()` restoring the collider on interrupt — see
`ExperienceSystem.md` → Gotchas. If heroes ever go untouchable on cold start again, suspect this.

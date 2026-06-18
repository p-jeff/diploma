# In-Headset Testing Checklist

Complete flow verification for the Experience System. Run through sequentially; each checkpoint tests a specific part of the interaction.

## Setup

- Build and deploy to Meta Quest.
- Start in the main scene (Experience.unity).
- Have Inspector accessible for on-the-fly tuning (or use ContextMenu methods for iteration).

## Checklist

### Phase 1: Idle & Selection

- [ ] **1.1** Launch. Poppy is visible and idle-shimmering (half-grey with faint sparkle). No other plants visible.
- [ ] **1.2** Hold hand over Poppy. Sparkle intensity increases (hand proximity); dismiss hand. Sparkle fades back.
- [ ] **1.3** "Touch Me" prompt visible above Poppy, floating 0.8m up. If felt too close to face, tune `TouchPrompt.offset` (try reducing Y from 0.8 to 0.5).
- [ ] **1.4** Touch Poppy's collider. Audio plays (Poppy has a poem clip), poem label fades in at plant height, Poppy splat reveals with shockwave animation. N grey preview instances (N = context block count) appear scattered around.
- [ ] **1.5** Prompt disappears. Crocus and Narcissus unlock only after the first like (not on first touch). They remain invisible until Poppy is liked.

### Phase 2: Context Grow (Proximity + Gesture)

- [ ] **2.1** Step toward one of the grey preview instances. When horizontal distance from head drops to `revealRadius` (default 0.9 m), it auto-reveals with a colour shockwave and its context label floats above it. No gesture needed.
- [ ] **2.2** Alternatively, perform the context gesture (IndexUp on either hand). The ungrown instance NEAREST to your head (by world distance, no gaze required) reveals the same way.
- [ ] **2.3** If nothing reveals on gesture: confirm there are ungrown instances remaining and a plant is selected. Check `revealRadius` is not 0 for proximity, and `contextSelectorObjects` are active for gesture.
- [ ] **2.4** Perform context gesture again (or step to another grey instance). It reveals and its label appears. Previous label stays visible.
- [ ] **2.5** Can grow multiple instances in one species. Labels stack without overlapping if `contextHeightOffset` tuned right (default 0.6m).

### Phase 3: Like & Completion

- [ ] **3.1** Wait for poem audio to finish after Show animation. Like gesture only enables once audio stops (audio-completion gate; `likeEnableDelay` default is 0 — no extra timer on top).
- [ ] **3.2** Perform like gesture (hand pose on either hand, wired to RightSmallHeart/LeftSmallHeart). Poppy is marked liked; poem label fades out (info stays active — labels remain visible).
- [ ] **3.3** Grey ungrown instances do NOT fade out on like. They remain in the world (liked plants keep all their instances). Only the poem label and context labels fade; the plant itself stays visible and explorable.
- [ ] **3.4** All instances (grown and ungrown) stay put. Grown (coloured) instances remain; ungrown grey instances also remain — no destruction on like.
- [ ] **3.5** Species counter: Poppy → 1 liked.
- [ ] **3.6** Touch Crocus. Show runs as before (new poem, new preview scatter). Like is re-gated until animation + delay finishes.
- [ ] **3.7** Touch Narcissus (don't like Crocus). Crocus' grey previews are destroyed when it is hidden (deselected), but grown (coloured) instances stay. Batch 2 (Lavender, Bamboo) only unlocks on the next like, not on this touch.

### Phase 4: Unlock & Progression

- [ ] **4.1** Touch + Like 4 more species (any combination). Count reaches 5 liked.
- [ ] **4.2** Each time a new species is liked, another batch of 2 plants unlocks (Lavender+Bamboo → Hemp+Rhododendron → Date_Palm+Fern → Fig_Tree+Hibiscus → Pear_Tree + (unused slot)).
- [ ] **4.3** After liking 8 species total: Flourish triggers.
  - [ ] All un-liked, active plants hide and deactivate instantly.
  - [ ] Touch prompt hides.
  - [ ] For each liked species (up to 8), spawn 4 extra instances and reveal with staggered cascade (1s apart by default).
  - [ ] All species' grown instances visible; extra instances add density.
  - [ ] Experience "locks" (no more selection, no more gestures).

### Phase 5: Label & UI Placement

- [ ] **5.1** Poem label sits above plant root (where?). If overlapping scene geometry or too close to face, tune `PlantInfo` transform position.
- [ ] **5.2** Context labels float above proximity/gesture-grown instances. If too high/low, tune `Plant.contextHeightOffset` (default 0.6m). Labels appear above whichever instances have been grown.
- [ ] **5.3** Labels billboard to face user always (LookAtTarget components snapping on PlaceContextAt).
- [ ] **5.4** If poem audio level too loud/quiet, tune `AudioSource.volume` on the plant GO.

### Phase 6: Gesture Timing & Gating

- [ ] **6.1** Immediately after Select (touch), try context gesture: no effect (selectors disabled). Wait for show animation to finish. Context selectors then enable; context gesture works. Like gesture additionally waits for poem audio to finish.
- [ ] **6.2** If context gesture enables too late: `likeEnableDelay` (default 0) adds an extra hold after animation. Keep it at 0 unless you need a forced buffer.
- [ ] **6.3** Like gesture disabled until animation + delay. Try smashing like button: no double-like.

### Phase 7: Scatter & Instance Placement

- [ ] **7.1** Preview instances (on Select) scatter around plant in a bounding box. If too tight or off-ground, check `PlantInstanceScatterer` bounds on the prefab.
- [ ] **7.2** Instances roughly at eye level or slightly below (z ~= 0 to -0.5m relative to plant base).
- [ ] **7.3** If an instance clips into the ground or floats high, ScatterBounds box position/scale needs tuning per plant.

### Phase 8: Fade-Out Behavior (Opacity)

- [ ] **8.1** Ungrown instances fade: watch opacity lerp from opaque → nearly transparent (0.002, not 0.0). Should be smooth over ~1.5s.
- [ ] **8.2** If instances vanish instantly: `instanceFadeOutDuration` is very small or 0. If they flicker, check that `GsplatInstanceFader` is not setting opacity to exactly 0.0 (it stops at 0.002).

### Phase 9: Audio Scaffolding

- [ ] **9.1** Poem plays on Select. If silent: check `PlantData.audioClip` is assigned (only Poppy & Narcissus have clips; others silent by design).
- [ ] **9.2** Selected/Liked SFX optional: if `ExperienceManager.selectedSfx` / `likedSfx` are empty, no beep. Assign clips if desired.
- [ ] **9.3** Like is only allowed after poem finishes (audio-completion gate). `LikeCommit()` then calls `audioSource.Stop()` defensively to ensure nothing keeps droning. Poem should already be done by the time a like is possible.

### Phase 10: Special Cases & Edge Cases

- [ ] **10.1** Fern splat placeholder: select Fig_Tree, Hibiscus, Narcissus, or Pear_Tree. Visual is Fern model (WP-C limitation). Confirm visual consistency acceptable for prototype.
- [ ] **10.2** After flourish: no new selections possible. UI locked. Scene stable.
- [ ] **10.3** DebugSelectNext/DebugGrow/DebugLike/DebugForceFlourish work in editor or via Inspector ContextMenus (headset + remote desktop, or editor test).

## Known Limitations & Tuning Guide

### Context Gesture Grows Wrong Instance
- The gesture always grows the nearest ungrown instance to the head. Move physically closer to the instance you want; there is no gaze targeting.
- If nothing grows: check that a plant is selected and ungrown instances exist.

### Selectors Gated Too Long
- Like gate is audio-completion based. If audio is short or silent, like becomes available quickly.
- `ExperienceManager.likeEnableDelay` (default 0) adds an optional extra delay on top of audio-completion. Increase it only if you need to force a minimum dwell time.

### Labels Too Close / Far from Face
- Tune `TouchPrompt.offset` Y (default 0.8m above anchor). Try 0.5–1.2m.
- Tune `Plant.contextHeightOffset` per plant (default 0.6m). Try 0.3–1.0m.
- Context labels appear above grown instances. If none appear, confirm the grow was triggered (proximity or gesture) and `contextHeightOffset` is not 0.

### Instance Scatter Degenerate (All Clustered)
- Check all 12 plants' `scatterer.ScatterBounds` boxes are non-null and sized reasonably (e.g. 2–4m cube).
- If bounds missing on a prefab, assign from the editor or Inspector "Regenerate Bounds" if available.

### Fern Placeholder Visual
- Expected: Fig_Tree, Hibiscus, Narcissus, Pear_Tree show Fern splat model.
- Acceptable for user test; real models needed pre-shipping.

### 9 Species Silent
- Expected: Crocus, Lavender, Bamboo, Hemp, Rhododendron, Date_Palm, Fern, Hibiscus, Pear_Tree have no poem audio.
- Poppy and Narcissus only species with audio (WP-A note). Silent by design; no bug.

### Opacity Flicker on Fade
- Shader treats `_GsplatOpacityMul ≤ 0.0001` as 1.0 (full opacity).
- `GsplatInstanceFader` stops at 0.002 to avoid quirk. Fade should be smooth; if jerky, check `instanceFadeOutDuration > 0`.

## End-of-Day Checklist

- [ ] All 8 likes trigger flourish ✓
- [ ] Grown labels persist; ungrown fade ✓
- [ ] Context grow works (proximity auto-reveal + nearest-distance gesture ✓)
- [ ] Gesture gating feels right (not too eager, not too locked) ✓
- [ ] Scatter placement sensible (no clipping, even coverage) ✓
- [ ] Audio on/off (expected silent plants silent) ✓
- [ ] Batch unlocks at correct moments ✓
- [ ] No crashes, no soft-locks ✓

**Log session tuning notes** (gaze cone, prompt offset, delays, scatter bounds per plant) for next iteration.

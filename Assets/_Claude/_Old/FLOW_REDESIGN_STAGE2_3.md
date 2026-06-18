# Flow Redesign — Stages 2 & 3 (record)

Builds on Stage 1 (see `FLOW_REDESIGN_STAGE1.md`). Verified: whole project compiles,
`Custom/URP/GroundGlow` shader imports, console error-free.

## New behaviour

**Stage 2 — proximity reveal + gesture recall**
- **Proximity reveal (the "ask"):** while a plant is the hero, physically stepping
  within `revealRadius` (horizontal distance from the head) of one of its grey context
  previews grows it — colour reveal + context label + 180° environment moment. No
  gesture needed for the first reveal. Driven in `ExperienceManager.Update` →
  `GrowInstanceWithContext`.
- **Gesture = recall:** the context gesture now calls `RecallTargeted` (was
  `GrowTargeted`). Gaze at an already-grown instance (across liked plants or the hero)
  and gesture to bring its context label + environment back up. Stays available while
  walking around, including after a like and after the flourish.
- The recall gesture is kept enabled after a like (only the *like* gesture is retired);
  like is still gated until the poem audio finishes (Stage 1).

**Stage 3 — hero highlight + mesh-collider-aware scatterer**
- **Hero ground glow:** `HeroGlow.cs` (auto-added by `ExperienceManager`) draws a soft
  additive disc on the ground under the selected plant. Self-contained: generates its
  own quad + material from the new `Custom/URP/GroundGlow` shader at runtime (no scene
  setup needed). Fades in on select, out on like/deselect.
- **Gaussian recolour:** `Plant.SetHeroTint` washes the hero plant's own gaussians
  toward the glow colour via the shader's `_GsplatTintColor` (a fragment multiply,
  independent of the reveal animator). Cleared in `Plant.Hide`/`LikeCommit`.
- **Mesh-collider scatterer:** `PlantInstanceScatterer` gained an optional
  `boundsCollider` (+ `boundsColliderMargin`). When assigned, copies scatter across that
  collider's world-AABB footprint (e.g. a plant's mesh `selectionCollider`) instead of
  the hand-placed `BoxCollider`. Existing prefabs (BoxCollider `bounds`) are unchanged.

## New / changed files
- `Assets/_Projects/_Scripts/Plants/Experience/HeroGlow.cs` (new)
- `Assets/_Projects/_Scripts/Shaders/GroundGlow.shader` (new)
- `Assets/_Projects/_Scripts/Plants/Experience/ExperienceManager.cs`
- `Assets/_Projects/_Scripts/Plants/Plant.cs`
- `Assets/_Projects/_Scripts/Plants/PlantInstanceScatterer.cs`

## Tuning knobs (ExperienceManager inspector)
- `revealRadius` (0.9 m) — how close you step to auto-reveal a context preview. 0 = off.
- `heroGlowColor` (light cyan) — ground glow + gaussian tint colour.
- `heroGlowRadius` (0.6 m) — glow disc radius.
- `heroTintStrength` (0.25) — how strongly the hero's gaussians take the glow colour.
- HeroGlow component: `fadeDuration`, `heightOffset`, `softness`.

## Works automatically (no scene wiring)
Hero glow (auto-added), gaussian tint, proximity reveal (uses the existing gaze head),
recall gesture (re-uses the existing context gesture wrappers).

## Needs editor adoption (optional / when ready)
- **Mesh-collider scattering:** assign each plant's mesh `selectionCollider` to its
  scatterer's `boundsCollider` to switch that plant off the hand-placed box. Until then
  it uses the existing box bounds.
- **Selector lists:** recall-after-like assumes `likeSelectorObjects` and
  `contextSelectorObjects` are *distinct* GameObject lists in the scene (so disabling
  like doesn't disable recall). Verify in the ExperienceManager inspector.

## In-headset verification checklist
1. Touch a plant → ground glow appears, gaussians take a subtle glow tint, poem plays.
2. Like is unavailable until the poem ends; liking then reveals the next plants, stops
   audio, drops the glow, leaves the plant in place. **No stall, no droning.**
3. Walk up to a grey context preview → it grows (label + environment).
4. After moving on, gaze at a grown instance + gesture → its context returns.
5. Do this for 8 likes → garden flourishes.

## Stage 4 — hand-reactive gaussians (touch interaction)
As a hand nears a plant's selection collider, the plant's own gaussians lean toward
the hand (a gentle "parting/brushing" feel). Per-splat: only gaussians within the
influence radius of the hand move, and the whole effect fades in with hand proximity.

**Changed files**
- `Packages/wu.yize.gsplat/Runtime/Shaders/Gsplat.shader` (embedded/customized package):
  - new uniforms `_GsplatHandCenter` (float4), `_GsplatHandRadius`, `_GsplatHandStrength`.
  - new per-splat displacement in the UNCOMPRESSED vertex path (next to the shock
    displacement): splats within `_GsplatHandRadius` of `_GsplatHandCenter` move toward
    it by `ht*ht * _GsplatHandStrength` (positive = attract). No-op when strength 0.
  - **NOTE:** this is outside `Assets/`. A `git stash` will also stash it — re-apply when
    re-running. (The 3 shader warnings about uninitialized center/corner/color +
    signed/unsigned are pre-existing gsplat-library warnings, not from this edit.)
- `Assets/_Projects/_Scripts/Plants/HandProximity.cs` — added `TryNearestHand` (returns
  the nearest hand's world position + distance).
- `Assets/_Projects/_Scripts/Plants/Plant.cs` — `Update` now does one proximity query
  feeding both the shimmer and the new `ApplyHandAttraction`/`ClearHandAttraction`
  (sets the `_GsplatHand*` props per-renderer). Runs for any active, non-liked plant.

**Tuning knobs (Plant inspector → Hand Attraction)**
- `handAttractStrength` (0.03 m) — peak lean toward the hand. 0 = off.
- `handAttractRadius` (0.25 m) — per-gaussian influence sphere (larger = more of the
  plant reacts).
- Fade-in reuses the existing `proximityNear`/`proximityFar` (same range as the shimmer).

**Works automatically** — `HandProximity` is already a scene singleton with the hand
anchors wired (the idle shimmer uses it). No new scene setup.

## Deferred (separate session)
Fully dynamic main-plant layout via mesh colliders — see memory
`dynamic-garden-layout-idea`.

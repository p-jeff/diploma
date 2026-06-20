# Reveal-Build Throttle (anti-flourish-stutter)

**Date:** 2026-06-19
**Goal:** Kill the flourish/select stutter caused by many gsplat reveals building their morph
scratch on the same frame.

---

## The measured problem

Profiling a standalone Development build over Link (`ProfilerCaptures/diploma_2026-06-19_13-26-01.data`)
showed the flourish worst frame at **71.75 ms** (~14 fps), dominated by
**`GsplatRevealAnimator.Update() = 46.49 ms` of CPU self-time** on the main thread, plus a
`GC.Collect`. Rendering/GPU was a non-issue (~2–5 ms). It is **not** GPU-bound and **not** the morph
buffer pool — it is the per-instance morph **build**:

`GsplatRevealAnimator` (and the two-capture `GsplatSplatMorph`) runs, the first frame an instance is
active at progress < 1, an **O(n)-over-every-gaussian** CPU pass (`BuildSourceBuffers`) that allocates
managed arrays (→ GC) and uploads ~8 `GraphicsBuffer`s. One such build is ~7–8 ms. The flourish wakes
the whole roster, and overlapping per-species cascades land several builds on one frame → the spike.
(See `_Claude/GsplatMorphBufferPooling.md` — the pool added "dispose-on-settle → rebuild-on-activate",
so flourish rebuilds; pre-pool it was built once and kept.)

## The fix — a garden-wide per-frame build budget

`Assets/_Projects/_Scripts/Plants/RevealBudget.cs` — a static, frame-reset counter. Reveal cascades
**claim a slot before activating each fresh instance**, so at most `PerFrame` heavy builds *begin* per
frame across the whole garden, regardless of how the existing time-based staggers
(`likedStaggerDelay`, `flourishSpeciesStagger`) or asset uploads happen to align.

Key property: an instance waiting for a slot **stays inactive (invisible)** — it is never drawn at
full detail while it waits, so there is **no full-detail flash** and **no shader/opacity changes**.
Only frame pacing under load changes, not the reveal's look.

### Why gate at activation, not inside the animator
`GsplatRenderer.OnDisable()` disposes its impl, so you cannot cheaply hide a deferred instance by
toggling `enabled`. And writing `_GsplatOpacityMul` from the animator would fight `Plant.SproutIn`'s
opacity fade. Keeping fresh instances **inactive until their slot** sidesteps both — the scatterer
already spawns clones inactive (`SetActive(false)`), so this is natural.

## Touch points (all in `Assets/_Projects/_Scripts/Plants`)
- **`RevealBudget.cs`** (new) — `static bool TryConsume()`, `static int PerFrame` (clamped ≥1, default 1).
- **`Plant.RevealLikedInstances`** (like / flourish / BloomForGarden cascade) — `while (!RevealBudget.TryConsume()) yield return null;` before `SetActive(true)`.
- **`Plant.SpawnPreviewInstances`** (on-select) — now spawns + registers synchronously, then activates
  via new gated coroutine **`Plant.ActivatePreviewInstances`** (fixes the smaller on-select hitch too).
- **`ExperienceManager`** — serialized `revealBuildsPerFrame` (Min 1, default 1) → sets
  `RevealBudget.PerFrame` in `Awake` + `OnValidate` (live-tunable).

## Not covered (by design)
- **Hero bodies** build when their plant root is `SetActive` (flourish activates not-yet-unlocked
  species). They are already 1 s-staggered (`flourishSpeciesStagger`) and masked by `SproutIn`'s
  near-zero opacity, so they neither pile up nor flash.
- The throttle **spreads** builds; it does not make a single build cheaper. If one species' build alone
  blows the budget, that's the separate option #2 (cache / GPU-derive the start-state — see
  memory `gsplat-flourish-cpu-spike`).

## Tuning
Default `revealBuildsPerFrame = 1` (smoothest). At 72 Hz that drains ~72 reveals/sec — a ~40-instance
flourish populates in ~0.5 s. Raise to 2 if reveals feel too slow to fill in *and* a single build is
comfortably under ~6 ms.

## Verification
Scripts recompiled clean in Unity 6000.3.10f1 (0 errors). **Not yet headset / build-profiler
verified** — recapture a flourish (build, Autoconnect Profiler) and confirm the
`GsplatRevealAnimator.Update` spike is gone / frame times stay under 13.89 ms.

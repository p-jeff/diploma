# Performance + Drift Fix Session — 2026-06-19

Investigation of "passthrough/scene stuttering over Link", which unfolded into a stack of
distinct problems. Recorded here so the chain of causes → fixes isn't lost. Hardware: RTX 5070 Ti,
Ryzen 7 3800, Quest over Link (ASW already disabled in Oculus Debug Tool).

## TL;DR — what ultimately worked (by impact)

1. **Removing `OVROverlayCanvas` was the big one.** It was the real regression (garden used to render
   10M gaussians fine; later choked + glitched at 3M). → flourish went from *no headroom + jumps* to
   **stable ~65 fps with ~20% HUD headroom, no jumps**. Tradeoff: slightly grainier label text (accepted).
2. **Freeze the spatial anchor after placement** → killed the "whole garden slightly drifts" at stable FPS.
3. **RevealBudget** (per-frame cap on gsplat morph-builds) → killed the flourish CPU spike.
4. **Sort handling**: throttle helped, but once the overlay (the real hog) was gone, **every-frame
   sorting fit budget again** (`gsplatSortRefreshRate = 1`) — no flicker, no sort cost problem.

## How we profiled (useful for next time)
- **Profile a standalone Development build** (Build Settings → Development + Autoconnect Profiler), not
  the Editor. In-editor captures were polluted by `EditorLoop` stalls (one frame hit 1280 ms) and
  `RenderPlayModeViewCameras`. The build streams to the Editor Profiler over localhost.
- The MCP `Unity_Profiler_*` reader tools threw on loaded captures. Workaround that worked: load the
  `.data` via `ProfilerDriver.LoadProfile`, then read frames with `ProfilerFrameDataIterator`
  (CPU self-time) and `ProfilerDriver.GetHierarchyFrameDataView(...).GetItemColumnDataAsFloat(id,
  HierarchyFrameDataView.columnSelfGpuTime / columnTotalGpuTime)` (GPU time, on the Main Thread).

## The problems & fixes, in the order found

### 1. Flourish CPU spike — `GsplatRevealAnimator.Update` (~46 ms in one frame)
Worst flourish frame was CPU-bound on the main thread: every gsplat instance builds its morph scratch
(O(n)-over-all-gaussians CPU loop + managed allocs → GC + ~8 GraphicsBuffer uploads) the first frame
it activates, and the flourish woke the whole roster at once.
**Fix:** `RevealBudget` (new static) — caps how many heavy reveal-builds may *begin* per frame
garden-wide; cascades claim a slot before activating a fresh instance (it stays inactive/invisible
until its turn). Wired into `Plant.RevealLikedInstances` + new `Plant.ActivatePreviewInstances`.
Tunable `ExperienceManager.revealBuildsPerFrame` (=1). Doc: `RevealBudgetThrottle.md`.

### 2. GPU bound — `SortGsplats` was ~72% of GPU
Not overdraw (`DrawProcedural` was 0.3 ms). The per-frame depth-sort of all bloomed splats
(`GsplatRenderer.SortMode.Always`, ~45 instances each sorted separately) was ~19 ms.
**Fix:** `GsplatSortThrottle` (new) sets renderers to `SortEveryNFrames`; `flourishInstancesPerSpecies`
4 → 2 (fewer instances to sort). **Caveat:** throttling stale-sorts gaussians → visible flicker
(discrete re-sort pops). Once #3 freed up the GPU, set `gsplatSortRefreshRate = 1` (every-frame) →
no flicker and still in budget. So the throttle is effectively *off* now; kept as a knob.

### 3. ⭐ `OVROverlayCanvas` — the actual regression
Poem/label text was rendered via Meta compositor-layer overlays for crispness. Cost: a per-frame
render-texture re-render **+ `TMP.ForceMeshUpdate()` every frame** (`LabelOverlayCanvas.LateUpdate`),
the compositor's hard **layer-count limit** once many labels were live (post-flourish glitches), and
**ASW mis-reprojecting** the layers. **3 were even baked into `VerticalSlice.unity`** (old play-mode
saves) — all on the **TitleSequence** labels (`TouchMeLabel`, `TitleCard`, `PoemCard`), which is why
the near-empty *title screen* dropped to 36 fps on head-turn.
**Fix (removed project-wide):** `PlantInfo.EnsureLabelOverlay` → no-op; `LabelOverlayCanvas` neutered
to a self-removing stub; new `OverlayCanvasStripper` ([RuntimeInitializeOnLoadMethod] + sceneLoaded)
destroys every `OVROverlayCanvas` on load and un-hides its canvas (Default layer); the 3 baked
components removed from `VerticalSlice.unity` and saved. Labels now plain world-space canvases.
Doc updated in memory `ovroverlaycanvas-poem-prototype`.

### 4. Whole-garden drift (slight shifts even at stable FPS)
`SceneLockController` parented `SceneRoot` under the `OVRSpatialAnchor` and followed it every frame;
MRUK `EnableWorldLock` was a second continuous re-localizer. Each tracking refinement nudged the
whole world. Confirmed by a debug bypass (`disableAnchorForTesting`).
**Fix:** `freezeAfterPlacement` (default ON) — anchor places the root (lock + restore), follows for
`freezeSettleSeconds` (=1s) to converge, then **detaches to its original parent** and freezes in
tracking space. Cross-session reproducibility preserved (anchor still saved/loaded). MRUK world-lock
now only in legacy follow mode. Doc updated in memory `scene-lock-system`.

## Files touched
New: `RevealBudget.cs`, `GsplatSortThrottle.cs`, `OverlayCanvasStripper.cs`.
Changed: `LabelOverlayCanvas.cs`, `PlantInfo.cs`, `Plant.cs`, `PlantInstanceScatterer.cs`,
`ExperienceManager.cs`, `SceneLockController.cs`, `VerticalSlice.unity`.

## Current tunable values
`gsplatSortRefreshRate = 1` · `flourishInstancesPerSpecies = 2` · `revealBuildsPerFrame = 1` ·
`freezeAfterPlacement = true` · `freezeSettleSeconds = 1` · `disableAnchorForTesting = false` (set OFF
for normal use).

## Don't-regress notes
- Don't reintroduce `OVROverlayCanvas` without first solving its per-frame + layer-count + ASW cost.
- Temporal gsplat sort-throttling flickers in VR — prefer every-frame sort + fewer/smaller splats.
- Profile builds, not the Editor.

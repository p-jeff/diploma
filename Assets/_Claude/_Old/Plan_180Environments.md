# 180° Environments — Implementation Plan

Status: **implemented** · Source: IDEAS.md · Implemented: 2026-06-12

## Context

- Gsplat package (`Packages\wu.yize.gsplat`) draws via `Graphics.RenderMeshPrimitives` (visible to all cameras) and on the Unity-6 RenderGraph URP path **re-sorts per camera inside each camera's render pass** (`GsplatURPFeature.RecordRenderGraph` → `DispatchSort(cameraData.camera)`, gated by `ComputeSortRequired` which stays true all frame with `SortMode.Always`). ⇒ A second fixed spectator camera sorts correctly; one PCVR instance suffices for the "Window" idea.
- Reusable fade pattern: `GsplatInstanceFader.cs` (coroutine lerp, `_GsplatOpacityMul` — never set ≤ 0.0001, snaps back to 1).
- Passthrough still works over Link via `OVRPassthroughLayer` ([BuildingBlock] Passthrough GO, `textureOpacity_` drivable 1→0).

## Idea 2 — 180° Environments

**Asset dependency (friend):** flat widescreen paintings. Recommend telling the artist: wide aspect (≥ 3:1, e.g. 6000×2000), since it wraps a ~180° arc; no projection warp needed.

**Data:** Add `public Texture2D environmentPainting;` (optional, default null) to `PlantLabelContent` in `PlantData.cs` — context entry `i` already index-matches spawned instance `i`, so the painting rides on the same association used by context labels. (Serialized-struct change: existing `.asset` files keep their values; new field defaults to null.)

**Display rig — new `EnvironmentCylinder`:**
- Procedurally generated partial-cylinder mesh (180° arc, radius ~3.5 m, height sized to cover vertical FOV at that radius, e.g. 4 m; UV-mapped flat across the arc). Generated once in `Awake` — no modeling needed.
- Unlit transparent material (`URP/Unlit` transparent or a 10-line shader), alpha-driven fade.
- Positioned at flourish/selection time: centered on the user's head XZ, arc facing the selected plant's direction; world-anchored (doesn't follow head rotation).

**New script `EnvironmentMoment.cs`:**
- Hook: `ExperienceManager.GrowTargeted()` → after `plant.GrowInstance(instance)`, look up the instance index → `plantData.contextInfos[i].environmentPainting`; if non-null, trigger the moment.
- Sequence (single coroutine, modeled on `GsplatInstanceFader`):
  1. Fade in: painting alpha 0→1 while `OVRPassthroughLayer.textureOpacity_` 1→0 (~2 s).
  2. Hold: serialized `holdSeconds` (default 12; if the context audio clip is playing, hold = clip remaining + tail).
  3. Fade back: reverse (~2 s).
- Interrupts: new species selection, `LikeSelected`, or a second environment trigger during hold → fade back early / crossfade. Keep one active moment max.

**Scene wiring:** one EnvironmentCylinder + EnvironmentMoment GO in `Experience.unity`; reference to the `[BuildingBlock] Passthrough`'s `OVRPassthroughLayer`.

## Verification

- Play-mode with a test painting on one Poppy context entry: grow that instance → passthrough opacity drops, cylinder fades in, holds, fades back; interrupt cases (select new species mid-hold).

## Open asset/hardware dependencies

- 180° paintings from friend (flat widescreen, ≥3:1 recommended)

## Decisions made

- 180° paintings delivered as **flat widescreen images** → curved partial-cylinder display.
- 180° environment timing = **brief immersion moment** (fade in, hold ~10–15 s while context label/audio plays, auto-fade back).

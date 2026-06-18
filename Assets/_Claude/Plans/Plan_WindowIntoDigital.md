# Window into the Digital — Implementation Plan

Status: planned · Source: IDEAS.md · Planned: 2026-06-11

## Context

- Gsplat package (`Packages\wu.yize.gsplat`) draws via `Graphics.RenderMeshPrimitives` (visible to all cameras) and on the Unity-6 RenderGraph URP path **re-sorts per camera inside each camera's render pass** (`GsplatURPFeature.RecordRenderGraph` → `DispatchSort(cameraData.camera)`, gated by `ComputeSortRequired` which stays true all frame with `SortMode.Always`). ⇒ A second fixed spectator camera sorts correctly; one PCVR instance suffices for the "Window" idea.
- Exhibition runs PCVR via Quest Link (user-confirmed), not standalone APK. So the PC has full `System.IO`, multi-display output, and PC-class GPU.

## Idea 4 — Window into the Digital (fixed spectator camera, one PCVR app)

Answer to the open question in IDEAS.md: **one PCVR instance handles it; no second computer / no networking needed.** Verified: `GsplatURPFeature` re-sorts per camera within each camera's render pass (RenderGraph path), and splat draws are camera-agnostic. Cost = one extra sort+draw pass on the PC GPU.

**Scene/setup:**
1. `SpectatorCamera` GO in `Experience.unity`: fixed pose framing the garden, `targetDisplay = 1`. Small bootstrap script: `if (Display.displays.Length > 1) Display.displays[1].Activate();` (Windows player; in-editor preview via Game view "Display 2").
2. Confirm all `GsplatRenderer`s use `SortMode.Always` (it's the field default in `GsplatRenderer.cs:79`; verify scene overrides).
3. Background: passthrough does NOT exist for this camera — by design it shows *only* the digital. Pick clear color / gradient skybox; the Idea-2 environment cylinder will naturally appear in this view too (nice synergy).

**Render filters:**
4. Duplicate `Assets\Settings\PC_Renderer.asset` → `Spectator_Renderer.asset`; add it to the URP asset's renderer list; set the spectator camera's "Renderer" to it. Filters live here as renderer features and/or a post-processing Volume on a spectator-only volume layer mask — fully decoupled from the headset view.
5. Filter design itself is an art pass (separate session): start with bloom + color grading via standard URP Volume, iterate.

**Performance note:** headset renders multipass stereo (2 eye passes) + spectator = 3 passes of splat sort/draw. Fine on a desktop GPU; profile once it's wired and drop `SortRefreshRate` on the spectator if needed.

## Verification

- Editor Game view set to Display 2 shows the fixed view with correct splat sorting (no flicker/wrong blend when both views render); then Windows player on a machine with a second display.

## Decisions made

- Spectator = **fixed camera, one PCVR app**, second display/projector (confirmed feasible, see gsplat fact above).

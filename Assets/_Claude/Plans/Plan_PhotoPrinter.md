# Photo Printer — Implementation Plan

Status: planned · Source: IDEAS.md · Planned: 2026-06-11

## Context

- Exhibition runs PCVR via Quest Link (user-confirmed), not standalone APK. So the PC has full `System.IO`, multi-display output, and PC-class GPU.
- Poems are **baked PNG sprites** (`PlantData.poem` → `PlantLabelContent { Sprite text, background }`), rendered via `PlantLabel` (UI `Image`, `SetNativeSize`, alpha fades). No string poem text exists. Assets in `Assets\_Projects\_Resources\TextCards\`.

## Prereqs: WP-0 + End Poem (see Plan_EndPoem.md)

## Idea 3 — Photo Printer (garden screenshot + end poem → synced folder → Mac prints)

Depends on WP-0 and Idea 1 (poem must exist to be on the postcard). PCVR ⇒ plain `System.IO` on the Link PC.

**New script `PostcardExporter.cs`:**
- Trigger: after the end poem has faded in (event from `EndPoemDisplay`), wait a beat (~2 s) so the garden + poem are fully visible.
- Capture: dedicated offscreen **postcard camera** (disabled `Camera` component, culling mask = garden layers + poem canvas layer) placed at the user's head pose (or a framed garden vantage), `targetTexture` = RenderTexture at print aspect (A6 landscape @300 dpi ≈ 1748×1240). Render once on demand — under URP use `UniversalRenderPipeline.SubmitRenderRequest` / enable-for-one-frame rather than legacy `Camera.Render()`; verify which works in this Unity 6 version during implementation.
  - Note: gsplat draws via `RenderMeshPrimitives` appear in any camera, and the per-camera sort covers this camera too — same mechanism as Idea 4.
- Encode & save: `ReadPixels`/`AsyncGPUReadback` → `ImageConversion.EncodeToPNG` → `File.WriteAllBytes(Path.Combine(exportFolder, $"garden_{DateTime:yyyyMMdd_HHmmss}.png"))`. `exportFolder` is a serialized path pointing at the cloud-synced folder (Dropbox/iCloud Drive folder on the Link PC). Wrap in try/catch — printing must never break the experience.
- Fallback toggle: also save to a local folder if the synced path is missing.

**Mac side (not Unity — put in repo under `Tools/postcard-printer/`):**
- Small watcher script (Python + watchdog, or `fswatch` + shell): on new `*.png` in the synced folder → `lp -d <printer-name> -o media=A6 <file>` → move to `printed/` subfolder.
- Plus a README with printer setup notes. Exact `lp` options depend on the postcard printer model (still to be chosen — placeholder).

## Verification

- Play-mode through flourish → PNG appears in the export folder at correct resolution with poem visible. Mac watcher tested separately by dropping a PNG into the synced folder.

## Open asset/hardware dependencies

- Postcard printer model choice + cloud-sync folder choice (Dropbox/iCloud)

## Decisions made

- Printer = **garden screenshot + the end poem**, saved by the Link PC to a **cloud-synced folder**; MacBook watches the folder and prints.

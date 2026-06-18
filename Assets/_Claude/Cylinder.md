# 180° Environment Cylinder — System Overview

**Status:** Implemented (2026-06-12)
**Source:** `IDEAS.md → Plan_180Environments.md`
**Scripts:** `EnvironmentCylinder.cs`, `EnvironmentMoment.cs`
**Data field:** `PlantData.environmentPainting` (species-level, set on `.asset` files)

---

## How It Works

When a plant is **selected** (touched), if its `PlantData` has an `environmentPainting` texture assigned, a half-cylinder fades in around the user's head displaying that image. The cylinder is positioned at the user's head height and faces their horizontal gaze direction.

### Sequence
1. **Select** plant → `ExperienceManager.Select()` calls `p.Show()`
2. Checks `p.Data.environmentPainting` — if non-null, calls `EnvironmentMoment.Trigger(texture, headPos, forwardDir, audioSource)`
3. **Fade in** (2s): cylinder alpha 0→1, passthrough opacity 1→0
4. **Hold**: lasts as long as the poem audio is playing (+1s tail), or 12s default if no audio
5. **Fade out** (2s): cylinder alpha 1→0, passthrough opacity 0→1
6. Cylinder GameObject is deactivated

### Interrupts
- New plant selection → interrupts current moment (fade-out immediately)
- Like gesture → interrupts current moment
- Second environment trigger during a moment → interrupts + starts new moment

---

## Scripts

### `EnvironmentCylinder.cs`
Procedural mesh + material. Attached to a child GameObject under EnvironmentMoment.

**Mesh:** 180° half-cylinder arc, configurable:
- `radius` (default 3.5m)
- `height` (default 4m)
- `segments` (default 32)

**Vertex alpha gradient:** Each vertex has a `Color32` where alpha fades from 0 (edges) to 1 (center) using `SmoothStep`. This makes the texture blend smoothly into transparency on the sides — no hard edges.

**Material:** URP Unlit, transparent, alpha driven via `_BaseColor.a` in `MaterialPropertyBlock`.

**Key methods:**
- `SetTexture(Texture2D tex)` — assign the painting
- `SetAlpha(float a)` — drive opacity (0–1) via MPB
- `PositionAt(Vector3 center, Vector3 forward)` — place at head position, face gaze direction (Y is preserved for head height)

### `EnvironmentMoment.cs`
Orchestrator. Sits on the same GameObject as `ExperienceManager` (auto-added in `Awake` if missing).

**Self-wiring:** In `Awake()`, if no `EnvironmentCylinder` child exists, creates one automatically.

**Timing:**
- `fadeInDuration` (2s)
- `fadeOutDuration` (2s)
- `defaultHoldSeconds` (12s)
- `audioTailSeconds` (1s)

**Key methods:**
- `Trigger(Texture2D, Vector3 center, Vector3 forward, AudioSource)` — start a moment
- `Interrupt()` — set interrupt flag; current coroutine checks it and bails out

**Passthrough control:** Uses reflection to find `[BuildingBlock] Passthrough` → `OVRPassthroughLayer.textureOpacity`. Fails silently if the component isn't present.

---

## Adding to a Plant

1. Open the plant's `.asset` file (e.g. `Assets/_Projects/_Resources/_PlantInfo/Lavender.asset`)
2. Add after the `audioClip` line:
   ```yaml
   environmentPainting: {fileID: 2800000, guid: <texture-guid>, type: 3}
   ```
3. The texture is placed in `Assets/_Projects/_Resources/180Images/`

### Current assignments
| Plant | Painting |
|-------|----------|
| Lavender | `lavender.jpeg` (guid: efa3e5c6db7e83d49a44ccdfcf0d6ae3) |
| Others | None (no environment moment) |

---

## Dependencies

- **URP Unlit shader** — must be available in the project (standard URP requirement)
- **OVRPassthroughLayer** — optional; passthrough dimming is skipped if not found
- **`Gsplat`** — cylinder is independent; no gsplat dependencies

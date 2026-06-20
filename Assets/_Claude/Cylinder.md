# 180° Environment Cylinder — System Overview

**Status:** Implemented (2026-06-12); parallax multi-layer added (2026-06-19, not yet headset-verified)
**Source:** `IDEAS.md → Plan_180Environments.md`
**Scripts:** `EnvironmentCylinder.cs`, `EnvironmentMoment.cs`
**Data fields:** `PlantData.environmentLayers` (parallax stack, preferred) + `PlantData.environmentPainting` (legacy single, fallback); both also exist per-context on `PlantLabelContent`. Set on `.asset` files.

---

## How It Works

When a plant is **selected** (touched), if its `PlantData` has an environment defined, a parallax stack of half-cylinders fades in around the user's head. Each `EnvironmentLayer` is its own painting/sprite wrapped on a cylinder at its own `radius`, so head movement reveals depth. The stack is centred at the user's head height and faces their horizontal gaze direction.

### Parallax layers
- **Data:** `PlantData.environmentLayers` is a `List<EnvironmentLayer>`; each layer = `{ texture, radius, width, hardEdges, verticalOffset }`.
- **Per-layer height nudge:** each layer's `verticalOffset` (metres) is added on top of `EnvironmentMoment.verticalOffset` (final Y = global + per-layer). Use it to raise/lower one band so layers overlap on the Y axis instead of all sitting flush on the floor.
- **Size in metres → radius is distance:** `width` is the painting's real-world width in metres, wrapped onto the cylinder at `radius` (arc angle = `width / radius`). Height is **derived from the texture's aspect ratio** (`width / aspect`), so the image is never distorted. Because the size is physical, increasing `radius` makes a same-`width` painting subtend a smaller angle — i.e. **radius reads as true distance** (farther = smaller on screen). There is deliberately **no** height knob — that's what makes distortion impossible. All layers keep their bottom edge on the floor (plus `verticalOffset`).
- **Inspector trap guard:** Unity zero-fills fields on inspector-added list elements (it skips C# field initializers). So `Configure` treats `radius <= 0` as 3.5 m and `width <= 0` as a full 180° wrap at that radius, and `hardEdges` defaults to `false` (= soft fade), meaning a freshly-added, untouched layer still renders sensibly.
- **Authoring:** order doesn't matter — `EnvironmentMoment` auto-sorts far→near (largest `radius` first) and pins each layer's `material.renderQueue = baseRenderQueue + order` (default `baseRenderQueue = 2900`) so concentric transparent cylinders blend deterministically. This is **below the gsplat plants' queue (Transparent/3000)**, so the diorama renders *behind* the plants and labels. The pin is essential: every cylinder is centred on the head, so its bounds centre is ~at the camera — URP's per-object transparent sort would treat it as the nearest object and draw it on top of everything. Raise `baseRenderQueue` above 3000 only if you want a layer in front of the plants.
- **Edges:** `hardEdges = false` (default) softly fades the left/right edges into transparency — good for a backdrop blending into passthrough. `hardEdges = true` keeps the edges and relies on the texture's own alpha — good for cutout sprites.
- **Art guidance:** opaque image for the farthest backdrop; transparent PNG cutouts for nearer layers so the layers behind show through.
- **Fallback:** if `environmentLayers` is empty, the system falls back to the single `environmentPainting` (rendered as a one-layer diorama at `EnvironmentMoment.defaultRadius`, 3.5m, full 180°).
- **Pooling:** `EnvironmentMoment` keeps a pool of `EnvironmentCylinder` children, grown on demand to the layer count; extras are deactivated. All active layers fade together.

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
- `PositionAt(Vector3 center, Vector3 forward)` — place with the bottom edge on the floor, face gaze direction
- `Configure(Texture2D tex, float radius, float width, bool hardEdges, int renderQueue)` — set this cylinder up as one parallax layer: derives the arc angle from `width / radius` and regenerates the mesh (height auto-derived from the texture aspect), sets hard/soft edges, and pins `material.renderQueue` to the passed value (default base 2900, below the gsplat queue)

### `EnvironmentMoment.cs`
Orchestrator. Sits on the same GameObject as `ExperienceManager` (auto-added in `Awake` if missing). **Now also serialized on the `Experience Manager` GO in `VerticalSlice.unity`** so its tuning knobs (esp. `verticalOffset`) are editable in the Inspector — `ExperienceManager.Awake` finds the existing one instead of adding a duplicate.

**Self-wiring:** In `Awake()`, if no `EnvironmentCylinder` child exists, creates one (pool index 0). Extra layers are pooled as additional children at runtime (`Environment Layer N`).

**Timing:**
- `fadeInDuration` (2s)
- `fadeOutDuration` (2s)
- `defaultHoldSeconds` (12s)
- `audioTailSeconds` (1s)
- `defaultRadius` (3.5m, used by the single-texture legacy path)

**Placement:**
- `verticalOffset` (0m) — raises (+) / lowers (−) **every** layer at once. Layers default to bottom-on-floor; use this to line the visible art up with the ground when a texture has empty space at its bottom.

**Key methods:**
- `Trigger(IReadOnlyList<EnvironmentLayer>, center, forward, AudioSource)` — start a parallax moment (skips null-texture layers; auto-sorts far→near)
- `Trigger(Texture2D, center, forward, AudioSource)` — legacy single-painting moment (wrapped as one layer at `defaultRadius`)
- `Interrupt()` — set interrupt flag; current coroutine checks it and bails out

---

## Adding to a Plant

1. Open the plant's `.asset` file (e.g. `Assets/_Projects/_Resources/_PlantInfo/Lavender.asset`).
2. Author layers in the Inspector under `Environment Layers` (preferred), or hand-edit the YAML:
   ```yaml
   environmentLayers:
   - texture: {fileID: 2800000, guid: <far-guid>, type: 3}
     radius: 8.0           # farther away
     width: 20             # metres; height follows the image aspect
     hardEdges: 0          # 0 = soft side fade (backdrop)
     verticalOffset: 0
   - texture: {fileID: 2800000, guid: <near-guid>, type: 3}   # transparent PNG
     radius: 4.0           # closer → same width looks bigger
     width: 20
     hardEdges: 1          # 1 = keep edges, use the PNG's own alpha
     verticalOffset: 0
   ```
   Or keep the single legacy painting (used only when `environmentLayers` is empty):
   ```yaml
   environmentPainting: {fileID: 2800000, guid: <texture-guid>, type: 3}
   ```
3. Textures live in `Assets/_Projects/_Resources/180Images/`.

### Current assignments
| Plant | Environment |
|-------|-------------|
| Lavender | single `lavender.jpeg` (guid: efa3e5c6db7e83d49a44ccdfcf0d6ae3) via legacy `environmentPainting` |
| Others | None (no environment moment) |

---

## Dependencies

- **`Custom/URP/EnvironmentCylinder` shader** (`EnvironmentCylinder.shader`) — transparent, multiplies texture α × vertex α × `_BaseColor.a`
- **`Gsplat`** — cylinder is independent; no gsplat dependencies

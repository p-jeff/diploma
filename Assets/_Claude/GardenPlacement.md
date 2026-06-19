# Dynamic Garden Placement — System Overview

**Status:** Implemented. Core engine 2026-06-16; edge/spacing/chair refinement + behind-anchor framing 2026-06-19 (confirmed working 2026-06-19; a dedicated headset pass on cone width / spacing feel is still worth doing).
**Source:** `IDEAS.md → "Placement Algo takes into account all plants"`
**Scripts:** `Plants/Garden/GardenPlacer.cs`, `Plants/Garden/GardenOccupant.cs`, `Plants/PlantInstanceScatterer.cs`
**Boundary:** the scene's `SpreadCollider` (VerticalSlice: 7×7 m box, floor y = 0).

---

## What It Does

`GardenPlacer` is a scene singleton that finds well-spaced, non-overlapping floor poses for plants and
their scattered copies, inside the garden boundary, clear of the user and the chair. It owns a shared
**occupancy registry** of every placed footprint (its convex mesh collider + the world pose it was
placed at), so the garden auto-grows without overlaps as plants bloom and scatter.

Overlap is tested by true 3D shape via `Physics.ComputePenetration`, so a small plant can sit near a
tree's trunk without colliding with the canopy above it. The registry stores the pose at placement
time (not a live transform), so a plant whose collider is later disabled (e.g. by `Plant.Like`) still
blocks new placements. `GardenOccupant` auto-releases a copy's reservation when it's destroyed.

The placement search is non-deterministic (random darts), which is why multiplayer **snapshots and
replicates** the host's poses rather than re-running the search — see `MultiplayerSpectator.md`.

---

## How a Pose Is Found (`TryFindRootPose`)

1. **Sample region** = the boundary's bounds, **inset by `footprintRadius + boundaryMargin`** so the
   *whole* plant — collider and the wider splat visual — stays inside, instead of the centre landing
   on the edge and the body spilling over. (Collapses to the centre line if the boundary is too small
   for that footprint.)
2. Throw `candidatesPerPlacement` random darts on the floor (XZ; the root keeps its authored height
   and base rotation, plus an optional random yaw).
3. Reject any candidate inside the **user keep-out** or **chair keep-out** (horizontal radii), or —
   when a behind-anchor bias is active — outside the framing cone (see below).
4. Reject overlaps against the registry (`ComputePenetration`, with a broad-phase radius cull).
5. **Score the clean candidates** and keep the best:
   `score = min(nearest, targetSpacing) − centerBias · distFromBoundaryCentre`
   - `min(nearest, targetSpacing)` rewards reaching the target gap but **saturates**, so it doesn't
     chase ever-larger gaps into the corners (the old "maximise nearest-neighbour" objective was
     corner-seeking and made the garden feel sparse and edge-hugging).
   - `− centerBias · distFromCentre` gently pulls placements inward off the edges.
6. If no clean spot exists, fall back to the least-overlapping candidate (one-time warning).

---

## Keep-outs

- **User** (`userKeepOut`, 0.6 m) — around the head (serialized `userHead`, else `Camera.main`).
- **Chair** (`chairKeepOut`, 0.9 m) — around the finale seat, so nothing spawns in the seat or its
  approach. The chair transform is auto-resolved from the scene's `ChairSit` (see `ChairSit` /
  chair-finale) and cached; resolved lazily because the chair is placed during scene setup.

Both are circular, horizontal (Y ignored).

---

## Behind-Anchor Framing

Some scattered copies deliberately spawn **behind the touched plant from the viewer's head**, so the
plant you touched reads as a foreground anchor with its kin receding behind it.

- **`GardenPlacer.BehindBias`** — a struct `{ active, anchor, coneDeg }`, built via
  `BehindBias.Behind(anchorWorld, coneDeg)`. `TryFindRootPose` takes it as an optional `bias`.
- The search runs **two passes** via a local `Search(requireBehind)`:
  - **Pass 1** keeps only candidates that are *inside a cone around the head→anchor axis*
    (`dot(normalize(cand − head), viewDir) ≥ cos(coneDeg)`) **and** *past the anchor*
    (`dot(cand − anchor, viewDir) ≥ 0`). So the copy is genuinely behind the plant from your view,
    not off to the side or in front.
  - **Pass 2** runs unconstrained as a graceful fallback if nothing clean fits behind (e.g. the plant
    is against the back edge of the boundary), so a copy is never forced into an overlap.
- Degenerate cases (no head, or the head sitting on the anchor) silently disable the bias.

Driven by **`PlantInstanceScatterer`**: it picks an even share of the copies to go behind —
`behindAnchorFraction` of them, spread across the spawn/reveal cascade (Bresenham pick, not clumped) —
and passes the parent `Plant`'s position as the anchor. The viewer is `GardenPlacer`'s resolved head.

**Caveat:** "behind" is locked at the **moment of touch**. Copies are world-anchored, so walking
around afterwards does not re-frame them. This is intended — it frames the reveal.

---

## Integration

- **`PlantInstanceScatterer.Spawn()`** routes every copy through `GardenPlacer`, reserves its
  footprint, and tags it with `GardenOccupant`. Footprint = the parent `Plant.SelectionCollider`.
- **`Plant`** reserves its own footprint on enable (`registerFootprint`, default on) and releases it
  on disable, so copies avoid all standing plants. The reservation happens at the *full* pose before
  the sprout, so copies never scatter onto a plant's final footprint (see `ExperienceSystem.md`).
- **Main-plant auto-placement is opt-in** (`Plant.autoPlaceInGarden`, default OFF → heroes keep their
  authored positions and only reserve a footprint). When ON, `Plant.PlaceInGarden()` repositions the
  whole plant. The edge/spacing/keep-out rules apply to auto-placed mains too; the behind-anchor bias
  is used only by the scatterer.

---

## Tunables

| Field (component) | Default | Effect |
|---|---|---|
| `boundary` / `boundaryObjectName` (Placer) | `SpreadCollider` | Garden footprint. Auto-found by name if unset. |
| `boundaryMargin` (Placer) | 0.15 m | Extra inset on top of footprint radius — raise if the splat visual still overhangs the edge. |
| `targetSpacing` (Placer) | 0.9 m | Desired gap between footprints. Lower = denser garden. |
| `centerBias` (Placer) | 0.15 | Pull toward the boundary centre (off the edges). |
| `candidatesPerPlacement` (Placer) | 32 | Darts per placement — more = better spacing, slightly more cost. |
| `overlapMargin` (Placer) | 0.05 m | Extra air the penetration test must clear between footprints. |
| `userKeepOut` (Placer) | 0.6 m | Radius around the head nothing spawns in. |
| `chairKeepOut` (Placer) | 0.9 m | Radius around the chair nothing spawns in. |
| `behindAnchorFraction` (Scatterer) | 0.35 | Share of copies placed behind the touched plant. 0 = off. |
| `behindConeAngle` (Scatterer) | 45° | Cone half-angle behind the plant. Narrow = stacked directly behind; wide = loose. |

---

## Dependencies / Gotchas

- All plant `selectionCollider`s must be **convex mesh colliders** for `ComputePenetration` (they are).
- `FootprintRadius` reads the mesh's local bounds × world scale, so it stays valid even when the
  collider is disabled (`Collider.bounds` reads zero while disabled).
- `MCP get_components` on plant colliders has crashed Unity before — diagnose collider issues by hand.

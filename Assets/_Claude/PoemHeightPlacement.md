# Poem Height Placement

_Status: implemented 2026-06-17. Added a manual-height override to the existing auto-derived poem
placement; not yet headset-verified._

## What it is

Where the poem canvas floats when a plant is selected. By default the height is **auto-derived from
the plant's selection collider bounds**; a new `Manual Poem Height` toggle lets you pin it to a
fixed height instead. The XZ position is always the bounds centre — only the Y changes.

## Placement modes (`Plant.poemPlacement`)

- **Above** — poem floats straight above the plant's top.
  Auto Y = `bounds.max.y + poemHeightOffset`.
- **Cylinder Around Center** — poem orbits a vertical cylinder around the plant's central axis,
  staying on the viewer-facing side (better for tall plants / tree trunks). Auto Y =
  `bounds.min.y + cylinderHeightOffset`, orbit radius = `cylinderRadius`.

## The manual override

When `manualPoemHeight` is **on**, the auto Y is replaced (for **both** modes) by:

```
Y = bounds.min.y + manualPoemHeightValue
```

i.e. `manualPoemHeightValue` metres **above the plant base** (the selection-collider bottom). It's
measured relative to the base, not absolute world Y, so it stays correct when plants are placed
dynamically at runtime by the GardenPlacer. XZ centring and the context-label top-lift are unchanged.

## Where it lives

| | |
|---|---|
| Script | `Assets/_Projects/_Scripts/Plants/Plant.cs` |
| Helper | `Plant.PlacePoem()` — single source of placement truth, called by `Show()` and `Replay()` (previously duplicated inline in both) |
| Drives | `PlantInfo.PositionPoem(worldPos)` (Above) / `PlantInfo.PositionPoemCylinder(axisPoint, radius)` (Cylinder) |

## Tuning knobs (on `Plant`, under "Poem Placement")

| Field | Default | Notes |
|---|---|---|
| `poemPlacement` | Above | Above vs. Cylinder Around Center |
| `poemHeightOffset` | 0.3 m | Above only: clearance above the collider top |
| `cylinderRadius` | 0.5 m | Cylinder only: orbit distance from the axis |
| `cylinderHeightOffset` | 1.0 m | Cylinder only: auto height above the base |
| `manualPoemHeight` | off | override the auto Y with a fixed height |
| `manualPoemHeightValue` | 1.0 m | manual height above the plant base; used only when the toggle is on |

## Notes / gotchas

- No-op without a `selectionCollider` or `info` object — `PlacePoem()` early-returns.
- Manual height applies to **both** placement modes; in Cylinder mode it replaces
  `cylinderHeightOffset`, in Above mode it replaces `bounds.max.y + poemHeightOffset`.
- Adding the fields changes the serialized layout, so saving marks affected scenes/prefabs dirty.

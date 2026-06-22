# Scene Lock System

_Last verified against code: 2026-06-17_

Aligns the whole virtual scene to the physical room before the experience starts, then
freezes it there persistently. Everything spatial lives under one root (`SceneRoot`); you
grab a room-sized wireframe box to slide/rotate that root onto your real space, then poke a
**LOCK IN** button to anchor it to a Meta spatial anchor and start the experience. The
placement is saved and auto-restored on the next launch, so the garden stays glued to the
room across sessions.

Generalises the old `Assets/_Projects/Old/ScanAlignment` `LockPosition` / `WorldLockController`
pair. The key upgrade: **the spatial anchor is the world-lock** (system drift-corrected), with
MRUK world-lock + recenter-disable applied on top as insurance.

## Hierarchy

```
SceneRoot                       ← the one "main Transform"; SceneLockController lives here
├─ Content                      ← disabled until lock; enabling it starts the experience
│   ├─ Poppy … Pear_Tree (12 plants)
│   ├─ SpreadCollider           (garden boundary)
│   └─ Experience Manager       (+ EnvironmentMoment — its Start() == the lock moment)
└─ CalibrationBox               ← shown while calibrating; disabled on lock
    ├─ Edge ×12                 (cyan wireframe ~7.5×8.5×2.5 m, CalibrationWire.mat)
    ├─ GrabHandle               (Grabbable → target = SceneRoot; kinematic RB; ISDK_HandGrabInteraction)
    └─ LockInButton             (world-space Canvas + Button → LockScene; ISDK_PokeCanvasInteraction)
```

**Stays in real space (NOT under the root):** `[BuildingBlock] Camera Rig`, `[BuildingBlock]
Passthrough`, `PCM` (PointableCanvasModule + EventSystem), `Like`/`Context` L+R (hand gesture
detectors), `HandProximity`, `[Hand Ready Cue]`, `Cube`. The player stays put — the virtual
world moves to meet the room.

## Components

| Object / Script | Role |
|---|---|
| `SceneLockController.cs` | The brain (on `SceneRoot`, Assembly-CSharp, no namespace). Owns the Restoring → Calibrating → Locked state, anchor save/load/erase, and the content/box visibility swap. |
| `SceneRoot` | The single root every spatially-placed object is parented under. Grabbing the box moves *this*; locking anchors *this* to the real world. |
| `Content` | Container for everything that switches on at lock. Disabled during calibration; `SetActive(true)` on lock — which is when Experience Manager's `Start()` runs, so the experience begins exactly at lock. |
| `CalibrationBox` | The calibration tool: wireframe room box + grab handle + Lock In button. `SetActive(false)` on lock. |
| `GrabHandle` | Meta `Grabbable` whose `_targetTransform` is rebound to `SceneRoot`, so grabbing the handle drives the whole root. Movement is constrained by **its own GrabFreeTransformer** (see below), not by the controller. |
| `LockInButton` | World-space uGUI Canvas + Button with a Meta poke interactable. `Button.onClick` → `SceneLockController.LockScene` (persistent listener). Poked by `HandPokeInteractor` / `ControllerPokeInteractor` via the scene `PointableCanvasModule`. |

## Flow

```
Start()
 ├─ Awake: boxGrabbable.InjectOptionalTargetTransform(sceneRoot)   ← grab moves the root
 └─ persistAcrossSessions && saved UUID parses?
      ├─ yes → RestoreAsync()
      │         ├─ LoadUnboundAnchorsAsync(uuid) → LocalizeAsync()
      │         ├─ new [SceneAnchor] GO + OVRSpatialAnchor; unbound.BindTo(anchor)
      │         ├─ AttachRootToAnchor → ApplyWorldLock → EnableContent
      │         └─ State = Locked          (any failure → BeginCalibration)
      └─ no  → BeginCalibration()
                ├─ content.SetActive(false); calibrationBox.SetActive(true)
                ├─ ovrManager.AllowRecenter = true; MRUK.EnableWorldLock = false
                └─ onCalibrationStarted.Invoke()

LockScene()   ← poke LOCK IN
 └─ LockRoutine()
      ├─ State = Locked; calibrationBox.SetActive(false)
      ├─ new [SceneAnchor] GO at sceneRoot pose + OVRSpatialAnchor; wait for Created (≤ anchorTimeout)
      ├─ AttachRootToAnchor (parent root under anchor, zero local) → ApplyWorldLock → EnableContent
      ├─ onSceneLocked.Invoke()
      └─ SaveAnchorJob: await SaveAnchorAsync(); on success → PlayerPrefs[anchorPrefKey] = Uuid

Recalibrate()  ← ContextMenu (or wire to a gesture)
 └─ EraseAnchorAsync(); PlayerPrefs.DeleteKey; detach root (keep world pose); BeginCalibration()
```

`ApplyWorldLock` = `ovrManager.AllowRecenter = false` (+ `MRUK.EnableWorldLock = true` if
`useMrukWorldLock` and an MRUK instance exists). `AttachRootToAnchor` parents `SceneRoot` under
the anchor with `worldPositionStays:false` then zeroes local pose, so on lock the content stays
where you placed it and on restore it snaps to the saved pose.

## Movement constraint

`SceneLockController` does **not** constrain the root's pose. The grab is shaped entirely by the
**Locked `GrabFreeTransformer`** on the box's grab interactable (configure yaw + floor-slide,
lock pitch/roll + Y there). An earlier LateUpdate clamp (+ `floorY`/`constrainToFloor` fields) was
removed on 2026-06-17 in favour of the transformer.

## Inspector Wiring (`SceneLockController` on `SceneRoot`)

| Field | Wired to / default | Notes |
|---|---|---|
| `sceneRoot` | `SceneRoot` transform | The moved/anchored root. |
| `calibrationBox` | `CalibrationBox` | Toggled with the calibration state. |
| `boxGrabbable` | `GrabHandle`'s `Grabbable` | Target rebound to `sceneRoot` in `Awake`. |
| `content` | `Content` | Enabled on lock. |
| `ovrManager` | `[BuildingBlock] Camera Rig`'s `OVRManager` | Recenter disabled on lock. Optional. |
| `useMrukWorldLock` | `true` | Also toggle MRUK world-lock when an MRUK instance is present. |
| `persistAcrossSessions` | `true` | If false, never save/restore — recalibrate every launch. |
| `anchorPrefKey` | `"SceneLock.AnchorUuid"` | PlayerPrefs key for the saved anchor UUID. |
| `anchorTimeout` | `6` (Min 1) | Seconds to wait for anchor create / localize before giving up. |
| `onCalibrationStarted` / `onSceneLocked` | — | UnityEvents for external listeners. |

`LockInButton/Button.onClick` → `SceneLockController.LockScene` (already wired, persistent).

## Persistence

- Uses `OVRSpatialAnchor` (create → `SaveAnchorAsync` → store `Uuid` in PlayerPrefs; restore via
  `LoadUnboundAnchorsAsync` → `LocalizeAsync` → `BindTo`).
- Requires **Anchor Support = Enabled** in `Assets/Oculus/OculusProjectConfig.asset`
  (`anchorSupport: 1`) — already set. No manifest edit needed.
- Anchors are **device-only**: in Editor Play mode the calibrate → grab → poke → enable-content
  flow runs, but save/restore only truly persist on the headset.

## Gotchas / to verify in headset

- **Anchors don't work in the Editor.** Test persistence on-device.
- **Grab handle is a child of the root it moves** (standard Meta "handle moves big object" pattern,
  but untested in this rig). If the grab ever feels unstable, check this first.
- **LOCK IN button** sits at local `(0, 0.7, 0)` on the box facing `+Z`. Reposition/rotate it so
  it's comfortably pokeable from where you stand to calibrate.
- **`meta_add_grabbable` chose the transformer.** Confirm it's the `GrabFreeTransformer` you want
  (Locked) and not a `OneGrabFreeTransformer`; swap if needed.
- **Recalibrate has no in-headset trigger yet** — it's a `[ContextMenu]` only. Wire it to a held
  gesture / hidden button if visitors need to re-place between sessions.

# Hand-Pose Cue Sprites (Like / Context gesture hints)

_Status: orientation + placement reworked and play-mode/headset-verified working 2026-06-20._

## What they are

Four floating icon sprites — one per hand for each of the two gestures — that hint the **Like**
and **Context** hand poses. Each is a world-space Canvas (UI `Image` + label) that **follows the
viewer's index fingertip**, floats just above it, faces the viewer, and **pops** (scale + particle
burst) when its pose is recognised (`HandPoseAnimation`).

| GameObject | Lives in | Sprite | Hand |
|---|---|---|---|
| `ContextLeft` / `ContextRight` | loose in `VerticalSlice.unity` | `_Resources/Sprites/hand.png` (reaching hand) | left / right |
| `LikeLeft` / `LikeRight` | instances of `_Prefabs/Like.prefab` | `_Resources/Gemini_Generated_Image_boulvmboulvmboul.png` (quill) | left / right |

The cue canvas itself (`Sprite.prefab` for Context; authored inside `Like.prefab`) is the same
shape: `Canvas` + `CanvasGroup` + **`LookAtTarget`** on the root, an `Image` + `Bobber` child, and a
`LabelText`. `HandPoseAnimation` (on the parent) only drives the pop/visibility; it does **not**
build anything.

## How they're positioned + oriented (the 2026-06-20 fix)

The cues used to inherit the hand's tumbling orientation and sit *on* / *below* the fingertip,
reading as tilted and "sideways." The working setup decouples them from the hand completely:

**`FollowTransform` (on the cue parent) — follow position only:**
- `target` = `…/RightInteractions|LeftInteractions/…/HandPokeInteractor/HandIndexFingertip`
- `followPosition = true`, **`followRotation = false`** — take the fingertip's *position*, never its
  rotation. (Inheriting rotation was the original "wonky"/tumbling cause; it copied the hand's
  roll/pitch onto the cue every `LateUpdate`, overriding the billboard.)
- **`worldSpaceOffset = true`** + `positionOffset = (0, 0.12, 0)` — float **0.12 m straight up** above
  the fingertip in **world** space, so the lift stays vertical no matter how the finger is tilted.
  - `worldSpaceOffset` is a **new field** on `FollowTransform.cs`: when set, `positionOffset` is added
    in world space (`target.position + offset`) instead of along the target's local axes
    (`target.TransformDirection(offset)`). Default `false` preserves old behaviour for other users.

**`LookAtTarget` (on the cue canvas) — upright billboard:**
- `rotateY = true`, **`rotateX = false`, `rotateZ = false`** → **Y-only (cylindrical) billboard**: yaws
  to face the viewer but stays upright; never pitches over when you look down at your hand.
- **`trackInUpdate = true`** → re-aims every frame as the hand moves (snap-on-enable alone goes stale
  on a moving hand). Safe now that nothing fights it (`followRotation` is off).
- `target` empty → resolves `Camera.main` = `CenterEyeAnchor`. `flipY = true`.

**Canvas roll must stay 0:** a baked **90° Z roll on the cue canvas** was the actual "facing
sideways" bug (`LookAtTarget` preserves Z, so it never self-corrected). The canvas `localEuler` X/Z
are now **0** (Y is irrelevant — `rotateY` overwrites it). Keep any art orientation on the **inner
`Image`**, not the canvas, so it can't corrupt the billboard.

## Art orientation knob

The two PNGs are drawn **diagonally** (the hand reaches ~45° up-left; the quill leans ~30° up-right),
and the inner `Image` is currently at `0°`. If you want the graphic to stand more vertical, roll the
**inner `Image` Z** (NOT the canvas) — roughly hand `+45°`, quill `−30°`. This rides under the
billboard and won't tilt the facing.

## Gotchas

- **Per-instance overrides mask the prefabs.** The scene instances carried their own overrides
  (`rotateX = 1`, the 90° canvas roll, and per-instance `positionOffset` incl. a stray `−0.10` on
  `LikeLeft` that put it *below* the hand). The base `Sprite.prefab` showed `rotateX:0` / no roll, so
  reading the prefab lies — **tune on the scene instances** (or reset the overrides).
- These cues only make sense in **play mode** — at edit time all four sit at the origin because the
  `HandIndexFingertip` targets are only tracked at runtime.
- Distinct from the **green/yellow hand outline** cue (`HandReadyCue`, see `LikeAvailableCue.md`) and
  the per-plant **touch-me** sprite (`TouchMePrompt`) — different systems.

## Tuning knobs

| Where | Field | Value | Notes |
|---|---|---|---|
| `FollowTransform` | `positionOffset.y` | `0.12` | metres above the fingertip (world-up) |
| `FollowTransform` | `worldSpaceOffset` | `true` | keep on so the lift stays vertical |
| `FollowTransform` | `followRotation` | `false` | keep off — on = inherits hand tumble |
| `LookAtTarget` | `rotateX/Y/Z` | `0/1/0` | Y-only = upright billboard |
| `LookAtTarget` | `trackInUpdate` | `true` | re-aim each frame on the moving hand |
| cue canvas | `localEuler` X/Z | `0` | any non-zero here = tilt/roll bug |
| inner `Image` | `localEuler.z` | `0` | art-only roll to point the graphic up |

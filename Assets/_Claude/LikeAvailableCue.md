# Like-Available Feedback (Hand Cue)

_Status: green silhouette outline working in code (replaced the additive disc that conflicted with
the passthrough hands); headset tuning + optional motes are the open work. Created 2026-06-16,
reworked to an outline 2026-06-17._

## What it is

The moment a plant's poem finishes, the **keep / like gesture unlocks**. Before this cue there
was nothing telling the user that just happened — they could heart-gesture during the poem with no
effect and give up. This cue lights up **both hands** with a soft green outline the instant keep
becomes available, and clears the moment keep is used or the selection changes.

Green = "go, you may act now". It lives on the **hands** (not the floor) because the affordance is
"your hand can do something now," and the gesture is a hand action. The floor `HeroGlow` is a
separate, unchanged thing (the idle "this plant is touchable" invite).

## Why an outline (the passthrough-hands constraint)

The hands are rendered with `Custom/HandDepthOccluder` — a **depth-only occluder**
(`ColorMask 0, ZWrite On, Queue Geometry-1`): it writes the hand's depth so virtual content behind
the real hand is culled and **passthrough shows through the hand**. There is no chroma key, so the
problem was never the green *value*.

The original cue was an **additive disc** (`GroundGlow`, `Blend SrcAlpha One`) parented at the palm.
That laid additive green directly over the **passthrough skin** (tinting the real hand green) and
the occluder's depth carved the disc unevenly — so it "conflicted with the hands shader."

The fix keeps green's meaning but moves it **off the skin and into the air at the hand edge**:
render the hand mesh a second time as an **inverted hull** and let the occluder mask it.

## How the outline works

**Two** materials are added as extra entries on each hand's depth-occluder `SkinnedMeshRenderer`
(a single-submesh hand mesh, so each extra material re-renders the whole hand as an inverted hull —
back-faces, `Cull Front`, extruded out along normals). The standoff gap and the line thickness are
decoupled because there are two shells at two radii:

- **`Custom/URP/HandOutlineMask`** (queue `Transparent-1`, draws **first**): extruded by
  `_EdgeOffset`, **depth-only** (`ColorMask 0, ZWrite On`). It lays a near-depth **wall** out to a
  silhouette inflated by `_EdgeOffset`.
- **`Custom/URP/HandOutline`** (queue `Transparent`, additive): extruded by `_EdgeOffset + _Width`.
  `ZTest LEqual` against both the hand occluder (`Geometry-1`) and the mask wall means it's culled
  over the body **and** inside the standoff wall — so only the thin
  **`[_EdgeOffset, _EdgeOffset + _Width]`** band survives.

So the two knobs are independent:
- `_Width` = **line thickness** (keep small for a thin line).
- `_EdgeOffset` = a true **passthrough gap** between the real hand edge and the line (it shifts the
  inner cut outward via the mask, *without* thickening the line).
- `_Blur` fades the line's **outer edge** via the back-face grazing term (`saturate(-dot(N,V))`
  through a `smoothstep`), so it can read as a soft glow rather than a hard band (`_Blur = 0` = crisp).
- `_Smoothing` widens that `smoothstep` to at least N screen pixels (`fwidth`), **anti-aliasing**
  the edge so corners stop reading as hard/jagged. NB: this softens the *outer* edge only — it does
  not geometrically round a sharp fingertip turn (that needs a screen-space distance-field outline).

Net result: a thin green line floating a controllable gap off the hand silhouette, in passthrough
air — **no fill on the real skin**, and the depth buffer does all the masking (no second mesh, no
bone copying). Render queue (mask `Transparent-1` < line `Transparent`) guarantees the mask draws
first regardless of material array order.

> Caveat: the mask writes depth in the transparent queue, so it extends the hand's existing
> occlusion outward by `_EdgeOffset` in a thin ring. This is the same behaviour the hand occluder
> already has (it writes depth too), just a few mm wider — negligible in practice.

## Where it lives

| | |
|---|---|
| Shaders | `HandOutline.shader` (`Custom/URP/HandOutline`, the line) + `HandOutlineMask.shader` (`Custom/URP/HandOutlineMask`, the standoff depth wall) in `Assets/_Projects/_Scripts/Shaders/` |
| Script | `Assets/_Projects/_Scripts/Plants/Experience/HandReadyCue.cs` (scene singleton) |
| Scene GO | `[Hand Ready Cue]` in `Experience.unity` |
| Driven by | `ExperienceManager` — `Show()` at the end of `EnableSelectorsAfterAnimation` (keep unlocked); `Hide()` in `LikeSelected` (used) and `DisableSelectorsAndCancelTimer` (new selection / re-gate / deselect) |
| Hand renderers | auto-resolved: any `SkinnedMeshRenderer` wearing `Custom/HandDepthOccluder`, unless `handRenderers` is wired explicitly. No scene wiring needed. |

## What already works

- Per hand, a **thin green silhouette line** standing off the edge, via the two inverted-hull
  shells above (depth mask + line), attached on the occluder renderer on `Show()` and removed on
  `Hide()`.
- `Show()` fades the outline in (drives `_Color.a`), then **breathes** (alpha pulse) while shown;
  `Hide()` fades it out then detaches.
- `EnsureAttached()` re-adds the outline each frame if something (e.g. OVRHand's system-gesture
  material swap) reset the renderer's material array while the cue is shown.
- Driven correctly by the experience: appears exactly when keep is live, clears when it's used or
  the selection changes. No per-plant wiring needed.

## What's left to do (the next-session task)

1. **Headset tuning pass** — feel only: `outlineWidth` (line thickness — keep thin), `outlineOffset`
   (standoff gap between hand and line), `outlineBlur` (outer-edge softness), `pulseMinAlpha` /
   `pulsePeriod` (the breath), and `color`. All push to the materials live while the cue is shown,
   so you can drag them in play mode. Check the line reads cleanly against passthrough. The mask's
   `Offset -1,-1` stabilises the inner edge; revisit only if green leaks into the gap or z-fights.
2. **(Optional) green motes** — the cue still supports an authored `motesPrefab` ParticleSystem per
   hand (a few sparse, slow, drifting green motes lifting off the fingertips into the air to add
   life). `Show()` calls `Play()` and `Hide()` `Stop(...StopEmitting)`. Use a **URP-compatible**
   particle material (URP/Particles/Unlit additive) or it renders magenta. Make it a prefab and drop
   it into `HandReadyCue.motesPrefab`. Currently unassigned → **outline-only**.

## How to test (headset-free)

In `Experience.unity` play mode:
1. `ExperienceManager` → **Debug Select Next**, let the poem play out.
2. At audio end, **both hands** should get the green silhouette outline (and motes, once authored).
3. **Debug Like** → cue clears. **Debug Select Next** again → it re-arms only after the next poem.
4. The outline needs the OVR hand mesh to be live, so the hands must be tracked/visible in play mode
   (the renderers are auto-found by their `Custom/HandDepthOccluder` material).

## Tuning knobs (on `HandReadyCue`)

| Field | Default | Notes |
|---|---|---|
| `color` | green (0.35, 1, 0.45) | cue colour |
| `outlineWidth` | 0.003 m | line thickness (keep small for a thin line; independent of the gap) |
| `outlineOffset` | 0.006 m | standoff gap (passthrough) between the real hand edge and the line |
| `outlineBlur` | 0.2 | softens the line's outer edge (0 = crisp, 1 = very soft) |
| `outlineSmoothing` | 1.5 px | screen-space anti-aliasing of the edge — takes the hardness off corners |
| `fadeDuration` | 0.35 s | fade in/out |
| `pulseMinAlpha` | 0.45 | dim end of the breathing pulse |
| `pulsePeriod` | 1.4 s | one breath cycle |
| `handRenderers` | _empty → auto_ | leave empty to auto-find by occluder material |
| `motesPrefab` | _empty_ | optional; author + assign for fingertip motes |

## Notes / gotchas

- If `Custom/URP/HandOutline` isn't found, the outline is skipped (logged), not magenta. If
  `Custom/URP/HandOutlineMask` isn't found, the line still draws but **without the standoff gap**
  (it sits on the silhouette; logged). The motes material is still on you (URP additive) if you
  author motes.
- The outline depends on the hand being a **depth-only occluder** that writes depth before the
  Transparent queue. If the hand shader ever changes to write colour or stops writing depth, the
  masking that keeps green off the skin breaks — re-check then.
- Single-submesh assumption: the extra material re-renders the **last** submesh. The OVR hand mesh
  is one submesh, so it outlines the whole hand. A multi-submesh hand would only outline the last.
- `HandReadyCue` builds lazily on the first `Show()`; if the hand renderers aren't resolvable yet
  (hands not tracked) it silently retries next `Show()`.

## Colours / modes

The same outline renders in three modes (driven by `ExperienceManager`):
- **`Show()`** — green "keep" line (like available). Single inner ring.
- **`ShowContext()`** — yellow "ask" line (post-flourish gaze). Single inner ring.
- **`ShowBoth()`** — green "keep" **inner** ring + yellow "ask" **outer** ring, TWO concentric lines per
  hand. Used pre-flourish while the like gesture is offered **and** the gaze is on a canopy-fruit
  tree's date fruit, so neither affordance is lost. Each ring is its own (mask + outline) pair; the
  outer ring's standoff is `dualOuterOffset` (≈0.013 m) and **must keep a few-mm gap from the inner
  band** (`[outlineOffset, outlineOffset+outlineWidth]`) or the two depth masks cross-cull. The fruit
  gaze (`UpdateFruitGaze`) calls `ShowBoth()`; looking away drops back to green-only `Show()`.

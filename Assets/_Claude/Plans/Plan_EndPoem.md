# End Poem — Implementation Plan

Status: planned · Source: IDEAS.md · Planned: 2026-06-11

## Context

- Poems are **baked PNG sprites** (`PlantData.poem` → `PlantLabelContent { Sprite text, background }`), rendered via `PlantLabel` (UI `Image`, `SetNativeSize`, alpha fades). No string poem text exists. Assets in `Assets\_Projects\_Resources\TextCards\`.
- `ExperienceManager` (`Assets\_Projects\_Scripts\Plants\Experience\ExperienceManager.cs`) does **not** store like-order; `FlourishRoutine` (lines ~264–283) rebuilds liked plants in batch order. `onGardenFlourish` (UnityEvent, line ~69) fires when the flourish finishes — the canonical end-of-experience hook.
- Exhibition runs PCVR via Quest Link (user-confirmed), not standalone APK. So the PC has full `System.IO`, multi-display output, and PC-class GPU.

## WP-0 — Shared groundwork (prerequisite)

Files: `ExperienceManager.cs`, `Plant.cs`

1. `Plant.cs`: expose data — `public PlantData PlantData => plantData;`
2. `ExperienceManager.cs`:
   - Add `private readonly List<Plant> m_likedOrder = new();`
   - In `LikeSelected()` (~line 207, between `m_likedCount++` and the flourish check): `m_likedOrder.Add(m_selected);`
   - Expose `public IReadOnlyList<Plant> LikedSpecies => m_likedOrder;`
3. Add a `public UnityEvent onFlourishComplete` invoked at the very end of `FlourishRoutine` (after the existing `onGardenFlourish.Invoke()` or reuse that event — decide in code review; the poem display listens here).
4. Update `IDEAS.md` statuses `discuss` → `planned` for all four.

## Idea 1 — End Poem from user choices

**Asset dependency (user):** 12 end-line sprites (one per species), same pipeline as `TextCards/` poems. Optional shared background card.

**Data:** Add `public Sprite endPoemLine;` to `PlantData` (`Assets\_Projects\_Scripts\Plants\PlantData.cs`). Assign in the 12 `.asset` files in `Assets\_Projects\_Resources\_PlantInfo\`.

**New script `EndPoemDisplay.cs`** (in `_Scripts\Plants\Experience\`):
- World-space canvas prefab modeled on the `PlantInfos.prefab` / `PlantLabel` pattern: a `VerticalLayoutGroup` of 8 `Image`s + optional background.
- On flourish-complete event: pull `ExperienceManager.LikedSpecies`, instantiate one `Image` per species' `endPoemLine` **in like-order**, `SetNativeSize` (uniform scale to fit), fade in via the existing `PlantLabel`-style alpha lerp.
- Placement: world-anchored ~1.8 m in front of the user's flourish-time head position, slightly below eye height. (EXPERIENCE_PROGRESS already flags face-proximity as a headset-tuning risk — expose distance/height as serialized fields.)
- Expose `public RectTransform PoemRoot` so the postcard exporter (Idea 3) can capture/composite the same composed poem.

**Scene wiring:** EndPoemDisplay prefab in `Experience.unity`, listening to the flourish-complete event on Experience Manager.

## Verification

- Editor play-mode run of the full flow (idle→select→grow→like ×8→flourish); confirm the 8 line sprites appear in the order liked, fade in, correct layout. Regression: Interaction Prototype scene still compiles/plays.

## Open asset/hardware dependencies

- 12 end-poem line sprites (Photoshop, TextCards pipeline)

## Decisions made

- End poem = **new baked per-species "end line" sprites** (12, Photoshop, matching TextCards aesthetic), 8 stitched vertically in like-order.

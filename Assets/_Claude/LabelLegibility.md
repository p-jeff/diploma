# Label Legibility over Bright / White Passthrough

_Status: built 2026-06-20, **not yet headset-verified**. Companion to `TmpLabels.md` (the label
migration plan)._

## Problem

This is an MR/passthrough piece, so poem + context labels float over the *real world*, whose
brightness is unknown — a white wall or window makes the pure-white SDF text
(`LabelStyle.color = Color.white`) disappear. The original fix was a solid black background panel,
but that is hard-disabled (`PlantLabel.showBackground = false`) after a stray-white-box bug and the
OVROverlay-compositor stutter history (see `ovroverlaycanvas-poem-prototype`). We needed contrast
that is **self-contained in the glyphs** so it works on *any* background, with no box, and stays on
a plain world-space canvas (no compositor layer).

## Approach — dark outline + soft underlay, baked into the text material

Contrast is baked into the glyphs via a TMP SDF **dark outline** (crisp edge) **+ soft dark
underlay** (a centered, dilated halo). White-on-white text now reads by its dark edge; on dark
backgrounds the white face pops. It is applied through the existing **`LabelStyle` SSOT**, so it's
tuned in one asset per role and can never drift per-TMP — exactly how fonts are already enforced.

### Key constraint
`LabelStyle.Apply()` sets `t.font`, which **resets the TMP material to the font's default**. So a
material assigned directly on a TMP gets clobbered at runtime — the preset must flow through
`LabelStyle` and be assigned *after* the font.

## Files changed

| File | Change |
|---|---|
| `_Scripts/Plants/LabelStyle.cs` | New `Material materialPreset` field; `Apply()` assigns `t.fontSharedMaterial = materialPreset` **after** `t.font`. |
| `_Scripts/Plants/PlantLabel.cs` | `SetAlpha` also fades the instance material's `_OutlineColor` + `_UnderlayColor` alpha; base alphas read once via `EnsureBaseAlphas` (invalidated in `SetStyle`). No-op for labels without a preset. |
| `_Resources/Fonts/Roboto-Light Outline SDF.mat` | New preset → `ContextStyle`. |
| `_Resources/Fonts/Junicode-Italic Outline SDF.mat` | New preset → `PoemStyle`. |
| `_Resources/LabelStyles/ContextStyle.asset`, `PoemStyle.asset` | `materialPreset` assigned. |

`PlantInfo` already pushes `contextStyle`/`poemStyle` to every label via `SetStyle`
(`PlantInfo.cs:201,212`), so all labels inherit the outlined material with no per-plant edits.
`showBackground` stays `false`.

### Why fade the outline/underlay
In the TMP SDF shader the **underlay** color alpha is a material property and does NOT follow
`text.alpha`; without `SetAlpha` scaling it, a fading-out label would leave a static halo. The
outline mostly follows the face, but it's scaled too so the whole label fades as one. `SetAlpha`
uses `text.fontMaterial` (a per-instance clone — cheap for the few live labels) so the shared
preset asset is never mutated.

## Preset values (starting points — tune in VR, on the `.mat`s only)

| | Roboto (context) | Junicode-Italic (poem) |
|---|---|---|
| `_OutlineColor` | black | black |
| `_OutlineWidth` | **0.15** | **0.10** (thinner — italic strokes are delicate) |
| `_UnderlayColor` α | **0.8** | **0.8** |
| `_UnderlayOffsetX/Y` | 0 / 0 (centered halo) | 0 / 0 |
| `_UnderlayDilate` | **0.2** | **0.2** |
| `_UnderlaySoftness` | **0.5** | **0.5** |

Suggested tuning ranges: outline width 0.10–0.20, underlay α 0.7–0.9, dilate 0.1–0.3,
softness 0.4–0.7. Contrast is deliberately soft; on a real textured wall it reads better than the
flat-white editor test.

## MCP gotcha — keyword via `.mat` YAML, not `EnableKeyword`

`Material.EnableKeyword("UNDERLAY_ON")` inside a `Unity_RunCommand` trips the "user interactions are
not supported" guard — the shader-variant compile shows a progress bar (same family as the blocked
TMP atlas generation). Workaround used here:
1. `RunCommand` creates the `.mat` by copying the font's default material and setting the
   outline/underlay **properties only** (no new variant → no prompt).
2. Add `UNDERLAY_ON` to `m_ValidKeywords` by editing the `.mat` YAML on disk.
3. `AssetDatabase.ImportAsset(path, ForceUpdate)` compiles the variant fine (not flagged).

The outline needs **no keyword** — it's unconditional in `TextMeshPro/Distance Field`.

## Verification

- Scripts compile clean (no console errors).
- Both presets confirmed: `IsKeywordEnabled("UNDERLAY_ON") == true`, correct outline widths,
  underlay α 0.8, shader `TextMeshPro/Distance Field`, atlas pointing at the right font.
- Styles reference the presets (GUIDs match).
- Editor render of both presets over a **white/dark split background**: both lines read on the white
  half (defined by their dark edge) and pop on the dark half; the thin 0.10 outline kept Junicode's
  italic strokes clean.

## Remaining
Headset pass over a real white wall + a dark area to dial outline width / underlay dilate-softness
on the two `.mat` presets. The fade-with-outline/underlay behaviour is verified by code/logic, best
confirmed live.

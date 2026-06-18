# TMP Labels (Poem + Context)

_Status: **Steps 1 + 2 + fonts all DONE** 2026-06-17. Text filled from SSOT (`SSOT_Autofill.md`);
multilingual fonts wired (poems = Junicode-Italic, context = Roboto-Light, CJK/Arabic via dynamic
Noto fallbacks — all scripts verified rendering). **The Step-2 background panel is currently DISABLED**
— both the `Background` child on `Label.prefab` is inactive AND `PlantLabel.showBackground = false`
force-hides it at runtime (it re-asserts the GameObject's active state, so no stray panel can render).
Flip `showBackground` on (and re-enable the child) to bring it back. Not yet headset-verified._

> **White-background follow-up (resolved):** after disabling, the user briefly saw a stray white box
> behind one Lavender context label. Static trace showed the panel was off everywhere (all labels are
> clean `Label.prefab` instances, no overrides re-enable it; Lavender's grown-context environment path
> passes a `null` texture so it's not the 180° cylinder). Once the code-level `showBackground=false`
> force-hide landed, it **no longer reproduces** — most likely a stale pre-disable instance. If it ever
> returns, it is NOT the label panel; check the grown gsplat instance / a leftover quad in that plant.

## Goal

Replace the two pre-rendered Photoshop **sprite `Image`s** (text + background) on every `Label`
with a **`TextMeshProUGUI`** for the text and a **dynamically-sized `Image`** panel drawn slightly
behind it. Wins:

- Crisp SDF text that scales without re-exporting sprites per zoom/distance.
- **Runtime-authorable strings** — the global `StorySequence` can push plain text through
  `PlantInfo.SetContextContent` instead of needing a baked sprite per beat (the K=36 sequence becomes
  text, not Photoshop round-trips).
- The background can wrap any string length instead of being a fixed-size image.

## Current state (sprite-based — what we're replacing)

- `Assets/_Projects/_Prefabs/Label.prefab`: a World-Space-Canvas UI tree —
  - `Background` — `Image`, local `z = 0.02` (the depth gap), scale 0.005, sizeDelta 1191×1715.
  - `Text` — `Image`, `z = 0`, scale 0.005, sizeDelta 1105×1629.
- `PlantLabel` (`_Scripts/Plants/PlantLabel.cs`) holds the two `Image`s; `SetContent` swaps sprites +
  `SetNativeSize()`; `SetAlpha` fades both Images' colour alpha.
- `PlantLabelContent` (`_Scripts/Plants/PlantData.cs`): `{ Sprite text; Sprite background;
  Texture2D environmentPainting; }`.
- `PlantInfo` owns the labels (poem + context) and only calls `SetContent` / `SetAlpha` — it does not
  care how a label renders, so **nothing above `PlantLabel` changes** in either step.
- The depth gap (background `z = 0.02` behind text) is a property of the prefab, kept in both steps.

---

## Step 1 — Text → TMP  *(this step)*

Swap only the **text** from a sprite `Image` to a `TextMeshProUGUI`. The background stays the existing
sprite `Image` for now (it will be mismatched in size against the new text until Step 2 — acceptable
interim).

**Code**

1. `PlantLabelContent.text`: `Sprite` → `string` (`[TextArea]`). `background` stays a `Sprite`.
2. `PlantLabel.text`: `Image` → `TMP_Text` (matches `TouchPrompt`, which already serialises
   `TMP_Text`). `SetContent` sets `text.text`; `SetAlpha` sets `text.alpha` (TMP) + the background
   Image alpha (unchanged). Background `Apply`/`SetAlpha(Image,…)` helpers stay.

**Prefab (`Label.prefab`, via unity-mcp)**

3. On the `Text` GameObject, replace the `UnityEngine.UI.Image` component with a `TextMeshProUGUI`
   (reuses the existing `CanvasRenderer`). Default font = TMP default for now; centre-aligned; white.
4. Re-wire `PlantLabel.text` to the new TMP component. (The old serialized ref points at the removed
   Image and resolves to null after the field type change — must be re-assigned.)

**Migration note** — changing the text field from `Sprite` to `string` drops every authored
text-sprite reference on the `PlantData` assets (they become empty strings). Poem + context **text
must be re-authored as strings** per `PlantData`. Background sprites are untouched until Step 2.

---

## Step 2 — Dynamic background  *(DONE 2026-06-17)*

The background is now a panel sized to the text instead of a fixed sprite.

- `PlantLabelContent`: `background` (`Sprite`) **removed**. (Old `background:` refs on `PlantData`
  assets are now orphaned/inert.)
- `PlantLabel.FitBackground()` (called from `SetContent`): wraps the text at its own rect width `w`
  (authored per label — kept), measures height via `text.GetPreferredValues(text.text, w, 0)`, sets
  the text rect height to fit, then `background.rectTransform.sizeDelta = new Vector2(w, h) + 2*padding`.
- New serialized knobs on `PlantLabel`: `padding` (Vector2, default 80×60 label-units) and
  `backgroundOpacity` (0..1, default 0.6). `SetAlpha` now sets `text.alpha = a` and the panel alpha
  to `backgroundOpacity * a`, so the panel stays translucent while text is fully opaque.
- `Label.prefab` Background Image: built-in **`UISprite` (9-sliced, rounded)**, `type = Sliced`,
  `pixelsPerUnitMultiplier = 0.5`, colour `(0.05, 0.05, 0.08, 0.6)` dark translucent, raycast off.
  Kept at `z = 0.02` behind the text. TMP text raycast also off.
- `PlantInfos.prefab`: the per-plant `m_Sprite` overrides on each nested Label's Background (and the
  orphaned overrides on the deleted Text Image) were **cleared** (6 across 5 instances) so every label
  inherits the new panel.

### Tuning (all placeholders — tune in VR)
`padding` 80×60, `backgroundOpacity` 0.6, panel rounding `pixelsPerUnitMultiplier` 0.5, TMP `fontSize`
120, panel tint — all on `Label.prefab` / `PlantLabel`.

## Fonts / multilingual  *(DONE 2026-06-17)*

Font assets in `Assets/_Projects/_Resources/Fonts/` (SDF, all **Dynamic** atlas + multi-atlas):

| Role | Font asset | Where set |
|---|---|---|
| **Poems** | `Junicode-Italic SDF` | override on `PlantInfos` → `PoemLabel` TMP |
| **Context (Latin)** | `Roboto-Light SDF` | base `Label.prefab` Text TMP (all labels inherit) |
| **CJK fallback** | `NotoSansSC-Light SDF` | in the fallback table of Junicode-Italic + Roboto |
| **Arabic fallback** | `NotoSansArabic-Light SDF` | "" |

So a label tries its primary (Junicode/Roboto), then Noto SC (CJK), then Noto Arabic — each Dynamic,
so any glyph renders on demand from the TTF. Verified via `HasCharacter`: CJK, Arabic, and Latin
accents (ī) all resolve.

**Gotchas:**
- TMP font-asset **creation/save via MCP RunCommand is blocked** (the atlas-generation progress bar
  counts as a "user interaction"). So creating new SDF assets must be done in the **Font Asset
  Creator** UI; only Junicode(-Italic) here were script-made (static source → no variable dialog).
- Font Asset Creator defaults to **Static** atlas (only baked glyphs → CJK shows `□`). Must be
  **Dynamic** for CJK. Converting Static→Dynamic in script also needs `fa.ClearFontAssetData(true)` —
  the static atlas texture is **non-readable**, so dynamic glyph adds fail ("make the texture
  readable") until cleared/rebuilt.
- Editing existing font assets (fallback table, atlas mode) and incremental dynamic glyph adds do
  **not** block — only bulk atlas generation does.
- Weight 300 came from the user creating `*-Light` instances of the variable fonts in the Creator.

---

## Where it lives

| | |
|---|---|
| Data model | `Assets/_Projects/_Scripts/Plants/PlantData.cs` (`PlantLabelContent`) |
| Label component | `Assets/_Projects/_Scripts/Plants/PlantLabel.cs` |
| Prefab | `Assets/_Projects/_Prefabs/Label.prefab` (under the World-Space canvases in `PlantInfos.prefab`) |
| Owner (unchanged) | `Assets/_Projects/_Scripts/Plants/PlantInfo.cs` |

## Notes / gotchas

- `TMP_Text` is the abstract base of `TextMeshProUGUI`; Unity serialises the concrete component fine
  (same pattern as `TouchPrompt`).
- Component-type swap on the prefab is done in-editor via unity-mcp (not raw YAML — a TMP component
  block is too field-heavy to hand-write safely). Pace MCP calls sequentially (Unity RAM).
- Changing the serialized field types marks the affected `PlantData` assets + the prefab dirty.

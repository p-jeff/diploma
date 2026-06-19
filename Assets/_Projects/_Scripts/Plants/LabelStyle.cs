using TMPro;
using UnityEngine;

namespace Plants
{
    /// <summary>
    /// A reusable text style for plant labels — the single source of truth for font + formatting.
    /// <see cref="PlantLabel"/> applies it in OnEnable and on every <see cref="PlantLabel.SetContent"/>,
    /// so a label always renders with the authored font/size/alignment regardless of what a prefab or
    /// scene instance happens to have on its TMP component. This is what stops font/format drift:
    /// labels that fell back to TMP's default font self-correct at runtime, and any runtime-spawned
    /// label is correct by construction.
    ///
    /// Create one asset per role (e.g. ContextStyle = Roboto-Light, PoemStyle = Junicode-Italic) via
    /// Assets ▸ Create ▸ Plants ▸ Label Style.
    /// </summary>
    [CreateAssetMenu(menuName = "Plants/Label Style", fileName = "LabelStyle")]
    public class LabelStyle : ScriptableObject
    {
        [Tooltip("SDF font asset. For poems use the multilingual one (with CJK/Arabic fallbacks wired).")]
        public TMP_FontAsset fontAsset;

        [Tooltip("Extra style flags layered on the font's own weight (e.g. Italic, Bold).")]
        public FontStyles fontStyle = FontStyles.Normal;

        [Tooltip("Text alignment within the label rect.")]
        public TextAlignmentOptions alignment = TextAlignmentOptions.Center;

        [Header("Size")]
        [Tooltip("Auto-size the text between Min/Max instead of using a fixed point size.")]
        public bool autoSize = false;

        [Tooltip("Fixed font size, used when Auto Size is off.")]
        public float fontSize = 120f;

        [Tooltip("Auto-size lower bound (used when Auto Size is on).")]
        public float fontSizeMin = 18f;

        [Tooltip("Auto-size upper bound (used when Auto Size is on).")]
        public float fontSizeMax = 120f;

        [Header("Layout")]
        [Tooltip("When > 0, force the text rect to this width (label units) so every label using this " +
                 "style wraps to the SAME reading column. This is what stops a long context running off " +
                 "as one ridiculously wide line and reins in width-drifted instances. 0 = leave each " +
                 "label's authored rect width alone.")]
        public float wrapWidth = 0f;

        [Tooltip("When > 0, force the text rect to this height (label units). The rect is centre-pivoted " +
                 "so growing it does NOT move the label — it just gives Auto Size headroom to shrink long " +
                 "text into, and bounds the box for the (optional) background panel. 0 = leave authored.")]
        public float wrapHeight = 0f;

        [Header("Spacing")]
        public float characterSpacing = 0f;
        public float wordSpacing = 0f;
        public float lineSpacing = 0f;
        public float paragraphSpacing = 0f;

        [Header("Colour")]
        [Tooltip("Text colour. The alpha here is ignored — the label's fade owns alpha.")]
        public Color color = Color.white;

        /// <summary>Write every property of this style onto a TMP field. Null-safe. The field's
        /// current alpha is preserved so applying a style never interrupts a fade.</summary>
        public void Apply(TMP_Text t)
        {
            if (t == null) return;

            // Setting the font reassigns TMP's default material for that font — this is what fixes
            // labels that drifted onto the wrong font/material (e.g. the built-in LiberationSans).
            if (fontAsset != null) t.font = fontAsset;

            t.fontStyle = fontStyle;
            t.alignment = alignment;

            t.enableAutoSizing = autoSize;
            if (autoSize)
            {
                t.fontSizeMin = fontSizeMin;
                t.fontSizeMax = fontSizeMax;
            }
            else
            {
                t.fontSize = fontSize;
            }

            t.characterSpacing = characterSpacing;
            t.wordSpacing = wordSpacing;
            t.lineSpacing = lineSpacing;
            t.paragraphSpacing = paragraphSpacing;

            // Always wrap, so a long line becomes a column instead of one ridiculously wide line.
            // (Explicit newlines in poems are preserved; Normal only wraps lines that exceed the rect.)
            t.textWrappingMode = TextWrappingModes.Normal;

            // Enforce a consistent box so every label using this style shares one reading column. The
            // rect is centre-pivoted/middle-aligned, so resizing it grows symmetrically about the centre
            // and never repositions the label. With Auto Size on, this box is also what long text shrinks
            // into (see fontSizeMin/Max above).
            if (wrapWidth > 0f || wrapHeight > 0f)
            {
                var rt = t.rectTransform;
                var sd = rt.sizeDelta;
                float w = wrapWidth > 0f ? wrapWidth : sd.x;
                float h = wrapHeight > 0f ? wrapHeight : sd.y;
                if (!Mathf.Approximately(w, sd.x) || !Mathf.Approximately(h, sd.y))
                    rt.sizeDelta = new Vector2(w, h);
            }

            // Apply RGB but keep the live alpha so fades (PlantLabel.SetAlpha) are not clobbered.
            var c = color;
            c.a = t.color.a;
            t.color = c;
        }
    }
}

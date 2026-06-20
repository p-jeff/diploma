using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Plants
{
    /// <summary>
    /// A single label: a <see cref="TMP_Text"/> field with a background <see cref="Image"/> panel
    /// drawn behind it. Lives on the "Label" prefab — the background child carries the small Z offset
    /// (≈0.02) that separates it from the text. The panel is sized dynamically to wrap whatever text
    /// is shown (<see cref="FitBackground"/>), so any string length gets a snug box.
    ///
    /// <see cref="PlantInfo"/> owns these: it pushes content in via <see cref="SetContent"/>
    /// and drives the fade via <see cref="SetAlpha"/>.
    /// </summary>
    public class PlantLabel : MonoBehaviour
    {
        [Tooltip("Foreground TextMeshPro field showing the label text.")]
        [SerializeField] private TMP_Text text;

        [Tooltip("Single source of truth for this label's font + formatting. Re-applied on enable and " +
                 "on every SetContent so the label can't drift onto the wrong font, whatever a prefab " +
                 "or scene instance left on its TMP. PlantInfo overrides this per role (poem/context).")]
        [SerializeField] private LabelStyle style;

        [Tooltip("Background panel Image drawn behind the text. Sized to the text + Padding each " +
                 "Show; its tint + sprite (e.g. a 9-sliced rounded panel) are authored on the prefab.")]
        [SerializeField] private Image background;

        [Tooltip("Padding (label units) added around the text on each side when sizing the panel.")]
        [SerializeField] private Vector2 padding = new Vector2(80f, 60f);

        [Tooltip("Panel opacity at full fade-in. The panel's RGB tint is authored on the Image; this " +
                 "is multiplied by the group fade so the panel stays translucent while text is opaque.")]
        [SerializeField, Range(0f, 1f)] private float backgroundOpacity = 0.6f;

        [Tooltip("Show the background panel behind the text. OFF for now — the panel needs rework, " +
                 "and this guarantees no stray white box renders behind any label regardless of " +
                 "prefab/instance state. Flip on to bring the panel back.")]
        [SerializeField] private bool showBackground = false;

        // Authored outline/underlay alphas read once from the style's material preset, so SetAlpha can
        // fade the outline + soft halo together with the text. <0 means "no such property / no preset".
        private bool alphasCached;
        private float baseOutlineAlpha = -1f;
        private float baseUnderlayAlpha = -1f;

        void OnEnable()
        {
            ApplyStyle();
            ApplyBackgroundVisibility();
        }

        /// <summary>Assign the label text (TMP) and (when enabled) size the background panel to fit it.
        /// Safe to call in edit mode.</summary>
        public void SetContent(PlantLabelContent content)
        {
            if (content == null) return;
            ApplyStyle();
            if (text != null) text.text = content.text ?? string.Empty;
            ApplyBackgroundVisibility();
            if (showBackground) FitBackground();
        }

        /// <summary>Override this label's style (e.g. PlantInfo assigning a poem vs context style) and
        /// re-apply it immediately.</summary>
        public void SetStyle(LabelStyle newStyle)
        {
            if (newStyle == null) return;
            style = newStyle;
            alphasCached = false;   // new style may carry a different (or no) material preset
            ApplyStyle();
        }

        /// <summary>Force the TMP field to this label's <see cref="style"/> (font, size, alignment, …).
        /// No-op when no style is assigned, so labels without one keep their authored look.</summary>
        private void ApplyStyle()
        {
            if (style != null && text != null) style.Apply(text);
        }

        /// <summary>Force the panel GameObject active state to match <see cref="showBackground"/>, so a
        /// disabled panel can never render a stray box even if some prefab/scene instance left it on.</summary>
        private void ApplyBackgroundVisibility()
        {
            if (background == null) return;
            if (background.gameObject.activeSelf != showBackground)
                background.gameObject.SetActive(showBackground);
        }

        /// <summary>
        /// Grow the background panel to wrap the current text. The text wraps at its own rect width
        /// (authored per label), the height follows the content, and the panel is that plus
        /// <see cref="padding"/> on each side. The text's own rect height is grown to match so its
        /// middle-aligned text stays centred in the panel.
        /// </summary>
        public void FitBackground()
        {
            if (text == null) return;

            var tr = text.rectTransform;
            float w = tr.sizeDelta.x;                 // wrap width (anchors are centred, so == width)
            if (w <= 1f) w = 600f;

            text.ForceMeshUpdate();
            float h = Mathf.Max(0f, text.GetPreferredValues(text.text, w, 0f).y);

            tr.sizeDelta = new Vector2(w, h);
            if (background != null)
                background.rectTransform.sizeDelta = new Vector2(w, h) + 2f * padding;
        }

        /// <summary>Set the alpha of both the text and the background. The panel keeps its authored
        /// translucency (<see cref="backgroundOpacity"/>) scaled by the fade. When the style carries a
        /// material preset with an outline/underlay, those fade with the text too so a fading label
        /// never leaves a stray halo behind.</summary>
        public void SetAlpha(float a)
        {
            if (text != null)
            {
                text.alpha = a;
                FadeMaterialEffects(a);
            }
            if (showBackground && background != null)
            {
                var c = background.color;
                c.a = backgroundOpacity * a;
                background.color = c;
            }
        }

        /// <summary>Scale the TMP outline + underlay colour alphas by <paramref name="a"/> so they fade
        /// in step with the text. The underlay alpha in particular is a material property that does NOT
        /// follow <c>text.alpha</c> on its own, so without this a fading label would keep its shadow.
        /// No-op unless the style's preset actually defines those properties (so plain labels never
        /// instantiate a material).</summary>
        private void FadeMaterialEffects(float a)
        {
            EnsureBaseAlphas();
            if (baseOutlineAlpha < 0f && baseUnderlayAlpha < 0f) return;

            var m = text.fontMaterial;   // per-instance clone of the preset; cheap for the few live labels
            if (baseOutlineAlpha >= 0f)
            {
                var c = m.GetColor(ShaderUtilities.ID_OutlineColor);
                c.a = baseOutlineAlpha * a;
                m.SetColor(ShaderUtilities.ID_OutlineColor, c);
            }
            if (baseUnderlayAlpha >= 0f)
            {
                var c = m.GetColor(ShaderUtilities.ID_UnderlayColor);
                c.a = baseUnderlayAlpha * a;
                m.SetColor(ShaderUtilities.ID_UnderlayColor, c);
            }
        }

        /// <summary>Read the authored outline/underlay alphas from the style's material preset once, so
        /// the fade multiplies the authored value (not a compounding one). Leaves both at -1 when there
        /// is no preset or the property is absent.</summary>
        private void EnsureBaseAlphas()
        {
            if (alphasCached) return;
            alphasCached = true;
            baseOutlineAlpha = -1f;
            baseUnderlayAlpha = -1f;

            var src = style != null ? style.materialPreset : null;
            if (src == null) return;
            if (src.HasProperty(ShaderUtilities.ID_OutlineColor))
                baseOutlineAlpha = src.GetColor(ShaderUtilities.ID_OutlineColor).a;
            if (src.HasProperty(ShaderUtilities.ID_UnderlayColor))
                baseUnderlayAlpha = src.GetColor(ShaderUtilities.ID_UnderlayColor).a;
        }
    }
}

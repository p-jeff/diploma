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

        [Header("Image contexts")]
        [Tooltip("Default edge feather (UV units) for image contexts: 0 = hard edges, ~0.12 = soft. " +
                 "A PlantLabelContent with imageFeather >= 0 overrides this per image.")]
        [SerializeField, Range(0f, 0.5f)] private float featherAmount = 0.12f;

        // Lazily-created child that renders an image context (a sprite with feathered edges) in place
        // of the text. Created on first image SetContent (play mode only); null for text-only labels.
        private UnityEngine.UI.Image m_image;
        private Material m_imageMat;     // per-label instance of the feather shader (owned; freed in OnDestroy)
        private float m_alpha = 1f;      // last alpha pushed via SetAlpha, so SetContent can match the fade
        static readonly int s_featherId = Shader.PropertyToID("_Feather");

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

            // An image context renders a feathered sprite in place of the text. The image child is
            // created lazily (play mode only) so text-only labels stay zero-cost and no prefab needs
            // an authored image slot.
            bool hasImage = content.contextImage != null;
            var img = hasImage ? EnsureImage() : m_image;
            if (img != null)
            {
                if (hasImage)
                {
                    img.sprite = content.contextImage;
                    SizeImage(img, content);
                    float feather = content.imageFeather >= 0f ? content.imageFeather : featherAmount;
                    if (m_imageMat != null) m_imageMat.SetFloat(s_featherId, Mathf.Clamp(feather, 0f, 0.5f));
                    img.color = new Color(1f, 1f, 1f, m_alpha);
                    if (!img.gameObject.activeSelf) img.gameObject.SetActive(true);
                }
                else if (img.gameObject.activeSelf)
                {
                    img.gameObject.SetActive(false);
                }
            }

            if (text != null)
            {
                // Hide the text GameObject for image contexts so it can't render behind the sprite.
                if (text.gameObject.activeSelf == hasImage) text.gameObject.SetActive(!hasImage);
                if (!hasImage) text.text = content.text ?? string.Empty;
            }

            ApplyBackgroundVisibility();
            if (showBackground && !hasImage) FitBackground();
        }

        /// <summary>Lazily create the image child (a UI <see cref="UnityEngine.UI.Image"/> with a
        /// feather-shader material) used to render image contexts. Sits at the label root's origin and
        /// inherits the text's px→metre scale so <see cref="SizeImage"/> can size it in real metres.
        /// Play mode only — returns null in edit mode so editor previews never spawn stray objects.</summary>
        private UnityEngine.UI.Image EnsureImage()
        {
            if (m_image != null) return m_image;
            if (!Application.isPlaying) return null;

            var go = new GameObject("ContextImage",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(transform, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.localPosition = Vector3.zero;
            rt.localRotation = Quaternion.identity;
            float s = (text != null) ? text.rectTransform.localScale.x : 0.005f;
            if (s <= 0.0001f) s = 0.005f;
            rt.localScale = new Vector3(s, s, s);

            m_image = go.GetComponent<UnityEngine.UI.Image>();
            m_image.raycastTarget = false;
            m_image.preserveAspect = true;
            m_image.color = new Color(1f, 1f, 1f, m_alpha);

            var sh = Shader.Find("Custom/UI/FeatheredImage");
            if (sh != null)
            {
                m_imageMat = new Material(sh) { name = "FeatheredImage (instance)" };
                m_image.material = m_imageMat;
            }
            else
            {
                Debug.LogError("[PlantLabel] Custom/UI/FeatheredImage shader not found — image " +
                               "contexts will render with hard edges.", this);
            }

            return m_image;
        }

        /// <summary>Size the image rect (in canvas units) so it spans <c>content.imageWidth</c> metres,
        /// with the height following the sprite's aspect ratio (preserveAspect guards against any
        /// rounding so the image never distorts).</summary>
        private void SizeImage(UnityEngine.UI.Image img, PlantLabelContent content)
        {
            var sprite = content.contextImage;
            float aspect = (sprite != null && sprite.rect.height > 0.0001f)
                ? sprite.rect.width / sprite.rect.height : 1f;
            float scale = img.rectTransform.localScale.x;
            if (scale <= 0.0001f) scale = 0.005f;
            float wUnits = Mathf.Max(0.01f, content.imageWidth) / scale;
            float hUnits = wUnits / Mathf.Max(0.0001f, aspect);
            img.rectTransform.sizeDelta = new Vector2(wUnits, hUnits);
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
            m_alpha = a;
            if (text != null && text.gameObject.activeSelf)
            {
                text.alpha = a;
                FadeMaterialEffects(a);
            }
            if (m_image != null && m_image.gameObject.activeSelf)
            {
                var c = m_image.color;
                c.a = a;
                m_image.color = c;   // multiplied by the per-pixel feather in the shader
            }
            if (showBackground && background != null)
            {
                var c = background.color;
                c.a = backgroundOpacity * a;
                background.color = c;
            }
        }

        void OnDestroy()
        {
            if (m_imageMat == null) return;
            if (Application.isPlaying) Destroy(m_imageMat);
            else DestroyImmediate(m_imageMat);
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

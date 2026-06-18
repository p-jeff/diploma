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

        void OnEnable() => ApplyBackgroundVisibility();

        /// <summary>Assign the label text (TMP) and (when enabled) size the background panel to fit it.
        /// Safe to call in edit mode.</summary>
        public void SetContent(PlantLabelContent content)
        {
            if (content == null) return;
            if (text != null) text.text = content.text ?? string.Empty;
            ApplyBackgroundVisibility();
            if (showBackground) FitBackground();
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
        /// translucency (<see cref="backgroundOpacity"/>) scaled by the fade.</summary>
        public void SetAlpha(float a)
        {
            if (text != null) text.alpha = a;
            if (showBackground && background != null)
            {
                var c = background.color;
                c.a = backgroundOpacity * a;
                background.color = c;
            }
        }
    }
}

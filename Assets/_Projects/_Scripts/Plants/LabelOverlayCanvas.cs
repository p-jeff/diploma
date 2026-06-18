using TMPro;
using UnityEngine;

namespace Plants
{
    /// <summary>
    /// Renders this Canvas as a Meta compositor layer (<see cref="OVROverlayCanvas"/>) for crisp
    /// text over passthrough, and keeps the Canvas rect sized to its TMP text so nothing is clipped.
    /// Used for both the poem label and the context labels.
    ///
    /// Config that works with this project's passthrough (found empirically): <b>Depth Tested +
    /// Transparent</b>. Punch-A-Hole (underlay) breaks passthrough here. The Canvas and its children
    /// are moved to the "Overlay UI" layer — which the main eye camera culls — so only the crisp
    /// compositor layer is shown, not the soft Unity-rendered canvas underneath it.
    ///
    /// Attached at runtime by <see cref="PlantInfo"/> to each label's Canvas. Play-mode only: the
    /// OVROverlayCanvas it adds spawns hidden helper objects (camera/imposter/overlay), which we
    /// never want created in the edit-mode scene.
    /// </summary>
    [RequireComponent(typeof(Canvas))]
    [RequireComponent(typeof(RectTransform))]
    public class LabelOverlayCanvas : MonoBehaviour
    {
        [Tooltip("Layer the canvas subtree is moved to so the eye camera hides the soft copy and " +
                 "only the compositor layer shows. Must be excluded from the eye camera's culling " +
                 "mask (it already is in this project).")]
        [SerializeField] private string hiddenLayerName = "Overlay UI";

        [Tooltip("Extra world-space margin (metres) added around the text when sizing the canvas rect.")]
        [SerializeField] private float worldMargin = 0.02f;

        [Tooltip("Text the canvas is sized to. Auto-found in children when left empty.")]
        [SerializeField] private TMP_Text text;

        private RectTransform m_rect;
        private OVROverlayCanvas m_overlay;
        private Vector2 m_lastSize = new Vector2(-1f, -1f);

        private void Awake()
        {
            m_rect = GetComponent<RectTransform>();
            if (text == null) text = GetComponentInChildren<TMP_Text>(true);
            EnsureOverlay();
            ApplyHiddenLayer();
        }

        private void OnEnable()
        {
            // Re-assert in case the component was added before the canvas finished setting up.
            EnsureOverlay();
            ApplyHiddenLayer();
            FitToText(force: true);
        }

        /// <summary>Add + configure the OVROverlayCanvas: Depth Tested + Transparent.</summary>
        private void EnsureOverlay()
        {
            if (m_overlay == null) m_overlay = GetComponent<OVROverlayCanvas>();
            if (m_overlay == null) m_overlay = gameObject.AddComponent<OVROverlayCanvas>();

            m_overlay.compositionMode = OVROverlayCanvas.CompositionMode.DepthTested;
            m_overlay.opacity = OVROverlayCanvas.DrawMode.Transparent;
        }

        /// <summary>Move the canvas subtree to the hidden layer so the eye camera doesn't draw the
        /// soft copy — only the compositor layer is shown.</summary>
        private void ApplyHiddenLayer()
        {
            int layer = LayerMask.NameToLayer(hiddenLayerName);
            if (layer < 0) return; // layer not defined — skip rather than throw
            SetLayerRecursive(transform, layer);
        }

        private static void SetLayerRecursive(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++)
                SetLayerRecursive(t.GetChild(i), layer);
        }

        private void LateUpdate() => FitToText(force: false);

        /// <summary>
        /// Size the Canvas RectTransform so its world-space rect encloses the TMP text's rendered
        /// content. OVROverlayCanvas re-reads the rect every frame (its Update → InitializeRenderTexture),
        /// so the compositor layer follows automatically.
        /// </summary>
        private void FitToText(bool force)
        {
            if (text == null || m_rect == null) return;

            var tr = text.rectTransform;
            text.ForceMeshUpdate();

            float wLocal = tr.rect.width;                                   // wrap width (text-local units)
            float hLocal = Mathf.Max(text.preferredHeight, tr.rect.height); // content height (text-local units)

            // Convert the text's world size into this canvas's local units, so the differing scales
            // of the Canvas vs the nested label/text don't matter.
            Vector3 textScale = tr.lossyScale;
            Vector3 canvasScale = m_rect.lossyScale;
            float sx = Mathf.Max(1e-6f, Mathf.Abs(canvasScale.x));
            float sy = Mathf.Max(1e-6f, Mathf.Abs(canvasScale.y));

            float width = wLocal * Mathf.Abs(textScale.x) / sx + (worldMargin / sx) * 2f;
            float height = hLocal * Mathf.Abs(textScale.y) / sy + (worldMargin / sy) * 2f;

            var size = new Vector2(width, height);
            if (!force && (size - m_lastSize).sqrMagnitude < 1e-4f) return;
            m_lastSize = size;
            m_rect.sizeDelta = size;
        }
    }
}

using UnityEngine;

namespace Plants
{
    /// <summary>
    /// DEPRECATED / inert. This used to render a label's Canvas as a Meta OVR compositor layer for
    /// crisp text over passthrough. That overlay path was removed project-wide because it cost a
    /// per-frame render-texture re-render plus a TMP <c>ForceMeshUpdate</c> every frame, hit the
    /// compositor's layer-count limit once many labels were live, and got mis-reprojected by ASW —
    /// see <see cref="OverlayCanvasStripper"/>.
    ///
    /// The type is kept (so any instance baked into a scene from an old play-mode save doesn't become
    /// a missing script) but it now only cleans itself up: strip the OVROverlayCanvas, move the
    /// subtree back to a camera-visible layer, and remove itself. Nothing adds it at runtime anymore.
    /// </summary>
    public class LabelOverlayCanvas : MonoBehaviour
    {
        private void Awake()
        {
            var overlay = GetComponent<OVROverlayCanvas>();
            if (overlay != null) Destroy(overlay);

            int hidden = LayerMask.NameToLayer("Overlay UI");
            if (hidden >= 0 && gameObject.layer == hidden)
                SetLayerRecursive(transform, 0); // Default — so the eye camera draws the plain canvas

            Destroy(this);
        }

        private static void SetLayerRecursive(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++)
                SetLayerRecursive(t.GetChild(i), layer);
        }
    }
}

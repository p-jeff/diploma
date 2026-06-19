using UnityEngine;
using UnityEngine.SceneManagement;

namespace Plants
{
    /// <summary>
    /// Removes every <see cref="OVROverlayCanvas"/> from loaded scenes at runtime and restores its
    /// canvas subtree to a camera-visible layer.
    ///
    /// The OVR compositor-layer overlays (added for crisp poem/label text over passthrough) caused
    /// more harm than good — a per-frame render-texture re-render, the compositor's hard layer-count
    /// limit once many labels were live, and ASW mis-reprojecting them — so they were removed
    /// project-wide. Labels now render as plain world-space canvases (softer text, but stable and
    /// cheap). Some overlays were also accidentally baked into scenes from old play-mode saves; this
    /// strips those on load so the scene files don't have to be hand-edited. The runtime add-path is
    /// gone too (see <see cref="LabelOverlayCanvas"/> and PlantInfo.EnsureLabelOverlay).
    /// </summary>
    public static class OverlayCanvasStripper
    {
        // Labels were parked on a camera-culled layer ("Overlay UI") while the overlay drew the crisp
        // copy. With the overlay gone they must move back to a layer the eye camera renders or they'd
        // be invisible. Default (0) is always in the camera's culling mask.
        const int k_visibleLayer = 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            StripAll();
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => StripAll();

        /// <summary>Destroy every OVROverlayCanvas in the loaded scenes; un-hide any subtree that was
        /// parked on the culled overlay layer. Returns how many were removed.</summary>
        public static int StripAll()
        {
            var overlays = Object.FindObjectsByType<OVROverlayCanvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            int hiddenLayer = LayerMask.NameToLayer("Overlay UI");
            int removed = 0;
            foreach (var o in overlays)
            {
                if (o == null) continue;
                if (hiddenLayer >= 0 && o.gameObject.layer == hiddenLayer)
                    SetLayerRecursive(o.transform, k_visibleLayer);
                Object.Destroy(o);
                removed++;
            }
            return removed;
        }

        static void SetLayerRecursive(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++)
                SetLayerRecursive(t.GetChild(i), layer);
        }
    }
}

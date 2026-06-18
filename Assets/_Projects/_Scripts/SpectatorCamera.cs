using UnityEngine;

namespace Plants
{
    /// <summary>
    /// Routes this camera to Display 1 (projector / second monitor) so the
    /// Gaussian-splat garden can be shown to an audience without appearing in
    /// the VR headset view.  Drop the SpectatorCamera prefab anywhere in the
    /// scene; it is fully decoupled from the headset.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class SpectatorCamera : MonoBehaviour
    {
        [Tooltip("Output is letterboxed/pillarboxed to this aspect ratio. Set to 0 to fill the display instead.")]
        public float TargetAspect = 16f / 9f;

        Camera _cam;
        int _lastWidth, _lastHeight;

        void Awake()
        {
            _cam = GetComponent<Camera>();

            // Safety net: ensure the camera never renders to the HMD.
            _cam.stereoTargetEye = StereoTargetEyeMask.None;
            _cam.targetDisplay   = 1;

            // Activate Display 1 at runtime (no-op in Editor / when only one
            // display is connected, so this is always safe to call).
            if (Display.displays.Length > 1)
                Display.displays[1].Activate();
        }

        void LateUpdate()
        {
            ApplyAspect();
        }

        void ApplyAspect()
        {
            if (TargetAspect <= 0f)
                return;

            // Full size of the display this camera renders to. In the Editor
            // Display.displays only contains the main display, so fall back to
            // the Game view size there.
            int displayIndex = _cam.targetDisplay;
            int width, height;
            if (displayIndex < Display.displays.Length)
            {
                width  = Display.displays[displayIndex].renderingWidth;
                height = Display.displays[displayIndex].renderingHeight;
            }
            else
            {
                width  = Screen.width;
                height = Screen.height;
            }

            if (width <= 0 || height <= 0 || (width == _lastWidth && height == _lastHeight))
                return;
            _lastWidth  = width;
            _lastHeight = height;

            float displayAspect = (float)width / height;
            if (displayAspect > TargetAspect)
            {
                // Display is wider than target: pillarbox.
                float w = TargetAspect / displayAspect;
                _cam.rect = new Rect((1f - w) * 0.5f, 0f, w, 1f);
            }
            else
            {
                // Display is taller than target: letterbox.
                float h = displayAspect / TargetAspect;
                _cam.rect = new Rect(0f, (1f - h) * 0.5f, 1f, h);
            }
        }
    }
}

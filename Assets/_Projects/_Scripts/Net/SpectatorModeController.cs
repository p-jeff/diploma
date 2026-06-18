using Mirror;
using Plants.Garden;
using UnityEngine;

namespace Plants.Net
{
    /// <summary>
    /// Lives in the garden scene. On the SPECTATOR client (a Mirror client that is
    /// NOT also the host) it strips the interactive headset layer and brings up a
    /// fixed-angle camera, so the Mac renders the garden purely as an audience view
    /// driven by the host's replicated state.
    ///
    /// On the host — or when the scene is played standalone with no networking — it
    /// does nothing, so the headset experience (and the host's display-1
    /// "window into the digital" SpectatorCamera) is untouched.
    /// </summary>
    public class SpectatorModeController : MonoBehaviour
    {
        [Header("Disabled on the spectator (auto-found by name when unset)")]
        public GameObject ovrRig;
        public GameObject passthrough;

        [Tooltip("Camera used for the spectator view. If unset, the scene's SpectatorCamera is used.")]
        public Camera spectatorCamera;

        [Header("Auto-find names")]
        public string ovrRigName      = "[BuildingBlock] Camera Rig";
        public string passthroughName = "[BuildingBlock] Passthrough";

        void Start()
        {
            // Pure client only. Host (server+client) and non-networked play behave normally.
            bool isSpectator = NetworkClient.active && !NetworkServer.active;
            if (!isSpectator) return;
            EnterSpectatorMode();
        }

        void EnterSpectatorMode()
        {
            // 1. Kill the headset rig + passthrough (no VR / no hands on the spectator).
            var rig = ovrRig != null ? ovrRig : GameObject.Find(ovrRigName);
            if (rig != null) rig.SetActive(false);
            var pt = passthrough != null ? passthrough : GameObject.Find(passthroughName);
            if (pt != null) pt.SetActive(false);

            // 2. Stop the local simulation — the host owns all of this and replicates it.
            DisableAll<ExperienceManager>();
            DisableAll<TitleSequenceController>();
            DisableAll<GardenPlacer>();

            // 3. Bring up the spectator camera on the main display.
            SetupSpectatorCamera();

            Debug.Log("[SpectatorMode] Entered spectator mode (rig/sim disabled, spectator camera live).", this);
        }

        void SetupSpectatorCamera()
        {
            var cam = spectatorCamera;
            SpectatorCamera helper = null;

            if (cam == null)
            {
                // Find the scene's SpectatorCamera even if its GameObject is inactive.
                var helpers = Object.FindObjectsByType<SpectatorCamera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                if (helpers.Length > 0)
                {
                    helper = helpers[0];
                    cam = helper.GetComponent<Camera>();
                }
            }
            else
            {
                helper = cam.GetComponent<SpectatorCamera>();
            }

            if (cam == null)
            {
                Debug.LogWarning("[SpectatorMode] No spectator camera found; the Mac will have no view.", this);
                return;
            }

            // The SpectatorCamera helper forces display 1 (the host's projector). On the
            // Mac we want the main display, so disable it and take the camera to display 0.
            if (helper != null) helper.enabled = false;

            cam.targetDisplay   = 0;
            cam.stereoTargetEye = StereoTargetEyeMask.None;
            cam.rect            = new Rect(0f, 0f, 1f, 1f);
            cam.gameObject.SetActive(true);
            cam.enabled         = true;
            cam.tag             = "MainCamera";   // so Camera.main resolves to the spectator view

            // The rig's AudioListener was just disabled — make sure the spectator has one.
            if (cam.GetComponent<AudioListener>() == null)
                cam.gameObject.AddComponent<AudioListener>();
        }

        static void DisableAll<T>() where T : Behaviour
        {
            var comps = Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var c in comps)
                if (c != null) c.enabled = false;
        }
    }
}

using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace Plants.Net
{
    /// <summary>
    /// Lives in the garden scene. On the SPECTATOR client (a networked client that is
    /// NOT also the host) it strips the interactive headset layer and brings up a
    /// fixed-angle camera, so the Mac renders the garden purely as an audience view
    /// driven by the host's replicated state.
    ///
    /// WHAT gets stripped is fully data-driven — drag the headset-only objects/components
    /// into the two lists below. That way, when something new on the headset throws on
    /// the Mac (a gesture, a hand visual, an OVR helper…), you just add it here; no code
    /// change. Typical entries: the OVR camera rig, passthrough, Like/Context gesture
    /// objects, hand-proximity / hand-cue objects (objectsToDisable); ExperienceManager,
    /// GardenPlacer, TitleSequenceController (componentsToDisable, when they live on a
    /// shared root you can't deactivate wholesale).
    ///
    /// On the host — or when the scene is played standalone with no networking — it does
    /// nothing, so the headset experience (and the host's display-1 SpectatorCamera) is
    /// untouched.
    /// </summary>
    [DefaultExecutionOrder(-1000)]   // run before other components Start, so the local sim is disabled before it can run / throw
    public class SpectatorModeController : MonoBehaviour
    {
        [Header("Disabled on the spectator client")]
        [Tooltip("GameObjects fully deactivated on the spectator: OVR rig, passthrough, " +
                 "Like/Context gesture objects, hand-proximity / hand-cue objects, etc.")]
        public List<GameObject> objectsToDisable = new List<GameObject>();

        [Tooltip("Individual components disabled (enabled=false) when you can't deactivate the " +
                 "whole GameObject — e.g. ExperienceManager / GardenPlacer / TitleSequenceController.")]
        public List<Behaviour> componentsToDisable = new List<Behaviour>();

        [Header("Enabled only on the spectator client")]
        [Tooltip("GameObjects activated ONLY on the spectator client. Keep these inactive by default " +
                 "in the scene so the host/headset never shows them — e.g. a spectator-only environment " +
                 "splat backdrop that replaces the headset's passthrough room.")]
        public List<GameObject> objectsToEnable = new List<GameObject>();

        [Header("Spectator camera")]
        [Tooltip("Camera used for the spectator view. If unset, the scene's SpectatorCamera is used.")]
        public Camera spectatorCamera;

        bool IsSpectator => SpectatorState.Active;

        void Awake()
        {
            if (!IsSpectator) return;

            // Disable the headset-only objects/components BEFORE their own Awake/OnEnable/Update
            // runs (this component is DefaultExecutionOrder -1000), so they can't throw on the
            // Mac — e.g. Like/Context gestures dereferencing null hand anchors.
            int objs = 0, comps = 0;
            foreach (var go in objectsToDisable)
                if (go != null) { go.SetActive(false); objs++; }
            foreach (var c in componentsToDisable)
                if (c != null) { c.enabled = false; comps++; }

            // Bring up spectator-only objects (e.g. the environment splat backdrop) that are kept
            // inactive by default so the host/headset never renders them.
            int en = 0;
            foreach (var go in objectsToEnable)
                if (go != null) { go.SetActive(true); en++; }

            Debug.Log($"[SpectatorMode] Disabled {objs} objects + {comps} components, enabled {en} objects (spectator).", this);
        }

        void Start()
        {
            if (!IsSpectator) return;
            SetupSpectatorCamera();   // after the SpectatorCamera helper's Awake has forced display 1
            Debug.Log("[SpectatorMode] Spectator camera live.", this);
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

            // Whatever rig we just disabled probably owned the AudioListener — make sure there's one.
            if (cam.GetComponent<AudioListener>() == null)
                cam.gameObject.AddComponent<AudioListener>();
        }
    }
}

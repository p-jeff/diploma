using System;
using System.Collections;
using System.Collections.Generic;
using Meta.XR.MRUtilityKit;
using Oculus.Interaction;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Scene Lock System.
///
/// Before the experience starts you position a room-sized calibration box. The box's
/// <see cref="Grabbable"/> targets <see cref="sceneRoot"/> — the single "main Transform"
/// every spatially-placed object lives under — so grabbing the box moves the whole world
/// to meet your physical room. While calibrating, motion is constrained to the floor
/// (yaw + horizontal slide only). Poking the box's "Lock In" button (→ <see cref="LockScene"/>)
/// anchors the root to a persistent Meta spatial anchor, hides the box and enables
/// <see cref="content"/> (which is where the Experience Manager lives, so the experience
/// begins exactly at lock). The placement is saved and auto-restored on the next launch,
/// so the garden stays glued to the real room across sessions.
///
/// Generalises the old ScanAlignment LockPosition / WorldLockController pair: the spatial
/// anchor is now the primary world-lock (drift-corrected by the system); MRUK world-lock +
/// recenter-disable are applied on top as belt-and-suspenders.
/// </summary>
public class SceneLockController : MonoBehaviour
{
    // ── Serialized ──────────────────────────────────────────────────────────────

    [Header("Root")]
    [Tooltip("The single 'main Transform' every spatially-placed object lives under. The box moves this; locking anchors it to the real world.")]
    [SerializeField] private Transform sceneRoot;

    [Header("Calibration Box")]
    [Tooltip("Box shown while positioning (wireframe + grab handle + Lock In button). Disabled on lock.")]
    [SerializeField] private GameObject calibrationBox;
    [Tooltip("Grabbable on the box. At startup its target transform is rebound to sceneRoot, so grabbing the box moves the whole root rather than just the box.")]
    [SerializeField] private Grabbable boxGrabbable;

    [Header("Content")]
    [Tooltip("Everything that switches on the moment the scene is locked (plants, garden, Experience Manager …). Disabled until then.")]
    [SerializeField] private GameObject content;

    [Header("Chair Placement")]
    [Tooltip("The chair / sit-spot root (the glowing marker + ChairSit volume). MUST be a child of " +
             "sceneRoot so its pose is anchor-relative. Positioned during calibration via the chair " +
             "handle and persisted (relative to sceneRoot) so it survives across sessions.")]
    [SerializeField] private Transform chairRoot;
    [Tooltip("Grabbable used to position the chair over the user's REAL chair during setup. At startup " +
             "its target transform is rebound to chairRoot, so grabbing the handle moves the chair root. Optional.")]
    [SerializeField] private Grabbable chairGrabbable;
    [Tooltip("Grab handle / visuals shown ONLY while calibrating (the thing you grab to place the chair). " +
             "Hidden once the scene is locked. Optional.")]
    [SerializeField] private GameObject chairPlacementHandle;

    [Header("World Lock")]
    [Tooltip("OVRManager whose recenter is disabled once locked, so the scene can't be nudged off its anchor. Optional.")]
    [SerializeField] private OVRManager ovrManager;
    [Tooltip("If true and an MRUK instance exists, also toggle MRUK world-lock (the spatial anchor is the primary lock; this is extra drift insurance).")]
    [SerializeField] private bool useMrukWorldLock = true;

    [Header("Persistence")]
    [Tooltip("If false, the placement is never saved/restored — you recalibrate every launch (it still anchors for the running session).")]
    [SerializeField] private bool persistAcrossSessions = true;
    [Tooltip("PlayerPrefs key the saved anchor UUID is stored under.")]
    [SerializeField] private string anchorPrefKey = "SceneLock.AnchorUuid";
    [Tooltip("PlayerPrefs key the chair's saved local pose (relative to sceneRoot) is stored under.")]
    [SerializeField] private string chairPosePrefKey = "SceneLock.ChairPose";
    [Tooltip("Seconds to wait for the runtime to create / localize the anchor before giving up.")]
    [SerializeField, Min(1f)] private float anchorTimeout = 6f;

    [Header("Events")]
    [SerializeField] private UnityEvent onCalibrationStarted;
    [SerializeField] private UnityEvent onSceneLocked;

    // ── State ───────────────────────────────────────────────────────────────────

    public enum LockState { Restoring, Calibrating, Locked }
    public LockState State { get; private set; } = LockState.Restoring;

    private OVRSpatialAnchor m_anchor;

    // ── Lifecycle ───────────────────────────────────────────────────────────────

    void Awake()
    {
        // Rebind the box's grab target to the root before the Grabbable caches it in its
        // own Start, so grabbing the box drives the whole hierarchy.
        if (boxGrabbable != null && sceneRoot != null)
            boxGrabbable.InjectOptionalTargetTransform(sceneRoot);

        // Same for the chair handle: grabbing it should move the chair root, not the handle itself.
        if (chairGrabbable != null && chairRoot != null)
            chairGrabbable.InjectOptionalTargetTransform(chairRoot);
    }

    void Start()
    {
        if (persistAcrossSessions && Guid.TryParse(PlayerPrefs.GetString(anchorPrefKey, ""), out _))
            RestoreAsync();
        else
            BeginCalibration();
    }

    // ── Calibration ─────────────────────────────────────────────────────────────

    /// <summary>Enter (or re-enter) calibration: show the box, hide content, allow recenter.</summary>
    public void BeginCalibration()
    {
        State = LockState.Calibrating;
        if (content != null) content.SetActive(false);
        if (calibrationBox != null) calibrationBox.SetActive(true);

        // Start the chair at its last-saved spot (if any) so the user fine-tunes rather than
        // re-places from scratch, and show the grab handle for positioning over their real chair.
        LoadChairPose();
        if (chairPlacementHandle != null) chairPlacementHandle.SetActive(true);

        if (ovrManager != null) ovrManager.AllowRecenter = true;
        if (useMrukWorldLock && MRUK.Instance != null) MRUK.Instance.EnableWorldLock = false;

        onCalibrationStarted.Invoke();
        Debug.Log("[SceneLock] Calibration started — position the box, then poke Lock In.");
    }

    /// <summary>Hook this to the box's "Lock In" poke button (InteractableUnityEventWrapper.WhenSelect).</summary>
    public void LockScene()
    {
        if (State == LockState.Locked) return;
        StartCoroutine(LockRoutine());
    }

    private IEnumerator LockRoutine()
    {
        State = LockState.Locked;

        // Stop grabbing immediately so the root (and the chair) can't drift while we anchor it.
        if (calibrationBox != null) calibrationBox.SetActive(false);
        if (chairPlacementHandle != null) chairPlacementHandle.SetActive(false);

        // Persist the chair's final placement (relative to the root, which the anchor world-locks).
        SaveChairPose();

        // Create a spatial anchor at the root's final pose.
        var anchorGo = new GameObject("[SceneAnchor]");
        anchorGo.transform.SetPositionAndRotation(sceneRoot.position, sceneRoot.rotation);
        m_anchor = anchorGo.AddComponent<OVRSpatialAnchor>();

        // Wait for the runtime to create it (bounded).
        float t = 0f;
        while (!m_anchor.Created && t < anchorTimeout) { t += Time.deltaTime; yield return null; }

        if (!m_anchor.Created)
            Debug.LogWarning("[SceneLock] Anchor not created in time — locking the transform anyway (no drift correction / persistence this session).");

        // Glue the root to the anchor and switch the experience on. Even if the anchor
        // failed, the root is parented to a now-stationary transform, so content stays put.
        AttachRootToAnchor(anchorGo.transform);
        ApplyWorldLock();
        EnableContent();
        onSceneLocked.Invoke();

        if (persistAcrossSessions && m_anchor.Created)
            SaveAnchorJob(m_anchor);

        Debug.Log("[SceneLock] Scene locked.");
    }

    private async void SaveAnchorJob(OVRSpatialAnchor anchor)
    {
        try
        {
            var result = await anchor.SaveAnchorAsync();
            if (result.Success)
            {
                PlayerPrefs.SetString(anchorPrefKey, anchor.Uuid.ToString());
                PlayerPrefs.Save();
                Debug.Log($"[SceneLock] Saved anchor {anchor.Uuid}.");
            }
            else
            {
                Debug.LogWarning($"[SceneLock] Anchor save failed ({result.Status}); placement will not persist.");
            }
        }
        catch (Exception e) { Debug.LogException(e); }
    }

    // ── Restore ─────────────────────────────────────────────────────────────────

    private async void RestoreAsync()
    {
        State = LockState.Restoring;
        if (content != null) content.SetActive(false);
        if (calibrationBox != null) calibrationBox.SetActive(false);
        if (chairPlacementHandle != null) chairPlacementHandle.SetActive(false);

        try
        {
            if (!Guid.TryParse(PlayerPrefs.GetString(anchorPrefKey, ""), out var uuid))
            {
                BeginCalibration();
                return;
            }

            var unbound = new List<OVRSpatialAnchor.UnboundAnchor>();
            var result = await OVRSpatialAnchor.LoadUnboundAnchorsAsync(new[] { uuid }, unbound);
            if (!result.Success || unbound.Count == 0)
            {
                Debug.LogWarning("[SceneLock] Saved anchor could not be loaded — recalibrating.");
                BeginCalibration();
                return;
            }

            var ua = unbound[0];
            if (!ua.Localized)
            {
                bool ok = await ua.LocalizeAsync(anchorTimeout);
                if (!ok)
                {
                    Debug.LogWarning("[SceneLock] Saved anchor failed to localize — recalibrating.");
                    BeginCalibration();
                    return;
                }
            }

            var anchorGo = new GameObject("[SceneAnchor]");
            m_anchor = anchorGo.AddComponent<OVRSpatialAnchor>();
            ua.BindTo(m_anchor);

            AttachRootToAnchor(anchorGo.transform);
            LoadChairPose();   // restore the chair where the user placed it (relative to the root)
            ApplyWorldLock();
            EnableContent();
            State = LockState.Locked;
            onSceneLocked.Invoke();
            Debug.Log($"[SceneLock] Restored placement from anchor {uuid}.");
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            BeginCalibration();
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    /// <summary>Parent the root under the anchor and snap it onto the anchor pose, so the system keeps it world-locked.</summary>
    private void AttachRootToAnchor(Transform anchor)
    {
        if (sceneRoot == null || anchor == null) return;
        sceneRoot.SetParent(anchor, worldPositionStays: false);
        sceneRoot.localPosition = Vector3.zero;
        sceneRoot.localRotation = Quaternion.identity;
    }

    private void ApplyWorldLock()
    {
        if (ovrManager != null) ovrManager.AllowRecenter = false;
        if (useMrukWorldLock && MRUK.Instance != null) MRUK.Instance.EnableWorldLock = true;
    }

    private void EnableContent()
    {
        if (content != null) content.SetActive(true);
    }

    // ── Chair pose persistence ────────────────────────────────────────────────────
    // The chair root lives under sceneRoot, so its LOCAL pose is what the anchor world-locks.
    // We persist that local pose (position + euler) as a CSV string, invariant-culture so the
    // headset's locale can't corrupt the decimal separator.

    private void SaveChairPose()
    {
        if (!persistAcrossSessions || chairRoot == null) return;
        Vector3 p = chairRoot.localPosition;
        Vector3 e = chairRoot.localEulerAngles;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        PlayerPrefs.SetString(chairPosePrefKey, string.Format(ci, "{0},{1},{2},{3},{4},{5}",
            p.x, p.y, p.z, e.x, e.y, e.z));
        PlayerPrefs.Save();
    }

    private void LoadChairPose()
    {
        if (chairRoot == null) return;
        string s = PlayerPrefs.GetString(chairPosePrefKey, "");
        if (string.IsNullOrEmpty(s)) return;

        var parts = s.Split(',');
        if (parts.Length != 6) return;

        var ci = System.Globalization.CultureInfo.InvariantCulture;
        var f = new float[6];
        for (int i = 0; i < 6; i++)
            if (!float.TryParse(parts[i], System.Globalization.NumberStyles.Float, ci, out f[i])) return;

        chairRoot.localPosition = new Vector3(f[0], f[1], f[2]);
        chairRoot.localEulerAngles = new Vector3(f[3], f[4], f[5]);
    }

    // ── Recalibrate / escape hatch ────────────────────────────────────────────────

    /// <summary>
    /// Erase the saved anchor, detach the root and return to calibration. Lets you re-place
    /// the scene (e.g. moved to a different room, or the restore landed wrong) without being
    /// stuck. Wire to a held gesture / hidden button, or call from the inspector context menu.
    /// </summary>
    [ContextMenu("Recalibrate (erase saved anchor)")]
    public void Recalibrate()
    {
        EraseAndRecalibrate();
    }

    private async void EraseAndRecalibrate()
    {
        try
        {
            if (m_anchor != null && m_anchor.Created)
                await m_anchor.EraseAnchorAsync();
        }
        catch (Exception e) { Debug.LogException(e); }

        PlayerPrefs.DeleteKey(anchorPrefKey);
        PlayerPrefs.Save();

        // Detach the root from the anchor (keep its current world pose), then drop the anchor.
        if (sceneRoot != null) sceneRoot.SetParent(null, worldPositionStays: true);
        if (m_anchor != null) { Destroy(m_anchor.gameObject); m_anchor = null; }

        BeginCalibration();
    }
}

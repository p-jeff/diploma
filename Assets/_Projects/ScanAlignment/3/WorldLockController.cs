using Meta.XR.MRUtilityKit;
using UnityEngine;

public class WorldLockController : MonoBehaviour
{
    [SerializeField] private OVRManager _ovrManager;

    private MRUK _mruk;

    void Awake()
    {
        _mruk = MRUK.Instance;
    }

    /// <summary>
    /// Locks the world: enables MRUK world locking and disables recentering
    /// so the scene stays fixed in physical space even if the headset is handed off.
    /// </summary>
    public void LockWorld()
    {
        if (_mruk != null)
        {
            _mruk.EnableWorldLock = true;
        }

        if (_ovrManager != null)
        {
            _ovrManager.AllowRecenter = false;
        }

        Debug.Log("[WorldLockController] World locked. Recentering disabled.");
    }

    /// <summary>
    /// Unlocks the world: disables MRUK world locking and re-enables recentering.
    /// </summary>
    public void UnlockWorld()
    {
        if (_mruk != null)
        {
            _mruk.EnableWorldLock = false;
        }

        if (_ovrManager != null)
        {
            _ovrManager.AllowRecenter = true;
        }

        Debug.Log("[WorldLockController] World unlocked. Recentering enabled.");
    }

    /// <summary>
    /// Returns true if world lock is currently active (enabled and localized).
    /// </summary>
    public bool IsWorldLocked()
    {
        return _mruk != null && _mruk.IsWorldLockActive;
    }
}
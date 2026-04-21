using Oculus.Interaction;
using UnityEngine;
using UnityEngine.Serialization;

public class LockPosition : MonoBehaviour
{
    [SerializeField] private GameObject _grabInteraction;
    [SerializeField] private GameObject _handle;
    [SerializeField] private GameObject _staricase;
    [SerializeField] private WorldLockController _lockController;

    public void DisableGrab()
    {
        _grabInteraction.SetActive(false);
        _lockController.LockWorld();
        _handle.SetActive(false);
        _staricase.SetActive(true);
    }

    void Start()
    {
        _lockController.UnlockWorld();
        _handle.SetActive(true);
        _staricase.SetActive(false);
    }
    
}

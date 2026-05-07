using UnityEngine;
using UnityEngine.Events;
using UnityEngine.VFX;

namespace Midterms
{
    public class TouchGo : MonoBehaviour
    {
        [SerializeField] private VisualEffect vfx;
        [SerializeField] private UnityEvent onTouch;

        private void OnTriggerEnter(Collider other)
        {
            vfx.Stop();
            onTouch.Invoke();
        }
    }
}
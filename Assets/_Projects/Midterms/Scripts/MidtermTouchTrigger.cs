using UnityEngine;

namespace Midterms
{
    /// <summary>
    /// Periphery aura trigger. Place on a GameObject with a SphereCollider (isTrigger).
    /// Optionally has a child ParticleSystem for the visual aura.
    /// On first trigger contact with a hand-tagged collider, fires the sequence.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class MidtermTouchTrigger : MonoBehaviour
    {
        public MidtermSequenceController controller;

        [Tooltip("Layers that count as the user's hands.")]
        public LayerMask handLayers = ~0;

        [Tooltip("Tag a hand collider must have. Leave empty to skip tag check.")]
        public string handTag = "";

        [Tooltip("Particle system to stop once the sequence starts.")]
        public ParticleSystem auraParticles;

        bool m_fired;

        void Reset()
        {
            var c = GetComponent<Collider>();
            if (c) c.isTrigger = true;
        }

        void OnTriggerEnter(Collider other)
        {
            if (m_fired || controller == null) return;
            if (((1 << other.gameObject.layer) & handLayers) == 0) return;
            if (!string.IsNullOrEmpty(handTag) && !other.CompareTag(handTag)) return;

            m_fired = true;
            controller.Begin();

            if (auraParticles != null)
                auraParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
    }
}

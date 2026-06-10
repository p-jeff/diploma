using UnityEngine;

namespace _Projects._3._3.Audio
{
    public class AudioCtrl : MonoBehaviour
    {
        [SerializeField] private AudioSource[] audioSources;
        [SerializeField] private GameObject portalFrame;

        private void Start()
        {
            audioSources = GetComponentsInChildren<AudioSource>();
            foreach (var source in audioSources)
                source.Pause();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Player")) return;
            portalFrame.SetActive(false);
            foreach (var source in audioSources)
                source.Play();
        }

        private void OnTriggerExit(Collider other)
        {
            if (!other.CompareTag("Player")) return;
            portalFrame.SetActive(true);
            foreach (var source in audioSources)
                source.Pause();
        }
    }
}
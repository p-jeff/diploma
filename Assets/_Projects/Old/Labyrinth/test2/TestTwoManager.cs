using System.Collections.Generic;
using Oculus.Interaction;
using UnityEngine;

namespace _Projects.test2
{
    public class TestTwoManager : MonoBehaviour
    {
        [SerializeField] List<GameObject> closedSegments;
        [SerializeField] List<GameObject> openSegments;

        [SerializeField] AudioSource mythologyAudio;

        [SerializeField] Grabbable yarnBall;

        [Header("Debug")]
        [SerializeField] bool debugSimulateGrab;

        private bool wasGrabbed = false;

        // ── Wall swap ──────────────────────────────────────────────────────────

        void OpenExit()
        {
            foreach (var seg in closedSegments)
                if (seg != null) seg.SetActive(false);

            foreach (var seg in openSegments)
                if (seg != null) seg.SetActive(true);
        }

        void CloseExit()
        {
            foreach (var seg in closedSegments)
                if (seg != null) seg.SetActive(true);

            foreach (var seg in openSegments)
                if (seg != null) seg.SetActive(false);
        }

        // ── Grab detection ─────────────────────────────────────────────────────

        void Update()
        {
            if (yarnBall == null) return;

            bool isGrabbed = debugSimulateGrab || yarnBall.GrabPoints.Count > 0;

            if (isGrabbed && !wasGrabbed)
                OpenExit();
            else if (!isGrabbed && wasGrabbed)
                CloseExit();

            wasGrabbed = isGrabbed;
        }

        // ── Button callbacks (wire in Inspector) ───────────────────────────────

        public void SolutionButton()
        {
            OpenExit();
        }

        public void MythologyButton()
        {
            if (mythologyAudio != null)
                mythologyAudio.Play();
        }
    }
}

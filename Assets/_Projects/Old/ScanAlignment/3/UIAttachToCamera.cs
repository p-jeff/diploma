using System;
using UnityEngine;


namespace Utils
{
    public class UIAttachToCamera : MonoBehaviour
    {
        public Transform cameraAnchor; // Assign this in the inspector
        public bool followCamera = true;
        [SerializeField] private float _distance = 1f;

        private void Start()
        {
            if (cameraAnchor == null || cameraAnchor == null)
            {
                Debug.LogWarning("CameraAnchor is null or has no value assigned!");
            }
        }

        private void Update()
        {
            if (followCamera && cameraAnchor != null && cameraAnchor != null)
            {
                Vector3 newPosition = cameraAnchor.position + cameraAnchor.forward * _distance;
                transform.position = newPosition;
                transform.rotation = cameraAnchor.rotation;
            }
        }
    }
}
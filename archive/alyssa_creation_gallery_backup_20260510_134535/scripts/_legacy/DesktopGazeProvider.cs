using UnityEngine;

namespace AlgorithmicGallery
{
    /// <summary>
    /// Desktop gaze provider that simulates head gaze via the camera center.
    /// Raycasts from camera through the center of the screen (NOT from mouse position).
    /// This simulates a head-forward gaze, not eye-tracking.
    /// Attach this to the main camera.
    /// </summary>
    public class DesktopGazeProvider : MonoBehaviour, IGazeProvider
    {
        public bool IsAvailable { get; private set; }

        private Camera _camera;

        private void OnEnable()
        {
            _camera = GetComponent<Camera>();
            if (_camera == null)
            {
                Debug.LogError("DesktopGazeProvider must be attached to a GameObject with a Camera component.");
                IsAvailable = false;
                return;
            }
            IsAvailable = true;
        }

        private void OnDisable()
        {
            IsAvailable = false;
        }

        public Ray GetGazeRay()
        {
            // Ray from camera through the center of the screen
            return _camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        }
    }
}

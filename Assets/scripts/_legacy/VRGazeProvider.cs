using UnityEngine;
using UnityEngine.XR;

namespace AlgorithmicGallery
{
    /// <summary>
    /// VR gaze provider using XR HMD head-forward direction.
    /// Queries the HMD's center eye position and forward direction via XR input system.
    /// Falls back to camera forward if XR is unavailable.
    /// </summary>
    public class VRGazeProvider : MonoBehaviour, IGazeProvider
    {
        public bool IsAvailable { get; private set; }

        private Camera _camera;
        private InputDevice _hmdDevice;

        private void OnEnable()
        {
            _camera = GetComponent<Camera>();
            if (_camera == null)
            {
                Debug.LogError("VRGazeProvider must be attached to a GameObject with a Camera component.");
                IsAvailable = false;
                return;
            }

            // Try to get the HMD device
            var xrDevices = new System.Collections.Generic.List<InputDevice>();
            InputDevices.GetDevicesWithCharacteristics(
                InputDeviceCharacteristics.HeadMounted,
                xrDevices
            );

            if (xrDevices.Count > 0)
            {
                _hmdDevice = xrDevices[0];
                IsAvailable = _hmdDevice.isValid;
            }
            else
            {
                IsAvailable = false;
            }

            if (!IsAvailable)
            {
                Debug.LogWarning("VRGazeProvider: No XR HMD device found. Will use camera forward as fallback.");
            }
        }

        private void OnDisable()
        {
            IsAvailable = false;
        }

        public Ray GetGazeRay()
        {
            if (_hmdDevice.isValid)
            {
                // HMD center eye position and rotation
                if (_hmdDevice.TryGetFeatureValue(CommonUsages.centerEyePosition, out Vector3 hmdPosition))
                {
                    if (_hmdDevice.TryGetFeatureValue(CommonUsages.centerEyeRotation, out Quaternion hmdRotation))
                    {
                        return new Ray(hmdPosition, hmdRotation * Vector3.forward);
                    }
                }
            }

            // Fallback: use camera forward direction
            return new Ray(_camera.transform.position, _camera.transform.forward);
        }
    }
}

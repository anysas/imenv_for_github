using System;
using UnityEngine;

namespace AlgorithmicGallery
{
    /// <summary>
    /// Core gaze processing system. Raycasts every frame to detect which sculpture is being gazed at.
    /// Tracks dwell time, handles gaze exit with a grace period to prevent jitter.
    /// Reports gaze events to SculptureController and GalleryManager.
    /// </summary>
    public class GazeManager : MonoBehaviour
    {
        [Header("Configuration")]
        [SerializeField]
        private float _dwellThreshold = 0.6f; // seconds before triggering OnGazeDwell

        [SerializeField]
        private float _gazeExitGracePeriod = 0.8f;

        [SerializeField]
        private float _maxRayDistance = 50f;

        [Tooltip("Extra sphere radius around the ray endpoint to catch nearby sculpture colliders.")]
        [SerializeField]
        private float _gazeSphereCastRadius = 0.35f;
        [Tooltip("Half-angle (in degrees) for sculptures that can still receive peripheral gaze influence.")]
        [SerializeField]
        private float _visionConeHalfAngle = 35f;
        [Tooltip("Higher values tighten response toward the center of vision.")]
        [SerializeField]
        private float _coneResponseExponent = 1.35f;
        [SerializeField]
        private float _sculptureCacheRefreshInterval = 0.75f;
        [Tooltip("0 = no smoothing, higher values damp head jitter more.")]
        [SerializeField, Range(0f, 0.95f)]
        private float _gazeDirectionSmoothing = 0.45f;
        [SerializeField]
        private float _closeRangeDistance = 5f;
        [SerializeField]
        private float _farRangeDistance = 25f;

        [Header("References")]
        [SerializeField]
        private GalleryManager _galleryManager;

        private IGazeProvider _gazeProvider;
        private SculptureController _currentGazeTarget;
        private SculptureController _previousGazeTarget; // tracks old target during grace period
        private float _currentDwellTime;
        private float _gazeExitTimer;
        private bool _isWaitingForGazeExit;
        private Vector3 _smoothedDirection;
        private bool _hasSmoothedDirection;
        private float _currentGazeDistance = -1f;
        private SculptureController[] _cachedSculptures = Array.Empty<SculptureController>();
        private float _nextSculptureCacheRefreshTime;

        private void Start()
        {
            // Auto-detect gaze provider: try VR first, fall back to desktop
            _gazeProvider = GetComponent<VRGazeProvider>();
            if (_gazeProvider == null || !_gazeProvider.IsAvailable)
            {
                _gazeProvider = GetComponent<DesktopGazeProvider>();
            }

            // Last resort: auto-add desktop provider
            if (_gazeProvider == null || !_gazeProvider.IsAvailable)
            {
                var desktopProvider = gameObject.AddComponent<DesktopGazeProvider>();
                _gazeProvider = desktopProvider;
                Debug.Log("GazeManager: Auto-added DesktopGazeProvider.");
            }

            if (_galleryManager == null)
            {
                _galleryManager = FindFirstObjectByType<GalleryManager>();
                if (_galleryManager == null)
                    Debug.LogWarning("GazeManager: No GalleryManager found in scene. Gaze exits will not be reported.");
            }

            _currentGazeTarget = null;
            _previousGazeTarget = null;
            _currentDwellTime = 0f;
            _gazeExitTimer = 0f;
            _isWaitingForGazeExit = false;
            _hasSmoothedDirection = false;
            _nextSculptureCacheRefreshTime = 0f;
        }

        private void Update()
        {
            if (_gazeProvider == null || !_gazeProvider.IsAvailable)
                return;

            Ray rawGazeRay = _gazeProvider.GetGazeRay();
            Ray gazeRay = GetSmoothedGazeRay(rawGazeRay);
            RefreshSculptureCacheIfNeeded();
            ResetCenterednessSignals();
            SculptureController hitTarget = null;
            float hitDistance = -1f;

            EvaluateVisionConeTarget(gazeRay, out hitTarget, out hitDistance);

            // Continuous gaze signal drives growth directly every frame.
            if (_currentGazeTarget != null)
            {
                float distanceForCurrent = hitTarget == _currentGazeTarget ? hitDistance : -1f;
                _currentGazeTarget.SetGazeState(hitTarget == _currentGazeTarget, distanceForCurrent);
            }

            // Same target as before — just keep accumulating dwell
            if (hitTarget == _currentGazeTarget && hitTarget != null)
            {
                _isWaitingForGazeExit = false;
                _previousGazeTarget = null;
                _gazeExitTimer = 0f;
                _currentGazeDistance = hitDistance;
                _currentDwellTime += Time.deltaTime;

                if (_currentDwellTime >= _dwellThreshold)
                {
                    _currentGazeTarget.OnGazeDwell(_currentDwellTime);
                }
                return;
            }

            // Target changed: we're now looking at something different (or nothing)
            if (hitTarget != _currentGazeTarget)
            {
                if (_currentGazeTarget != null && !_isWaitingForGazeExit)
                {
                    // Start grace period — don't immediately call exit
                    _isWaitingForGazeExit = true;
                    _previousGazeTarget = _currentGazeTarget;
                    _gazeExitTimer = 0f;
                }

                if (hitTarget != null)
                {
                    // Immediately switch to new target if we hit something new
                    ConfirmGazeExit();
                    _currentGazeTarget = hitTarget;
                    _currentDwellTime = 0f;
                    _currentGazeDistance = hitDistance;
                    _currentGazeTarget.SetGazeState(true, hitDistance);
                }
            }

            // If we're in grace period but gaze returned to the previous target, cancel the exit.
            if (_isWaitingForGazeExit && hitTarget == _previousGazeTarget && hitTarget != null)
            {
                _isWaitingForGazeExit = false;
                _currentGazeTarget = _previousGazeTarget;
                _previousGazeTarget = null;
                _gazeExitTimer = 0f;
                _currentGazeDistance = hitDistance;
                _currentGazeTarget.SetGazeState(true, hitDistance);
                return;
            }

            if (_isWaitingForGazeExit && hitTarget == null)
            {
                _gazeExitTimer += Time.deltaTime;
                if (_gazeExitTimer >= _gazeExitGracePeriod)
                {
                    ConfirmGazeExit();
                    _currentGazeTarget = null;
                    _currentDwellTime = 0f;
                    _currentGazeDistance = -1f;
                }
            }
        }

        private void ConfirmGazeExit()
        {
            if (!_isWaitingForGazeExit) return;

            if (_previousGazeTarget != null)
            {
                _previousGazeTarget.SetGazeState(false, -1f);
                _previousGazeTarget.OnGazeExit();
                if (_galleryManager != null)
                {
                    _galleryManager.OnSculptureGazeExit(_previousGazeTarget);
                }
            }

            _previousGazeTarget = null;
            _isWaitingForGazeExit = false;
            _gazeExitTimer = 0f;
        }

        private void RefreshSculptureCacheIfNeeded()
        {
            if (Time.time < _nextSculptureCacheRefreshTime)
                return;

            _cachedSculptures = FindObjectsByType<SculptureController>(FindObjectsSortMode.None);
            _nextSculptureCacheRefreshTime = Time.time + Mathf.Max(0.1f, _sculptureCacheRefreshInterval);
        }

        private void ResetCenterednessSignals()
        {
            for (int i = 0; i < _cachedSculptures.Length; i++)
            {
                var sculpture = _cachedSculptures[i];
                if (sculpture != null)
                    sculpture.SetGazeCenteredness(0f);
            }
        }

        private void EvaluateVisionConeTarget(Ray gazeRay, out SculptureController bestTarget, out float bestDistance)
        {
            bestTarget = null;
            bestDistance = -1f;
            float bestCenteredness = 0f;

            float clampedHalfAngle = Mathf.Clamp(_visionConeHalfAngle, 1f, 89f);
            float coneCosThreshold = Mathf.Cos(clampedHalfAngle * Mathf.Deg2Rad);
            float responsePower = Mathf.Max(0.25f, _coneResponseExponent);

            for (int i = 0; i < _cachedSculptures.Length; i++)
            {
                var sculpture = _cachedSculptures[i];
                if (sculpture == null || !sculpture.isActiveAndEnabled)
                    continue;

                Vector3 focusPoint = GetSculptureFocusPoint(sculpture);
                Vector3 toSculpture = focusPoint - gazeRay.origin;
                float distance = toSculpture.magnitude;
                if (distance <= 0.001f || distance > _maxRayDistance)
                    continue;

                Vector3 direction = toSculpture / distance;
                float dot = Vector3.Dot(gazeRay.direction, direction);
                if (dot <= coneCosThreshold)
                    continue;

                float centeredness = Mathf.InverseLerp(coneCosThreshold, 1f, dot);
                centeredness = Mathf.Pow(Mathf.Clamp01(centeredness), responsePower);
                sculpture.SetGazeCenteredness(centeredness);

                if (centeredness > bestCenteredness)
                {
                    bestCenteredness = centeredness;
                    bestTarget = sculpture;
                    bestDistance = distance;
                }
            }

            if (bestTarget != null)
                bestTarget.SetGazeCenteredness(1f);
        }

        private static Vector3 GetSculptureFocusPoint(SculptureController sculpture)
        {
            var collider = sculpture.GetComponent<Collider>();
            if (collider != null)
                return collider.bounds.center;

            return sculpture.transform.position;
        }

        private Ray GetSmoothedGazeRay(Ray rawRay)
        {
            Vector3 rawDirection = rawRay.direction.normalized;
            if (!_hasSmoothedDirection)
            {
                _smoothedDirection = rawDirection;
                _hasSmoothedDirection = true;
            }
            else
            {
                float response = Mathf.Clamp01(1f - _gazeDirectionSmoothing);
                _smoothedDirection = Vector3.Slerp(_smoothedDirection, rawDirection, Mathf.Max(0.02f, response));
                _smoothedDirection.Normalize();
            }

            return new Ray(rawRay.origin, _smoothedDirection);
        }

        public float GetDistanceAttenuation01(float distance)
        {
            float near = Mathf.Max(0.01f, _closeRangeDistance);
            float far = Mathf.Max(near + 0.01f, _farRangeDistance);
            return Mathf.InverseLerp(far, near, distance);
        }

        public SculptureController CurrentGazeTarget => _currentGazeTarget;
        public float CurrentDwellTime => _currentDwellTime;
        public float CurrentGazeDistance => _currentGazeDistance;
    }
}

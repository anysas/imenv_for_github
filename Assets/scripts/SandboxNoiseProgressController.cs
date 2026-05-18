using UnityEngine;
using UnityEngine.Rendering;
using PSX;

namespace AlgorithmicGallery.Corruption
{
    /// <summary>
    /// Adjusts CRT noise and dithering over the player's 25 sandbox placements,
    /// then restores the starting values when the sandbox session completes.
    /// </summary>
    public class SandboxNoiseProgressController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private SandboxManager _sandbox;
        [SerializeField] private Volume _volume;
        [SerializeField] private VolumeProfile _volumeProfile;

        [Header("Progress ramp")]
        [Tooltip("CRT noise amount to reach by the player's final placement.")]
        [SerializeField] private float _targetNoiseAmount = 60f;
        [Tooltip("Dither threshold to reach by the player's final placement.")]
        [SerializeField] private float _targetDitherThreshold = 0.60f;
        [Tooltip("How quickly the runtime volume values ease toward the placement-based targets.")]
        [SerializeField] private float _responseSpeed = 3.5f;

        private Crt _crt;
        private Dithering _dithering;
        private bool _subscribed;
        private float _baselineNoiseAmount;
        private float _currentNoiseAmount;
        private float _runtimeTargetNoiseAmount;
        private float _baselineDitherThreshold;
        private float _currentDitherThreshold;
        private float _runtimeTargetDitherThreshold;
        private bool _runtimeReady;

        void Start()
        {
            ResolveReferences();
            Subscribe();
            CaptureBaseline();
            ApplyRuntimeValuesImmediate(_baselineNoiseAmount, _baselineDitherThreshold);
        }

        void Update()
        {
            if (!_runtimeReady)
            {
                ResolveReferences();
                if (!_runtimeReady)
                    return;
            }

            if (_sandbox == null || !_sandbox.SandboxActive || _sandbox.StyleProfile == null)
                return;

            float maxPlacements = Mathf.Max(1, _sandbox.MaxPlayerPlacements);
            float progress = Mathf.Clamp01(_sandbox.StyleProfile.PlayerPlacementCount / maxPlacements);
            float desiredNoiseAmount = Mathf.Lerp(_baselineNoiseAmount, _targetNoiseAmount, progress);
            float desiredDitherThreshold = Mathf.Lerp(_baselineDitherThreshold, _targetDitherThreshold, progress);

            float ease = 1f - Mathf.Exp(-_responseSpeed * Time.deltaTime);
            _runtimeTargetNoiseAmount = desiredNoiseAmount;
            _runtimeTargetDitherThreshold = desiredDitherThreshold;
            _currentNoiseAmount = Mathf.Lerp(_currentNoiseAmount, _runtimeTargetNoiseAmount, ease);
            _currentDitherThreshold = Mathf.Lerp(_currentDitherThreshold, _runtimeTargetDitherThreshold, ease);
            ApplyRuntimeValuesImmediate(_currentNoiseAmount, _currentDitherThreshold);
        }

        void OnDestroy()
        {
            Unsubscribe();
            if (_runtimeReady)
                ApplyRuntimeValuesImmediate(_baselineNoiseAmount, _baselineDitherThreshold);
        }

        private void ResolveReferences()
        {
            if (_sandbox == null)
                _sandbox = FindFirstObjectByType<SandboxManager>();

            if (_volume == null)
                _volume = FindFirstObjectByType<Volume>();

            if (_volumeProfile == null && _volume != null)
                _volumeProfile = _volume.profile;

            if (_crt == null && _volumeProfile != null)
                _volumeProfile.TryGet(out _crt);
            if (_dithering == null && _volumeProfile != null)
                _volumeProfile.TryGet(out _dithering);

            _runtimeReady = _crt != null && _dithering != null;
        }

        private void Subscribe()
        {
            if (_subscribed || _sandbox == null)
                return;

            _sandbox.OnSandboxEntered.AddListener(HandleSandboxEntered);
            _sandbox.OnSessionComplete.AddListener(HandleSessionComplete);
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed || _sandbox == null)
                return;

            _sandbox.OnSandboxEntered.RemoveListener(HandleSandboxEntered);
            _sandbox.OnSessionComplete.RemoveListener(HandleSessionComplete);
            _subscribed = false;
        }

        private void HandleSandboxEntered()
        {
            ResolveReferences();
            CaptureBaseline();
            ApplyRuntimeValuesImmediate(_baselineNoiseAmount, _baselineDitherThreshold);
        }

        private void HandleSessionComplete()
        {
            if (!_runtimeReady)
                return;

            _runtimeTargetNoiseAmount = _baselineNoiseAmount;
            _runtimeTargetDitherThreshold = _baselineDitherThreshold;
            _currentNoiseAmount = _baselineNoiseAmount;
            _currentDitherThreshold = _baselineDitherThreshold;
            ApplyRuntimeValuesImmediate(_baselineNoiseAmount, _baselineDitherThreshold);
        }

        private void CaptureBaseline()
        {
            if (!_runtimeReady)
                return;

            _baselineNoiseAmount = _crt.noiseAmount.value;
            _currentNoiseAmount = _baselineNoiseAmount;
            _runtimeTargetNoiseAmount = _baselineNoiseAmount;

            _baselineDitherThreshold = _dithering.ditherThreshold.value;
            _currentDitherThreshold = _baselineDitherThreshold;
            _runtimeTargetDitherThreshold = _baselineDitherThreshold;
        }

        private void ApplyRuntimeValuesImmediate(float noiseAmount, float ditherThreshold)
        {
            if (!_runtimeReady)
                return;

            _crt.noiseAmount.Override(noiseAmount);
            _dithering.ditherThreshold.Override(ditherThreshold);
        }
    }
}

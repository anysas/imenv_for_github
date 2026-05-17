using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace AlgorithmicGallery.Corruption
{
    // Drives short PSX glitch bursts tied to sandbox/assistant events.
    // Baseline intensity remains driven by AssistantSystem over session time.
    public class SandboxReactiveVfxDirector : MonoBehaviour
    {
        [Header("References (auto-resolve if null)")]
        [SerializeField] private SandboxManager _sandbox;
        [SerializeField] private AssistantSystem _assistant;
        [SerializeField] private PropPlacer _placer;
        [SerializeField] private VolumeProfile _volumeProfile;

        [Header("Bursts")]
        [SerializeField] private bool _enabled = true;
        [SerializeField] private float _sandboxEnterBurst = 0.10f;
        [SerializeField] private float _assistantActivateBurst = 0.24f;
        [SerializeField] private float _assistantPlacementBurst = 0.08f;
        [SerializeField] private float _phaseSuggestingBurst = 0.12f;
        [SerializeField] private float _phaseOverridingBurst = 0.18f;
        [SerializeField] private float _sessionCompleteBurst = 0.16f;
        [SerializeField] private float _burstDecayPerSecond = 2.8f;

        [Header("Volume Pulses (Optional)")]
        [SerializeField] private bool _driveVolumePulses = true;
        [SerializeField] private float _sandboxEnterChromaticPulse = 0.012f;
        [SerializeField] private float _sandboxEnterLensPulse = 0.015f;
        [SerializeField] private float _assistantActivateChromaticPulse = 0.045f;
        [SerializeField] private float _assistantActivateLensPulse = 0.06f;
        [SerializeField] private float _assistantPlacementChromaticPulse = 0.018f;
        [SerializeField] private float _assistantPlacementLensPulse = 0.02f;
        [SerializeField] private float _phaseSuggestingChromaticPulse = 0.025f;
        [SerializeField] private float _phaseSuggestingLensPulse = 0.03f;
        [SerializeField] private float _phaseOverridingChromaticPulse = 0.04f;
        [SerializeField] private float _phaseOverridingLensPulse = 0.05f;
        [SerializeField] private float _sessionCompleteChromaticPulse = 0.03f;
        [SerializeField] private float _sessionCompleteLensPulse = 0.04f;
        [SerializeField] private float _volumePulseDecayPerSecond = 3.5f;
        [SerializeField] private float _maxChromaticIntensity = 0.2f;
        [SerializeField] private float _minLensDistortion = -0.14f;

        [Header("Cooldowns")]
        [SerializeField] private float _placementBurstCooldown = 0.35f;
        [SerializeField] private float _phaseBurstCooldown = 1.0f;

        private float _nextPlacementBurstTime;
        private float _nextPhaseBurstTime;
        private AssistantPhase _lastPhase;
        private bool _subscribed;
        private ChromaticAberration _chromatic;
        private LensDistortion _lensDistortion;
        private bool _volumeReady;
        private float _baseChromaticIntensity;
        private float _baseLensDistortionIntensity;
        private float _chromaticImpulse;
        private float _lensImpulse;

        void Start()
        {
            ResolveReferences();
            Subscribe();
            ResolveVolumeOverrides();
            if (_assistant != null)
                _lastPhase = _assistant.Phase;
        }

        void Update()
        {
            if (!_enabled)
                return;

            if (_assistant == null)
                _assistant = FindFirstObjectByType<AssistantSystem>();
            if (_assistant == null || !_assistant.IsRunning)
            {
                UpdateVolumePulse(Time.deltaTime);
                return;
            }

            if (_assistant.Phase != _lastPhase)
            {
                TryPhaseBurst(_assistant.Phase);
                _lastPhase = _assistant.Phase;
            }

            UpdateVolumePulse(Time.deltaTime);
        }

        void OnDestroy()
        {
            Unsubscribe();
        }

        private void ResolveReferences()
        {
            if (_sandbox == null) _sandbox = FindFirstObjectByType<SandboxManager>();
            if (_assistant == null) _assistant = FindFirstObjectByType<AssistantSystem>();
            if (_placer == null) _placer = FindFirstObjectByType<PropPlacer>();
            if (_volumeProfile == null)
            {
                var volume = FindFirstObjectByType<Volume>();
                if (volume != null)
                    _volumeProfile = volume.profile;
            }
        }

        private void ResolveVolumeOverrides()
        {
            _volumeReady = false;
            if (!_driveVolumePulses || _volumeProfile == null)
                return;

            _volumeProfile.TryGet(out _chromatic);
            _volumeProfile.TryGet(out _lensDistortion);

            if (_chromatic != null)
                _baseChromaticIntensity = _chromatic.intensity.value;
            if (_lensDistortion != null)
                _baseLensDistortionIntensity = _lensDistortion.intensity.value;

            _volumeReady = _chromatic != null || _lensDistortion != null;
        }

        private void Subscribe()
        {
            if (_subscribed)
                return;

            if (_sandbox != null)
            {
                _sandbox.OnSandboxEntered.AddListener(HandleSandboxEntered);
                _sandbox.OnSessionComplete.AddListener(HandleSessionComplete);
            }
            if (_assistant != null)
                _assistant.OnActivated += HandleAssistantActivated;
            if (_placer != null)
                _placer.OnPropPlaced += HandlePropPlaced;

            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed)
                return;

            if (_sandbox != null)
            {
                _sandbox.OnSandboxEntered.RemoveListener(HandleSandboxEntered);
                _sandbox.OnSessionComplete.RemoveListener(HandleSessionComplete);
            }
            if (_assistant != null)
                _assistant.OnActivated -= HandleAssistantActivated;
            if (_placer != null)
                _placer.OnPropPlaced -= HandlePropPlaced;

            _subscribed = false;
        }

        private void HandleSandboxEntered()
        {
            CaptureCurrentVolumeBaseline();
            FireBurst(_sandboxEnterBurst, _sandboxEnterChromaticPulse, _sandboxEnterLensPulse);
        }

        private void HandleAssistantActivated()
        {
            FireBurst(_assistantActivateBurst, _assistantActivateChromaticPulse, _assistantActivateLensPulse);
        }

        private void HandleSessionComplete()
        {
            FireBurst(_sessionCompleteBurst, _sessionCompleteChromaticPulse, _sessionCompleteLensPulse);
        }

        private void HandlePropPlaced(bool isPlayerPlaced)
        {
            if (isPlayerPlaced || Time.time < _nextPlacementBurstTime)
                return;

            _nextPlacementBurstTime = Time.time + _placementBurstCooldown;
            FireBurst(_assistantPlacementBurst, _assistantPlacementChromaticPulse, _assistantPlacementLensPulse);
        }

        private void TryPhaseBurst(AssistantPhase phase)
        {
            if (Time.time < _nextPhaseBurstTime)
                return;

            _nextPhaseBurstTime = Time.time + _phaseBurstCooldown;

            switch (phase)
            {
                case AssistantPhase.Suggesting:
                    FireBurst(_phaseSuggestingBurst, _phaseSuggestingChromaticPulse, _phaseSuggestingLensPulse);
                    break;
                case AssistantPhase.Overriding:
                    FireBurst(_phaseOverridingBurst, _phaseOverridingChromaticPulse, _phaseOverridingLensPulse);
                    break;
            }
        }

        private void FireBurst(float glitchAmount, float chromaticAmount = 0f, float lensAmount = 0f)
        {
            if (!_enabled)
                return;

            if (glitchAmount > 0f)
                PSXRendererFeature.AddGlitchImpulse(glitchAmount, _burstDecayPerSecond);

            if (!_driveVolumePulses)
                return;

            _chromaticImpulse = Mathf.Clamp(_chromaticImpulse + Mathf.Max(0f, chromaticAmount), 0f, _maxChromaticIntensity);
            _lensImpulse = Mathf.Clamp(_lensImpulse + Mathf.Max(0f, lensAmount), 0f, Mathf.Abs(_minLensDistortion));
        }

        private void CaptureCurrentVolumeBaseline()
        {
            if (!_volumeReady)
                ResolveVolumeOverrides();
            if (!_volumeReady)
                return;

            if (_chromatic != null)
                _baseChromaticIntensity = _chromatic.intensity.value;
            if (_lensDistortion != null)
                _baseLensDistortionIntensity = _lensDistortion.intensity.value;
        }

        private void UpdateVolumePulse(float dt)
        {
            if (!_driveVolumePulses || !_enabled)
                return;
            if (!_volumeReady)
                ResolveVolumeOverrides();
            if (!_volumeReady)
                return;

            _chromaticImpulse = Mathf.MoveTowards(_chromaticImpulse, 0f, _volumePulseDecayPerSecond * dt);
            _lensImpulse = Mathf.MoveTowards(_lensImpulse, 0f, _volumePulseDecayPerSecond * dt);

            if (_chromatic != null)
            {
                float chroma = Mathf.Clamp(_baseChromaticIntensity + _chromaticImpulse, 0f, _maxChromaticIntensity);
                _chromatic.intensity.Override(chroma);
            }

            if (_lensDistortion != null)
            {
                float lens = Mathf.Clamp(_baseLensDistortionIntensity - _lensImpulse, _minLensDistortion, 1f);
                _lensDistortion.intensity.Override(lens);
            }
        }
    }
}

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using AlgorithmicGallery.Recommendation;

namespace AlgorithmicGallery
{
    /// <summary>
    /// Lerps URP Volume overrides based on the current recommendation phase.
    /// Fascination = cool/open, Recognition = warm/focused, Unease = oppressive/claustrophobic.
    /// Attach to the same GameObject as the Volume component, or assign the profile in Inspector.
    /// </summary>
    public class PhaseVolumeController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private GalleryManager _galleryManager;
        [SerializeField] private VolumeProfile _volumeProfile;

        [Header("Transition")]
        [SerializeField] private float _transitionSpeed = 0.8f;

        [Header("Debug")]
        [SerializeField] private bool _enableDebugOverride = false;
        [SerializeField] private ArcPhase _debugPhase = ArcPhase.Fascination;
        [SerializeField, Range(0f, 1f)] private float _debugProgress = 0f;

        // Cached volume overrides
        private Bloom _bloom;
        private Vignette _vignette;
        private ColorAdjustments _colorAdjustments;
        private FilmGrain _filmGrain;
        private ChromaticAberration _chromaticAberration;
        private LensDistortion _lensDistortion;

        // Smooth targets
        private float _targetBloomIntensity;
        private float _targetBloomScatter;
        private float _targetVignetteIntensity;
        private float _targetVignetteSmoothness;
        private float _targetSaturation;
        private float _targetContrast;
        private float _targetFilmGrainIntensity;
        private float _targetChromaticIntensity;
        private float _targetLensDistortionIntensity;
        private Color _targetBloomTint;
        private Color _targetColorFilter;

        // Smoothed current values
        private float _currentBloomIntensity;
        private float _currentBloomScatter;
        private float _currentVignetteIntensity;
        private float _currentVignetteSmoothness;
        private float _currentSaturation;
        private float _currentContrast;
        private float _currentFilmGrainIntensity;
        private float _currentChromaticIntensity;
        private float _currentLensDistortionIntensity;
        private Color _currentBloomTint;
        private Color _currentColorFilter;

        private ArcPhase _lastPhase = ArcPhase.Fascination;
        private bool _initialized = false;

        private void Start()
        {
            ResolveReferences();
            CacheVolumeOverrides();

            if (_initialized)
            {
                // Initialize to current phase immediately
                UpdateTargetsForPhase(GetCurrentPhase(), GetCurrentProgress());
                SnapToTargets();
            }
        }

        private void Update()
        {
            if (!_initialized)
                return;

            ArcPhase phase = GetCurrentPhase();
            float progress = GetCurrentProgress();

            UpdateTargetsForPhase(phase, progress);
            SmoothTowardsTargets(Time.deltaTime);
            ApplyToVolume();

            _lastPhase = phase;
        }

        private ArcPhase GetCurrentPhase()
        {
            if (_enableDebugOverride)
                return _debugPhase;
            if (_galleryManager != null)
                return _galleryManager.CurrentPhase;
            return ArcPhase.Fascination;
        }

        private float GetCurrentProgress()
        {
            if (_enableDebugOverride)
                return _debugProgress;
            if (_galleryManager != null)
                return _galleryManager.PhaseProgress;
            return 0f;
        }

        private void UpdateTargetsForPhase(ArcPhase phase, float progress)
        {
            switch (phase)
            {
                case ArcPhase.Fascination:
                    SetFascinationTargets(progress);
                    break;
                case ArcPhase.Recognition:
                    SetRecognitionTargets(progress);
                    break;
                case ArcPhase.Unease:
                    SetUneaseTargets(progress);
                    break;
            }
        }

        private void SetFascinationTargets(float progress)
        {
            _targetBloomIntensity = Mathf.Lerp(1.0f, 1.2f, progress);
            _targetBloomScatter = 0.65f;
            _targetBloomTint = new Color(0.7f, 0.85f, 1f); // cool cyan
            _targetVignetteIntensity = Mathf.Lerp(0.1f, 0.15f, progress);
            _targetVignetteSmoothness = 0.5f;
            _targetSaturation = Mathf.Lerp(-5f, 0f, progress);
            _targetContrast = 10f;
            _targetFilmGrainIntensity = 0.05f;
            _targetChromaticIntensity = 0f;
            _targetLensDistortionIntensity = 0f;
            _targetColorFilter = new Color(0.92f, 0.95f, 1f);
        }

        private void SetRecognitionTargets(float progress)
        {
            _targetBloomIntensity = Mathf.Lerp(1.2f, 1.8f, progress);
            _targetBloomScatter = Mathf.Lerp(0.65f, 0.75f, progress);
            _targetBloomTint = Color.Lerp(new Color(0.7f, 0.85f, 1f), new Color(1f, 0.8f, 0.5f), progress); // cool → warm amber
            _targetVignetteIntensity = Mathf.Lerp(0.15f, 0.28f, progress);
            _targetVignetteSmoothness = Mathf.Lerp(0.5f, 0.45f, progress);
            _targetSaturation = Mathf.Lerp(0f, 5f, progress);
            _targetContrast = Mathf.Lerp(10f, 18f, progress);
            _targetFilmGrainIntensity = Mathf.Lerp(0.05f, 0.12f, progress);
            _targetChromaticIntensity = Mathf.Lerp(0f, 0.08f, progress);
            _targetLensDistortionIntensity = 0f;
            _targetColorFilter = Color.Lerp(new Color(0.92f, 0.95f, 1f), new Color(1f, 0.95f, 0.88f), progress);
        }

        private void SetUneaseTargets(float progress)
        {
            _targetBloomIntensity = Mathf.Lerp(1.8f, 2.5f, progress);
            _targetBloomScatter = Mathf.Lerp(0.75f, 0.85f, progress);
            _targetBloomTint = Color.Lerp(new Color(1f, 0.8f, 0.5f), new Color(1f, 0.3f, 0.2f), progress); // amber → deep red
            _targetVignetteIntensity = Mathf.Lerp(0.28f, 0.45f, progress);
            _targetVignetteSmoothness = Mathf.Lerp(0.45f, 0.35f, progress);
            _targetSaturation = Mathf.Lerp(5f, 15f, progress);
            _targetContrast = Mathf.Lerp(18f, 25f, progress);
            _targetFilmGrainIntensity = Mathf.Lerp(0.12f, 0.22f, progress);
            _targetChromaticIntensity = Mathf.Lerp(0.08f, 0.18f, progress);
            _targetLensDistortionIntensity = Mathf.Lerp(0f, -0.08f, progress);
            _targetColorFilter = Color.Lerp(new Color(1f, 0.95f, 0.88f), new Color(1f, 0.85f, 0.82f), progress);
        }

        private void SmoothTowardsTargets(float dt)
        {
            float t = 1f - Mathf.Exp(-_transitionSpeed * dt * 4f);

            _currentBloomIntensity = Mathf.Lerp(_currentBloomIntensity, _targetBloomIntensity, t);
            _currentBloomScatter = Mathf.Lerp(_currentBloomScatter, _targetBloomScatter, t);
            _currentBloomTint = Color.Lerp(_currentBloomTint, _targetBloomTint, t);
            _currentVignetteIntensity = Mathf.Lerp(_currentVignetteIntensity, _targetVignetteIntensity, t);
            _currentVignetteSmoothness = Mathf.Lerp(_currentVignetteSmoothness, _targetVignetteSmoothness, t);
            _currentSaturation = Mathf.Lerp(_currentSaturation, _targetSaturation, t);
            _currentContrast = Mathf.Lerp(_currentContrast, _targetContrast, t);
            _currentFilmGrainIntensity = Mathf.Lerp(_currentFilmGrainIntensity, _targetFilmGrainIntensity, t);
            _currentChromaticIntensity = Mathf.Lerp(_currentChromaticIntensity, _targetChromaticIntensity, t);
            _currentLensDistortionIntensity = Mathf.Lerp(_currentLensDistortionIntensity, _targetLensDistortionIntensity, t);
            _currentColorFilter = Color.Lerp(_currentColorFilter, _targetColorFilter, t);
        }

        private void SnapToTargets()
        {
            _currentBloomIntensity = _targetBloomIntensity;
            _currentBloomScatter = _targetBloomScatter;
            _currentBloomTint = _targetBloomTint;
            _currentVignetteIntensity = _targetVignetteIntensity;
            _currentVignetteSmoothness = _targetVignetteSmoothness;
            _currentSaturation = _targetSaturation;
            _currentContrast = _targetContrast;
            _currentFilmGrainIntensity = _targetFilmGrainIntensity;
            _currentChromaticIntensity = _targetChromaticIntensity;
            _currentLensDistortionIntensity = _targetLensDistortionIntensity;
            _currentColorFilter = _targetColorFilter;
        }

        private void ApplyToVolume()
        {
            if (_bloom != null)
            {
                _bloom.intensity.Override(_currentBloomIntensity);
                _bloom.scatter.Override(_currentBloomScatter);
                _bloom.tint.Override(_currentBloomTint);
            }

            if (_vignette != null)
            {
                _vignette.intensity.Override(_currentVignetteIntensity);
                _vignette.smoothness.Override(_currentVignetteSmoothness);
            }

            if (_colorAdjustments != null)
            {
                _colorAdjustments.saturation.Override(_currentSaturation);
                _colorAdjustments.contrast.Override(_currentContrast);
                _colorAdjustments.colorFilter.Override(_currentColorFilter);
            }

            if (_filmGrain != null)
            {
                _filmGrain.intensity.Override(_currentFilmGrainIntensity);
            }

            if (_chromaticAberration != null)
            {
                _chromaticAberration.intensity.Override(_currentChromaticIntensity);
            }

            if (_lensDistortion != null)
            {
                _lensDistortion.intensity.Override(_currentLensDistortionIntensity);
            }
        }

        private void ResolveReferences()
        {
            if (_galleryManager == null)
                _galleryManager = FindFirstObjectByType<GalleryManager>();

            if (_volumeProfile == null)
            {
                var volume = GetComponent<Volume>();
                if (volume != null)
                    _volumeProfile = volume.profile;
            }

            if (_volumeProfile == null)
            {
                var volume = FindFirstObjectByType<Volume>();
                if (volume != null)
                    _volumeProfile = volume.profile;
            }

            if (_volumeProfile == null)
            {
                Debug.LogError("PhaseVolumeController: No VolumeProfile found. Assign one in Inspector or attach to a Volume GameObject.");
            }
        }

        private void CacheVolumeOverrides()
        {
            if (_volumeProfile == null)
                return;

            // Get or add overrides
            if (!_volumeProfile.TryGet(out _bloom))
            {
                _bloom = _volumeProfile.Add<Bloom>(true);
            }

            if (!_volumeProfile.TryGet(out _vignette))
            {
                _vignette = _volumeProfile.Add<Vignette>(true);
            }

            if (!_volumeProfile.TryGet(out _colorAdjustments))
            {
                _colorAdjustments = _volumeProfile.Add<ColorAdjustments>(true);
            }

            if (!_volumeProfile.TryGet(out _filmGrain))
            {
                _filmGrain = _volumeProfile.Add<FilmGrain>(true);
            }

            if (!_volumeProfile.TryGet(out _chromaticAberration))
            {
                _chromaticAberration = _volumeProfile.Add<ChromaticAberration>(true);
            }

            if (!_volumeProfile.TryGet(out _lensDistortion))
            {
                _lensDistortion = _volumeProfile.Add<LensDistortion>(true);
            }

            _initialized = true;
            Debug.Log("PhaseVolumeController: Volume overrides cached and ready.");
        }
    }
}

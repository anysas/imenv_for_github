using UnityEngine;

namespace AlgorithmicGallery
{
    /// <summary>
    /// Subtle atmospheric flicker for gallery accent lights.
    /// Uses Perlin noise for natural-feeling intensity variation.
    /// Phase-aware: nervous flicker in Unease, calm in Fascination.
    /// </summary>
    [RequireComponent(typeof(Light))]
    public class LightFlickerController : MonoBehaviour
    {
        [Header("Flicker Settings")]
        [SerializeField] private float _baseIntensityMultiplier = 1f;
        [SerializeField, Range(0f, 1f)] private float _flickerAmount = 0.15f;
        [SerializeField] private float _flickerSpeed = 2.5f;
        [SerializeField] private float _smoothness = 0.5f;

        [Header("Phase Response")]
        [SerializeField] private bool _phaseAware = true;
        [SerializeField] private float _uneaseFlickerBoost = 0.35f;
        [SerializeField] private float _uneaseSpeedBoost = 3f;

        [Header("References")]
        [SerializeField] private GalleryManager _galleryManager;

        private Light _light;
        private float _originalIntensity;
        private float _noiseOffset;
        private float _smoothedFlicker;

        private void OnEnable()
        {
            _light = GetComponent<Light>();
            _originalIntensity = _light.intensity;
            _noiseOffset = Random.Range(0f, 100f);
            _smoothedFlicker = 0f;

            if (_galleryManager == null)
                _galleryManager = FindFirstObjectByType<GalleryManager>();
        }

        private void Update()
        {
            if (_light == null)
                return;

            float dt = Time.deltaTime;
            float speed = _flickerSpeed;
            float amount = _flickerAmount;

            // Phase-driven modulation
            if (_phaseAware && _galleryManager != null)
            {
                float phaseProgress = Mathf.Clamp01(_galleryManager.PhaseProgress);
                var phase = _galleryManager.CurrentPhase;

                if (phase == Recommendation.ArcPhase.Unease)
                {
                    amount += _uneaseFlickerBoost * phaseProgress;
                    speed += _uneaseSpeedBoost * phaseProgress;
                }
                else if (phase == Recommendation.ArcPhase.Recognition)
                {
                    amount += _uneaseFlickerBoost * 0.3f * phaseProgress;
                    speed += _uneaseSpeedBoost * 0.2f * phaseProgress;
                }
            }

            // Perlin noise for natural flicker
            float time = Time.time * speed + _noiseOffset;
            float noise1 = Mathf.PerlinNoise(time, _noiseOffset);
            float noise2 = Mathf.PerlinNoise(time * 1.7f, _noiseOffset + 42.5f);
            float rawFlicker = (noise1 * 0.7f + noise2 * 0.3f) * 2f - 1f; // -1 to 1

            // Smooth the result
            float smoothT = 1f - Mathf.Exp(-Mathf.Max(0.1f, 1f - _smoothness) * dt * 20f);
            _smoothedFlicker = Mathf.Lerp(_smoothedFlicker, rawFlicker, smoothT);

            // Apply
            float intensity = _originalIntensity * _baseIntensityMultiplier * (1f + _smoothedFlicker * amount);
            _light.intensity = Mathf.Max(0f, intensity);
        }

        private void OnDisable()
        {
            if (_light != null)
                _light.intensity = _originalIntensity;
        }
    }
}

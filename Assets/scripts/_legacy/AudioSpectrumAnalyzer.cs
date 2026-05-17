using UnityEngine;

namespace AlgorithmicGallery
{
    /// <summary>
    /// Core audio spectrum analysis component.
    /// Extracts bass, mids, and highs bands from an AudioSource,
    /// smooths the output, and provides beat detection for bass onsets.
    /// Other components read the public properties to drive visual reactivity.
    /// </summary>
    public class AudioSpectrumAnalyzer : MonoBehaviour
    {
        [Header("Source")]
        [SerializeField] private AudioSource _audioSource;

        [Header("FFT")]
        [SerializeField] private FFTWindow _fftWindow = FFTWindow.BlackmanHarris;
        private const int SpectrumSamples = 256;

        [Header("Band Ranges (Hz)")]
        [SerializeField] private float _bassMin = 20f;
        [SerializeField] private float _bassMax = 250f;
        [SerializeField] private float _midsMin = 250f;
        [SerializeField] private float _midsMax = 2000f;
        [SerializeField] private float _highsMin = 2000f;
        [SerializeField] private float _highsMax = 16000f;

        [Header("Smoothing")]
        [SerializeField, Range(0f, 0.99f)] private float _smoothDecay = 0.85f;
        [SerializeField] private float _riseSpeed = 8f;

        [Header("Beat Detection")]
        [SerializeField] private float _beatThreshold = 0.6f;
        [SerializeField] private float _beatCooldownSeconds = 0.2f;

        [Header("Normalization")]
        [SerializeField] private float _autoGainSpeed = 0.5f;
        [SerializeField] private float _maxGainMultiplier = 5f;

        private float[] _spectrum;
        private float _sampleRate;

        // Smoothed band values
        private float _rawBass;
        private float _rawMids;
        private float _rawHighs;
        private float _smoothBass;
        private float _smoothMids;
        private float _smoothHighs;

        // Beat detection
        private float _lastBeatTime = float.NegativeInfinity;
        private bool _beatDetected;
        private float _bassHistory;

        // Auto-gain
        private float _gainMultiplier = 1f;
        private float _peakTracker = 0.01f;

        /// <summary>Smoothed bass band energy (0-1 normalized).</summary>
        public float Bass => Mathf.Clamp01(_smoothBass * _gainMultiplier);

        /// <summary>Smoothed mids band energy (0-1 normalized).</summary>
        public float Mids => Mathf.Clamp01(_smoothMids * _gainMultiplier);

        /// <summary>Smoothed highs band energy (0-1 normalized).</summary>
        public float Highs => Mathf.Clamp01(_smoothHighs * _gainMultiplier);

        /// <summary>True for exactly one Update frame when a bass beat is detected.</summary>
        public bool BeatDetected => _beatDetected;

        /// <summary>Raw bass value before normalization.</summary>
        public float RawBass => _rawBass;

        private void Start()
        {
            _spectrum = new float[SpectrumSamples];
            _sampleRate = AudioSettings.outputSampleRate;

            if (_audioSource == null)
                _audioSource = GetComponent<AudioSource>();

            if (_audioSource == null)
            {
                Debug.LogWarning("AudioSpectrumAnalyzer: No AudioSource found. Audio reactivity disabled.");
            }
        }

        private void Update()
        {
            _beatDetected = false;

            if (_audioSource == null || !_audioSource.isPlaying)
                return;

            _audioSource.GetSpectrumData(_spectrum, 0, _fftWindow);

            _rawBass = ComputeBandEnergy(_bassMin, _bassMax);
            _rawMids = ComputeBandEnergy(_midsMin, _midsMax);
            _rawHighs = ComputeBandEnergy(_highsMin, _highsMax);

            float dt = Time.deltaTime;

            // Smooth with asymmetric rise/fall
            _smoothBass = SmoothBand(_smoothBass, _rawBass, dt);
            _smoothMids = SmoothBand(_smoothMids, _rawMids, dt);
            _smoothHighs = SmoothBand(_smoothHighs, _rawHighs, dt);

            // Auto-gain normalization
            float currentPeak = Mathf.Max(_rawBass, Mathf.Max(_rawMids, _rawHighs));
            _peakTracker = Mathf.Lerp(_peakTracker, Mathf.Max(0.01f, currentPeak), _autoGainSpeed * dt);
            _gainMultiplier = Mathf.Clamp(1f / Mathf.Max(0.01f, _peakTracker), 1f, _maxGainMultiplier);

            // Beat detection
            float normalizedBass = _smoothBass * _gainMultiplier;
            bool beatCandidate = normalizedBass > _beatThreshold && normalizedBass > _bassHistory * 1.3f;
            float timeSinceLastBeat = Time.time - _lastBeatTime;

            if (beatCandidate && timeSinceLastBeat > _beatCooldownSeconds)
            {
                _beatDetected = true;
                _lastBeatTime = Time.time;
            }

            _bassHistory = Mathf.Lerp(_bassHistory, normalizedBass, 0.3f);
        }

        private float ComputeBandEnergy(float minHz, float maxHz)
        {
            float freqPerBin = _sampleRate / 2f / SpectrumSamples;
            int minBin = Mathf.FloorToInt(minHz / freqPerBin);
            int maxBin = Mathf.CeilToInt(maxHz / freqPerBin);
            minBin = Mathf.Clamp(minBin, 0, SpectrumSamples - 1);
            maxBin = Mathf.Clamp(maxBin, minBin, SpectrumSamples - 1);

            float energy = 0f;
            int count = 0;
            for (int i = minBin; i <= maxBin; i++)
            {
                energy += _spectrum[i];
                count++;
            }

            return count > 0 ? energy / count : 0f;
        }

        private float SmoothBand(float current, float target, float dt)
        {
            if (target > current)
            {
                // Rise quickly
                return Mathf.Lerp(current, target, 1f - Mathf.Exp(-_riseSpeed * dt));
            }
            else
            {
                // Decay slowly
                return Mathf.Lerp(current, target, 1f - _smoothDecay);
            }
        }
    }
}

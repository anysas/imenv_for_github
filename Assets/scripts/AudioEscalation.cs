using UnityEngine;

namespace AlgorithmicGallery.Corruption
{
    // Drives an AudioSource's volume / pitch / lowpass from AssistantSystem.Influence.
    // Optional: assign a base ambient loop to the AudioSource and a pulse SFX for phase changes.
    [RequireComponent(typeof(AudioSource))]
    public class AudioEscalation : MonoBehaviour
    {
        [SerializeField] private AssistantSystem _assistant;
        [SerializeField] private AnimationCurve _volumeCurve = AnimationCurve.EaseInOut(0f, 0.2f, 1f, 1f);
        [SerializeField] private AnimationCurve _pitchCurve  = AnimationCurve.EaseInOut(0f, 1f, 1f, 1.4f);
        [SerializeField] private AudioClip _phaseChangeSfx;

        private AudioSource _src;
        private AssistantPhase _lastPhase;

        void Awake()
        {
            _src = GetComponent<AudioSource>();
            _src.loop = true;
        }

        void Start()
        {
            if (_assistant == null) _assistant = FindFirstObjectByType<AssistantSystem>();
            if (_assistant != null) _lastPhase = _assistant.Phase;

            if (_src.clip != null && !_src.isPlaying)
                _src.Play();
        }

        void Update()
        {
            if (_assistant == null || !_assistant.IsRunning) return;

            float t = Mathf.Clamp01(_assistant.Influence);
            _src.volume = _volumeCurve.Evaluate(t);
            _src.pitch  = _pitchCurve.Evaluate(t);

            if (_assistant.Phase != _lastPhase)
            {
                _lastPhase = _assistant.Phase;
                if (_phaseChangeSfx != null)
                    _src.PlayOneShot(_phaseChangeSfx, 0.7f);
            }
        }
    }
}

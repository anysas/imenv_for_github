using UnityEngine;

namespace AlgorithmicGallery.Corruption
{
    // Lightweight runtime SFX layer for sandbox interactions.
    // Uses inspector clips when assigned; otherwise generates simple tone clips as fallback.
    [RequireComponent(typeof(AudioSource))]
    public class SandboxSfx : MonoBehaviour
    {
        [Header("References (auto-resolve if null)")]
        [SerializeField] private SandboxManager _sandbox;
        [SerializeField] private HotbarController _hotbar;
        [SerializeField] private PropPlacer _placer;
        [SerializeField] private AssistantSystem _assistant;

        [Header("Volumes")]
        [SerializeField] private float _uiVolume = 0.18f;
        [SerializeField] private float _placeVolume = 0.25f;
        [SerializeField] private float _removeVolume = 0.2f;
        [SerializeField] private float _assistantVolume = 0.3f;
        [SerializeField] private float _sessionVolume = 0.35f;
        [SerializeField] private bool _playPlacementOneShots = false;

        [Header("Optional Clip Overrides")]
        [SerializeField] private AudioClip _slotChangeClip;
        [SerializeField] private AudioClip _placeClip;
        [SerializeField] private AudioClip _removeClip;
        [SerializeField] private AudioClip _sandboxEnterClip;
        [SerializeField] private AudioClip _assistantActivateClip;
        [SerializeField] private AudioClip _sessionCompleteClip;

        private AudioSource _source;

        void Awake()
        {
            _source = GetComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f; // UI/system feedback should be global
            _source.loop = false;

            ResolveReferences();
            EnsureFallbackClips();
        }

        void OnEnable()
        {
            ResolveReferences();
            Subscribe();
        }

        void OnDisable()
        {
            Unsubscribe();
        }

        private void ResolveReferences()
        {
            if (_sandbox == null) _sandbox = FindFirstObjectByType<SandboxManager>();
            if (_hotbar == null) _hotbar = FindFirstObjectByType<HotbarController>();
            if (_placer == null) _placer = FindFirstObjectByType<PropPlacer>();
            if (_assistant == null) _assistant = FindFirstObjectByType<AssistantSystem>();
        }

        private void Subscribe()
        {
            if (_sandbox != null)
            {
                _sandbox.OnSandboxEntered.AddListener(HandleSandboxEntered);
                _sandbox.OnSessionComplete.AddListener(HandleSessionComplete);
            }
            if (_hotbar != null)
                _hotbar.OnActiveSlotChanged += HandleActiveSlotChanged;
            if (_placer != null)
            {
                _placer.OnPropPlaced += HandlePropPlaced;
                _placer.OnPropRemoved += HandlePropRemoved;
            }
            if (_assistant != null)
                _assistant.OnActivated += HandleAssistantActivated;
        }

        private void Unsubscribe()
        {
            if (_sandbox != null)
            {
                _sandbox.OnSandboxEntered.RemoveListener(HandleSandboxEntered);
                _sandbox.OnSessionComplete.RemoveListener(HandleSessionComplete);
            }
            if (_hotbar != null)
                _hotbar.OnActiveSlotChanged -= HandleActiveSlotChanged;
            if (_placer != null)
            {
                _placer.OnPropPlaced -= HandlePropPlaced;
                _placer.OnPropRemoved -= HandlePropRemoved;
            }
            if (_assistant != null)
                _assistant.OnActivated -= HandleAssistantActivated;
        }

        private void HandleActiveSlotChanged(int _)
        {
            Play(_slotChangeClip, _uiVolume);
        }

        private void HandlePropPlaced(bool isPlayer)
        {
            if (!_playPlacementOneShots)
                return;
            float vol = isPlayer ? _placeVolume : _placeVolume * 0.65f;
            Play(_placeClip, vol);
        }

        private void HandlePropRemoved()
        {
            Play(_removeClip, _removeVolume);
        }

        private void HandleSandboxEntered()
        {
            Play(_sandboxEnterClip, _sessionVolume);
        }

        private void HandleAssistantActivated()
        {
            Play(_assistantActivateClip, _assistantVolume);
        }

        private void HandleSessionComplete()
        {
            Play(_sessionCompleteClip, _sessionVolume);
        }

        private void Play(AudioClip clip, float volume)
        {
            if (_source == null || clip == null)
                return;

            _source.pitch = Random.Range(0.97f, 1.03f);
            _source.PlayOneShot(clip, Mathf.Clamp01(volume));
        }

        private void EnsureFallbackClips()
        {
            if (_slotChangeClip == null) _slotChangeClip = CreateToneClip("sfx_slot", 880f, 0.045f, 0.2f);
            if (_placeClip == null) _placeClip = CreateToneClip("sfx_place", 520f, 0.06f, 0.28f);
            if (_removeClip == null) _removeClip = CreateToneClip("sfx_remove", 280f, 0.08f, 0.24f);
            if (_sandboxEnterClip == null) _sandboxEnterClip = CreateSweepClip("sfx_enter", 340f, 620f, 0.22f, 0.25f);
            if (_assistantActivateClip == null) _assistantActivateClip = CreateSweepClip("sfx_activate", 420f, 980f, 0.18f, 0.3f);
            if (_sessionCompleteClip == null) _sessionCompleteClip = CreateSweepClip("sfx_complete", 620f, 260f, 0.32f, 0.24f);
        }

        private static AudioClip CreateToneClip(string name, float frequency, float duration, float amplitude)
        {
            int sampleRate = 44100;
            int sampleCount = Mathf.Max(1, Mathf.RoundToInt(sampleRate * duration));
            float[] data = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                float t = i / (float)sampleRate;
                float env = Mathf.Sin((i / (float)sampleCount) * Mathf.PI);
                data[i] = Mathf.Sin(2f * Mathf.PI * frequency * t) * amplitude * env;
            }
            var clip = AudioClip.Create(name, sampleCount, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip CreateSweepClip(string name, float f0, float f1, float duration, float amplitude)
        {
            int sampleRate = 44100;
            int sampleCount = Mathf.Max(1, Mathf.RoundToInt(sampleRate * duration));
            float[] data = new float[sampleCount];
            float phase = 0f;
            for (int i = 0; i < sampleCount; i++)
            {
                float k = i / (float)(sampleCount - 1);
                float freq = Mathf.Lerp(f0, f1, k);
                phase += (2f * Mathf.PI * freq) / sampleRate;
                float env = Mathf.Sin(k * Mathf.PI);
                data[i] = Mathf.Sin(phase) * amplitude * env;
            }
            var clip = AudioClip.Create(name, sampleCount, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}

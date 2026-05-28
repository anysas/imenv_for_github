using System;
using UnityEngine;

namespace AlgorithmicGallery.Corruption
{
    /// <summary>
    /// Drag-and-drop <see cref="AudioClip"/>s in the Inspector for sandbox gameplay feedback.
    /// Auto-subscribes to sterilization, grid repositioning, and player placement.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class SandboxGameplaySfx : MonoBehaviour
    {
        public static SandboxGameplaySfx Instance { get; private set; }
        private const string SfxResourcePrefix = "Sfx/";

        [Header("Sound clips (drag files here)")]
        [Tooltip("Played when the system starts turning a prop white / sterile.")]
        [SerializeField] private AudioClip _objectTurnedWhiteClip;
        [Tooltip("Played when the system moves a prop onto the grid (deferred snap, full relayout, etc.).")]
        [SerializeField] private AudioClip _objectMovedOnGridClip;
        [Tooltip("Played when the player successfully places a prop.")]
        [SerializeField] private AudioClip _playerPlacedBlockClip;

        [Header("Volumes")]
        [SerializeField, Range(0f, 1f)] private float _sterilizeVolume = 0.38f;
        [SerializeField, Range(0f, 1f)] private float _gridMoveVolume = 0.32f;
        [SerializeField, Range(0f, 1f)] private float _playerPlaceVolume = 0.4f;

        [Header("Playback")]
        [SerializeField] private bool _randomizePitch = true;
        [SerializeField] private Vector2 _pitchRange = new(0.96f, 1.04f);
        [Tooltip("Minimum XZ movement (metres) before a grid-move sound plays.")]
        [SerializeField] private float _gridMoveMinDistance = 0.08f;

        private AudioSource _source;

        void Awake()
        {
            _source = GetComponent<AudioSource>();
            if (_source == null)
                _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;
            _source.loop = false;
            LoadDefaultClipsFromResources();

            if (!Application.isPlaying)
                return;

            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[SandboxGameplaySfx] Duplicate instance destroyed.");
                Destroy(this);
                return;
            }

            Instance = this;
        }

        private void LoadDefaultClipsFromResources()
        {
            if (_playerPlacedBlockClip == null)
                _playerPlacedBlockClip = Resources.Load<AudioClip>(SfxResourcePrefix + "PlayerPlace");
            if (_objectTurnedWhiteClip == null)
                _objectTurnedWhiteClip = Resources.Load<AudioClip>(SfxResourcePrefix + "PropSterilize");
            if (_objectMovedOnGridClip == null)
                _objectMovedOnGridClip = Resources.Load<AudioClip>(SfxResourcePrefix + "PropGridMove");
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        /// <summary>Call when the player successfully places a prop.</summary>
        public static void NotifyPlayerPlaced()
        {
            EnsureInstance();
            Instance?.PlayPlayerPlaced();
        }

        private void PlayPlayerPlaced()
        {
            PlayClip(_playerPlacedBlockClip, _playerPlaceVolume);
        }

        private void HandlePropSterilizationStarted(GameObject prop)
        {
            PlayClip(_objectTurnedWhiteClip, _sterilizeVolume);
        }

        private void HandlePropMovedOnGrid(GameObject prop, Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            delta.y = 0f;
            if (delta.sqrMagnitude < _gridMoveMinDistance * _gridMoveMinDistance)
                return;

            PlayClip(_objectMovedOnGridClip, _gridMoveVolume);
        }

        /// <summary>Call from gameplay code when a prop is reassigned to a grid cell.</summary>
        public static void NotifyPropMovedOnGrid(GameObject prop, Vector3 from, Vector3 to)
        {
            EnsureInstance();
            Instance?.HandlePropMovedOnGrid(prop, from, to);
        }

        /// <summary>Call when the system begins bleaching a prop white.</summary>
        public static void NotifyPropSterilizationStarted(GameObject prop)
        {
            EnsureInstance();
            Instance?.HandlePropSterilizationStarted(prop);
        }

        private static void EnsureInstance()
        {
            if (Instance != null)
                return;

            Instance = FindFirstObjectByType<SandboxGameplaySfx>();
            if (Instance != null)
                return;

            var go = new GameObject("SandboxGameplaySfx");
            go.AddComponent<AudioSource>();
            Instance = go.AddComponent<SandboxGameplaySfx>();
        }

        private void PlayClip(AudioClip clip, float volume)
        {
            if (_source == null)
                return;

            if (clip == null)
                clip = CreateFallbackPlacementClip();

            if (_randomizePitch)
                _source.pitch = UnityEngine.Random.Range(_pitchRange.x, _pitchRange.y);
            else
                _source.pitch = 1f;

            _source.PlayOneShot(clip, Mathf.Clamp01(volume));
        }

        private static AudioClip CreateFallbackPlacementClip()
        {
            const int sampleRate = 44100;
            const float duration = 0.05f;
            const float frequency = 520f;
            const float amplitude = 0.24f;
            int sampleCount = Mathf.Max(1, Mathf.RoundToInt(sampleRate * duration));
            float[] data = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                float t = i / (float)sampleRate;
                float env = Mathf.Sin((i / (float)sampleCount) * Mathf.PI);
                data[i] = Mathf.Sin(2f * Mathf.PI * frequency * t) * amplitude * env;
            }

            var clip = AudioClip.Create("sfx_place_fallback", sampleCount, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}

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
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;
            _source.loop = false;

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

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        /// <summary>Call when the player successfully places a prop.</summary>
        public static void NotifyPlayerPlaced()
        {
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
            Instance?.HandlePropMovedOnGrid(prop, from, to);
        }

        /// <summary>Call when the system begins bleaching a prop white.</summary>
        public static void NotifyPropSterilizationStarted(GameObject prop)
        {
            Instance?.HandlePropSterilizationStarted(prop);
        }

        private void PlayClip(AudioClip clip, float volume)
        {
            if (_source == null || clip == null)
                return;

            if (_randomizePitch)
                _source.pitch = UnityEngine.Random.Range(_pitchRange.x, _pitchRange.y);
            else
                _source.pitch = 1f;

            _source.PlayOneShot(clip, Mathf.Clamp01(volume));
        }
    }
}

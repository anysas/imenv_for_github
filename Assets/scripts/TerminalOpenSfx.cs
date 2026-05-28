using UnityEngine;

namespace AlgorithmicGallery.Corruption
{
    /// <summary>
    /// Plays a looping ambience while the terminal prompt UI is open.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class TerminalOpenSfx : MonoBehaviour
    {
        private const string TerminalOpenResourcePath = "Sfx/TerminalOpen";

        [SerializeField] private ThemeSelectionUI _themeSelectionUI;
        [SerializeField] private AudioClip _openClip;
        [SerializeField, Range(0f, 1f)] private float _volume = 0.55f;
        [SerializeField] private bool _loopWhileOpen = true;
        [SerializeField, Range(0f, 1f)] private float _spatialBlend = 0f;
        [SerializeField] private float _fadeOutDuration = 0.35f;

        private AudioSource _source;
        private bool _playing;
        private float _fadeVelocity;

        void Awake()
        {
            _source = GetComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.loop = _loopWhileOpen;
            _source.spatialBlend = _spatialBlend;

            if (_openClip == null)
                _openClip = Resources.Load<AudioClip>(TerminalOpenResourcePath);
        }

        void OnEnable()
        {
            ResolveThemeSelectionUi();
            Subscribe();
        }

        void OnDisable()
        {
            Unsubscribe();
            StopOpenSoundImmediate();
        }

        void Update()
        {
            ResolveThemeSelectionUi();
            if (_themeSelectionUI == null)
                return;

            if (_themeSelectionUI.IsOpen)
            {
                if (!_playing)
                    StartOpenSound();
            }
            else if (_playing)
            {
                FadeOutAndStop();
            }
        }

        private void ResolveThemeSelectionUi()
        {
            if (_themeSelectionUI == null)
            {
                _themeSelectionUI = FindFirstObjectByType<ThemeSelectionUI>();
                if (_themeSelectionUI != null)
                    Subscribe();
            }
        }

        private void Subscribe()
        {
            if (_themeSelectionUI == null)
                return;

            _themeSelectionUI.OnPromptSelected -= HandleTerminalClosed;
            _themeSelectionUI.OnPromptSelected += HandleTerminalClosed;
            _themeSelectionUI.OnUiFadeOutComplete -= HandleTerminalFadeOutComplete;
            _themeSelectionUI.OnUiFadeOutComplete += HandleTerminalFadeOutComplete;
        }

        private void Unsubscribe()
        {
            if (_themeSelectionUI == null)
                return;

            _themeSelectionUI.OnPromptSelected -= HandleTerminalClosed;
            _themeSelectionUI.OnUiFadeOutComplete -= HandleTerminalFadeOutComplete;
        }

        private void StartOpenSound()
        {
            if (_source == null || _openClip == null)
                return;

            _source.clip = _openClip;
            _source.loop = _loopWhileOpen;
            _source.spatialBlend = _spatialBlend;
            _source.volume = _volume;
            _source.Play();
            _playing = true;
        }

        private void HandleTerminalClosed(PromptDefinition _)
        {
            FadeOutAndStop();
        }

        private void HandleTerminalFadeOutComplete()
        {
            FadeOutAndStop();
        }

        private void FadeOutAndStop()
        {
            if (!_playing || _source == null)
                return;

            if (_fadeOutDuration <= 0f)
            {
                StopOpenSoundImmediate();
                return;
            }

            _source.volume = Mathf.SmoothDamp(
                _source.volume,
                0f,
                ref _fadeVelocity,
                Mathf.Max(0.05f, _fadeOutDuration));

            if (_source.volume <= 0.01f)
                StopOpenSoundImmediate();
        }

        private void StopOpenSoundImmediate()
        {
            if (_source == null)
                return;

            if (_source.isPlaying)
                _source.Stop();

            _source.volume = _volume;
            _fadeVelocity = 0f;
            _playing = false;
        }
    }
}

using System.Collections;
using UnityEngine;

namespace AlgorithmicGallery.Corruption
{
    /// <summary>
    /// Plays an assigned clip while the intake terminal prompt HUD is open.
    /// Place on a GameObject near the terminal and assign <see cref="_openClip"/> in the Inspector.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class TerminalOpenSfx : MonoBehaviour
    {
        [SerializeField] private ThemeSelectionUI _themeSelectionUI;
        [SerializeField] private AudioClip _openClip;
        [SerializeField, Range(0f, 1f)] private float _volume = 0.5f;
        [Tooltip("When enabled, the clip loops until the player submits a prompt.")]
        [SerializeField] private bool _loopWhileOpen = true;
        [Tooltip("0 = 2D (HUD), 1 = 3D at this object's position.")]
        [SerializeField, Range(0f, 1f)] private float _spatialBlend = 0f;
        [SerializeField] private float _fadeOutDuration = 0.45f;

        private AudioSource _source;
        private bool _playing;
        private bool _subscribed;
        private Coroutine _subscribeRoutine;
        private Coroutine _fadeOutRoutine;

        void Awake()
        {
            _source = GetComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.loop = _loopWhileOpen;
            _source.spatialBlend = _spatialBlend;
        }

        void OnEnable()
        {
            _subscribeRoutine = StartCoroutine(SubscribeWhenReady());
        }

        void OnDisable()
        {
            if (_subscribeRoutine != null)
            {
                StopCoroutine(_subscribeRoutine);
                _subscribeRoutine = null;
            }

            Unsubscribe();
            StopOpenSound();
        }

        private IEnumerator SubscribeWhenReady()
        {
            while (_themeSelectionUI == null)
            {
                ResolveThemeSelectionUi();
                if (_themeSelectionUI == null)
                    yield return null;
            }

            if (!_subscribed)
            {
                _themeSelectionUI.OnPromptHudOpened += HandleTerminalOpened;
                _themeSelectionUI.OnPromptSelected += HandleTerminalClosed;
                _subscribed = true;
            }

            if (_themeSelectionUI.IsOpen)
                HandleTerminalOpened();
        }

        private void Unsubscribe()
        {
            if (!_subscribed || _themeSelectionUI == null)
                return;

            _themeSelectionUI.OnPromptHudOpened -= HandleTerminalOpened;
            _themeSelectionUI.OnPromptSelected -= HandleTerminalClosed;
            _subscribed = false;
        }

        private void ResolveThemeSelectionUi()
        {
            if (_themeSelectionUI == null)
                _themeSelectionUI = FindFirstObjectByType<ThemeSelectionUI>();
        }

        private void HandleTerminalOpened()
        {
            if (_openClip == null || _source == null || _playing)
                return;

            if (_fadeOutRoutine != null)
            {
                StopCoroutine(_fadeOutRoutine);
                _fadeOutRoutine = null;
            }

            _source.clip = _openClip;
            _source.loop = _loopWhileOpen;
            _source.volume = _volume;
            _source.spatialBlend = _spatialBlend;
            _source.Play();
            _playing = true;
        }

        private void HandleTerminalClosed(PromptDefinition _)
        {
            if (!_playing || _source == null)
                return;

            if (_fadeOutRoutine != null)
                StopCoroutine(_fadeOutRoutine);

            if (_fadeOutDuration <= 0f)
            {
                StopOpenSoundImmediate();
                return;
            }

            _fadeOutRoutine = StartCoroutine(FadeOutAndStop());
        }

        private IEnumerator FadeOutAndStop()
        {
            float startVolume = _source.volume;
            float duration = Mathf.Max(0.01f, _fadeOutDuration);
            float t = 0f;

            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / duration);
                _source.volume = Mathf.Lerp(startVolume, 0f, k);
                yield return null;
            }

            StopOpenSoundImmediate();
            _fadeOutRoutine = null;
        }

        private void StopOpenSound()
        {
            if (_fadeOutRoutine != null)
            {
                StopCoroutine(_fadeOutRoutine);
                _fadeOutRoutine = null;
            }

            StopOpenSoundImmediate();
        }

        private void StopOpenSoundImmediate()
        {
            if (!_playing || _source == null)
                return;

            _source.Stop();
            _source.volume = _volume;
            _playing = false;
        }
    }
}

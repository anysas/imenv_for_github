using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace AlgorithmicGallery.Corruption
{
    [RequireComponent(typeof(Collider))]
    public class DioramaProximityTrigger : MonoBehaviour
    {
        [SerializeField] private SandboxManager _sandbox;
        [SerializeField] private string _playerTag = "Player";
        [SerializeField] private bool _onlyActiveAfterSessionComplete = true;
        [Tooltip("Seconds after the player enters this trigger before fade-to-black starts.")]
        [SerializeField] private float _triggerDelay = 5f;
        [Tooltip("Fade-to-black duration immediately before reload.")]
        [SerializeField] private float _fadeDuration = 2f;

        private bool _active;
        private bool _restartCommitted;
        private Coroutine _countdownRoutine;
        private CanvasGroup _fadeCanvasGroup;
        private Collider _collider;

        void Awake()
        {
            _collider = GetComponent<Collider>();
            _collider.isTrigger = true;

            if (_onlyActiveAfterSessionComplete)
            {
                _active = false;
                _collider.enabled = false;
                enabled = false;
            }
            else
            {
                _active = true;
            }
        }

        void Start()
        {
            if (_sandbox == null)
                _sandbox = FindFirstObjectByType<SandboxManager>();

            if (_onlyActiveAfterSessionComplete && _sandbox != null)
                _sandbox.OnSessionComplete.AddListener(Arm);
        }

        void OnDestroy()
        {
            if (_onlyActiveAfterSessionComplete && _sandbox != null)
                _sandbox.OnSessionComplete.RemoveListener(Arm);
        }

        /// <summary>Arms trigger collider and behaviour (called on session complete or when spawned late).</summary>
        public void Arm()
        {
            _active = true;
            enabled = true;
            if (_collider != null)
                _collider.enabled = true;
            GameplayEventDebugLog.Push("DioramaTrigger", "armed (session complete)");
        }

        void OnTriggerEnter(Collider other)
        {
            if (!_active || _restartCommitted) return;
            if (!IsPlayer(other)) return;
            if (_countdownRoutine != null) return;
            GameplayEventDebugLog.Push("DioramaTrigger", "player entered → restart countdown");
            _countdownRoutine = StartCoroutine(CountdownThenFade());
        }

        void OnTriggerExit(Collider other)
        {
            if (!IsPlayer(other)) return;
            if (_countdownRoutine == null || _restartCommitted) return;
            GameplayEventDebugLog.Push("DioramaTrigger", "player left → countdown cancelled");
            StopCoroutine(_countdownRoutine);
            _countdownRoutine = null;
        }

        private bool IsPlayer(Collider other)
        {
            return other.CompareTag(_playerTag)
                   || other.GetComponentInParent<SimplePlayerRig>() != null
                   || other.GetComponentInParent<CharacterController>() != null;
        }

        private IEnumerator CountdownThenFade()
        {
            float holdBeforeFade = Mathf.Max(0f, _triggerDelay);
            float fadeDur = Mathf.Max(0.01f, _fadeDuration);

            float t = 0f;
            while (t < holdBeforeFade)
            {
                t += Time.deltaTime;
                yield return null;
            }

            EnsureFadeCanvas();
            if (_fadeCanvasGroup == null)
                yield break;

            _restartCommitted = true;

            float ft = 0f;
            while (ft < fadeDur)
            {
                ft += Time.deltaTime;
                _fadeCanvasGroup.alpha = Mathf.Clamp01(ft / fadeDur);
                yield return null;
            }

            _fadeCanvasGroup.alpha = 1f;

            var scene = SceneManager.GetActiveScene();
            GameplayEventDebugLog.Push("DioramaTrigger", $"fade done → reload scene \"{scene.name}\" (#{scene.buildIndex})");
            SceneManager.LoadScene(scene.buildIndex);
        }

        private void EnsureFadeCanvas()
        {
            if (_fadeCanvasGroup != null) return;

            var go = new GameObject("DioramaFadeCanvas");
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;
            go.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            go.AddComponent<GraphicRaycaster>();

            _fadeCanvasGroup = go.AddComponent<CanvasGroup>();
            _fadeCanvasGroup.alpha = 0f;
            _fadeCanvasGroup.blocksRaycasts = false;
            _fadeCanvasGroup.interactable = false;

            var bg = new GameObject("Bg");
            bg.transform.SetParent(go.transform, false);
            var rt = bg.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var img = bg.AddComponent<Image>();
            img.color = Color.black;
            img.raycastTarget = false;
        }
    }
}

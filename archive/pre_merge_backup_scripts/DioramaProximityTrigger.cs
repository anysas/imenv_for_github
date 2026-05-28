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
        [SerializeField] private float _triggerDelay = 10f;
        [SerializeField] private float _fadeDuration = 2f;

        private bool _active;
        private Coroutine _countdownRoutine;
        private CanvasGroup _fadeCanvasGroup;

        void Awake()
        {
            var col = GetComponent<Collider>();
            col.isTrigger = true;
        }

        void Start()
        {
            if (_sandbox == null)
                _sandbox = FindFirstObjectByType<SandboxManager>();

            _active = !_onlyActiveAfterSessionComplete;

            if (_onlyActiveAfterSessionComplete && _sandbox != null)
                _sandbox.OnSessionComplete.AddListener(Enable);
        }

        void OnDestroy()
        {
            if (_onlyActiveAfterSessionComplete && _sandbox != null)
                _sandbox.OnSessionComplete.RemoveListener(Enable);
        }

        private void Enable()
        {
            _active = true;
            GameplayEventDebugLog.Push("DioramaTrigger", "armed (session complete)");
        }

        void OnTriggerEnter(Collider other)
        {
            if (!_active) return;
            if (!IsPlayer(other)) return;
            if (_countdownRoutine != null) return;
            GameplayEventDebugLog.Push("DioramaTrigger", "player entered → linger countdown");
            _countdownRoutine = StartCoroutine(CountdownThenFade());
        }

        void OnTriggerExit(Collider other)
        {
            if (!IsPlayer(other)) return;
            if (_countdownRoutine == null) return;
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
            float t = 0f;
            float delay = Mathf.Max(0f, _triggerDelay);
            while (t < delay)
            {
                t += Time.deltaTime;
                yield return null;
            }

            EnsureFadeCanvas();
            if (_fadeCanvasGroup == null)
                yield break;

            float ft = 0f;
            float dur = Mathf.Max(0.01f, _fadeDuration);
            while (ft < dur)
            {
                ft += Time.deltaTime;
                _fadeCanvasGroup.alpha = Mathf.Clamp01(ft / dur);
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


using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace AlgorithmicGallery.Corruption
{
    // End-of-session UI. Builds itself at runtime, hidden until OnSessionComplete.
    // Shows a brief reflection: how much the player placed vs. how much the assistant placed,
    // dominant tags the assistant learned, and a final fade-to-black.
    public class EndCard : MonoBehaviour
    {
        [SerializeField] private SandboxManager _sandbox;
        [SerializeField] private Color _backgroundColor = new Color(0f, 0f, 0f, 0.92f);
        [SerializeField] private float _fadeInDuration = 2f;
        [Header("Finale camera")]
        [SerializeField] private bool _switchToFinaleCamera = true;
        [SerializeField] private Camera _finaleCamera;
        [SerializeField] private string _finaleCameraName = "FinaleCamera";
        [Header("Audio")]
        [SerializeField] private AudioClip _yayClip;
        [SerializeField] private float _yayVolume = 1f;

        private CanvasGroup _canvasGroup;
        private Text _titleText;
        private Text _bodyText;
        private Button _replayButton;
        private bool _shown;
        private AudioSource _audioSource;

        void Awake()
        {
            _audioSource = gameObject.AddComponent<AudioSource>();
            _audioSource.playOnAwake = false;
            _audioSource.spatialBlend = 0f;
            BuildCanvas();
        }

        void Start()
        {
            if (_sandbox == null) _sandbox = FindFirstObjectByType<SandboxManager>();
            if (_sandbox != null)
                _sandbox.OnSessionComplete.AddListener(Show);
        }

        private void BuildCanvas()
        {
            var canvasGO = new GameObject("EndCardCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            _canvasGroup = canvasGO.AddComponent<CanvasGroup>();
            _canvasGroup.alpha = 0f;
            _canvasGroup.blocksRaycasts = false;

            var bg = new GameObject("Background");
            bg.transform.SetParent(canvasGO.transform, false);
            var bgRect = bg.AddComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;
            var bgImg = bg.AddComponent<Image>();
            bgImg.color = _backgroundColor;
            bgImg.raycastTarget = false;

            var titleGO = new GameObject("Title");
            titleGO.transform.SetParent(canvasGO.transform, false);
            var titleRect = titleGO.AddComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0.1f, 0.6f);
            titleRect.anchorMax = new Vector2(0.9f, 0.8f);
            titleRect.offsetMin = Vector2.zero;
            titleRect.offsetMax = Vector2.zero;
            _titleText = titleGO.AddComponent<Text>();
            _titleText.font = UiFontResolver.LoadVt323OrFallback();
            _titleText.fontSize = 56;
            _titleText.fontStyle = FontStyle.Bold;
            _titleText.color = new Color(1f, 0.55f, 0.2f);
            _titleText.alignment = TextAnchor.MiddleCenter;
            _titleText.text = "It built you.";

            var bodyGO = new GameObject("Body");
            bodyGO.transform.SetParent(canvasGO.transform, false);
            var bodyRect = bodyGO.AddComponent<RectTransform>();
            bodyRect.anchorMin = new Vector2(0.15f, 0.25f);
            bodyRect.anchorMax = new Vector2(0.85f, 0.58f);
            bodyRect.offsetMin = Vector2.zero;
            bodyRect.offsetMax = Vector2.zero;
            _bodyText = bodyGO.AddComponent<Text>();
            _bodyText.font = UiFontResolver.LoadVt323OrFallback();
            _bodyText.fontSize = 22;
            _bodyText.color = new Color(0.92f, 0.92f, 0.92f);
            _bodyText.alignment = TextAnchor.UpperCenter;
            _bodyText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _bodyText.text = "";

            var replayGO = new GameObject("ReplayButton");
            replayGO.transform.SetParent(canvasGO.transform, false);
            var replayRect = replayGO.AddComponent<RectTransform>();
            replayRect.anchorMin = new Vector2(0.42f, 0.1f);
            replayRect.anchorMax = new Vector2(0.58f, 0.18f);
            replayRect.offsetMin = Vector2.zero;
            replayRect.offsetMax = Vector2.zero;

            var replayImage = replayGO.AddComponent<Image>();
            replayImage.color = new Color(1f, 1f, 1f, 0.12f);
            _replayButton = replayGO.AddComponent<Button>();
            _replayButton.targetGraphic = replayImage;
            _replayButton.onClick.AddListener(RestartScene);

            var replayLabelGO = new GameObject("Label");
            replayLabelGO.transform.SetParent(replayGO.transform, false);
            var replayLabelRect = replayLabelGO.AddComponent<RectTransform>();
            replayLabelRect.anchorMin = Vector2.zero;
            replayLabelRect.anchorMax = Vector2.one;
            replayLabelRect.offsetMin = Vector2.zero;
            replayLabelRect.offsetMax = Vector2.zero;

            var replayLabel = replayLabelGO.AddComponent<Text>();
            replayLabel.font = UiFontResolver.LoadVt323OrFallback();
            replayLabel.fontSize = 28;
            replayLabel.fontStyle = FontStyle.Bold;
            replayLabel.alignment = TextAnchor.MiddleCenter;
            replayLabel.color = Color.white;
            replayLabel.text = "Replay";
        }

        public void Show()
        {
            if (_shown) return;
            _shown = true;

            // Immediately unlock cursor and disable player movement
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            // Hide the hotbar
            var hotbarUI = FindFirstObjectByType<HotbarUI>();
            if (hotbarUI != null)
            {
                var hbCg = hotbarUI.GetComponentInChildren<CanvasGroup>();
                if (hbCg != null) { hbCg.alpha = 0f; hbCg.blocksRaycasts = false; }
            }

            // Freeze player movement
            var playerRig = FindFirstObjectByType<SimplePlayerRig>();
            if (playerRig != null) playerRig.enabled = false;

            SwitchToFinaleCamera();
            PlayYay();

            if (_sandbox == null) _sandbox = FindFirstObjectByType<SandboxManager>();

            string promptText = _sandbox?.SelectedPrompt?.DisplayText;
            if (!string.IsNullOrEmpty(promptText))
            {
                _titleText.text = $"Your \"{promptText}\" is complete.";
            }
            else
            {
                _titleText.text = "Your creation is complete.";
            }

            // Show placement stats to underscore the gap
            if (_sandbox?.StyleProfile != null)
            {
                var profile = _sandbox.StyleProfile;
                int playerCount = profile.PlayerPlacementCount;
                int assistantCount = profile.AssistantPlacementCount;
                _bodyText.text = $"You placed {playerCount} objects.\nIt placed {assistantCount}.";
            }
            else
            {
                _bodyText.text = "";
            }

            StartCoroutine(FadeIn());
        }

        private System.Collections.IEnumerator FadeIn()
        {
            float elapsed = 0f;
            while (elapsed < _fadeInDuration)
            {
                elapsed += Time.deltaTime;
                _canvasGroup.alpha = Mathf.Clamp01(elapsed / _fadeInDuration);
                yield return null;
            }
            if (_canvasGroup != null)
                _canvasGroup.blocksRaycasts = true;
        }

        private void SwitchToFinaleCamera()
        {
            if (!_switchToFinaleCamera)
                return;

            Camera target = _finaleCamera;
            if (target == null && !string.IsNullOrWhiteSpace(_finaleCameraName))
            {
                var go = GameObject.Find(_finaleCameraName);
                if (go != null)
                    target = go.GetComponent<Camera>();
            }
            if (target == null)
                return;

            var allCameras = FindObjectsByType<Camera>(FindObjectsSortMode.None);
            foreach (var cam in allCameras)
                cam.enabled = cam == target;
        }

        private void PlayYay()
        {
            if (_audioSource == null || _yayClip == null)
                return;

            _audioSource.PlayOneShot(_yayClip, Mathf.Clamp01(_yayVolume));
        }

        private void RestartScene()
        {
            var active = SceneManager.GetActiveScene();
            SceneManager.LoadScene(active.buildIndex);
        }
    }
}

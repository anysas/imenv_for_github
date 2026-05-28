using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections;

namespace AlgorithmicGallery.Corruption
{
    // Runtime-built UGUI hotbar. No prefab required — constructs Canvas + slot panels on Awake.
    // Listens to HotbarController events to update visuals.
    [RequireComponent(typeof(HotbarController))]
    public class HotbarUI : MonoBehaviour
    {
        [Header("Layout")]
        [SerializeField] private float _slotSize = 176f;
        [SerializeField] private float _slotSpacing = 24f;
        [SerializeField] private float _bottomMargin = 64f;

        [Header("Rendering")]
        [Tooltip("If true, HUD renders via camera and receives post FX. If false, HUD is overlay (most readable).")]
        [SerializeField] private bool _renderThroughCamera = false;
        [Tooltip("Optional camera when rendering through camera mode. Falls back to Camera.main.")]
        [SerializeField] private Camera _uiCameraOverride;
        [Tooltip("If set and override is empty, tries to find a camera GameObject with this exact name.")]
        [SerializeField] private string _preferredUiCameraName = "UICamera_CRTOnly";

        [Header("Colors")]
        [SerializeField] private Color _slotColor = new Color(0.08f, 0.08f, 0.10f, 0.85f);
        [SerializeField] private Color _activeSlotColor = new Color(1f, 0.55f, 0.20f, 0.95f);
        [SerializeField] private Color _slotBorderColor = new Color(1f, 1f, 1f, 0.4f);
        [SerializeField] private Color _labelColor = new Color(0.95f, 0.95f, 0.95f, 1f);

        [Header("Typography")]
        [SerializeField] private int _keyFontSize = 18;
        [SerializeField] private int _labelFontSize = 18;
        [SerializeField] private int _loadingDotsFontSize = 68;
        [SerializeField] private int _hintFontSize = 26;
        [SerializeField] private int _placementsRemainingFontSize = 46;
        [SerializeField] private bool _showControlHints = false;
        [SerializeField] private bool _showSlotKeyLabels = false;

        private HotbarController _hotbar;
        private RectTransform[] _slotRects;
        private Image[] _slotBackgrounds;
        private Image[] _slotIcons;
        private Text[] _slotLabels;
        private Text[] _slotKeyLabels;
        private Text[] _slotLoadingDots;
        private CanvasGroup _canvasGroup;
        private SandboxManager _sandboxManager;
        private Font _uiFont;
        private Canvas _canvas;
        private float _thumbnailRefreshTimer;
        private Sprite[] _fallbackSprites;
        private bool[] _slotWaitingForModel;

        private Coroutine _thinkingDotsRoutine;
        private Coroutine _pulseRoutine;
        private PropPlacer _propPlacer;
        private Text _placementsRemainingText;

        private static readonly string[] ThinkingDotPatterns = { ".", "..", "..." };

        void Awake()
        {
            _hotbar = GetComponent<HotbarController>();
            _uiFont = LoadBuiltinFontSafe();
            BuildCanvas();
            TryAssignCanvasCamera();
        }

        void Start()
        {
            // Subscribe after HotbarController.Initialize has likely been called by SandboxManager
            _hotbar.OnSlotChanged += HandleSlotChanged;
            _hotbar.OnActiveSlotChanged += HandleActiveSlotChanged;
            _hotbar.OnThinkingStateChanged += HandleThinkingStateChanged;

            // Initial sync (slots may already have content from Initialize)
            for (int i = 0; i < HotbarController.SlotCount; i++)
                HandleSlotChanged(i, _hotbar.Slots[i]);
            HandleActiveSlotChanged(_hotbar.ActiveSlot);
            TryAssignCanvasCamera();

            _sandboxManager = FindFirstObjectByType<SandboxManager>();
            if (_sandboxManager != null)
            {
                _sandboxManager.OnSandboxEntered.AddListener(HandleSandboxEntered);
                _sandboxManager.OnSessionComplete.AddListener(HandleSessionComplete);
                if (_sandboxManager.SandboxActive)
                    HandleSandboxEntered();
            }
            else
            {
                // Fail open for non-sandbox scenes that still use the hotbar.
                HandleSandboxEntered();
            }

            _propPlacer = FindFirstObjectByType<PropPlacer>();
            if (_propPlacer != null)
            {
                _propPlacer.OnThinkingBlockedClick += HandleThinkingBlockedClick;
                _propPlacer.OnPropPlaced += HandlePropPlaced;
            }

            RefreshPlacementsRemainingText();
        }

        void Update()
        {
            // Recover from early-order thumbnail misses (e.g. capture rig/spawner not ready yet).
            _thumbnailRefreshTimer -= Time.deltaTime;
            if (_thumbnailRefreshTimer > 0f)
                return;
            _thumbnailRefreshTimer = 1.0f;

            if (_hotbar == null || _slotIcons == null)
                return;

            for (int i = 0; i < HotbarController.SlotCount; i++)
            {
                if (_slotIcons[i] == null || _slotIcons[i].sprite != null)
                    continue;

                PropEntry prop = _hotbar.Slots[i];
                if (prop != null)
                    HandleSlotChanged(i, prop);
            }
        }

        void OnDestroy()
        {
            if (_hotbar != null)
            {
                _hotbar.OnSlotChanged -= HandleSlotChanged;
                _hotbar.OnActiveSlotChanged -= HandleActiveSlotChanged;
                _hotbar.OnThinkingStateChanged -= HandleThinkingStateChanged;
            }

            if (_propPlacer != null)
            {
                _propPlacer.OnThinkingBlockedClick -= HandleThinkingBlockedClick;
                _propPlacer.OnPropPlaced -= HandlePropPlaced;
            }

            if (_sandboxManager != null)
            {
                _sandboxManager.OnSandboxEntered.RemoveListener(HandleSandboxEntered);
                _sandboxManager.OnSessionComplete.RemoveListener(HandleSessionComplete);
            }
        }

        private void BuildCanvas()
        {
            var canvasGO = new GameObject("HotbarCanvas");
            canvasGO.transform.SetParent(transform, false);
            _canvas = canvasGO.AddComponent<Canvas>();
            _canvas.renderMode = _renderThroughCamera ? RenderMode.ScreenSpaceCamera : RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 100;
            if (_renderThroughCamera)
                _canvas.planeDistance = 1f;
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGO.AddComponent<GraphicRaycaster>();
            _canvasGroup = canvasGO.AddComponent<CanvasGroup>();
            _canvasGroup.alpha = 0f;
            _canvasGroup.blocksRaycasts = false;
            _canvasGroup.interactable = false;

            // Container
            var containerGO = new GameObject("HotbarContainer");
            containerGO.transform.SetParent(canvasGO.transform, false);
            var containerRect = containerGO.AddComponent<RectTransform>();
            containerRect.anchorMin = new Vector2(0.5f, 0f);
            containerRect.anchorMax = new Vector2(0.5f, 0f);
            containerRect.pivot = new Vector2(0.5f, 0f);
            containerRect.anchoredPosition = new Vector2(0f, _bottomMargin);

            float totalWidth = HotbarController.SlotCount * _slotSize + (HotbarController.SlotCount - 1) * _slotSpacing;
            containerRect.sizeDelta = new Vector2(totalWidth, _slotSize);

            _slotRects = new RectTransform[HotbarController.SlotCount];
            _slotBackgrounds = new Image[HotbarController.SlotCount];
            _slotIcons = new Image[HotbarController.SlotCount];
            _slotLabels = new Text[HotbarController.SlotCount];
            _slotKeyLabels = new Text[HotbarController.SlotCount];
            _slotLoadingDots = new Text[HotbarController.SlotCount];
            _fallbackSprites = new Sprite[HotbarController.SlotCount];
            _slotWaitingForModel = new bool[HotbarController.SlotCount];

            for (int i = 0; i < HotbarController.SlotCount; i++)
                BuildSlot(containerRect, i, totalWidth);

            if (_showControlHints)
                BuildControlHints(canvasGO.transform);

            BuildPlacementsRemainingHud(containerRect);
        }

        private void BuildPlacementsRemainingHud(RectTransform hotbarContainer)
        {
            var hudGO = new GameObject("PlacementsRemaining");
            hudGO.transform.SetParent(hotbarContainer, false);
            var rect = hudGO.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, 12f);
            rect.sizeDelta = new Vector2(560f, 72f);

            _placementsRemainingText = hudGO.AddComponent<Text>();
            _placementsRemainingText.font = _uiFont;
            _placementsRemainingText.fontSize = _placementsRemainingFontSize;
            _placementsRemainingText.fontStyle = FontStyle.Bold;
            _placementsRemainingText.alignment = TextAnchor.MiddleCenter;
            _placementsRemainingText.color = _labelColor;
            _placementsRemainingText.horizontalOverflow = HorizontalWrapMode.Overflow;
            _placementsRemainingText.verticalOverflow = VerticalWrapMode.Overflow;
            _placementsRemainingText.text = "Placements left: —";

            var outline = hudGO.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.75f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);
        }

        private void BuildControlHints(Transform canvasRoot)
        {
            var hintGO = new GameObject("ControlHints");
            hintGO.transform.SetParent(canvasRoot, false);
            var hintRect = hintGO.AddComponent<RectTransform>();
            hintRect.anchorMin = new Vector2(1f, 0f);
            hintRect.anchorMax = new Vector2(1f, 0f);
            hintRect.pivot = new Vector2(1f, 0f);
            hintRect.anchoredPosition = new Vector2(-36f, 24f);
            hintRect.sizeDelta = new Vector2(420f, 96f);

            var hintText = hintGO.AddComponent<Text>();
            hintText.font = _uiFont;
            hintText.fontSize = _hintFontSize;
            hintText.fontStyle = FontStyle.Bold;
            hintText.alignment = TextAnchor.LowerRight;
            hintText.color = new Color(1f, 1f, 1f, 0.65f);
            hintText.text = "Left click - place\nQ / E - rotate\nScroll - change slot";
            hintText.horizontalOverflow = HorizontalWrapMode.Overflow;
            hintText.verticalOverflow = VerticalWrapMode.Overflow;

            var hintOutline = hintGO.AddComponent<Outline>();
            hintOutline.effectColor = new Color(0f, 0f, 0f, 0.75f);
            hintOutline.effectDistance = new Vector2(1.5f, -1.5f);
        }

        private void HandleSandboxEntered()
        {
            if (_canvasGroup == null)
                return;

            _canvasGroup.alpha = 1f;
            _canvasGroup.blocksRaycasts = true;
            _canvasGroup.interactable = true;
            RefreshPlacementsRemainingText();
        }

        private void HandlePropPlaced(bool _)
        {
            RefreshPlacementsRemainingText();
        }

        private void HandleSessionComplete()
        {
            RefreshPlacementsRemainingText();
        }

        private void RefreshPlacementsRemainingText()
        {
            if (_placementsRemainingText == null)
                return;
            if (_sandboxManager != null)
                _placementsRemainingText.text = $"Placements left: {_sandboxManager.GetPlacementsLeft()}";
            else
                _placementsRemainingText.text = "Placements left: —";
        }

        private void BuildSlot(RectTransform parent, int index, float totalWidth)
        {
            var slotGO = new GameObject($"Slot_{index}");
            slotGO.transform.SetParent(parent, false);
            var rect = slotGO.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 0f);
            rect.pivot = new Vector2(0f, 0f);
            rect.sizeDelta = new Vector2(_slotSize, _slotSize);
            rect.anchoredPosition = new Vector2(index * (_slotSize + _slotSpacing), 0f);

            var bg = slotGO.AddComponent<Image>();
            bg.color = _slotColor;
            _slotRects[index] = rect;
            _slotBackgrounds[index] = bg;

            // Border (Outline component)
            var outline = slotGO.AddComponent<Outline>();
            outline.effectColor = _slotBorderColor;
            outline.effectDistance = new Vector2(2, -2);

            // Icon (centered, slightly smaller than slot)
            var iconGO = new GameObject("Icon");
            iconGO.transform.SetParent(slotGO.transform, false);
            var iconRect = iconGO.AddComponent<RectTransform>();
            iconRect.anchorMin = new Vector2(0.1f, 0.25f);
            iconRect.anchorMax = new Vector2(0.9f, 0.95f);
            iconRect.offsetMin = Vector2.zero;
            iconRect.offsetMax = Vector2.zero;
            var iconImg = iconGO.AddComponent<Image>();
            iconImg.preserveAspect = true;
            iconImg.color = new Color(1, 1, 1, 0.92f);
            _slotIcons[index] = iconImg;

            // Loading dots (shown during thinking in icon area)
            var loadingGO = new GameObject("LoadingDots");
            loadingGO.transform.SetParent(slotGO.transform, false);
            var loadingRect = loadingGO.AddComponent<RectTransform>();
            loadingRect.anchorMin = new Vector2(0.1f, 0.25f);
            loadingRect.anchorMax = new Vector2(0.9f, 0.95f);
            loadingRect.offsetMin = Vector2.zero;
            loadingRect.offsetMax = Vector2.zero;
            var loadingText = loadingGO.AddComponent<Text>();
            loadingText.font = _uiFont;
            loadingText.fontSize = _loadingDotsFontSize;
            loadingText.fontStyle = FontStyle.Bold;
            loadingText.color = _labelColor;
            loadingText.alignment = TextAnchor.MiddleCenter;
            loadingText.horizontalOverflow = HorizontalWrapMode.Overflow;
            loadingText.verticalOverflow = VerticalWrapMode.Overflow;
            loadingText.text = "...";
            loadingText.enabled = false;
            _slotLoadingDots[index] = loadingText;

            // Key label (top-left corner)
            var keyGO = new GameObject("KeyLabel");
            keyGO.transform.SetParent(slotGO.transform, false);
            var keyRect = keyGO.AddComponent<RectTransform>();
            keyRect.anchorMin = new Vector2(0f, 1f);
            keyRect.anchorMax = new Vector2(0f, 1f);
            keyRect.pivot = new Vector2(0f, 1f);
            keyRect.anchoredPosition = new Vector2(6f, -4f);
            keyRect.sizeDelta = new Vector2(24f, 18f);
            var keyText = keyGO.AddComponent<Text>();
            keyText.text = (index + 1).ToString();
            keyText.font = _uiFont;
            keyText.fontSize = _keyFontSize;
            keyText.fontStyle = FontStyle.Bold;
            keyText.color = _labelColor;
            keyText.alignment = TextAnchor.UpperLeft;
            keyText.enabled = _showSlotKeyLabels;
            _slotKeyLabels[index] = keyText;

            // Display name label (bottom)
            var labelGO = new GameObject("Label");
            labelGO.transform.SetParent(slotGO.transform, false);
            var labelRect = labelGO.AddComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(1f, 0f);
            labelRect.pivot = new Vector2(0.5f, 0f);
            labelRect.anchoredPosition = new Vector2(0f, 4f);
            labelRect.sizeDelta = new Vector2(-8f, 32f);
            var labelText = labelGO.AddComponent<Text>();
            labelText.font = _uiFont;
            labelText.fontSize = _labelFontSize;
            labelText.color = _labelColor;
            labelText.alignment = TextAnchor.LowerCenter;
            labelText.horizontalOverflow = HorizontalWrapMode.Wrap;
            labelText.verticalOverflow = VerticalWrapMode.Truncate;
            _slotLabels[index] = labelText;

            var labelOutline = labelGO.AddComponent<Outline>();
            labelOutline.effectColor = new Color(0f, 0f, 0f, 0.65f);
            labelOutline.effectDistance = new Vector2(1f, -1f);
        }

        private void HandleSlotChanged(int index, PropEntry prop)
        {
            if (_slotLabels == null || index >= _slotLabels.Length) return;
            if (_slotWaitingForModel != null && index >= 0 && index < _slotWaitingForModel.Length)
                _slotWaitingForModel[index] = prop != null;

            if (_slotLabels[index] != null)
            {
                _slotLabels[index].text = prop != null ? Truncate(prop.DisplayName, 20) : "";
                _slotLabels[index].enabled = prop == null ? true : false;
            }

            if (_slotIcons != null && index < _slotIcons.Length && _slotIcons[index] != null)
            {
                _slotIcons[index].enabled = !_hotbar.IsInPostPlacementThinking;
                _slotIcons[index].sprite = GetOrCreateFallbackSprite(index, prop);
            }

            if (_slotLoadingDots != null && index < _slotLoadingDots.Length && _slotLoadingDots[index] != null)
                _slotLoadingDots[index].enabled = false;

            if (prop == null) return;

            // Request thumbnail (cached if previously rendered)
            var capture = RuntimeThumbnailCapture.Instance;
            if (capture == null)
            {
                var go = new GameObject("RuntimeThumbnailCapture");
                capture = go.AddComponent<RuntimeThumbnailCapture>();
            }
            int captured = index;
            PropEntry capturedProp = prop;
            capture.RequestThumbnail(prop, sprite =>
            {
                // Slot may have changed during async load; only apply if still relevant
                if (_slotIcons == null || captured >= _slotIcons.Length) return;
                PropEntry current = _hotbar.Slots[captured];
                if (current == null) return;
                if (!string.Equals(current.Id, capturedProp.Id, StringComparison.Ordinal)) return;
                _slotIcons[captured].sprite = sprite != null ? sprite : GetOrCreateFallbackSprite(captured, capturedProp);
                if (_slotWaitingForModel != null && captured >= 0 && captured < _slotWaitingForModel.Length)
                    _slotWaitingForModel[captured] = false;
                if (_slotLabels != null && captured >= 0 && captured < _slotLabels.Length && _slotLabels[captured] != null)
                {
                    _slotLabels[captured].enabled = !_hotbar.IsInPostPlacementThinking;
                    _slotLabels[captured].text = Truncate(capturedProp.DisplayName, 20);
                }
            });
        }

        private void HandleActiveSlotChanged(int activeIndex)
        {
            if (_slotBackgrounds == null) return;
            for (int i = 0; i < _slotBackgrounds.Length; i++)
                _slotBackgrounds[i].color = (i == activeIndex) ? _activeSlotColor : _slotColor;
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= max ? s : s.Substring(0, max - 1) + "…";
        }

        private static Font LoadBuiltinFontSafe()
        {
            return UiFontResolver.LoadVt323OrFallback();
        }

        private void TryAssignCanvasCamera()
        {
            if (_canvas == null)
                return;
            if (!_renderThroughCamera)
            {
                _canvas.worldCamera = null;
                return;
            }

            Camera cam = _uiCameraOverride;
            if (cam == null && !string.IsNullOrWhiteSpace(_preferredUiCameraName))
            {
                var go = GameObject.Find(_preferredUiCameraName);
                if (go != null)
                    cam = go.GetComponent<Camera>();
            }
            if (cam == null)
                cam = Camera.main;

            if (cam != null)
                _canvas.worldCamera = cam;
        }

        private Sprite GetOrCreateFallbackSprite(int index, PropEntry prop)
        {
            if (_fallbackSprites != null && index >= 0 && index < _fallbackSprites.Length && _fallbackSprites[index] != null)
                return _fallbackSprites[index];

            var tex = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            int hash = prop?.Id != null ? prop.Id.GetHashCode() : index;
            float h = Mathf.Abs((hash % 997) / 997f);
            Color a = Color.HSVToRGB(h, 0.35f, 0.85f);
            Color b = a * 0.5f;

            for (int y = 0; y < 32; y++)
            {
                for (int x = 0; x < 32; x++)
                {
                    bool border = x < 2 || y < 2 || x >= 30 || y >= 30;
                    bool checker = ((x / 6) + (y / 6)) % 2 == 0;
                    tex.SetPixel(x, y, border ? Color.black : (checker ? a : b));
                }
            }

            tex.Apply();
            Sprite sprite = Sprite.Create(tex, new Rect(0, 0, 32, 32), new Vector2(0.5f, 0.5f));
            if (_fallbackSprites != null && index >= 0 && index < _fallbackSprites.Length)
                _fallbackSprites[index] = sprite;
            return sprite;
        }

        private void HandleThinkingStateChanged(bool thinking)
        {
            if (thinking)
                StartThinkingState();
            else
                StopThinkingState();
        }

        private void HandleThinkingBlockedClick()
        {
            PulseActiveSlot();
        }

        /// <summary>Begins loading-style visuals on all slots (cycling dots, icon hidden).</summary>
        public void StartThinkingState()
        {
            if (_hotbar == null) return;
            BeginThinkingVisuals();
        }

        /// <summary>Ends loading visuals and restores slot icons (content should already be updated by HotbarController).</summary>
        public void StopThinkingState()
        {
            EndThinkingVisuals();
        }

        /// <summary>Quick feedback when the player clicks during the thinking cooldown.</summary>
        public void PulseActiveSlot()
        {
            if (_hotbar == null || _slotRects == null) return;
            if (_pulseRoutine != null)
                StopCoroutine(_pulseRoutine);
            _pulseRoutine = StartCoroutine(PulseActiveSlotRoutine());
        }

        private void BeginThinkingVisuals()
        {
            EndThinkingVisuals();
            for (int i = 0; i < HotbarController.SlotCount; i++)
            {
                if (_slotLabels != null && i < _slotLabels.Length && _slotLabels[i] != null)
                    _slotLabels[i].enabled = false;
                if (_slotIcons != null && i < _slotIcons.Length && _slotIcons[i] != null)
                    _slotIcons[i].enabled = false;
                if (_slotLoadingDots != null && i < _slotLoadingDots.Length && _slotLoadingDots[i] != null)
                {
                    _slotLoadingDots[i].enabled = true;
                    _slotLoadingDots[i].text = ThinkingDotPatterns[0];
                }
                if (_slotWaitingForModel != null && i < _slotWaitingForModel.Length)
                    _slotWaitingForModel[i] = true;
            }

            if (_thinkingDotsRoutine != null)
                StopCoroutine(_thinkingDotsRoutine);
            _thinkingDotsRoutine = StartCoroutine(ThinkingDotsRoutine());
        }

        private void EndThinkingVisuals()
        {
            if (_thinkingDotsRoutine != null)
            {
                StopCoroutine(_thinkingDotsRoutine);
                _thinkingDotsRoutine = null;
            }

            if (_slotIcons != null)
            {
                for (int i = 0; i < _slotIcons.Length; i++)
                {
                    if (_slotIcons[i] != null)
                        _slotIcons[i].enabled = true;
                }
            }

            if (_slotLoadingDots != null)
            {
                for (int i = 0; i < _slotLoadingDots.Length; i++)
                {
                    if (_slotLoadingDots[i] != null)
                        _slotLoadingDots[i].enabled = false;
                }
            }

            if (_hotbar != null && _slotLabels != null)
            {
                for (int i = 0; i < HotbarController.SlotCount && i < _slotLabels.Length; i++)
                {
                    PropEntry p = _hotbar.Slots[i];
                    if (_slotLabels[i] != null)
                    {
                        _slotLabels[i].text = p != null ? Truncate(p.DisplayName, 20) : "";
                        bool waiting = _slotWaitingForModel != null && i < _slotWaitingForModel.Length && _slotWaitingForModel[i];
                        _slotLabels[i].enabled = !waiting;
                    }
                }
            }
        }

        private IEnumerator ThinkingDotsRoutine()
        {
            int phase = 0;
            while (true)
            {
                if (_slotLoadingDots != null)
                {
                    string dots = ThinkingDotPatterns[phase % ThinkingDotPatterns.Length];
                    for (int i = 0; i < _slotLoadingDots.Length; i++)
                    {
                        if (_slotLoadingDots[i] != null && _slotLoadingDots[i].enabled)
                            _slotLoadingDots[i].text = dots;
                    }
                }

                phase++;
                yield return new WaitForSecondsRealtime(0.22f);
            }
        }

        private IEnumerator PulseActiveSlotRoutine()
        {
            int i = _hotbar.ActiveSlot;
            if (_slotRects == null || i < 0 || i >= _slotRects.Length || _slotRects[i] == null)
            {
                _pulseRoutine = null;
                yield break;
            }

            RectTransform rt = _slotRects[i];
            float elapsed = 0f;
            const float dur = 0.2f;
            while (elapsed < dur)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / dur);
                float bump = Mathf.Sin(t * Mathf.PI) * 0.1f;
                rt.localScale = Vector3.one * (1f + bump);
                yield return null;
            }

            rt.localScale = Vector3.one;
            _pulseRoutine = null;
        }
    }
}

using UnityEngine;
using UnityEngine.UI;
using System;

namespace AlgorithmicGallery.Corruption
{
    // Runtime-built UGUI hotbar. No prefab required — constructs Canvas + 5 slot panels on Awake.
    // Listens to HotbarController events to update visuals.
    [RequireComponent(typeof(HotbarController))]
    public class HotbarUI : MonoBehaviour
    {
        [Header("Layout")]
        [SerializeField] private float _slotSize = 128f;
        [SerializeField] private float _slotSpacing = 16f;
        [SerializeField] private float _bottomMargin = 52f;

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
        [SerializeField] private int _hintFontSize = 26;
        [SerializeField] private bool _showControlHints = false;
        [SerializeField] private bool _showSlotKeyLabels = false;

        private HotbarController _hotbar;
        private RectTransform[] _slotRects;
        private Image[] _slotBackgrounds;
        private Image[] _slotIcons;
        private Text[] _slotLabels;
        private Text[] _slotKeyLabels;
        private CanvasGroup _canvasGroup;
        private SandboxManager _sandboxManager;
        private Font _uiFont;
        private Canvas _canvas;
        private float _thumbnailRefreshTimer;
        private Sprite[] _fallbackSprites;

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

            // Initial sync (slots may already have content from Initialize)
            for (int i = 0; i < HotbarController.SlotCount; i++)
                HandleSlotChanged(i, _hotbar.Slots[i]);
            HandleActiveSlotChanged(_hotbar.ActiveSlot);
            TryAssignCanvasCamera();

            _sandboxManager = FindFirstObjectByType<SandboxManager>();
            if (_sandboxManager != null)
            {
                _sandboxManager.OnSandboxEntered.AddListener(HandleSandboxEntered);
                if (_sandboxManager.SandboxActive)
                    HandleSandboxEntered();
            }
            else
            {
                // Fail open for non-sandbox scenes that still use the hotbar.
                HandleSandboxEntered();
            }
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
            }

            if (_sandboxManager != null)
                _sandboxManager.OnSandboxEntered.RemoveListener(HandleSandboxEntered);
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
            _fallbackSprites = new Sprite[HotbarController.SlotCount];

            for (int i = 0; i < HotbarController.SlotCount; i++)
                BuildSlot(containerRect, i, totalWidth);

            if (_showControlHints)
                BuildControlHints(canvasGO.transform);
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
            hintText.text = "Right click - place\nLeft click - destroy\nR - rotate";
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
            _slotLabels[index].text = prop != null ? Truncate(prop.DisplayName, 20) : "";

            // Clear icon while waiting for capture
            if (_slotIcons != null && _slotIcons[index] != null)
                _slotIcons[index].sprite = GetOrCreateFallbackSprite(index, prop);

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
    }
}

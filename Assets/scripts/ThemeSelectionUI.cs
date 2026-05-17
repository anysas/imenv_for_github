using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AlgorithmicGallery.Corruption
{
    public class ThemeSelectionUI : MonoBehaviour
    {
        public event Action<PromptDefinition> OnPromptSelected;
        public event Action OnUiFadeOutComplete;

        [SerializeField] private Color _cardColor = new Color(0.08f, 0.08f, 0.1f, 0.92f);
        [SerializeField] private Color _cardHoverColor = new Color(0.14f, 0.14f, 0.18f, 0.95f);
        [SerializeField] private Color _customCardColor = new Color(0.04f, 0.04f, 0.05f, 0.92f);
        [SerializeField] private Color _textColor = new Color(0.92f, 0.92f, 0.92f);
        [SerializeField] private Color _headerColor = new Color(1f, 0.55f, 0.2f);
        [SerializeField] private Color _systemColor = new Color(1f, 0.55f, 0.2f);
        [SerializeField] private float _promptTagDisplayDuration = 2.0f;   // regular prompts
        [SerializeField] private float _customTagDisplayDuration  = 4.5f;  // custom input (animated flatten)
        [SerializeField] private float _uiFadeOutDuration = 0.45f;

        private Canvas _canvas;
        private CanvasGroup _canvasGroup;
        private Image _rootBackgroundImage;
        private GameObject _selectionPanel;
        private GameObject _inputPanel;
        private GameObject _flattenPanel;
        private InputField _customInputField;

        // Direct references to flatten panel text — avoids fragile Find() calls.
        private Text _flattenHeaderText;   // "you said: …"  /  "prompt text"
        private Text _flattenSubLabel;     // "the system understood:" / "tagged as:"
        private Text _flattenStatusText; // "Formatting Marketability…"
        private Text _flattenTagsText;     // tag list

        private PromptDefinition[] _currentPrompts;
        private bool _hasSelected;
        private bool _isOpen;

        public PromptDefinition SelectedPrompt { get; private set; }
        public bool HasSelected => _hasSelected;
        public bool IsOpen => _isOpen;

        // The 3 emotional/personal tags surfaced by the most recent flatten reveal.
        // Read by SandboxManager so the scoring wall can label its personal rows.
        public string[] LastFlattenedTags { get; private set; }

        void Awake()
        {
            BuildUI();
        }

        // ────────────────────────────────────────────────────────────────────
        // Construction
        // ────────────────────────────────────────────────────────────────────
        private void BuildUI()
        {
            if (FindFirstObjectByType<EventSystem>() == null)
            {
                var esGO = new GameObject("EventSystem");
                esGO.AddComponent<EventSystem>();
                esGO.AddComponent<StandaloneInputModule>();
            }

            var canvasGO = new GameObject("ThemeSelectionCanvas");
            canvasGO.transform.SetParent(transform, false);
            _canvas = canvasGO.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 150;
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            canvasGO.AddComponent<GraphicRaycaster>();
            _canvasGroup = canvasGO.AddComponent<CanvasGroup>();
            _canvasGroup.alpha = 1f;

            var bg = CreateChild(canvasGO.transform, "Background");
            var bgRect = bg.AddComponent<RectTransform>();
            Stretch(bgRect);
            var bgImg = bg.AddComponent<Image>();
            bgImg.color = new Color(0f, 0f, 0f, 0.78f);
            bgImg.raycastTarget = true;
            _rootBackgroundImage = bgImg;

            BuildSelectionPanel(canvasGO.transform);
            BuildInputPanel(canvasGO.transform);
            BuildFlattenPanel(canvasGO.transform);

            _canvasGroup.alpha = 0f;
            _canvasGroup.blocksRaycasts = false;
            _canvasGroup.interactable = false;
            _selectionPanel.SetActive(false);
            _inputPanel.SetActive(false);
            _flattenPanel.SetActive(false);
            _isOpen = false;
        }

        // ────────────────────────────────────────────────────────────────────
        // Panel 1 — prompt selection cards
        // ────────────────────────────────────────────────────────────────────
        private void BuildSelectionPanel(Transform parent)
        {
            _selectionPanel = CreateChild(parent, "SelectionPanel");
            var pRect = _selectionPanel.AddComponent<RectTransform>();
            Stretch(pRect);

            var header = CreateChild(_selectionPanel.transform, "Header");
            var headerRect = header.AddComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0.1f, 0.78f);
            headerRect.anchorMax = new Vector2(0.9f, 0.9f);
            headerRect.offsetMin = Vector2.zero;
            headerRect.offsetMax = Vector2.zero;
            var headerText = header.AddComponent<Text>();
            headerText.font = UiFontResolver.LoadVt323OrFallback();
            headerText.fontSize = 48;
            headerText.color = _headerColor;
            headerText.alignment = TextAnchor.MiddleCenter;
            headerText.text = "What space will you create?";

            CreateCustomCard(_selectionPanel.transform);
        }

        private void CreatePromptCard(Transform parent, PromptDefinition prompt, float xAnchor, float width)
        {
            var card = CreateChild(parent, "PromptCard");
            var cardRect = card.AddComponent<RectTransform>();
            cardRect.anchorMin = new Vector2(xAnchor, 0.40f);
            cardRect.anchorMax = new Vector2(xAnchor + width, 0.72f);
            cardRect.offsetMin = Vector2.zero;
            cardRect.offsetMax = Vector2.zero;

            var cardImg = card.AddComponent<Image>();
            cardImg.color = _cardColor;

            var btn = card.AddComponent<Button>();
            var colors = btn.colors;
            colors.normalColor = _cardColor;
            colors.highlightedColor = _cardHoverColor;
            colors.pressedColor = _cardHoverColor;
            colors.selectedColor = _cardHoverColor;
            btn.colors = colors;
            btn.targetGraphic = cardImg;

            var captured = prompt;
            // Show the tag reveal first, then start the sandbox.
            btn.onClick.AddListener(() => PreviewAndSelectPrompt(captured));

            var label = CreateChild(card.transform, "Label");
            var labelRect = label.AddComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0.08f, 0.15f);
            labelRect.anchorMax = new Vector2(0.92f, 0.85f);
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            var labelText = label.AddComponent<Text>();
            labelText.font = UiFontResolver.LoadVt323OrFallback();
            labelText.fontSize = 32;
            labelText.color = _textColor;
            labelText.alignment = TextAnchor.MiddleCenter;
            labelText.horizontalOverflow = HorizontalWrapMode.Wrap;
            labelText.verticalOverflow = VerticalWrapMode.Overflow;
            labelText.text = $"\"{prompt.DisplayText}\"";
        }

        private void CreateCustomCard(Transform parent)
        {
            var card = CreateChild(parent, "CustomPromptCard");
            var cardRect = card.AddComponent<RectTransform>();
            cardRect.anchorMin = new Vector2(0.32f, 0.20f);
            cardRect.anchorMax = new Vector2(0.68f, 0.34f);
            cardRect.offsetMin = Vector2.zero;
            cardRect.offsetMax = Vector2.zero;

            var cardImg = card.AddComponent<Image>();
            cardImg.color = _customCardColor;

            var outline = card.AddComponent<Outline>();
            outline.effectColor = new Color(_systemColor.r, _systemColor.g, _systemColor.b, 0.5f);
            outline.effectDistance = new Vector2(2f, -2f);

            var btn = card.AddComponent<Button>();
            var colors = btn.colors;
            colors.normalColor = _customCardColor;
            colors.highlightedColor = new Color(0.10f, 0.10f, 0.12f, 0.95f);
            colors.pressedColor = colors.highlightedColor;
            colors.selectedColor = colors.highlightedColor;
            btn.colors = colors;
            btn.targetGraphic = cardImg;
            btn.onClick.AddListener(ShowInput);

            var label = CreateChild(card.transform, "Label");
            var labelRect = label.AddComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0.05f, 0.1f);
            labelRect.anchorMax = new Vector2(0.95f, 0.9f);
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            var labelText = label.AddComponent<Text>();
            labelText.font = UiFontResolver.LoadVt323OrFallback();
            labelText.fontSize = 28;
            labelText.color = _textColor;
            labelText.alignment = TextAnchor.MiddleCenter;
            labelText.text = "Or write your own…";
        }

        // ────────────────────────────────────────────────────────────────────
        // Panel 2 — text input
        // ────────────────────────────────────────────────────────────────────
        private void BuildInputPanel(Transform parent)
        {
            _inputPanel = CreateChild(parent, "InputPanel");
            var pRect = _inputPanel.AddComponent<RectTransform>();
            Stretch(pRect);
            _inputPanel.SetActive(false);

            var prompt = CreateChild(_inputPanel.transform, "Prompt");
            var promptRect = prompt.AddComponent<RectTransform>();
            promptRect.anchorMin = new Vector2(0.1f, 0.62f);
            promptRect.anchorMax = new Vector2(0.9f, 0.78f);
            promptRect.offsetMin = Vector2.zero;
            promptRect.offsetMax = Vector2.zero;
            var promptText = prompt.AddComponent<Text>();
            promptText.font = UiFontResolver.LoadVt323OrFallback();
            promptText.fontSize = 42;
            promptText.color = _headerColor;
            promptText.alignment = TextAnchor.MiddleCenter;
            promptText.horizontalOverflow = HorizontalWrapMode.Wrap;
            promptText.text = "What space will you create?";

            var sub = CreateChild(_inputPanel.transform, "Subtext");
            var subRect = sub.AddComponent<RectTransform>();
            subRect.anchorMin = new Vector2(0.1f, 0.55f);
            subRect.anchorMax = new Vector2(0.9f, 0.62f);
            subRect.offsetMin = Vector2.zero;
            subRect.offsetMax = Vector2.zero;
            var subText = sub.AddComponent<Text>();
            subText.font = UiFontResolver.LoadVt323OrFallback();
            subText.fontSize = 22;
            subText.color = new Color(_textColor.r, _textColor.g, _textColor.b, 0.7f);
            subText.alignment = TextAnchor.MiddleCenter;
            subText.text = "write something meaningful to you.";

            var inputBg = CreateChild(_inputPanel.transform, "InputBg");
            var inputRect = inputBg.AddComponent<RectTransform>();
            inputRect.anchorMin = new Vector2(0.18f, 0.40f);
            inputRect.anchorMax = new Vector2(0.82f, 0.50f);
            inputRect.offsetMin = Vector2.zero;
            inputRect.offsetMax = Vector2.zero;
            var inputBgImg = inputBg.AddComponent<Image>();
            inputBgImg.color = new Color(0.05f, 0.05f, 0.06f, 0.95f);
            var inputOutline = inputBg.AddComponent<Outline>();
            inputOutline.effectColor = new Color(_systemColor.r, _systemColor.g, _systemColor.b, 0.6f);
            inputOutline.effectDistance = new Vector2(2f, -2f);

            var textGO = CreateChild(inputBg.transform, "Text");
            var textRect = textGO.AddComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0.02f, 0.05f);
            textRect.anchorMax = new Vector2(0.98f, 0.95f);
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            var inputText = textGO.AddComponent<Text>();
            inputText.font = UiFontResolver.LoadVt323OrFallback();
            inputText.fontSize = 30;
            inputText.color = _textColor;
            inputText.alignment = TextAnchor.MiddleLeft;
            inputText.supportRichText = false;

            var placeholderGO = CreateChild(inputBg.transform, "Placeholder");
            var placeholderRect = placeholderGO.AddComponent<RectTransform>();
            placeholderRect.anchorMin = new Vector2(0.02f, 0.05f);
            placeholderRect.anchorMax = new Vector2(0.98f, 0.95f);
            placeholderRect.offsetMin = Vector2.zero;
            placeholderRect.offsetMax = Vector2.zero;
            var placeholderText = placeholderGO.AddComponent<Text>();
            placeholderText.font = UiFontResolver.LoadVt323OrFallback();
            placeholderText.fontSize = 30;
            placeholderText.color = new Color(_textColor.r, _textColor.g, _textColor.b, 0.4f);
            placeholderText.alignment = TextAnchor.MiddleLeft;
            placeholderText.text = "type here…";
            placeholderText.fontStyle = FontStyle.Italic;

            _customInputField = inputBg.AddComponent<InputField>();
            _customInputField.targetGraphic = inputBgImg;
            _customInputField.textComponent = inputText;
            _customInputField.placeholder = placeholderText;
            _customInputField.characterLimit = 120;
            // Only wire submit via the button — avoids accidental submission on Enter mid-sentence.
            _customInputField.onSubmit.AddListener(SubmitCustom);

            var submit = CreateChild(_inputPanel.transform, "SubmitButton");
            var submitRect = submit.AddComponent<RectTransform>();
            submitRect.anchorMin = new Vector2(0.42f, 0.27f);
            submitRect.anchorMax = new Vector2(0.58f, 0.35f);
            submitRect.offsetMin = Vector2.zero;
            submitRect.offsetMax = Vector2.zero;
            var submitImg = submit.AddComponent<Image>();
            submitImg.color = _systemColor;
            var submitBtn = submit.AddComponent<Button>();
            submitBtn.targetGraphic = submitImg;
            submitBtn.onClick.AddListener(() => SubmitCustom(_customInputField != null ? _customInputField.text : ""));

            var submitLabel = CreateChild(submit.transform, "Label");
            var submitLabelRect = submitLabel.AddComponent<RectTransform>();
            Stretch(submitLabelRect);
            var submitLabelText = submitLabel.AddComponent<Text>();
            submitLabelText.font = UiFontResolver.LoadVt323OrFallback();
            submitLabelText.fontSize = 30;
            submitLabelText.fontStyle = FontStyle.Bold;
            submitLabelText.color = Color.black;
            submitLabelText.alignment = TextAnchor.MiddleCenter;
            submitLabelText.text = "Input";
        }

        // ────────────────────────────────────────────────────────────────────
        // Panel 3 — tag reveal ("the system understood…")
        // ────────────────────────────────────────────────────────────────────
        private void BuildFlattenPanel(Transform parent)
        {
            _flattenPanel = CreateChild(parent, "FlattenPanel");
            var pRect = _flattenPanel.AddComponent<RectTransform>();
            Stretch(pRect);
            _flattenPanel.SetActive(false);

            var youSaid = CreateChild(_flattenPanel.transform, "YouSaid");
            var youSaidRect = youSaid.AddComponent<RectTransform>();
            youSaidRect.anchorMin = new Vector2(0.04f, 0.70f);
            youSaidRect.anchorMax = new Vector2(0.96f, 0.90f);
            youSaidRect.offsetMin = Vector2.zero;
            youSaidRect.offsetMax = Vector2.zero;
            _flattenHeaderText = youSaid.AddComponent<Text>();
            _flattenHeaderText.font = UiFontResolver.LoadVt323OrFallback();
            _flattenHeaderText.fontSize = 46;
            _flattenHeaderText.color = new Color(_textColor.r, _textColor.g, _textColor.b, 0.92f);
            _flattenHeaderText.alignment = TextAnchor.MiddleCenter;
            _flattenHeaderText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _flattenHeaderText.verticalOverflow = VerticalWrapMode.Overflow;
            _flattenHeaderText.text = "";

            var statusGo = CreateChild(_flattenPanel.transform, "StatusLine");
            var statusRect = statusGo.AddComponent<RectTransform>();
            statusRect.anchorMin = new Vector2(0.06f, 0.56f);
            statusRect.anchorMax = new Vector2(0.94f, 0.66f);
            statusRect.offsetMin = Vector2.zero;
            statusRect.offsetMax = Vector2.zero;
            _flattenStatusText = statusGo.AddComponent<Text>();
            _flattenStatusText.font = UiFontResolver.LoadVt323OrFallback();
            _flattenStatusText.fontSize = 34;
            _flattenStatusText.fontStyle = FontStyle.Bold;
            _flattenStatusText.color = new Color(_systemColor.r, _systemColor.g, _systemColor.b, 0.95f);
            _flattenStatusText.alignment = TextAnchor.MiddleCenter;
            _flattenStatusText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _flattenStatusText.text = "";

            var divider = CreateChild(_flattenPanel.transform, "Divider");
            var divRect = divider.AddComponent<RectTransform>();
            divRect.anchorMin = new Vector2(0.35f, 0.498f);
            divRect.anchorMax = new Vector2(0.65f, 0.505f);
            divRect.offsetMin = Vector2.zero;
            divRect.offsetMax = Vector2.zero;
            var divImg = divider.AddComponent<Image>();
            divImg.color = new Color(_systemColor.r, _systemColor.g, _systemColor.b, 0.55f);

            var heard = CreateChild(_flattenPanel.transform, "Heard");
            var heardRect = heard.AddComponent<RectTransform>();
            heardRect.anchorMin = new Vector2(0.06f, 0.38f);
            heardRect.anchorMax = new Vector2(0.94f, 0.52f);
            heardRect.offsetMin = Vector2.zero;
            heardRect.offsetMax = Vector2.zero;
            _flattenSubLabel = heard.AddComponent<Text>();
            _flattenSubLabel.font = UiFontResolver.LoadVt323OrFallback();
            _flattenSubLabel.fontSize = 28;
            _flattenSubLabel.color = new Color(_systemColor.r, _systemColor.g, _systemColor.b, 0.9f);
            _flattenSubLabel.alignment = TextAnchor.MiddleCenter;
            _flattenSubLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
            _flattenSubLabel.text = "the system understood:";

            var tagsGO = CreateChild(_flattenPanel.transform, "Tags");
            var tagsRect = tagsGO.AddComponent<RectTransform>();
            tagsRect.anchorMin = new Vector2(0.05f, 0.10f);
            tagsRect.anchorMax = new Vector2(0.95f, 0.34f);
            tagsRect.offsetMin = Vector2.zero;
            tagsRect.offsetMax = Vector2.zero;
            _flattenTagsText = tagsGO.AddComponent<Text>();
            _flattenTagsText.font = UiFontResolver.LoadVt323OrFallback();
            _flattenTagsText.fontSize = 52;
            _flattenTagsText.fontStyle = FontStyle.Bold;
            _flattenTagsText.color = _systemColor;
            _flattenTagsText.alignment = TextAnchor.MiddleCenter;
            _flattenTagsText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _flattenTagsText.text = "";
        }

        // ────────────────────────────────────────────────────────────────────
        // Panel switching
        // ────────────────────────────────────────────────────────────────────
        private void ShowSelection()
        {
            if (_rootBackgroundImage != null)
                _rootBackgroundImage.color = new Color(0f, 0f, 0f, 0.78f);

            _selectionPanel.SetActive(true);
            _inputPanel.SetActive(false);
            _flattenPanel.SetActive(false);
        }

        private void ShowInput()
        {
            if (_rootBackgroundImage != null)
                _rootBackgroundImage.color = new Color(0f, 0f, 0f, 0.78f);

            PlayerInputFreeze.FreezePlayerLocomotion();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            _selectionPanel.SetActive(false);
            _inputPanel.SetActive(true);
            _flattenPanel.SetActive(false);

            if (_customInputField != null)
            {
                _customInputField.text = "";
                _customInputField.ActivateInputField();
                // Force EventSystem focus so all keystrokes go to the field
                EventSystem.current?.SetSelectedGameObject(_customInputField.gameObject);
            }
        }

        public void OpenPromptHud()
        {
            if (_hasSelected) return;
            _isOpen = true;

            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 1f;
                _canvasGroup.blocksRaycasts = true;
                _canvasGroup.interactable = true;
            }

            ShowInput();
        }

        // header / subheader / tags are stored directly — no Find() needed.
        private void ShowFlatten(string headerText, string subHeaderText, IEnumerable<string> tags)
        {
            _selectionPanel.SetActive(false);
            _inputPanel.SetActive(false);
            _flattenPanel.SetActive(true);

            if (_rootBackgroundImage != null)
                _rootBackgroundImage.color = new Color(0f, 0f, 0f, 0.96f);

            if (_flattenHeaderText != null) _flattenHeaderText.text = headerText;
            if (_flattenSubLabel   != null) _flattenSubLabel.text   = subHeaderText;
            if (_flattenTagsText   != null) _flattenTagsText.text   = string.Join("   ·   ", tags);
            if (_flattenStatusText != null)
            {
                _flattenStatusText.text = "";
                SetTextAlpha(_flattenStatusText, 0f);
            }
        }

        // ────────────────────────────────────────────────────────────────────
        // Selection paths
        // ────────────────────────────────────────────────────────────────────

        // Called when the player clicks a pre-defined prompt card.
        // Shows the animated flatten reveal before entering the sandbox.
        private void PreviewAndSelectPrompt(PromptDefinition prompt)
        {
            if (_hasSelected) return;
            PlayerInputFreeze.FreezePlayerLocomotion();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            PromptScoringHelper.EnsureCorporateTarget(prompt);
            string[] tags = PromptScoringHelper.BuildThreeFlattenLabels(prompt);

            LastFlattenedTags = tags;

            string header = $"\"{prompt.DisplayText}\"";
            string sub = "Target engagement vectors assigned:";
            ShowFlatten(header, sub, tags);
            StartCoroutine(FlattenAnimateThenSelect(prompt, header, sub, tags, _promptTagDisplayDuration));
        }

        private void SubmitCustom(string text)
        {
            if (_hasSelected) return;
            string trimmed = (text ?? "").Trim();
            if (trimmed.Length < 2)
            {
                if (_customInputField != null) _customInputField.ActivateInputField();
                return;
            }

            var result = PromptParser.Parse(trimmed);
            PromptScoringHelper.EnsureCorporateTarget(result.Prompt);
            string[] tags = PromptScoringHelper.BuildThreeFlattenLabels(result.Prompt);

            LastFlattenedTags = tags;

            string summary = result.CollapseSeverity > 0.55f
                ? "Input normalised. Consumer categories assigned."
                : "Target engagement vectors assigned.";
            string header = $"you said:\n\"{trimmed}\"";

            ShowFlatten(header, summary, tags);
            StartCoroutine(FlattenAnimateThenSelect(result.Prompt, header, summary, tags, _customTagDisplayDuration));
        }

        // Animated flatten: prompt fades in, holds, "squishes" (font shrinks), and crossfades to tags.
        // Total runtime targets ~`duration` seconds; uses unscaled time so it is safe across time-manipulation.
        private IEnumerator FlattenAnimateThenSelect(
            PromptDefinition prompt,
            string headerText,
            string subText,
            string[] tags,
            float duration)
        {
            const float headerFadeIn = 0.4f;
            const float subHold = 0.6f;
            const float squish = 1.2f;
            const float crossfade = 0.8f;
            const float tagHoldBase = 1.5f;
            const float statusFadeIn = 0.28f;
            const float statusHoldA = 0.55f;
            const float statusHoldB = 0.72f;
            const float statusFadeOut = 0.25f;
            float statusBlock = statusFadeIn + statusHoldA + statusHoldB + statusFadeOut;

            float originalHeaderSize = _flattenHeaderText != null ? _flattenHeaderText.fontSize : 46;

            SetTextAlpha(_flattenHeaderText, 0f);
            SetTextAlpha(_flattenSubLabel, 0f);
            SetTextAlpha(_flattenTagsText, 0f);
            SetTextAlpha(_flattenStatusText, 0f);
            yield return AnimateTextAlpha(_flattenHeaderText, 0f, 1f, headerFadeIn);

            yield return AnimateTextAlpha(_flattenSubLabel, 0f, 1f, 0.2f);
            yield return WaitUnscaled(subHold);

            PromptScoringHelper.EnsureCorporateTarget(prompt);
            string noun = PromptScoringHelper.CorporateFormattingNoun(prompt.CorporateTargetTag ?? "marketable");
            if (_flattenStatusText != null)
            {
                _flattenStatusText.text = $"Formatting {noun}…";
                yield return AnimateTextAlpha(_flattenStatusText, 0f, 1f, statusFadeIn);
                yield return WaitUnscaled(statusHoldA);
                _flattenStatusText.text = "Analysing Target Audience…";
                yield return WaitUnscaled(statusHoldB);
                yield return AnimateTextAlpha(_flattenStatusText, 1f, 0f, statusFadeOut);
            }
            else
                yield return WaitUnscaled(statusBlock);

            yield return AnimateFontSize(_flattenHeaderText, originalHeaderSize, Mathf.Max(8, Mathf.RoundToInt(originalHeaderSize * 0.36f)), squish);

            yield return CrossfadeAlpha(_flattenHeaderText, _flattenTagsText, crossfade);

            float consumed = headerFadeIn + 0.2f + subHold + statusBlock + squish + crossfade;
            float hold = Mathf.Max(tagHoldBase, duration - consumed);
            yield return WaitUnscaled(hold);

            if (_flattenHeaderText != null)
                _flattenHeaderText.fontSize = Mathf.RoundToInt(originalHeaderSize);

            SelectPrompt(prompt);
        }

        private static IEnumerator WaitUnscaled(float seconds)
        {
            float t = 0f;
            while (t < seconds)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        private static IEnumerator AnimateTextAlpha(Text text, float from, float to, float duration)
        {
            if (text == null) yield break;
            duration = Mathf.Max(0.01f, duration);
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / duration);
                SetTextAlpha(text, Mathf.Lerp(from, to, k));
                yield return null;
            }
            SetTextAlpha(text, to);
        }

        private static IEnumerator AnimateFontSize(Text text, float from, float to, float duration)
        {
            if (text == null) yield break;
            duration = Mathf.Max(0.01f, duration);
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / duration);
                // Ease in cubic — pressure builds.
                float eased = k * k * k;
                text.fontSize = Mathf.Max(1, Mathf.RoundToInt(Mathf.Lerp(from, to, eased)));
                yield return null;
            }
            text.fontSize = Mathf.Max(1, Mathf.RoundToInt(to));
        }

        private static IEnumerator CrossfadeAlpha(Text fadeOut, Text fadeIn, float duration)
        {
            duration = Mathf.Max(0.01f, duration);
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / duration);
                SetTextAlpha(fadeOut, 1f - k);
                SetTextAlpha(fadeIn, k);
                yield return null;
            }
            SetTextAlpha(fadeOut, 0f);
            SetTextAlpha(fadeIn, 1f);
        }

        private static void SetTextAlpha(Text text, float alpha)
        {
            if (text == null) return;
            var c = text.color;
            c.a = Mathf.Clamp01(alpha);
            text.color = c;
        }

        private void SelectPrompt(PromptDefinition prompt)
        {
            if (_hasSelected) return;
            _hasSelected = true;
            _isOpen = false;
            SelectedPrompt = prompt;
            Debug.Log($"[ThemeSelectionUI] Selected: \"{prompt.DisplayText}\" " +
                      $"(tags: {string.Join(", ", prompt.EmotionalTags ?? Array.Empty<string>())})");

            OnPromptSelected?.Invoke(prompt);

            StartCoroutine(FadeOutAndCloseUi());

            PlayerInputFreeze.RestorePlayerLocomotion();

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private IEnumerator FadeOutAndCloseUi()
        {
            if (_canvasGroup != null)
            {
                _canvasGroup.blocksRaycasts = false;
                _canvasGroup.interactable = false;

                float start = _canvasGroup.alpha;
                float duration = Mathf.Max(0.01f, _uiFadeOutDuration);
                float t = 0f;
                while (t < duration)
                {
                    t += Time.unscaledDeltaTime;
                    float k = Mathf.Clamp01(t / duration);
                    _canvasGroup.alpha = Mathf.Lerp(start, 0f, k);
                    yield return null;
                }
                _canvasGroup.alpha = 0f;
            }

            if (_canvas != null)
                Destroy(_canvas.gameObject, 0.05f);

            // Fire event when UI fade is complete
            OnUiFadeOutComplete?.Invoke();
            Debug.Log("[ThemeSelectionUI] UI fade out complete.");
        }

        // ────────────────────────────────────────────────────────────────────
        // Helpers
        // ────────────────────────────────────────────────────────────────────
        private static GameObject CreateChild(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}

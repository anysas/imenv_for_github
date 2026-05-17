using System.Text;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace AlgorithmicGallery.Corruption
{
    // World-space "terminal" screen that surfaces how the system is profiling the player.
    // Attach this to a wall object. If no UI references are assigned, it builds a simple
    // runtime canvas + text layout as a fallback.
    public class ProfileTerminalDisplay : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private SandboxManager _sandbox;
        [SerializeField] private AssistantSystem _assistant;

        [Header("Mode")]
        [SerializeField] private bool _disableDisplayCompletely = true;

        [Header("Display")]
        [SerializeField] private bool _buildRuntimeCanvasIfMissing = false;
        [SerializeField] private bool _hiddenUntilSandboxEntered = true;
        [SerializeField] private float _refreshInterval = 0.25f;
        [SerializeField] private bool _freezeOnSessionComplete = true;
        [SerializeField] private bool _showIntakePromptSequence = false;

        [Header("Boot Sequence")]
        [SerializeField] private bool _showBootSequence = true;
        [SerializeField] private float _bootDuration = 1.8f;
        [SerializeField] private float _squishDuration = 1.1f;

        [Header("Runtime UI (optional override)")]
        [SerializeField] private Canvas _canvas;
        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private Text _headerText;
        [SerializeField] private Text _statusText;
        [SerializeField] private Text _behaviorText;
        [SerializeField] private Text _preferenceText;
        [SerializeField] private Text _riskText;
        [SerializeField] private Text _footerText;

        [Header("Fallback Canvas Layout")]
        [SerializeField] private Vector2 _canvasSize = new Vector2(1800f, 1050f);
        [SerializeField] private float _pixelsPerUnit = 100f;

        private bool _subscribed;
        private bool _sessionStarted;
        private bool _frozen;
        private float _refreshTimer;
        private float _bootTimer;
        private bool _showingSquish;
        private Coroutine _squishRoutine;
        private Font _font;

        void Awake()
        {
            if (_disableDisplayCompletely)
            {
                HideDisplayNow();
                enabled = false;
                return;
            }

            ResolveReferences();
            EnsureDisplayBuilt();
            SetVisible(!_hiddenUntilSandboxEntered);
        }

        void Start()
        {
            ResolveReferences();
            SubscribeSandboxEvents();

            if (_sandbox != null && _sandbox.SandboxActive)
                HandleSandboxEntered();
            else if (_hiddenUntilSandboxEntered)
                SetVisible(false);
        }

        void OnDestroy()
        {
            UnsubscribeSandboxEvents();
        }

        void Update()
        {
            if (_sandbox == null || _assistant == null)
                ResolveReferences();

            if (!_sessionStarted && !_hiddenUntilSandboxEntered)
                _sessionStarted = true;

            if (_showBootSequence && _bootTimer > 0f)
            {
                _bootTimer -= Time.deltaTime;
                DrawBootFrame();
                return;
            }

            if (_showingSquish)
                return;

            if (_frozen)
                return;

            _refreshTimer -= Time.deltaTime;
            if (_refreshTimer <= 0f)
            {
                _refreshTimer = _refreshInterval;
                RefreshDisplay();
            }
        }

        private void ResolveReferences()
        {
            if (_sandbox == null)
                _sandbox = FindFirstObjectByType<SandboxManager>();
            if (_assistant == null)
                _assistant = FindFirstObjectByType<AssistantSystem>();
        }

        private void SubscribeSandboxEvents()
        {
            if (_sandbox == null || _subscribed)
                return;

            _sandbox.OnSandboxEntered.AddListener(HandleSandboxEntered);
            _sandbox.OnSessionComplete.AddListener(HandleSessionComplete);
            _sandbox.OnPromptSquishStarted.AddListener(HandlePromptSquishStarted);
            _sandbox.OnPromptCommitted.AddListener(HandlePromptCommitted);
            _subscribed = true;
        }

        private void UnsubscribeSandboxEvents()
        {
            if (_sandbox == null || !_subscribed)
                return;

            _sandbox.OnSandboxEntered.RemoveListener(HandleSandboxEntered);
            _sandbox.OnSessionComplete.RemoveListener(HandleSessionComplete);
            _sandbox.OnPromptSquishStarted.RemoveListener(HandlePromptSquishStarted);
            _sandbox.OnPromptCommitted.RemoveListener(HandlePromptCommitted);
            _subscribed = false;
        }

        private void HandleSandboxEntered()
        {
            _sessionStarted = true;
            _frozen = false;
            _refreshTimer = 0f;
            _bootTimer = _showBootSequence ? _bootDuration : 0f;
            SetVisible(true);
        }

        private void HandleSessionComplete()
        {
            if (_freezeOnSessionComplete)
            {
                _frozen = true;
                RefreshDisplay();
            }
        }

        private void HandlePromptSquishStarted()
        {
            if (!_showIntakePromptSequence || _sandbox == null) return;
            if (_squishRoutine != null)
                StopCoroutine(_squishRoutine);
            _squishRoutine = StartCoroutine(SquishPromptSequence(_sandbox.SelectedPrompt));
        }

        private void HandlePromptCommitted()
        {
            if (!_showIntakePromptSequence || _sandbox == null) return;
            DrawPromptCollapseSummary(_sandbox.SelectedPrompt);
        }

        private void EnsureDisplayBuilt()
        {
            if (HasAssignedTextReferences())
                return;

            if (!_buildRuntimeCanvasIfMissing)
                return;

            BuildRuntimeCanvas();
        }

        private bool HasAssignedTextReferences()
        {
            return _headerText != null
                   && _statusText != null
                   && _behaviorText != null
                   && _preferenceText != null
                   && _riskText != null
                   && _footerText != null;
        }

        private void BuildRuntimeCanvas()
        {
            _font = LoadBuiltinFontSafe();

            var canvasGO = new GameObject("ProfileTerminalCanvas");
            canvasGO.transform.SetParent(transform, false);

            _canvas = canvasGO.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.worldCamera = Camera.main;
            _canvas.sortingOrder = 150;

            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = _pixelsPerUnit;
            scaler.referencePixelsPerUnit = 100f;

            _canvasGroup = canvasGO.AddComponent<CanvasGroup>();
            _canvasGroup.alpha = 1f;

            RectTransform canvasRect = canvasGO.GetComponent<RectTransform>();
            canvasRect.sizeDelta = _canvasSize;
            canvasRect.localScale = Vector3.one * 0.0025f;
            canvasRect.localPosition = Vector3.zero;

            var panel = CreateImage("Panel", canvasGO.transform, new Color(0.02f, 0.05f, 0.05f, 0.9f));
            panel.rectTransform.anchorMin = new Vector2(0f, 0f);
            panel.rectTransform.anchorMax = new Vector2(1f, 1f);
            panel.rectTransform.offsetMin = Vector2.zero;
            panel.rectTransform.offsetMax = Vector2.zero;

            _headerText = CreateText(
                "Header",
                panel.rectTransform,
                44,
                TextAnchor.UpperLeft,
                new Color(0.62f, 1f, 0.73f, 1f),
                new Vector2(24f, -20f),
                new Vector2(-24f, -90f),
                FontStyle.Bold
            );

            _statusText = CreateText(
                "Status",
                panel.rectTransform,
                32,
                TextAnchor.UpperLeft,
                new Color(0.80f, 1f, 0.86f, 1f),
                new Vector2(24f, -116f),
                new Vector2(-24f, -380f),
                FontStyle.Normal
            );

            _behaviorText = CreateText(
                "Behavior",
                panel.rectTransform,
                30,
                TextAnchor.UpperLeft,
                new Color(0.80f, 1f, 0.86f, 1f),
                new Vector2(24f, -402f),
                new Vector2(-24f, -590f),
                FontStyle.Normal
            );

            _preferenceText = CreateText(
                "Preference",
                panel.rectTransform,
                30,
                TextAnchor.UpperLeft,
                new Color(0.80f, 1f, 0.86f, 1f),
                new Vector2(24f, -612f),
                new Vector2(-24f, -800f),
                FontStyle.Normal
            );

            _riskText = CreateText(
                "Risk",
                panel.rectTransform,
                30,
                TextAnchor.UpperLeft,
                new Color(1f, 0.86f, 0.72f, 1f),
                new Vector2(24f, -822f),
                new Vector2(-24f, -960f),
                FontStyle.Bold
            );

            _footerText = CreateText(
                "Footer",
                panel.rectTransform,
                26,
                TextAnchor.LowerLeft,
                new Color(1f, 0.62f, 0.62f, 0.95f),
                new Vector2(24f, -982f),
                new Vector2(-24f, -16f),
                FontStyle.Italic
            );
        }

        private Image CreateImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.color = color;
            return image;
        }

        private Text CreateText(
            string name,
            RectTransform parent,
            int fontSize,
            TextAnchor alignment,
            Color color,
            Vector2 topLeftOffset,
            Vector2 bottomRightOffset,
            FontStyle style)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<Text>();
            text.font = _font;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.alignment = alignment;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(topLeftOffset.x, bottomRightOffset.y);
            rt.offsetMax = new Vector2(bottomRightOffset.x, topLeftOffset.y);
            return text;
        }

        private void SetVisible(bool visible)
        {
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = visible ? 1f : 0f;
                _canvasGroup.blocksRaycasts = false;
                _canvasGroup.interactable = false;
            }
            else if (_canvas != null)
            {
                _canvas.enabled = visible;
            }
        }

        private void HideDisplayNow()
        {
            if (_canvasGroup != null)
                _canvasGroup.alpha = 0f;
            if (_canvas != null)
                _canvas.enabled = false;

            var runtimeCanvas = transform.Find("ProfileTerminalCanvas");
            if (runtimeCanvas != null)
                runtimeCanvas.gameObject.SetActive(false);
        }

        private void DrawBootFrame()
        {
            float pct = Mathf.Clamp01(1f - (_bootTimer / Mathf.Max(0.01f, _bootDuration)));
            int step = Mathf.FloorToInt(pct * 4f);

            _headerText.text = "AG_SYS PROFILE CONSOLE // BOOTSTRAP";
            _statusText.text = step switch
            {
                0 => "init::sandbox-link ...",
                1 => "init::sandbox-link OK\ninit::profile-buffer ...",
                2 => "init::sandbox-link OK\ninit::profile-buffer OK\ninit::inference-core ...",
                _ => "init::sandbox-link OK\ninit::profile-buffer OK\ninit::inference-core OK\nready.",
            };
            _behaviorText.text = "";
            _preferenceText.text = "";
            _riskText.text = "";
            _footerText.text = "profiling subsystem warming...";
        }

        private IEnumerator SquishPromptSequence(PromptDefinition prompt)
        {
            if (prompt == null || !HasAssignedTextReferences())
                yield break;

            _showingSquish = true;
            SetVisible(true);

            string original = prompt.DisplayText ?? string.Empty;
            var collapsed = prompt.CollapsedTerms ?? System.Array.Empty<string>();

            float t = 0f;
            float duration = Mathf.Max(0.05f, _squishDuration);
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / duration);

                string squished = BuildSquishedText(original, k);
                _headerText.text = "AG_SYS INTAKE TERMINAL // DESIRE COMPRESSION";
                _statusText.text = $"INPUT_STREAM:\n{squished}";
                _behaviorText.text = $"TOKEN_PRESSURE: {(k * 100f):F0}%\nCOLLAPSE: {(prompt.CollapseSeverity * 100f):F0}%";
                _preferenceText.text = $"OUTPUT_REGISTER:\n{string.Join("  ·  ", collapsed)}";
                _riskText.text = $"LOSS_OF_SPECIFICITY {BuildBar(Mathf.Clamp01(prompt.CollapseSeverity))}";
                _footerText.text = "compressing personal language into machine categories...";
                yield return null;
            }

            _showingSquish = false;
            DrawPromptCollapseSummary(prompt);
        }

        private void DrawPromptCollapseSummary(PromptDefinition prompt)
        {
            if (prompt == null || !HasAssignedTextReferences()) return;

            _headerText.text = "AG_SYS INTAKE TERMINAL // DESIRE COLLAPSE RESULT";
            _statusText.text =
                $"YOU_SAID:\n\"{prompt.DisplayText}\"\n\n" +
                $"SYSTEM_KEPT: {JoinOrFallback(prompt.NormalizedTokens)}";
            _behaviorText.text =
                $"INTENT_OBJECTS: {JoinOrFallback(prompt.IntentObjects)}\n" +
                $"INTENT_SETTING: {JoinOrFallback(prompt.IntentSetting)}\n" +
                $"INTENT_ACTIONS: {JoinOrFallback(prompt.IntentActions)}";
            _preferenceText.text =
                $"COLLAPSED_TO: {JoinOrFallback(prompt.CollapsedTerms)}\n" +
                $"DROPPED: {JoinOrFallback(prompt.DroppedTerms)}";
            _riskText.text =
                $"LOSS_OF_SPECIFICITY {BuildBar(Mathf.Clamp01(prompt.CollapseSeverity))} {(prompt.CollapseSeverity * 100f):F0}%\n" +
                $"PARSER_CONFIDENCE {BuildBar(Mathf.Clamp01(prompt.ParseConfidence))} {(prompt.ParseConfidence * 100f):F0}%";
            _footerText.text = "gate unlock pending... attribute package ready.";
        }

        private void RefreshDisplay()
        {
            if (!HasAssignedTextReferences())
                return;

            StyleProfile profile = _sandbox != null ? _sandbox.StyleProfile : null;
            float sessionTime = _assistant != null ? _assistant.SessionTime : 0f;
            float influence = _assistant != null ? _assistant.Influence : 0f;
            AssistantPhase phase = _assistant != null ? _assistant.Phase : AssistantPhase.Helping;
            bool running = _assistant != null && _assistant.IsRunning;

            int playerPlacements = profile != null ? profile.PlayerPlacementCount : 0;
            int assistantPlacements = profile != null ? profile.AssistantPlacementCount : 0;
            int totalPlacements = playerPlacements + assistantPlacements;
            float cadence = profile != null ? profile.AverageCadenceSeconds() : 0f;

            float confidence = Mathf.Clamp01(totalPlacements * 0.08f + influence * 0.5f);
            float overrideRisk = influence;
            float agencyRetention = 1f - overrideRisk;

            string groups = profile != null ? JoinOrFallback(profile.DominantGroups(3)) : "n/a";
            string tags = profile != null ? JoinOrFallback(profile.DominantTags(5)) : "n/a";

            _headerText.text = "AG_SYS PROFILE CONSOLE // SUBJECT: VISITOR_01";

            _statusText.text =
                $"SESSION: {sessionTime:F1}s / 90.0s\n" +
                $"PHASE: {phase}\n" +
                $"INFLUENCE: {influence:F3}\n" +
                $"CONFIDENCE: {(confidence * 100f):F0}%\n" +
                $"RUNTIME: {(running ? "ACTIVE" : "IDLE")}";

            _behaviorText.text =
                $"PLAYER_PLACEMENTS: {playerPlacements}\n" +
                $"ASSIST_PLACEMENTS: {assistantPlacements}\n" +
                $"CADENCE_AVG: {(cadence > 0.01f ? cadence.ToString("F1") + "s" : "n/a")}";

            _preferenceText.text =
                $"TOP_GROUPS: {groups}\n" +
                $"TOP_TAGS: {tags}";

            _riskText.text =
                $"AGENCY_RETENTION {BuildBar(agencyRetention)} {(agencyRetention * 100f):F0}%\n" +
                $"AUTONOMY_OVERRIDE {BuildBar(overrideRisk)} {(overrideRisk * 100f):F0}%";

            _footerText.text = phase switch
            {
                AssistantPhase.Helping => "mode::assistive alignment // user intent dominant",
                AssistantPhase.Suggesting => "mode::preference steering // guidance intensity rising",
                AssistantPhase.Overriding => "mode::intent substitution in progress",
                _ => "mode::unknown",
            };
        }

        private static string JoinOrFallback(System.Collections.Generic.IReadOnlyList<string> items)
        {
            if (items == null || items.Count == 0)
                return "n/a";
            return string.Join(", ", items);
        }

        private static string JoinOrFallback(string[] items)
        {
            if (items == null || items.Length == 0)
                return "n/a";
            return string.Join(", ", items);
        }

        private static string BuildBar(float value, int length = 12)
        {
            int filled = Mathf.Clamp(Mathf.RoundToInt(value * length), 0, length);
            var sb = new StringBuilder(length + 2);
            sb.Append('[');
            for (int i = 0; i < length; i++)
                sb.Append(i < filled ? '#' : '-');
            sb.Append(']');
            return sb.ToString();
        }

        private static string BuildSquishedText(string original, float progress)
        {
            if (string.IsNullOrWhiteSpace(original))
                return "[]";

            string compact = original.Replace(" ", string.Empty);
            int keepCount = Mathf.Clamp(
                Mathf.RoundToInt(Mathf.Lerp(original.Length, Mathf.Max(6, compact.Length / 3), progress)),
                1,
                compact.Length
            );
            string kept = compact.Substring(0, keepCount);
            int chunk = Mathf.Max(3, Mathf.RoundToInt(Mathf.Lerp(10, 4, progress)));
            var sb = new StringBuilder();
            for (int i = 0; i < kept.Length; i += chunk)
            {
                int len = Mathf.Min(chunk, kept.Length - i);
                sb.Append(kept.Substring(i, len));
                if (i + chunk < kept.Length) sb.Append('\n');
            }
            return sb.ToString();
        }

        private static Font LoadBuiltinFontSafe()
        {
            return UiFontResolver.LoadVt323OrFallback();
        }
    }
}

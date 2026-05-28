using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AlgorithmicGallery.Corruption
{
    /// <summary>
    /// Runtime-only scoring wall: three labeled vertical columns (sketch style). Scene-authored
    /// <see cref="ScoringWallRow"/> layouts are never used — the wall is always rebuilt here.
    /// Labels use <see cref="TextMeshProUGUI"/> for stable world-space readability under post-processing.
    /// </summary>
    public class ScoringWallController : MonoBehaviour
    {
        private const int BarCount = 3;
        private const string RuntimeRootName = "ScoringWallColumns_Runtime";

        [Header("Scene references")]
        [SerializeField] private Canvas _wallCanvas;

        [Header("Appearance")]
        [SerializeField] private float _fadeInDuration = 1.5f;

        [Header("Scores")]
        [SerializeField] private float _maxScore = 1500f;

        [Header("Wall label diagnostics")]
        [Tooltip("Logs canvas scaler DPI, world camera assignment, shader channels, per-column label rect sizes, and resolved TMP font.")]
        [SerializeField] private bool _logWallLabelDiagnostics;

        private SandboxManager _sandbox;
        private CanvasGroup _canvasGroup;
        private bool _visible;
        private bool _sandboxListenerRegistered;
        private Coroutine _fadeInCoroutine;
        private bool _wallUiInitialized;

        private readonly List<BarColumn> _bars = new();
        private readonly float[] _scores = new float[BarCount];

        private static readonly string[] DefaultBarLabels = { "Personal", "Nostalgic", "Marketable" };

        void Awake()
        {
            if (_wallCanvas == null)
                _wallCanvas = GetComponentInChildren<Canvas>(includeInactive: true);

            if (_wallCanvas != null)
            {
                _canvasGroup = _wallCanvas.GetComponent<CanvasGroup>();
                if (_canvasGroup == null)
                    _canvasGroup = _wallCanvas.gameObject.AddComponent<CanvasGroup>();
                _canvasGroup.alpha = 0f;
                _canvasGroup.blocksRaycasts = false;
                _canvasGroup.interactable = false;

                ApplyWallCanvasReadability();
            }
        }

        void OnEnable()
        {
            TryRegisterSandboxListener();
            // Start() does not run again after disable/enable; catch reveal if sandbox is already active.
            if (_wallUiInitialized)
                StartCoroutine(DelayedRevealIfSandboxActive());
        }

        void OnDisable()
        {
            UnregisterSandboxListener();
        }

        void Start()
        {
            // Listener is registered in OnEnable so we never miss OnSandboxEntered if SandboxManager.Start runs first.
            TryRegisterSandboxListener();

            RebuildWallLayout();
            ApplyLabelsFromSandbox();

            if (_sandbox != null && _sandbox.SandboxActive)
                HandleSandboxEntered();

            StartCoroutine(DelayedRevealIfSandboxActive());
            _wallUiInitialized = true;
        }

        void OnDestroy()
        {
            UnregisterSandboxListener();
            if (_fadeInCoroutine != null)
            {
                StopCoroutine(_fadeInCoroutine);
                _fadeInCoroutine = null;
            }
        }

        private void TryRegisterSandboxListener()
        {
            if (_sandboxListenerRegistered) return;
            if (_sandbox == null)
                _sandbox = FindFirstObjectByType<SandboxManager>();
            if (_sandbox == null) return;
            _sandbox.OnSandboxEntered.AddListener(HandleSandboxEntered);
            _sandboxListenerRegistered = true;
        }

        private void UnregisterSandboxListener()
        {
            if (!_sandboxListenerRegistered || _sandbox == null) return;
            _sandbox.OnSandboxEntered.RemoveListener(HandleSandboxEntered);
            _sandboxListenerRegistered = false;
        }

        /// <summary>
        /// Catches execution-order races: SandboxManager may invoke OnSandboxEntered before this component's Start runs.
        /// </summary>
        private IEnumerator DelayedRevealIfSandboxActive()
        {
            yield return null;
            if (_sandbox != null && _sandbox.SandboxActive && !_visible)
                HandleSandboxEntered();
        }

        /// <summary>Configure the three bar labels and reset scores (call when a prompt is committed).</summary>
        public void ConfigureSession(IReadOnlyList<string> barLabels, float? maxScore = null)
        {
            if (maxScore.HasValue)
                _maxScore = Mathf.Max(1f, maxScore.Value);

            ResetScores();
            ApplyLabelsWithFallbacks(barLabels);
            RefreshAllFills();
        }

        private void ApplyLabelsWithFallbacks(IReadOnlyList<string> barLabels)
        {
            if (_bars.Count == 0) return;

            for (int i = 0; i < BarCount && i < _bars.Count; i++)
            {
                string raw = (barLabels != null && i < barLabels.Count) ? barLabels[i] : null;
                string label = string.IsNullOrWhiteSpace(raw) ? DefaultBarLabels[i] : raw.Trim();
                if (string.IsNullOrWhiteSpace(label))
                    label = DefaultBarLabels[i];
                if (string.IsNullOrWhiteSpace(label))
                    label = "—";
                _bars[i].SetLabel(label);
            }
        }

        public void ResetScores()
        {
            for (int i = 0; i < BarCount; i++)
                _scores[i] = 0f;
            RefreshAllFills();
        }

        /// <summary>Add points to bar index 0..2 (clamped to max).</summary>
        public void AddScoreToBar(int barIndex, int amount)
        {
            if (barIndex < 0 || barIndex >= BarCount) return;
            float add = Mathf.Max(0, amount);
            _scores[barIndex] = Mathf.Min(_maxScore, _scores[barIndex] + add);
            if (barIndex < _bars.Count)
                _bars[barIndex].SetValue(_scores[barIndex] / _maxScore);
        }

        public IReadOnlyList<string> GetBarLabels()
        {
            return _bars.Select(b => b.LabelText).ToList();
        }

        private void HandleSandboxEntered()
        {
            if (_visible) return;
            _visible = true;
            ApplyLabelsFromSandbox();
            if (_canvasGroup != null)
            {
                if (_fadeInCoroutine != null)
                    StopCoroutine(_fadeInCoroutine);
                _fadeInCoroutine = StartCoroutine(FadeCanvasIn());
            }
            else
            {
                // No CanvasGroup — still show labels/bars if canvas exists.
                Debug.LogWarning("[ScoringWallController] No CanvasGroup on wall canvas; cannot fade in.");
            }
        }

        private void ApplyLabelsFromSandbox()
        {
            if (_sandbox == null || _bars.Count == 0) return;
            var labels = _sandbox.ScoringBarLabels;
            if (labels == null || labels.Length == 0) return;
            ConfigureSession(labels, null);
        }

        private IEnumerator FadeCanvasIn()
        {
            float t = 0f;
            float duration = Mathf.Max(0.01f, _fadeInDuration);
            while (t < duration)
            {
                t += Time.deltaTime;
                _canvasGroup.alpha = Mathf.Clamp01(t / duration);
                yield return null;
            }
            _canvasGroup.alpha = 1f;
            _fadeInCoroutine = null;
        }

        private void RefreshAllFills()
        {
            float denom = Mathf.Max(1f, _maxScore);
            for (int i = 0; i < _bars.Count && i < BarCount; i++)
            {
                _bars[i].SetValue(_scores[i] / denom);
                _bars[i].SetLabelAlpha(1f);
            }
        }

        /// <summary>Always destroys prior runtime UI and builds three vertical columns.</summary>
        private void RebuildWallLayout()
        {
            if (_wallCanvas == null)
            {
                Debug.LogWarning("[ScoringWallController] No wall canvas assigned/found. Scoring wall will not render.");
                return;
            }

            ClearRuntimeWallChildren();

            _bars.Clear();
            ApplyWallCanvasReadability();
            var root = _wallCanvas.transform;
            var tmpFont = UiFontResolver.LoadWallTmpFontAsset(_logWallLabelDiagnostics);
            if (tmpFont == null)
                Debug.LogError(
                    "[ScoringWallController] No TMP font asset for score wall labels — labels will not render. " +
                    "See console for [UiFontResolver] message: import TMP Essentials and/or assign TMP_Settings default font.");

            var panel = new GameObject(RuntimeRootName);
            panel.transform.SetParent(root, false);
            panel.layer = _wallCanvas.gameObject.layer;
            var panelRect = panel.AddComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;

            float pad = 0.04f;
            float usable = 1f - pad * 2f;
            float colW = usable / BarCount;

            for (int i = 0; i < BarCount; i++)
            {
                bool corporate = i == BarCount - 1;
                float xMin = pad + i * colW;
                float xMax = pad + (i + 1) * colW;
                _bars.Add(CreateColumn(panel.transform, tmpFont, xMin, xMax, corporate, i));
            }
        }

        private const float WallCanvasMinDynamicPixelsPerUnit = 24f;

        private void ApplyWallCanvasReadability()
        {
            if (_wallCanvas == null) return;

            EnsureWorldSpaceCanvasCameraAndShaderChannels();

            var scaler = _wallCanvas.GetComponent<CanvasScaler>();
            if (scaler != null && scaler.dynamicPixelsPerUnit < WallCanvasMinDynamicPixelsPerUnit)
            {
                scaler.dynamicPixelsPerUnit = WallCanvasMinDynamicPixelsPerUnit;
                if (_logWallLabelDiagnostics)
                    Debug.Log($"[ScoringWallController] CanvasScaler.dynamicPixelsPerUnit -> {scaler.dynamicPixelsPerUnit}");
            }

            if (_wallCanvas.sortingOrder < 50)
                _wallCanvas.sortingOrder = 50;
        }

        /// <summary>
        /// World-space TMP UGUI needs a camera reference and canvas shader channels for correct mesh generation.
        /// </summary>
        private void EnsureWorldSpaceCanvasCameraAndShaderChannels()
        {
            if (_wallCanvas == null) return;

            if (_wallCanvas.renderMode == RenderMode.WorldSpace && _wallCanvas.worldCamera == null)
            {
                Camera cam = Camera.main;
                if (cam == null)
                {
                    var cameras = FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                    for (int i = 0; i < cameras.Length; i++)
                    {
                        var c = cameras[i];
                        if (c != null && c.enabled && c.gameObject.activeInHierarchy)
                        {
                            cam = c;
                            break;
                        }
                    }
                }

                _wallCanvas.worldCamera = cam;
                if (cam == null)
                    Debug.LogWarning(
                        "[ScoringWallController] World-space scoring wall canvas has no worldCamera and no suitable Camera was found. " +
                        "Assign the gameplay camera on the Canvas, or tag it MainCamera.");
                else if (_logWallLabelDiagnostics)
                    Debug.Log($"[ScoringWallController] World-space canvas worldCamera -> \"{cam.name}\" (renderMode=WorldSpace).");
            }

            var needed = AdditionalCanvasShaderChannels.TexCoord1
                         | AdditionalCanvasShaderChannels.Normal
                         | AdditionalCanvasShaderChannels.Tangent;
            if ((_wallCanvas.additionalShaderChannels & needed) != needed)
            {
                _wallCanvas.additionalShaderChannels |= needed;
                if (_logWallLabelDiagnostics)
                    Debug.Log($"[ScoringWallController] Canvas.additionalShaderChannels -> {_wallCanvas.additionalShaderChannels}");
            }

            if (_logWallLabelDiagnostics)
            {
                string camName = _wallCanvas.worldCamera != null ? _wallCanvas.worldCamera.name : "(null)";
                Debug.Log(
                    $"[ScoringWallController] Wall canvas diagnostics: renderMode={_wallCanvas.renderMode} " +
                    $"worldCamera=\"{camName}\" sortingOrder={_wallCanvas.sortingOrder} " +
                    $"additionalShaderChannels={_wallCanvas.additionalShaderChannels}");
            }
        }

        private void ClearRuntimeWallChildren()
        {
            if (_wallCanvas == null) return;
            var t = _wallCanvas.transform;
            for (int c = t.childCount - 1; c >= 0; c--)
                Destroy(t.GetChild(c).gameObject);
        }

        private BarColumn CreateColumn(Transform parent, TMP_FontAsset tmpFont, float anchorXMin, float anchorXMax, bool corporate, int index)
        {
            var colGo = new GameObject($"Column_{index}");
            colGo.transform.SetParent(parent, false);
            int uiLayer = parent.gameObject.layer;
            colGo.layer = uiLayer;
            var colRect = colGo.AddComponent<RectTransform>();
            colRect.anchorMin = new Vector2(anchorXMin, 0.08f);
            colRect.anchorMax = new Vector2(anchorXMax, 0.94f);
            colRect.offsetMin = Vector2.zero;
            colRect.offsetMax = Vector2.zero;

            var track = new GameObject("BarTrack");
            track.transform.SetParent(colGo.transform, false);
            track.layer = uiLayer;
            var trackRect = track.AddComponent<RectTransform>();
            trackRect.anchorMin = new Vector2(0.08f, 0.22f);
            trackRect.anchorMax = new Vector2(0.92f, 1f);
            trackRect.offsetMin = Vector2.zero;
            trackRect.offsetMax = Vector2.zero;

            var barBg = new GameObject("BarBg");
            barBg.transform.SetParent(track.transform, false);
            barBg.layer = uiLayer;
            var barBgRect = barBg.AddComponent<RectTransform>();
            StretchFull(barBgRect);
            var barBgImg = barBg.AddComponent<Image>();
            barBgImg.color = new Color(0.02f, 0.02f, 0.02f, corporate ? 0.55f : 0.5f);
            barBgImg.raycastTarget = false;

            var fillGo = new GameObject("Fill");
            fillGo.transform.SetParent(barBg.transform, false);
            fillGo.layer = uiLayer;
            var fillRect = fillGo.AddComponent<RectTransform>();
            fillRect.anchorMin = new Vector2(0f, 0f);
            fillRect.anchorMax = new Vector2(1f, 0f);
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            var fillImg = fillGo.AddComponent<Image>();
            fillImg.raycastTarget = false;
            fillImg.type = Image.Type.Simple;
            fillImg.color = index < 2
                ? new Color(1f, 0.48f, 0.06f, 1f)
                : new Color(0.2f, 0.62f, 1f, 1f);

            var labelBg = new GameObject("LabelCard");
            labelBg.transform.SetParent(colGo.transform, false);
            labelBg.layer = uiLayer;
            var labelBgRect = labelBg.AddComponent<RectTransform>();
            labelBgRect.anchorMin = new Vector2(0.04f, 0f);
            labelBgRect.anchorMax = new Vector2(0.96f, 0.22f);
            labelBgRect.offsetMin = Vector2.zero;
            labelBgRect.offsetMax = Vector2.zero;
            var labelBgImg = labelBg.AddComponent<Image>();
            labelBgImg.color = new Color(1f, 1f, 1f, 0.96f);
            labelBgImg.raycastTarget = false;

            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(labelBg.transform, false);
            labelGo.layer = uiLayer;
            var labelRect = labelGo.AddComponent<RectTransform>();

            var tmp = labelGo.AddComponent<TextMeshProUGUI>();
            // TMP may reset newly-added RectTransform defaults; enforce full-card stretch afterwards.
            StretchFull(labelRect);
            labelRect.pivot = new Vector2(0.5f, 0.5f);
            if (tmpFont != null)
                tmp.font = tmpFont;
            tmp.fontSize = corporate ? 18f : 17f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.overflowMode = TextOverflowModes.Truncate;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 8f;
            tmp.fontSizeMax = corporate ? 18f : 17f;
            tmp.raycastTarget = false;
            tmp.richText = false;
            tmp.margin = Vector4.zero;
            tmp.color = corporate
                ? new Color(0.08f, 0.38f, 0.9f, 1f)
                : new Color(0.02f, 0.02f, 0.03f, 1f);
            tmp.outlineWidth = 0.12f;
            tmp.outlineColor = corporate
                ? new Color(1f, 1f, 1f, 0.55f)
                : new Color(0f, 0f, 0f, 0.35f);

            string initial = DefaultBarLabels[index];
            if (string.IsNullOrWhiteSpace(initial))
                initial = "—";
            tmp.text = initial;
            labelGo.transform.SetAsLastSibling();

            var labelCg = labelGo.AddComponent<CanvasGroup>();
            labelCg.alpha = 1f;

            if (_logWallLabelDiagnostics)
            {
                var corners = new Vector3[4];
                labelRect.GetWorldCorners(corners);
                float w = Vector3.Distance(corners[0], corners[3]);
                float h = Vector3.Distance(corners[0], corners[1]);
                string fontName = tmp.font != null ? tmp.font.name : "null";
                Debug.Log(
                    $"[ScoringWall][Column_{index}] TMP text=\"{tmp.text}\" fontAsset={fontName} " +
                    $"fontSize={tmp.fontSize:F0} localRect={labelRect.rect.size} approxWorld={w:F2}x{h:F2}");
            }

            return new BarColumn(tmp, fillImg, fillRect, labelCg, corporate, initial);
        }

        private static void StretchFull(RectTransform r)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = Vector2.zero;
            r.offsetMax = Vector2.zero;
        }

        private class BarColumn
        {
            private readonly TMP_Text _label;
            private readonly Image _fill;
            private readonly RectTransform _fillRect;
            private readonly CanvasGroup _labelCg;
            private readonly string _fallbackLabel;

            public BarColumn(TMP_Text label, Image fill, RectTransform fillRect, CanvasGroup labelCg, bool corporate, string fallbackLabel)
            {
                _label = label;
                _fill = fill;
                _fillRect = fillRect;
                _labelCg = labelCg;
                Corporate = corporate;
                _fallbackLabel = string.IsNullOrWhiteSpace(fallbackLabel) ? "—" : fallbackLabel;
            }

            public bool Corporate { get; }
            public string LabelText => _label != null ? _label.text : "";

            public void SetLabel(string text)
            {
                if (_label == null) return;
                if (string.IsNullOrWhiteSpace(text))
                    _label.text = _fallbackLabel;
                else
                    _label.text = text.Trim();
            }

            public void SetValue(float normalized01)
            {
                float v = Mathf.Clamp01(normalized01);
                if (_fillRect != null)
                {
                    var max = _fillRect.anchorMax;
                    max.y = v;
                    _fillRect.anchorMax = max;
                }
                else if (_fill != null)
                {
                    _fill.fillAmount = v;
                }
            }

            public void SetLabelAlpha(float a)
            {
                if (_labelCg != null) _labelCg.alpha = Mathf.Clamp01(a);
            }
        }
    }

    public enum ScoringWallRowType { Personal, Corporate }

    public class ScoringWallRow : MonoBehaviour
    {
        public ScoringWallRowType RowType = ScoringWallRowType.Personal;
        public int Index = 0;
        public Text Label;
        public Image Fill;
        public CanvasGroup LabelCanvasGroup;
    }
}

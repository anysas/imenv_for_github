using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AlgorithmicGallery.Corruption
{
    // ─────────────────────────────────────────────────────────────────────────
    // Layout (left→right at 1920×1080):
    //
    //  [Left 260px]       [Center flex]          [Right 340px]
    //  Queue selectors    RenderTexture           Group dropdown
    //  Group filter       Prop info overlay       + Add custom group
    //  ──────             ─────────────────       Emotional tags (checkboxes)
    //  Stats              ← PREV  47/312  NEXT →  Scale: [slider────] [0.00]
    //                                             Custom tags (text field)
    //                                             Notes (multiline)
    //                                             ──────────────────────────
    //                                             [Delete] [Skip/Keep] [Save→]
    //
    // All UGUI; built purely at runtime.  No prefabs required.
    // ─────────────────────────────────────────────────────────────────────────
    [RequireComponent(typeof(CurationManager))]
    [RequireComponent(typeof(CurationViewport))]
    public class CurationUI : MonoBehaviour
    {
        // ── Colour palette ────────────────────────────────────────────────────
        private static readonly Color BgDark     = new Color(0.08f, 0.08f, 0.10f);
        private static readonly Color BgPanel    = new Color(0.12f, 0.12f, 0.15f);
        private static readonly Color BgButton   = new Color(0.20f, 0.20f, 0.26f);
        private static readonly Color Accent     = new Color(0.35f, 0.65f, 1.00f);
        private static readonly Color AccentRed  = new Color(1.00f, 0.35f, 0.35f);
        private static readonly Color AccentGreen= new Color(0.35f, 1.00f, 0.55f);
        private static readonly Color TextHi     = Color.white;
        private static readonly Color TextMed    = new Color(0.80f, 0.80f, 0.85f);
        private static readonly Color TextLow    = new Color(0.50f, 0.50f, 0.55f);

        // ── All emotional tags in vocabulary ──────────────────────────────────
        private static readonly string[] EmotionalTagVocab =
        {
            "intimate", "nostalgic", "comforting", "domestic", "clinical",
            "institutional", "bureaucratic", "threatening", "melancholy",
            "abandoned", "decayed", "liminal", "sacred", "public", "mundane", "personal",
        };

        // ── References ────────────────────────────────────────────────────────
        private CurationManager  _mgr;
        private CurationViewport _vp;

        // UI nodes
        private Canvas     _canvas;
        private Text       _propNameText;
        private Text       _propInfoText;
        private Text       _progressText;
        private Text       _queueSubtitle;
        private RawImage   _viewImage;
        private Dropdown   _groupDropdown;
        private InputField _customGroupInput;
        private InputField _customTagsInput;
        private InputField _notesInput;
        private Slider     _scaleSlider;
        private InputField _scaleNumInput;
        private Text       _saveLabel;

        // Checkbox state for emotional tags
        private readonly Dictionary<string, Toggle> _tagToggles = new();

        // Track whether we're programmatically setting slider/input to avoid feedback loops
        private bool _suppressScaleSync = false;

        // ── Lifecycle ─────────────────────────────────────────────────────────
        void Awake()
        {
            _mgr = GetComponent<CurationManager>();
            _vp  = GetComponent<CurationViewport>();
        }

        void Start()
        {
            BuildCanvas();

            _mgr.OnPropChanged  += RefreshAll;
            _mgr.OnQueueRebuilt += () => { RefreshQueueButtons(); RefreshAll(_mgr.CurrentProp); };
            _mgr.OnOverlaySaved += FlashSaved;

            RefreshAll(_mgr.CurrentProp);
            _vp.LoadProp(_mgr.CurrentProp);
        }

        void Update()
        {
            HandleHotkeys();
        }

        // ── Hotkeys ───────────────────────────────────────────────────────────
        private void HandleHotkeys()
        {
            // Suppress hotkeys when any input field is focused
            if (IsInputFocused()) return;

            if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D))
                ActionNext();
            if (Input.GetKeyDown(KeyCode.LeftArrow)  || Input.GetKeyDown(KeyCode.A))
                ActionPrev();
            if (Input.GetKeyDown(KeyCode.Delete)  || Input.GetKeyDown(KeyCode.X))
                ActionDelete();
            if (Input.GetKeyDown(KeyCode.S))
                ActionSaveAndAdvance();
            if (Input.GetKeyDown(KeyCode.K))
                ActionKeepAndAdvance();
            if (Input.GetKeyDown(KeyCode.R))
                _vp.ToggleTurntable();
            if (Input.GetKeyDown(KeyCode.F))
                _vp.FocusCamera();

            // +/- to nudge scale
            if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadPlus))
                NudgeScale(+0.05f);
            if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus))
                NudgeScale(-0.05f);
        }

        private static bool IsInputFocused()
        {
            var sel = EventSystem.current?.currentSelectedGameObject;
            if (sel == null) return false;
            return sel.GetComponent<InputField>() != null;
        }

        // ── Actions ───────────────────────────────────────────────────────────
        private void ActionNext()
        {
            _mgr.Next();
            _vp.LoadProp(_mgr.CurrentProp);
        }

        private void ActionPrev()
        {
            _mgr.Prev();
            _vp.LoadProp(_mgr.CurrentProp);
        }

        private void ActionDelete()
        {
            _mgr.DeleteAndAdvance();
            _vp.LoadProp(_mgr.CurrentProp);
        }

        private void ActionKeepAndAdvance()
        {
            _mgr.KeepAndAdvance();
            _vp.LoadProp(_mgr.CurrentProp);
        }

        private void ActionSaveAndAdvance()
        {
            CommitCurrentEdits();
            _mgr.SaveAndAdvance();
            _vp.LoadProp(_mgr.CurrentProp);
        }

        private void ActionRestore()
        {
            var prop = _mgr.CurrentProp;
            if (prop == null) return;
            _mgr.RestoreProp(prop.Id);
            _vp.LoadProp(_mgr.CurrentProp);
        }

        // Commit current UI values → overlay entry for the current prop.
        private void CommitCurrentEdits()
        {
            var prop = _mgr.CurrentProp;
            if (prop == null) return;

            string group = _groupDropdown != null && _groupDropdown.options.Count > 0
                ? _groupDropdown.options[_groupDropdown.value].text
                : null;

            var emotionalTags = _tagToggles
                .Where(kv => kv.Value.isOn)
                .Select(kv => kv.Key)
                .ToList();

            float scaleOverride = 0f;
            if (_scaleNumInput != null && float.TryParse(_scaleNumInput.text, out float parsed))
                scaleOverride = Mathf.Max(0f, parsed);

            string customTags = _customTagsInput?.text ?? "";
            var ctList = customTags.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                                   .Select(s => s.Trim().ToLower())
                                   .Where(s => s.Length > 0)
                                   .ToList();

            string notes = _notesInput?.text ?? "";

            _mgr.ApplyOverride(group, emotionalTags, scaleOverride, ctList, notes);
        }

        private void NudgeScale(float delta)
        {
            float cur = _vp.CurrentDisplayScale;
            float next = Mathf.Max(0.01f, cur + delta);
            _vp.SetDisplayScale(next);
            SyncScaleControls(next, fromSlider: false);
        }

        // ── Refresh ───────────────────────────────────────────────────────────
        private void RefreshAll(PropEntry prop)
        {
            RefreshInfoPanel(prop);
            RefreshEditPanel(prop);
            RefreshProgress();
        }

        private void RefreshInfoPanel(PropEntry prop)
        {
            if (_propNameText == null) return;
            if (prop == null)
            {
                _propNameText.text = "Queue empty";
                if (_propInfoText != null) _propInfoText.text = "";
                return;
            }

            _propNameText.text = prop.DisplayName ?? prop.Id;

            var dims = prop.Dimensions;
            string dimStr = (dims != null && prop.LongestAxis > 0.001f)
                ? $"{dims.X:F2} × {dims.Y:F2} × {dims.Z:F2} m"
                : "dims unknown";

            string scaledStr = prop.LongestAxis > 0.001f
                ? $"  →  {PropScaler.ScaledLongestAxis(prop):F2} m scaled"
                : "";

            if (_propInfoText != null)
                _propInfoText.text =
                    $"id: {prop.Id}\n" +
                    $"group: {prop.Group}  |  {prop.SizeCategory}\n" +
                    $"conf: {prop.Confidence:F3}  |  verts: {prop.VertexCount:N0}\n" +
                    $"{dimStr}{scaledStr}";
        }

        private void RefreshEditPanel(PropEntry prop)
        {
            if (prop == null) return;

            // Group
            if (_groupDropdown != null)
            {
                var groups = _mgr.AllGroups().ToList();
                _groupDropdown.ClearOptions();
                _groupDropdown.AddOptions(groups);
                int idx = groups.IndexOf(prop.Group ?? "");
                if (idx >= 0) _groupDropdown.value = idx;
            }

            // Emotional tags
            foreach (var kv in _tagToggles)
                kv.Value.isOn = prop.EmotionalTags != null &&
                                prop.EmotionalTags.Contains(kv.Key);

            // Scale
            float autoScale = PropScaler.ComputeScaleFactor(prop);
            float display   = prop.ScaleOverride > 0.001f ? prop.ScaleOverride : autoScale;
            SyncScaleControls(display, fromSlider: false);
            _vp.SetDisplayScale(display);

            // Custom tags
            if (_customTagsInput != null)
                _customTagsInput.text = prop.CustomTags != null
                    ? string.Join(", ", prop.CustomTags)
                    : "";

            // Notes
            if (_notesInput != null)
                _notesInput.text = prop.Notes ?? "";

            // Save button label
            bool inRemovedQueue = _mgr.ActiveQueue == CurationQueue.Removed;
            if (_saveLabel != null)
                _saveLabel.text = inRemovedQueue ? "Restore" : "Save →";
        }

        private void RefreshProgress()
        {
            if (_progressText == null) return;
            _progressText.text = _mgr.QueueEmpty
                ? "Queue empty"
                : $"{_mgr.CurrentIndex + 1} / {_mgr.QueueCount}";
        }

        private void RefreshQueueButtons()
        {
            // Subtitle shows current queue + filter
            if (_queueSubtitle != null)
            {
                string label = _mgr.ActiveQueue == CurationQueue.ByGroup && !string.IsNullOrEmpty(_mgr.FilterGroup)
                    ? $"By Group: {_mgr.FilterGroup}"
                    : _mgr.ActiveQueue.ToString();
                _queueSubtitle.text = $"Queue: {label}";
            }
        }

        private void SyncScaleControls(float value, bool fromSlider)
        {
            _suppressScaleSync = true;
            if (!fromSlider && _scaleSlider != null)
                _scaleSlider.value = Mathf.Clamp(Mathf.Log10(Mathf.Max(0.001f, value)) + 2f, 0f, 4f);
            if (_scaleNumInput != null)
                _scaleNumInput.text = value.ToString("F3");
            _suppressScaleSync = false;
        }

        private void FlashSaved()
        {
            // Just update label briefly — not worth a coroutine here
            if (_saveLabel != null) _saveLabel.text = "Saved ✓";
            // Reset after a moment via existing refresh on next prop change
        }

        // ── Canvas build ──────────────────────────────────────────────────────
        private void BuildCanvas()
        {
            // Root canvas
            var canvasGo = new GameObject("CurationCanvas");
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 100;
            canvasGo.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasGo.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1920, 1080);
            canvasGo.AddComponent<GraphicRaycaster>();

            if (FindFirstObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<StandaloneInputModule>();
            }

            // Full-screen dark background
            var bg = MakePanel(canvasGo.transform, "Background",
                new Vector2(0,0), new Vector2(1,1), BgDark);

            // ── Left panel ────────────────────────────────────────────────────
            var left = MakeAnchoredPanel(canvasGo.transform, "LeftPanel",
                new Vector2(0,0), new Vector2(0,1), new Vector2(0,0), new Vector2(260,0));
            left.GetComponent<Image>().color = BgPanel;
            BuildLeftPanel(left.transform);

            // ── Right panel ───────────────────────────────────────────────────
            var right = MakeAnchoredPanel(canvasGo.transform, "RightPanel",
                new Vector2(1,0), new Vector2(1,1), new Vector2(-340,0), new Vector2(0,0));
            right.GetComponent<Image>().color = BgPanel;
            BuildRightPanel(right.transform);

            // ── Center: RenderTexture view ────────────────────────────────────
            var centerGo = new GameObject("CenterView");
            var centerRt = centerGo.AddComponent<RectTransform>();
            centerGo.transform.SetParent(canvasGo.transform, false);
            centerRt.anchorMin = new Vector2(0, 0.07f);
            centerRt.anchorMax = new Vector2(1, 1);
            centerRt.offsetMin = new Vector2(265, 0);
            centerRt.offsetMax = new Vector2(-345, -4);

            _viewImage = centerGo.AddComponent<RawImage>();
            _viewImage.texture = _vp.ViewRT;

            // Prop info overlay (bottom-left of viewport)
            BuildViewportInfoOverlay(centerGo.transform);

            // ── Bottom nav bar ────────────────────────────────────────────────
            BuildNavBar(canvasGo.transform);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Left panel — queue selectors
        // ─────────────────────────────────────────────────────────────────────
        private void BuildLeftPanel(Transform parent)
        {
            float y = -12f;
            MakeLabel(parent, "Queues", "QUEUES", 13, TextMed, Vector2.zero, new Vector2(240, 24), ref y);
            y -= 4;

            var counts = _mgr.GetQueueCounts();

            MakeQueueButton(parent, CurationQueue.Oversized,  $"Oversized  ({counts[CurationQueue.Oversized]})", ref y);
            MakeQueueButton(parent, CurationQueue.LowConf,    $"Low Conf   ({counts[CurationQueue.LowConf]})",   ref y);
            MakeQueueButton(parent, CurationQueue.Unreviewed, $"Unreviewed ({counts[CurationQueue.Unreviewed]})",ref y);
            MakeQueueButton(parent, CurationQueue.All,        $"All        ({counts[CurationQueue.All]})",       ref y);
            MakeQueueButton(parent, CurationQueue.Removed,    $"Removed    ({counts[CurationQueue.Removed]})",  ref y);

            y -= 12;
            MakeLabel(parent, "ByGroupLabel", "BY GROUP", 13, TextMed, Vector2.zero, new Vector2(240, 24), ref y);
            y -= 4;

            // Group filter dropdown
            var groupNames = _mgr.GetGroupBreakdown().Select(t => $"{t.group} ({t.count})").ToList();
            var dd = MakeDropdown(parent, "GroupFilterDD", groupNames, 240, ref y,
                val =>
                {
                    var name = _mgr.GetGroupBreakdown().Select(t => t.group).ToList();
                    if (val >= 0 && val < name.Count)
                        _mgr.SetQueue(CurationQueue.ByGroup, name[val]);
                });
            y -= 4;

            // Queue subtitle
            _queueSubtitle = MakeLabel(parent, "QueueSubtitle", "Queue: Oversized",
                11, TextLow, Vector2.zero, new Vector2(240, 20), ref y);

            // Hotkey reference
            y -= 16;
            MakeLabel(parent, "Hotkeys", "HOTKEYS", 12, TextMed, Vector2.zero, new Vector2(240, 20), ref y);
            var hotkeyLines = new[]
            {
                "→/← or D/A  next/prev",
                "X or Del    delete",
                "K           keep+advance",
                "S           save+advance",
                "R           turntable toggle",
                "F           focus camera",
                "+/-         scale nudge",
                "Right drag  orbit",
                "Scroll      zoom",
            };
            foreach (var line in hotkeyLines)
            {
                float dummy = y;
                MakeLabel(parent, $"hk_{line[0]}", line, 10, TextLow, Vector2.zero, new Vector2(240, 16), ref dummy);
                y = dummy;
            }
        }

        private void MakeQueueButton(Transform parent, CurationQueue queue, string label, ref float y)
        {
            var btn = MakeButton(parent, $"QBtn_{queue}", label, 240, 28, ref y,
                () =>
                {
                    if (queue == CurationQueue.ByGroup)
                        return; // handled by dropdown
                    _mgr.SetQueue(queue);
                    _vp.LoadProp(_mgr.CurrentProp);
                });
            var btnImg = btn.GetComponent<Image>();
            if (queue == _mgr.ActiveQueue && btnImg != null)
                btnImg.color = Accent;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Right panel — edit fields
        // ─────────────────────────────────────────────────────────────────────
        private void BuildRightPanel(Transform parent)
        {
            float y = -12f;

            MakeLabel(parent, "EditHeader", "EDIT PROP", 13, TextMed, Vector2.zero, new Vector2(316, 24), ref y);
            y -= 6;

            // Group
            MakeLabel(parent, "GroupLabel", "Group", 11, TextLow, Vector2.zero, new Vector2(316, 18), ref y);
            var allGroups = _mgr.AllGroups().ToList();
            _groupDropdown = MakeDropdown(parent, "GroupDD", allGroups, 316, ref y, _ => { });

            // Add custom group row
            y -= 2;
            MakeLabel(parent, "CustomGroupLabel", "Add custom group:", 10, TextLow, Vector2.zero, new Vector2(316, 16), ref y);
            float inputRowY = y;   // save y before MakeInputField advances it
            _customGroupInput = MakeInputField(parent, "CustomGroupInput", "group_name", 220, 26, ref y);

            // "Add" button sits inline to the right of the input field at the same row
            var addGo = new GameObject("AddGroupBtn");
            var addRt = addGo.AddComponent<RectTransform>();
            addGo.transform.SetParent(parent, false);
            addRt.anchorMin = addRt.anchorMax = new Vector2(0, 1);
            addRt.pivot     = new Vector2(0, 1);
            addRt.anchoredPosition = new Vector2(228, -inputRowY);
            addRt.sizeDelta = new Vector2(80, 26);
            var addImg = addGo.AddComponent<Image>();
            addImg.color = Accent;
            var addBtn = addGo.AddComponent<Button>();
            var addTxt = MakeTextChild(addGo.transform, "Add", 11, Color.white);
            addBtn.onClick.AddListener(() =>
            {
                if (_customGroupInput != null && !string.IsNullOrWhiteSpace(_customGroupInput.text))
                {
                    _mgr.AddCustomGroup(_customGroupInput.text);
                    // Rebuild group dropdown
                    _groupDropdown.ClearOptions();
                    _groupDropdown.AddOptions(_mgr.AllGroups().ToList());
                    _customGroupInput.text = "";
                }
            });

            y -= 12;

            // Emotional tags
            MakeLabel(parent, "ETagsLabel", "Emotional Tags", 11, TextLow, Vector2.zero, new Vector2(316, 18), ref y);
            y -= 2;

            // Checkboxes in 2-column grid
            float checkX = 8;
            float checkY = y;
            int col = 0;
            foreach (var tag in EmotionalTagVocab)
            {
                float cx = checkX + col * 158f;
                var toggle = MakeToggle(parent, $"Tag_{tag}", tag, new Vector2(cx, -checkY), 150);
                _tagToggles[tag] = toggle;
                col++;
                if (col >= 2) { col = 0; checkY += 22; }
            }
            // Remaining half-row
            if (col == 1) checkY += 22;
            y = checkY + 6;

            // Scale
            MakeLabel(parent, "ScaleLabel", "Scale Override  (0 = auto)", 11, TextLow, Vector2.zero, new Vector2(316, 18), ref y);
            y -= 2;

            // Slider
            var sliderGo = new GameObject("ScaleSlider");
            var sliderRt = sliderGo.AddComponent<RectTransform>();
            sliderGo.transform.SetParent(parent, false);
            sliderRt.anchorMin = sliderRt.anchorMax = new Vector2(0, 1);
            sliderRt.pivot     = new Vector2(0, 1);
            sliderRt.anchoredPosition = new Vector2(8, -y);
            sliderRt.sizeDelta = new Vector2(220, 22);
            _scaleSlider = BuildSlider(sliderGo.transform, 0f, 4f, 2f);  // log10 space: 0=0.01, 2=1.0, 4=100
            _scaleSlider.onValueChanged.AddListener(sliderVal =>
            {
                if (_suppressScaleSync) return;
                float scale = Mathf.Pow(10f, sliderVal - 2f);   // log10 back to linear
                _vp.SetDisplayScale(scale);
                _suppressScaleSync = true;
                if (_scaleNumInput != null) _scaleNumInput.text = scale.ToString("F3");
                _suppressScaleSync = false;
            });

            // Numeric input (right of slider)
            var numGo = new GameObject("ScaleNumInput");
            var numRt = numGo.AddComponent<RectTransform>();
            numGo.transform.SetParent(parent, false);
            numRt.anchorMin = numRt.anchorMax = new Vector2(0, 1);
            numRt.pivot     = new Vector2(0, 1);
            numRt.anchoredPosition = new Vector2(234, -y);
            numRt.sizeDelta = new Vector2(74, 22);
            var numBg = numGo.AddComponent<Image>();
            numBg.color = BgButton;
            _scaleNumInput = numGo.AddComponent<InputField>();
            _scaleNumInput.contentType = InputField.ContentType.DecimalNumber;
            _scaleNumInput.text = "1.000";
            var numTxt = MakeTextChild(numGo.transform, "1.000", 12, TextHi);
            _scaleNumInput.textComponent = numTxt;
            var numPlaceholder = MakeTextChild(numGo.transform, "Placeholder", 12, TextLow);
            numPlaceholder.text = "scale";
            _scaleNumInput.placeholder = numPlaceholder;
            _scaleNumInput.onValueChanged.AddListener(val =>
            {
                if (_suppressScaleSync) return;
                if (float.TryParse(val, out float s) && s > 0f)
                {
                    _vp.SetDisplayScale(s);
                    _suppressScaleSync = true;
                    if (_scaleSlider != null)
                        _scaleSlider.value = Mathf.Clamp(Mathf.Log10(s) + 2f, 0f, 4f);
                    _suppressScaleSync = false;
                }
            });

            y += 28;

            // Custom tags
            y += 6;
            MakeLabel(parent, "CTagsLabel", "Custom Tags (comma-separated)", 11, TextLow, Vector2.zero, new Vector2(316, 18), ref y);
            _customTagsInput = MakeInputField(parent, "CustomTagsInput", "angry, red, broken…", 316, 26, ref y);

            // Notes
            y += 4;
            MakeLabel(parent, "NotesLabel", "Notes", 11, TextLow, Vector2.zero, new Vector2(316, 18), ref y);
            _notesInput = MakeInputField(parent, "NotesInput", "curator notes…", 316, 60, ref y);
            _notesInput.lineType = InputField.LineType.MultiLineNewline;

            // ── Action buttons ────────────────────────────────────────────────
            y += 12;
            MakeLabel(parent, "ActionDiv", "──────────────────────────────", 10, TextLow, Vector2.zero, new Vector2(316, 16), ref y);

            float btnY = y + 2;
            // Delete (left)
            var delGo = MakeButton(parent, "DeleteBtn", "Delete", 94, 32, ref btnY, ActionDelete);
            delGo.GetComponent<Image>().color = AccentRed;

            // Keep (middle)
            float keepY = y + 2;
            var keepGo = new GameObject("KeepBtn");
            SetupRectButton(keepGo, parent, new Vector2(102, -keepY - 0), new Vector2(104, 32), "Skip/Keep", BgButton, ActionKeepAndAdvance);

            // Save + advance (right)
            float saveY = y + 2;
            var saveGo = new GameObject("SaveBtn");
            SetupRectButton(saveGo, parent, new Vector2(212, -saveY - 0), new Vector2(104, 32), "Save →", AccentGreen, ActionSaveAndAdvance);
            _saveLabel = saveGo.GetComponentInChildren<Text>();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Viewport info overlay (bottom-left of center)
        // ─────────────────────────────────────────────────────────────────────
        private void BuildViewportInfoOverlay(Transform parent)
        {
            var infoGo = new GameObject("PropInfoOverlay");
            var infoRt = infoGo.AddComponent<RectTransform>();
            infoGo.transform.SetParent(parent, false);
            infoRt.anchorMin = new Vector2(0, 0);
            infoRt.anchorMax = new Vector2(0.5f, 0);
            infoRt.pivot     = new Vector2(0, 0);
            infoRt.anchoredPosition = new Vector2(8, 8);
            infoRt.sizeDelta = new Vector2(0, 90);

            _propNameText = infoGo.AddComponent<Text>();
            _propNameText.font      = UiFontResolver.GetDefault();
            _propNameText.fontSize  = 16;
            _propNameText.fontStyle = FontStyle.Bold;
            _propNameText.color     = TextHi;
            _propNameText.text      = "";
            _propNameText.supportRichText = false;

            var subGo = new GameObject("PropSubInfo");
            var subRt = subGo.AddComponent<RectTransform>();
            subGo.transform.SetParent(parent, false);
            subRt.anchorMin = new Vector2(0, 0);
            subRt.anchorMax = new Vector2(0.6f, 0);
            subRt.pivot     = new Vector2(0, 0);
            subRt.anchoredPosition = new Vector2(8, 30);
            subRt.sizeDelta = new Vector2(0, 68);

            _propInfoText = subGo.AddComponent<Text>();
            _propInfoText.font            = UiFontResolver.GetDefault();
            _propInfoText.fontSize        = 11;
            _propInfoText.color           = TextMed;
            _propInfoText.lineSpacing     = 1.2f;
            _propInfoText.supportRichText = false;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Navigation bar
        // ─────────────────────────────────────────────────────────────────────
        private void BuildNavBar(Transform parent)
        {
            var nav = MakeAnchoredPanel(parent, "NavBar",
                new Vector2(0, 0), new Vector2(1, 0),
                new Vector2(265, 0), new Vector2(-345, 50));
            nav.GetComponent<Image>().color = BgPanel;
            var navRt = nav.GetComponent<RectTransform>();

            // Prev button
            var prevGo = new GameObject("PrevBtn");
            SetupRectButton(prevGo, nav.transform, new Vector2(8, -9), new Vector2(100, 32),
                "← PREV", BgButton, ActionPrev);

            // Progress text
            var progGo = new GameObject("Progress");
            var progRt = progGo.AddComponent<RectTransform>();
            progGo.transform.SetParent(nav.transform, false);
            progRt.anchorMin = progRt.anchorMax = new Vector2(0.5f, 0.5f);
            progRt.pivot     = new Vector2(0.5f, 0.5f);
            progRt.anchoredPosition = Vector2.zero;
            progRt.sizeDelta = new Vector2(200, 40);
            _progressText = progGo.AddComponent<Text>();
            _progressText.font      = UiFontResolver.GetDefault();
            _progressText.fontSize  = 16;
            _progressText.fontStyle = FontStyle.Bold;
            _progressText.alignment = TextAnchor.MiddleCenter;
            _progressText.color     = TextHi;
            _progressText.text      = "";

            // Next button
            var nextGo = new GameObject("NextBtn");
            SetupRectButton(nextGo, nav.transform, new Vector2(-108, -9), new Vector2(100, 32),
                "NEXT →", BgButton, ActionNext);
            // anchor right
            nextGo.GetComponent<RectTransform>().anchorMin = new Vector2(1, 1);
            nextGo.GetComponent<RectTransform>().anchorMax = new Vector2(1, 1);
        }

        // ─────────────────────────────────────────────────────────────────────
        // UGUI primitive helpers
        // ─────────────────────────────────────────────────────────────────────

        private static GameObject MakePanel(Transform parent, string name,
            Vector2 anchorMin, Vector2 anchorMax, Color color)
        {
            var go = new GameObject(name);
            var rt = go.AddComponent<RectTransform>();
            go.transform.SetParent(parent, false);
            rt.anchorMin  = anchorMin;
            rt.anchorMax  = anchorMax;
            rt.offsetMin  = Vector2.zero;
            rt.offsetMax  = Vector2.zero;
            var img = go.AddComponent<Image>();
            img.color = color;
            return go;
        }

        private static GameObject MakeAnchoredPanel(Transform parent, string name,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name);
            var rt = go.AddComponent<RectTransform>();
            go.transform.SetParent(parent, false);
            rt.anchorMin  = anchorMin;
            rt.anchorMax  = anchorMax;
            rt.offsetMin  = offsetMin;
            rt.offsetMax  = offsetMax;
            go.AddComponent<Image>().color = BgPanel;
            return go;
        }

        private static Text MakeLabel(Transform parent, string name, string text,
            int fontSize, Color color, Vector2 offsetMin, Vector2 size, ref float y)
        {
            var go = new GameObject(name);
            var rt = go.AddComponent<RectTransform>();
            go.transform.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot     = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(8 + offsetMin.x, -y);
            rt.sizeDelta = size;
            var t = go.AddComponent<Text>();
            t.font      = UiFontResolver.GetDefault();
            t.fontSize  = fontSize;
            t.color     = color;
            t.text      = text;
            t.supportRichText = false;
            y += size.y + 2;
            return t;
        }

        private static Dropdown MakeDropdown(Transform parent, string name,
            List<string> options, float width, ref float y, Action<int> onChange)
        {
            var go = new GameObject(name);
            var rt = go.AddComponent<RectTransform>();
            go.transform.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot     = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(8, -y);
            rt.sizeDelta = new Vector2(width - 16, 28);
            var img = go.AddComponent<Image>();
            img.color = BgButton;
            var dd = go.AddComponent<Dropdown>();

            // Label
            var lbl = MakeTextChild(go.transform, "Label", 12, TextHi);
            lbl.GetComponent<RectTransform>().anchorMin = Vector2.zero;
            lbl.GetComponent<RectTransform>().anchorMax = Vector2.one;
            dd.captionText = lbl;

            // Arrow
            var arrow = new GameObject("Arrow");
            arrow.transform.SetParent(go.transform, false);
            var arrowRt = arrow.AddComponent<RectTransform>();
            arrowRt.anchorMin = new Vector2(1, 0.5f);
            arrowRt.anchorMax = new Vector2(1, 0.5f);
            arrowRt.anchoredPosition = new Vector2(-14, 0);
            arrowRt.sizeDelta = new Vector2(20, 20);
            var arrowTxt = arrow.AddComponent<Text>();
            arrowTxt.font     = UiFontResolver.GetDefault();
            arrowTxt.fontSize = 12;
            arrowTxt.color    = TextMed;
            arrowTxt.text     = "▾";
            arrowTxt.alignment = TextAnchor.MiddleCenter;

            // Template
            var template = new GameObject("Template");
            template.transform.SetParent(go.transform, false);
            var templateRt = template.AddComponent<RectTransform>();
            templateRt.anchorMin = new Vector2(0, 0);
            templateRt.anchorMax = new Vector2(1, 0);
            templateRt.pivot     = new Vector2(0.5f, 1f);
            templateRt.sizeDelta = new Vector2(0, 150);
            templateRt.anchoredPosition = Vector2.zero;
            var templateImg = template.AddComponent<Image>();
            templateImg.color = BgPanel;
            var scroll = template.AddComponent<ScrollRect>();

            var viewport = new GameObject("Viewport");
            viewport.transform.SetParent(template.transform, false);
            var vpRt = viewport.AddComponent<RectTransform>();
            vpRt.anchorMin = Vector2.zero; vpRt.anchorMax = Vector2.one;
            vpRt.sizeDelta = Vector2.zero; vpRt.offsetMin = Vector2.zero;
            viewport.AddComponent<Image>();
            viewport.AddComponent<Mask>().showMaskGraphic = false;
            scroll.viewport = vpRt;

            var content = new GameObject("Content");
            content.transform.SetParent(viewport.transform, false);
            var contentRt = content.AddComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0, 1);
            contentRt.anchorMax = new Vector2(1, 1);
            contentRt.pivot     = new Vector2(0.5f, 1);
            contentRt.sizeDelta = new Vector2(0, 28);
            scroll.content = contentRt;

            var item = new GameObject("Item");
            item.transform.SetParent(content.transform, false);
            var itemRt = item.AddComponent<RectTransform>();
            itemRt.anchorMin = new Vector2(0, 0.5f);
            itemRt.anchorMax = new Vector2(1, 0.5f);
            itemRt.sizeDelta = new Vector2(0, 26);
            item.AddComponent<Image>().color = BgButton;
            var itemToggle = item.AddComponent<Toggle>();
            var itemLabel = MakeTextChild(item.transform, "Item Label", 12, TextHi);
            itemLabel.GetComponent<RectTransform>().anchorMin = Vector2.zero;
            itemLabel.GetComponent<RectTransform>().anchorMax = Vector2.one;
            itemToggle.isOn = false;
            dd.itemText = itemLabel;
            dd.template = templateRt;
            template.SetActive(false);

            dd.ClearOptions();
            if (options != null) dd.AddOptions(options);
            dd.onValueChanged.AddListener(val => onChange?.Invoke(val));

            y += 32;
            return dd;
        }

        private static InputField MakeInputField(Transform parent, string name,
            string placeholder, float width, float height, ref float y)
        {
            var go = new GameObject(name);
            var rt = go.AddComponent<RectTransform>();
            go.transform.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot     = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(8, -y);
            rt.sizeDelta = new Vector2(width - 16, height);
            var bg = go.AddComponent<Image>();
            bg.color = BgButton;
            var field = go.AddComponent<InputField>();

            var txt  = MakeTextChild(go.transform, "Text",        12, TextHi);
            var phTxt= MakeTextChild(go.transform, "Placeholder", 12, TextLow);
            phTxt.text = placeholder;
            phTxt.fontStyle = FontStyle.Italic;

            field.textComponent = txt;
            field.placeholder   = phTxt;
            field.transition    = Selectable.Transition.ColorTint;

            y += height + 4;
            return field;
        }

        private static GameObject MakeButton(Transform parent, string name, string label,
            float width, float height, ref float y, Action onClick)
        {
            var go = new GameObject(name);
            var rt = go.AddComponent<RectTransform>();
            go.transform.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot     = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(8, -y);
            rt.sizeDelta = new Vector2(width - 16, height);
            var img = go.AddComponent<Image>();
            img.color = BgButton;
            var btn = go.AddComponent<Button>();
            btn.onClick.AddListener(() => onClick?.Invoke());
            MakeTextChild(go.transform, label, 12, TextHi);
            y += height + 4;
            return go;
        }

        private static void SetupRectButton(GameObject go, Transform parent,
            Vector2 anchoredPos, Vector2 size, string label, Color bgColor, Action onClick)
        {
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot     = new Vector2(0, 1);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;
            var img = go.AddComponent<Image>();
            img.color = bgColor;
            var btn = go.AddComponent<Button>();
            btn.onClick.AddListener(() => onClick?.Invoke());
            MakeTextChild(go.transform, label, 12, Color.white);
        }

        private static Toggle MakeToggle(Transform parent, string name, string label,
            Vector2 anchoredPos, float width)
        {
            var go = new GameObject(name);
            var rt = go.AddComponent<RectTransform>();
            go.transform.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot     = new Vector2(0, 1);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = new Vector2(width, 20);

            var tog = go.AddComponent<Toggle>();
            tog.transition = Selectable.Transition.ColorTint;

            // Background (checkbox box)
            var bg = new GameObject("Background");
            var bgRt = bg.AddComponent<RectTransform>();
            bg.transform.SetParent(go.transform, false);
            bgRt.anchorMin = new Vector2(0, 0.5f);
            bgRt.anchorMax = new Vector2(0, 0.5f);
            bgRt.pivot     = new Vector2(0, 0.5f);
            bgRt.anchoredPosition = new Vector2(2, 0);
            bgRt.sizeDelta = new Vector2(14, 14);
            var bgImg = bg.AddComponent<Image>();
            bgImg.color = BgButton;

            // Checkmark
            var check = new GameObject("Checkmark");
            var checkRt = check.AddComponent<RectTransform>();
            check.transform.SetParent(bg.transform, false);
            checkRt.anchorMin = Vector2.zero;
            checkRt.anchorMax = Vector2.one;
            checkRt.offsetMin = new Vector2(2, 2);
            checkRt.offsetMax = new Vector2(-2, -2);
            var checkImg = check.AddComponent<Image>();
            checkImg.color = Accent;
            tog.graphic    = checkImg;
            tog.targetGraphic = bgImg;

            // Label
            var lbl = new GameObject("Label");
            var lblRt = lbl.AddComponent<RectTransform>();
            lbl.transform.SetParent(go.transform, false);
            lblRt.anchorMin = Vector2.zero;
            lblRt.anchorMax = Vector2.one;
            lblRt.offsetMin = new Vector2(20, 0);
            lblRt.offsetMax = Vector2.zero;
            var lblTxt = lbl.AddComponent<Text>();
            lblTxt.font     = UiFontResolver.GetDefault();
            lblTxt.fontSize = 11;
            lblTxt.color    = TextMed;
            lblTxt.text     = label;
            lblTxt.alignment = TextAnchor.MiddleLeft;
            lblTxt.supportRichText = false;

            return tog;
        }

        private static Slider BuildSlider(Transform parent, float min, float max, float value)
        {
            var bg = new GameObject("SliderBg");
            var bgRt = bg.AddComponent<RectTransform>();
            bg.transform.SetParent(parent, false);
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = new Vector2(0, 8);
            bgRt.offsetMax = new Vector2(0, -8);
            var bgImg = bg.AddComponent<Image>();
            bgImg.color = new Color(0.15f, 0.15f, 0.20f);

            var fillArea = new GameObject("FillArea");
            var fillAreaRt = fillArea.AddComponent<RectTransform>();
            fillArea.transform.SetParent(bg.transform, false);
            fillAreaRt.anchorMin = Vector2.zero;
            fillAreaRt.anchorMax = Vector2.one;
            fillAreaRt.offsetMin = Vector2.zero;
            fillAreaRt.offsetMax = Vector2.zero;

            var fill = new GameObject("Fill");
            var fillRt = fill.AddComponent<RectTransform>();
            fill.transform.SetParent(fillArea.transform, false);
            fillRt.anchorMin = new Vector2(0, 0);
            fillRt.anchorMax = new Vector2(0, 1);
            fillRt.sizeDelta = Vector2.zero;
            var fillImg = fill.AddComponent<Image>();
            fillImg.color = Accent;

            var handleArea = new GameObject("HandleSlideArea");
            var handleAreaRt = handleArea.AddComponent<RectTransform>();
            handleArea.transform.SetParent(bg.transform, false);
            handleAreaRt.anchorMin = Vector2.zero;
            handleAreaRt.anchorMax = Vector2.one;
            handleAreaRt.offsetMin = new Vector2(7, 0);
            handleAreaRt.offsetMax = new Vector2(-7, 0);

            var handle = new GameObject("Handle");
            var handleRt = handle.AddComponent<RectTransform>();
            handle.transform.SetParent(handleArea.transform, false);
            handleRt.sizeDelta = new Vector2(14, 0);
            handleRt.anchorMin = new Vector2(0, 0);
            handleRt.anchorMax = new Vector2(0, 1);
            var handleImg = handle.AddComponent<Image>();
            handleImg.color = Color.white;

            // Need a root RectTransform for the Slider component
            var sliderGo = parent.gameObject;
            var slider = sliderGo.AddComponent<Slider>();
            slider.fillRect   = fillRt;
            slider.handleRect = handleRt;
            slider.direction  = Slider.Direction.LeftToRight;
            slider.minValue   = min;
            slider.maxValue   = max;
            slider.value      = value;
            slider.targetGraphic = handleImg;

            return slider;
        }

        private static Text MakeTextChild(Transform parent, string label, int fontSize, Color color)
        {
            var go = new GameObject("Text_" + label);
            var rt = go.AddComponent<RectTransform>();
            go.transform.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(4, 0);
            rt.offsetMax = new Vector2(-4, 0);
            var t = go.AddComponent<Text>();
            t.font      = UiFontResolver.GetDefault();
            t.fontSize  = fontSize;
            t.color     = color;
            t.text      = label;
            t.alignment = TextAnchor.MiddleCenter;
            t.supportRichText = false;
            return t;
        }
    }
}

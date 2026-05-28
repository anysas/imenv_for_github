using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace AlgorithmicGallery.Corruption
{
    // Top-level orchestrator for the sandbox scene.
    // Auto-wires missing dependencies on Start so it works with zero scene setup.
    //
    // Manual override (Inspector) is supported — assigned references take precedence.
    public class SandboxManager : MonoBehaviour
    {
        [Header("Scene references (auto-created if null)")]
        [SerializeField] private PropPlacer _propPlacer;
        [SerializeField] private HotbarController _hotbarController;
        [SerializeField] private AssistantSystem _assistantSystem;
        [SerializeField] private Transform _sandboxFloorRoot;
        [SerializeField] private Collider _sandboxEnterTrigger;

        [Header("Auto-bootstrap")]
        [Tooltip("If true, missing dependencies are created at Start.")]
        [SerializeField] private bool _autoBootstrap = true;
        [Tooltip("If true and no enter trigger is assigned, sandbox starts immediately on Start.")]
        [SerializeField] private bool _startSandboxImmediately = true;
        [SerializeField] private Vector2 _defaultFloorSize = new Vector2(10f, 10f);

        [Header("Session")]
        [SerializeField] private float _sessionDuration = 120f;
        [SerializeField] private float _endGracePeriod = 0f;
        [Tooltip("If true, session timer starts when the player places their first prop.")]
        [SerializeField] private bool _startTimerOnFirstPlayerPlacement = true;

        [Header("Events")]
        public UnityEvent OnSandboxEntered;
        public UnityEvent OnSessionComplete;
        public UnityEvent OnPromptCommitted;
        public UnityEvent OnHallwayUnlocked;
        public UnityEvent OnPromptSquishStarted;

        public bool SandboxActive { get; private set; }
        public PromptState OpeningState { get; private set; } = PromptState.InIntakeRoom;
        public bool HallwayUnlocked { get; private set; }
        public CuratedPropManifest Manifest { get; private set; }
        public StyleProfile StyleProfile { get; private set; }
        public PromptDefinition SelectedPrompt { get; private set; }
        public AssistantSystem Assistant => _assistantSystem;
        public HotbarController Hotbar => _hotbarController;
        public PropPlacer Placer => _propPlacer;
        public Transform SandboxFloor => _sandboxFloorRoot;
        public string[] PersonalDimensionTags => _personalDimensionTags;

        private ThemeSelectionUI _themeSelectionUI;
        private bool _sessionTimerStarted;
        private Coroutine _commitRoutine;
        private string[] _personalDimensionTags;

        void Start()
        {
            StyleProfile = new StyleProfile();
            Manifest = CuratedPropManifest.LoadFromStreamingAssets();

            if (Manifest == null)
            {
                Debug.LogError("[SandboxManager] Failed to load curated-props manifest. Aborting.");
                return;
            }

            if (_autoBootstrap)
                BootstrapMissingDependencies();

            if (_themeSelectionUI != null && !_themeSelectionUI.HasSelected)
                OpeningState = PromptState.AwaitingPrompt;

            if (_hotbarController != null)
                _hotbarController.Initialize(Manifest, StyleProfile);

            if (_propPlacer != null)
                _propPlacer.Initialize(_hotbarController, StyleProfile, this);

            if (_assistantSystem != null)
                _assistantSystem.Initialize(_propPlacer, _hotbarController, StyleProfile, Manifest, _sandboxFloorRoot);

            if (_sandboxEnterTrigger == null && _startSandboxImmediately)
                BeginSandbox();
        }

        private void BootstrapMissingDependencies()
        {
            // Sandbox floor
            if (_sandboxFloorRoot == null)
            {
                var existing = GameObject.Find("sandboxpedestal")
                               ?? GameObject.Find("SandboxPedestal")
                               ?? GameObject.Find("SandboxFloor");
                if (existing != null)
                {
                    _sandboxFloorRoot = existing.transform;
                }
                else
                {
                    var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
                    floor.name = "SandboxFloor";
                    floor.transform.position = Vector3.zero;
                    // Plane primitive is 10x10; scale to desired size
                    floor.transform.localScale = new Vector3(_defaultFloorSize.x / 10f, 1f, _defaultFloorSize.y / 10f);
                    _sandboxFloorRoot = floor.transform;
                    Debug.Log($"[SandboxManager] Auto-created SandboxFloor ({_defaultFloorSize.x}x{_defaultFloorSize.y})");
                }
            }

            // PropPlacer + SculptureSpawner
            if (_propPlacer == null)
            {
                _propPlacer = FindFirstObjectByType<PropPlacer>();
                if (_propPlacer == null)
                {
                    var go = new GameObject("PropPlacer");
                    go.transform.SetParent(transform);
                    go.AddComponent<AlgorithmicGallery.SculptureSpawner>();
                    _propPlacer = go.AddComponent<PropPlacer>();
                    Debug.Log("[SandboxManager] Auto-created PropPlacer");
                }
            }

            // HotbarController + HotbarUI
            if (_hotbarController == null)
            {
                _hotbarController = FindFirstObjectByType<HotbarController>();
                if (_hotbarController == null)
                {
                    var go = new GameObject("Hotbar");
                    go.transform.SetParent(transform);
                    _hotbarController = go.AddComponent<HotbarController>();
                    go.AddComponent<HotbarUI>();
                    Debug.Log("[SandboxManager] Auto-created HotbarController + HotbarUI");
                }
                else if (_hotbarController.GetComponent<HotbarUI>() == null)
                {
                    _hotbarController.gameObject.AddComponent<HotbarUI>();
                }
            }

            // AssistantSystem
            if (_assistantSystem == null)
            {
                _assistantSystem = FindFirstObjectByType<AssistantSystem>();
                if (_assistantSystem == null)
                {
                    var go = new GameObject("AssistantSystem");
                    go.transform.SetParent(transform);
                    _assistantSystem = go.AddComponent<AssistantSystem>();
                    Debug.Log("[SandboxManager] Auto-created AssistantSystem");
                }
            }

            // Player rig (only if no MainCamera exists)
            if (Camera.main == null)
            {
                var rig = new GameObject("PlayerRig");
                rig.transform.position = new Vector3(0f, 0f, -3f);
                rig.AddComponent<SimplePlayerRig>();
                Debug.Log("[SandboxManager] Auto-created PlayerRig (no MainCamera found)");
            }
            else if (Camera.main.GetComponentInParent<SimplePlayerRig>() == null
                     && FindFirstObjectByType<SimplePlayerRig>() == null
                     && FindFirstObjectByType<CharacterController>() == null)
            {
                // There's a camera but nothing to drive it. Add a rig parent.
                var camTransform = Camera.main.transform;
                var rig = new GameObject("PlayerRig");
                rig.transform.position = camTransform.position;
                rig.AddComponent<SimplePlayerRig>();
                camTransform.SetParent(rig.transform);
                camTransform.localPosition = new Vector3(0f, 1.6f, 0f);
                Debug.Log("[SandboxManager] Wrapped existing MainCamera in SimplePlayerRig");
            }

            // Prop budget
            if (PropBudget.Instance == null)
            {
                var pb = new GameObject("PropBudget");
                pb.transform.SetParent(transform);
                pb.AddComponent<PropBudget>();
            }

            // Session exporter
            if (FindFirstObjectByType<SessionExporter>() == null)
            {
                var se = new GameObject("SessionExporter");
                se.transform.SetParent(transform);
                se.AddComponent<SessionExporter>();
            }

            // End card
            // (End card removed — do not auto-create)

            // Thumbnail capture (singleton)
            if (RuntimeThumbnailCapture.Instance == null)
            {
                var tc = new GameObject("RuntimeThumbnailCapture");
                tc.transform.SetParent(transform);
                tc.AddComponent<RuntimeThumbnailCapture>();
            }

            // Echo feedback system
            if (FindFirstObjectByType<EchoFeedback>() == null)
            {
                var echoGO = new GameObject("EchoFeedback");
                echoGO.transform.SetParent(transform);
                echoGO.AddComponent<EchoFeedback>();
            }

            // Theme selection UI
            _themeSelectionUI = FindFirstObjectByType<ThemeSelectionUI>();
            if (_themeSelectionUI == null)
            {
                var tsGO = new GameObject("ThemeSelectionUI");
                tsGO.transform.SetParent(transform);
                _themeSelectionUI = tsGO.AddComponent<ThemeSelectionUI>();
            }
            _themeSelectionUI.OnPromptSelected += OnPromptSelected;

            // Title screen UI (overlay) — created if missing
            if (FindFirstObjectByType<TitleScreenUI>() == null)
            {
                var ts = new GameObject("TitleScreenUI");
                ts.transform.SetParent(transform);
                ts.AddComponent<TitleScreenUI>();
            }

            // Debug UI
            if (FindFirstObjectByType<AssistantDebugUI>() == null)
            {
                var dbg = new GameObject("AssistantDebugUI");
                dbg.transform.SetParent(transform);
                var ui = dbg.AddComponent<AssistantDebugUI>();
                // Use reflection-free assignment via SerializedProperty isn't possible at runtime,
                // so AssistantDebugUI uses FindFirstObjectByType in OnGUI fallback (see below).
            }

            // Interaction/system SFX
            if (FindFirstObjectByType<SandboxSfx>() == null)
            {
                var sfx = new GameObject("SandboxSfx");
                sfx.transform.SetParent(transform);
                sfx.AddComponent<AudioSource>();
                sfx.AddComponent<SandboxSfx>();
            }

            // Ambient audio escalation — volume/pitch follow assistant influence
            if (FindFirstObjectByType<AudioEscalation>() == null)
            {
                var aeGO = new GameObject("AudioEscalation");
                aeGO.transform.SetParent(transform);
                var aeSrc = aeGO.AddComponent<AudioSource>();
                aeSrc.loop = true;
                aeSrc.volume = 0.2f;
                aeSrc.playOnAwake = false;
                aeGO.AddComponent<AudioEscalation>();
                Debug.Log("[SandboxManager] Auto-created AudioEscalation");
            }

            // Reactive PSX glitch bursts tied to assistant events.
            if (FindFirstObjectByType<SandboxReactiveVfxDirector>() == null)
            {
                var vfx = new GameObject("SandboxReactiveVfxDirector");
                vfx.transform.SetParent(transform);
                vfx.AddComponent<SandboxReactiveVfxDirector>();
            }

            // Prompt-driven mood lighting — shifts directional light color/intensity per prompt
            if (FindFirstObjectByType<PromptMoodLighting>() == null)
            {
                var pml = new GameObject("PromptMoodLighting");
                pml.transform.SetParent(transform);
                pml.AddComponent<PromptMoodLighting>();
            }

            // Minimal lighting if scene has none
            if (FindFirstObjectByType<Light>() == null)
            {
                var lightGO = new GameObject("Sun");
                lightGO.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
                var light = lightGO.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.1f;
                Debug.Log("[SandboxManager] Auto-created directional light");
            }
        }

        private void OnPromptSelected(PromptDefinition prompt)
        {
            if (_commitRoutine != null)
                StopCoroutine(_commitRoutine);

            // Seed the personal dimension labels for the scoring wall from the flatten output.
            // Falls back to prompt tags, then a fixed set.
            _personalDimensionTags = _themeSelectionUI?.LastFlattenedTags
                ?? prompt?.EmotionalTags
                ?? new[] { "personal", "nostalgic", "intimate" };

            _commitRoutine = StartCoroutine(CommitPromptFlow(prompt));
        }

        private IEnumerator CommitPromptFlow(PromptDefinition prompt)
        {
            SelectedPrompt = prompt;
            Debug.Log($"[SandboxManager] Prompt selected: \"{prompt.DisplayText}\"");
            OpeningState = PromptState.SquishingText;
            OnPromptSquishStarted?.Invoke();

            // Terminal/UI squish and reduction readout beat.
            yield return new WaitForSecondsRealtime(0.95f);

            OpeningState = PromptState.InterpretingPrompt;
            yield return new WaitForSecondsRealtime(0.6f);

            _hotbarController?.SetActivePrompt(prompt);
            _assistantSystem?.SetActivePrompt(prompt);
            OpeningState = PromptState.PromptCommitted;
            OnPromptCommitted?.Invoke();

            HallwayUnlocked = true;
            OpeningState = PromptState.HallwayUnlocked;
            OnHallwayUnlocked?.Invoke();

            if (_startSandboxImmediately)
                ActivateSandbox();
        }

        // Called from the hallway trigger (HallwayTrigger.cs) or auto on Start.
        // If a prompt selection UI exists, this shows the prompt screen first.
        public void BeginSandbox()
        {
            if (SandboxActive) return;
            if (!HallwayUnlocked && _themeSelectionUI != null)
                return;

            if (_themeSelectionUI != null && !_themeSelectionUI.HasSelected)
            {
                // Wait for player to pick a prompt — OnPromptSelected will call ActivateSandbox
                return;
            }

            ActivateSandbox();
        }

        private void ActivateSandbox()
        {
            if (SandboxActive) return;
            SandboxActive = true;
            OpeningState = PromptState.SandboxActive;

            if (_propPlacer != null)
                _propPlacer.PlacementEnabled = true;

            if (_assistantSystem != null)
                _assistantSystem.StartSession();

            OnSandboxEntered?.Invoke();
            Debug.Log("[SandboxManager] Sandbox session begun.");

            if (!_startTimerOnFirstPlayerPlacement)
            {
                _sessionTimerStarted = true;
                StartCoroutine(SessionTimer());
            }
        }

        // Called by PropPlacer after the player successfully places a prop.
        public void NotifyPlayerPlaced(Vector3 worldPos)
        {
            if (!SandboxActive) return;
            _assistantSystem?.OnPlayerPlaced(worldPos);

            if (_startTimerOnFirstPlayerPlacement && !_sessionTimerStarted)
            {
                _sessionTimerStarted = true;
                StartCoroutine(SessionTimer());
            }
        }

        private IEnumerator SessionTimer()
        {
            yield return new WaitForSeconds(_sessionDuration);

            if (_propPlacer != null)
                _propPlacer.PlacementEnabled = false;

            yield return new WaitForSeconds(_endGracePeriod);

            _assistantSystem?.StopSession();
            OnSessionComplete?.Invoke();
            Debug.Log("[SandboxManager] Session complete.");

            // End card removed. At session end, the player should be able to leave the sandbox
            // and walk back into the hallway to view their persisted diorama.
            var hotbarUI = FindFirstObjectByType<HotbarUI>();
            if (hotbarUI != null)
            {
                var hbCg = hotbarUI.GetComponentInChildren<CanvasGroup>();
                if (hbCg != null) { hbCg.alpha = 0f; hbCg.blocksRaycasts = false; }
            }
        }
    }

    public enum PromptState
    {
        InIntakeRoom,
        AwaitingPrompt,
        SquishingText,
        InterpretingPrompt,
        PromptCommitted,
        HallwayUnlocked,
        SandboxActive,
    }
}

using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        [Header("Curated manifest")]
        [Tooltip("If true, loads split personal/corporate manifests when present; otherwise falls back to game-ready or full manifest.")]
        [SerializeField] private bool _preferSplitManifests = true;
        [SerializeField] private string _personalManifestFileName = "curated-props.personal.json";
        [SerializeField] private string _corporateManifestFileName = "curated-props.corporate.json";
        [Tooltip("If true, loads curated-props.game-ready.json when split files are missing.")]
        [SerializeField] private bool _preferGameReadyManifest = true;
        [SerializeField] private string _gameReadyManifestFileName = "curated-props.game-ready.json";
        [SerializeField] private string _fallbackManifestFileName = CuratedPropManifest.DefaultManifestFileName;

        [Header("Auto-bootstrap")]
        [Tooltip("If true, missing dependencies are created at Start.")]
        [SerializeField] private bool _autoBootstrap = true;
        [Tooltip("If true and no enter trigger is assigned, sandbox starts immediately on Start.")]
        [SerializeField] private bool _startSandboxImmediately = true;
        [SerializeField] private Vector2 _defaultFloorSize = new Vector2(10f, 10f);

        [Header("Session (placement cap)")]
        [Tooltip("Sandbox ends when total placements (player + assistant) reach this count.")]
        [SerializeField] private int _maxTotalPlacements = 25;
        [SerializeField] private float _endGracePeriod = 0f;
        [SerializeField] private float _hotbarFadeOutDuration = 0.35f;
        [SerializeField] private CanvasGroup _endFadeCanvasGroup;
        [SerializeField] private float _fadeDuration = 3f;

        [Header("Events")]
        public UnityEvent OnSandboxEntered;
        public UnityEvent OnSessionComplete;
        public UnityEvent OnPromptCommitted;
        public UnityEvent OnHallwayUnlocked;
        public UnityEvent OnPromptSquishStarted;

        public bool SandboxActive { get; private set; }
        public PromptState OpeningState { get; private set; } = PromptState.InIntakeRoom;
        public bool HallwayUnlocked { get; private set; }
        /// <summary>Player hotbar pool (domestic, furniture, item, workshop).</summary>
        public CuratedPropManifest PersonalManifest { get; private set; }

        /// <summary>System / assistant placement pool (office, lab, tech, retail).</summary>
        public CuratedPropManifest CorporateManifest { get; private set; }

        /// <summary>Alias for <see cref="PersonalManifest"/> (backward compatibility).</summary>
        public CuratedPropManifest Manifest => PersonalManifest;
        public StyleProfile StyleProfile { get; private set; }
        public PromptDefinition SelectedPrompt { get; private set; }
        public AssistantSystem Assistant => _assistantSystem;
        public HotbarController Hotbar => _hotbarController;
        public PropPlacer Placer => _propPlacer;
        public Transform SandboxFloor => _sandboxFloorRoot;

        /// <summary>Three UI labels for the session score bars: two personal dimensions + corporate target.</summary>
        public string[] ScoringBarLabels => _scoringBarLabels ?? FallbackScoringBarLabels;

        /// <summary>Emotional slugs for bars 0–1 (for placement score routing).</summary>
        public string ScoringPersonalSlug0 => _scoringPersonalSlug0 ?? "personal";
        public string ScoringPersonalSlug1 => _scoringPersonalSlug1 ?? "nostalgic";
        /// <summary>Corporate taxonomy slug for bar 2 (matches <see cref="PromptDefinition.CorporateTargetTag"/>).</summary>
        public string ScoringCorporateSlug => _scoringCorporateSlug ?? "marketable";

        // Personal-dimension tags — kept for compatibility; now mirrors <see cref="ScoringBarLabels"/> (three entries).
        private string[] _personalDimensionTags;
        private string[] _scoringBarLabels;
        private string _scoringPersonalSlug0;
        private string _scoringPersonalSlug1;
        private string _scoringCorporateSlug;

        private static readonly string[] FallbackScoringBarLabels = { "Personal", "Nostalgic", "Marketable" };

        public string[] PersonalDimensionTags
        {
            get
            {
                if (_personalDimensionTags != null && _personalDimensionTags.Length > 0)
                    return _personalDimensionTags;

                if (_scoringBarLabels != null && _scoringBarLabels.Length > 0)
                    return _scoringBarLabels;

                if (StyleProfile != null)
                {
                    var emotional = StyleProfile.DominantEmotionalTags(3);
                    if (emotional != null && emotional.Count > 0)
                        return emotional.ToArray();

                    var generic = StyleProfile.DominantTags(3);
                    if (generic != null && generic.Count > 0)
                        return generic.ToArray();
                }

                return FallbackPersonalDimensionTags;
            }
        }

        private static readonly string[] FallbackPersonalDimensionTags = { "personal", "nostalgic", "intimate" };

        private ThemeSelectionUI _themeSelectionUI;
        private bool _sessionCompletionStarted;
        private Coroutine _commitRoutine;

        void Start()
        {
            StyleProfile = new StyleProfile();
            if (!LoadManifestsForSandbox())
            {
                Debug.LogError("[SandboxManager] Failed to load curated-props manifest(s). Aborting.");
                return;
            }

            if (_autoBootstrap)
                BootstrapMissingDependencies();

            if (_themeSelectionUI != null && !_themeSelectionUI.HasSelected)
                OpeningState = PromptState.AwaitingPrompt;

            if (_hotbarController != null)
                _hotbarController.Initialize(PersonalManifest, StyleProfile);

            if (_propPlacer != null)
            {
                _propPlacer.Initialize(_hotbarController, StyleProfile, this);
                _propPlacer.OnPropPlacedWithContext += HandlePlacementScoreWithContext;
            }

            if (_assistantSystem != null)
            {
                var systemManifest = CorporateManifest != null && CorporateManifest.Count > 0
                    ? CorporateManifest
                    : PersonalManifest;
                _assistantSystem.Initialize(_propPlacer, _hotbarController, StyleProfile, systemManifest, _sandboxFloorRoot);
            }

            if (_endFadeCanvasGroup != null)
                _endFadeCanvasGroup.alpha = 0f;

            if (_sandboxEnterTrigger == null && _startSandboxImmediately)
                BeginSandbox();
        }

        /// <summary>
        /// Loads personal + corporate manifests for sandbox. Run tools/split_corporate_personal.py after game-ready split.
        /// </summary>
        private bool LoadManifestsForSandbox()
        {
            if (_preferSplitManifests)
            {
                string personalPath = CuratedPropManifest.ManifestPath(_personalManifestFileName);
                string corporatePath = CuratedPropManifest.ManifestPath(_corporateManifestFileName);

                if (File.Exists(personalPath) && File.Exists(corporatePath))
                {
                    PersonalManifest = CuratedPropManifest.LoadFromStreamingAssets(_personalManifestFileName);
                    CorporateManifest = CuratedPropManifest.LoadFromStreamingAssets(_corporateManifestFileName);

                    if (PersonalManifest != null && PersonalManifest.Count > 0 &&
                        CorporateManifest != null && CorporateManifest.Count > 0)
                    {
                        Debug.Log(
                            $"[SandboxManager] Using split manifests: personal={PersonalManifest.Count} " +
                            $"({_personalManifestFileName}), corporate={CorporateManifest.Count} ({_corporateManifestFileName}).");
                        return true;
                    }

                    Debug.LogWarning("[SandboxManager] Split manifest(s) empty or failed to parse; trying game-ready fallback.");
                }
                else
                {
                    Debug.LogWarning(
                        $"[SandboxManager] Split manifests not found. Run: python3 tools/split_corporate_personal.py");
                }
            }

            var combined = LoadCombinedManifestFallback();
            if (combined == null || combined.Count == 0)
                return false;

            PersonalManifest = combined;
            CorporateManifest = combined;
            Debug.LogWarning(
                $"[SandboxManager] Using combined manifest for both pools ({combined.Count} props). " +
                "Run tools/split_corporate_personal.py for a proper personal/corporate split.");
            return true;
        }

        private CuratedPropManifest LoadCombinedManifestFallback()
        {
            if (_preferGameReadyManifest &&
                !string.IsNullOrWhiteSpace(_gameReadyManifestFileName))
            {
                string grPath = CuratedPropManifest.ManifestPath(_gameReadyManifestFileName);
                if (File.Exists(grPath))
                {
                    var gameReady = CuratedPropManifest.LoadFromStreamingAssets(_gameReadyManifestFileName);
                    if (gameReady != null && gameReady.Count > 0)
                    {
                        Debug.Log($"[SandboxManager] Using game-ready manifest ({_gameReadyManifestFileName}), count={gameReady.Count}.");
                        return gameReady;
                    }

                    Debug.LogWarning(
                        $"[SandboxManager] Game-ready manifest empty or failed to parse: {_gameReadyManifestFileName}. " +
                        $"Falling back to {_fallbackManifestFileName}.");
                }
                else
                {
                    Debug.LogWarning(
                        $"[SandboxManager] Game-ready manifest not found at {grPath}. " +
                        $"Run tools/split_game_ready_manifest.py. Using {_fallbackManifestFileName}.");
                }
            }

            return CuratedPropManifest.LoadFromStreamingAssets(_fallbackManifestFileName);
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

            // (EndCard intentionally not bootstrapped — session end uses rig freeze + hotbar hide
            // and the diorama loop reload instead of a black overlay screen.)

            // Scoring wall — scene-placed only. Warn if missing so the wall's absence is visible.
            if (FindFirstObjectByType<ScoringWallController>() == null)
                Debug.LogWarning("[SandboxManager] ScoringWallController not found in scene. Scoring wall will not appear.");

            // Title screen — auto-create if not scene-placed so the loop always starts behind a Begin gate.
            if (FindFirstObjectByType<TitleScreenUI>() == null)
            {
                var ts = new GameObject("TitleScreenUI");
                ts.transform.SetParent(transform);
                ts.AddComponent<TitleScreenUI>();
                Debug.Log("[SandboxManager] Auto-created TitleScreenUI");
            }

            // HallwayManager safety-net — required for live pedestal refresh on session end.
            // Alyssa can override by placing one in the scene with explicit anchors.
            if (FindFirstObjectByType<HallwayManager>() == null)
            {
                var hmGO = new GameObject("HallwayManager");
                hmGO.transform.SetParent(transform);
                hmGO.AddComponent<HallwayManager>();
                Debug.Log("[SandboxManager] Auto-created HallwayManager (no scene-placed instance found)");
            }

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

        void OnDestroy()
        {
            if (_themeSelectionUI != null)
                _themeSelectionUI.OnPromptSelected -= OnPromptSelected;
            if (_propPlacer != null)
                _propPlacer.OnPropPlacedWithContext -= HandlePlacementScoreWithContext;
        }

        private void OnPromptSelected(PromptDefinition prompt)
        {
            if (_commitRoutine != null)
                StopCoroutine(_commitRoutine);
            _commitRoutine = StartCoroutine(CommitPromptFlow(prompt));
        }

        private void ApplyScoringSessionToPrompt(PromptDefinition prompt)
        {
            if (prompt == null) return;
            PromptScoringHelper.EnsureCorporateTarget(prompt);
            _scoringBarLabels = PromptScoringHelper.BuildThreeFlattenLabels(prompt);
            var slugs = PromptScoringHelper.PersonalSlugsForScoring(prompt);
            _scoringPersonalSlug0 = slugs.slug0;
            _scoringPersonalSlug1 = slugs.slug1;
            _scoringCorporateSlug = string.IsNullOrWhiteSpace(prompt.CorporateTargetTag)
                ? "marketable"
                : prompt.CorporateTargetTag.Trim().ToLowerInvariant();
            _personalDimensionTags = _scoringBarLabels;

            var wall = FindFirstObjectByType<ScoringWallController>();
            if (wall != null)
                wall.ConfigureSession(_scoringBarLabels, 1500f);
        }

        private void HandlePlacementScoreWithContext(bool isPlayer, PropEntry prop, Vector3 worldPosition)
        {
            if (!SandboxActive || prop == null) return;

            var wall = FindFirstObjectByType<ScoringWallController>();
            var scoreEvents = BuildScoreEventsForPlacement(isPlayer, prop);
            if (scoreEvents.Count == 0)
                return;

            for (int i = 0; i < scoreEvents.Count; i++)
            {
                var evt = scoreEvents[i];
                wall?.AddScoreToBar(evt.barIndex, evt.amount);
                PlacementScoreFloater.Spawn(
                    worldPosition,
                    evt.amount,
                    evt.label,
                    useCorporateBlue: evt.barIndex == 2,
                    worldOffset: BuildFloaterOffset(i, scoreEvents.Count));
            }
        }

        private List<(int barIndex, int amount, string label)> BuildScoreEventsForPlacement(bool isPlayer, PropEntry prop)
        {
            var events = new List<(int barIndex, int amount, string label)>();
            var labels = ScoringBarLabels;

            if (!isPlayer)
            {
                // System placements still only score the corporate column.
                string corp = (labels != null && labels.Length > 2 && !string.IsNullOrWhiteSpace(labels[2]))
                    ? labels[2].Trim()
                    : PromptScoringHelper.CorporateBarDisplayLabel(SelectedPrompt);
                events.Add((2, UnityEngine.Random.Range(50, 101), corp));
                return events;
            }

            var matchedBars = ResolveMatchingPlayerBars(prop);
            if (matchedBars.Count == 0)
                matchedBars.Add(ResolveScoreBarIndex(prop));

            for (int i = 0; i < matchedBars.Count; i++)
            {
                int bar = matchedBars[i];
                string label = (bar >= 0 && bar < labels.Length) ? labels[bar] : "Score";
                events.Add((bar, UnityEngine.Random.Range(50, 101), label));
            }

            return events;
        }

        private List<int> ResolveMatchingPlayerBars(PropEntry prop)
        {
            var bars = new List<int>(3);
            if (ScorePersonalMatch(prop, ScoringPersonalSlug0) > 0f)
                bars.Add(0);
            if (ScorePersonalMatch(prop, ScoringPersonalSlug1) > 0f)
                bars.Add(1);
            if (ScoreCorporateMatch(prop, ScoringCorporateSlug) > 0f)
                bars.Add(2);
            return bars;
        }

        private static Vector3 BuildFloaterOffset(int index, int total)
        {
            if (total <= 1) return Vector3.zero;

            const float radius = 0.14f;
            const float yStep = 0.03f;
            float step = (Mathf.PI * 2f) / total;
            float angle = step * index;
            return new Vector3(Mathf.Cos(angle) * radius, index * yStep, Mathf.Sin(angle) * radius);
        }

        private int ResolveScoreBarIndex(PropEntry prop)
        {
            float w0 = ScorePersonalMatch(prop, ScoringPersonalSlug0) + 0.12f;
            float w1 = ScorePersonalMatch(prop, ScoringPersonalSlug1) + 0.12f;
            float w2 = ScoreCorporateMatch(prop, ScoringCorporateSlug) + 0.38f;
            float t = w0 + w1 + w2;
            float r = UnityEngine.Random.value * t;
            if (r < w0) return 0;
            r -= w0;
            if (r < w1) return 1;
            return 2;
        }

        private static float ScorePersonalMatch(PropEntry p, string slug)
        {
            if (p == null || string.IsNullOrWhiteSpace(slug)) return 0f;
            int n = 0;
            if (p.EmotionalTags != null)
                n += p.EmotionalTags.Count(t => string.Equals(t, slug, System.StringComparison.OrdinalIgnoreCase));
            if (p.PersonalTags != null)
                n += p.PersonalTags.Count(t => string.Equals(t, slug, System.StringComparison.OrdinalIgnoreCase));
            return Mathf.Min(5, n) * 0.85f;
        }

        private static float ScoreCorporateMatch(PropEntry p, string slug)
        {
            if (p?.CorporateTags == null || string.IsNullOrWhiteSpace(slug)) return 0f;
            return p.CorporateTags.Any(c => string.Equals(c, slug, System.StringComparison.OrdinalIgnoreCase))
                ? 2.4f
                : 0f;
        }

        private IEnumerator CommitPromptFlow(PromptDefinition prompt)
        {
            SelectedPrompt = prompt;
            Debug.Log($"[SandboxManager] Prompt selected: \"{prompt.DisplayText}\"");
            OpeningState = PromptState.SquishingText;
            OnPromptSquishStarted?.Invoke();
            GameplayEventDebugLog.Push("Sandbox", "OnPromptSquishStarted");

            // Terminal/UI squish and reduction readout beat.
            yield return new WaitForSecondsRealtime(0.95f);

            OpeningState = PromptState.InterpretingPrompt;
            yield return new WaitForSecondsRealtime(0.6f);

            ApplyScoringSessionToPrompt(prompt);
            _hotbarController?.SetActivePrompt(prompt);
            _assistantSystem?.SetActivePrompt(prompt);
            OpeningState = PromptState.PromptCommitted;
            OnPromptCommitted?.Invoke();
            GameplayEventDebugLog.Push("Sandbox", "OnPromptCommitted");

            HallwayUnlocked = true;
            OpeningState = PromptState.HallwayUnlocked;
            OnHallwayUnlocked?.Invoke();
            GameplayEventDebugLog.Push("Sandbox", "OnHallwayUnlocked");

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
            _sessionCompletionStarted = false;

            if (_propPlacer != null)
                _propPlacer.PlacementEnabled = true;

            if (_assistantSystem != null)
                _assistantSystem.StartSession();

            OnSandboxEntered?.Invoke();
            GameplayEventDebugLog.Push("Sandbox", "OnSandboxEntered");
            Debug.Log("[SandboxManager] Sandbox session begun.");
        }

        /// <summary>Remaining placements until the session completes (player + assistant).</summary>
        public int GetPlacementsLeft()
        {
            if (StyleProfile == null)
                return Mathf.Max(0, _maxTotalPlacements);
            return Mathf.Max(0, _maxTotalPlacements - StyleProfile.PlacementCount);
        }

        // Called by PropPlacer after the player successfully places a prop.
        public void NotifyPlayerPlaced(Vector3 worldPos)
        {
            if (!SandboxActive) return;
            _assistantSystem?.OnPlayerPlaced(worldPos);
        }

        /// <summary>
        /// Called after any successful placement is recorded (player or assistant).
        /// Ends the sandbox when total placements reach <see cref="_maxTotalPlacements"/>.
        /// </summary>
        public void NotifyPlacementRecorded()
        {
            if (_sessionCompletionStarted || !SandboxActive || StyleProfile == null)
                return;
            if (StyleProfile.PlacementCount < _maxTotalPlacements)
                return;

            _sessionCompletionStarted = true;
            SandboxActive = false;

            if (_propPlacer != null)
                _propPlacer.PlacementEnabled = false;

            _assistantSystem?.StopSession();

            // Fire completion immediately so the hallway door opens the moment placements run out.
            OnSessionComplete?.Invoke();
            GameplayEventDebugLog.Push("Sandbox", "OnSessionComplete");
            Debug.Log("[SandboxManager] Session complete.");

            // Fade hotbar out right away at placement cap.
            StartCoroutine(FadeOutHotbarUi());

            StartCoroutine(CompleteSessionSequence());
        }

        private IEnumerator CompleteSessionSequence()
        {
            yield return new WaitForSeconds(_endGracePeriod);

            // Freeze player movement and hide the hotbar for the post-session walkback.
            // The session-complete listeners (gate open, live pedestal refresh) take it from here.
            PlayerInputFreeze.FreezePlayerLocomotion();

            // Restore the rig now that gate/diorama listeners have been notified — the player
            // needs to be able to walk back to the hallway. Hotbar stays hidden.
            PlayerInputFreeze.RestorePlayerLocomotion();

            if (_endFadeCanvasGroup != null)
                yield return FadeOut();
        }

        private IEnumerator FadeOutHotbarUi()
        {
            var hotbarUI = FindFirstObjectByType<HotbarUI>();
            if (hotbarUI == null)
                yield break;

            var hbCg = hotbarUI.GetComponentInChildren<CanvasGroup>(includeInactive: true);
            if (hbCg == null)
            {
                hotbarUI.gameObject.SetActive(false);
                yield break;
            }

            hbCg.blocksRaycasts = false;
            hbCg.interactable = false;

            float startAlpha = hbCg.alpha;
            float dur = Mathf.Max(0.01f, _hotbarFadeOutDuration);
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                hbCg.alpha = Mathf.Lerp(startAlpha, 0f, t / dur);
                yield return null;
            }
            hbCg.alpha = 0f;
        }

        private IEnumerator FadeOut()
        {
            float elapsed = 0f;
            while (elapsed < _fadeDuration)
            {
                elapsed += Time.deltaTime;
                _endFadeCanvasGroup.alpha = Mathf.Clamp01(elapsed / _fadeDuration);
                yield return null;
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

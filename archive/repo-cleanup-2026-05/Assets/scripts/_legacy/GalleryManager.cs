using UnityEngine;
using UnityEngine.Rendering;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using AlgorithmicGallery.Recommendation;

namespace AlgorithmicGallery
{
    /// <summary>
    /// Top-level orchestrator for the Algorithmic Gallery.
    /// Initializes the recommendation engine, manages sculpture spawning, and tracks session state.
    /// Communicates with GazeManager to drive the gallery experience.
    /// </summary>
    public class GalleryManager : MonoBehaviour
    {
        [System.Serializable]
        private class ModelFilterSettings
        {
            [SerializeField]
            private bool _enabled = true;

            [Header("Path / Category Allow-list")]
            [Tooltip("When enabled, only models whose category or glb_path contains at least one allowed keyword will pass.")]
            [SerializeField]
            private bool _usePathAllowList = true;
            [Tooltip("A model is allowed if its category OR glb_path contains any of these keywords (case-insensitive, partial match).")]
            [SerializeField]
            private string[] _allowedPathKeywords = new[]
            {
                "props", "characters", "weapons", "infected", "items",
                "player", "survivors", "vehicles", "npcs", "gibs",
                "humans", "zombie", "bots", "furniture", "deadbodies"
            };

            [Header("Path / Category Deny-list")]
            [Tooltip("Even if a model passes the allow-list, these keywords reject it (overrides allow).")]
            [SerializeField]
            private string[] _deniedPathKeywords = new[]
            {
                "editor", "shadertest", "perftest", "vgui", "skybox",
                "shells", "anim_wp", "hybridphysx", "class_menu",
                "handles_map_editor", "props_map_editor", "props_silhouettes",
                "stars", "portals", "testing", "test",
                "terrain", "cliffs", "horizon", "facade", "bridge",
                "building", "buildings", "hangar", "highway", "sewer",
                "canal", "rockwall",
                "props_buildings", "props_underground", "props_highway",
                "props_sewer", "props_canal", "props_skybox", "props_rocks"
            };

            [Header("Name Deny-list")]
            [SerializeField]
            private bool _filterByObjectName = true;
            [SerializeField]
            private string[] _objectNameDenyKeywords = new[]
            {
                "node", "nodes", "collision", "collider", "helper",
                "trigger", "lod", "dummy", "origin", "socket"
            };

            [Header("Poly / Vertex Bounds")]
            [SerializeField]
            private bool _filterByPolyCount = true;
            [SerializeField]
            private int _minPolyCount = 20;
            [SerializeField]
            private int _maxPolyCount = 0;
            [Header("Dimension Bounds")]
            [Tooltip("Reject models whose largest metadata dimension exceeds this value (0 disables).")]
            [SerializeField]
            private float _maxModelAxisDimension = 4f;
            [Tooltip("Reject models whose metadata volume (x*y*z) exceeds this value (0 disables).")]
            [SerializeField]
            private float _maxModelVolume = 20f;
            [Tooltip("Reject models tagged as monumental in metadata scale tag.")]
            [SerializeField]
            private bool _rejectMonumentalScaleTag = true;

            [Tooltip("When true, ineligible models are excluded from recommender candidates by marking them as shown.")]
            [SerializeField]
            private bool _markIneligibleAsShown = true;
            [SerializeField]
            private bool _logDiagnostics = true;

            public bool Enabled => _enabled;
            public bool MarkIneligibleAsShown => _markIneligibleAsShown;
            public bool LogDiagnostics => _logDiagnostics;

            public bool IsPathRejected(string category, string glbPath)
            {
                string catLower = string.IsNullOrWhiteSpace(category) ? "" : category.ToLowerInvariant();
                string pathLower = string.IsNullOrWhiteSpace(glbPath) ? "" : glbPath.ToLowerInvariant();

                if (ContainsAnyKeyword(catLower, _deniedPathKeywords) || ContainsAnyKeyword(pathLower, _deniedPathKeywords))
                    return true;

                if (!_usePathAllowList || _allowedPathKeywords == null || _allowedPathKeywords.Length == 0)
                    return false;

                bool allowed = ContainsAnyKeyword(catLower, _allowedPathKeywords) || ContainsAnyKeyword(pathLower, _allowedPathKeywords);
                return !allowed;
            }

            public bool IsNameDenied(string value)
            {
                if (!_filterByObjectName)
                    return false;

                string lower = string.IsNullOrWhiteSpace(value) ? "" : value.ToLowerInvariant();
                return ContainsAnyKeyword(lower, _objectNameDenyKeywords);
            }

            public bool IsPolyRejected(int polyCount)
            {
                if (!_filterByPolyCount || polyCount <= 0)
                    return false;

                if (_minPolyCount > 0 && polyCount < _minPolyCount)
                    return true;
                if (_maxPolyCount > 0 && polyCount > _maxPolyCount)
                    return true;
                return false;
            }

            public bool IsDimensionsRejected(ModelDimensions dimensions, string scaleTag)
            {
                if (_rejectMonumentalScaleTag && !string.IsNullOrWhiteSpace(scaleTag) &&
                    string.Equals(scaleTag.Trim(), "monumental", System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (dimensions == null)
                    return false;

                float dx = Mathf.Max(0f, dimensions.X);
                float dy = Mathf.Max(0f, dimensions.Y);
                float dz = Mathf.Max(0f, dimensions.Z);
                if (dx <= 0f || dy <= 0f || dz <= 0f)
                    return false;

                if (_maxModelAxisDimension > 0f)
                {
                    float maxAxis = Mathf.Max(dx, Mathf.Max(dy, dz));
                    if (maxAxis > _maxModelAxisDimension)
                        return true;
                }

                if (_maxModelVolume > 0f)
                {
                    float volume = dx * dy * dz;
                    if (volume > _maxModelVolume)
                        return true;
                }

                return false;
            }

            private static bool ContainsAnyKeyword(string lowerValue, string[] keywords)
            {
                if (string.IsNullOrEmpty(lowerValue) || keywords == null || keywords.Length == 0)
                    return false;

                for (int i = 0; i < keywords.Length; i++)
                {
                    string keyword = keywords[i];
                    if (string.IsNullOrWhiteSpace(keyword))
                        continue;

                    if (lowerValue.Contains(keyword.Trim().ToLowerInvariant()))
                        return true;
                }

                return false;
            }
        }

        private enum ModelFilterRejectReason
        {
            None = 0,
            MissingId,
            GameNotAllowed,
            MissingFile,
            PathRejected,
            NameRejected,
            PolyRejected,
            DimensionsRejected
        }

        private struct ModelFilterStats
        {
            public int accepted;
            public int rejectedMissingId;
            public int rejectedByGame;
            public int rejectedByMissingFile;
            public int rejectedByPath;
            public int rejectedByName;
            public int rejectedByPoly;
            public int rejectedByDimensions;

            public int TotalRejected =>
                rejectedMissingId +
                rejectedByGame +
                rejectedByMissingFile +
                rejectedByPath +
                rejectedByName +
                rejectedByPoly +
                rejectedByDimensions;
        }

        [Header("Core Setup")]
        [SerializeField]
        private Transform[] _pedestalSlots;

        [SerializeField]
        private GazeManager _gazeManager;

        [SerializeField]
        private SculptureSpawner _spawner;

        [Header("Configuration")]
        [SerializeField]
        private string _metadataJsonPath = "metadata.json";

        [Tooltip("If set AND _useAllowedGameFilter is true, only models from these games are eligible.")]
        [SerializeField]
        private string[] _allowedGames = new[] { "portal", "portal2", "hl2", "tf2", "css", "dod", "l4d2" };
        [Tooltip("When disabled (default), models from ALL games are eligible. The content filters still apply.")]
        [SerializeField]
        private bool _useAllowedGameFilter = false;
        [SerializeField]
        private ModelFilterSettings _modelFilters = new ModelFilterSettings();
        [Tooltip("Removes placeholder mesh/collider components from pedestal slots when first real model spawns.")]
        [SerializeField]
        private bool _clearPlaceholderPedestalsOnFirstSpawn = true;
        [Header("Sculpture Tree")]
        [Tooltip("Primary target for per-sculpture part count.")]
        [SerializeField]
        private int _modelsPerSculptureTarget = 200;
        [Tooltip("Absolute cap for per-sculpture part count.")]
        [SerializeField]
        private int _modelsPerSculptureHardCap = 300;
        [SerializeField]
        private int _maxBlendAssemblyAttempts = 6000;
        [SerializeField]
        private bool _allowDuplicatePartFill = true;
        [SerializeField]
        private bool _relaxFiltersOnUnderfill = true;
        [SerializeField, Range(0.5f, 1f)]
        private float _requiredFillRatio = 1f;
        [SerializeField]
        private bool _reserveSelectedPartsAcrossPedestals = false;
        [Tooltip("Number of trunk/core models at the base.")]
        [SerializeField]
        private int _trunkCount = 40;
        [SerializeField]
        private float _trunkRadius = 0.58f;
        [SerializeField]
        private float _trunkHeight = 3.8f;
        [SerializeField]
        private int _tendrilCount = 16;
        [SerializeField]
        private float _tendrilStepSize = 0.48f;
        [SerializeField]
        private float _tendrilCurvature = 26f;
        [SerializeField, Range(0f, 1f)]
        private float _forkChance = 0.22f;
        [SerializeField]
        private int _maxForkDepth = 5;
        [SerializeField]
        private float _tendrilUpwardBias = 0.05f;
        [SerializeField]
        private float _tendrilOutwardBias = 0.25f;
        [SerializeField]
        private float _tendrilMaxHeight = 6.5f;
        [SerializeField]
        private float _placementSpreadMultiplier = 1.05f;
        [SerializeField]
        private float _globalSculptureScale = 1.6f;
        [SerializeField, Range(0.05f, 0.6f)]
        private float _targetPartOverlapRatio = 0.38f;
        [SerializeField]
        private float _tighteningPassStrength = 0.6f;
        [SerializeField]
        private bool _enablePartShadowOnTrunk = true;
        [Header("Sculpture Bounds")]
        [Tooltip("Hard clamp to keep assembled sculptures within room height (0 disables).")]
        [SerializeField]
        private float _maxSculptureHeight = 0f;
        [Tooltip("Hard clamp on XZ radius from sculpture center (0 disables).")]
        [SerializeField]
        private float _maxSculptureRadius = 0f;
        [SerializeField]
        private Vector2 _partScaleRange = new Vector2(0.8f, 1.6f);
        [SerializeField]
        private Vector2 _trunkPartScaleRange = new Vector2(1.2f, 2.2f);

        [Header("Retry Behavior")]
        [SerializeField]
        private float _spawnRetryIntervalSecs = 0.5f;
        [SerializeField]
        private float _noLoadableWarningIntervalSecs = 5f;

        [Header("Profile Bootstrap")]
        [Tooltip("Seeds profile weights at session start so recommendations have an initial bias.")]
        [SerializeField]
        private bool _randomizeInitialPreferences = true;
        [SerializeField]
        private int _initialPreferenceTagCount = 5;
        [SerializeField]
        private Vector2 _initialPreferenceRange = new Vector2(0.15f, 0.55f);

        private RecommendationEngine _engine;
        private MetadataIndex _metadataIndex;
        private float _sessionStartTime;
        private int _sculpturesViewed;
        private float _averageDwellTime;
        private List<float> _dwellTimes = new List<float>();

        private Dictionary<Transform, SculptureController> _activeStatues = new Dictionary<Transform, SculptureController>();
        private Dictionary<Transform, ModelEntry> _activeModels = new Dictionary<Transform, ModelEntry>();
        private HashSet<Transform> _pendingSpawns = new HashSet<Transform>();
        private HashSet<Transform> _preparedPedestals = new HashSet<Transform>();
        private HashSet<string> _unavailableModelIds = new HashSet<string>();
        private HashSet<string> _eligibleModelIds = new HashSet<string>();
        private List<ModelEntry> _eligibleModelEntries = new List<ModelEntry>();
        private List<ModelEntry> _fallbackModelEntries = new List<ModelEntry>();
        private List<string> _eligibleTags = new List<string>();
        private int _pedestalSlotGeneration;
        private float _nextSpawnRetryTime;
        private float _nextNoLoadableWarningTime;
        private bool _warnedMissingPedestalSlots;
        private bool _spawnSystemReady;
        private bool _deferredSpawnRequested;

        private void Awake()
        {
            // Force prototype-critical values at runtime so stale scene serialization cannot override them.
            _modelsPerSculptureTarget = 200;
            _modelsPerSculptureHardCap = 300;
            _maxBlendAssemblyAttempts = 6000;

            _trunkCount = 40;
            _trunkRadius = 0.58f;
            _trunkHeight = 3.8f;
            _tendrilCount = 16;
            _tendrilStepSize = 0.48f;
            _tendrilOutwardBias = 0.25f;
            _placementSpreadMultiplier = 1.05f;
            _globalSculptureScale = 1.6f;

            _maxSculptureHeight = 10f;
            _maxSculptureRadius = 0f;

            _targetPartOverlapRatio = 0.38f;
            _tighteningPassStrength = 0.6f;
            _partScaleRange = new Vector2(0.8f, 1.6f);
            _trunkPartScaleRange = new Vector2(1.2f, 2.2f);
            _reserveSelectedPartsAcrossPedestals = true;
        }

        private void Start()
        {
            _sessionStartTime = Time.time;
            _sculpturesViewed = 0;
            _spawnSystemReady = false;
            _deferredSpawnRequested = false;

            // Initialize metadata index
            _metadataIndex = new MetadataIndex();
            LoadMetadata();

            // Initialize recommendation engine
            _engine = new RecommendationEngine(_metadataIndex);
            PrepareEligiblePool();
            BootstrapInitialPreferences();
            LogLoadableSummary();
            _spawnSystemReady = _engine != null && _eligibleModelEntries.Count > 0;

            if (_gazeManager == null)
            {
                _gazeManager = FindFirstObjectByType<GazeManager>();
            }

            if (_spawner == null)
            {
                _spawner = GetComponent<SculptureSpawner>();
                if (_spawner == null)
                {
                    _spawner = gameObject.AddComponent<SculptureSpawner>();
                }
            }

            if (_pedestalSlots == null || _pedestalSlots.Length == 0)
            {
                Debug.LogWarning("GalleryManager: No pedestal slots assigned. Assign Transform positions in the inspector.");
                _warnedMissingPedestalSlots = true;
                return;
            }

            if (_spawnSystemReady)
            {
                SpawnForCurrentPedestals();
            }
            else
            {
                _deferredSpawnRequested = true;
            }
        }

        private void Update()
        {
            if (_pedestalSlots == null || _pedestalSlots.Length == 0)
                return;

            if (!_spawnSystemReady)
            {
                _spawnSystemReady = _engine != null && _eligibleModelEntries.Count > 0;
                if (_spawnSystemReady && _deferredSpawnRequested)
                {
                    _deferredSpawnRequested = false;
                    SpawnForCurrentPedestals();
                }
                return;
            }

            if (Time.time < _nextSpawnRetryTime)
                return;

            // Refill empty pedestals
            foreach (Transform pedestal in _pedestalSlots)
            {
                if (!_activeStatues.ContainsKey(pedestal) || _activeStatues[pedestal] == null)
                {
                    SpawnAtPedestal(pedestal);
                }
            }

            // Update average dwell time
            if (_dwellTimes.Count > 0)
            {
                float sum = 0;
                foreach (float dwell in _dwellTimes)
                {
                    sum += dwell;
                }
                _averageDwellTime = sum / _dwellTimes.Count;
            }
        }

        public void SetPedestalSlots(Transform[] pedestalSlots, bool clearSlotState = true, bool spawnImmediately = true)
        {
            _pedestalSlots = NormalizePedestalSlots(pedestalSlots);
            _pedestalSlotGeneration++;
            if (_pedestalSlots.Length == 0)
            {
                if (!_warnedMissingPedestalSlots)
                {
                    Debug.LogWarning("GalleryManager: SetPedestalSlots received no valid transforms.");
                    _warnedMissingPedestalSlots = true;
                }
                return;
            }

            _warnedMissingPedestalSlots = false;

            if (clearSlotState)
            {
                ClearPedestalRuntimeState();
            }

            if (spawnImmediately)
            {
                if (_spawnSystemReady)
                {
                    SpawnForCurrentPedestals();
                }
                else
                {
                    _deferredSpawnRequested = true;
                }
            }
        }

        private void LoadMetadata()
        {
            string metadataPath = Path.Combine(Application.streamingAssetsPath, _metadataJsonPath);

            if (!File.Exists(metadataPath))
            {
                Debug.LogError($"GalleryManager: Metadata file not found at {metadataPath}");
                return;
            }

            try
            {
                string jsonContent = File.ReadAllText(metadataPath);
                _metadataIndex.LoadFromJsonString(jsonContent);
                Debug.Log($"GalleryManager: Loaded {_metadataIndex.TotalCount} models from metadata.");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"GalleryManager: Failed to load metadata: {ex.Message}");
            }
        }

        private struct SpawnDiagnostics
        {
            public int target;
            public int attempted;
            public int loaded;
            public int failed;
            public bool finalized;
        }

        private struct PlacedPartData
        {
            public Vector3 localPosition;
            public float radius;
        }

        private void SpawnAtPedestal(Transform pedestal)
        {
            if (pedestal == null)
                return;

            if (!_spawnSystemReady)
            {
                _deferredSpawnRequested = true;
                return;
            }

            if (_pendingSpawns.Contains(pedestal))
                return;

            if (!TryGetBlendModels(out List<ModelEntry> blendModels, out int targetCount))
            {
                if (Time.time >= _nextNoLoadableWarningTime)
                {
                    Debug.LogWarning("GalleryManager: No loadable models available for current filters/content.");
                    _nextNoLoadableWarningTime = Time.time + Mathf.Max(1f, _noLoadableWarningIntervalSecs);
                }
                _nextSpawnRetryTime = Time.time + Mathf.Max(0.1f, _spawnRetryIntervalSecs);
                return;
            }

            _pendingSpawns.Add(pedestal);
            PreparePedestalForModel(pedestal);
            int generationToken = _pedestalSlotGeneration;
            SpawnModelBlendAsync(pedestal, blendModels, targetCount, generationToken);
        }

        private async void SpawnModelBlendAsync(Transform pedestal, List<ModelEntry> modelEntries, int targetCount, int generationToken)
        {
            GameObject blendRoot = null;
            try
            {
                if (pedestal == null || modelEntries == null || modelEntries.Count == 0 || targetCount <= 0)
                {
                    return;
                }

                if (!IsSpawnRequestCurrent(pedestal, generationToken))
                {
                    Debug.Log("GalleryManager: Ignoring stale spawn request before assembly start.");
                    return;
                }

                blendRoot = new GameObject("SculptureBlend");
                blendRoot.transform.SetParent(pedestal);
                blendRoot.transform.localPosition = Vector3.zero;
                blendRoot.transform.localRotation = Quaternion.identity;

                var placements = ComputeTreePlacements(targetCount);
                int minimumRequiredCount = Mathf.CeilToInt(targetCount * Mathf.Clamp01(_requiredFillRatio));
                int maxLoadAttempts = Mathf.Max(targetCount * 6, _maxBlendAssemblyAttempts);
                int spawnedParts = 0;
                int loadAttempts = 0;
                int sourceIndex = 0;
                int failedParts = 0;
                var pickedIds = new HashSet<string>();
                var placedParts = new List<PlacedPartData>(targetCount);

                while (spawnedParts < targetCount && loadAttempts < maxLoadAttempts)
                {
                    ModelEntry modelEntry = null;
                    if (sourceIndex < modelEntries.Count)
                    {
                        modelEntry = modelEntries[sourceIndex++];
                    }
                    else
                    {
                        bool allowDuplicates = _allowDuplicatePartFill || pickedIds.Count >= _eligibleModelEntries.Count;
                        if (!TryGetReplacementModel(pickedIds, allowDuplicates, out modelEntry))
                            break;
                    }

                    if (modelEntry == null)
                        break;

                    loadAttempts++;
                    GameObject part = await _spawner.LoadModel(
                        modelEntry.GlbPath,
                        blendRoot.transform,
                        addSculptureController: false,
                        addCollider: false
                    );

                    if (!IsSpawnRequestCurrent(pedestal, generationToken))
                    {
                        Debug.Log("GalleryManager: Aborted stale spawn request during async assembly.");
                        if (blendRoot != null)
                            Destroy(blendRoot);
                        return;
                    }

                    if (part == null)
                    {
                        failedParts++;
                        MarkModelUnavailable(modelEntry.Id);
                        continue;
                    }

                    var placement = placements[Mathf.Min(spawnedParts, placements.Count - 1)];
                    part.transform.localPosition = placement.position;
                    part.transform.localRotation = placement.rotation;
                    part.transform.localScale *= placement.scale;

                    if (_enablePartShadowOnTrunk)
                    {
                        var renderers = part.GetComponentsInChildren<Renderer>(true);
                        bool isTrunkPart = spawnedParts < _trunkCount;
                        for (int r = 0; r < renderers.Length; r++)
                        {
                            renderers[r].shadowCastingMode = isTrunkPart ? ShadowCastingMode.On : ShadowCastingMode.Off;
                        }
                    }

                    float partRadius = EstimatePartRadius(part);
                    SnapPartToAnchor(ref part, placement, placedParts, partRadius);

                    var growthPart = part.AddComponent<GrowthPart>();
                    growthPart.Configure(placement.revealThreshold, placement.emergenceDirection);

                    placedParts.Add(new PlacedPartData
                    {
                        localPosition = part.transform.localPosition,
                        radius = partRadius
                    });

                    pickedIds.Add(modelEntry.Id);
                    spawnedParts++;
                }

                // Imported GLBs often include mesh colliders on children; disable them so only the
                // blend-root collider is used (non-blocking trigger for gaze/touch).
                DisableCollidersOnDescendants(blendRoot.transform);

                if (spawnedParts == 0 || spawnedParts < minimumRequiredCount)
                {
                    Destroy(blendRoot);
                    Debug.LogWarning($"GalleryManager: Discarded underfilled sculpture at {pedestal.name}. target={targetCount}, loaded={spawnedParts}, required={minimumRequiredCount}, attempts={loadAttempts}, failed={failedParts}");
                    _nextSpawnRetryTime = Time.time + Mathf.Max(0.1f, _spawnRetryIntervalSecs);
                    return;
                }

                TightenLooseParts(blendRoot.transform, placedParts);

                if (_globalSculptureScale > 0.01f)
                {
                    blendRoot.transform.localScale *= _globalSculptureScale;
                }

                if (!TryGetCombinedRendererBounds(blendRoot, out Bounds bounds))
                {
                    Destroy(blendRoot);
                    Debug.LogWarning("GalleryManager: Spawned blend has no renderers.");
                    return;
                }

                ClampSculptureBounds(blendRoot, ref bounds);

                GroundBlendToSurface(blendRoot, ref bounds);
                TryGetCombinedRendererBounds(blendRoot, out bounds);

                var collider = blendRoot.AddComponent<BoxCollider>();
                collider.center = bounds.center - blendRoot.transform.position;
                collider.size = bounds.size;
                collider.isTrigger = true;

                var rigidbody = blendRoot.AddComponent<Rigidbody>();
                rigidbody.isKinematic = true;
                rigidbody.useGravity = false;

                SculptureController controller = blendRoot.AddComponent<SculptureController>();
                controller.ModelId = modelEntries[0].Id;
                controller.RefreshGrowthParts();

                if (!IsSpawnRequestCurrent(pedestal, generationToken))
                {
                    Debug.Log("GalleryManager: Dropping stale sculpture spawn after assembly completed.");
                    Destroy(blendRoot);
                    return;
                }

                _activeStatues[pedestal] = controller;
                _activeModels[pedestal] = modelEntries[0];
                _sculpturesViewed++;

                Debug.Log($"GalleryManager: Spawned blend at {pedestal.name} target={targetCount}, attempted={loadAttempts}, loaded={spawnedParts}, failed={failedParts}, finalized=true");
            }
            finally
            {
                _pendingSpawns.Remove(pedestal);
            }
        }

        /// <summary>
        /// Called by GazeManager when a sculpture's gaze is lost.
        /// Reports the gaze event to the recommendation engine.
        /// </summary>
        public void OnSculptureGazeExit(SculptureController sculpture)
        {
            if (sculpture == null)
                return;

            float dwellMs = sculpture.TotalDwellMs;
            float elapsedSessionSecs = Time.time - _sessionStartTime;

            _engine.ReportGaze(sculpture.ModelId, dwellMs, elapsedSessionSecs);
            _dwellTimes.Add(dwellMs / 1000f);

            Debug.Log($"GalleryManager: Gaze exit on {sculpture.ModelId}, dwell={dwellMs}ms");
        }

        // Debug properties
        public ArcPhase CurrentPhase => _engine.CurrentPhase;
        public float PhaseProgress => _engine.PhaseProgress;
        public UserProfile CurrentProfile => _engine.Profile;
        public int SculpturesViewed => _sculpturesViewed;
        public float AverageDwellTime => _averageDwellTime;
        public float SessionElapsedTime => Time.time - _sessionStartTime;

        private bool TryGetNextLoadableModel(out ModelEntry modelEntry)
        {
            const int maxAttempts = 150;
            if (_eligibleModelIds.Count == 0)
            {
                modelEntry = null;
                return false;
            }

            for (int i = 0; i < maxAttempts; i++)
            {
                var candidate = _engine.GetNext();
                if (candidate == null)
                    break;

                if (_unavailableModelIds.Contains(candidate.Id) || !_eligibleModelIds.Contains(candidate.Id))
                    continue;

                modelEntry = candidate;
                return true;
            }

            if (_eligibleModelEntries.Count > 0)
            {
                int fallbackAttempts = Mathf.Max(60, _eligibleModelEntries.Count * 2);
                for (int i = 0; i < fallbackAttempts; i++)
                {
                    int idx = Random.Range(0, _eligibleModelEntries.Count);
                    var randomCandidate = _eligibleModelEntries[idx];
                    if (randomCandidate == null || _unavailableModelIds.Contains(randomCandidate.Id))
                        continue;

                    modelEntry = randomCandidate;
                    return true;
                }
            }

            modelEntry = null;
            return false;
        }

        private bool TryGetBlendModels(out List<ModelEntry> selectedModels, out int targetCount)
        {
            selectedModels = new List<ModelEntry>();
            targetCount = GetTargetPartCount();
            if (targetCount <= 0 || _eligibleModelEntries.Count == 0)
                return false;

            int attemptLimit = Mathf.Max(targetCount * 12, _maxBlendAssemblyAttempts);
            var selectedIds = new HashSet<string>();

            for (int i = 0; i < attemptLimit && selectedModels.Count < targetCount; i++)
            {
                if (!TryGetNextLoadableModel(out ModelEntry candidate))
                    break;
                if (candidate == null)
                    continue;
                if (!selectedIds.Add(candidate.Id))
                    continue;

                selectedModels.Add(candidate);

                if (_reserveSelectedPartsAcrossPedestals)
                {
                    // Optional reserve mode for stronger no-repeat behavior.
                    _engine.Profile.ModelsShown.Add(candidate.Id);
                }
            }

            bool allowDuplicates = _allowDuplicatePartFill || selectedIds.Count >= _eligibleModelEntries.Count;
            int duplicateGuard = Mathf.Max(targetCount * 20, 200);
            while (selectedModels.Count < targetCount)
            {
                duplicateGuard--;
                if (duplicateGuard <= 0)
                    break;

                if (!TryGetReplacementModel(selectedIds, allowDuplicates, out var fallback))
                    break;
                selectedModels.Add(fallback);
            }

            if (selectedModels.Count < targetCount && _relaxFiltersOnUnderfill && _fallbackModelEntries.Count > 0)
            {
                int fallbackGuard = Mathf.Max(targetCount * 30, 300);
                while (selectedModels.Count < targetCount)
                {
                    fallbackGuard--;
                    if (fallbackGuard <= 0)
                        break;

                    int idx = Random.Range(0, _fallbackModelEntries.Count);
                    var candidate = _fallbackModelEntries[idx];
                    if (candidate == null || _unavailableModelIds.Contains(candidate.Id))
                        continue;

                    if (!allowDuplicates && !selectedIds.Add(candidate.Id))
                        continue;

                    selectedModels.Add(candidate);
                }
            }

            return selectedModels.Count > 0;
        }

        private int GetTargetPartCount()
        {
            int hardCap = Mathf.Max(1, _modelsPerSculptureHardCap);
            int desired = Mathf.Clamp(_modelsPerSculptureTarget, 1, hardCap);
            if (_allowDuplicatePartFill)
                return desired;

            return Mathf.Min(desired, _eligibleModelEntries.Count);
        }

        private bool TryGetReplacementModel(HashSet<string> selectedIds, bool allowDuplicates, out ModelEntry model)
        {
            model = null;
            if (_eligibleModelEntries.Count == 0)
                return false;

            int attempts = Mathf.Max(60, _eligibleModelEntries.Count * 2);
            for (int i = 0; i < attempts; i++)
            {
                int idx = Random.Range(0, _eligibleModelEntries.Count);
                var candidate = _eligibleModelEntries[idx];
                if (candidate == null || _unavailableModelIds.Contains(candidate.Id))
                    continue;
                if (!allowDuplicates && !selectedIds.Add(candidate.Id))
                    continue;

                model = candidate;
                return true;
            }

            if (_fallbackModelEntries.Count == 0)
                return false;

            attempts = Mathf.Max(60, _fallbackModelEntries.Count * 2);
            for (int i = 0; i < attempts; i++)
            {
                int idx = Random.Range(0, _fallbackModelEntries.Count);
                var candidate = _fallbackModelEntries[idx];
                if (candidate == null || _unavailableModelIds.Contains(candidate.Id))
                    continue;
                if (!allowDuplicates && !selectedIds.Add(candidate.Id))
                    continue;

                model = candidate;
                return true;
            }

            return false;
        }

        private bool IsAllowedGame(string game)
        {
            if (!_useAllowedGameFilter)
                return true;

            if (_allowedGames == null || _allowedGames.Length == 0)
                return true;

            return _allowedGames.Any(g => !string.IsNullOrWhiteSpace(g) &&
                                          string.Equals(g.Trim(), game, System.StringComparison.OrdinalIgnoreCase));
        }

        private static bool ModelFileExists(string glbRelativePath)
        {
            if (string.IsNullOrWhiteSpace(glbRelativePath))
                return false;

            string fullPath = Path.Combine(Application.streamingAssetsPath, "models", glbRelativePath);
            return File.Exists(fullPath);
        }

        private void MarkModelUnavailable(string modelId)
        {
            if (string.IsNullOrWhiteSpace(modelId))
                return;

            if (_unavailableModelIds.Add(modelId))
                _engine.Profile.ModelsShown.Add(modelId);
        }

        private void PreparePedestalForModel(Transform pedestal)
        {
            if (!_clearPlaceholderPedestalsOnFirstSpawn || pedestal == null)
                return;

            if (_preparedPedestals.Contains(pedestal))
                return;

            // Remove visuals/colliders from the slot object itself.
            RemovePlaceholderComponentsOnObject(pedestal.gameObject);

            // Remove any pre-existing placeholder children under the slot transform.
            var childrenToRemove = new List<GameObject>();
            for (int i = 0; i < pedestal.childCount; i++)
            {
                childrenToRemove.Add(pedestal.GetChild(i).gameObject);
            }
            foreach (var child in childrenToRemove)
            {
                Destroy(child);
            }

            _preparedPedestals.Add(pedestal);
        }

        private void SpawnForCurrentPedestals()
        {
            if (_pedestalSlots == null || _pedestalSlots.Length == 0)
                return;

            foreach (Transform pedestal in _pedestalSlots)
            {
                SpawnAtPedestal(pedestal);
            }
        }

        private void ClearPedestalRuntimeState()
        {
            _pedestalSlotGeneration++;

            if (_activeStatues.Count > 0)
            {
                foreach (var kvp in _activeStatues)
                {
                    var active = kvp.Value;
                    if (active != null)
                    {
                        Destroy(active.gameObject);
                    }
                }
            }

            _activeStatues.Clear();
            _activeModels.Clear();
            _pendingSpawns.Clear();
            _preparedPedestals.Clear();
        }

        private bool IsSpawnRequestCurrent(Transform pedestal, int generationToken)
        {
            if (pedestal == null)
                return false;
            if (generationToken != _pedestalSlotGeneration)
                return false;
            if (_pedestalSlots == null || _pedestalSlots.Length == 0)
                return false;

            for (int i = 0; i < _pedestalSlots.Length; i++)
            {
                if (_pedestalSlots[i] == pedestal)
                    return true;
            }

            return false;
        }

        private static Transform[] NormalizePedestalSlots(Transform[] pedestalSlots)
        {
            if (pedestalSlots == null || pedestalSlots.Length == 0)
                return System.Array.Empty<Transform>();

            var seen = new HashSet<Transform>();
            var normalized = new List<Transform>(pedestalSlots.Length);
            for (int i = 0; i < pedestalSlots.Length; i++)
            {
                Transform slot = pedestalSlots[i];
                if (slot == null || !seen.Add(slot))
                    continue;

                normalized.Add(slot);
            }

            return normalized.ToArray();
        }

        private static void RemovePlaceholderComponentsOnObject(GameObject go)
        {
            if (go == null)
                return;

            var renderers = go.GetComponents<Renderer>();
            for (int i = 0; i < renderers.Length; i++)
                renderers[i].enabled = false;

            var colliders = go.GetComponents<Collider>();
            for (int i = 0; i < colliders.Length; i++)
                colliders[i].enabled = false;
        }

        private static void DisableCollidersOnDescendants(Transform root)
        {
            if (root == null)
                return;

            for (int i = 0; i < root.childCount; i++)
            {
                foreach (var col in root.GetChild(i).GetComponentsInChildren<Collider>(true))
                    col.enabled = false;
            }
        }

        private struct PartPlacement
        {
            public Vector3 position;
            public Quaternion rotation;
            public float scale;
            public float revealThreshold;
            public Vector3 emergenceDirection;
            public int anchorPartIndex;
        }

        private struct Tendril
        {
            public Vector3 tip;
            public Vector3 direction;
            public int depth;
            public int chainLength;
            public float revealBase;
            public int anchorPartIndex;
        }

        private List<PartPlacement> ComputeTreePlacements(int total)
        {
            var placements = new List<PartPlacement>(total);
            if (total <= 0)
                return placements;

            int trunkEnd = Mathf.Clamp(Mathf.Min(_trunkCount, total), 1, total);
            int tendrilTotal = total - trunkEnd;
            float safeTrunkHeight = Mathf.Max(0.4f, _trunkHeight);
            float spread = Mathf.Max(0.2f, _placementSpreadMultiplier);
            const float goldenAngle = 2.39996323f;

            var tendrilAnchors = new List<Vector3>();

            // Trunk: rooted core that grows first.
            for (int i = 0; i < trunkEnd; i++)
            {
                float t = (i + 0.5f) / trunkEnd;
                float y = t * safeTrunkHeight;
                float angle = i * goldenAngle;
                float r = _trunkRadius * (0.5f + 0.5f * t);
                var pos = new Vector3(Mathf.Cos(angle) * r, y, Mathf.Sin(angle) * r);
                Vector3 radial = new Vector3(pos.x, 0f, pos.z).normalized;
                if (radial.sqrMagnitude < 0.0001f)
                    radial = Quaternion.Euler(0f, i * (360f / trunkEnd), 0f) * Vector3.forward;
                Vector3 trunkDir = Vector3.Slerp(Vector3.up, radial, 0.25f).normalized;

                float scaleMult = Random.Range(_trunkPartScaleRange.x, _trunkPartScaleRange.y);
                Vector3 emergenceDirection = (Vector3.zero - pos) + (Vector3.down * 0.25f);
                if (emergenceDirection.sqrMagnitude < 0.0001f)
                    emergenceDirection = Vector3.down;

                placements.Add(new PartPlacement
                {
                    position = pos * spread,
                    rotation = Quaternion.LookRotation(trunkDir, Vector3.up) * Quaternion.Euler(Random.Range(-8f, 8f), Random.Range(-20f, 20f), Random.Range(-8f, 8f)),
                    scale = scaleMult,
                    revealThreshold = Mathf.Lerp(0.02f, 0.14f, t),
                    emergenceDirection = emergenceDirection.normalized,
                    anchorPartIndex = i > 0 ? i - 1 : -1
                });

                if (t > 0.45f)
                    tendrilAnchors.Add(pos + (Vector3.up * Random.Range(0.03f, 0.16f)));
            }

            if (tendrilTotal <= 0)
                return placements;

            if (tendrilAnchors.Count == 0)
            {
                tendrilAnchors.Add(new Vector3(0f, safeTrunkHeight * 0.8f, 0f));
            }

            int primaryCount = Mathf.Clamp(_tendrilCount, 1, Mathf.Max(1, tendrilTotal));
            float safeStepSize = Mathf.Max(0.06f, _tendrilStepSize);
            float safeCurvature = Mathf.Clamp(_tendrilCurvature, 0f, 70f);
            int safeForkDepth = Mathf.Clamp(_maxForkDepth, 0, 6);
            float safeMaxHeight = Mathf.Max(safeTrunkHeight + 0.4f, _tendrilMaxHeight);
            float thresholdStep = 0.8f / Mathf.Max(1, tendrilTotal);

            var tendrils = new List<Tendril>(primaryCount * 2);
            for (int i = 0; i < primaryCount; i++)
            {
                Vector3 anchor = tendrilAnchors[i % tendrilAnchors.Count];
                float angle = ((i + 1) * goldenAngle) + Random.Range(-0.35f, 0.35f);
                Vector3 outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                Vector3 direction = (outward * (1.45f + _tendrilOutwardBias)) + (Vector3.up * (0.4f + _tendrilUpwardBias));
                if (direction.sqrMagnitude < 0.0001f)
                    direction = Vector3.up;

                tendrils.Add(new Tendril
                {
                    tip = anchor,
                    direction = direction.normalized,
                    depth = 0,
                    chainLength = 0,
                    revealBase = 0.18f + Random.Range(-0.02f, 0.02f),
                    anchorPartIndex = Mathf.Max(0, trunkEnd - 1)
                });
            }

            int tendrilPlaced = 0;
            int guard = 0;
            int guardLimit = Mathf.Max(500, tendrilTotal * 30);

            while (tendrilPlaced < tendrilTotal && tendrils.Count > 0 && guard < guardLimit)
            {
                guard++;
                int tendrilIdx = GetShortestTendrilIndex(tendrils);
                Tendril tendril = tendrils[tendrilIdx];

                float depthScale = Mathf.Lerp(1f, 0.58f, tendril.depth / (float)Mathf.Max(1, safeForkDepth));
                float stepSize = safeStepSize * depthScale * Random.Range(0.78f, 1.34f);
                Vector3 nextPos = tendril.tip + (tendril.direction * stepSize);
                nextPos.y = Mathf.Clamp(nextPos.y, 0.03f, safeMaxHeight);

                float chainReveal = tendril.revealBase + (tendril.chainLength * thresholdStep);
                chainReveal += tendril.depth * 0.05f;
                float revealThreshold = Mathf.Clamp(chainReveal, 0.18f, 0.98f);
                Vector3 emergenceDirection = -tendril.direction;
                if (emergenceDirection.sqrMagnitude < 0.0001f)
                    emergenceDirection = Vector3.down;

                placements.Add(new PartPlacement
                {
                    position = nextPos * spread,
                    rotation = Quaternion.LookRotation(tendril.direction, Vector3.up) * Quaternion.Euler(0f, Random.Range(-20f, 20f), 0f),
                    scale = Random.Range(_partScaleRange.x, _partScaleRange.y),
                    revealThreshold = revealThreshold,
                    emergenceDirection = emergenceDirection.normalized,
                    anchorPartIndex = tendril.anchorPartIndex
                });
                int addedPlacementIndex = placements.Count - 1;
                tendrilPlaced++;

                Quaternion bend = Quaternion.Euler(
                    Random.Range(-safeCurvature, safeCurvature),
                    Random.Range(-safeCurvature, safeCurvature),
                    Random.Range(-safeCurvature * 0.4f, safeCurvature * 0.4f)
                );
                Vector3 bentDirection = bend * tendril.direction;
                Vector3 radial = new Vector3(nextPos.x, 0f, nextPos.z);
                Vector3 outwardBias = radial.sqrMagnitude > 0.0001f
                    ? radial.normalized * (0.35f + _tendrilOutwardBias)
                    : Vector3.zero;
                Vector3 upBias = Vector3.up * (0.08f + _tendrilUpwardBias);
                Vector3 nextDirection = (bentDirection + outwardBias + upBias).normalized;
                if (nextDirection.sqrMagnitude < 0.0001f)
                    nextDirection = tendril.direction;

                tendril.tip = nextPos;
                tendril.direction = nextDirection;
                tendril.chainLength++;
                tendril.anchorPartIndex = addedPlacementIndex;
                tendrils[tendrilIdx] = tendril;

                bool canFork = tendril.depth < safeForkDepth && tendrils.Count < 72;
                if (canFork && tendrilPlaced < tendrilTotal && Random.value < _forkChance)
                {
                    Vector3 forkAxis = Vector3.Cross(tendril.direction, Vector3.up);
                    if (forkAxis.sqrMagnitude < 0.0001f)
                        forkAxis = Vector3.right;
                    forkAxis.Normalize();

                    Quaternion forkYaw = Quaternion.AngleAxis(Random.Range(35f, 85f) * (Random.value < 0.5f ? -1f : 1f), Vector3.up);
                    Quaternion forkPitch = Quaternion.AngleAxis(Random.Range(-18f, 26f), forkAxis);
                    Vector3 forkDir = (forkPitch * forkYaw) * tendril.direction;
                    if (forkDir.sqrMagnitude < 0.0001f)
                        forkDir = tendril.direction;

                    tendrils.Add(new Tendril
                    {
                        tip = nextPos,
                        direction = forkDir.normalized,
                        depth = tendril.depth + 1,
                        chainLength = 0,
                        revealBase = Mathf.Clamp01(tendril.revealBase + 0.08f + Random.Range(-0.02f, 0.03f)),
                        anchorPartIndex = addedPlacementIndex
                    });
                }
            }

            return placements;
        }

        private static int GetShortestTendrilIndex(List<Tendril> tendrils)
        {
            int bestIdx = 0;
            int bestLen = tendrils[0].chainLength;
            for (int i = 1; i < tendrils.Count; i++)
            {
                if (tendrils[i].chainLength < bestLen)
                {
                    bestLen = tendrils[i].chainLength;
                    bestIdx = i;
                }
            }

            return bestIdx;
        }

        private float EstimatePartRadius(GameObject part)
        {
            if (part == null)
                return 0.2f;

            if (!TryGetCombinedRendererBounds(part, out var bounds))
                return 0.2f;

            return Mathf.Max(0.08f, bounds.extents.magnitude);
        }

        private void SnapPartToAnchor(ref GameObject part, PartPlacement placement, List<PlacedPartData> placedParts, float partRadius)
        {
            if (part == null || placement.anchorPartIndex < 0 || placement.anchorPartIndex >= placedParts.Count)
                return;

            var anchor = placedParts[placement.anchorPartIndex];
            Vector3 anchorPos = anchor.localPosition;
            Vector3 currentPos = part.transform.localPosition;
            Vector3 direction = currentPos - anchorPos;
            if (direction.sqrMagnitude < 0.0001f)
                direction = placement.emergenceDirection.sqrMagnitude > 0.0001f ? placement.emergenceDirection.normalized : Vector3.up;
            else
                direction.Normalize();

            float desiredDistance = (anchor.radius + partRadius) * (1f - Mathf.Clamp(_targetPartOverlapRatio, 0.05f, 0.6f));
            desiredDistance = Mathf.Max(0.05f, desiredDistance);
            part.transform.localPosition = anchorPos + (direction * desiredDistance);
        }

        private void TightenLooseParts(Transform root, List<PlacedPartData> placedParts)
        {
            if (root == null || placedParts == null || placedParts.Count < 2 || _tighteningPassStrength <= 0f)
                return;

            float strength = Mathf.Clamp01(_tighteningPassStrength);
            for (int i = 0; i < root.childCount && i < placedParts.Count; i++)
            {
                Transform part = root.GetChild(i);
                Vector3 sourcePos = part.localPosition;
                float sourceRadius = placedParts[i].radius;

                int nearestIndex = -1;
                float nearestDistance = float.MaxValue;
                for (int j = 0; j < placedParts.Count; j++)
                {
                    if (i == j)
                        continue;

                    float distance = Vector3.Distance(sourcePos, placedParts[j].localPosition);
                    if (distance < nearestDistance)
                    {
                        nearestDistance = distance;
                        nearestIndex = j;
                    }
                }

                if (nearestIndex < 0)
                    continue;

                float desired = (sourceRadius + placedParts[nearestIndex].radius) * (1f - _targetPartOverlapRatio);
                if (nearestDistance <= desired)
                    continue;

                Vector3 toNearest = (placedParts[nearestIndex].localPosition - sourcePos).normalized;
                float move = (nearestDistance - desired) * strength;
                Vector3 updated = sourcePos + (toNearest * move);
                part.localPosition = updated;
                placedParts[i] = new PlacedPartData { localPosition = updated, radius = sourceRadius };
            }
        }

        private void GroundBlendToSurface(GameObject blendRoot, ref Bounds bounds)
        {
            if (blendRoot == null)
                return;

            // Hard-ground to pedestal height so sculpture bottoms always sit on slot Y (never on ceilings).
            float targetBaseY = blendRoot.transform.parent != null
                ? blendRoot.transform.parent.position.y
                : blendRoot.transform.position.y;
            float bottomOffset = bounds.min.y - targetBaseY;
            blendRoot.transform.position -= new Vector3(0f, bottomOffset, 0f);
        }

        private static bool TryGetCombinedRendererBounds(GameObject root, out Bounds bounds)
        {
            var renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers == null || renderers.Length == 0)
            {
                bounds = default;
                return false;
            }

            bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
            return true;
        }

        private void ClampSculptureBounds(GameObject root, ref Bounds bounds)
        {
            if (root == null)
                return;

            float scaleMultiplier = 1f;
            float currentHeight = Mathf.Max(0.001f, bounds.size.y);
            float currentRadius = Mathf.Max(0.001f, Mathf.Max(bounds.extents.x, bounds.extents.z));

            if (_maxSculptureHeight > 0f && currentHeight > _maxSculptureHeight)
            {
                scaleMultiplier = Mathf.Min(scaleMultiplier, _maxSculptureHeight / currentHeight);
            }

            if (_maxSculptureRadius > 0f && currentRadius > _maxSculptureRadius)
            {
                scaleMultiplier = Mathf.Min(scaleMultiplier, _maxSculptureRadius / currentRadius);
            }

            if (scaleMultiplier >= 0.999f)
                return;

            root.transform.localScale *= Mathf.Max(0.05f, scaleMultiplier);
            TryGetCombinedRendererBounds(root, out bounds);
        }

        private void PrepareEligiblePool()
        {
            _eligibleModelIds.Clear();
            _eligibleModelEntries.Clear();
            _fallbackModelEntries.Clear();
            _eligibleTags.Clear();

            if (_metadataIndex == null)
                return;

            BuildEligiblePool(applyContentFilters: true, out ModelFilterStats stats);
            bool attemptedContentFiltering = _modelFilters != null && _modelFilters.Enabled;
            BuildFallbackPoolWithoutContentFilters();

            // Safety fallback: if metadata/content filters zero-out the pool, retry with only game + file checks.
            if (_eligibleModelIds.Count == 0 && attemptedContentFiltering)
            {
                Debug.LogWarning("GalleryManager: Content filters removed all models. Falling back to baseline game/file filtering.");
                BuildEligiblePool(applyContentFilters: false, out stats);
            }

            if ((_modelFilters != null && _modelFilters.LogDiagnostics) || _eligibleModelIds.Count == 0)
            {
                Debug.Log(
                    "GalleryManager: Filter diagnostics " +
                    $"accepted={stats.accepted}, " +
                    $"rejectedMissingId={stats.rejectedMissingId}, " +
                    $"rejectedByGame={stats.rejectedByGame}, " +
                    $"rejectedByMissingFile={stats.rejectedByMissingFile}, " +
                    $"rejectedByPath={stats.rejectedByPath}, " +
                    $"rejectedByName={stats.rejectedByName}, " +
                    $"rejectedByPoly={stats.rejectedByPoly}, " +
                    $"rejectedByDimensions={stats.rejectedByDimensions}, " +
                    $"totalRejected={stats.TotalRejected}"
                );
            }
        }

        private void BuildEligiblePool(bool applyContentFilters, out ModelFilterStats stats)
        {
            stats = default;
            _eligibleModelIds.Clear();
            _eligibleModelEntries.Clear();
            _eligibleTags.Clear();

            var tagSet = new HashSet<string>();
            bool shouldMarkIneligibleAsShown = _modelFilters == null || _modelFilters.MarkIneligibleAsShown;

            foreach (var model in _metadataIndex.AllModels)
            {
                var rejectReason = EvaluateModelEligibility(model, applyContentFilters);
                if (rejectReason == ModelFilterRejectReason.None)
                {
                    _eligibleModelIds.Add(model.Id);
                    _eligibleModelEntries.Add(model);
                    stats.accepted++;
                    foreach (var tag in model.FlatTags)
                    {
                        if (!string.IsNullOrWhiteSpace(tag))
                            tagSet.Add(tag);
                    }
                    continue;
                }

                TrackRejection(ref stats, rejectReason);

                bool isContentLevelReject =
                    rejectReason == ModelFilterRejectReason.PathRejected ||
                    rejectReason == ModelFilterRejectReason.NameRejected ||
                    rejectReason == ModelFilterRejectReason.PolyRejected;

                if (shouldMarkIneligibleAsShown && !string.IsNullOrWhiteSpace(model.Id) && !isContentLevelReject)
                {
                    // Remove ineligible models from recommendation candidates up front.
                    _engine.Profile.ModelsShown.Add(model.Id);
                }
            }

            _eligibleTags = tagSet.ToList();
        }

        private void BuildFallbackPoolWithoutContentFilters()
        {
            _fallbackModelEntries.Clear();
            if (_metadataIndex == null)
                return;

            foreach (var model in _metadataIndex.AllModels)
            {
                if (model == null || string.IsNullOrWhiteSpace(model.Id))
                    continue;
                if (!IsAllowedGame(model.Game))
                    continue;
                if (!ModelFileExists(model.GlbPath))
                    continue;

                _fallbackModelEntries.Add(model);
            }
        }

        private ModelFilterRejectReason EvaluateModelEligibility(ModelEntry model, bool applyContentFilters)
        {
            if (model == null || string.IsNullOrWhiteSpace(model.Id))
                return ModelFilterRejectReason.MissingId;

            if (!IsAllowedGame(model.Game))
                return ModelFilterRejectReason.GameNotAllowed;

            if (!ModelFileExists(model.GlbPath))
                return ModelFilterRejectReason.MissingFile;

            if (!applyContentFilters || _modelFilters == null || !_modelFilters.Enabled)
                return ModelFilterRejectReason.None;

            if (_modelFilters.IsPathRejected(model.Category, model.GlbPath))
                return ModelFilterRejectReason.PathRejected;

            if (_modelFilters.IsNameDenied(model.ObjectName))
                return ModelFilterRejectReason.NameRejected;

            if (_modelFilters.IsPolyRejected(model.PolyCount))
                return ModelFilterRejectReason.PolyRejected;

            if (_modelFilters.IsDimensionsRejected(model.Dimensions, model.Tags.Scale))
                return ModelFilterRejectReason.DimensionsRejected;

            return ModelFilterRejectReason.None;
        }

        private static void TrackRejection(ref ModelFilterStats stats, ModelFilterRejectReason reason)
        {
            switch (reason)
            {
                case ModelFilterRejectReason.MissingId:
                    stats.rejectedMissingId++;
                    break;
                case ModelFilterRejectReason.GameNotAllowed:
                    stats.rejectedByGame++;
                    break;
                case ModelFilterRejectReason.MissingFile:
                    stats.rejectedByMissingFile++;
                    break;
                case ModelFilterRejectReason.PathRejected:
                    stats.rejectedByPath++;
                    break;
                case ModelFilterRejectReason.NameRejected:
                    stats.rejectedByName++;
                    break;
                case ModelFilterRejectReason.PolyRejected:
                    stats.rejectedByPoly++;
                    break;
                case ModelFilterRejectReason.DimensionsRejected:
                    stats.rejectedByDimensions++;
                    break;
            }
        }

        private void BootstrapInitialPreferences()
        {
            if (!_randomizeInitialPreferences || _eligibleTags.Count == 0)
                return;

            int targetCount = Mathf.Clamp(_initialPreferenceTagCount, 1, _eligibleTags.Count);
            float minWeight = Mathf.Min(_initialPreferenceRange.x, _initialPreferenceRange.y);
            float maxWeight = Mathf.Max(_initialPreferenceRange.x, _initialPreferenceRange.y);

            var rng = new System.Random();
            for (int i = 0; i < targetCount; i++)
            {
                int idx = rng.Next(_eligibleTags.Count);
                string tag = _eligibleTags[idx];
                float random01 = (float)rng.NextDouble();
                float weight = Mathf.Lerp(minWeight, maxWeight, random01);
                _engine.Profile.PreferenceWeights[tag] = weight;
                _eligibleTags.RemoveAt(idx);
            }
        }

        private void LogLoadableSummary()
        {
            if (_metadataIndex == null)
                return;

            int allowedModels = 0;
            int loadableModels = 0;
            string firstMissingPath = null;

            foreach (var model in _metadataIndex.AllModels)
            {
                if (!IsAllowedGame(model.Game))
                    continue;

                allowedModels++;
                if (ModelFileExists(model.GlbPath))
                {
                    loadableModels++;
                }
                else if (firstMissingPath == null)
                {
                    firstMissingPath = model.GlbPath;
                }
            }

            string filterStatus = _useAllowedGameFilter
                ? $"allowedGames=[{string.Join(", ", _allowedGames ?? new string[0])}]"
                : "allowedGames=[ALL]";
            Debug.Log($"GalleryManager: Allowed-models={allowedModels}, loadable-on-disk={loadableModels}, eligible-for-recommender={_eligibleModelIds.Count}, {filterStatus}");
            if (loadableModels == 0 && !string.IsNullOrWhiteSpace(firstMissingPath))
            {
                Debug.LogWarning($"GalleryManager: Example missing model path relative to StreamingAssets/models: {firstMissingPath}");
            }
        }
    }
}

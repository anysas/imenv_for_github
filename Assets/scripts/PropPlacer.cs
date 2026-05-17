using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using UnityEngine;
using System;
using System.Collections;
using AlgorithmicGallery;

namespace AlgorithmicGallery.Corruption
{
    // Handles player-driven prop placement.
    // Raycasts to sandbox floor, shows a ghost preview, places on left-click (click-removal disabled).
    // Also used by AssistantSystem for autonomous placement (set isPlayerControlled=false).
    [RequireComponent(typeof(SculptureSpawner))]
    public class PropPlacer : MonoBehaviour
    {
        public event Action<bool> OnPropPlaced; // bool: true if player placed, false if assistant placed
        /// <summary>Fired after a successful placement with context for scoring/UI (world position is above the placed model, for floaters).</summary>
        public event Action<bool, PropEntry, Vector3> OnPropPlacedWithContext;
        public event Action<bool> OnPropPlacementStarted; // bool: true if player placement started
        public event Action OnPropRemoved;
        /// <summary>Fired when the player clicks while the post-placement "thinking" cooldown is active (placement blocked).</summary>
        public event Action OnThinkingBlockedClick;

        [Header("Raycast")]
        [SerializeField] private LayerMask _placementLayerMask = ~0;
        [SerializeField] private float _maxPlacementDistance = 25f;
        [Tooltip("Ray hit normal must be mostly upward (pedestal top), not prop sides.")]
        [SerializeField] private float _minPedestalHitNormalY = 0.65f;

        [Header("Ghost Preview")]
        [SerializeField] private Material _ghostMaterial;
        [Tooltip("Ghost preview hovers this many units above the surface.")]
        [SerializeField] private float _ghostHoverHeight = 0.05f / 3f; // scaled with PropScaler sandbox shrink

        [Header("Placement")]
        [SerializeField] private Transform _sandboxRoot;
        [Tooltip("Global scale multiplier applied to placed props and ghost preview.")]
        [SerializeField] private float _globalPlacedScaleMultiplier = 1f;
        [Tooltip("If true, placement is constrained to sandbox root footprint (XZ).")]
        [SerializeField] private bool _restrictPlacementToSandboxFootprint = true;
        [Tooltip("Vertical offset above the detected floor surface to avoid z-fighting/intersection.")]
        [SerializeField] private float _floorSurfaceOffset = 0.01f / 3f; // scaled with PropScaler sandbox shrink
        [Tooltip("Extra padding added around prop footprints when testing click-placement overlap.")]
        [SerializeField] private float _placementBoundsPadding = 0.12f;
        [Tooltip("Fallback footprint half-size (metres) when the model is not loaded yet.")]
        [SerializeField] private float _defaultPlacementHalfExtent = 0.55f;
        [Tooltip("Max attempts to nudge a click position to clear footprint overlap before placement is rejected.")]
        [SerializeField] private int _antiOverlapAttempts = 10;

        [Header("Assistant Placement VFX")]
        [SerializeField] private bool _spawnAssistantPlacementVfx = true;
        [SerializeField] private float _assistantPlacementVfxDuration = 0.62f;
        [SerializeField] private int _assistantPlacementParticleCount = 34;
        [SerializeField] private Color _assistantPlacementVfxColor = new Color(0.42f, 0.95f, 1f, 1f);

        [Header("Assistant Placement Look")]
        [SerializeField] private float _assistantBrightnessMultiplier = 2f;
        [SerializeField] private float _assistantSmoothnessBoost = 1f;

        [Header("Player Placement VFX")]
        [SerializeField] private bool _spawnPlayerPlacementVfx = true;
        [SerializeField] private float _playerPlacementVfxDuration = 0.28f;
        [SerializeField] private int _playerPlacementParticleCount = 20;
        [SerializeField] private Color _playerPlacementVfxColor = new Color(1f, 0.62f, 0.16f, 1f);

        [Header("Player placement pacing")]
        [Tooltip("After each successful player placement, hotbar shows a loading state and placement is blocked for this many seconds.")]
        [SerializeField] private float _postPlacementThinkingSeconds = 2f;

        public bool IsPlayerControlled { get; set; } = true;
        public bool PlacementEnabled { get; set; } = false;
        /// <summary>When false, assistant/system cannot place via <see cref="PlaceAt"/>.</summary>
        public bool AllowSystemPlacement { get; private set; }
        /// <summary>When false, player placement only spawns the model (no hotbar cooldown, VFX, scoring, assistant notify).</summary>
        public bool PlacementSideEffects { get; private set; }
        public float GlobalPlacedScaleMultiplier => _globalPlacedScaleMultiplier;
        public float FloorSurfaceOffset => _floorSurfaceOffset;

        private SculptureSpawner _spawner;
        private HotbarController _hotbar;
        private StyleProfile _styleProfile;
        private SandboxManager _sandbox;
        private SandboxPropGridAligner _gridAligner;
        private AudioSource _placementAudioSource;

        private GameObject _ghostInstance;
        private PropEntry _ghostProp;
        private PropEntry _ghostLoadingProp;
        private Material _runtimeGhostMaterial;
        private Material _runtimeAssistantVfxMaterial;
        private Material _runtimePlayerVfxMaterial;
        private bool _isPlayerSpawning;
        private bool _isAssistantSpawning;
        private float _sessionTime;
        private float _playerPlacementYaw;
        private readonly List<GameObject> _overlapScratch = new();

        private static readonly int BaseColorPropertyId = Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColorPropertyId = Shader.PropertyToID("_Color");
        private static readonly int SmoothnessPropertyId = Shader.PropertyToID("_Smoothness");
        private static readonly int GlossinessPropertyId = Shader.PropertyToID("_Glossiness");

        public void Initialize(
            HotbarController hotbar,
            StyleProfile styleProfile,
            SandboxManager sandbox,
            bool placementSideEffects = true,
            bool allowSystemPlacement = false)
        {
            _hotbar = hotbar;
            _styleProfile = styleProfile;
            _sandbox = sandbox;
            PlacementSideEffects = placementSideEffects;
            AllowSystemPlacement = allowSystemPlacement;
            _spawner = GetComponent<SculptureSpawner>();
            if (_sandboxRoot == null && sandbox != null)
                _sandboxRoot = sandbox.SandboxFloor;

            // Grab a dedicated AudioSource for placement tones.
            // Reuse existing one if available; otherwise add one.
            _placementAudioSource = GetComponent<AudioSource>();
            if (_placementAudioSource == null)
                _placementAudioSource = gameObject.AddComponent<AudioSource>();
            _placementAudioSource.spatialBlend = 0f;
            _placementAudioSource.playOnAwake = false;
        }

        void Update()
        {
            _sessionTime += Time.deltaTime;

            if (!IsPlayerControlled || !PlacementEnabled)
            {
                DestroyGhost();
                return;
            }

            if (Input.GetKeyDown(KeyCode.Q))
                _playerPlacementYaw = Mathf.Repeat(_playerPlacementYaw - 45f, 360f);
            if (Input.GetKeyDown(KeyCode.E))
                _playerPlacementYaw = Mathf.Repeat(_playerPlacementYaw + 45f, 360f);

            UpdateGhostPreview();

            var es = UnityEngine.EventSystems.EventSystem.current;
            bool overUI = es != null && Cursor.lockState != CursorLockMode.Locked && es.IsPointerOverGameObject();

            // Left-click = place (click-removal disabled)
            if (Input.GetMouseButtonDown(0) && !overUI)
            {
                if (PlacementSideEffects && _hotbar != null && _hotbar.IsInPostPlacementThinking)
                {
                    OnThinkingBlockedClick?.Invoke();
                    GameplayEventDebugLog.Push("Hotbar", "placement blocked (thinking cooldown)");
                    return;
                }

                TryPlaceAtCursor();
            }
        }

        // Used by player and assistant placement paths.
        // isPlayerPlacement controls style recording, hotbar consumption, and callbacks.
        public async Task PlaceAt(PropEntry prop, Vector3 worldPosition, float yRotation = 0f, bool isPlayerPlacement = true)
        {
            if (!isPlayerPlacement && !AllowSystemPlacement)
                return;

            if (isPlayerPlacement)
            {
                if (_isPlayerSpawning) return;
                _isPlayerSpawning = true;
            }
            else
            {
                if (_isAssistantSpawning) return;
                _isAssistantSpawning = true;
            }

            if (PlacementSideEffects)
                OnPropPlacementStarted?.Invoke(isPlayerPlacement);

            float floorY = GetSandboxSurfaceY();
            worldPosition = ClampToSandboxFootprint(worldPosition);
            worldPosition.y = floorY + _floorSurfaceOffset;

            bool useImmediateGrid = isPlayerPlacement && ShouldUseImmediateGridForNextPlayerPlacement();
            // Ghost follows the cursor; grid assignment happens after spawn for immediate-grid phase.
            if (!useImmediateGrid && !TryResolveNonOverlappingPlacement(ref worldPosition, prop))
            {
                if (isPlayerPlacement)
                    _isPlayerSpawning = false;
                else
                    _isAssistantSpawning = false;
                return;
            }

            var go = await SpawnProp(prop, worldPosition, yRotation, isPlayerPlacement);

            if (useImmediateGrid && go != null && !EnforceImmediateGridPosition(go, prop))
            {
                Destroy(go);
                if (isPlayerPlacement)
                    _isPlayerSpawning = false;
                else
                    _isAssistantSpawning = false;
                return;
            }

            Vector3 placedPosition = go != null ? go.transform.position : worldPosition;
            Vector3 floaterAnchor = go != null ? GetWorldPointAbovePlacedModel(go, placedPosition) : worldPosition;

            if (go != null && isPlayerPlacement)
            {
                SandboxGameplaySfx.NotifyPlayerPlaced();

                if (PlacementSideEffects)
                {
                    _styleProfile.RecordPlacement(
                        go.transform.position,
                        prop,
                        _sessionTime,
                        isPlayer: true,
                        go.transform.rotation,
                        go.transform.localScale);
                    _hotbar?.BeginPostPlacementThinking(_postPlacementThinkingSeconds);
                    _sandbox?.NotifyPlayerPlaced(placedPosition);
                    OnPropPlacedWithContext?.Invoke(true, prop, floaterAnchor);
                    OnPropPlaced?.Invoke(true);
                    GameplayEventDebugLog.Push("Place", $"player placed \"{prop.DisplayName}\"");
                    SpawnPlayerPlacementVfx(go);
                }

                PropBudget.Instance?.Register(go, isPlayerPlaced: true);
            }
            else if (go != null)
            {
                _styleProfile.RecordPlacement(
                    go.transform.position,
                    prop,
                    _sessionTime,
                    isPlayer: false,
                    go.transform.rotation,
                    go.transform.localScale);
                PropBudget.Instance?.Register(go, isPlayerPlaced: false);
                OnPropPlacedWithContext?.Invoke(false, prop, floaterAnchor);
                OnPropPlaced?.Invoke(false);
                GameplayEventDebugLog.Push("Place", $"assistant placed \"{prop.DisplayName}\"");
                ApplyAssistantVisualTreatment(go);
                SpawnAssistantPlacementVfx(go);

                if (PlacementSideEffects)
                    _sandbox?.NotifyPlacementRecorded();
            }

            if (go != null && isPlayerPlacement && PlacementSideEffects)
                _sandbox?.NotifyPlacementRecorded();

            if (isPlayerPlacement)
                _isPlayerSpawning = false;
            else
                _isAssistantSpawning = false;
        }

        /// <summary>
        /// World-space point at the top center of the placed prop mesh/colliders so UI floaters sit above the model, not inside it.
        /// </summary>
        private static Vector3 GetWorldPointAbovePlacedModel(GameObject root, Vector3 floorFallback)
        {
            if (root == null) return floorFallback + Vector3.up * 0.35f;

            bool has = false;
            Bounds b = default;

            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                if (!has)
                {
                    b = r.bounds;
                    has = true;
                }
                else
                    b.Encapsulate(r.bounds);
            }

            if (!has)
            {
                foreach (var c in root.GetComponentsInChildren<Collider>(true))
                {
                    if (c == null || !c.enabled) continue;
                    if (!has)
                    {
                        b = c.bounds;
                        has = true;
                    }
                    else
                        b.Encapsulate(c.bounds);
                }
            }

            if (has)
                return new Vector3(b.center.x, b.max.y, b.center.z);

            float h = Mathf.Max(0.15f, root.transform.lossyScale.y * 0.5f);
            return root.transform.position + Vector3.up * h;
        }

        private void UpdateGhostPreview()
        {
            if (PlacementSideEffects && _hotbar != null && _hotbar.IsInPostPlacementThinking)
            {
                DestroyGhost();
                return;
            }

            PropEntry active = _hotbar?.ActiveProp;
            if (active == null)
            {
                DestroyGhost();
                return;
            }

            if (_ghostProp != active && _ghostLoadingProp != active)
            {
                _ghostLoadingProp = active;
                _ = LoadGhostAsync(active);
            }

            if (_ghostInstance == null)
                return;

            if (!TryRaycastPedestalTop(out Vector3 flatPoint))
            {
                _ghostInstance.SetActive(false);
                return;
            }

            if (!TryResolveNonOverlappingPlacement(ref flatPoint, active))
            {
                _ghostInstance.SetActive(false);
                return;
            }

            flatPoint.y += _ghostHoverHeight;

            _ghostInstance.SetActive(true);
            _ghostInstance.transform.position = flatPoint;
            _ghostInstance.transform.rotation = Quaternion.Euler(0f, _playerPlacementYaw, 0f);
        }

        private async Task LoadGhostAsync(PropEntry prop)
        {
            if (prop == null || _spawner == null)
            {
                _ghostLoadingProp = null;
                return;
            }

            float scaleMult = PropScaler.ComputeScaleFactor(prop, _globalPlacedScaleMultiplier);
            var loaded = await _spawner.LoadModel(
                prop.GlbPath,
                parent: transform,
                addSculptureController: false,
                addCollider: false,
                normalizeScale: true,
                scaleMultiplier: scaleMult);

            _ghostLoadingProp = null;

            if (loaded == null)
                return;

            if (_hotbar == null || _hotbar.ActiveProp != prop)
            {
                Destroy(loaded);
                return;
            }

            var ghostMat = _ghostMaterial != null ? _ghostMaterial : CreateFallbackGhostMaterial();
            ApplyGhostAppearance(loaded, ghostMat);

            if (_ghostInstance != null)
                Destroy(_ghostInstance);

            _ghostInstance = loaded;
            _ghostProp = prop;
            _ghostInstance.name = "_GhostPreview";
            _ghostInstance.SetActive(false);
        }

        private void ApplyGhostAppearance(GameObject root, Material ghostMat)
        {
            foreach (var col in root.GetComponentsInChildren<Collider>(true))
                col.enabled = false;

            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null)
                    continue;

                var shared = renderer.sharedMaterials;
                for (int i = 0; i < shared.Length; i++)
                    shared[i] = ghostMat;
                renderer.sharedMaterials = shared;
            }
        }

        private Material CreateFallbackGhostMaterial()
        {
            if (_runtimeGhostMaterial != null)
                return _runtimeGhostMaterial;

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");
            if (shader == null)
                shader = Shader.Find("Standard");

            if (shader == null)
                return null;

            _runtimeGhostMaterial = new Material(shader)
            {
                color = new Color(0.45f, 0.75f, 1f, 0.4f)
            };
            _runtimeGhostMaterial.name = "RuntimeGhostFallback";
            return _runtimeGhostMaterial;
        }

        private void TryPlaceAtCursor()
        {
            if (_isPlayerSpawning) return;
            if (PlacementSideEffects && _hotbar != null && _hotbar.IsInPostPlacementThinking) return;
            if (_hotbar?.ActiveProp == null) return;
            if (!TryRaycastPedestalTop(out Vector3 hit)) return;

            float yRot = _playerPlacementYaw;
            _ = PlaceAt(_hotbar.ActiveProp, hit, yRot, isPlayerPlacement: true);
        }

        private bool ShouldUseImmediateGridForNextPlayerPlacement()
        {
            if (_styleProfile == null)
                return false;

            if (_gridAligner == null)
                _gridAligner = FindFirstObjectByType<SandboxPropGridAligner>();

            int nextPlacement = _styleProfile.PlayerPlacementCount + 1;
            return _gridAligner != null && _gridAligner.UsesImmediateGridPlacement(nextPlacement);
        }

        private bool TryApplyRandomEmptyGridPlacement(
            ref Vector3 worldPosition,
            PropEntry prop,
            GameObject footprintSource = null)
        {
            if (_gridAligner == null)
                _gridAligner = FindFirstObjectByType<SandboxPropGridAligner>();

            if (_gridAligner == null)
                return false;

            return _gridAligner.TryAssignRandomGridSlot(footprintSource, prop, ref worldPosition);
        }

        private bool EnforceImmediateGridPosition(GameObject placed, PropEntry prop)
        {
            if (placed == null)
                return false;

            Vector3 from = placed.transform.position;
            Vector3 gridPos = from;
            float y = gridPos.y;
            if (!TryApplyRandomEmptyGridPlacement(ref gridPos, prop, placed))
                return false;

            gridPos.y = y;
            placed.transform.position = gridPos;
            SandboxGameplaySfx.NotifyPropMovedOnGrid(placed, from, gridPos);
            return true;
        }

        /// <summary>
        /// Keeps XZ inside footprint; Y locked to pedestal top. Returns false if overlap cannot be cleared.
        /// </summary>
        private bool TryResolveNonOverlappingPlacement(ref Vector3 worldPos, PropEntry placingProp)
        {
            float floorY = GetSandboxSurfaceY();
            worldPos = ClampToSandboxFootprint(worldPos);
            worldPos.y = floorY + _floorSurfaceOffset;

            GetPlacementHalfExtents(placingProp, _ghostInstance, out float halfX, out float halfZ);

            Vector3 candidate = worldPos;
            for (int attempt = 0; attempt < _antiOverlapAttempts; attempt++)
            {
                if (!HasPlacedPropBoundsOverlap(candidate, placingProp, _ghostInstance))
                {
                    worldPos = candidate;
                    return true;
                }

                float angle = attempt * 1.13f * Mathf.PI;
                float nudge = (Mathf.Max(halfX, halfZ) + _placementBoundsPadding) * (0.85f + 0.35f * attempt);
                candidate += new Vector3(Mathf.Cos(angle) * nudge, 0f, Mathf.Sin(angle) * nudge);
                candidate = ClampToSandboxFootprint(candidate);
                candidate.y = floorY + _floorSurfaceOffset;
            }

            return false;
        }

        private bool HasPlacedPropBoundsOverlap(Vector3 worldPos, PropEntry placingProp, GameObject placingInstance)
        {
            GetPlacementHalfExtents(placingProp, placingInstance, out float halfX, out float halfZ);

            _overlapScratch.Clear();
            if (PropBudget.Instance != null)
                PropBudget.Instance.GetTrackedPlacedProps(_overlapScratch);
            else if (_sandboxRoot != null)
            {
                for (int i = 0; i < _sandboxRoot.childCount; i++)
                    _overlapScratch.Add(_sandboxRoot.GetChild(i).gameObject);
            }

            for (int i = 0; i < _overlapScratch.Count; i++)
            {
                GameObject other = _overlapScratch[i];
                if (other == null || !other.activeInHierarchy || other == placingInstance)
                    continue;

                if (!TryGetObjectFootprintHalfExtents(other, out float otherHalfX, out float otherHalfZ))
                    continue;

                if (FootprintsOverlapXZ(worldPos, halfX, halfZ, other.transform.position, otherHalfX, otherHalfZ, _placementBoundsPadding))
                    return true;
            }

            return false;
        }

        private void GetPlacementHalfExtents(PropEntry prop, GameObject instance, out float halfX, out float halfZ)
        {
            halfX = _defaultPlacementHalfExtent;
            halfZ = _defaultPlacementHalfExtent;

            if (instance != null && TryGetObjectFootprintHalfExtents(instance, out float ix, out float iz))
            {
                halfX = ix;
                halfZ = iz;
                return;
            }

            if (prop == null)
                return;

            float longest = PropScaler.ScaledLongestAxis(prop, _globalPlacedScaleMultiplier);
            if (longest > 0.001f)
                halfX = halfZ = longest * 0.5f;
        }

        private static bool TryGetObjectFootprintHalfExtents(GameObject prop, out float halfX, out float halfZ)
        {
            halfX = 0.12f;
            halfZ = 0.12f;

            if (prop == null)
                return false;

            bool hasBounds = false;
            Bounds bounds = default;

            var renderers = prop.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !renderer.enabled)
                    continue;

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            if (!hasBounds)
                return false;

            halfX = Mathf.Max(halfX, bounds.extents.x);
            halfZ = Mathf.Max(halfZ, bounds.extents.z);
            return true;
        }

        private static bool FootprintsOverlapXZ(
            Vector3 centerA,
            float halfAX,
            float halfAZ,
            Vector3 centerB,
            float halfBX,
            float halfBZ,
            float padding)
        {
            return Mathf.Abs(centerA.x - centerB.x) < halfAX + halfBX + padding
                && Mathf.Abs(centerA.z - centerB.z) < halfAZ + halfBZ + padding;
        }

        private float GetSandboxSurfaceY()
        {
            if (_sandboxRoot == null)
                return 0f;

            if (TryGetSandboxBounds(out Bounds bounds))
                return bounds.max.y;

            return _sandboxRoot.position.y;
        }

        /// <summary>
        /// Picks the closest ray hit that is on the pedestal (upward-facing), not on stacked props, inside footprint.
        /// </summary>
        private bool TryRaycastPedestalTop(out Vector3 hitPoint)
        {
            hitPoint = Vector3.zero;
            var cam = Camera.main;
            if (cam == null) return false;

            Vector3 screenPos = Cursor.lockState == CursorLockMode.Locked
                ? new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f)
                : Input.mousePosition;

            Ray ray = cam.ScreenPointToRay(screenPos);
            int hitCount = Physics.RaycastNonAlloc(ray, RaycastScratch, _maxPlacementDistance, _placementLayerMask, QueryTriggerInteraction.Ignore);
            if (hitCount <= 0) return false;

            float floorY = GetSandboxSurfaceY();

            bool found = false;
            float bestDist = float.MaxValue;
            Vector3 bestFlat = Vector3.zero;

            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = RaycastScratch[i];
                if (hit.normal.y < _minPedestalHitNormalY)
                    continue;

                if (PropBudget.Instance != null && PropBudget.Instance.IsTrackedPlacedPropCollider(hit.collider))
                    continue;

                Vector3 point = hit.point;
                if (_restrictPlacementToSandboxFootprint && !IsWithinSandboxFootprint(point))
                    continue;

                if (hit.distance < bestDist)
                {
                    bestDist = hit.distance;
                    point.y = floorY + _floorSurfaceOffset;
                    bestFlat = point;
                    found = true;
                }
            }

            if (!found)
                return false;

            hitPoint = bestFlat;
            return true;
        }

        private static readonly RaycastHit[] RaycastScratch = new RaycastHit[64];

        private bool IsWithinSandboxFootprint(Vector3 worldPos)
        {
            if (!_restrictPlacementToSandboxFootprint || _sandboxRoot == null)
                return true;

            if (!TryGetSandboxBounds(out Bounds bounds))
                return true;

            return worldPos.x >= bounds.min.x && worldPos.x <= bounds.max.x
                && worldPos.z >= bounds.min.z && worldPos.z <= bounds.max.z;
        }

        private Vector3 ClampToSandboxFootprint(Vector3 worldPos)
        {
            if (!_restrictPlacementToSandboxFootprint || _sandboxRoot == null)
                return worldPos;

            if (!TryGetSandboxBounds(out Bounds bounds))
                return worldPos;

            worldPos.x = Mathf.Clamp(worldPos.x, bounds.min.x, bounds.max.x);
            worldPos.z = Mathf.Clamp(worldPos.z, bounds.min.z, bounds.max.z);
            return worldPos;
        }

        private bool TryGetSandboxBounds(out Bounds bounds)
        {
            bounds = default;
            if (_sandboxRoot == null)
                return false;

            var rootCollider = _sandboxRoot.GetComponent<Collider>();
            if (rootCollider != null)
            {
                bounds = rootCollider.bounds;
                return true;
            }

            var renderers = _sandboxRoot.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
                return false;

            bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
            return true;
        }

        private async Task<GameObject> SpawnProp(PropEntry prop, Vector3 position, float yRotation, bool isPlayerPlacement)
        {
            var parent = _sandboxRoot != null ? _sandboxRoot : transform;
            float scaleMult = PropScaler.ComputeScaleFactor(prop, _globalPlacedScaleMultiplier);
            var go = await _spawner.LoadModel(
                prop.GlbPath,
                parent: parent,
                addSculptureController: false,
                addCollider: true,
                normalizeScale: true,
                scaleMultiplier: scaleMult);

            if (go == null) return null;

            go.transform.position = position;
            go.transform.rotation = Quaternion.Euler(0f, yRotation, 0f);
            int placedLayer = isPlayerPlacement ? parent.gameObject.layer : SystemPropCleanOverlaySetup.SystemPropLayer;
            SetLayerRecursive(go, placedLayer);

            var metadata = go.GetComponent<PlacedPropMetadata>();
            if (metadata == null)
                metadata = go.AddComponent<PlacedPropMetadata>();
            metadata.GlbPath = prop.GlbPath;
            metadata.IsPlayerPlaced = isPlayerPlacement;

            return go;
        }

        private static void SetLayerRecursive(GameObject root, int layer)
        {
            if (root == null) return;
            root.layer = layer;
            foreach (Transform child in root.transform)
                SetLayerRecursive(child.gameObject, layer);
        }

        private void ApplyAssistantVisualTreatment(GameObject placedProp)
        {
            if (placedProp == null)
                return;

            float brightness = Mathf.Max(1f, _assistantBrightnessMultiplier);
            float smoothnessBoost = Mathf.Max(0f, _assistantSmoothnessBoost);
            var propertyBlock = new MaterialPropertyBlock();

            foreach (var renderer in placedProp.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null)
                    continue;

                Material referenceMaterial = renderer.sharedMaterial;
                if (referenceMaterial == null)
                    continue;

                renderer.GetPropertyBlock(propertyBlock);

                if (referenceMaterial.HasProperty(BaseColorPropertyId))
                {
                    Color baseColor = referenceMaterial.GetColor(BaseColorPropertyId);
                    propertyBlock.SetColor(BaseColorPropertyId, BoostBrightness(baseColor, brightness));
                }
                else if (referenceMaterial.HasProperty(LegacyColorPropertyId))
                {
                    Color baseColor = referenceMaterial.GetColor(LegacyColorPropertyId);
                    propertyBlock.SetColor(LegacyColorPropertyId, BoostBrightness(baseColor, brightness));
                }

                if (referenceMaterial.HasProperty(SmoothnessPropertyId))
                {
                    float smoothness = referenceMaterial.GetFloat(SmoothnessPropertyId);
                    propertyBlock.SetFloat(SmoothnessPropertyId, Mathf.Clamp01(smoothness + smoothnessBoost));
                }
                else if (referenceMaterial.HasProperty(GlossinessPropertyId))
                {
                    float smoothness = referenceMaterial.GetFloat(GlossinessPropertyId);
                    propertyBlock.SetFloat(GlossinessPropertyId, Mathf.Clamp01(smoothness + smoothnessBoost));
                }

                renderer.SetPropertyBlock(propertyBlock);
            }
        }

        private static Color BoostBrightness(Color color, float multiplier)
        {
            return new Color(
                Mathf.Clamp01(color.r * multiplier),
                Mathf.Clamp01(color.g * multiplier),
                Mathf.Clamp01(color.b * multiplier),
                color.a);
        }

        private void SpawnPlayerPlacementVfx(GameObject placedProp)
        {
            if (!_spawnPlayerPlacementVfx || placedProp == null)
                return;

            if (!TryGetCombinedRendererBounds(placedProp, out Bounds bounds))
                bounds = new Bounds(placedProp.transform.position, Vector3.one * 0.5f);

            float duration = Mathf.Max(0.08f, _playerPlacementVfxDuration);
            float radius = Mathf.Clamp(bounds.extents.magnitude * 0.45f, 0.16f, 0.85f);

            var fxGO = new GameObject("_PlayerPlacementBurstVfx");
            fxGO.transform.SetParent(placedProp.transform, worldPositionStays: true);
            fxGO.transform.position = bounds.center + Vector3.up * 0.05f;

            var ps = fxGO.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = duration;
            main.startLifetime = new ParticleSystem.MinMaxCurve(duration * 0.25f, duration * 0.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(radius * 1.5f, radius * 2.8f);
            main.startSize = new ParticleSystem.MinMaxCurve(radius * 0.08f, radius * 0.16f);
            main.startColor = _playerPlacementVfxColor;
            main.maxParticles = Mathf.Max(8, _playerPlacementParticleCount);
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ps.emission;
            emission.enabled = false;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 16f;
            shape.radius = radius * 0.08f;

            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(_playerPlacementVfxColor, 0f),
                    new GradientColorKey(new Color(1f, 0.9f, 0.5f, 1f), 1f),
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.08f),
                    new GradientAlphaKey(0.35f, 0.8f),
                    new GradientAlphaKey(0f, 1f),
                }
            );
            colorOverLifetime.color = gradient;

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = GetPlayerVfxMaterial();
            renderer.alignment = ParticleSystemRenderSpace.View;

            ps.Emit(Mathf.Max(8, _playerPlacementParticleCount));
            ps.Play();
            Destroy(fxGO, duration + 0.2f);
        }

        private void SpawnAssistantPlacementVfx(GameObject placedProp)
        {
            if (!_spawnAssistantPlacementVfx || placedProp == null)
                return;

            if (!TryGetCombinedRendererBounds(placedProp, out Bounds bounds))
            {
                bounds = new Bounds(placedProp.transform.position, Vector3.one * 0.6f);
            }

            float duration = Mathf.Max(0.08f, _assistantPlacementVfxDuration);
            float radius = Mathf.Clamp(bounds.extents.magnitude * 0.55f, 0.18f, 1.1f);

            var fxGO = new GameObject("_AssistantPlacementBurstVfx");
            fxGO.transform.SetParent(placedProp.transform, worldPositionStays: true);
            fxGO.transform.position = bounds.center;

            var ps = fxGO.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = duration;
            main.startLifetime = new ParticleSystem.MinMaxCurve(duration * 0.45f, duration * 0.95f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(radius * 1.2f, radius * 2.1f);
            main.startSize = new ParticleSystem.MinMaxCurve(radius * 0.09f, radius * 0.2f);
            main.startColor = _assistantPlacementVfxColor;
            main.maxParticles = Mathf.Max(8, _assistantPlacementParticleCount);
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ps.emission;
            emission.enabled = false;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = radius * 0.25f;

            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(_assistantPlacementVfxColor, 0f),
                    new GradientColorKey(_assistantPlacementVfxColor * 0.95f, 1f),
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.95f, 0.1f),
                    new GradientAlphaKey(0.45f, 0.7f),
                    new GradientAlphaKey(0f, 1f),
                }
            );
            colorOverLifetime.color = gradient;

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = GetAssistantVfxMaterial();
            renderer.alignment = ParticleSystemRenderSpace.View;

            var glow = fxGO.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.color = _assistantPlacementVfxColor;
            glow.intensity = 2.4f;
            glow.range = Mathf.Clamp(radius * 3.3f, 0.9f, 3.5f);
            glow.shadows = LightShadows.None;

            ps.Emit(Mathf.Max(8, _assistantPlacementParticleCount));
            ps.Play();
            StartCoroutine(FadeAndCleanupAssistantVfx(fxGO, glow, duration));
        }

        private IEnumerator FadeAndCleanupAssistantVfx(GameObject fxGO, Light glow, float duration)
        {
            float t = 0f;
            float startIntensity = glow != null ? glow.intensity : 0f;
            while (t < duration && fxGO != null)
            {
                t += Time.deltaTime;
                float k = 1f - Mathf.Clamp01(t / duration);
                if (glow != null)
                    glow.intensity = startIntensity * k;
                yield return null;
            }

            if (fxGO != null)
                Destroy(fxGO);
        }

        private Material GetAssistantVfxMaterial()
        {
            if (_runtimeAssistantVfxMaterial != null)
                return _runtimeAssistantVfxMaterial;

            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
                shader = Shader.Find("Particles/Standard Unlit");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");
            if (shader == null)
                return null;

            _runtimeAssistantVfxMaterial = new Material(shader)
            {
                color = _assistantPlacementVfxColor
            };
            _runtimeAssistantVfxMaterial.name = "RuntimeAssistantPlacementVfx";
            return _runtimeAssistantVfxMaterial;
        }

        private Material GetPlayerVfxMaterial()
        {
            if (_runtimePlayerVfxMaterial != null)
                return _runtimePlayerVfxMaterial;

            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
                shader = Shader.Find("Particles/Standard Unlit");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");
            if (shader == null)
                return null;

            _runtimePlayerVfxMaterial = new Material(shader)
            {
                color = _playerPlacementVfxColor
            };
            _runtimePlayerVfxMaterial.name = "RuntimePlayerPlacementVfx";
            return _runtimePlayerVfxMaterial;
        }

        private static bool TryGetCombinedRendererBounds(GameObject root, out Bounds bounds)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
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

        private void DestroyGhost()
        {
            if (_ghostInstance != null)
            {
                Destroy(_ghostInstance);
                _ghostInstance = null;
            }
            _ghostProp = null;
            _ghostLoadingProp = null;
        }

        void OnDestroy()
        {
            DestroyGhost();
            if (_runtimeGhostMaterial != null)
            {
                Destroy(_runtimeGhostMaterial);
                _runtimeGhostMaterial = null;
            }
            if (_runtimeAssistantVfxMaterial != null)
            {
                Destroy(_runtimeAssistantVfxMaterial);
                _runtimeAssistantVfxMaterial = null;
            }
            if (_runtimePlayerVfxMaterial != null)
            {
                Destroy(_runtimePlayerVfxMaterial);
                _runtimePlayerVfxMaterial = null;
            }
        }
    }
}

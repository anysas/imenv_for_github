using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace AlgorithmicGallery.Corruption
{
    /// <summary>
    /// Spawns vertical planes over the sandbox session to progressively block the player's view of placed props.
    /// Walls use a cubicle-style grid: rectilinear bays that slowly box in player-placed props.
    /// </summary>
    public class SandboxViewBlockerController : MonoBehaviour
    {
        [Header("Enable")]
        [Tooltip("When off, no view-blocking walls spawn on player placement.")]
        [SerializeField] private bool _enablePlacementBlockers = false;

        [Header("References")]
        [SerializeField] private SandboxManager _sandbox;
        [SerializeField] private PropPlacer _placer;
        [SerializeField] private Transform _planeRoot;

        [Header("Spawn ramp")]
        [Tooltip("First player placement index that can spawn a blocker (1-based, matches placement count).")]
        [SerializeField] private int _spawnStartPlacement = 3;
        [Tooltip("Total blocker planes spawned by the final player placement.")]
        [SerializeField] private int _totalPlanesBySessionEnd = 30;
        [Tooltip("Lower = faster ramp (more planes sooner). Higher = slow start.")]
        [SerializeField] private float _spawnRampExponent = 1.15f;
        [Tooltip("Minimum planes spawned each time blockers are eligible (early session).")]
        [SerializeField] private int _minPlanesPerPlacementEvent = 2;
        [Tooltip("Late session: up to this many planes per player placement.")]
        [SerializeField] private int _maxPlanesPerPlacement = 6;

        [Header("Size (modular panels, grows over session)")]
        [SerializeField] private float _earlyWidthMin = 1.85f;
        [SerializeField] private float _earlyWidthMax = 3.1f;
        [SerializeField] private float _earlyHeightMin = 2.1f;
        [SerializeField] private float _earlyHeightMax = 3.4f;
        [Tooltip("Minimum panel modules at session start (width and height).")]
        [SerializeField] private int _earlyMinPanelModules = 2;
        [SerializeField] private float _lateWidthMin = 3.6f;
        [SerializeField] private float _lateWidthMax = 6.1f;
        [SerializeField] private float _lateHeightMin = 4.8f;
        [SerializeField] private float _lateHeightMax = 7.3f;
        [Tooltip("Lower = walls reach large sizes sooner.")]
        [SerializeField] private float _sizeGrowthExponent = 1.2f;

        [Header("Placement")]
        [Tooltip("Random XZ inset from sandbox edges so planes stay on the pedestal.")]
        [SerializeField] private float _sandboxEdgeInset = 0.35f;
        [Tooltip("Extra inset so the plane mesh (width) stays fully on the pedestal.")]
        [SerializeField] private float _planeFootprintPadding = 0.08f;
        [SerializeField] private float _surfaceLift = 0.05f;
        [Tooltip("Max Z roll (degrees). Cubicle partitions stay near 0.")]
        [SerializeField] private float _randomZRollDegrees = 1.5f;

        [Header("Corporate cubicle layout")]
        [Tooltip("Floor-plan grid spacing (~4 ft module). Positions and sizes snap to this.")]
        [SerializeField] private float _cubicleGridSpacing = 1.22f;
        [Tooltip("Center-to-center spacing between partition panels along a wall run.")]
        [SerializeField] private float _cubicleBaySpacing = 1.22f;
        [Tooltip("Snap boxing bounds outward to the grid so walls form a rectilinear cage.")]
        [SerializeField] private bool _snapBoundsToGrid = true;
        [Tooltip("Only allow 0/90/180/270° yaw (office partition alignment).")]
        [SerializeField] private bool _orthogonalYawOnly = true;

        [Header("Box-in props")]
        [Tooltip("Early session: loose padding around placed props before walls hug the cluster.")]
        [SerializeField] private float _boxPaddingEarly = 2.2f;
        [Tooltip("Late session: tight padding so walls close in on the build.")]
        [SerializeField] private float _boxPaddingLate = 0.35f;
        [Tooltip("How far past the boxing rectangle each wall sits outward.")]
        [SerializeField] private float _boxWallOutset = 0.12f;
        [Tooltip("Lower = walls start boxing props sooner (0–1 session blend).")]
        [SerializeField] private float _boxingRampExponent = 0.85f;
        [Tooltip("How loosely walls sit on the cage early (0 = fixed bays, 1 = anywhere on each edge).")]
        [SerializeField] private float _boxedPositionRandomnessEarly = 0.72f;
        [Tooltip("Late-session looseness along each boxed edge.")]
        [SerializeField] private float _boxedPositionRandomnessLate = 0.38f;
        [Tooltip("Extra jitter along a box edge, as a fraction of edge length.")]
        [SerializeField] private float _boxEdgeJitter = 0.14f;
        [Tooltip("Chance to pick a random side instead of the most under-filled side.")]
        [SerializeField] private float _boxSideRandomChance = 0.42f;
        [Tooltip("Random variation on outward wall offset.")]
        [SerializeField] private float _boxWallOutsetJitter = 0.18f;
        [Tooltip("Chance to spawn a wall inside the cage (not on an edge).")]
        [SerializeField] private float _interiorSpawnWeightEarly = 0.32f;
        [SerializeField] private float _interiorSpawnWeightLate = 0.52f;
        [Tooltip("Blend wall yaw toward the camera (0 = rectilinear cage, 1 = always face viewer).")]
        [SerializeField] private float _faceCameraYawBlend = 0f;

        [Header("Wall textures")]
        [Tooltip("Folder under StreamingAssets containing wall images (.png, .jpg, etc.).")]
        [SerializeField] private string _wallsFolder = "Walls";
        [SerializeField] private Vector2 _textureScale = Vector2.one;
        [Tooltip("Tint multiplied with the wall image. Use white for no tint.")]
        [SerializeField] private Color _textureTint = Color.white;
        [Tooltip("Material used when no wall images are found.")]
        [SerializeField] private Color _fallbackPlaneColor = new Color(0.06f, 0.06f, 0.07f, 1f);

        [Header("Look")]
        [SerializeField] private bool _castShadows;
        [SerializeField] private bool _receiveShadows;

        private static readonly int BaseMapPropertyId = Shader.PropertyToID("_BaseMap");
        private static readonly int MainTexPropertyId = Shader.PropertyToID("_MainTex");
        private static readonly int CullPropertyId = Shader.PropertyToID("_Cull");
        private static readonly string[] WallImageExtensions = { ".png", ".jpg", ".jpeg", ".webp", ".tga", ".bmp" };

        private readonly List<GameObject> _spawned = new();
        private readonly List<Material> _spawnedMaterials = new();
        private readonly List<Texture2D> _loadedTextures = new();
        private readonly Dictionary<string, Texture2D> _textureCache = new();
        private readonly List<string> _wallImagePaths = new();
        private readonly List<GameObject> _propsScratch = new();
        private readonly int[] _boxSideCounts = new int[4];
        private Shader _blockerShader;
        private bool _wallCatalogLoaded;
        private int _planesSpawned;
        private bool _subscribed;

        private const int BoxSideNorth = 0;
        private const int BoxSideEast = 1;
        private const int BoxSideSouth = 2;
        private const int BoxSideWest = 3;

        void Start()
        {
            ResolveReferences();
            EnsureWallCatalogLoaded();
            Subscribe();
        }

        void OnDestroy()
        {
            Unsubscribe();
            ClearPlanes();
            ReleaseLoadedTextures();
        }

        private void ResolveReferences()
        {
            if (_sandbox == null)
                _sandbox = FindFirstObjectByType<SandboxManager>();
            if (_placer == null)
                _placer = FindFirstObjectByType<PropPlacer>();

            if (_planeRoot == null)
            {
                var existing = GameObject.Find("SandboxViewBlockers");
                _planeRoot = existing != null ? existing.transform : null;
            }

            if (_planeRoot == null && _sandbox != null)
            {
                var rootGo = new GameObject("SandboxViewBlockers");
                rootGo.transform.SetParent(_sandbox.transform, false);
                _planeRoot = rootGo.transform;
            }
        }

        private void Subscribe()
        {
            if (!_enablePlacementBlockers || _subscribed)
                return;

            if (_placer != null)
                _placer.OnPropPlacedWithContext += HandlePropPlaced;
            if (_sandbox != null)
                _sandbox.OnSessionComplete.AddListener(HandleSessionComplete);

            if (_placer == null && _sandbox == null)
                return;

            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed)
                return;

            if (_placer != null)
                _placer.OnPropPlacedWithContext -= HandlePropPlaced;
            if (_sandbox != null)
                _sandbox.OnSessionComplete.RemoveListener(HandleSessionComplete);

            _subscribed = false;
        }

        private void HandleSessionComplete()
        {
            if (_spawned.Count > 0)
                StartCoroutine(TryAttachToPedestalDisplayWhenReady());
        }

        private IEnumerator TryAttachToPedestalDisplayWhenReady()
        {
            for (int frame = 0; frame < 60; frame++)
            {
                yield return null;

                var display = GameObject.Find("_RuntimeSandboxCombinedModel");
                if (display == null)
                    continue;

                if (!TryGetTrackedPropsWorldBounds(out Bounds sourceWorldBounds))
                    continue;

                AttachBlockersToPedestalDisplay(display.transform, sourceWorldBounds);
                yield break;
            }
        }

        private bool TryGetTrackedPropsWorldBounds(out Bounds bounds)
            => TryGetPropsWorldBounds(playerOnly: false, out bounds);

        private bool TryGetPlayerPropsWorldBounds(out Bounds bounds)
            => TryGetPropsWorldBounds(playerOnly: true, out bounds);

        private bool TryGetPropsWorldBounds(bool playerOnly, out Bounds bounds)
        {
            bounds = default;
            _propsScratch.Clear();
            if (playerOnly)
                PropBudget.Instance?.GetTrackedPlayerPlacedProps(_propsScratch);
            else
                PropBudget.Instance?.GetTrackedPlacedProps(_propsScratch);

            if (_propsScratch.Count == 0 && _sandbox != null && _sandbox.transform != null)
            {
                Transform sandboxRoot = _sandbox.transform;
                for (int i = 0; i < sandboxRoot.childCount; i++)
                {
                    Transform child = sandboxRoot.GetChild(i);
                    if (child != null)
                        _propsScratch.Add(child.gameObject);
                }
            }

            return TryEncapsulateRendererBounds(_propsScratch, out bounds);
        }

        private static bool TryEncapsulateRendererBounds(List<GameObject> props, out Bounds bounds)
        {
            bounds = default;
            bool hasBounds = false;

            for (int i = 0; i < props.Count; i++)
            {
                GameObject prop = props[i];
                if (prop == null || !prop.activeInHierarchy)
                    continue;

                var renderers = prop.GetComponentsInChildren<Renderer>(true);
                for (int r = 0; r < renderers.Length; r++)
                {
                    Renderer renderer = renderers[r];
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
            }

            return hasBounds;
        }

        private void HandlePropPlaced(bool isPlayer, PropEntry prop, Vector3 worldPosition)
        {
            if (!_enablePlacementBlockers)
                return;

            if (!isPlayer || _sandbox == null || !_sandbox.SandboxActive)
                return;

            int playerCount = _sandbox.StyleProfile?.PlayerPlacementCount ?? 0;
            if (playerCount < _spawnStartPlacement)
                return;

            int targetSpawned = ComputeTargetPlaneCount(playerCount);
            int spawnNow = ComputePlanesToSpawnThisPlacement(playerCount, targetSpawned);

            for (int i = 0; i < spawnNow; i++)
            {
                SpawnBlockerPlane(playerCount);
                _planesSpawned++;
            }
        }

        private float GetSessionProgress(int playerPlacementCount)
        {
            int max = Mathf.Max(_spawnStartPlacement + 1, _sandbox.MaxPlayerPlacements);
            int window = max - _spawnStartPlacement + 1;
            int active = playerPlacementCount - _spawnStartPlacement + 1;
            return Mathf.Clamp01(active / (float)window);
        }

        /// <summary>How many planes to add on this placement (1 early, ramps toward <see cref="_maxPlanesPerPlacement"/>).</summary>
        private float GetSpawnEasedProgress(int playerPlacementCount)
        {
            float t = GetSessionProgress(playerPlacementCount);
            float curved = Mathf.Pow(t, Mathf.Max(0.5f, _spawnRampExponent));
            // Blend toward linear so mid-session catches up faster (more aggressive).
            return Mathf.Lerp(curved, t, 0.45f);
        }

        private int ComputePlanesToSpawnThisPlacement(int playerPlacementCount, int targetTotal)
        {
            int needed = Mathf.Max(0, targetTotal - _planesSpawned);
            if (needed <= 0)
                return 0;

            float eased = GetSpawnEasedProgress(playerPlacementCount);
            int minBurst = Mathf.Max(1, _minPlanesPerPlacementEvent);
            int maxBurst = Mathf.Max(minBurst, _maxPlanesPerPlacement);
            int burstCap = Mathf.RoundToInt(Mathf.Lerp(minBurst, maxBurst, eased));

            return Mathf.Min(needed, burstCap);
        }

        private int ComputeTargetPlaneCount(int playerPlacementCount)
        {
            float eased = GetSpawnEasedProgress(playerPlacementCount);
            int target = Mathf.RoundToInt(eased * _totalPlanesBySessionEnd);

            int minAtStart = Mathf.Max(1, _minPlanesPerPlacementEvent);
            if (playerPlacementCount == _spawnStartPlacement && target < minAtStart)
                target = minAtStart;

            return Mathf.Clamp(target, 0, _totalPlanesBySessionEnd);
        }

        private void SpawnBlockerPlane(int playerPlacementCount)
        {
            if (_planeRoot == null)
                ResolveReferences();
            if (_planeRoot == null)
                return;

            if (!TryGetSandboxBounds(out Bounds sandboxBounds))
                return;

            float sessionProgress = GetSessionProgress(playerPlacementCount);
            ComputeRandomPlaneSize(playerPlacementCount, out float width, out float height, sessionProgress);
            float floorY = sandboxBounds.max.y;
            bool hasPlayerProps = TryGetPlayerPropsWorldBounds(out _);
            float interiorWeight = Mathf.Lerp(
                Mathf.Clamp01(_interiorSpawnWeightEarly),
                Mathf.Clamp01(_interiorSpawnWeightLate),
                sessionProgress);

            Vector3 pos;
            float yaw;
            if (hasPlayerProps)
            {
                if (Random.value < interiorWeight
                    && TryPickInteriorPlacement(playerPlacementCount, sandboxBounds, floorY, height, out pos, out yaw))
                {
                    // inside the cage
                }
                else if (TryPickBoxedPlacement(playerPlacementCount, sandboxBounds, floorY, height, out pos, out yaw))
                {
                    // on the cage edge
                }
                else
                {
                    PickRandomSandboxPlacement(sandboxBounds, floorY, height, out pos, out yaw);
                }
            }
            else
            {
                if (Random.value < interiorWeight
                    && TryPickSandboxInteriorPlacement(sandboxBounds, floorY, height, out pos, out yaw))
                {
                    // sandbox center
                }
                else
                {
                    PickRandomSandboxPlacement(sandboxBounds, floorY, height, out pos, out yaw);
                }
            }

            yaw = FinalizeCorporateYaw(yaw, pos);
            pos = ClampBlockerPositionInsideSandbox(pos, width, height, yaw, sandboxBounds);
            float zRoll = _randomZRollDegrees > 0.001f
                ? Random.Range(-_randomZRollDegrees, _randomZRollDegrees)
                : 0f;

            var plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            plane.name = $"ViewBlocker_{playerPlacementCount}_{_planesSpawned}";
            plane.transform.SetParent(_planeRoot, true);
            plane.transform.position = pos;
            plane.transform.rotation = Quaternion.Euler(90f, yaw, zRoll);
            plane.transform.localScale = new Vector3(width / 10f, 1f, height / 10f);

            var col = plane.GetComponent<Collider>();
            if (col != null)
                Destroy(col);

            var renderer = plane.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                Material mat = CreateMaterialForRandomWall();
                _spawnedMaterials.Add(mat);
                renderer.material = mat;
                renderer.shadowCastingMode = _castShadows
                    ? ShadowCastingMode.On
                    : ShadowCastingMode.Off;
                renderer.receiveShadows = _receiveShadows;
            }

            _spawned.Add(plane);
        }

        private void PickRandomSandboxPlacement(Bounds sandboxBounds, float floorY, float height, out Vector3 pos, out float yaw)
        {
            float inset = Mathf.Max(0f, _sandboxEdgeInset);
            float xMin = sandboxBounds.min.x + inset;
            float xMax = sandboxBounds.max.x - inset;
            float zMin = sandboxBounds.min.z + inset;
            float zMax = sandboxBounds.max.z - inset;
            if (xMax < xMin) { xMin = sandboxBounds.min.x; xMax = sandboxBounds.max.x; }
            if (zMax < zMin) { zMin = sandboxBounds.min.z; zMax = sandboxBounds.max.z; }

            float grid = Mathf.Max(0.25f, _cubicleGridSpacing);
            int cellsX = Mathf.Max(1, Mathf.FloorToInt((xMax - xMin) / grid));
            int cellsZ = Mathf.Max(1, Mathf.FloorToInt((zMax - zMin) / grid));
            int ix = Random.Range(0, cellsX);
            int iz = Random.Range(0, cellsZ);

            float x = xMin + (ix + 0.5f) * grid;
            float z = zMin + (iz + 0.5f) * grid;
            pos = new Vector3(
                SnapToGrid(x, grid),
                floorY + _surfaceLift + height * 0.5f,
                SnapToGrid(z, grid));

            Vector3 toCenter = sandboxBounds.center - pos;
            toCenter.y = 0f;
            if (toCenter.sqrMagnitude > 0.0001f)
                yaw = QuantizeYawToCardinal(toCenter);
            else
                yaw = Random.value < 0.5f ? 0f : 90f;
        }

        private bool TryPickInteriorPlacement(
            int playerPlacementCount,
            Bounds sandboxBounds,
            float floorY,
            float height,
            out Vector3 pos,
            out float yaw)
        {
            pos = default;
            yaw = 0f;

            if (!TryComputePropCageBounds(playerPlacementCount, sandboxBounds, out float xMin, out float xMax, out float zMin, out float zMax))
                return false;

            return TryPickPointInsideCage(xMin, xMax, zMin, zMax, sandboxBounds, floorY, height, out pos, out yaw);
        }

        private bool TryPickSandboxInteriorPlacement(
            Bounds sandboxBounds,
            float floorY,
            float height,
            out Vector3 pos,
            out float yaw)
        {
            float inset = Mathf.Max(0f, _sandboxEdgeInset);
            float fill = 0.45f;
            float halfX = sandboxBounds.size.x * fill * 0.5f;
            float halfZ = sandboxBounds.size.z * fill * 0.5f;
            Vector3 center = sandboxBounds.center;

            float xMin = center.x - halfX;
            float xMax = center.x + halfX;
            float zMin = center.z - halfZ;
            float zMax = center.z + halfZ;

            xMin = Mathf.Max(xMin, sandboxBounds.min.x + inset);
            xMax = Mathf.Min(xMax, sandboxBounds.max.x - inset);
            zMin = Mathf.Max(zMin, sandboxBounds.min.z + inset);
            zMax = Mathf.Min(zMax, sandboxBounds.max.z - inset);

            return TryPickPointInsideCage(xMin, xMax, zMin, zMax, sandboxBounds, floorY, height, out pos, out yaw);
        }

        private bool TryPickPointInsideCage(
            float xMin,
            float xMax,
            float zMin,
            float zMax,
            Bounds sandboxBounds,
            float floorY,
            float height,
            out Vector3 pos,
            out float yaw)
        {
            pos = default;
            yaw = 0f;

            if (xMax <= xMin + 0.05f || zMax <= zMin + 0.05f)
                return false;

            float x = Random.Range(xMin, xMax);
            float z = Random.Range(zMin, zMax);
            float y = floorY + _surfaceLift + height * 0.5f;
            pos = new Vector3(x, y, z);

            Vector3 viewer = GetViewerPosition();
            Vector3 toViewer = viewer - pos;
            toViewer.y = 0f;
            if (toViewer.sqrMagnitude > 0.0001f)
                yaw = QuantizeYawToCardinal(toViewer);
            else
            {
                Vector3 toCenter = sandboxBounds.center - pos;
                toCenter.y = 0f;
                yaw = toCenter.sqrMagnitude > 0.0001f
                    ? QuantizeYawToCardinal(toCenter)
                    : Random.value < 0.5f ? 0f : 90f;
            }

            return true;
        }

        private bool TryComputePropCageBounds(
            int playerPlacementCount,
            Bounds sandboxBounds,
            out float xMin,
            out float xMax,
            out float zMin,
            out float zMax)
        {
            xMin = xMax = zMin = zMax = 0f;

            if (!TryGetPlayerPropsWorldBounds(out Bounds propBounds))
                return false;

            float progress = GetSpawnEasedProgress(playerPlacementCount);
            float padding = Mathf.Lerp(_boxPaddingEarly, _boxPaddingLate, progress);

            xMin = propBounds.min.x - padding;
            xMax = propBounds.max.x + padding;
            zMin = propBounds.min.z - padding;
            zMax = propBounds.max.z + padding;

            float sandboxInset = Mathf.Max(0f, _sandboxEdgeInset);
            xMin = Mathf.Max(xMin, sandboxBounds.min.x + sandboxInset);
            xMax = Mathf.Min(xMax, sandboxBounds.max.x - sandboxInset);
            zMin = Mathf.Max(zMin, sandboxBounds.min.z + sandboxInset);
            zMax = Mathf.Min(zMax, sandboxBounds.max.z - sandboxInset);

            if (_snapBoundsToGrid)
                ExpandBoundsToCubicleGrid(ref xMin, ref xMax, ref zMin, ref zMax);

            ClampBoxToSandboxInterior(ref xMin, ref xMax, ref zMin, ref zMax, sandboxBounds, sandboxInset);
            return xMax > xMin + 0.05f && zMax > zMin + 0.05f;
        }

        private bool TryPickBoxedPlacement(
            int playerPlacementCount,
            Bounds sandboxBounds,
            float floorY,
            float height,
            out Vector3 pos,
            out float yaw)
        {
            pos = default;
            yaw = 0f;

            if (!TryComputePropCageBounds(playerPlacementCount, sandboxBounds, out float xMin, out float xMax, out float zMin, out float zMax))
                return false;

            float progress = GetSpawnEasedProgress(playerPlacementCount);
            float sessionProgress = GetSessionProgress(playerPlacementCount);
            float edgeRandomness = Mathf.Lerp(
                Mathf.Clamp01(_boxedPositionRandomnessEarly),
                Mathf.Clamp01(_boxedPositionRandomnessLate),
                sessionProgress);
            float outset = Mathf.Max(0f, _boxWallOutset);
            if (_boxWallOutsetJitter > 0.001f)
                outset += Random.Range(-_boxWallOutsetJitter, _boxWallOutsetJitter);

            int side = PickBoxSide(xMin, xMax, zMin, zMax);
            float edgeMin = side == BoxSideNorth || side == BoxSideSouth ? xMin : zMin;
            float edgeMax = side == BoxSideNorth || side == BoxSideSouth ? xMax : zMax;
            float center = PickCorporateBayPosition(edgeMin, edgeMax, side, edgeRandomness);

            float y = floorY + _surfaceLift + height * 0.5f;
            float inwardYaw = 0f;

            switch (side)
            {
                case BoxSideNorth:
                    pos = new Vector3(Mathf.Clamp(center, xMin, xMax), y, zMax);
                    inwardYaw = 180f;
                    break;
                case BoxSideEast:
                    pos = new Vector3(xMax, y, Mathf.Clamp(center, zMin, zMax));
                    inwardYaw = -90f;
                    break;
                case BoxSideSouth:
                    pos = new Vector3(Mathf.Clamp(center, xMin, xMax), y, zMin);
                    inwardYaw = 0f;
                    break;
                default:
                    pos = new Vector3(xMin, y, Mathf.Clamp(center, zMin, zMax));
                    inwardYaw = 90f;
                    break;
            }

            NudgeWallInsideFromEdge(ref pos, side, outset);
            yaw = BlendYawTowardCamera(pos, inwardYaw, progress);
            _boxSideCounts[side]++;
            return true;
        }

        private float PickCorporateBayPosition(float edgeMin, float edgeMax, int side, float edgeRandomness)
        {
            float bay = Mathf.Max(0.35f, _cubicleBaySpacing > 0.01f ? _cubicleBaySpacing : _cubicleGridSpacing);
            float edgeLength = Mathf.Max(bay, edgeMax - edgeMin);
            int bayCount = Mathf.Max(1, Mathf.FloorToInt(edgeLength / bay));
            int slot = _boxSideCounts[side] % bayCount;
            float bayPos = edgeMin + bay * (slot + 0.5f);
            float randomAlongEdge = Random.Range(edgeMin, edgeMax);

            float t = Mathf.Clamp01(edgeRandomness);
            float pos = Mathf.Lerp(bayPos, randomAlongEdge, t);

            if (_boxEdgeJitter > 0.001f)
            {
                float jitter = edgeLength * _boxEdgeJitter * Random.Range(-0.5f, 0.5f);
                pos += jitter;
            }

            pos = SnapToGrid(pos, bay);
            return Mathf.Clamp(pos, edgeMin, edgeMax);
        }

        private int PickBoxSide(float xMin, float xMax, float zMin, float zMax)
        {
            if (_boxSideRandomChance > 0.001f && Random.value < _boxSideRandomChance)
                return Random.Range(BoxSideNorth, BoxSideWest + 1);

            return PickUnderfilledBoxSide(xMin, xMax, zMin, zMax);
        }

        private static float SnapToGrid(float value, float grid)
        {
            if (grid <= 0.0001f)
                return value;
            return Mathf.Round(value / grid) * grid;
        }

        private static void ClampBoxToSandboxInterior(
            ref float xMin,
            ref float xMax,
            ref float zMin,
            ref float zMax,
            Bounds sandboxBounds,
            float inset)
        {
            float innerXMin = sandboxBounds.min.x + inset;
            float innerXMax = sandboxBounds.max.x - inset;
            float innerZMin = sandboxBounds.min.z + inset;
            float innerZMax = sandboxBounds.max.z - inset;

            xMin = Mathf.Max(xMin, innerXMin);
            xMax = Mathf.Min(xMax, innerXMax);
            zMin = Mathf.Max(zMin, innerZMin);
            zMax = Mathf.Min(zMax, innerZMax);
        }

        private static void NudgeWallInsideFromEdge(ref Vector3 pos, int side, float inset)
        {
            if (inset <= 0.0001f)
                return;

            switch (side)
            {
                case BoxSideNorth:
                    pos.z -= inset;
                    break;
                case BoxSideEast:
                    pos.x -= inset;
                    break;
                case BoxSideSouth:
                    pos.z += inset;
                    break;
                default:
                    pos.x += inset;
                    break;
            }
        }

        private Vector3 ClampBlockerPositionInsideSandbox(
            Vector3 pos,
            float width,
            float height,
            float yaw,
            Bounds sandboxBounds)
        {
            GetPlaneFootprintHalfExtents(width, height, yaw, out float halfX, out float halfZ);
            float pad = Mathf.Max(_sandboxEdgeInset, _planeFootprintPadding);

            float xMin = sandboxBounds.min.x + pad + halfX;
            float xMax = sandboxBounds.max.x - pad - halfX;
            float zMin = sandboxBounds.min.z + pad + halfZ;
            float zMax = sandboxBounds.max.z - pad - halfZ;

            if (xMax < xMin)
            {
                xMin = sandboxBounds.min.x + pad;
                xMax = sandboxBounds.max.x - pad;
            }

            if (zMax < zMin)
            {
                zMin = sandboxBounds.min.z + pad;
                zMax = sandboxBounds.max.z - pad;
            }

            pos.x = Mathf.Clamp(pos.x, xMin, xMax);
            pos.z = Mathf.Clamp(pos.z, zMin, zMax);
            return pos;
        }

        private static void GetPlaneFootprintHalfExtents(float width, float height, float yawDeg, out float halfX, out float halfZ)
        {
            float halfWidth = Mathf.Max(0.05f, width) * 0.5f;
            float rad = yawDeg * Mathf.Deg2Rad;
            float cos = Mathf.Abs(Mathf.Cos(rad));
            float sin = Mathf.Abs(Mathf.Sin(rad));
            halfX = halfWidth * cos;
            halfZ = halfWidth * sin;

            // Conservative padding so tall panels do not read as hanging over the lip.
            float verticalPad = Mathf.Max(0.05f, height) * 0.02f;
            halfX += verticalPad;
            halfZ += verticalPad;
        }

        private void ExpandBoundsToCubicleGrid(ref float xMin, ref float xMax, ref float zMin, ref float zMax)
        {
            float grid = Mathf.Max(0.25f, _cubicleGridSpacing);
            xMin = Mathf.Floor(xMin / grid) * grid;
            xMax = Mathf.Ceil(xMax / grid) * grid;
            zMin = Mathf.Floor(zMin / grid) * grid;
            zMax = Mathf.Ceil(zMax / grid) * grid;
        }

        private static float QuantizeYawToCardinal(Vector3 flatDirection)
        {
            if (flatDirection.sqrMagnitude < 0.0001f)
                return 0f;

            float yaw = Mathf.Atan2(flatDirection.x, flatDirection.z) * Mathf.Rad2Deg;
            return Mathf.Round(yaw / 90f) * 90f;
        }

        private static float QuantizeYawToCardinal(float yaw)
        {
            return Mathf.Round(yaw / 90f) * 90f;
        }

        private float FinalizeCorporateYaw(float yaw, Vector3 wallPos)
        {
            if (_orthogonalYawOnly)
                yaw = QuantizeYawToCardinal(yaw);

            if (_faceCameraYawBlend > 0.001f)
            {
                Vector3 viewer = GetViewerPosition();
                Vector3 toViewer = viewer - wallPos;
                toViewer.y = 0f;
                float cameraYaw = toViewer.sqrMagnitude > 0.0001f
                    ? Mathf.Atan2(toViewer.x, toViewer.z) * Mathf.Rad2Deg
                    : yaw;
                float blend = Mathf.Clamp01(_faceCameraYawBlend);
                yaw = Mathf.LerpAngle(yaw, _orthogonalYawOnly ? QuantizeYawToCardinal(cameraYaw) : cameraYaw, blend);
            }

            return yaw;
        }

        private void ApplyCorporatePanelDimensions(ref float width, ref float height, float sessionProgress)
        {
            float module = Mathf.Max(0.35f, _cubicleGridSpacing);
            int widthModules = Mathf.Clamp(Mathf.RoundToInt(width / module), 1, 6);
            int heightModules = Mathf.Clamp(Mathf.RoundToInt(height / (module * 1.25f)), 1, 6);

            int minModules = sessionProgress < 0.55f ? Mathf.Max(1, _earlyMinPanelModules) : 1;
            widthModules = Mathf.Max(minModules, widthModules);
            heightModules = Mathf.Max(minModules, heightModules);

            width = widthModules * module;
            height = heightModules * module * 1.25f;
        }

        private int PickUnderfilledBoxSide(float xMin, float xMax, float zMin, float zMax)
        {
            float northSouthLen = Mathf.Max(0.1f, xMax - xMin);
            float eastWestLen = Mathf.Max(0.1f, zMax - zMin);
            float[] edgeLengths = { northSouthLen, eastWestLen, northSouthLen, eastWestLen };

            int bestSide = BoxSideNorth;
            float bestScore = float.MaxValue;

            for (int side = 0; side < 4; side++)
            {
                float score = _boxSideCounts[side] / edgeLengths[side];
                if (score < bestScore)
                {
                    bestScore = score;
                    bestSide = side;
                }
            }

            return bestSide;
        }

        private float BlendYawTowardCamera(Vector3 wallPos, float inwardYaw, float sessionProgress)
        {
            Vector3 viewer = GetViewerPosition();
            Vector3 toViewer = viewer - wallPos;
            toViewer.y = 0f;

            float cameraYaw = toViewer.sqrMagnitude > 0.0001f
                ? Mathf.Atan2(toViewer.x, toViewer.z) * Mathf.Rad2Deg
                : inwardYaw;

            float blend = Mathf.Clamp01(_faceCameraYawBlend) * Mathf.Clamp01(sessionProgress);
            return Mathf.LerpAngle(inwardYaw, cameraYaw, blend);
        }

        private void EnsureWallCatalogLoaded()
        {
            if (_wallCatalogLoaded)
                return;
            _wallCatalogLoaded = true;
            _wallImagePaths.Clear();

            string dir = Path.Combine(Application.streamingAssetsPath, _wallsFolder.Trim().TrimStart('/', '\\'));
            if (!Directory.Exists(dir))
            {
                Debug.LogWarning($"[SandboxViewBlocker] Walls folder not found: {dir}. Using solid fallback material.");
                return;
            }

            foreach (string path in Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories))
            {
                string ext = Path.GetExtension(path);
                if (string.IsNullOrEmpty(ext))
                    continue;
                if (!WallImageExtensions.Any(s => ext.Equals(s, System.StringComparison.OrdinalIgnoreCase)))
                    continue;
                _wallImagePaths.Add(path);
            }

            _wallImagePaths.Sort(System.StringComparer.OrdinalIgnoreCase);

            if (_wallImagePaths.Count == 0)
                Debug.LogWarning($"[SandboxViewBlocker] No images in {dir}. Add .png/.jpg files to StreamingAssets/{_wallsFolder}.");
            else
                Debug.Log($"[SandboxViewBlocker] Loaded wall image catalog ({_wallImagePaths.Count} files).");
        }

        private Texture2D GetRandomWallTexture()
        {
            if (_wallImagePaths.Count == 0)
                return null;

            string path = _wallImagePaths[Random.Range(0, _wallImagePaths.Count)];
            if (_textureCache.TryGetValue(path, out Texture2D cached) && cached != null)
                return cached;

            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                {
                    name = Path.GetFileNameWithoutExtension(path),
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };

                if (!tex.LoadImage(bytes, markNonReadable: false))
                {
                    Destroy(tex);
                    Debug.LogWarning($"[SandboxViewBlocker] Failed to decode image: {path}");
                    return null;
                }

                _textureCache[path] = tex;
                _loadedTextures.Add(tex);
                return tex;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[SandboxViewBlocker] Could not read wall image {path}: {e.Message}");
                return null;
            }
        }

        private Material CreateMaterialForRandomWall()
        {
            Texture2D tex = GetRandomWallTexture();
            Shader shader = GetBlockerShader();
            var mat = new Material(shader);

            ConfigureDoubleSided(mat);

            if (tex != null)
            {
                AssignMaterialMainTexture(mat, tex);
                SetMaterialMainTextureScale(mat, _textureScale);

                if (mat.HasProperty("_BaseColor"))
                    mat.SetColor("_BaseColor", _textureTint);
                else
                    mat.color = _textureTint;
            }
            else
            {
                if (mat.HasProperty("_BaseColor"))
                    mat.SetColor("_BaseColor", _fallbackPlaneColor);
                else
                    mat.color = _fallbackPlaneColor;
            }

            return mat;
        }

        private Shader GetBlockerShader()
        {
            if (_blockerShader != null)
                return _blockerShader;

            _blockerShader = Shader.Find("Universal Render Pipeline/Unlit")
                          ?? Shader.Find("Unlit/Texture")
                          ?? Shader.Find("Unlit/Color")
                          ?? Shader.Find("Standard");
            return _blockerShader;
        }

        private static void AssignMaterialMainTexture(Material material, Texture2D texture)
        {
            if (material == null || texture == null)
                return;

            if (material.HasProperty(BaseMapPropertyId))
                material.SetTexture(BaseMapPropertyId, texture);
            if (material.HasProperty(MainTexPropertyId))
                material.SetTexture(MainTexPropertyId, texture);
            material.mainTexture = texture;
        }

        private static void SetMaterialMainTextureScale(Material material, Vector2 scale)
        {
            if (material == null)
                return;

            if (material.HasProperty(BaseMapPropertyId))
                material.SetTextureScale(BaseMapPropertyId, scale);
            if (material.HasProperty(MainTexPropertyId))
                material.SetTextureScale(MainTexPropertyId, scale);
        }

        private void ReleaseLoadedTextures()
        {
            for (int i = 0; i < _loadedTextures.Count; i++)
            {
                if (_loadedTextures[i] != null)
                    Destroy(_loadedTextures[i]);
            }
            _loadedTextures.Clear();
            _textureCache.Clear();
        }

        private static void ConfigureDoubleSided(Material mat)
        {
            if (mat == null) return;

            // URP / HDRP / most SRP unlit shaders
            if (mat.HasProperty(CullPropertyId))
                mat.SetInt(CullPropertyId, (int)CullMode.Off);

            // Some legacy / custom shaders
            if (mat.HasProperty("_CullMode"))
                mat.SetInt("_CullMode", (int)CullMode.Off);

            mat.doubleSidedGI = true;
        }

        private Vector3 GetViewerPosition()
        {
            if (Camera.main != null)
                return Camera.main.transform.position;

            var rig = FindFirstObjectByType<SimplePlayerRig>();
            if (rig != null)
                return rig.transform.position;

            return _planeRoot != null
                ? _planeRoot.position + Vector3.back * 4f
                : Vector3.back * 4f;
        }

        private bool TryGetSandboxBounds(out Bounds bounds)
        {
            bounds = default;
            Transform floor = _sandbox?.SandboxFloor;
            if (floor == null)
                return false;

            var col = floor.GetComponent<Collider>();
            if (col != null)
            {
                bounds = col.bounds;
                return true;
            }

            var renderers = floor.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
                return false;

            bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
            return true;
        }

        private void ComputeRandomPlaneSize(int playerPlacementCount, out float width, out float height, float sessionProgress)
        {
            float eased = Mathf.Pow(sessionProgress, Mathf.Max(1f, _sizeGrowthExponent));

            float widthMin = Mathf.Lerp(_earlyWidthMin, _lateWidthMin, eased);
            float widthMax = Mathf.Lerp(_earlyWidthMax, _lateWidthMax, eased);
            float heightMin = Mathf.Lerp(_earlyHeightMin, _lateHeightMin, eased);
            float heightMax = Mathf.Lerp(_earlyHeightMax, _lateHeightMax, eased);

            if (widthMax < widthMin) (widthMin, widthMax) = (widthMax, widthMin);
            if (heightMax < heightMin) (heightMin, heightMax) = (heightMax, heightMin);

            width = Random.Range(widthMin, widthMax);
            height = Random.Range(heightMin, heightMax);
            ApplyCorporatePanelDimensions(ref width, ref height, sessionProgress);
        }

        /// <summary>
        /// Parents view blockers under the main-pedestal miniature so they scale/move with the combined props.
        /// Called after session complete when a combined pedestal display is shown.
        /// </summary>
        public void AttachBlockersToPedestalDisplay(Transform miniatureDisplay, Bounds sourceWorldBounds)
        {
            if (!_enablePlacementBlockers)
                return;

            if (miniatureDisplay == null || _spawned.Count == 0)
                return;

            if (_spawned[0] != null && _spawned[0].transform.parent == miniatureDisplay)
                return;

            ResolveReferences();
            Vector3 pivot = GetPedestalPivot(sourceWorldBounds);
            Vector3 parentScale = miniatureDisplay.lossyScale;
            float scaleX = Mathf.Max(0.0001f, parentScale.x);
            float scaleY = Mathf.Max(0.0001f, parentScale.y);
            float scaleZ = Mathf.Max(0.0001f, parentScale.z);
            Quaternion parentWorldRotation = miniatureDisplay.rotation;
            int layer = miniatureDisplay.gameObject.layer;
            int attached = 0;

            for (int i = 0; i < _spawned.Count; i++)
            {
                GameObject plane = _spawned[i];
                if (plane == null)
                    continue;

                Vector3 worldPos = plane.transform.position;
                Quaternion worldRot = plane.transform.rotation;
                Vector3 worldScale = plane.transform.lossyScale;

                plane.transform.SetParent(miniatureDisplay, false);
                plane.transform.localPosition = worldPos - pivot;
                plane.transform.localRotation = Quaternion.Inverse(parentWorldRotation) * worldRot;
                plane.transform.localScale = new Vector3(
                    worldScale.x / scaleX,
                    worldScale.y / scaleY,
                    worldScale.z / scaleZ);

                SetLayerRecursive(plane, layer);
                attached++;
            }

            if (_planeRoot != null && _planeRoot.childCount == 0)
                _planeRoot.gameObject.SetActive(false);

            Debug.Log($"[SandboxViewBlocker] Attached {attached} blocker plane(s) to mainPedestal miniature.");
        }

        private Vector3 GetPedestalPivot(Bounds sourceWorldBounds)
        {
            if (TryGetSandboxBounds(out Bounds sandboxBounds))
            {
                return new Vector3(
                    sandboxBounds.center.x,
                    sourceWorldBounds.min.y,
                    sandboxBounds.center.z);
            }

            return new Vector3(
                sourceWorldBounds.center.x,
                sourceWorldBounds.min.y,
                sourceWorldBounds.center.z);
        }

        private static void SetLayerRecursive(GameObject root, int layer)
        {
            if (root == null)
                return;

            root.layer = layer;
            foreach (Transform child in root.transform)
                SetLayerRecursive(child.gameObject, layer);
        }

        public void ClearPlanes()
        {
            for (int i = 0; i < _spawned.Count; i++)
            {
                if (_spawned[i] != null)
                    Destroy(_spawned[i]);
            }
            _spawned.Clear();

            for (int i = 0; i < _spawnedMaterials.Count; i++)
            {
                if (_spawnedMaterials[i] != null)
                    Destroy(_spawnedMaterials[i]);
            }
            _spawnedMaterials.Clear();

            _planesSpawned = 0;
            for (int i = 0; i < _boxSideCounts.Length; i++)
                _boxSideCounts[i] = 0;
        }
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace AlgorithmicGallery.Corruption
{
    /// <summary>
    /// Shrinks <c>sandboxroomtogrow</c> horizontally (X/Z only) as the player places props.
    /// Milestones at placements 5, 10, 15, 18 — each advances 25% toward the final X/Z scale multiplier.
    /// Y scale is left unchanged so walls close inward without rising or compressing in height.
    /// </summary>
    public class SandboxRoomShrinkController : MonoBehaviour
    {
        private static readonly int[] MilestonePlacements = { 5, 10, 15, 18 };
        private static readonly float[] MilestoneProgress = { 0.25f, 0.50f, 0.75f, 1.00f };

        [SerializeField] private string _roomObjectName = "sandboxroomtogrow";
        [SerializeField] private string _pedestalObjectName = "sandboxpedestal";
        [SerializeField] private float _shrinkDuration = 1.25f;
        [Tooltip("Final horizontal scale at full shrink (1 = no shrink). 0.625 matches the old 15th-placement size.")]
        [SerializeField] private float _finalScaleMultiplier = 0.625f;

        [Header("Shrink behavior")]
        [Tooltip("Shrink toward this pedestal so walls close in around it (X/Z only).")]
        [SerializeField] private bool _shrinkTowardPedestal = true;
        [Tooltip("Keep the room grounded by aligning its world bounds minimum Y to this floor height.")]
        [SerializeField] private float _floorY = 0f;
        [Tooltip("Resolve floor Y from this hierarchy path first, e.g. space/floor.")]
        [SerializeField] private string _floorHierarchyPath = "space/floor";
        [Tooltip("Fallback floor object name if hierarchy path is missing.")]
        [SerializeField] private string _floorObjectName = "floor";
        [Tooltip("If enabled, floor Y is read from the floor object's top surface at runtime.")]
        [SerializeField] private bool _autoResolveFloorYFromScene = true;
        [Tooltip("Prevent the shrinking room bounds from overlapping the pedestal bounds (XZ), leaving this margin.")]
        [SerializeField] private float _pedestalCollisionMargin = 0.08f;

        [Header("Room lights")]
        [Tooltip("Move/dim sandbox point lights as walls close in to avoid harsh circular hotspots.")]
        [SerializeField] private bool _compensateRoomLights = true;
        [SerializeField] private string[] _roomLightsRootNames = { "squarelights", "sandboxdecor" };
        [Tooltip("How much room point lights move inward with the walls (0 = dim only, 1 = match wall shrink).")]
        [Range(0f, 1f)]
        [SerializeField] private float _lightInwardFollow = 1f;
        [SerializeField] private float _lightIntensityAtFullShrink = 0.42f;
        [SerializeField] private float _lightRangeAtFullShrink = 0.5f;
        [Tooltip("Only reposition point lights that are not attached to visible/physical objects (prevents decor meshes from being moved).")]
        [SerializeField] private bool _repositionOnlyStandaloneLights = true;

        private Transform _room;
        private Transform _pedestal;
        private Transform _floorTransform;
        private Vector3 _initialScale;
        private Vector3 _initialRoomPosition;
        private Vector3 _initialRoomPositionRelativeToPedestal;
        private float _initialBottomOffsetFromFloor;
        private bool _hasInitialBottomOffsetFromFloor;
        private float _currentProgress;
        private int _nextMilestoneIndex;
        private Coroutine _scaleRoutine;
        private SandboxManager _sandbox;
        private PropPlacer _placer;
        private bool _subscribed;
        private readonly List<ManagedRoomLight> _managedLights = new();
        private bool _loggedMissingLightsRoot;

        private sealed class ManagedRoomLight
        {
            public Light Light;
            public Vector3 InitialWorldPosition;
            public float InitialIntensity;
            public float InitialRange;
        }

        void Start()
        {
            ResolveReferences();
            Subscribe();
        }

        void OnDestroy()
        {
            Unsubscribe();
        }

        public void Initialize()
        {
            ResolveReferences();
            Subscribe();
        }

        private void ResolveReferences()
        {
            if (_sandbox == null)
                _sandbox = FindFirstObjectByType<SandboxManager>();
            if (_placer == null)
                _placer = FindFirstObjectByType<PropPlacer>();

            if (_room != null)
                return;

            if (string.IsNullOrWhiteSpace(_roomObjectName))
                return;

            var roomGo = GameObject.Find(_roomObjectName);
            if (roomGo == null)
            {
                Debug.LogWarning($"[SandboxRoomShrinkController] Could not find GameObject \"{_roomObjectName}\".");
                return;
            }

            _room = roomGo.transform;
            _initialScale = _room.localScale;
            _initialRoomPosition = _room.position;
            _currentProgress = 0f;
            _nextMilestoneIndex = 0;

            ResolvePedestalReference();
            ResolveFloorReference();
            CacheInitialRelativeOffsetToPedestal();
            CacheInitialFloorOffset();
            CacheManagedLights();
        }

        private void CacheInitialFloorOffset()
        {
            _hasInitialBottomOffsetFromFloor = false;
            if (_room == null)
                return;

            if (_autoResolveFloorYFromScene)
                ResolveFloorReference();

            if (!TryGetWorldBoundsFromHierarchy(_room, out Bounds roomBounds))
                return;

            // Preserve the authored vertical relationship instead of forcing exact floor contact.
            _initialBottomOffsetFromFloor = _floorY - roomBounds.min.y;
            _hasInitialBottomOffsetFromFloor = true;
        }

        private void ResolvePedestalReference()
        {
            if (_pedestal != null)
                return;

            if (string.IsNullOrWhiteSpace(_pedestalObjectName))
                return;

            var pedestalGo = GameObject.Find(_pedestalObjectName.Trim());
            if (pedestalGo != null)
                _pedestal = pedestalGo.transform;
        }

        private void ResolveFloorReference()
        {
            if (_floorTransform == null)
            {
                _floorTransform = SceneHierarchyLookup.FindTransform(_floorHierarchyPath, _floorObjectName);
            }

            if (!_autoResolveFloorYFromScene || _floorTransform == null)
                return;

            if (TryGetFloorTopY(_floorTransform, out float resolvedY))
                _floorY = resolvedY;
        }

        private void CacheInitialRelativeOffsetToPedestal()
        {
            if (_room == null)
                return;

            ResolvePedestalReference();
            if (_pedestal == null)
            {
                _initialRoomPositionRelativeToPedestal = Vector3.zero;
                return;
            }

            Vector3 pedestalPivot = GetPedestalPivotXZ();
            Vector3 roomPos = _room.position;
            _initialRoomPositionRelativeToPedestal = new Vector3(
                roomPos.x - pedestalPivot.x,
                0f,
                roomPos.z - pedestalPivot.z);
        }

        private void CacheManagedLights()
        {
            _managedLights.Clear();
            if (!_compensateRoomLights || _room == null)
                return;

            var seen = new HashSet<Light>();
            int rootsFound = 0;

            if (_roomLightsRootNames != null)
            {
                for (int r = 0; r < _roomLightsRootNames.Length; r++)
                {
                    string rootName = _roomLightsRootNames[r];
                    if (string.IsNullOrWhiteSpace(rootName))
                        continue;

                    var rootGo = GameObject.Find(rootName.Trim());
                    if (rootGo == null)
                        continue;

                    rootsFound++;
                    AddPointLightsFromHierarchy(rootGo.transform, seen);
                }
            }

            if (rootsFound == 0)
            {
                if (!_loggedMissingLightsRoot)
                {
                    _loggedMissingLightsRoot = true;
                    Debug.LogWarning(
                        "[SandboxRoomShrinkController] No room light roots found " +
                        "(expected squarelights / sandboxdecor); point lights will not be adjusted.");
                }

                return;
            }
        }

        private void AddPointLightsFromHierarchy(Transform root, HashSet<Light> seen)
        {
            var lights = root.GetComponentsInChildren<Light>(true);
            for (int i = 0; i < lights.Length; i++)
            {
                Light light = lights[i];
                if (light == null || light.type != LightType.Point || !seen.Add(light))
                    continue;
                if (_repositionOnlyStandaloneLights && !IsStandaloneLightTransform(light))
                    continue;

                _managedLights.Add(new ManagedRoomLight
                {
                    Light = light,
                    InitialWorldPosition = light.transform.position,
                    InitialIntensity = light.intensity,
                    InitialRange = light.range
                });
            }
        }

        private static bool IsStandaloneLightTransform(Light light)
        {
            if (light == null)
                return false;

            Transform t = light.transform;
            GameObject go = t.gameObject;

            // If this exact object has visual/physical components, moving it can drag decor.
            if (go.GetComponent<Renderer>() != null)
                return false;
            if (go.GetComponent<Collider>() != null)
                return false;
            if (go.GetComponent<Rigidbody>() != null)
                return false;

            return true;
        }

        private void Subscribe()
        {
            if (_subscribed)
                return;

            if (_placer == null)
                _placer = FindFirstObjectByType<PropPlacer>();
            if (_placer == null)
                return;

            _placer.OnPropPlaced += HandlePropPlaced;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed || _placer == null)
                return;

            _placer.OnPropPlaced -= HandlePropPlaced;
            _subscribed = false;
        }

        private void HandlePropPlaced(bool isPlayer)
        {
            if (!isPlayer)
                return;

            if (_room == null)
                ResolveReferences();
            if (_room == null || _sandbox?.StyleProfile == null)
                return;

            int placementCount = _sandbox.StyleProfile.PlayerPlacementCount;

            while (_nextMilestoneIndex < MilestonePlacements.Length
                   && placementCount >= MilestonePlacements[_nextMilestoneIndex])
            {
                float targetProgress = MilestoneProgress[_nextMilestoneIndex];
                _nextMilestoneIndex++;
                ApplyShrinkProgress(targetProgress);
            }
        }

        private void ApplyShrinkProgress(float progress)
        {
            progress = Mathf.Clamp01(progress);
            if (progress <= _currentProgress + 0.0001f)
                return;

            _currentProgress = progress;
            Vector3 targetScale = GetTargetScaleForProgress(progress);
            ApplyLightCompensationForTargetScale(targetScale);

            if (_scaleRoutine != null)
                StopCoroutine(_scaleRoutine);
            _scaleRoutine = StartCoroutine(AnimateScale(targetScale));
        }

        private Vector3 GetTargetScaleForProgress(float progress)
        {
            float horizontal = Mathf.Lerp(1f, _finalScaleMultiplier, progress);
            return new Vector3(
                _initialScale.x * horizontal,
                _initialScale.y,
                _initialScale.z * horizontal);
        }

        private IEnumerator AnimateScale(Vector3 targetScale)
        {
            Vector3 startScale = _room.localScale;
            Vector3 startPos = _room.position;
            float duration = Mathf.Max(0.01f, _shrinkDuration);
            float t = 0f;

            while (t < duration)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / duration);
                float eased = k * k * (3f - 2f * k); // smoothstep

                Vector3 nextScale = Vector3.Lerp(startScale, targetScale, eased);
                nextScale = ClampScaleToAvoidPedestalOverlap(nextScale);
                _room.localScale = nextScale;

                Vector3 nextPos = startPos;
                if (_shrinkTowardPedestal)
                    nextPos = ComputeRoomPositionForScaleTowardPedestal(nextScale);

                _room.position = nextPos;
                SnapRoomToFloor();

                ApplyLightCompensationForTargetScale(_room.localScale);
                yield return null;
            }

            targetScale = ClampScaleToAvoidPedestalOverlap(targetScale);
            _room.localScale = targetScale;

            Vector3 finalPos = _shrinkTowardPedestal
                ? ComputeRoomPositionForScaleTowardPedestal(targetScale)
                : _room.position;
            _room.position = finalPos;
            SnapRoomToFloor();

            ApplyLightCompensationForTargetScale(targetScale);
            _scaleRoutine = null;
        }

        private Vector3 GetPedestalPivotXZ()
        {
            ResolvePedestalReference();
            if (_pedestal == null)
                return _room != null ? _room.position : Vector3.zero;

            Vector3 p = _pedestal.position;
            return new Vector3(p.x, 0f, p.z);
        }

        private Vector3 ComputeRoomPositionForScaleTowardPedestal(Vector3 roomScale)
        {
            ResolvePedestalReference();
            if (_room == null || _pedestal == null)
                return _room != null ? _room.position : Vector3.zero;

            float horizontal = roomScale.x / Mathf.Max(0.0001f, _initialScale.x);
            Vector3 pedestalPivot = GetPedestalPivotXZ();
            Vector3 xzOffset = _initialRoomPositionRelativeToPedestal * horizontal;

            // Keep Y from whatever the room is currently at (grounding happens separately).
            return new Vector3(
                pedestalPivot.x + xzOffset.x,
                _room.position.y,
                pedestalPivot.z + xzOffset.z);
        }

        private void SnapRoomToFloor()
        {
            if (_room == null)
                return;

            if (_autoResolveFloorYFromScene)
                ResolveFloorReference();

            if (!TryGetWorldBoundsFromHierarchy(_room, out Bounds roomBounds))
                return;

            float bottomY = roomBounds.min.y;
            float targetBottomY = _hasInitialBottomOffsetFromFloor
                ? (_floorY - _initialBottomOffsetFromFloor)
                : _floorY;
            float dy = targetBottomY - bottomY;
            if (Mathf.Abs(dy) <= 0.0001f)
                return;

            Vector3 pos = _room.position;
            pos.y += dy;
            _room.position = pos;
        }

        private static bool TryGetFloorTopY(Transform floorRoot, out float topY)
        {
            topY = 0f;
            if (floorRoot == null)
                return false;

            var col = floorRoot.GetComponent<Collider>();
            if (col != null)
            {
                topY = col.bounds.max.y;
                return true;
            }

            if (TryGetWorldBoundsFromHierarchy(floorRoot, out Bounds floorBounds))
            {
                topY = floorBounds.max.y;
                return true;
            }

            return false;
        }

        private Vector3 ClampScaleToAvoidPedestalOverlap(Vector3 desiredScale)
        {
            ResolvePedestalReference();
            if (_room == null || _pedestal == null)
                return desiredScale;

            Bounds pedestalBounds;
            if (!TryGetWorldBoundsFromHierarchy(_pedestal, out pedestalBounds))
                return desiredScale;

            // Binary search a horizontal scale multiplier in [min, desired] that avoids overlap.
            float desiredHorizontal = desiredScale.x / Mathf.Max(0.0001f, _initialScale.x);
            float minHorizontal = Mathf.Min(desiredHorizontal, _finalScaleMultiplier);
            minHorizontal = Mathf.Clamp(minHorizontal, 0.01f, 1f);
            float maxHorizontal = Mathf.Clamp(desiredHorizontal, minHorizontal, 1f);

            // If we're already safe at the desired scale, keep it.
            if (!WouldOverlapPedestalAtHorizontalScale(maxHorizontal, pedestalBounds))
                return desiredScale;

            // Find the largest horizontal that is safe (i.e., least shrink) within [min, max].
            // If even min overlaps, just return min (can happen if pedestal is very large).
            if (WouldOverlapPedestalAtHorizontalScale(minHorizontal, pedestalBounds))
            {
                return new Vector3(
                    _initialScale.x * minHorizontal,
                    _initialScale.y,
                    _initialScale.z * minHorizontal);
            }

            float lo = minHorizontal;
            float hi = maxHorizontal;
            for (int i = 0; i < 18; i++)
            {
                float mid = (lo + hi) * 0.5f;
                if (WouldOverlapPedestalAtHorizontalScale(mid, pedestalBounds))
                    hi = mid;
                else
                    lo = mid;
            }

            float safeHorizontal = lo;
            return new Vector3(
                _initialScale.x * safeHorizontal,
                _initialScale.y,
                _initialScale.z * safeHorizontal);
        }

        private bool WouldOverlapPedestalAtHorizontalScale(float horizontal, Bounds pedestalBounds)
        {
            if (_room == null)
                return false;

            Vector3 testScale = new Vector3(
                _initialScale.x * horizontal,
                _initialScale.y,
                _initialScale.z * horizontal);

            Vector3 testPos = _room.position;
            if (_shrinkTowardPedestal)
                testPos = ComputeRoomPositionForScaleTowardPedestal(testScale);

            Vector3 originalScale = _room.localScale;
            Vector3 originalPos = _room.position;
            _room.localScale = testScale;
            _room.position = testPos;
            bool hasRoomBounds = TryGetWorldBoundsFromHierarchy(_room, out Bounds roomBounds);
            _room.localScale = originalScale;
            _room.position = originalPos;
            if (!hasRoomBounds)
                return false;

            // Expand pedestal bounds by margin in XZ only.
            float m = Mathf.Max(0f, _pedestalCollisionMargin);
            pedestalBounds.Expand(new Vector3(m * 2f, 0f, m * 2f));

            bool overlapX = roomBounds.max.x > pedestalBounds.min.x && roomBounds.min.x < pedestalBounds.max.x;
            bool overlapZ = roomBounds.max.z > pedestalBounds.min.z && roomBounds.min.z < pedestalBounds.max.z;
            return overlapX && overlapZ;
        }

        private static bool TryGetWorldBoundsFromHierarchy(Transform root, out Bounds bounds)
        {
            bounds = default;
            if (root == null)
                return false;

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            bool hasBounds = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                if (r == null || !r.enabled)
                    continue;

                if (!hasBounds)
                {
                    bounds = r.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(r.bounds);
                }
            }

            return hasBounds;
        }

        private void ApplyLightCompensationForTargetScale(Vector3 roomScale)
        {
            if (!_compensateRoomLights || _room == null || _managedLights.Count == 0)
                return;

            float horizontal = roomScale.x / Mathf.Max(0.0001f, _initialScale.x);
            float progress = _currentProgress;
            Vector3 pivot = _room.position;
            float follow = Mathf.Clamp01(_lightInwardFollow);
            float intensityMul = Mathf.Lerp(1f, _lightIntensityAtFullShrink, progress);
            float rangeMul = Mathf.Lerp(1f, _lightRangeAtFullShrink, progress);

            for (int i = 0; i < _managedLights.Count; i++)
            {
                ManagedRoomLight entry = _managedLights[i];
                if (entry.Light == null)
                    continue;

                Vector3 offset = entry.InitialWorldPosition - pivot;
                float xz = Mathf.Lerp(1f, horizontal, follow);
                Vector3 adjustedOffset = new Vector3(offset.x * xz, offset.y, offset.z * xz);
                entry.Light.transform.position = pivot + adjustedOffset;
                entry.Light.intensity = entry.InitialIntensity * intensityMul;
                entry.Light.range = entry.InitialRange * rangeMul;
            }
        }
    }
}

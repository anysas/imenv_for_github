using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace AlgorithmicGallery.Corruption
{
    /// <summary>
    /// Grid alignment by placement phase (1-based player placement count):
    /// 1–20: new props stay at click; the previous prop moves to a random empty grid cell on the next click.
    /// 21+: new props spawn on a random empty grid cell immediately (no full-scene relayout).
    /// </summary>
    public class SandboxPropGridAligner : MonoBehaviour
    {
        /// <summary>Fired when a prop begins sliding to a grid slot (from, to are world positions).</summary>
        public event System.Action<GameObject, Vector3, Vector3> OnPropMovedOnGrid;

        [Header("References")]
        [SerializeField] private SandboxManager _sandbox;
        [SerializeField] private PropPlacer _placer;

        [Header("Placement phases")]
        [Tooltip("Placements 1 through this value use click placement; the prior prop is grid-snapped on the next click.")]
        [SerializeField] private int _clickPlacementThrough = 20;
        [Tooltip("From this placement onward, new props spawn on the grid immediately.")]
        [SerializeField] private int _immediateGridFrom = 21;

        [Header("Grid")]
        [Tooltip("Distance between grid slot centers (larger = props spread farther apart).")]
        [SerializeField] private float _gridSpacing = 2.1f;
        [Tooltip("Never use a spacing smaller than this, even when props are tiny.")]
        [SerializeField] private float _minGridSpacing = 2.1f;
        [Tooltip("Extra gap between prop footprints when reserving cells and checking overlap.")]
        [SerializeField] private float _cellPadding = 0.15f;
        [Tooltip("Fallback footprint radius when placing before the model exists (metres, half-extent).")]
        [SerializeField] private float _defaultPlacementHalfExtent = 0.55f;
        [Tooltip("Grid footprint as a fraction of the sandbox size (centered). Higher = props can spread across more of the pedestal.")]
        [SerializeField] private float _gridRegionFill = 0.65f;
        [SerializeField] private float _sandboxEdgeInset = 0.2f;
        [Tooltip("If true, yaw snaps to 0/90/180/270 each alignment pass.")]
        [SerializeField] private bool _snapRotationToCardinal = true;
        [Tooltip("If no empty cells remain, fall back to ring search around a random point in the grid region.")]
        [SerializeField] private int _overlapSearchRings = 6;

        [Header("Grid slide")]
        [Tooltip("Pause before a prop starts sliding (lets the newly placed prop read clearly first).")]
        [SerializeField] private float _gridSlideStartDelay = 0.45f;
        [Tooltip("How long props take to slide into a grid cell (seconds).")]
        [SerializeField] private float _gridSlideDuration = 1f;
        [Tooltip("XZ distance below this snaps instantly with no slide.")]
        [SerializeField] private float _gridSlideMinDistance = 0.05f;
        [SerializeField] private AnimationCurve _gridSlideEase = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        private readonly List<GameObject> _propsScratch = new();
        private readonly Dictionary<int, Coroutine> _activeSlides = new();
        private readonly List<Vector3> _gridCellsScratch = new();
        private readonly List<Vector3> _emptyCellsScratch = new();
        private readonly HashSet<long> _occupiedCells = new();
        private bool _subscribed;

        void Start()
        {
            ResolveReferences();
            Subscribe();
        }

        void OnDestroy()
        {
            Unsubscribe();
            _activeSlides.Clear();
        }

        /// <summary>Slides a prop to a grid world position on XZ (preserves Y). Used for immediate-grid placement.</summary>
        public void SlidePropToGridPosition(GameObject prop, Vector3 targetWorld)
        {
            if (prop == null)
                return;

            Transform t = prop.transform;
            Vector3 from = t.position;
            targetWorld.y = from.y;
            BeginSlide(prop, from, targetWorld, GetCardinalRotation(t));
        }

        public bool HasActiveSlides => _activeSlides.Count > 0;

        /// <summary>Waits until every in-flight grid slide has finished (used before main-pedestal exhibit).</summary>
        public IEnumerator WaitUntilSlidesFinished()
        {
            while (true)
            {
                PruneFinishedSlides();
                if (_activeSlides.Count == 0)
                    yield break;
                yield return null;
            }
        }

        private void PruneFinishedSlides()
        {
            if (_activeSlides.Count == 0)
                return;

            var finished = new List<int>();
            foreach (var entry in _activeSlides)
            {
                if (entry.Value == null)
                    finished.Add(entry.Key);
            }

            for (int i = 0; i < finished.Count; i++)
                _activeSlides.Remove(finished[i]);
        }

        public bool UsesImmediateGridPlacement(int placementNumber1Based) =>
            placementNumber1Based >= _immediateGridFrom;

        public bool UsesDeferredGridForPrevious(int placementNumber1Based) =>
            placementNumber1Based >= 2 && placementNumber1Based < _immediateGridFrom;

        /// <summary>
        /// Picks a random unoccupied grid cell. Preserves worldPosition.y.
        /// </summary>
        public bool TryPickRandomEmptyGridPosition(
            ref Vector3 worldPosition,
            GameObject propForFootprint,
            PropEntry propEntry = null)
        {
            ResolveReferences();
            if (!TryGetSandboxBounds(out Bounds sandboxBounds))
                return false;

            Bounds gridRegion = GetCenteredGridRegion(sandboxBounds);
            float grid = GetEffectiveGridSpacing(gridRegion);
            Vector3 gridOrigin = gridRegion.center;

            GetPlacementHalfExtents(propForFootprint, propEntry, out float halfX, out float halfZ);

            CollectOccupiedCellsFromPlacedProps(gridOrigin, grid, excludeProp: propForFootprint);
            BuildEmptyGridCells(gridRegion, gridOrigin, grid, _emptyCellsScratch);

            if (_emptyCellsScratch.Count == 0)
            {
                Vector3 seed = GetRandomPointInGridRegion(gridRegion, worldPosition.y);
                worldPosition = ResolveOccupiedCellRingSearch(
                    seed,
                    gridOrigin,
                    grid,
                    gridRegion,
                    sandboxBounds,
                    propForFootprint,
                    halfX,
                    halfZ);
                return true;
            }

            Vector3 pick = _emptyCellsScratch[Random.Range(0, _emptyCellsScratch.Count)];
            pick.y = worldPosition.y;
            worldPosition = ClampPropCenterToRegion(pick, propForFootprint, gridRegion, sandboxBounds, halfX, halfZ);
            return true;
        }

        private void ResolveReferences()
        {
            if (_sandbox == null)
                _sandbox = FindFirstObjectByType<SandboxManager>();
            if (_placer == null)
                _placer = FindFirstObjectByType<PropPlacer>();
        }

        private void Subscribe()
        {
            if (_subscribed || _placer == null)
                return;

            _placer.OnPropPlacedWithContext += HandlePropPlaced;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed || _placer == null)
                return;

            _placer.OnPropPlacedWithContext -= HandlePropPlaced;
            _subscribed = false;
        }

        private void HandlePropPlaced(bool isPlayer, PropEntry prop, Vector3 worldPosition)
        {
            if (!isPlayer || _sandbox == null || !_sandbox.SandboxActive)
                return;

            int placementNumber = _sandbox.StyleProfile?.PlayerPlacementCount ?? 0;
            if (placementNumber <= 0)
                return;

            if (UsesImmediateGridPlacement(placementNumber))
                return;

            if (UsesDeferredGridForPrevious(placementNumber))
                SnapPreviousPlayerPropToRandomEmptyGrid();
        }

        private void SnapPreviousPlayerPropToRandomEmptyGrid()
        {
            if (!TryGetPlayerPropByOffsetFromNewest(1, out GameObject previous))
                return;

            SnapPropToRandomEmptyGrid(previous);
        }

        /// <summary>
        /// Assigns a random empty grid slot (ignores where the player is looking). Returns false if none exist.
        /// </summary>
        public bool TryAssignRandomGridSlot(GameObject prop, PropEntry propEntry, ref Vector3 worldPosition)
        {
            return TryPickRandomEmptyGridPosition(ref worldPosition, prop, propEntry);
        }

        private bool TryGetPlayerPropByOffsetFromNewest(int offsetFromNewest, out GameObject prop)
        {
            prop = null;
            _propsScratch.Clear();
            PropBudget.Instance?.GetTrackedPlayerPlacedProps(_propsScratch);

            int index = _propsScratch.Count - 1 - offsetFromNewest;
            if (index < 0 || index >= _propsScratch.Count)
                return false;

            prop = _propsScratch[index];
            return prop != null && prop.activeInHierarchy;
        }

        private void SnapPropToRandomEmptyGrid(GameObject prop)
        {
            if (prop == null || !TryGetSandboxBounds(out Bounds sandboxBounds))
                return;

            Bounds gridRegion = GetCenteredGridRegion(sandboxBounds);
            float grid = GetEffectiveGridSpacing(gridRegion);
            Vector3 gridOrigin = gridRegion.center;

            Transform t = prop.transform;
            Vector3 current = t.position;
            GetPlacementHalfExtents(prop, null, out float halfX, out float halfZ);

            _occupiedCells.Clear();
            CollectOccupiedCellsFromPlacedProps(gridOrigin, grid, excludeProp: prop);

            Vector3 target = current;
            if (!TryPickRandomEmptyFromOccupiedSet(
                    gridOrigin,
                    grid,
                    gridRegion,
                    sandboxBounds,
                    prop,
                    halfX,
                    halfZ,
                    out Vector3 randomEmpty))
            {
                Vector3 seed = GetRandomPointInGridRegion(gridRegion, current.y);
                target = ResolveOccupiedCellRingSearch(
                    seed,
                    gridOrigin,
                    grid,
                    gridRegion,
                    sandboxBounds,
                    prop,
                    halfX,
                    halfZ);
            }
            else
            {
                target = randomEmpty;
            }

            target.y = current.y;
            Vector3 from = t.position;
            BeginSlide(prop, from, target, GetCardinalRotation(t));
        }

        private void BeginSlide(GameObject prop, Vector3 from, Vector3 to, Quaternion targetRotation)
        {
            if (prop == null)
                return;

            Transform t = prop.transform;
            Vector3 delta = to - from;
            delta.y = 0f;

            int id = prop.GetInstanceID();
            if (_activeSlides.TryGetValue(id, out Coroutine running) && running != null)
                StopCoroutine(running);

            if (delta.sqrMagnitude < _gridSlideMinDistance * _gridSlideMinDistance)
            {
                _activeSlides[id] = StartCoroutine(DelayedSnapRoutine(prop, from, to, targetRotation));
                return;
            }

            _activeSlides[id] = StartCoroutine(DelayedSlideRoutine(prop, from, to, targetRotation));
        }

        private IEnumerator DelayedSnapRoutine(GameObject prop, Vector3 from, Vector3 to, Quaternion targetRotation)
        {
            int id = prop.GetInstanceID();
            if (_gridSlideStartDelay > 0f)
                yield return new WaitForSeconds(_gridSlideStartDelay);

            if (prop == null)
            {
                _activeSlides.Remove(id);
                yield break;
            }

            prop.transform.position = to;
            prop.transform.rotation = targetRotation;
            RaisePropMovedOnGrid(prop, from, to);
            _activeSlides.Remove(id);
        }

        private IEnumerator DelayedSlideRoutine(GameObject prop, Vector3 from, Vector3 to, Quaternion targetRotation)
        {
            int id = prop.GetInstanceID();
            if (_gridSlideStartDelay > 0f)
                yield return new WaitForSeconds(_gridSlideStartDelay);

            if (prop == null)
            {
                _activeSlides.Remove(id);
                yield break;
            }

            RaisePropMovedOnGrid(prop, from, to);
            yield return SlidePropRoutine(prop, from, to, targetRotation, id);
        }

        private IEnumerator SlidePropRoutine(GameObject prop, Vector3 from, Vector3 to, Quaternion targetRotation, int id)
        {
            Transform t = prop.transform;
            Quaternion startRotation = t.rotation;
            float duration = Mathf.Max(0.01f, _gridSlideDuration);
            float elapsed = 0f;

            while (elapsed < duration)
            {
                if (prop == null || t == null)
                {
                    _activeSlides.Remove(id);
                    yield break;
                }

                elapsed += Time.deltaTime;
                float normalized = Mathf.Clamp01(elapsed / duration);
                float eased = _gridSlideEase != null && _gridSlideEase.length > 0
                    ? _gridSlideEase.Evaluate(normalized)
                    : normalized;

                t.position = Vector3.Lerp(from, to, eased);
                t.rotation = Quaternion.Slerp(startRotation, targetRotation, eased);
                yield return null;
            }

            if (prop != null && t != null)
            {
                t.position = to;
                t.rotation = targetRotation;
            }

            _activeSlides.Remove(id);
        }

        private Quaternion GetCardinalRotation(Transform t)
        {
            if (!_snapRotationToCardinal || t == null)
                return t != null ? t.rotation : Quaternion.identity;

            float yaw = Mathf.Round(t.eulerAngles.y / 90f) * 90f;
            return Quaternion.Euler(0f, yaw, 0f);
        }

        private void RaisePropMovedOnGrid(GameObject prop, Vector3 from, Vector3 to)
        {
            OnPropMovedOnGrid?.Invoke(prop, from, to);
            SandboxGameplaySfx.NotifyPropMovedOnGrid(prop, from, to);
        }

        private void CollectOccupiedCellsFromPlacedProps(Vector3 gridOrigin, float gridSpacing, GameObject excludeProp = null)
        {
            _propsScratch.Clear();
            PropBudget.Instance?.GetTrackedPlayerPlacedProps(_propsScratch);

            for (int i = 0; i < _propsScratch.Count; i++)
            {
                GameObject prop = _propsScratch[i];
                if (prop == null || !prop.activeInHierarchy || prop == excludeProp)
                    continue;

                MarkOccupiedCellsForProp(prop.transform.position, prop, gridOrigin, gridSpacing);
            }
        }

        private void MarkOccupiedCellsForProp(Vector3 worldPos, GameObject prop, Vector3 gridOrigin, float gridSpacing)
        {
            GetPlacementHalfExtents(prop, null, out float halfX, out float halfZ);
            float reach = Mathf.Max(halfX, halfZ) + _cellPadding;
            int cellRadius = Mathf.Max(0, Mathf.CeilToInt(reach / gridSpacing));

            int cx = Mathf.RoundToInt((worldPos.x - gridOrigin.x) / gridSpacing);
            int cz = Mathf.RoundToInt((worldPos.z - gridOrigin.z) / gridSpacing);

            for (int dx = -cellRadius; dx <= cellRadius; dx++)
            {
                for (int dz = -cellRadius; dz <= cellRadius; dz++)
                {
                    _occupiedCells.Add(PackCellIndices(cx + dx, cz + dz));
                }
            }
        }

        private float GetEffectiveGridSpacing(Bounds gridRegion)
        {
            float spacing = Mathf.Max(_minGridSpacing, _gridSpacing);

            _propsScratch.Clear();
            PropBudget.Instance?.GetTrackedPlayerPlacedProps(_propsScratch);

            float largestDiameter = 0f;
            for (int i = 0; i < _propsScratch.Count; i++)
            {
                GameObject prop = _propsScratch[i];
                if (prop == null || !prop.activeInHierarchy)
                    continue;

                GetPlacementHalfExtents(prop, null, out float halfX, out float halfZ);
                largestDiameter = Mathf.Max(largestDiameter, 2f * Mathf.Max(halfX, halfZ) + _cellPadding);
            }

            if (largestDiameter > spacing)
                spacing = largestDiameter;

            return Mathf.Max(0.25f, spacing);
        }

        private Vector3 GetRandomPointInGridRegion(Bounds gridRegion, float y)
        {
            float pad = Mathf.Max(0f, _sandboxEdgeInset);
            float x = Random.Range(gridRegion.min.x + pad, gridRegion.max.x - pad);
            float z = Random.Range(gridRegion.min.z + pad, gridRegion.max.z - pad);
            return new Vector3(x, y, z);
        }

        private void BuildEmptyGridCells(Bounds gridRegion, Vector3 gridOrigin, float gridSpacing, List<Vector3> output)
        {
            output.Clear();

            float pad = Mathf.Max(0f, _sandboxEdgeInset);
            float minX = gridRegion.min.x + pad;
            float maxX = gridRegion.max.x - pad;
            float minZ = gridRegion.min.z + pad;
            float maxZ = gridRegion.max.z - pad;

            int minCx = Mathf.FloorToInt((minX - gridOrigin.x) / gridSpacing);
            int maxCx = Mathf.CeilToInt((maxX - gridOrigin.x) / gridSpacing);
            int minCz = Mathf.FloorToInt((minZ - gridOrigin.z) / gridSpacing);
            int maxCz = Mathf.CeilToInt((maxZ - gridOrigin.z) / gridSpacing);

            for (int cx = minCx; cx <= maxCx; cx++)
            {
                for (int cz = minCz; cz <= maxCz; cz++)
                {
                    long key = PackCellIndices(cx, cz);
                    if (_occupiedCells.Contains(key))
                        continue;

                    output.Add(CellToWorld(cx, cz, gridOrigin, gridSpacing));
                }
            }
        }

        private Bounds GetCenteredGridRegion(Bounds sandboxBounds)
        {
            float fill = Mathf.Clamp(_gridRegionFill, 0.15f, 1f);
            var size = new Vector3(
                sandboxBounds.size.x * fill,
                sandboxBounds.size.y,
                sandboxBounds.size.z * fill);
            return new Bounds(sandboxBounds.center, size);
        }

        private bool TryPickRandomEmptyFromOccupiedSet(
            Vector3 gridOrigin,
            float gridSpacing,
            Bounds gridRegion,
            Bounds sandboxBounds,
            GameObject prop,
            float halfX,
            float halfZ,
            out Vector3 worldPos)
        {
            worldPos = default;
            BuildEmptyGridCells(gridRegion, gridOrigin, gridSpacing, _emptyCellsScratch);

            if (_emptyCellsScratch.Count == 0)
                return false;

            Vector3 pick = _emptyCellsScratch[Random.Range(0, _emptyCellsScratch.Count)];
            worldPos = ClampPropCenterToRegion(pick, prop, gridRegion, sandboxBounds, halfX, halfZ);
            return true;
        }

        private void GetPlacementHalfExtents(GameObject prop, PropEntry propEntry, out float halfX, out float halfZ)
        {
            halfX = _defaultPlacementHalfExtent;
            halfZ = _defaultPlacementHalfExtent;

            if (prop != null)
            {
                GetPropFootprintHalfExtents(prop, out halfX, out halfZ);
                return;
            }

            if (propEntry == null)
                return;

            float sceneMul = _placer != null ? _placer.GlobalPlacedScaleMultiplier : 1f;
            float longest = PropScaler.ScaledLongestAxis(propEntry, sceneMul);
            if (longest > 0.001f)
                halfX = halfZ = longest * 0.5f;
        }

        private Vector3 ResolveOccupiedCellRingSearch(
            Vector3 desired,
            Vector3 gridOrigin,
            float gridSpacing,
            Bounds gridRegion,
            Bounds sandboxBounds,
            GameObject prop,
            float halfX,
            float halfZ)
        {
            int cx = Mathf.RoundToInt((desired.x - gridOrigin.x) / gridSpacing);
            int cz = Mathf.RoundToInt((desired.z - gridOrigin.z) / gridSpacing);

            for (int ring = 1; ring <= Mathf.Max(1, _overlapSearchRings); ring++)
            {
                for (int dx = -ring; dx <= ring; dx++)
                {
                    for (int dz = -ring; dz <= ring; dz++)
                    {
                        if (Mathf.Abs(dx) != ring && Mathf.Abs(dz) != ring)
                            continue;

                        Vector3 candidate = CellToWorld(cx + dx, cz + dz, gridOrigin, gridSpacing);
                        candidate = ClampPropCenterToRegion(candidate, prop, gridRegion, sandboxBounds, halfX, halfZ);

                        if (_occupiedCells.Add(PackCellKey(candidate, gridOrigin, gridSpacing)))
                            return candidate;
                    }
                }
            }

            _occupiedCells.Add(PackCellKey(desired, gridOrigin, gridSpacing));
            return ClampPropCenterToRegion(desired, prop, gridRegion, sandboxBounds, halfX, halfZ);
        }

        private static Vector3 SnapXZToGrid(Vector3 worldPos, Vector3 gridOrigin, float gridSpacing)
        {
            float x = gridOrigin.x + Mathf.Round((worldPos.x - gridOrigin.x) / gridSpacing) * gridSpacing;
            float z = gridOrigin.z + Mathf.Round((worldPos.z - gridOrigin.z) / gridSpacing) * gridSpacing;
            return new Vector3(x, worldPos.y, z);
        }

        private static long PackCellKey(Vector3 worldPos, Vector3 gridOrigin, float gridSpacing)
        {
            int cx = Mathf.RoundToInt((worldPos.x - gridOrigin.x) / gridSpacing);
            int cz = Mathf.RoundToInt((worldPos.z - gridOrigin.z) / gridSpacing);
            return PackCellIndices(cx, cz);
        }

        private static long PackCellIndices(int cx, int cz)
        {
            return ((long)cx << 32) | (uint)cz;
        }

        private static Vector3 CellToWorld(int cx, int cz, Vector3 gridOrigin, float gridSpacing)
        {
            return new Vector3(
                gridOrigin.x + cx * gridSpacing,
                0f,
                gridOrigin.z + cz * gridSpacing);
        }

        private Vector3 ClampPropCenterToRegion(
            Vector3 center,
            GameObject prop,
            Bounds gridRegion,
            Bounds sandboxBounds,
            float halfX,
            float halfZ)
        {
            float pad = Mathf.Max(0f, _sandboxEdgeInset);
            if (halfX <= 0f || halfZ <= 0f)
                GetPlacementHalfExtents(prop, null, out halfX, out halfZ);

            float xMin = gridRegion.min.x + pad + halfX;
            float xMax = gridRegion.max.x - pad - halfX;
            float zMin = gridRegion.min.z + pad + halfZ;
            float zMax = gridRegion.max.z - pad - halfZ;

            if (xMax < xMin)
            {
                xMin = gridRegion.min.x + pad;
                xMax = gridRegion.max.x - pad;
            }

            if (zMax < zMin)
            {
                zMin = gridRegion.min.z + pad;
                zMax = gridRegion.max.z - pad;
            }

            center.x = Mathf.Clamp(center.x, xMin, xMax);
            center.z = Mathf.Clamp(center.z, zMin, zMax);

            float outerPad = pad * 0.5f;
            center.x = Mathf.Clamp(center.x, sandboxBounds.min.x + outerPad + halfX, sandboxBounds.max.x - outerPad - halfX);
            center.z = Mathf.Clamp(center.z, sandboxBounds.min.z + outerPad + halfZ, sandboxBounds.max.z - outerPad - halfZ);
            return center;
        }

        private static void GetPropFootprintHalfExtents(GameObject prop, out float halfX, out float halfZ)
        {
            halfX = 0.12f;
            halfZ = 0.12f;

            if (prop == null)
                return;

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
                return;

            halfX = Mathf.Max(halfX, bounds.extents.x);
            halfZ = Mathf.Max(halfZ, bounds.extents.z);
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
    }
}

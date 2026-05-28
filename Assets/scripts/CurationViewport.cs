using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using AlgorithmicGallery;    // SculptureSpawner

namespace AlgorithmicGallery.Corruption
{
    // Renders the current prop to a RenderTexture using an isolated camera + layer.
    // CurationUI reads ViewRT to display in a RawImage.
    //
    // Layer allocation (chosen to be far from game layers):
    //   PropLayer  = 29  — the loaded prop mesh
    //   LightLayer = 29  — point lights that illuminate only layer 29
    //
    // Why point lights? URP directional lights ignore cullingMask and would bleed
    // into the main game camera. Point lights respect it.
    public class CurationViewport : MonoBehaviour
    {
        // ── Public surface ────────────────────────────────────────────────────
        public RenderTexture ViewRT { get; private set; }

        // Current raw scale applied to the loaded prop root
        public float CurrentDisplayScale =>
            _propRoot != null ? _propRoot.transform.localScale.x : 1f;
        public string CurrentShaderSummary => _currentShaderSummary;
        public float GlobalPlacedScaleMultiplier => _globalPlacedScaleMultiplier;
        public bool IsLoadInProgress => _isLoadInProgress;
        public string CurrentLoadedPropId => _currentLoadedPropId;
        public float CurrentCameraDistance => _distance;
        public float PropPedestalClearance => _propPedestalClearance;
        public float MinFramingDistance => _minFramingDistance;
        public float MaxFramingDistance => _maxFramingDistance;
        public Camera ViewCamera => _cam;

        // ── Constants ─────────────────────────────────────────────────────────
        private const int  PropLayer      = 29;
        private const int  RtWidth        = 1024;
        private const int  RtHeight       = 768;
        private const float TurntableSpeed = 28f;    // deg/sec
        private const float OrbitSensitivity = 0.35f;

        [Header("Scale Parity")]
        [Tooltip("Scene-level placement multiplier. Mirrors PropPlacer when present so curation preview matches gameplay pedestal scale.")]
        [SerializeField] private float _globalPlacedScaleMultiplier = 1f;
        [Header("Scene References")]
        [Tooltip("Optional pedestal root shown in curation viewport for final-size reference.")]
        [SerializeField] private Transform _pedestalRoot;
        [Tooltip("When true, curation preview preserves imported shaders by skipping compatibility override pass.")]
        [SerializeField] private bool _preserveSourceShadersInCuration = true;
        [Tooltip("Optional explicit surface transform (e.g. top face). If null, pedestal top Y is inferred from renderers on Pedestal Root.")]
        [SerializeField] private Transform _pedestalSurfaceAnchor;
        [Tooltip("Small lift above pedestal top to avoid z-fighting. Default mirrors PropPlacer floor offset for placement parity.")]
        [SerializeField] private float _propPedestalClearance = 0.01f;

        [Header("Pedestal Prefab")]
        [Tooltip("The premadePedestal prefab from the in-game placement scene. Instantiated when no pedestal already exists in the curation scene so curation matches gameplay parity exactly.")]
        [SerializeField] private GameObject _premadePedestalPrefab;
        [Tooltip("If true, automatically locate the premadePedestal prefab in Editor when the prefab field is unset.")]
        [SerializeField] private bool _autoResolvePedestalPrefab = true;

        [Header("Camera Framing")]
        [Tooltip("Multiplied by combined bounds extents magnitude for camera distance.")]
        [SerializeField] private float _framingDistanceMultiplier = 4.2f;
        [Tooltip("Extra scale on computed framing distance (zoom out further when > 1).")]
        [SerializeField] private float _framingPaddingScale = 1.15f;
        [SerializeField] private float _minFramingDistance = 1.2f;
        [SerializeField] private float _maxFramingDistance = 22f;

        // ── Internals ─────────────────────────────────────────────────────────
        private Camera       _cam;
        private Transform    _pivot;         // turntable pivot (lights + prop orbit this)
        private GameObject   _propRoot;
        private SculptureSpawner _spawner;

        private GameObject _spawnedPedestalGo;
        // Prefab path used to auto-resolve in Editor when the inspector field is empty.
        private const string PremadePedestalAssetPath = "Assets/premadePedestal (2).prefab";
        private const string PremadePedestalNameToken = "premadepedestal";

        private bool  _turntableOn  = true;
        private bool  _orbiting     = false;
        private Vector3 _lastMouse;
        private float _yaw          = 0f;
        private float _pitch        = 18f;
        private float _distance     = 3.0f;
        private Vector3 _lookAtTarget;
        private string _currentShaderSummary = "shader: no model loaded";
        private bool _isLoadInProgress = false;
        private string _currentLoadedPropId = "";

        // Single-owner load tracking. Each LoadProp() bumps `_loadRequestId`; the running
        // coroutine captures the value at start and rejects results that complete after a
        // newer request has been issued. `_activeLoadRoutine` lets us pre-empt any in-flight
        // coroutine when the user switches models faster than GLTFast can finish.
        private int _loadRequestId = 0;
        private Coroutine _activeLoadRoutine;

        // ── Lifecycle ─────────────────────────────────────────────────────────
        void Awake()
        {
            InitializeIfNeeded();
        }

        public void InitializeForDiagnosticsIfNeeded()
        {
            InitializeIfNeeded();
        }

        private void InitializeIfNeeded()
        {
            if (_cam != null && _pivot != null && ViewRT != null && _spawner != null)
                return;

            // RenderTexture
            ViewRT = new RenderTexture(RtWidth, RtHeight, 24, RenderTextureFormat.ARGB32);
            ViewRT.name = "CurationViewRT";
            ViewRT.Create();

            // Pivot
            _pivot = new GameObject("CurationPivot").transform;
            _pivot.SetParent(transform);
            _pivot.localPosition = Vector3.zero;

            // Camera
            var camGo = new GameObject("CurationCam");
            camGo.transform.SetParent(transform);
            _cam = camGo.AddComponent<Camera>();
            _cam.targetTexture    = ViewRT;
            _cam.cullingMask      = 1 << PropLayer;
            _cam.clearFlags       = CameraClearFlags.SolidColor;
            _cam.backgroundColor  = new Color(0.09f, 0.09f, 0.11f, 1f);
            _cam.nearClipPlane    = 0.05f;
            _cam.farClipPlane     = 200f;

            SetupLights();
            _lookAtTarget = _pivot.position + Vector3.up * 0.4f;
            UpdateCameraPosition();
            SyncScaleMultiplierFromGameplayIfPresent();
            ConfigurePedestalVisibility();

            // Spawner (we need a MonoBehaviour instance to run coroutines via GLTFast)
            _spawner = gameObject.AddComponent<SculptureSpawner>();
            _spawner.SetSkipMaterialCompatibilityPass(_preserveSourceShadersInCuration);
            _spawner.SetForceGrowthShaderMaterialOverride(!_preserveSourceShadersInCuration);
            Debug.Log($"[CurationViewport] Shader preview mode: preserveSourceShaders={_preserveSourceShadersInCuration}");
        }

        void OnDestroy()
        {
            if (ViewRT != null) ViewRT.Release();
            if (_spawnedPedestalGo != null) Destroy(_spawnedPedestalGo);
        }

        void Update()
        {
            if (_turntableOn && !_orbiting)
                _pivot.Rotate(Vector3.up, TurntableSpeed * Time.deltaTime, Space.World);

            HandleOrbitInput();
        }

        // ── Public API ────────────────────────────────────────────────────────

        public void LoadProp(PropEntry prop)
        {
            InitializeIfNeeded();
            ClearProp();
            if (prop != null)
            {
                int requestId = ++_loadRequestId;
                _activeLoadRoutine = StartCoroutine(LoadPropCoroutine(prop, requestId));
            }
        }

        // Batch validation helper: expose the exact runtime load coroutine for manual pumping.
        public IEnumerator CreateLoadPropCoroutineForDiagnostics(PropEntry prop)
        {
            InitializeIfNeeded();
            ClearProp();
            if (prop == null)
                return null;
            int requestId = ++_loadRequestId;
            return LoadPropCoroutine(prop, requestId);
        }

        public void ClearProp()
        {
            // Pre-empt any in-flight load so its task completion can no longer attach a model.
            if (_activeLoadRoutine != null)
            {
                StopCoroutine(_activeLoadRoutine);
                _activeLoadRoutine = null;
            }
            // Bump the token so any already-running coroutine that has yielded will
            // see a stale id when it resumes and discard its result.
            _loadRequestId++;

            if (_propRoot != null)
            {
                Destroy(_propRoot);
                _propRoot = null;
            }
            // Belt-and-braces: destroy any orphan prop containers that may still be parented
            // under the pivot from earlier loads (e.g. async results that landed during a
            // very fast switch). Lights and pedestal-anchored objects are not children of
            // the pivot, so this only targets prop roots.
            DestroyOrphanedPropContainers();

            _currentShaderSummary = "shader: no model loaded";
            _currentLoadedPropId = "";
            _isLoadInProgress = false;
        }

        // Destroys any non-light children under the pivot. Curation lights are GameObjects
        // named with the "CurationLight_" prefix and are intentionally preserved. Anything
        // else under the pivot is a leftover prop container that must be cleaned up.
        private void DestroyOrphanedPropContainers()
        {
            if (_pivot == null) return;
            for (int i = _pivot.childCount - 1; i >= 0; i--)
            {
                var child = _pivot.GetChild(i);
                if (child == null) continue;
                if (child.name != null && child.name.StartsWith("CurationLight_"))
                    continue;
                Destroy(child.gameObject);
            }
        }

        public void ToggleTurntable() => _turntableOn = !_turntableOn;
        public bool TurntableOn      => _turntableOn;

        // Override the visual scale (does not write to overlay).
        public void SetDisplayScale(float scale)
        {
            if (_propRoot == null) return;
            _propRoot.transform.localScale = Vector3.one * Mathf.Max(0.001f, scale);
            AlignPropToPedestalTop(out _, out _, out _);
            FocusCamera(resetOrbitAndPitch: false);
        }

        /// <param name="resetOrbitAndPitch">When false, only reframes distance/target (e.g. after scale nudge) without resetting user orbit.</param>
        public void FocusCamera(bool resetOrbitAndPitch = true)
        {
            if (_propRoot == null)
            {
                _distance = Mathf.Clamp(_minFramingDistance, _minFramingDistance, _maxFramingDistance);
                _lookAtTarget = _pivot.position + Vector3.up * 0.4f;
                UpdateCameraPosition();
                return;
            }

            var renderers = _propRoot.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                UpdateCameraPosition();
                return;
            }

            var bounds = renderers[0].bounds;
            foreach (var r in renderers)
            {
                if (r != null) bounds.Encapsulate(r.bounds);
            }

            // Only include the slice of the pedestal directly under the prop in the frame,
            // not the entire pedestal extents. The premadePedestal prefab is a wide
            // gallery floor (~17m); encapsulating it whole would push the camera far
            // beyond the prop. Adding the pedestal top point under the prop keeps the
            // visual reference visible without blowing up framing distance.
            if (TryResolvePedestalTopWorldY(out float pedTopY))
            {
                Vector3 propBaseRef = new Vector3(bounds.center.x, pedTopY, bounds.center.z);
                bounds.Encapsulate(propBaseRef);
            }

            _lookAtTarget = bounds.center;
            float size = bounds.extents.magnitude;
            float padded = size * _framingDistanceMultiplier * _framingPaddingScale;
            _distance = Mathf.Clamp(Mathf.Max(padded, _minFramingDistance), _minFramingDistance, _maxFramingDistance);
            if (resetOrbitAndPitch)
            {
                _pitch = 18f;
                _pivot.rotation = Quaternion.identity;
            }
            UpdateCameraPosition();
        }

        // ── Private ───────────────────────────────────────────────────────────

        private void SetupLights()
        {
            AddPointLight("Key",  new Vector3(-2.5f, 3.5f, 2.5f), 4.0f, new Color(1.00f, 0.97f, 0.90f));
            AddPointLight("Fill", new Vector3( 3.0f, 0.8f,-1.5f), 1.8f, new Color(0.65f, 0.75f, 1.00f));
            AddPointLight("Back", new Vector3( 0.0f, 2.5f,-3.5f), 2.4f, Color.white);
        }

        private void AddPointLight(string lightName, Vector3 localPos, float intensity, Color color)
        {
            var go = new GameObject($"CurationLight_{lightName}");
            go.transform.SetParent(_pivot);
            go.transform.localPosition = localPos;
            go.layer = PropLayer;
            var l = go.AddComponent<Light>();
            l.type      = LightType.Point;
            l.range     = 18f;
            l.intensity = intensity;
            l.color     = color;
            l.cullingMask = 1 << PropLayer;
        }

        private IEnumerator LoadPropCoroutine(PropEntry prop, int requestId)
        {
            _isLoadInProgress = true;
            _currentLoadedPropId = prop != null ? prop.Id ?? "" : "";

            // SculptureSpawner takes relative path from StreamingAssets/models
            float scaleMult = PropScaler.ComputeScaleFactor(prop, _globalPlacedScaleMultiplier);

            var task = _spawner.LoadModel(
                glbRelativePath:       prop.GlbPath,
                parent:                _pivot,
                addSculptureController: false,
                addCollider:           false,
                normalizeScale:        false,
                scaleMultiplier:       scaleMult);

            yield return new WaitUntil(() => task.IsCompleted);

            // Stale-result rejection: a newer LoadProp() may have superseded this one
            // while the GLTFast task was running. If so, the task's container (parented
            // to `_pivot`) must be destroyed rather than displayed.
            if (requestId != _loadRequestId)
            {
                if (task.Result != null)
                    Destroy(task.Result);
                Debug.Log($"[CurationViewport] Discarded stale load for '{prop?.Id}' (request {requestId}, current {_loadRequestId}).");
                yield break;
            }

            if (task.IsFaulted || task.Result == null)
            {
                Debug.LogWarning($"CurationViewport: failed to load {prop.Id}: " +
                                 $"{task.Exception?.GetBaseException().Message}");
                _isLoadInProgress = false;
                _activeLoadRoutine = null;
                yield break;
            }

            _propRoot = task.Result;
            SetLayerRecursive(_propRoot, PropLayer);
            RefreshShaderSummary();

            float pedestalTopY = float.NaN;
            float bottomBefore = float.NaN;
            float bottomAfter = float.NaN;
            AlignPropToPedestalTop(out pedestalTopY, out bottomBefore, out bottomAfter);

            FocusCamera();

            Debug.Log($"[CurationViewport] Loaded '{prop.Id}' | scaleMult={_globalPlacedScaleMultiplier:F3} | " +
                      $"pedestalTopY={(float.IsNaN(pedestalTopY) ? "n/a" : pedestalTopY.ToString("F4"))} | " +
                      $"propMinY before={(float.IsNaN(bottomBefore) ? "n/a" : bottomBefore.ToString("F4"))} after={(float.IsNaN(bottomAfter) ? "n/a" : bottomAfter.ToString("F4"))} | " +
                      $"camDist={_distance:F3} | forceMat={_spawner.IsForceGrowthShaderMaterialOverrideEnabled} | skipCompat={_spawner.IsSkippingMaterialCompatibilityPass}");
            _isLoadInProgress = false;
            _activeLoadRoutine = null;
        }

        public bool TryGetLoadedPropBounds(out Bounds bounds)
        {
            bounds = default;
            if (_propRoot == null) return false;
            return TryGetRendererBoundsWorld(_propRoot, out bounds);
        }

        public bool TryGetPedestalTopWorldYForDiagnostics(out float topY)
        {
            return TryResolvePedestalTopWorldY(out topY);
        }

        private void RefreshShaderSummary()
        {
            if (_propRoot == null)
            {
                _currentShaderSummary = "shader: no model loaded";
                return;
            }

            var rendererMats = _propRoot
                .GetComponentsInChildren<Renderer>(true)
                .Where(r => r != null)
                .SelectMany(r => r.sharedMaterials ?? System.Array.Empty<Material>())
                .Where(m => m != null && m.shader != null)
                .Select(m => m.shader.name)
                .ToList();

            if (rendererMats.Count == 0)
            {
                _currentShaderSummary = "shader: none found on renderers";
                return;
            }

            var grouped = rendererMats
                .GroupBy(s => s)
                .OrderByDescending(g => g.Count())
                .Take(4)
                .Select(g => $"{g.Key} ({g.Count()})");

            bool usesUrp = rendererMats.Any(s =>
                s.Contains("Universal Render Pipeline") || s.Contains("URP"));

            _currentShaderSummary = $"shaders: {string.Join(", ", grouped)} | urp-compatible: {(usesUrp ? "yes" : "no")}";

            if (!usesUrp)
                Debug.LogWarning($"CurationViewport: {CurrentShaderSummary}");
        }

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
                SetLayerRecursive(child.gameObject, layer);
        }

        private void HandleOrbitInput()
        {
            // Right-drag to orbit
            if (Input.GetMouseButtonDown(1)) { _orbiting = true; _lastMouse = Input.mousePosition; }
            if (Input.GetMouseButtonUp(1))     _orbiting = false;

            if (_orbiting)
            {
                var delta  = Input.mousePosition - _lastMouse;
                _lastMouse = Input.mousePosition;
                _yaw   += delta.x * OrbitSensitivity;
                _pitch -= delta.y * OrbitSensitivity;
                _pitch  = Mathf.Clamp(_pitch, -70f, 80f);
                _pivot.rotation = Quaternion.identity;   // stop turntable while orbiting
                UpdateCameraPosition();
            }

            // Scroll to zoom
            float scroll = Input.GetAxisRaw("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.001f)
            {
                _distance = Mathf.Clamp(_distance - scroll * 2.5f, 0.15f, 25f);
                UpdateCameraPosition();
            }
        }

        private void UpdateCameraPosition()
        {
            if (_cam == null) return;
            Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 target = _lookAtTarget;
            _cam.transform.position = target + rot * new Vector3(0f, 0f, -_distance);
            _cam.transform.LookAt(target);
        }

        /// <summary>
        /// Moves the loaded prop in world Y so its renderer bounds sit on the pedestal top.
        /// </summary>
        private void AlignPropToPedestalTop(out float pedestalTopY, out float propMinYBefore, out float propMinYAfter)
        {
            pedestalTopY = float.NaN;
            propMinYBefore = float.NaN;
            propMinYAfter = float.NaN;

            if (_propRoot == null) return;

            if (!TryGetRendererBoundsWorld(_propRoot, out Bounds propBounds))
            {
                Debug.LogWarning("[CurationViewport] AlignPropToPedestalTop: no renderers on prop.");
                return;
            }

            propMinYBefore = propBounds.min.y;

            if (!TryResolvePedestalTopWorldY(out float topY))
            {
                Debug.LogWarning("[CurationViewport] AlignPropToPedestalTop: could not resolve pedestal top; keeping spawner grounding.");
                propMinYAfter = propMinYBefore;
                return;
            }

            pedestalTopY = topY;
            float targetMinY = topY + _propPedestalClearance;
            float deltaY = targetMinY - propBounds.min.y;
            _propRoot.transform.position += Vector3.up * deltaY;

            if (!TryGetRendererBoundsWorld(_propRoot, out Bounds after))
            {
                propMinYAfter = propMinYBefore + deltaY;
                return;
            }

            propMinYAfter = after.min.y;
        }

        private bool TryResolvePedestalTopWorldY(out float topY)
        {
            topY = 0f;

            if (_pedestalSurfaceAnchor != null)
            {
                if (TryGetRendererBoundsWorld(_pedestalSurfaceAnchor.gameObject, out Bounds b))
                {
                    topY = b.max.y;
                    return true;
                }

                topY = _pedestalSurfaceAnchor.position.y;
                return true;
            }

            if (_pedestalRoot == null)
                return false;

            if (TryGetRendererBoundsWorld(_pedestalRoot.gameObject, out Bounds pedBounds))
            {
                topY = pedBounds.max.y;
                return true;
            }

            var col = _pedestalRoot.GetComponentInChildren<Collider>(true);
            if (col != null)
            {
                topY = col.bounds.max.y;
                return true;
            }

            return false;
        }

        private static bool TryGetRendererBoundsWorld(GameObject root, out Bounds bounds)
        {
            bounds = default;
            if (root == null) return false;
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0) return false;
            bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                    bounds.Encapsulate(renderers[i].bounds);
            }
            return true;
        }

        private void SyncScaleMultiplierFromGameplayIfPresent()
        {
            var placer = FindFirstObjectByType<PropPlacer>();
            if (placer == null) return;
            _globalPlacedScaleMultiplier = placer.GlobalPlacedScaleMultiplier;
            _propPedestalClearance      = placer.FloorSurfaceOffset;
            Debug.Log($"[CurationViewport] Synced gameplay placement parity: scaleMultiplier={_globalPlacedScaleMultiplier:F3} floorClearance={_propPedestalClearance:F3}");
        }

        private void ConfigurePedestalVisibility()
        {
            if (_pedestalSurfaceAnchor != null && _pedestalRoot == null)
                _pedestalRoot = _pedestalSurfaceAnchor;

            if (_pedestalRoot == null)
                _pedestalRoot = FindPremadePedestalInScene();

            if (_pedestalRoot == null)
                _pedestalRoot = InstantiatePremadePedestal();

            if (_pedestalRoot == null)
            {
                Debug.LogWarning("[CurationViewport] premadePedestal prefab not found in scene or assigned; curation pedestal will be missing.");
                return;
            }

            SetLayerRecursive(_pedestalRoot.gameObject, PropLayer);
            AlignPivotToPedestalCenter();
            Debug.Log($"[CurationViewport] Pedestal active: '{_pedestalRoot.name}' (layer {PropLayer}).");
        }

        // Move the curation pivot to sit directly above the pedestal's XZ centre so the prop
        // (parented to the pivot) spawns over the pedestal regardless of where the pedestal
        // is placed in the scene.
        private void AlignPivotToPedestalCenter()
        {
            if (_pivot == null || _pedestalRoot == null) return;
            if (!TryGetRendererBoundsWorld(_pedestalRoot.gameObject, out Bounds b)) return;
            Vector3 p = _pivot.position;
            _pivot.position = new Vector3(b.center.x, p.y, b.center.z);
        }

        // Locate an existing premadePedestal in the loaded scene. Matches the in-game
        // prefab name token so the curation viewport reuses the very same instance
        // (and its placement) when one is already present.
        private Transform FindPremadePedestalInScene()
        {
            var all = FindObjectsByType<Transform>(FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                var t = all[i];
                if (t == null || t == transform) continue;
                if (t.IsChildOf(transform)) continue; // skip our own previously-spawned instance
                if (t.name.Replace(" ", "").Replace("_", "").ToLowerInvariant().Contains(PremadePedestalNameToken))
                    return t;
            }
            return null;
        }

        // Instantiates the premadePedestal prefab as a child of the viewport so curation
        // shows the exact same pedestal as the in-game placement scene. Repositions the
        // instance so the renderer top sits at viewport origin (Y of `transform.position`),
        // which is the height AlignPropToPedestalTop will then seat the prop on.
        private Transform InstantiatePremadePedestal()
        {
            GameObject prefab = _premadePedestalPrefab;
#if UNITY_EDITOR
            if (prefab == null && _autoResolvePedestalPrefab)
                prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PremadePedestalAssetPath);
#endif
            if (prefab == null)
            {
                Debug.LogWarning($"[CurationViewport] premadePedestal prefab unassigned and could not auto-resolve at '{PremadePedestalAssetPath}'. Drag it into CurationViewport._premadePedestalPrefab.");
                return null;
            }

            var instance = Instantiate(prefab, transform);
            instance.name = "CurationPremadePedestal";
            // Reset to viewport-local origin then offset so the renderer top sits at world Y of viewport.
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;

            // Compute current renderer bounds top in world space, then translate so top.y == transform.position.y.
            if (TryGetRendererBoundsWorld(instance, out Bounds b))
            {
                float deltaY = transform.position.y - b.max.y;
                instance.transform.position += Vector3.up * deltaY;
            }

            // Strip colliders — curation viewport doesn't simulate physics and we don't want
            // the prop placement raycasts in gameplay scenes to ever interact with this preview clone.
            foreach (var col in instance.GetComponentsInChildren<Collider>(true))
            {
                if (col != null) Destroy(col);
            }

            _spawnedPedestalGo = instance;
            Debug.Log($"[CurationViewport] Instantiated '{prefab.name}' as curation pedestal (top seated at viewport origin).");
            return instance.transform;
        }
    }
}

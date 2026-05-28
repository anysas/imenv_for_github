using System.Collections;
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

        // ── Constants ─────────────────────────────────────────────────────────
        private const int  PropLayer      = 29;
        private const int  RtWidth        = 1024;
        private const int  RtHeight       = 768;
        private const float TurntableSpeed = 28f;    // deg/sec
        private const float OrbitSensitivity = 0.35f;

        // ── Internals ─────────────────────────────────────────────────────────
        private Camera       _cam;
        private Transform    _pivot;         // turntable pivot (lights + prop orbit this)
        private GameObject   _propRoot;
        private SculptureSpawner _spawner;

        private bool  _turntableOn  = true;
        private bool  _orbiting     = false;
        private Vector3 _lastMouse;
        private float _yaw          = 0f;
        private float _pitch        = 18f;
        private float _distance     = 3.0f;

        // ── Lifecycle ─────────────────────────────────────────────────────────
        void Awake()
        {
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
            UpdateCameraPosition();

            // Spawner (we need a MonoBehaviour instance to run coroutines via GLTFast)
            _spawner = gameObject.AddComponent<SculptureSpawner>();
        }

        void OnDestroy()
        {
            if (ViewRT != null) ViewRT.Release();
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
            ClearProp();
            if (prop != null)
                StartCoroutine(LoadPropCoroutine(prop));
        }

        public void ClearProp()
        {
            if (_propRoot != null)
            {
                Destroy(_propRoot);
                _propRoot = null;
            }
        }

        public void ToggleTurntable() => _turntableOn = !_turntableOn;
        public bool TurntableOn      => _turntableOn;

        // Override the visual scale (does not write to overlay).
        public void SetDisplayScale(float scale)
        {
            if (_propRoot == null) return;
            _propRoot.transform.localScale = Vector3.one * Mathf.Max(0.001f, scale);
        }

        public void FocusCamera()
        {
            if (_propRoot == null) { _distance = 3.0f; UpdateCameraPosition(); return; }

            var renderers = _propRoot.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) { UpdateCameraPosition(); return; }

            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            _distance = Mathf.Max(bounds.extents.magnitude * 2.8f, 0.4f);
            _pitch = 18f;
            _pivot.rotation = Quaternion.identity;
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

        private IEnumerator LoadPropCoroutine(PropEntry prop)
        {
            // SculptureSpawner takes relative path from StreamingAssets/models
            float scaleMult = prop.ScaleOverride > 0.001f
                ? prop.ScaleOverride
                : PropScaler.ComputeScaleFactor(prop);

            var task = _spawner.LoadModel(
                glbRelativePath:       prop.GlbPath,
                parent:                _pivot,
                addSculptureController: false,
                addCollider:           false,
                normalizeScale:        false,
                scaleMultiplier:       scaleMult);

            yield return new WaitUntil(() => task.IsCompleted);

            if (task.IsFaulted || task.Result == null)
            {
                Debug.LogWarning($"CurationViewport: failed to load {prop.Id}: " +
                                 $"{task.Exception?.GetBaseException().Message}");
                yield break;
            }

            _propRoot = task.Result;
            SetLayerRecursive(_propRoot, PropLayer);
            FocusCamera();
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
            Vector3 target = _pivot.position + Vector3.up * 0.4f;
            _cam.transform.position = target + rot * new Vector3(0f, 0f, -_distance);
            _cam.transform.LookAt(target);
        }
    }
}

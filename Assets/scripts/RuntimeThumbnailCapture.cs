using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace AlgorithmicGallery.Corruption
{
    /// <summary>
    /// Renders a StreamingAssets GLB to a sprite for hotbar icons.
    /// </summary>
    public class RuntimeThumbnailCapture : MonoBehaviour
    {
        [SerializeField] private int _resolution = 128;
        [SerializeField] private float _cameraDistance = 1.6f;
        [SerializeField] private Vector3 _cameraEulerAngles = new Vector3(20f, -25f, 0f);
        [SerializeField] private Color _backgroundColor = new Color(0.12f, 0.12f, 0.14f, 1f);

        private SculptureSpawner _spawner;
        private Camera _captureCamera;
        private Transform _stagingArea;
        private RenderTexture _renderTarget;
        private readonly Dictionary<string, Sprite> _cache = new();
        private bool _busy;
        private readonly Queue<(PropEntry prop, System.Action<Sprite> callback)> _pending = new();
        private readonly HashSet<string> _inFlightIds = new();

        public static RuntimeThumbnailCapture Instance { get; private set; }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
            BuildCaptureRig();
        }

        void OnDestroy()
        {
            if (_renderTarget != null)
                _renderTarget.Release();
            if (Instance == this)
                Instance = null;
        }

        public void RequestThumbnail(PropEntry prop, System.Action<Sprite> callback)
        {
            if (prop == null || callback == null)
                return;

            if (_cache.TryGetValue(prop.Id, out Sprite cached))
            {
                callback(cached);
                return;
            }

            if (_inFlightIds.Contains(prop.Id))
            {
                _pending.Enqueue((prop, callback));
                return;
            }

            _inFlightIds.Add(prop.Id);
            _ = CaptureInternalAsync(prop, callback);
        }

        private async Task CaptureInternalAsync(PropEntry prop, System.Action<Sprite> callback)
        {
            if (_busy)
            {
                _pending.Enqueue((prop, callback));
                _inFlightIds.Remove(prop.Id);
                return;
            }

            _busy = true;
            Sprite sprite = null;

            try
            {
                if (_spawner == null)
                    _spawner = FindFirstObjectByType<AlgorithmicGallery.SculptureSpawner>();
                if (_spawner == null)
                {
                    var host = new GameObject("_ThumbnailSpawner");
                    host.transform.SetParent(transform);
                    _spawner = host.AddComponent<AlgorithmicGallery.SculptureSpawner>();
                }

                float scale = PropScaler.ComputeScaleFactor(prop);
                var model = await _spawner.LoadModel(
                    prop.GlbPath,
                    parent: _stagingArea,
                    addSculptureController: false,
                    addCollider: false,
                    normalizeScale: true,
                    scaleMultiplier: scale);

                if (model != null)
                {
                    FrameModel(model);
                    sprite = RenderToSprite();
                    Destroy(model);
                }

                if (sprite != null)
                    _cache[prop.Id] = sprite;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[RuntimeThumbnailCapture] Capture failed for '{prop.DisplayName}' ({prop.Id}): {ex.Message}");
            }
            finally
            {
                _inFlightIds.Remove(prop.Id);
                _busy = false;
                callback?.Invoke(sprite);
                DrainQueue();
            }
        }

        private void DrainQueue()
        {
            while (!_busy && _pending.Count > 0)
            {
                var (prop, cb) = _pending.Dequeue();
                RequestThumbnail(prop, cb);
                break;
            }
        }

        private void BuildCaptureRig()
        {
            var stage = new GameObject("_ThumbnailStage");
            stage.transform.SetParent(transform);
            stage.transform.position = new Vector3(0f, -2000f, 0f);
            _stagingArea = stage.transform;

            var camGo = new GameObject("_ThumbnailCamera");
            camGo.transform.SetParent(_stagingArea);
            camGo.transform.localPosition = Quaternion.Euler(_cameraEulerAngles) * Vector3.back * _cameraDistance;
            camGo.transform.LookAt(_stagingArea);
            _captureCamera = camGo.AddComponent<Camera>();
            _captureCamera.clearFlags = CameraClearFlags.SolidColor;
            _captureCamera.backgroundColor = _backgroundColor;
            _captureCamera.cullingMask = ~0;
            _captureCamera.nearClipPlane = 0.1f;
            _captureCamera.farClipPlane = 20f;
            _captureCamera.enabled = false;
            _captureCamera.fieldOfView = 35f;

            var keyLightGo = new GameObject("_ThumbnailKeyLight");
            keyLightGo.transform.SetParent(_stagingArea);
            keyLightGo.transform.localPosition = new Vector3(1.5f, 2f, -1f);
            var keyLight = keyLightGo.AddComponent<Light>();
            keyLight.type = LightType.Point;
            keyLight.intensity = 4f;
            keyLight.range = 8f;

            var fillLightGo = new GameObject("_ThumbnailFillLight");
            fillLightGo.transform.SetParent(_stagingArea);
            fillLightGo.transform.localPosition = new Vector3(-1.2f, 1f, 1.5f);
            var fillLight = fillLightGo.AddComponent<Light>();
            fillLight.type = LightType.Point;
            fillLight.intensity = 2f;
            fillLight.range = 8f;

            _renderTarget = new RenderTexture(_resolution, _resolution, 16, RenderTextureFormat.ARGB32);
            _renderTarget.Create();
            _captureCamera.targetTexture = _renderTarget;
        }

        private void FrameModel(GameObject model)
        {
            if (model == null || _captureCamera == null || _stagingArea == null)
                return;

            if (!TryGetBounds(model, out Bounds bounds))
                return;

            Vector3 center = bounds.center;
            float radius = bounds.extents.magnitude;
            float distance = Mathf.Max(radius * 2.2f, _cameraDistance);
            _captureCamera.transform.position = center + Quaternion.Euler(_cameraEulerAngles) * Vector3.back * distance;
            _captureCamera.transform.LookAt(center);
        }

        private Sprite RenderToSprite()
        {
            _captureCamera.Render();
            var tex = new Texture2D(_resolution, _resolution, TextureFormat.RGBA32, false);
            RenderTexture.active = _renderTarget;
            tex.ReadPixels(new Rect(0, 0, _resolution, _resolution), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            return Sprite.Create(tex, new Rect(0, 0, _resolution, _resolution), new Vector2(0.5f, 0.5f), 100f);
        }

        private static bool TryGetBounds(GameObject root, out Bounds bounds)
        {
            bounds = default;
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
                return false;

            bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
            return true;
        }
    }
}

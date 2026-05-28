using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace AlgorithmicGallery.Corruption
{
    // Renders a loaded GLB to a small RenderTexture and converts to a Sprite for hotbar icons.
    // Called on demand from HotbarUI when a slot updates.
    //
    // Notes:
    //   - Captures are cached by prop ID so we don't re-render.
    //   - Uses a hidden camera at a fixed offset; the GLB is loaded into a hidden staging area.
    //   - Captures are 96x96 by default; cheap enough for runtime.
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
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            BuildCaptureRig();
        }

        void OnDestroy()
        {
            if (_renderTarget != null) _renderTarget.Release();
            if (Instance == this) Instance = null;
        }

        private void BuildCaptureRig()
        {
            // Staging area far below the scene
            var stage = new GameObject("_ThumbnailStage");
            stage.transform.SetParent(transform);
            stage.transform.position = new Vector3(0f, -2000f, 0f);
            _stagingArea = stage.transform;

            var camGO = new GameObject("_ThumbnailCamera");
            camGO.transform.SetParent(_stagingArea);
            camGO.transform.localPosition = Quaternion.Euler(_cameraEulerAngles) * Vector3.back * _cameraDistance;
            camGO.transform.LookAt(_stagingArea);
            _captureCamera = camGO.AddComponent<Camera>();
            _captureCamera.clearFlags = CameraClearFlags.SolidColor;
            _captureCamera.backgroundColor = _backgroundColor;
            // Render all layers — staging area at y=-2000 provides spatial isolation.
            // Layer-based culling is unreliable in URP (directional lights ignore cullingMask).
            _captureCamera.cullingMask = ~0;
            _captureCamera.nearClipPlane = 0.1f;
            _captureCamera.farClipPlane = 20f; // tight far plane so scene objects at y=0 are clipped
            _captureCamera.enabled = false;
            _captureCamera.orthographic = false;
            _captureCamera.fieldOfView = 35f;

            // Point lights for readable thumbnails (not directional — avoids URP culling issues)
            var keyLightGO = new GameObject("_ThumbnailKeyLight");
            keyLightGO.transform.SetParent(_stagingArea);
            keyLightGO.transform.localPosition = new Vector3(1.5f, 2f, -1f);
            var keyLight = keyLightGO.AddComponent<Light>();
            keyLight.type = LightType.Point;
            keyLight.intensity = 4.0f;
            keyLight.range = 8f;
            keyLight.color = new Color(1f, 0.97f, 0.92f);

            var fillLightGO = new GameObject("_ThumbnailFillLight");
            fillLightGO.transform.SetParent(_stagingArea);
            fillLightGO.transform.localPosition = new Vector3(-1.2f, 0.8f, 0.5f);
            var fillLight = fillLightGO.AddComponent<Light>();
            fillLight.type = LightType.Point;
            fillLight.intensity = 1.8f;
            fillLight.range = 6f;
            fillLight.color = new Color(0.75f, 0.82f, 1f);

            _renderTarget = new RenderTexture(_resolution, _resolution, 16, RenderTextureFormat.ARGB32);
            _renderTarget.Create();
            _captureCamera.targetTexture = _renderTarget;
        }

        public void RequestThumbnail(PropEntry prop, System.Action<Sprite> callback)
        {
            if (prop == null) { callback?.Invoke(null); return; }

            if (_cache.TryGetValue(prop.Id, out var cached))
            {
                callback?.Invoke(cached);
                return;
            }

            if (_inFlightIds.Contains(prop.Id))
            {
                _pending.Enqueue((prop, callback));
                return;
            }

            _inFlightIds.Add(prop.Id);
            _pending.Enqueue((prop, callback));
            if (!_busy) _ = ProcessQueue();
        }

        private async Task ProcessQueue()
        {
            _busy = true;
            if (_spawner == null) _spawner = FindFirstObjectByType<AlgorithmicGallery.SculptureSpawner>();

            try
            {
                while (_pending.Count > 0)
                {
                    var (prop, callback) = _pending.Dequeue();
                    if (prop == null)
                        continue;

                    if (_cache.TryGetValue(prop.Id, out var cached))
                    {
                        _inFlightIds.Remove(prop.Id);
                        callback?.Invoke(cached);
                        continue;
                    }

                    Sprite sprite = null;
                    try
                    {
                        sprite = await CaptureOne(prop);
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogWarning($"[RuntimeThumbnailCapture] Capture failed for '{prop.DisplayName}' ({prop.Id}): {ex.Message}");
                    }

                    if (sprite == null)
                    {
                        // Never leave the slot blank — fallback icon keeps UX readable.
                        sprite = BuildFallbackSprite(prop);
                    }

                    if (sprite != null)
                        _cache[prop.Id] = sprite;

                    _inFlightIds.Remove(prop.Id);
                    callback?.Invoke(sprite);
                }
            }
            finally
            {
                _busy = false;
            }
        }

        private async Task<Sprite> CaptureOne(PropEntry prop)
        {
            if (_spawner == null)
                _spawner = FindFirstObjectByType<AlgorithmicGallery.SculptureSpawner>();
            if (_spawner == null)
                return null;
            if (_captureCamera == null || _renderTarget == null)
                return null;

            var go = await _spawner.LoadModel(
                prop.GlbPath,
                parent: _stagingArea,
                addSculptureController: false,
                addCollider: false);
            if (go == null) return null;

            go.transform.localPosition = Vector3.zero;

            // Normalize to placed size so thumbnail matches what the player sees in-world.
            PropScaler.Apply(go, prop);

            // Auto-frame: compute bounds and adjust camera distance so the prop fills the frame
            await Task.Yield(); // wait for renderers to initialize
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length > 0)
            {
                Bounds b = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                    b.Encapsulate(renderers[i].bounds);

                // Center the model on the staging area
                Vector3 offset = _stagingArea.position - b.center;
                go.transform.position += offset;

                // Pull camera back based on bounds size so it frames the object
                float extent = Mathf.Max(b.extents.magnitude, 0.1f);
                float dist = extent / Mathf.Tan(_captureCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
                dist = Mathf.Clamp(dist, 0.3f, 8f);
                _captureCamera.transform.localPosition = Quaternion.Euler(_cameraEulerAngles) * Vector3.back * dist;
                _captureCamera.transform.LookAt(_stagingArea);
            }

            // Let materials/shaders settle (GLTFast can take a few frames)
            await Task.Yield();
            await Task.Yield();
            await Task.Yield();

            _captureCamera.enabled = true;
            _captureCamera.Render();
            _captureCamera.enabled = false;

            // Read pixels to Texture2D
            var tex = new Texture2D(_resolution, _resolution, TextureFormat.RGBA32, false);
            var prevActive = RenderTexture.active;
            RenderTexture.active = _renderTarget;
            tex.ReadPixels(new Rect(0, 0, _resolution, _resolution), 0, 0);
            tex.Apply();
            RenderTexture.active = prevActive;

            Destroy(go);
            return Sprite.Create(tex, new Rect(0, 0, _resolution, _resolution), new Vector2(0.5f, 0.5f));
        }

        private Sprite BuildFallbackSprite(PropEntry prop)
        {
            var tex = new Texture2D(_resolution, _resolution, TextureFormat.RGBA32, false);
            int hash = prop?.Id != null ? prop.Id.GetHashCode() : 0;
            float h = Mathf.Abs((hash % 997) / 997f);
            Color baseCol = Color.HSVToRGB(h, 0.45f, 0.85f);
            Color dark = baseCol * 0.45f;

            for (int y = 0; y < _resolution; y++)
            {
                for (int x = 0; x < _resolution; x++)
                {
                    bool border = x < 4 || y < 4 || x >= _resolution - 4 || y >= _resolution - 4;
                    bool checker = ((x / 8) + (y / 8)) % 2 == 0;
                    tex.SetPixel(x, y, border ? Color.black : (checker ? baseCol : dark));
                }
            }

            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, _resolution, _resolution), new Vector2(0.5f, 0.5f));
        }

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform t in go.transform)
                SetLayerRecursive(t.gameObject, layer);
        }
    }
}

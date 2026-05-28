using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace AlgorithmicGallery.Corruption
{
    /// <summary>
    /// Renders assistant/system-placed props on a clean overlay camera while the base camera
    /// keeps the PSX-style filter for the rest of the world.
    /// </summary>
    public class SystemPropCleanOverlaySetup : MonoBehaviour
    {
        public const int SystemPropLayer = 6;
        public const string SystemPropLayerName = "SystemPropsClean";

        private const int CleanRendererIndex = 1;
        private const string OverlayCameraName = "SystemPropsCleanOverlayCamera";

        [SerializeField] private Camera _baseCamera;

        private Camera _overlayCamera;
        private UniversalAdditionalCameraData _baseCameraData;
        private UniversalAdditionalCameraData _overlayCameraData;

        void Awake()
        {
            EnsureSetup();
        }

        void LateUpdate()
        {
            if (_baseCamera == null || _overlayCamera == null)
                EnsureSetup();

            SyncOverlayCamera();
        }

        private void EnsureSetup()
        {
            if (_baseCamera == null)
                _baseCamera = Camera.main;
            if (_baseCamera == null)
                return;

            _baseCameraData = _baseCamera.GetComponent<UniversalAdditionalCameraData>();
            if (_baseCameraData == null)
            {
                Debug.LogWarning("[SystemPropCleanOverlaySetup] Main camera is missing UniversalAdditionalCameraData.");
                return;
            }

            int systemPropMask = 1 << SystemPropLayer;
            _baseCamera.cullingMask &= ~systemPropMask;
            _baseCameraData.renderPostProcessing = true;

            Transform existing = _baseCamera.transform.Find(OverlayCameraName);
            if (existing != null)
                _overlayCamera = existing.GetComponent<Camera>();

            if (_overlayCamera == null)
            {
                var overlayGO = new GameObject(OverlayCameraName);
                overlayGO.transform.SetParent(_baseCamera.transform, false);
                _overlayCamera = overlayGO.AddComponent<Camera>();
            }

            _overlayCameraData = _overlayCamera.GetComponent<UniversalAdditionalCameraData>();
            if (_overlayCameraData == null)
                _overlayCameraData = _overlayCamera.gameObject.AddComponent<UniversalAdditionalCameraData>();

            var listener = _overlayCamera.GetComponent<AudioListener>();
            if (listener != null)
                Destroy(listener);

            SyncOverlayCamera();

            _overlayCameraData.renderType = CameraRenderType.Overlay;
            _overlayCameraData.renderPostProcessing = false;
            TrySetOverlayClearDepth(_overlayCameraData, false);
            _overlayCameraData.volumeLayerMask = 0;
            _overlayCameraData.volumeTrigger = null;
            TrySetRendererIndex(_overlayCameraData, ResolveCleanRendererIndex());

            var stack = _baseCameraData.cameraStack;
            if (!stack.Contains(_overlayCamera))
                stack.Add(_overlayCamera);
        }

        private void SyncOverlayCamera()
        {
            if (_baseCamera == null || _overlayCamera == null)
                return;

            _overlayCamera.transform.localPosition = Vector3.zero;
            _overlayCamera.transform.localRotation = Quaternion.identity;

            _overlayCamera.orthographic = _baseCamera.orthographic;
            _overlayCamera.fieldOfView = _baseCamera.fieldOfView;
            _overlayCamera.orthographicSize = _baseCamera.orthographicSize;
            _overlayCamera.nearClipPlane = _baseCamera.nearClipPlane;
            _overlayCamera.farClipPlane = _baseCamera.farClipPlane;
            _overlayCamera.allowHDR = _baseCamera.allowHDR;
            _overlayCamera.allowMSAA = false;
            _overlayCamera.allowDynamicResolution = _baseCamera.allowDynamicResolution;
            _overlayCamera.clearFlags = CameraClearFlags.Nothing;
            _overlayCamera.cullingMask = 1 << SystemPropLayer;
            _overlayCamera.depth = _baseCamera.depth + 1f;
        }

        private static int ResolveCleanRendererIndex()
        {
            var urpAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urpAsset == null)
                return 0;

            var renderers = urpAsset.rendererDataList;
            if (renderers.Length == 0)
                return 0;

            if (renderers.Length > CleanRendererIndex)
                return CleanRendererIndex;

            return 0;
        }

        private static void TrySetRendererIndex(UniversalAdditionalCameraData cameraData, int rendererIndex)
        {
            if (cameraData == null)
                return;

            var method = typeof(UniversalAdditionalCameraData).GetMethod(
                "SetRenderer",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(int) },
                null);

            if (method != null)
            {
                method.Invoke(cameraData, new object[] { rendererIndex });
                return;
            }

            var field = typeof(UniversalAdditionalCameraData).GetField(
                "m_RendererIndex",
                BindingFlags.Instance | BindingFlags.NonPublic);

            if (field != null)
                field.SetValue(cameraData, rendererIndex);
        }

        private static void TrySetOverlayClearDepth(UniversalAdditionalCameraData cameraData, bool clearDepth)
        {
            if (cameraData == null)
                return;

            var field = typeof(UniversalAdditionalCameraData).GetField(
                "m_ClearDepth",
                BindingFlags.Instance | BindingFlags.NonPublic);

            if (field != null)
                field.SetValue(cameraData, clearDepth);
        }
    }
}

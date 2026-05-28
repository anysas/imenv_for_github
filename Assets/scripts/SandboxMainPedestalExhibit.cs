using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace AlgorithmicGallery.Corruption
{
    /// <summary>
    /// When the sandbox session ends, merges placed props into one exhibit on
    /// <c>glass-case/mainPedestal</c> and shows the player's terminal prompt on <c>pedestalTextBox</c>.
    /// </summary>
    public class SandboxMainPedestalExhibit : MonoBehaviour
    {
        private const string CombinedRootName = "_RuntimeSandboxCombinedModel";
        private const string SterilityCleanProxyName = "_SterilityCleanProxy";

        [Header("References")]
        [SerializeField] private SandboxManager _sandbox;
        [SerializeField] private SandboxPropGridAligner _gridAligner;
        [SerializeField] private Transform _mainPedestal;
        [SerializeField] private TMP_Text _mainPlateText;
        [Tooltip("Extra pause after grid slides finish before building the exhibit.")]
        [SerializeField] private float _settleDelayAfterGridSlides = 0.08f;

        [Header("Scene object names")]
        [SerializeField] private string _mainPedestalHierarchyPath = "glasscase/mainPedestal";
        [SerializeField] private string _mainPedestalObjectName = "mainPedestal";
        [SerializeField] private string _mainPlateTextObjectName = "pedestalTextBox";

        [Header("Layout")]
        [Tooltip("World-space offset applied after snapping to the pedestal top surface.")]
        [SerializeField] private Vector3 _exhibitOffsetFromAnchor = new Vector3(0f, 0.05f, 0f);
        [Tooltip("Max world footprint for the arrangement; only shrinks if the sandbox layout is larger.")]
        [SerializeField] private float _maxFootprintWorldSize = 11f;
        [Tooltip("Half-extent (m) used per prop when measuring layout span from positions.")]
        [SerializeField] private float _layoutBoundsPadding = 0.55f;
        [SerializeField] private bool _usePlayerPlacedPropsOnly = true;

        [Header("Transfer reveal")]
        [Tooltip("Fade in the exhibit via particle transfer toward mainPedestal after build.")]
        [SerializeField] private bool _useTransferReveal = true;
        [SerializeField] private DioramaTransferVfxController _transferVfx;

        [Header("Restart trigger")]
        [Tooltip("Extra trigger padding (metres) added around the glasscase bounds in X/Z.")]
        [SerializeField] private float _restartTriggerExtraXZ = 10f;
        [Tooltip("Extra trigger padding (metres) added above/below the glasscase bounds in Y.")]
        [SerializeField] private float _restartTriggerExtraY = 0.75f;
        [Tooltip("Minimum world height of the restart trigger.")]
        [SerializeField] private float _restartTriggerMinHeight = 2.5f;

        private readonly List<GameObject> _propsScratch = new();
        private readonly List<Transform> _reparentedProps = new();
        private GameObject _combinedRoot;
        private bool _subscribed;

        void Start()
        {
            ResolveReferences();
            Subscribe();
        }

        void OnDestroy()
        {
            Unsubscribe();
        }

        private void ResolveReferences()
        {
            if (_sandbox == null)
                _sandbox = FindFirstObjectByType<SandboxManager>();

            if (_gridAligner == null)
                _gridAligner = FindFirstObjectByType<SandboxPropGridAligner>();

            if (_mainPedestal == null)
            {
                _mainPedestal = SceneHierarchyLookup.FindTransform(
                    _mainPedestalHierarchyPath,
                    _mainPedestalObjectName);
            }

            if (_mainPlateText == null && !string.IsNullOrWhiteSpace(_mainPlateTextObjectName))
            {
                var textGo = GameObject.Find(_mainPlateTextObjectName);
                if (textGo != null)
                    _mainPlateText = textGo.GetComponent<TMP_Text>();
            }

            if (_transferVfx == null)
                _transferVfx = FindFirstObjectByType<DioramaTransferVfxController>();
        }

        /// <summary>Combined exhibit root after session end, or null if not built yet.</summary>
        public GameObject CombinedExhibitRoot => _combinedRoot;

        /// <summary>World anchor on the pedestal top used when placing the exhibit.</summary>
        public Vector3 ExhibitAnchorWorldPoint => GetExhibitAnchorWorldPoint() + _exhibitOffsetFromAnchor;

        private void Subscribe()
        {
            if (_subscribed || _sandbox == null)
                return;

            _sandbox.OnSessionComplete.AddListener(HandleSessionComplete);
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed || _sandbox == null)
                return;

            _sandbox.OnSessionComplete.RemoveListener(HandleSessionComplete);
            _subscribed = false;
        }

        private void HandleSessionComplete()
        {
            ResolveReferences();
            ApplyPromptText();
            StartCoroutine(BuildExhibitAfterGridSettles());
        }

        private IEnumerator BuildExhibitAfterGridSettles()
        {
            if (_gridAligner == null)
                _gridAligner = FindFirstObjectByType<SandboxPropGridAligner>();

            if (_gridAligner != null && _gridAligner.HasActiveSlides)
                yield return _gridAligner.WaitUntilSlidesFinished();

            if (_settleDelayAfterGridSlides > 0f)
                yield return new WaitForSeconds(_settleDelayAfterGridSlides);

            if (BuildExhibit(out Vector3 sourceLayoutCenter))
                PlayTransferRevealIfEnabled(sourceLayoutCenter);

            EnsureDioramaRestartTrigger();
        }

        public void BuildExhibit() => BuildExhibit(out _);

        /// <summary>Builds the combined exhibit; returns false if props or pedestal are missing.</summary>
        public bool BuildExhibit(out Vector3 sourceLayoutCenter)
        {
            sourceLayoutCenter = Vector3.zero;
            ResolveReferences();
            if (_mainPedestal == null)
            {
                Debug.LogWarning("[SandboxMainPedestalExhibit] mainPedestal not found in scene.");
                return false;
            }

            if (!CollectExhibitProps())
            {
                Debug.LogWarning("[SandboxMainPedestalExhibit] No placed props to exhibit.");
                return false;
            }

            if (!TryGetLayoutBoundsFromPositions(_propsScratch, out Bounds sourceLayout))
            {
                Debug.LogWarning("[SandboxMainPedestalExhibit] Could not measure placed prop layout.");
                return false;
            }

            sourceLayoutCenter = sourceLayout.center;

            DestroyExistingCombinedRoot();

            Vector3 pivot = new Vector3(
                sourceLayout.center.x,
                sourceLayout.min.y,
                sourceLayout.center.z);

            _combinedRoot = new GameObject(CombinedRootName);
            _combinedRoot.transform.SetParent(null);
            _combinedRoot.transform.SetPositionAndRotation(pivot, Quaternion.identity);
            _combinedRoot.transform.localScale = Vector3.one;
            _reparentedProps.Clear();

            for (int i = 0; i < _propsScratch.Count; i++)
            {
                GameObject prop = _propsScratch[i];
                if (prop == null)
                    continue;

                prop.SetActive(true);
                PreserveSterilityVisualState(prop);

                Transform t = prop.transform;
                Vector3 worldPos = t.position;
                Quaternion worldRot = t.rotation;
                Vector3 worldScale = t.lossyScale;

                t.SetParent(_combinedRoot.transform, true);
                t.localPosition = worldPos - pivot;
                t.localRotation = worldRot;
                PreserveWorldScale(t, worldScale, _combinedRoot.transform);

                _reparentedProps.Add(t);
            }

            if (!TryGetLayoutBoundsFromTransforms(_reparentedProps, out Bounds layoutBounds))
                layoutBounds = sourceLayout;

            float spanXZ = Mathf.Max(layoutBounds.size.x, layoutBounds.size.z, 0.001f);
            float uniformScale = 1f;
            if (spanXZ > _maxFootprintWorldSize)
                uniformScale = _maxFootprintWorldSize / spanXZ;

            _combinedRoot.transform.localScale = Vector3.one * uniformScale;

            if (!TryGetLayoutBoundsFromTransforms(_reparentedProps, out Bounds scaledLayout))
                scaledLayout = layoutBounds;

            Vector3 anchorPoint = GetExhibitAnchorWorldPoint() + _exhibitOffsetFromAnchor;
            Vector3 exhibitBottomCenter = new Vector3(
                scaledLayout.center.x,
                scaledLayout.min.y,
                scaledLayout.center.z);
            _combinedRoot.transform.position += anchorPoint - exhibitBottomCenter;

            if (TryGetPropsWorldBoundsFromTransforms(_reparentedProps, out Bounds finalBounds))
            {
                Debug.Log(
                    $"[SandboxMainPedestalExhibit] Exhibit on mainPedestal: {_reparentedProps.Count} props, " +
                    $"scale {uniformScale:F3}, anchor {anchorPoint}, world center {finalBounds.center}, size {finalBounds.size}.");
            }
            else
            {
                Debug.Log(
                    $"[SandboxMainPedestalExhibit] Exhibit on mainPedestal: {_reparentedProps.Count} props, " +
                    $"scale {uniformScale:F3}, anchor {anchorPoint}.");
            }

            return true;
        }

        private void PlayTransferRevealIfEnabled(Vector3 sourceLayoutCenter)
        {
            if (!_useTransferReveal || _combinedRoot == null)
                return;

            ResolveReferences();
            if (_transferVfx == null)
                _transferVfx = FindFirstObjectByType<DioramaTransferVfxController>();

            if (_transferVfx == null || !_transferVfx.EffectEnabled)
                return;

            Vector3 target = _transferVfx.ResolvePedestalTargetAnchor();
            _transferVfx.PlayTransfer(_combinedRoot, sourceLayoutCenter, target);
            GameplayEventDebugLog.Push("DioramaTransfer", "started particle transfer to mainPedestal");
        }

        private void EnsureDioramaRestartTrigger()
        {
            if (_mainPedestal == null)
                return;

            const string triggerName = "DioramaRestartTrigger";
            Transform triggerParent = _mainPedestal.parent != null ? _mainPedestal.parent : _mainPedestal;
            Transform existing = triggerParent.Find(triggerName);
            GameObject triggerGo = existing != null ? existing.gameObject : new GameObject(triggerName);

            if (existing == null)
            {
                triggerGo.transform.SetParent(triggerParent, false);
                triggerGo.transform.localPosition = new Vector3(0f, 0.12f, 0f);
                triggerGo.transform.localRotation = Quaternion.identity;
                triggerGo.transform.localScale = Vector3.one;
            }

            var box = triggerGo.GetComponent<BoxCollider>();
            if (box == null)
                box = triggerGo.AddComponent<BoxCollider>();
            box.isTrigger = true;

            // Size trigger to be larger than the enclosing glasscase/parent bounds.
            if (TryGetWorldBoundsFromHierarchy(triggerParent, out Bounds parentBounds))
            {
                Vector3 worldSize = new Vector3(
                    parentBounds.size.x + (_restartTriggerExtraXZ * 2f),
                    Mathf.Max(_restartTriggerMinHeight, parentBounds.size.y + (_restartTriggerExtraY * 2f)),
                    parentBounds.size.z + (_restartTriggerExtraXZ * 2f));

                Vector3 lossy = triggerGo.transform.lossyScale;
                box.size = new Vector3(
                    worldSize.x / Mathf.Max(0.0001f, Mathf.Abs(lossy.x)),
                    worldSize.y / Mathf.Max(0.0001f, Mathf.Abs(lossy.y)),
                    worldSize.z / Mathf.Max(0.0001f, Mathf.Abs(lossy.z)));

                box.center = triggerGo.transform.InverseTransformPoint(parentBounds.center);
            }
            else
            {
                // Fallback if bounds are unavailable.
                box.center = Vector3.zero;
                box.size = new Vector3(5f, 2.5f, 5f);
            }

            var proximity = triggerGo.GetComponent<DioramaProximityTrigger>();
            if (proximity == null)
                proximity = triggerGo.AddComponent<DioramaProximityTrigger>();

            proximity.Arm();
        }

        private static bool TryGetWorldBoundsFromHierarchy(Transform root, out Bounds bounds)
        {
            bounds = default;
            if (root == null)
                return false;

            bool hasBounds = false;

            var colliders = root.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider c = colliders[i];
                if (c == null || !c.enabled || c.isTrigger)
                    continue;

                if (!hasBounds)
                {
                    bounds = c.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(c.bounds);
                }
            }

            if (hasBounds)
                return true;

            var renderers = root.GetComponentsInChildren<Renderer>(true);
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

        public void ApplyPromptText()
        {
            ResolveReferences();
            if (_mainPlateText == null)
            {
                Debug.LogWarning("[SandboxMainPedestalExhibit] pedestalTextBox not found in scene.");
                return;
            }

            string prompt = _sandbox?.SelectedPrompt?.DisplayText;
            if (string.IsNullOrWhiteSpace(prompt))
            {
                _mainPlateText.text = string.Empty;
                return;
            }

            prompt = prompt.Trim();
            if (!prompt.StartsWith("\""))
                prompt = $"\"{prompt}\"";

            _mainPlateText.text = prompt;
            _mainPlateText.gameObject.SetActive(true);
        }

        /// <summary>
        /// Top surface of <c>mainPedestal</c> using mesh bounds (not the separate nameplate object).
        /// </summary>
        private Vector3 GetExhibitAnchorWorldPoint()
        {
            if (SceneHierarchyLookup.TryGetPedestalTopSurface(_mainPedestal, out Vector3 surface))
                return surface;

            return _mainPedestal != null ? _mainPedestal.position : Vector3.zero;
        }

        private bool CollectExhibitProps()
        {
            _propsScratch.Clear();
            if (PropBudget.Instance == null)
                return false;

            if (_usePlayerPlacedPropsOnly)
                PropBudget.Instance.GetTrackedPlayerPlacedProps(_propsScratch);
            else
                PropBudget.Instance.GetTrackedPlacedProps(_propsScratch);

            for (int i = _propsScratch.Count - 1; i >= 0; i--)
            {
                if (_propsScratch[i] == null)
                    _propsScratch.RemoveAt(i);
            }

            return _propsScratch.Count > 0;
        }

        private bool TryGetLayoutBoundsFromPositions(List<GameObject> props, out Bounds bounds)
        {
            bounds = default;
            bool hasBounds = false;
            float pad = _layoutBoundsPadding;

            for (int i = 0; i < props.Count; i++)
            {
                GameObject prop = props[i];
                if (prop == null)
                    continue;

                if (!EncapsulatePoint(ref bounds, ref hasBounds, prop.transform.position, pad))
                    continue;
            }

            return hasBounds;
        }

        private bool TryGetLayoutBoundsFromTransforms(List<Transform> props, out Bounds bounds)
        {
            bounds = default;
            bool hasBounds = false;
            float pad = _layoutBoundsPadding;

            for (int i = 0; i < props.Count; i++)
            {
                Transform t = props[i];
                if (t == null)
                    continue;

                Vector3 worldPos = t.parent == _combinedRoot.transform
                    ? _combinedRoot.transform.TransformPoint(t.localPosition)
                    : t.position;

                if (!EncapsulatePoint(ref bounds, ref hasBounds, worldPos, pad))
                    continue;
            }

            return hasBounds;
        }

        private static bool EncapsulatePoint(ref Bounds bounds, ref bool hasBounds, Vector3 center, float padding)
        {
            var pointBounds = new Bounds(center, Vector3.one * (padding * 2f));
            if (!hasBounds)
            {
                bounds = pointBounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(pointBounds.min);
                bounds.Encapsulate(pointBounds.max);
            }

            return true;
        }

        private static void PreserveWorldScale(Transform target, Vector3 worldScale, Transform newParent)
        {
            Vector3 parentScale = newParent.lossyScale;
            target.localScale = new Vector3(
                worldScale.x / Mathf.Max(0.0001f, parentScale.x),
                worldScale.y / Mathf.Max(0.0001f, parentScale.y),
                worldScale.z / Mathf.Max(0.0001f, parentScale.z));
        }

        private static bool TryGetPropsWorldBoundsFromTransforms(List<Transform> props, out Bounds bounds)
        {
            bounds = default;
            bool hasBounds = false;

            for (int i = 0; i < props.Count; i++)
            {
                Transform t = props[i];
                if (t == null)
                    continue;

                if (!TryEncapsulateRenderers(t.gameObject, ref bounds, ref hasBounds))
                    continue;
            }

            return hasBounds;
        }

        private static bool TryEncapsulateRenderers(GameObject root, ref Bounds bounds, ref bool hasBounds)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
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

            return hasBounds;
        }

        private static void PreserveSterilityVisualState(GameObject root)
        {
            if (root == null)
                return;

            Transform cleanProxy = root.transform.Find(SterilityCleanProxyName);
            if (cleanProxy != null)
            {
                // Keep hallway exhibit in the same final clean state reached at session end:
                // disable processed renderers and render only the clean proxy.
                SetLayerRecursive(cleanProxy.gameObject, SystemPropCleanOverlaySetup.SystemPropLayer);

                var proxyRenderers = root.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < proxyRenderers.Length; i++)
                {
                    Renderer renderer = proxyRenderers[i];
                    if (renderer == null)
                        continue;

                    bool isCleanRenderer = renderer.transform == cleanProxy
                        || renderer.transform.IsChildOf(cleanProxy);
                    renderer.enabled = isCleanRenderer;
                }

                return;
            }

            // Non-sterilized fallback.
            var allRenderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < allRenderers.Length; i++)
            {
                if (allRenderers[i] != null)
                    allRenderers[i].enabled = true;
            }
        }

        private static void SetLayerRecursive(GameObject root, int layer)
        {
            if (root == null)
                return;

            root.layer = layer;
            foreach (Transform child in root.transform)
                SetLayerRecursive(child.gameObject, layer);
        }

        private void DestroyExistingCombinedRoot()
        {
            var existing = _combinedRoot != null ? _combinedRoot : GameObject.Find(CombinedRootName);
            if (existing == null)
                return;

            _combinedRoot = null;
            Destroy(existing);
        }
    }
}

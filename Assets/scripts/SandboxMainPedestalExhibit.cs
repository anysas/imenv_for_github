using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace AlgorithmicGallery.Corruption
{
    /// <summary>
    /// When the sandbox session ends, merges placed props into one exhibit on
    /// <c>mainPedestal</c> and shows the player's terminal prompt on <c>mainPlateTextBox</c>.
    /// </summary>
    public class SandboxMainPedestalExhibit : MonoBehaviour
    {
        private const string CombinedRootName = "_RuntimeSandboxCombinedModel";

        [Header("References")]
        [SerializeField] private SandboxManager _sandbox;
        [SerializeField] private Transform _mainPedestal;
        [SerializeField] private TMP_Text _mainPlateText;

        [Header("Scene object names")]
        [SerializeField] private string _mainPedestalObjectName = "mainPedestal";
        [SerializeField] private string _mainPlateTextObjectName = "mainPlateTextBox";

        [Header("Layout")]
        [Tooltip("World-space offset applied after snapping to the pedestal top surface.")]
        [SerializeField] private Vector3 _exhibitOffsetFromAnchor = new Vector3(0f, 0.05f, 0f);
        [SerializeField] private float _surfaceRaycastHeight = 80f;
        [SerializeField] private float _surfaceRaycastDistance = 160f;
        [Tooltip("Max world footprint for the arrangement; only shrinks if the sandbox layout is larger.")]
        [SerializeField] private float _maxFootprintWorldSize = 11f;
        [Tooltip("Half-extent (m) used per prop when measuring layout span from positions.")]
        [SerializeField] private float _layoutBoundsPadding = 0.55f;
        [SerializeField] private bool _usePlayerPlacedPropsOnly = true;

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

            if (_mainPedestal == null && !string.IsNullOrWhiteSpace(_mainPedestalObjectName))
            {
                var pedestalGo = GameObject.Find(_mainPedestalObjectName);
                if (pedestalGo != null)
                    _mainPedestal = pedestalGo.transform;
            }

            if (_mainPlateText == null && !string.IsNullOrWhiteSpace(_mainPlateTextObjectName))
            {
                var textGo = GameObject.Find(_mainPlateTextObjectName);
                if (textGo != null)
                    _mainPlateText = textGo.GetComponent<TMP_Text>();
            }
        }

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
            BuildExhibit();
            ApplyPromptText();
        }

        public void BuildExhibit()
        {
            ResolveReferences();
            if (_mainPedestal == null)
            {
                Debug.LogWarning("[SandboxMainPedestalExhibit] mainPedestal not found in scene.");
                return;
            }

            if (!CollectExhibitProps())
            {
                Debug.LogWarning("[SandboxMainPedestalExhibit] No placed props to exhibit.");
                return;
            }

            if (!TryGetLayoutBoundsFromPositions(_propsScratch, out Bounds sourceLayout))
            {
                Debug.LogWarning("[SandboxMainPedestalExhibit] Could not measure placed prop layout.");
                return;
            }

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
                EnsureRenderersEnabled(prop);

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
        }

        public void ApplyPromptText()
        {
            ResolveReferences();
            if (_mainPlateText == null)
            {
                Debug.LogWarning("[SandboxMainPedestalExhibit] mainPlateTextBox not found in scene.");
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
        /// Top surface of <c>mainPedestal</c> at its transform XZ (not the separate nameplate object).
        /// </summary>
        private Vector3 GetExhibitAnchorWorldPoint()
        {
            if (TryGetPedestalTopSurfaceAtTransform(out Vector3 surface))
                return surface;

            Vector3 anchor = _mainPedestal.position;
            if (TryGetPedestalRendererTopY(out float topY))
                anchor.y = topY;

            return anchor;
        }

        private bool TryGetPedestalTopSurfaceAtTransform(out Vector3 surface)
        {
            surface = _mainPedestal.position;
            Collider pedestalCollider = _mainPedestal.GetComponent<Collider>();
            if (pedestalCollider == null)
                return false;

            Vector3 sampleXz = new Vector3(_mainPedestal.position.x, 0f, _mainPedestal.position.z);
            float rayStartY = Mathf.Max(
                pedestalCollider.bounds.max.y + 2f,
                _mainPedestal.position.y + _surfaceRaycastHeight);
            Vector3 origin = new Vector3(sampleXz.x, rayStartY, sampleXz.z);
            var ray = new Ray(origin, Vector3.down);

            if (pedestalCollider.Raycast(ray, out RaycastHit hit, _surfaceRaycastDistance))
            {
                surface = hit.point;
                return true;
            }

            RaycastHit[] hits = Physics.RaycastAll(ray, _surfaceRaycastDistance);
            float bestY = float.NegativeInfinity;
            bool found = false;
            for (int i = 0; i < hits.Length; i++)
            {
                Collider col = hits[i].collider;
                if (col == null || !IsMainPedestalCollider(col))
                    continue;

                if (hits[i].point.y <= bestY)
                    continue;

                bestY = hits[i].point.y;
                surface = hits[i].point;
                found = true;
            }

            return found;
        }

        private bool IsMainPedestalCollider(Collider col)
        {
            if (col == null || _mainPedestal == null)
                return false;

            Transform hitTransform = col.transform;
            return hitTransform == _mainPedestal || hitTransform.IsChildOf(_mainPedestal);
        }

        private bool TryGetPedestalRendererTopY(out float topY)
        {
            topY = _mainPedestal.position.y;
            bool found = false;

            var renderers = _mainPedestal.GetComponents<Renderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !renderer.enabled)
                    continue;

                if (!found)
                {
                    topY = renderer.bounds.max.y;
                    found = true;
                }
                else
                {
                    topY = Mathf.Max(topY, renderer.bounds.max.y);
                }
            }

            return found;
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

        private static void EnsureRenderersEnabled(GameObject root)
        {
            if (root == null)
                return;

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                    renderers[i].enabled = true;
            }
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

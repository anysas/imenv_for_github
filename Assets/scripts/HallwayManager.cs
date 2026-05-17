using System.Collections.Generic;
using UnityEngine;

namespace AlgorithmicGallery.Corruption
{
    /// <summary>
    /// Keeps the hallway pedestals empty by clearing any runtime diorama content.
    /// </summary>
    public class HallwayManager : MonoBehaviour
    {
        private const string RuntimeDioramaRootName = "_RuntimeDiorama";

        [Header("Scene anchors")]
        [Tooltip("Root containing pedestal anchor child GameObjects. If null, searches by name 'HallwayPedestals'.")]
        [SerializeField] private Transform _pedestalsRoot;
        [Tooltip("Number of pedestals to spawn if no explicit anchor children are present.")]
        [SerializeField] private int _autoPedestalCount = 3;
        [Tooltip("Spacing between auto-generated pedestals along the local Z axis.")]
        [SerializeField] private float _autoPedestalSpacing = 2.5f;

        private SandboxManager _sandbox;

        void Start()
        {
            ResolvePedestalsRoot();
            ClearAllPedestals();

            _sandbox = FindFirstObjectByType<SandboxManager>();
            if (_sandbox != null)
                _sandbox.OnSessionComplete.AddListener(ClearAllPedestals);
        }

        void OnDestroy()
        {
            if (_sandbox != null)
                _sandbox.OnSessionComplete.RemoveListener(ClearAllPedestals);
        }

        private void ResolvePedestalsRoot()
        {
            if (_pedestalsRoot != null)
                return;

            var go = GameObject.Find("HallwayPedestals");
            if (go != null)
                _pedestalsRoot = go.transform;
        }

        private void ClearAllPedestals()
        {
            foreach (var anchor in CollectOrCreateAnchors())
                ClearRuntimeDioramaChildren(anchor);
        }

        // ── Anchor resolution ──────────────────────────────────────────────

        private List<Transform> CollectOrCreateAnchors()
        {
            var anchors = new List<Transform>();

            if (_pedestalsRoot != null && _pedestalsRoot.childCount > 0)
            {
                for (int i = 0; i < _pedestalsRoot.childCount; i++)
                    anchors.Add(_pedestalsRoot.GetChild(i));
                return anchors;
            }

            // No explicit anchors: generate them along this object's forward axis
            Transform parent = _pedestalsRoot != null ? _pedestalsRoot : transform;
            for (int i = 0; i < _autoPedestalCount; i++)
            {
                var anchor = new GameObject($"PedestalAnchor_{i}");
                anchor.transform.SetParent(parent, worldPositionStays: false);
                anchor.transform.localPosition = new Vector3(0f, 0f, i * _autoPedestalSpacing);
                anchors.Add(anchor.transform);
            }
            return anchors;
        }

        private static void ClearRuntimeDioramaChildren(Transform anchor)
        {
            if (anchor == null) return;
            for (int i = anchor.childCount - 1; i >= 0; i--)
            {
                var child = anchor.GetChild(i);
                if (child != null && child.name == RuntimeDioramaRootName)
                    Destroy(child.gameObject);
            }
        }

    }
}

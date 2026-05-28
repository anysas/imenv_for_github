using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace AlgorithmicGallery.Corruption
{
    // Pre-warms a subset of GLBs into hidden, deactivated GameObjects so the first placement
    // doesn't cause a hitch. Called from the hallway phase or at boot.
    //
    // Caveat: GLBs are unique per file; we don't reuse instances. The "pre-warm" effect is
    // really about loading textures/meshes into Unity's caches early. Activated instances
    // will still be cloned via SculptureSpawner.LoadModel — but that's now hot.
    public class PropPool : MonoBehaviour
    {
        [SerializeField] private int _prewarmCount = 30;
        [SerializeField] private SculptureSpawner _spawner;

        private readonly List<GameObject> _prewarmed = new();

        public bool IsWarmedUp { get; private set; }

        public async Task PrewarmAsync(CuratedPropManifest manifest)
        {
            if (IsWarmedUp) return;
            if (_spawner == null) _spawner = GetComponent<SculptureSpawner>();
            if (_spawner == null) _spawner = FindFirstObjectByType<AlgorithmicGallery.SculptureSpawner>();
            if (_spawner == null)
            {
                Debug.LogWarning("[PropPool] No SculptureSpawner; skipping prewarm.");
                IsWarmedUp = true;
                return;
            }

            // Container is far below world origin so prewarmed instances are invisible
            var container = new GameObject("_PropPoolPrewarmed");
            container.transform.position = new Vector3(0f, -1000f, 0f);
            container.transform.SetParent(transform);

            int target = Mathf.Min(_prewarmCount, manifest.Count);
            for (int i = 0; i < target; i++)
            {
                var prop = manifest.GetRandom();
                var go = await _spawner.LoadModel(
                    prop.GlbPath,
                    parent: container.transform,
                    addSculptureController: false,
                    addCollider: false);
                if (go != null)
                {
                    go.SetActive(false);
                    _prewarmed.Add(go);
                }
            }

            IsWarmedUp = true;
            Debug.Log($"[PropPool] Prewarmed {_prewarmed.Count} props.");
        }

        public void Clear()
        {
            foreach (var go in _prewarmed)
                if (go != null) Destroy(go);
            _prewarmed.Clear();
            IsWarmedUp = false;
        }
    }
}

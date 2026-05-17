using System;
using System.IO;
using System.Threading.Tasks;
using AlgorithmicGallery.Corruption;
using GLTFast;
using UnityEngine;

namespace AlgorithmicGallery
{
    /// <summary>
    /// Loads .glb/.gltf props from <see cref="Application.streamingAssetsPath"/> at runtime
    /// (e.g. StreamingAssets/RandomObjects). GLTFast is only the file loader — all assets
    /// come from your StreamingAssets folders.
    /// </summary>
    public class SculptureSpawner : MonoBehaviour
    {
        private static readonly int EmissiveFactorProperty = Shader.PropertyToID("emissiveFactor");

        [Header("Configuration")]
        [SerializeField] private bool _normalizeModelScale = true;
        [Tooltip("Uniformly scales each model so its largest dimension matches this size in world units.")]
        [SerializeField] private float _targetMaxDimension = 3.8f;
        [SerializeField] private float _minScaleFactor = 0.05f;
        [SerializeField] private float _maxScaleFactor = 5f;
        [SerializeField] private bool _centerAndGroundModel = true;

        private bool _skipMaterialCompatibilityPass;
        private bool _forceGrowthShaderMaterialOverride;

        public bool IsSkippingMaterialCompatibilityPass => _skipMaterialCompatibilityPass;
        public bool IsForceGrowthShaderMaterialOverrideEnabled => _forceGrowthShaderMaterialOverride;

        public void SetSkipMaterialCompatibilityPass(bool skip) => _skipMaterialCompatibilityPass = skip;
        public void SetForceGrowthShaderMaterialOverride(bool force) => _forceGrowthShaderMaterialOverride = force;

        /// <summary>
        /// Loads a model from StreamingAssets and parents it under <paramref name="parent"/>.
        /// </summary>
        public async Task<GameObject> LoadModel(
            string glbRelativePath,
            Transform parent = null,
            bool addSculptureController = true,
            bool addCollider = true,
            bool normalizeScale = true,
            float scaleMultiplier = 1f)
        {
            if (string.IsNullOrWhiteSpace(glbRelativePath))
            {
                Debug.LogError("SculptureSpawner: Invalid glb path provided.");
                return null;
            }

            string fullPath = StreamingAssetModelPaths.ResolveFullPath(glbRelativePath);
            if (string.IsNullOrEmpty(fullPath))
            {
                Debug.LogError(
                    $"SculptureSpawner: Model file not found for '{glbRelativePath}' " +
                    $"(looked under StreamingAssets and StreamingAssets/models).");
                return null;
            }

            // Parent only after scale is finalized — parenting first applies the pedestal's
            // non-uniform scale and squashes props (flattened on Y).
            var container = new GameObject(Path.GetFileNameWithoutExtension(fullPath));

            var gltfImport = new GltfImport();
            bool loadSuccess;
            try
            {
                byte[] data = await Task.Run(() => File.ReadAllBytes(fullPath));
                loadSuccess = await gltfImport.LoadGltfBinary(data, new Uri(fullPath));
            }
            catch (Exception ex)
            {
                Debug.LogError($"SculptureSpawner: Failed reading '{fullPath}': {ex.Message}");
                Destroy(container);
                return null;
            }

            if (container == null)
                return null;

            if (!loadSuccess)
            {
                Debug.LogError($"SculptureSpawner: Failed to load glTF from {fullPath}");
                Destroy(container);
                return null;
            }

            bool instantiateSuccess = await gltfImport.InstantiateMainSceneAsync(container.transform);
            if (container == null)
                return null;

            if (!instantiateSuccess)
            {
                Debug.LogError($"SculptureSpawner: Failed to instantiate glTF scene for {fullPath}");
                Destroy(container);
                return null;
            }

            if (!_skipMaterialCompatibilityPass)
                ApplyMaterialCompatibility(container);

            DisableImportedCollidersUnder(container.transform);

            if (!TryGetRendererBounds(container, out Bounds bounds))
            {
                Debug.LogWarning($"SculptureSpawner: No renderers found for {glbRelativePath}");
            }
            else
            {
                if (normalizeScale && _normalizeModelScale)
                {
                    float largest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
                    if (largest > 0.0001f)
                    {
                        float targetScale = _targetMaxDimension / largest;
                        targetScale = Mathf.Clamp(targetScale, _minScaleFactor, _maxScaleFactor);
                        container.transform.localScale = Vector3.one * targetScale;
                        TryGetRendererBounds(container, out bounds);
                    }
                }

                if (!Mathf.Approximately(scaleMultiplier, 1f))
                    container.transform.localScale *= scaleMultiplier;

                if (_centerAndGroundModel)
                    CenterAndGround(container, bounds);
            }

            if (parent != null)
                container.transform.SetParent(parent, worldPositionStays: true);

            bool shouldEnsureCollider = addCollider || addSculptureController;
            if (shouldEnsureCollider)
                EnsureBoxCollider(container);

            if (addSculptureController)
            {
                var sculptureController = container.GetComponent<SculptureController>();
                if (sculptureController == null)
                    sculptureController = container.AddComponent<SculptureController>();
                if (sculptureController == null)
                    Debug.LogError($"SculptureSpawner: Failed to add SculptureController for {glbRelativePath}");
            }

            return container;
        }

        private void ApplyMaterialCompatibility(GameObject root)
        {
            if (root == null)
                return;

            Shader urpLit = Shader.Find("Universal Render Pipeline/Lit")
                         ?? Shader.Find("Universal Render Pipeline/Simple Lit");
            if (urpLit == null)
                return;

            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null)
                    continue;

                var mats = renderer.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var mat = mats[i];
                    if (mat == null || mat.shader == null)
                        continue;

                    string shaderName = mat.shader.name;
                    if (shaderName.Contains("Error") || shaderName.Contains("Hidden/InternalErrorShader"))
                    {
                        var replacement = new Material(urpLit);
                        if (mat.HasProperty("_BaseColor"))
                            replacement.color = mat.GetColor("_BaseColor");
                        else if (mat.HasProperty("_Color"))
                            replacement.color = mat.GetColor("_Color");
                        mats[i] = replacement;
                    }
                }

                renderer.sharedMaterials = mats;
            }

            if (_forceGrowthShaderMaterialOverride)
            {
                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer == null)
                        continue;
                    foreach (var mat in renderer.sharedMaterials)
                    {
                        if (mat != null && mat.HasProperty(EmissiveFactorProperty))
                            mat.SetVector(EmissiveFactorProperty, Vector3.zero);
                    }
                }
            }
        }

        private static void DisableImportedCollidersUnder(Transform root)
        {
            if (root == null)
                return;

            foreach (var col in root.GetComponentsInChildren<Collider>(true))
                col.enabled = false;
        }

        private static void EnsureBoxCollider(GameObject root)
        {
            if (root == null)
                return;

            if (!TryGetRendererBounds(root, out Bounds bounds))
                return;

            var box = root.GetComponent<BoxCollider>();
            if (box == null)
                box = root.AddComponent<BoxCollider>();

            box.center = root.transform.InverseTransformPoint(bounds.center);
            Vector3 lossy = root.transform.lossyScale;
            box.size = new Vector3(
                bounds.size.x / Mathf.Max(lossy.x, 0.0001f),
                bounds.size.y / Mathf.Max(lossy.y, 0.0001f),
                bounds.size.z / Mathf.Max(lossy.z, 0.0001f));
            box.enabled = true;
        }

        private static bool TryGetRendererBounds(GameObject root, out Bounds bounds)
        {
            bounds = default;
            if (root == null)
                return false;

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
                return false;

            // Use mesh-local extents so parent scale does not skew normalization.
            Bounds combined = default;
            bool hasBounds = false;
            foreach (var renderer in renderers)
            {
                if (renderer == null)
                    continue;

                Bounds meshBounds = renderer.localBounds;
                Vector3 worldCenter = renderer.transform.TransformPoint(meshBounds.center);
                Vector3 worldExtents = renderer.transform.TransformVector(meshBounds.extents);
                var worldBox = new Bounds(worldCenter, worldExtents * 2f);

                if (!hasBounds)
                {
                    combined = worldBox;
                    hasBounds = true;
                }
                else
                {
                    combined.Encapsulate(worldBox.min);
                    combined.Encapsulate(worldBox.max);
                }
            }

            if (!hasBounds)
                return false;

            bounds = combined;
            return true;
        }

        private static void CenterAndGround(GameObject root, Bounds worldBounds)
        {
            if (root == null)
                return;

            Vector3 offset = root.transform.position - worldBounds.center;
            offset.y = root.transform.position.y - worldBounds.min.y;
            root.transform.position -= offset;
        }
    }
}

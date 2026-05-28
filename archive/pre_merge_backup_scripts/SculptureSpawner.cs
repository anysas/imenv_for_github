using UnityEngine;
using GLTFast;
using System.IO;
using System.Threading.Tasks;

namespace AlgorithmicGallery
{
    /// <summary>
    /// Handles loading .glb models at runtime using GLTFast and setting up sculpture instances.
    /// Asynchronously loads glTF files, attaches required components, and configures colliders.
    /// </summary>
    public class SculptureSpawner : MonoBehaviour
    {
        private static readonly int EmissiveFactorProperty = Shader.PropertyToID("emissiveFactor");

        [Header("Configuration")]
        [SerializeField]
        private bool _normalizeModelScale = true;
        [Tooltip("Uniformly scales each model so its largest dimension matches this size in world units.")]
        [SerializeField]
        private float _targetMaxDimension = 1.2f;
        [SerializeField]
        private float _minScaleFactor = 0.05f;
        [SerializeField]
        private float _maxScaleFactor = 5f;
        [SerializeField]
        private bool _centerAndGroundModel = true;
        [Tooltip("Optional offset above pedestal origin after grounding.")]
        [SerializeField]
        private float _groundYOffset = 0f;
        [Header("Material Compatibility")]
        [Tooltip("Optional shader/material override to ensure growth MPB properties are supported.")]
        [SerializeField]
        private Material _growthShaderMaterialTemplate;
        [SerializeField]
        private bool _forceGrowthShaderMaterialOverride = true;
        [SerializeField]
        private bool _copyBaseTextureFromSource = true;
        [SerializeField]
        private bool _skipMaterialCompatibilityPass = false;

        // Runtime API for context-specific material behavior (e.g. curation preview vs gameplay).
        public void SetForceGrowthShaderMaterialOverride(bool enabled) => _forceGrowthShaderMaterialOverride = enabled;
        public bool IsForceGrowthShaderMaterialOverrideEnabled => _forceGrowthShaderMaterialOverride;
        public void SetSkipMaterialCompatibilityPass(bool enabled) => _skipMaterialCompatibilityPass = enabled;
        public bool IsSkippingMaterialCompatibilityPass => _skipMaterialCompatibilityPass;

        private void Awake()
        {
            // Scene serialization currently contains stale tiny-scale values; force runtime-safe prototype values.
            _targetMaxDimension = 1.2f;
            _maxScaleFactor = 5f;
            _forceGrowthShaderMaterialOverride = true;
        }

        /// <summary>
        /// Asynchronously loads a model from a .glb file path and returns a configured GameObject.
        /// The GameObject will have a SculptureController and BoxCollider attached.
        /// </summary>
        /// <param name="glbRelativePath">Relative path from StreamingAssets/models, e.g. "portal/props/oildrum001.glb"</param>
        /// <param name="parent">Transform to parent the loaded model under (optional)</param>
        /// <param name="addSculptureController">If true, adds SculptureController to the root container.</param>
        /// <param name="addCollider">If true, adds BoxCollider sized to renderer bounds.</param>
        /// <returns>Configured GameObject with SculptureController, or null if loading failed</returns>
        public async Task<GameObject> LoadModel(
            string glbRelativePath,
            Transform parent = null,
            bool addSculptureController = true,
            bool addCollider = true,
            bool normalizeScale = true,
            float scaleMultiplier = 1f)
        {
            if (string.IsNullOrEmpty(glbRelativePath))
            {
                Debug.LogError("SculptureSpawner: Invalid glb path provided.");
                return null;
            }

            string fullPath = Path.Combine(Application.streamingAssetsPath, "models", glbRelativePath);

            // Note on Android/Quest: File.Exists won't work with StreamingAssets.
            // Use UnityWebRequest instead. For now, desktop prototype uses File API.
            if (!File.Exists(fullPath))
            {
                Debug.LogError($"SculptureSpawner: Model file not found at {fullPath}");
                return null;
            }

            // Create a container for the loaded model
            GameObject container = new GameObject(Path.GetFileNameWithoutExtension(glbRelativePath));
            if (parent != null)
            {
                container.transform.SetParent(parent);
            }
            container.transform.localPosition = Vector3.zero;
            container.transform.localRotation = Quaternion.identity;

            // Load the glTF model
            GltfImport gltfImport = new GltfImport();
            bool loadSuccess = await gltfImport.Load(fullPath);
            if (container == null)
                return null;

            if (!loadSuccess)
            {
                Debug.LogError($"SculptureSpawner: Failed to load glTF model from {fullPath}");
                Destroy(container);
                return null;
            }

            // Instantiate the loaded glTF into the container.
            // This matches the GLTFast API variant available in this project.
            bool instantiateSuccess = await gltfImport.InstantiateMainSceneAsync(container.transform);
            if (container == null)
                return null;

            if (!instantiateSuccess)
            {
                Debug.LogError($"SculptureSpawner: Failed to instantiate glTF scene");
                Destroy(container);
                return null;
            }

            if (!_skipMaterialCompatibilityPass)
                ApplyGrowthMaterialCompatibility(container);
            DisableImportedCollidersUnder(container.transform);

            if (!TryGetRendererBounds(container, out Bounds bounds))
            {
                Debug.LogWarning($"SculptureSpawner: No renderers found for model {glbRelativePath}");
                return container;
            }

            if (_normalizeModelScale && normalizeScale)
            {
                float largestDimension = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
                if (largestDimension > 0.0001f)
                {
                    float targetScale = _targetMaxDimension / largestDimension;
                    targetScale = Mathf.Clamp(targetScale, _minScaleFactor, _maxScaleFactor);
                    container.transform.localScale = Vector3.one * targetScale;
                    TryGetRendererBounds(container, out bounds);
                }
            }

            if (!Mathf.Approximately(scaleMultiplier, 1f))
            {
                container.transform.localScale *= Mathf.Max(0.0001f, scaleMultiplier);
                TryGetRendererBounds(container, out bounds);
            }

            if (_centerAndGroundModel)
            {
                Vector3 offset = new Vector3(
                    container.transform.position.x - bounds.center.x,
                    container.transform.position.y - bounds.min.y + _groundYOffset,
                    container.transform.position.z - bounds.center.z
                );

                for (int i = 0; i < container.transform.childCount; i++)
                {
                    container.transform.GetChild(i).position += offset;
                }

                TryGetRendererBounds(container, out bounds);
            }

            // Add or update collider
            BoxCollider collider = container.GetComponent<BoxCollider>();
            bool shouldEnsureCollider = addCollider || addSculptureController;
            if (collider == null && shouldEnsureCollider)
            {
                collider = container.AddComponent<BoxCollider>();
            }

            if (collider != null && shouldEnsureCollider)
            {
                // Size collider to fit renderer bounds
                collider.center = bounds.center - container.transform.position;
                collider.size = bounds.size;
                collider.isTrigger = true; // Non-blocking for traversal; still usable for triggers.
            }

            // Add SculptureController after collider exists (required by [RequireComponent(typeof(Collider))]).
            if (addSculptureController)
            {
                var sculptureController = container.GetComponent<SculptureController>();
                if (sculptureController == null)
                    sculptureController = container.AddComponent<SculptureController>();
                if (sculptureController == null)
                {
                    Debug.LogError($"SculptureSpawner: Failed to add SculptureController for {glbRelativePath}");
                    Destroy(container);
                    return null;
                }
            }

            return container;
        }

        private static void DisableImportedCollidersUnder(Transform root)
        {
            if (root == null)
                return;

            for (int i = 0; i < root.childCount; i++)
            {
                foreach (var col in root.GetChild(i).GetComponentsInChildren<Collider>(true))
                    col.enabled = false;
            }
        }

        private void ApplyGrowthMaterialCompatibility(GameObject container)
        {
            if (container == null)
                return;

            var renderers = container.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                    continue;

                var shared = renderer.sharedMaterials;
                bool changed = false;
                for (int matIdx = 0; matIdx < shared.Length; matIdx++)
                {
                    Material source = shared[matIdx];
                    if (source == null)
                        continue;

                    if (_growthShaderMaterialTemplate != null)
                    {
                        bool needsOverride = _forceGrowthShaderMaterialOverride || !MaterialSupportsGrowthProperties(source);
                        if (!needsOverride)
                            continue;

                        var replacement = new Material(_growthShaderMaterialTemplate);
                        if (_copyBaseTextureFromSource)
                        {
                            CopyMainTexture(source, replacement);
                        }
                        replacement.EnableKeyword("_EMISSIVE");
                        replacement.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                        if (replacement.HasProperty(EmissiveFactorProperty))
                            replacement.SetColor(EmissiveFactorProperty, Color.black);

                        shared[matIdx] = replacement;
                        changed = true;
                        continue;
                    }

                    // Template is unassigned: normalize source materials to support runtime emission/color modulation.
                    Material runtimeMaterial = renderer.materials[matIdx];
                    if (runtimeMaterial == null)
                        continue;

                    runtimeMaterial.EnableKeyword("_EMISSIVE");
                    runtimeMaterial.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                    if (runtimeMaterial.HasProperty(EmissiveFactorProperty))
                        runtimeMaterial.SetColor(EmissiveFactorProperty, Color.black);
                }

                if (changed)
                {
                    renderer.sharedMaterials = shared;
                }
            }
        }

        private static bool MaterialSupportsGrowthProperties(Material material)
        {
            if (material == null)
                return false;

            return material.HasProperty("_Saturation")
                   && material.HasProperty("_EmissionPower")
                   && material.HasProperty("_DissolveAmount");
        }

        private static void CopyMainTexture(Material source, Material destination)
        {
            if (source == null || destination == null)
                return;

            const string baseMap = "_BaseMap";
            const string mainTex = "_MainTex";

            if (source.HasProperty(baseMap) && source.GetTexture(baseMap) != null && destination.HasProperty(baseMap))
            {
                destination.SetTexture(baseMap, source.GetTexture(baseMap));
            }
            else if (source.HasProperty(mainTex) && source.GetTexture(mainTex) != null)
            {
                if (destination.HasProperty(mainTex))
                {
                    destination.SetTexture(mainTex, source.GetTexture(mainTex));
                }
                else if (destination.HasProperty(baseMap))
                {
                    destination.SetTexture(baseMap, source.GetTexture(mainTex));
                }
            }
        }

        private static bool TryGetRendererBounds(GameObject root, out Bounds bounds)
        {
            var renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers == null || renderers.Length == 0)
            {
                bounds = default;
                return false;
            }

            bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }
            return true;
        }
    }
}

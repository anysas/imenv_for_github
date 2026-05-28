using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace AlgorithmicGallery.Corruption
{
    /// <summary>
    /// Drains placed props toward sterile white over the session.
    /// Older placements drain first; newer ones join as the session advances.
    /// PSX-processed and clean overlay representations crossfade during mid/late sterility.
    /// </summary>
    public class PlacedPropSterilizationController : MonoBehaviour
    {
        public static PlacedPropSterilizationController Instance { get; private set; }

        private const string CleanProxyRootName = "_SterilityCleanProxy";
        private const string WhiteOverlayRootName = "_SterilityWhiteOverlay";

        private static Material s_WhiteOverlayMaterialTemplate;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int MetallicId = Shader.PropertyToID("_Metallic");
        private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private const string SterileMaterialResourcePath = "PropSterileWhite";
        private static Material s_SterileMaterialTemplate;
        private static readonly List<GameObject> s_PendingPlayerProps = new();
        /// <summary>Fired when a prop's clean overlay representation becomes dominant (SFX sync).</summary>
        public event Action<GameObject> OnPropSterilizationStarted;

        [Header("References")]
        [SerializeField] private SandboxManager _sandbox;
        [SerializeField] private PropPlacer _placer;

        [Header("Drain ramp")]
        [SerializeField] private float _blendSpeed = 2f;
        [Tooltip("Brightness multiplier at full sterility (toward white).")]
        [SerializeField] private float _clearBrightnessMultiplier = 1.55f;
        [Tooltip("Saturation at full sterility (0 = fully desaturated).")]
        [SerializeField, Range(0f, 1f)] private float _clearSaturation = 0f;
        [Tooltip("Extra white wash through emission (scaled by drain strength, not raw sterility).")]
        [SerializeField, Range(0f, 3f)] private float _maxEmissionWhiten = 0.85f;
        [Tooltip("Off by default — overlay crossfade needs a second camera and often fails in player builds.")]
        [SerializeField] private bool _useCleanOverlayCrossfade;

        [Header("White opacity overlay")]
        [Tooltip("Keeps original materials and fades a transparent white layer from 0 to 100% opacity.")]
        [SerializeField] private bool _useWhiteOpacityOverlay = true;

        [Header("SFX")]
        [Tooltip("If false, session-complete forced whitening will NOT trigger per-prop sterilization SFX (prevents a loud stacked burst).")]
        [SerializeField] private bool _playSterilizationSfxOnSessionComplete = false;

        [Header("Age-weighted targeting")]
        [Tooltip("When enabled, older placements reach full white slightly sooner than newer ones.")]
        [SerializeField] private bool _staggerDrainByPlacementAge = false;
        [Tooltip("Extra sterility applied to the oldest placement when stagger is enabled.")]
        [SerializeField, Range(0f, 0.35f)] private float _maxAgeSterilityLead = 0.15f;

        [Header("Transition bands")]
        [Tooltip("Player placement count before any sterility drain starts.")]
        [SerializeField, Min(0)] private int _drainStartPlacement = 4;
        [Tooltip("Sterility where visible colour drain begins.")]
        [SerializeField, Range(0f, 0.5f)] private float _drainStart = 0.02f;
        [Tooltip("Sterility where colour drain reaches full strength.")]
        [SerializeField, Range(0.5f, 1f)] private float _drainEnd = 1f;
        [Tooltip("Sterility where clean overlay representation begins fading in.")]
        [SerializeField, Range(0f, 0.9f)] private float _crossfadeStart = 0.4f;
        [Tooltip("Sterility where processed representation is fully replaced by clean.")]
        [SerializeField, Range(0.1f, 1f)] private float _crossfadeEnd = 0.72f;

        private readonly List<TrackedProp> _tracked = new();
        private readonly List<GameObject> _propsScratch = new();
        private readonly HashSet<GameObject> _seenScratch = new();
        private bool _subscribed;
        private float _lastSyncedGlobalProgress = -1f;
        private int _nextPlacementIndex;

        private sealed class TrackedProp
        {
            public GameObject Root;
            public int PlacementIndex;
            public int OriginalLayer;
            public float CurrentSterility;
            public float TargetSterility;
            public bool PendingSterilizationSfx;
            public bool SterilizationSfxPlayed;
            public bool HasCleanProxy;
            public GameObject CleanProxyRoot;
            public bool HasWhiteOverlay;
            public GameObject WhiteOverlayRoot;
            public readonly List<RendererSlot> ProcessedSlots = new();
            public readonly List<RendererSlot> CleanSlots = new();
        }

        private sealed class RendererSlot
        {
            public Renderer Renderer;
            public Transform SourceTransform;
            public Material[] Originals;
            public Material[] RuntimeInstances;
            public MaterialPropertyBlock PropertyBlock;
            public Renderer WhiteOverlayRenderer;
            public Material[] WhiteOverlayMaterials;
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[PlacedPropSterilization] Duplicate controller destroyed.");
                Destroy(this);
                return;
            }

            Instance = this;
            GetSterileMaterialTemplate();
        }

        void OnEnable()
        {
            if (Instance == null)
                Instance = this;

            ResolveReferences();
            Subscribe();
        }

        void Start()
        {
            ResolveReferences();
            Subscribe();
            FlushPendingPlayerProps();
        }

        void Update()
        {
            if (_sandbox == null || !_sandbox.SandboxActive)
                return;

            RefreshTrackedPropsFromBudget();

            SyncGlobalTargetsIfChanged();

            if (_tracked.Count == 0)
                return;

            for (int i = _tracked.Count - 1; i >= 0; i--)
            {
                TrackedProp tracked = _tracked[i];
                if (tracked.Root == null)
                {
                    DisposeTrackedProp(tracked);
                    _tracked.RemoveAt(i);
                    continue;
                }

                if (!Mathf.Approximately(tracked.CurrentSterility, tracked.TargetSterility))
                {
                    tracked.CurrentSterility = Mathf.MoveTowards(
                        tracked.CurrentSterility,
                        tracked.TargetSterility,
                        Time.deltaTime * _blendSpeed);
                }

                ApplySterility(tracked, tracked.CurrentSterility);
            }
        }

        void OnDestroy()
        {
            Unsubscribe();
            for (int i = 0; i < _tracked.Count; i++)
                DisposeTrackedProp(_tracked[i]);
            _tracked.Clear();

            if (Instance == this)
                Instance = null;
        }

        /// <summary>Call immediately after a player prop is registered in <see cref="PropBudget"/>.</summary>
        public static void NotifyPlayerPropPlaced(GameObject placedRoot)
        {
            if (placedRoot == null)
                return;

            if (Instance == null)
                Instance = FindFirstObjectByType<PlacedPropSterilizationController>();

            if (Instance == null)
            {
                if (!s_PendingPlayerProps.Contains(placedRoot))
                    s_PendingPlayerProps.Add(placedRoot);
                return;
            }

            Instance.RegisterPlayerPropPlaced(placedRoot);
        }

        private void FlushPendingPlayerProps()
        {
            if (s_PendingPlayerProps.Count == 0)
                return;

            for (int i = 0; i < s_PendingPlayerProps.Count; i++)
                RegisterPlayerPropPlaced(s_PendingPlayerProps[i]);

            s_PendingPlayerProps.Clear();
        }

        private void RegisterPlayerPropPlaced(GameObject placedRoot)
        {
            if (placedRoot == null)
                return;

            ResolveReferences();

            if (FindTracked(placedRoot) == null)
            {
                int placementIndex = _nextPlacementIndex + 1;
                if (_sandbox?.StyleProfile != null && _sandbox.StyleProfile.PlayerPlacementCount > 0)
                    placementIndex = _sandbox.StyleProfile.PlayerPlacementCount;

                RegisterProp(placedRoot, placementIndex);
            }

            if (_sandbox != null && _sandbox.SandboxActive)
                SyncGlobalTargetsIfChanged(force: true);
        }

        /// <summary>Called by <see cref="SandboxManager"/> after bootstrap so references are wired immediately.</summary>
        public void EnsureReferences(PropPlacer placer, SandboxManager sandbox)
        {
            if (placer != null)
                _placer = placer;
            if (sandbox != null)
                _sandbox = sandbox;

            ResolveReferences();
            Subscribe();
        }

        private void ResolveReferences()
        {
            if (_sandbox == null)
                _sandbox = FindFirstObjectByType<SandboxManager>();
            if (_placer == null)
                _placer = FindFirstObjectByType<PropPlacer>();
        }

        private void Subscribe()
        {
            if (_subscribed)
                return;

            ResolveReferences();
            if (_placer != null)
                _placer.OnPropPlacedWithContext += HandlePropPlaced;
            if (_sandbox != null)
            {
                _sandbox.OnSandboxEntered.AddListener(HandleSandboxEntered);
                _sandbox.OnSessionComplete.AddListener(HandleSessionComplete);
            }

            _subscribed = _placer != null || _sandbox != null;
        }

        private void Unsubscribe()
        {
            if (!_subscribed)
                return;

            if (_placer != null)
                _placer.OnPropPlacedWithContext -= HandlePropPlaced;
            if (_sandbox != null)
            {
                _sandbox.OnSandboxEntered.RemoveListener(HandleSandboxEntered);
                _sandbox.OnSessionComplete.RemoveListener(HandleSessionComplete);
            }

            _subscribed = false;
        }

        private void HandleSandboxEntered()
        {
            for (int i = 0; i < _tracked.Count; i++)
                DisposeTrackedProp(_tracked[i]);
            _tracked.Clear();
            _lastSyncedGlobalProgress = -1f;
            _nextPlacementIndex = 0;
        }

        private void HandleSessionComplete()
        {
            bool playSfx = _playSterilizationSfxOnSessionComplete;
            for (int i = 0; i < _tracked.Count; i++)
                ForceFullyClear(_tracked[i], playSfx);
        }

        private void HandlePropPlaced(bool isPlayer, PropEntry prop, Vector3 worldPosition)
        {
            if (!isPlayer || _sandbox == null || !_sandbox.SandboxActive)
                return;

            StartCoroutine(ProcessPlacementDrain());
        }

        private IEnumerator ProcessPlacementDrain()
        {
            yield return null;

            RefreshTrackedPropsFromBudget();
            SyncGlobalTargetsIfChanged(force: true);
        }

        private float GetGlobalSterilityProgress()
        {
            // Drive whitening from PLAYER placement count only, so progression is stable
            // and doesn't jump due to assistant/system prop counts.
            int placementCount = 0;
            if (_sandbox?.StyleProfile != null)
                placementCount = _sandbox.StyleProfile.PlayerPlacementCount;
            else if (PropBudget.Instance != null)
            {
                _propsScratch.Clear();
                PropBudget.Instance.GetTrackedPlayerPlacedProps(_propsScratch);
                placementCount = _propsScratch.Count;
            }

            if (placementCount <= 0)
                return 0f;

            int max = Mathf.Max(1, _sandbox != null ? _sandbox.MaxPlayerPlacements : 20);

            int startPlacement = Mathf.Clamp(_drainStartPlacement, 0, Mathf.Max(0, max - 1));
            if (placementCount <= startPlacement)
                return 0f;

            int activeWindow = Mathf.Max(1, max - startPlacement);
            return Mathf.Clamp01((placementCount - startPlacement) / (float)activeWindow);
        }

        private void GetPlacementIndexRange(out int minPlacement, out int maxPlacement)
        {
            minPlacement = int.MaxValue;
            maxPlacement = 0;

            for (int i = 0; i < _tracked.Count; i++)
            {
                int idx = _tracked[i].PlacementIndex;
                if (idx <= 0)
                    continue;

                if (idx < minPlacement)
                    minPlacement = idx;
                if (idx > maxPlacement)
                    maxPlacement = idx;
            }

            if (minPlacement == int.MaxValue)
                minPlacement = maxPlacement = 0;
        }

        private float ComputeTargetSterility(int placementIndex, float global, int minPlacement, int maxPlacement)
        {
            if (!_staggerDrainByPlacementAge || global <= 0.001f)
                return global;

            float ageNorm = 0f;
            if (placementIndex > 0 && maxPlacement > minPlacement)
            {
                ageNorm = 1f - (placementIndex - minPlacement) / (float)(maxPlacement - minPlacement);
                ageNorm = Mathf.Clamp01(ageNorm);
            }

            // Keep every prop on the same drain curve; age only adds a small lead for older props.
            float ageLead = ageNorm * _maxAgeSterilityLead * global;
            return Mathf.Clamp01(global + ageLead);
        }

        private void SyncGlobalTargetsIfChanged(bool force = false)
        {
            float progress = GetGlobalSterilityProgress();
            if (!force && Mathf.Approximately(progress, _lastSyncedGlobalProgress))
                return;

            _lastSyncedGlobalProgress = progress;
            GetPlacementIndexRange(out int minPlacement, out int maxPlacement);

            for (int i = 0; i < _tracked.Count; i++)
            {
                TrackedProp tracked = _tracked[i];
                tracked.TargetSterility = ComputeTargetSterility(
                    tracked.PlacementIndex,
                    progress,
                    minPlacement,
                    maxPlacement);
            }
        }

        private void RefreshTrackedPropsFromBudget()
        {
            _propsScratch.Clear();
            PropBudget.Instance?.GetTrackedPlayerPlacedProps(_propsScratch);

            _seenScratch.Clear();
            for (int i = 0; i < _propsScratch.Count; i++)
            {
                GameObject go = _propsScratch[i];
                if (go == null || !_seenScratch.Add(go))
                    continue;

                if (FindTracked(go) == null)
                    RegisterProp(go, i + 1);
            }
        }

        private void ForceFullyClear(TrackedProp tracked, bool playSterilizationSfx)
        {
            if (tracked == null || tracked.Root == null)
                return;

            tracked.TargetSterility = 1f;
            tracked.CurrentSterility = 1f;
            if (playSterilizationSfx && !tracked.SterilizationSfxPlayed)
                tracked.PendingSterilizationSfx = true;
            else
                tracked.PendingSterilizationSfx = false;

            ApplySterility(tracked, 1f);
        }

        private void RaiseSterilizationStarted(GameObject root)
        {
            if (root == null)
                return;

            OnPropSterilizationStarted?.Invoke(root);
            SandboxGameplaySfx.NotifyPropSterilizationStarted(root);
        }

        private TrackedProp FindTracked(GameObject root)
        {
            for (int i = 0; i < _tracked.Count; i++)
            {
                if (_tracked[i].Root == root)
                    return _tracked[i];
            }

            return null;
        }

        private void RegisterProp(GameObject root, int placementIndex)
        {
            if (root == null)
                return;

            var tracked = new TrackedProp
            {
                Root = root,
                PlacementIndex = placementIndex,
                OriginalLayer = root.layer,
                CurrentSterility = 0f,
                TargetSterility = 0f
            };

            EnsureRootOnProcessedLayer(tracked);

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || renderer is ParticleSystemRenderer or TrailRenderer or LineRenderer)
                    continue;

                if (IsUnderSterilityInternalChild(renderer.transform))
                    continue;

                if (!TryGetSharedMesh(renderer, out _) && renderer is not MeshRenderer and not SkinnedMeshRenderer)
                    continue;

                if (!TryCreateRendererSlot(renderer, out RendererSlot slot))
                    continue;

                renderer.SetPropertyBlock(null);
                tracked.ProcessedSlots.Add(slot);
            }

            if (tracked.ProcessedSlots.Count == 0)
            {
                Debug.LogWarning(
                    $"[PlacedPropSterilization] No renderable meshes on \"{root.name}\" — cannot apply sterility drain.");
                return;
            }

            if (placementIndex <= 0)
            {
                _nextPlacementIndex++;
                placementIndex = _nextPlacementIndex;
            }
            else
            {
                _nextPlacementIndex = Mathf.Max(_nextPlacementIndex, placementIndex);
            }

            tracked.PlacementIndex = placementIndex;
            tracked.HasWhiteOverlay = _useWhiteOpacityOverlay && TryBuildWhiteOverlay(tracked);
            tracked.HasCleanProxy = !_useWhiteOpacityOverlay && _useCleanOverlayCrossfade && TryBuildCleanProxy(tracked);
            _tracked.Add(tracked);

            float global = GetGlobalSterilityProgress();
            GetPlacementIndexRange(out int minPlacement, out int maxPlacement);
            tracked.TargetSterility = ComputeTargetSterility(
                tracked.PlacementIndex,
                global,
                minPlacement,
                maxPlacement);

            ApplySterility(tracked, tracked.CurrentSterility);
        }

        private static bool TryCreateRendererSlot(Renderer renderer, out RendererSlot slot)
        {
            slot = null;
            if (renderer == null)
                return false;

            Material[] originals = renderer.sharedMaterials;
            if (originals == null || originals.Length == 0)
                return false;

            var copies = new Material[originals.Length];
            var runtime = new Material[originals.Length];
            for (int m = 0; m < originals.Length; m++)
            {
                copies[m] = originals[m];
                runtime[m] = CreateRuntimeMaterial(originals[m]);
            }

            slot = new RendererSlot
            {
                Renderer = renderer,
                SourceTransform = renderer.transform,
                Originals = copies,
                RuntimeInstances = runtime,
                PropertyBlock = new MaterialPropertyBlock()
            };
            return true;
        }

        private bool TryBuildCleanProxy(TrackedProp tracked)
        {
            if (tracked?.Root == null || tracked.ProcessedSlots.Count == 0)
                return false;

            var proxyRoot = new GameObject(CleanProxyRootName);
            proxyRoot.transform.SetParent(tracked.Root.transform, false);
            proxyRoot.transform.localPosition = Vector3.zero;
            proxyRoot.transform.localRotation = Quaternion.identity;
            proxyRoot.transform.localScale = Vector3.one;
            SetLayerRecursive(proxyRoot, SystemPropCleanOverlaySetup.SystemPropLayer);

            for (int i = 0; i < tracked.ProcessedSlots.Count; i++)
            {
                RendererSlot processed = tracked.ProcessedSlots[i];
                if (processed.SourceTransform == null)
                    continue;

                if (!TryGetSharedMesh(processed.Renderer, out Mesh sourceMesh))
                    continue;

                var cleanGo = new GameObject(processed.SourceTransform.name + "_Clean");
                cleanGo.transform.SetParent(proxyRoot.transform, true);
                cleanGo.transform.position = processed.SourceTransform.position;
                cleanGo.transform.rotation = processed.SourceTransform.rotation;
                cleanGo.transform.localScale = processed.SourceTransform.lossyScale;
                cleanGo.layer = SystemPropCleanOverlaySetup.SystemPropLayer;

                var meshFilter = cleanGo.AddComponent<MeshFilter>();
                meshFilter.sharedMesh = sourceMesh;

                var meshRenderer = cleanGo.AddComponent<MeshRenderer>();
                meshRenderer.shadowCastingMode = processed.Renderer.shadowCastingMode;
                meshRenderer.receiveShadows = processed.Renderer.receiveShadows;
                // Important: inherit original URP/glTF materials, otherwise Unity assigns a fallback
                // built-in material that renders magenta under URP.
                meshRenderer.sharedMaterials = processed.Originals;

                if (!TryCreateRendererSlot(meshRenderer, out RendererSlot cleanSlot))
                {
                    Destroy(cleanGo);
                    continue;
                }

                cleanSlot.SourceTransform = processed.SourceTransform;
                tracked.CleanSlots.Add(cleanSlot);
            }

            tracked.CleanProxyRoot = proxyRoot;
            proxyRoot.SetActive(false);
            return tracked.CleanSlots.Count > 0;
        }

        private bool TryBuildWhiteOverlay(TrackedProp tracked)
        {
            if (tracked?.Root == null || tracked.ProcessedSlots.Count == 0)
                return false;

            var overlayRoot = new GameObject(WhiteOverlayRootName);
            overlayRoot.transform.SetParent(tracked.Root.transform, false);
            overlayRoot.transform.localPosition = Vector3.zero;
            overlayRoot.transform.localRotation = Quaternion.identity;
            overlayRoot.transform.localScale = Vector3.one;

            for (int i = 0; i < tracked.ProcessedSlots.Count; i++)
            {
                RendererSlot processed = tracked.ProcessedSlots[i];
                if (processed.SourceTransform == null || processed.Renderer == null)
                    continue;

                if (!TryGetSharedMesh(processed.Renderer, out Mesh sourceMesh))
                    continue;

                var overlayGo = new GameObject(processed.SourceTransform.name + "_WhiteOverlay");
                overlayGo.transform.SetParent(overlayRoot.transform, true);
                overlayGo.transform.position = processed.SourceTransform.position;
                overlayGo.transform.rotation = processed.SourceTransform.rotation;
                overlayGo.transform.localScale = processed.SourceTransform.lossyScale;
                overlayGo.layer = tracked.Root.layer;

                var meshFilter = overlayGo.AddComponent<MeshFilter>();
                meshFilter.sharedMesh = sourceMesh;

                var meshRenderer = overlayGo.AddComponent<MeshRenderer>();
                meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
                meshRenderer.receiveShadows = false;

                int submeshCount = GetSubmeshCount(processed.Renderer, sourceMesh);
                var whiteMats = new Material[submeshCount];
                bool createdAnyMat = false;
                for (int sm = 0; sm < submeshCount; sm++)
                {
                    whiteMats[sm] = CreateWhiteOverlayMaterialInstance();
                    if (whiteMats[sm] != null)
                        createdAnyMat = true;
                }

                if (!createdAnyMat)
                {
                    Destroy(overlayGo);
                    continue;
                }

                meshRenderer.sharedMaterials = whiteMats;

                if (!TryCreateRendererSlot(meshRenderer, out RendererSlot overlaySlot))
                {
                    Destroy(overlayGo);
                    for (int sm = 0; sm < whiteMats.Length; sm++)
                    {
                        if (whiteMats[sm] != null)
                            Destroy(whiteMats[sm]);
                    }
                    continue;
                }

                processed.WhiteOverlayRenderer = meshRenderer;
                processed.WhiteOverlayMaterials = overlaySlot.RuntimeInstances;
            }

            tracked.WhiteOverlayRoot = overlayRoot;
            overlayRoot.SetActive(false);

            for (int i = 0; i < tracked.ProcessedSlots.Count; i++)
            {
                if (tracked.ProcessedSlots[i].WhiteOverlayRenderer != null)
                    return true;
            }

            Destroy(overlayRoot);
            tracked.WhiteOverlayRoot = null;
            return false;
        }

        private static int GetSubmeshCount(Renderer renderer, Mesh mesh)
        {
            if (mesh != null)
                return Mathf.Max(1, mesh.subMeshCount);

            if (renderer != null && renderer.sharedMaterials != null && renderer.sharedMaterials.Length > 0)
                return renderer.sharedMaterials.Length;

            return 1;
        }

        private void ApplyWhiteOpacityOverlay(TrackedProp tracked, float t)
        {
            for (int i = 0; i < tracked.ProcessedSlots.Count; i++)
            {
                RendererSlot slot = tracked.ProcessedSlots[i];
                if (slot.Renderer == null || slot.Originals == null)
                    continue;

                if (!slot.Renderer.enabled)
                    slot.Renderer.enabled = true;

                slot.Renderer.sharedMaterials = slot.Originals;
                slot.Renderer.SetPropertyBlock(null);
            }

            float opacity = Mathf.SmoothStep(0f, 1f, t);
            bool showOverlay = opacity > 0.001f;

            if (tracked.WhiteOverlayRoot != null && tracked.WhiteOverlayRoot.activeSelf != showOverlay)
                tracked.WhiteOverlayRoot.SetActive(showOverlay);

            if (!showOverlay)
                return;

            for (int i = 0; i < tracked.ProcessedSlots.Count; i++)
            {
                RendererSlot slot = tracked.ProcessedSlots[i];
                if (slot.WhiteOverlayRenderer == null)
                    continue;

                Transform source = slot.SourceTransform != null ? slot.SourceTransform : slot.Renderer.transform;
                Transform overlayTransform = slot.WhiteOverlayRenderer.transform;
                overlayTransform.position = source.position;
                overlayTransform.rotation = source.rotation;
                SetWorldScale(overlayTransform, source.lossyScale);

                slot.WhiteOverlayRenderer.enabled = true;

                if (slot.WhiteOverlayMaterials == null || slot.WhiteOverlayMaterials.Length == 0)
                    continue;

                for (int m = 0; m < slot.WhiteOverlayMaterials.Length; m++)
                    SetMaterialAlpha(slot.WhiteOverlayMaterials[m], opacity);

                slot.WhiteOverlayRenderer.sharedMaterials = slot.WhiteOverlayMaterials;
            }
        }

        private static Material GetWhiteOverlayMaterialTemplate()
        {
            if (s_WhiteOverlayMaterialTemplate != null)
                return s_WhiteOverlayMaterialTemplate;

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");
            if (shader == null)
                return null;

            var mat = new Material(shader) { name = "SterilityWhiteOverlayTemplate" };
            ConfigureTransparentWhiteMaterial(mat, 0f);
            s_WhiteOverlayMaterialTemplate = mat;
            return s_WhiteOverlayMaterialTemplate;
        }

        private static Material CreateWhiteOverlayMaterialInstance()
        {
            Material template = GetWhiteOverlayMaterialTemplate();
            if (template == null)
                return null;

            var instance = new Material(template) { name = "SterilityWhiteOverlay (Instance)" };
            ConfigureTransparentWhiteMaterial(instance, 0f);
            return instance;
        }

        private static void ConfigureTransparentWhiteMaterial(Material mat, float alpha)
        {
            if (mat == null)
                return;

            alpha = Mathf.Clamp01(alpha);
            Color c = new Color(1f, 1f, 1f, alpha);

            if (mat.HasProperty(BaseColorId))
                mat.SetColor(BaseColorId, c);
            if (mat.HasProperty(ColorId))
                mat.SetColor(ColorId, c);

            if (mat.HasProperty("_Surface"))
                mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend"))
                mat.SetFloat("_Blend", 0f);

            mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.renderQueue = (int)RenderQueue.Transparent;

            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        }

        private static void SetMaterialAlpha(Material mat, float alpha)
        {
            if (mat == null)
                return;

            alpha = Mathf.Clamp01(alpha);
            if (mat.HasProperty(BaseColorId))
            {
                Color c = mat.GetColor(BaseColorId);
                c.a = alpha;
                mat.SetColor(BaseColorId, c);
            }

            if (mat.HasProperty(ColorId))
            {
                Color c = mat.GetColor(ColorId);
                c.a = alpha;
                mat.SetColor(ColorId, c);
            }
        }

        private void ApplySterility(TrackedProp tracked, float t)
        {
            if (tracked?.Root == null)
                return;

            t = Mathf.Clamp01(t);
            EnsureRootOnProcessedLayer(tracked);

            if (_useWhiteOpacityOverlay && tracked.HasWhiteOverlay)
            {
                ApplyWhiteOpacityOverlay(tracked, t);

                bool playOverlaySfx = tracked.PendingSterilizationSfx
                    && !tracked.SterilizationSfxPlayed
                    && t >= 0.5f;

                if (playOverlaySfx)
                {
                    tracked.PendingSterilizationSfx = false;
                    tracked.SterilizationSfxPlayed = true;
                    StartCoroutine(PlaySterilizationSfxAfterVisualSnap(tracked.Root));
                }

                return;
            }

            float drainStrength = GetDrainStrength(t);
            float brightness = GetBrightnessForSterility(drainStrength);
            float saturation = GetSaturationForSterility(drainStrength);
            float emissionWhiten = Mathf.Pow(drainStrength, 1.65f) * Mathf.Max(0f, _maxEmissionWhiten);

            float processedWeight = 1f - Mathf.SmoothStep(_crossfadeStart, _crossfadeEnd, t);
            float cleanWeight = Mathf.SmoothStep(_crossfadeStart, _crossfadeEnd, t);

            if (t <= 0.001f)
            {
                processedWeight = 1f;
                cleanWeight = 0f;
                brightness = 1f;
                saturation = 1f;
                emissionWhiten = 0f;
            }

            ApplyPathVisuals(tracked.ProcessedSlots, brightness, saturation, emissionWhiten, processedWeight, drainStrength);

            if (tracked.HasCleanProxy && tracked.CleanProxyRoot != null)
            {
                bool showClean = cleanWeight > 0.01f;
                if (tracked.CleanProxyRoot.activeSelf != showClean)
                    tracked.CleanProxyRoot.SetActive(showClean);

                if (showClean)
                {
                    SyncCleanProxyTransforms(tracked);
                    ApplyPathVisuals(tracked.CleanSlots, brightness, saturation, emissionWhiten, cleanWeight, drainStrength);
                }
            }
            else if (cleanWeight > 0.99f)
            {
                ApplyFallbackHardSwitch(tracked, brightness, saturation, emissionWhiten, drainStrength);
            }

            bool playSfx = tracked.PendingSterilizationSfx
                && !tracked.SterilizationSfxPlayed
                && cleanWeight >= 0.5f;

            if (playSfx)
            {
                tracked.PendingSterilizationSfx = false;
                tracked.SterilizationSfxPlayed = true;
                StartCoroutine(PlaySterilizationSfxAfterVisualSnap(tracked.Root));
            }
        }

        private static void SyncCleanProxyTransforms(TrackedProp tracked)
        {
            int count = Mathf.Min(tracked.ProcessedSlots.Count, tracked.CleanSlots.Count);
            for (int i = 0; i < count; i++)
            {
                Transform source = tracked.ProcessedSlots[i].SourceTransform;
                Renderer cleanRenderer = tracked.CleanSlots[i].Renderer;
                if (source == null || cleanRenderer == null)
                    continue;

                Transform cleanTransform = cleanRenderer.transform;
                cleanTransform.position = source.position;
                cleanTransform.rotation = source.rotation;
                SetWorldScale(cleanTransform, source.lossyScale);
            }
        }

        private static void SetWorldScale(Transform target, Vector3 desiredWorldScale)
        {
            if (target == null)
                return;

            Transform parent = target.parent;
            if (parent == null)
            {
                target.localScale = desiredWorldScale;
                return;
            }

            Vector3 parentWorldScale = parent.lossyScale;
            float x = Mathf.Abs(parentWorldScale.x) < 0.0001f ? 1f : desiredWorldScale.x / parentWorldScale.x;
            float y = Mathf.Abs(parentWorldScale.y) < 0.0001f ? 1f : desiredWorldScale.y / parentWorldScale.y;
            float z = Mathf.Abs(parentWorldScale.z) < 0.0001f ? 1f : desiredWorldScale.z / parentWorldScale.z;
            target.localScale = new Vector3(x, y, z);
        }

        private void ApplyFallbackHardSwitch(
            TrackedProp tracked,
            float brightness,
            float saturation,
            float emissionWhiten,
            float drainStrength)
        {
            ApplyPathVisuals(tracked.ProcessedSlots, brightness, saturation, emissionWhiten, 0f, drainStrength);
            SetLayerRecursive(tracked.Root, SystemPropCleanOverlaySetup.SystemPropLayer);
        }

        private float GetDrainStrength(float t)
        {
            if (t <= 0.001f)
                return 0f;

            float end = Mathf.Max(_drainStart + 0.01f, _drainEnd);
            return Mathf.Clamp01(Mathf.InverseLerp(_drainStart, end, t));
        }

        private float GetBrightnessForSterility(float drainStrength)
        {
            float target = Mathf.Max(1f, _clearBrightnessMultiplier);
            float eased = Mathf.SmoothStep(0f, 1f, drainStrength);
            return Mathf.Lerp(1f, target, eased);
        }

        private float GetSaturationForSterility(float drainStrength)
        {
            float target = Mathf.Clamp01(_clearSaturation);
            float eased = Mathf.SmoothStep(0f, 1f, drainStrength);
            return Mathf.Lerp(1f, target, eased);
        }

        private void ApplyPathVisuals(
            List<RendererSlot> slots,
            float brightnessMultiplier,
            float saturation,
            float emissionWhiten,
            float visibilityWeight,
            float drainStrength)
        {
            visibilityWeight = Mathf.Clamp01(visibilityWeight);
            drainStrength = Mathf.Clamp01(drainStrength);

            for (int s = 0; s < slots.Count; s++)
            {
                RendererSlot slot = slots[s];
                if (slot.Renderer == null)
                    continue;

                bool visible = visibilityWeight > 0.02f;
                if (slot.Renderer.enabled != visible)
                    slot.Renderer.enabled = visible;

                if (!visible)
                    continue;

                ApplyRuntimeMaterials(slot);
                ApplyClearMaterialOverrides(slot, brightnessMultiplier, saturation, emissionWhiten, drainStrength);
                ApplyPropertyBlockColors(slot, brightnessMultiplier, saturation, emissionWhiten, drainStrength);
            }
        }

        private static Material GetSterileMaterialTemplate()
        {
            if (s_SterileMaterialTemplate == null)
                s_SterileMaterialTemplate = Resources.Load<Material>(SterileMaterialResourcePath);

            return s_SterileMaterialTemplate;
        }

        private void ApplyClearMaterialOverrides(
            RendererSlot slot,
            float brightnessMultiplier,
            float saturation,
            float emissionWhiten,
            float drainStrength)
        {
            if (slot.Renderer == null)
                return;

            drainStrength = Mathf.Clamp01(drainStrength);
            Material sterileTemplate = drainStrength > 0.85f ? GetSterileMaterialTemplate() : null;

            int count = slot.Originals.Length;
            for (int m = 0; m < count; m++)
            {
                Material source = slot.Originals[m];
                Material runtime = slot.RuntimeInstances != null && m < slot.RuntimeInstances.Length
                    ? slot.RuntimeInstances[m]
                    : null;
                if (source == null || runtime == null)
                    continue;

                if (sterileTemplate != null && drainStrength > 0.92f)
                {
                    runtime.shader = sterileTemplate.shader;
                    runtime.CopyPropertiesFromMaterial(sterileTemplate);
                    continue;
                }

                ApplyWhiteningToMaterial(source, runtime, brightnessMultiplier, saturation, emissionWhiten, drainStrength);
            }
        }

        private static void ApplyWhiteningToMaterial(
            Material source,
            Material runtime,
            float brightnessMultiplier,
            float saturation,
            float emissionWhiten,
            float drainStrength)
        {
            if (source == null || runtime == null)
                return;

            Color sourceBase = source.HasProperty(BaseColorId)
                ? source.GetColor(BaseColorId)
                : (source.HasProperty(ColorId) ? source.GetColor(ColorId) : Color.white);

            Color treated = ApplyClearColorTreatment(sourceBase, brightnessMultiplier, saturation);
            Color finalColor = Color.Lerp(treated, Color.white, drainStrength);

            if (runtime.HasProperty(BaseColorId))
                runtime.SetColor(BaseColorId, finalColor);
            if (runtime.HasProperty(ColorId))
                runtime.SetColor(ColorId, finalColor);

            if (drainStrength > 0.08f)
            {
                Texture white = Texture2D.whiteTexture;
                if (runtime.HasProperty(BaseMapId))
                    runtime.SetTexture(BaseMapId, white);
                if (runtime.HasProperty(MainTexId))
                    runtime.SetTexture(MainTexId, white);
            }

            if (runtime.HasProperty(MetallicId))
            {
                float metallic = source.HasProperty(MetallicId) ? source.GetFloat(MetallicId) : 0f;
                runtime.SetFloat(MetallicId, Mathf.Lerp(metallic, 0f, drainStrength));
            }

            if (runtime.HasProperty(SmoothnessId))
            {
                float smoothness = source.HasProperty(SmoothnessId) ? source.GetFloat(SmoothnessId) : 0.35f;
                runtime.SetFloat(SmoothnessId, Mathf.Lerp(smoothness, 0.4f, drainStrength));
            }

            if (runtime.HasProperty(EmissionColorId))
            {
                runtime.EnableKeyword("_EMISSION");
                runtime.SetColor(EmissionColorId, Color.white * emissionWhiten);
            }
        }

        private static Color ApplyClearColorTreatment(Color color, float brightnessMultiplier, float saturation)
        {
            color = AdjustSaturation(color, saturation);
            return BoostBrightness(color, brightnessMultiplier);
        }

        private static Color AdjustSaturation(Color color, float saturation)
        {
            float luma = color.r * 0.299f + color.g * 0.587f + color.b * 0.114f;
            var gray = new Color(luma, luma, luma, color.a);
            return Color.Lerp(gray, color, saturation);
        }

        private static Color BoostBrightness(Color color, float multiplier)
        {
            return new Color(
                Mathf.Clamp01(color.r * multiplier),
                Mathf.Clamp01(color.g * multiplier),
                Mathf.Clamp01(color.b * multiplier),
                color.a);
        }

        private static void ApplyPropertyBlockColors(
            RendererSlot slot,
            float brightnessMultiplier,
            float saturation,
            float emissionWhiten,
            float drainStrength)
        {
            if (slot?.Renderer == null || slot.PropertyBlock == null || slot.Originals == null)
                return;

            drainStrength = Mathf.Clamp01(drainStrength);
            if (drainStrength <= 0.001f)
            {
                slot.Renderer.SetPropertyBlock(null);
                return;
            }

            slot.Renderer.GetPropertyBlock(slot.PropertyBlock);
            bool wroteAny = false;

            for (int m = 0; m < slot.Originals.Length; m++)
            {
                Material source = slot.Originals[m];
                if (source == null)
                    continue;

                Color sourceBase = source.HasProperty(BaseColorId)
                    ? source.GetColor(BaseColorId)
                    : (source.HasProperty(ColorId) ? source.GetColor(ColorId) : Color.white);
                Color finalColor = Color.Lerp(
                    ApplyClearColorTreatment(sourceBase, brightnessMultiplier, saturation),
                    Color.white,
                    drainStrength);

                if (source.HasProperty(BaseColorId))
                {
                    slot.PropertyBlock.SetColor(BaseColorId, finalColor);
                    wroteAny = true;
                }

                if (source.HasProperty(ColorId))
                {
                    slot.PropertyBlock.SetColor(ColorId, finalColor);
                    wroteAny = true;
                }

                if (drainStrength > 0.08f)
                {
                    Texture white = Texture2D.whiteTexture;
                    if (source.HasProperty(BaseMapId))
                    {
                        slot.PropertyBlock.SetTexture(BaseMapId, white);
                        wroteAny = true;
                    }

                    if (source.HasProperty(MainTexId))
                    {
                        slot.PropertyBlock.SetTexture(MainTexId, white);
                        wroteAny = true;
                    }
                }

                if (source.HasProperty(EmissionColorId) && emissionWhiten > 0.001f)
                {
                    slot.PropertyBlock.SetColor(EmissionColorId, Color.white * emissionWhiten);
                    wroteAny = true;
                }
            }

            if (wroteAny)
                slot.Renderer.SetPropertyBlock(slot.PropertyBlock);
        }

        private void EnsureRootOnProcessedLayer(TrackedProp tracked)
        {
            if (tracked?.Root == null)
                return;

            if (tracked.Root.layer != tracked.OriginalLayer)
                SetLayerRecursive(tracked.Root, tracked.OriginalLayer);
        }

        private static bool TryGetSharedMesh(Renderer renderer, out Mesh mesh)
        {
            mesh = null;
            if (renderer == null)
                return false;

            if (renderer is MeshRenderer)
            {
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null)
                {
                    mesh = filter.sharedMesh;
                    return true;
                }
            }

            if (renderer is SkinnedMeshRenderer skinned && skinned.sharedMesh != null)
            {
                mesh = skinned.sharedMesh;
                return true;
            }

            return false;
        }

        private static bool IsUnderSterilityInternalChild(Transform t)
        {
            while (t != null)
            {
                if (t.name == CleanProxyRootName || t.name == WhiteOverlayRootName)
                    return true;
                t = t.parent;
            }

            return false;
        }

        private static void SetLayerRecursive(GameObject root, int layer)
        {
            if (root == null)
                return;

            root.layer = layer;
            foreach (Transform child in root.transform)
                SetLayerRecursive(child.gameObject, layer);
        }

        private IEnumerator PlaySterilizationSfxAfterVisualSnap(GameObject root)
        {
            yield return null;
            if (root != null)
                RaiseSterilizationStarted(root);
        }

        private static void DisposeTrackedProp(TrackedProp tracked)
        {
            if (tracked == null)
                return;

            for (int s = 0; s < tracked.ProcessedSlots.Count; s++)
            {
                RendererSlot slot = tracked.ProcessedSlots[s];
                if (slot?.Renderer != null && slot.Originals != null)
                {
                    slot.Renderer.enabled = true;
                    slot.Renderer.sharedMaterials = slot.Originals;
                    slot.Renderer.SetPropertyBlock(null);
                }

                DestroyRuntimeMaterials(slot);
            }

            for (int s = 0; s < tracked.CleanSlots.Count; s++)
                DestroyRuntimeMaterials(tracked.CleanSlots[s]);

            for (int s = 0; s < tracked.ProcessedSlots.Count; s++)
            {
                RendererSlot slot = tracked.ProcessedSlots[s];
                if (slot?.WhiteOverlayRenderer != null)
                    UnityEngine.Object.Destroy(slot.WhiteOverlayRenderer.gameObject);

                if (slot?.WhiteOverlayMaterials != null)
                {
                    for (int m = 0; m < slot.WhiteOverlayMaterials.Length; m++)
                    {
                        Material mat = slot.WhiteOverlayMaterials[m];
                        if (mat != null)
                            UnityEngine.Object.Destroy(mat);
                    }
                }
            }

            if (tracked.WhiteOverlayRoot != null)
                UnityEngine.Object.Destroy(tracked.WhiteOverlayRoot);

            if (tracked.CleanProxyRoot != null)
                UnityEngine.Object.Destroy(tracked.CleanProxyRoot);

            if (tracked.Root != null && tracked.Root.layer != tracked.OriginalLayer)
                SetLayerRecursive(tracked.Root, tracked.OriginalLayer);
        }

        private static void DestroyRuntimeMaterials(RendererSlot slot)
        {
            if (slot?.RuntimeInstances == null)
                return;

            for (int i = 0; i < slot.RuntimeInstances.Length; i++)
            {
                Material runtime = slot.RuntimeInstances[i];
                if (runtime != null)
                    UnityEngine.Object.Destroy(runtime);
                slot.RuntimeInstances[i] = null;
            }
        }

        private static void ApplyRuntimeMaterials(RendererSlot slot)
        {
            if (slot?.Renderer == null)
                return;

            if (slot.RuntimeInstances != null && slot.RuntimeInstances.Length > 0)
                slot.Renderer.materials = slot.RuntimeInstances;
            else if (slot.Originals != null)
                slot.Renderer.materials = slot.Originals;
        }

        private static Material CreateRuntimeMaterial(Material source)
        {
            if (source == null)
                return null;

            var runtime = new Material(source) { name = source.name + " (SterilityRuntime)" };
            if (runtime.HasProperty(EmissionColorId))
            {
                runtime.EnableKeyword("_EMISSION");
                runtime.SetColor(EmissionColorId, Color.black);
            }

            return runtime;
        }
    }
}

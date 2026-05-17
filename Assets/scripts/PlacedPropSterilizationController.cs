using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace AlgorithmicGallery.Corruption
{
    /// <summary>
    /// Escalating sanitization by placement count:
    /// Phase 1 (1–6): no bleaching.
    /// Phase 2 (7–20): 1 then 2 oldest props bleached per click.
    /// Phase 3 (21+): 3+ oldest per click (accelerating) and new props spawn sterile.
    /// </summary>
    public class PlacedPropSterilizationController : MonoBehaviour
    {
        /// <summary>Fired when the system starts turning a prop white (bleach queued or instant).</summary>
        public event Action<GameObject> OnPropSterilizationStarted;

        private const string DefaultSterileMaterialPath = "Assets/Materials/basic colours/SterileObject.mat";

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
        private static readonly int GlossinessId = Shader.PropertyToID("_Glossiness");
        private static readonly int MetallicId = Shader.PropertyToID("_Metallic");

        [Header("References")]
        [SerializeField] private SandboxManager _sandbox;
        [SerializeField] private PropPlacer _placer;
        [SerializeField] private Material _sterileMaterial;

        [Header("Phase boundaries (placement count, 1-based)")]
        [SerializeField] private int _phase1End = 6;
        [SerializeField] private int _phase2SlowEnd = 13;
        [SerializeField] private int _phase2FastEnd = 20;
        [SerializeField] private int _phase3End = 35;

        [Header("Bleach rates")]
        [SerializeField] private int _phase2SlowBleachPerClick = 1;
        [SerializeField] private int _phase2FastBleachPerClick = 2;
        [SerializeField] private int _phase3BaseBleachPerClick = 3;
        [Tooltip("Every N placements in phase 3 adds +1 bleach (accelerating catch-up).")]
        [SerializeField] private int _phase3ExtraBleachEveryNPlacements = 2;

        [Header("Visuals")]
        [SerializeField] private float _blendSpeed = 0.22f;
        [Tooltip("Blend level where props snap to the white sterile material (SFX is synced to this).")]
        [SerializeField] private float _fullySterileThreshold = 0.985f;

        private readonly List<TrackedProp> _tracked = new();
        private readonly List<TrackedProp> _bleachScratch = new();
        private readonly List<GameObject> _propsScratch = new();
        private Material _sterileMaterialInstance;
        private bool _subscribed;

        private sealed class TrackedProp
        {
            public GameObject Root;
            public int PlacementIndex;
            public float CurrentSterility;
            public float TargetSterility;
            public bool PendingSterilizationSfx;
            public readonly List<RendererSlot> Slots = new();
        }

        private sealed class RendererSlot
        {
            public Renderer Renderer;
            public Material[] Originals;
            public Material[] Blends;
        }

        void Awake()
        {
            ResolveSterileMaterial();
        }

        void Start()
        {
            ResolveReferences();
            Subscribe();
        }

        void Update()
        {
            if (_tracked.Count == 0)
                return;

            for (int i = _tracked.Count - 1; i >= 0; i--)
            {
                TrackedProp tracked = _tracked[i];
                if (tracked.Root == null)
                {
                    ReleaseTracked(tracked);
                    _tracked.RemoveAt(i);
                    continue;
                }

                if (Mathf.Approximately(tracked.CurrentSterility, tracked.TargetSterility))
                    continue;

                tracked.CurrentSterility = Mathf.MoveTowards(
                    tracked.CurrentSterility,
                    tracked.TargetSterility,
                    Time.deltaTime * _blendSpeed);

                ApplySterility(tracked, tracked.CurrentSterility);
            }
        }

        void OnDestroy()
        {
            Unsubscribe();
            ReleaseBlendMaterials();
            if (_sterileMaterialInstance != null)
                Destroy(_sterileMaterialInstance);
        }

        private void ResolveReferences()
        {
            if (_sandbox == null)
                _sandbox = FindFirstObjectByType<SandboxManager>();
            if (_placer == null)
                _placer = FindFirstObjectByType<PropPlacer>();
        }

        private void ResolveSterileMaterial()
        {
            if (_sterileMaterial != null)
                return;

#if UNITY_EDITOR
            _sterileMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(DefaultSterileMaterialPath);
#endif
            if (_sterileMaterial == null)
            {
                Debug.LogWarning(
                    $"[PlacedPropSterilization] Assign SterileObject material in the inspector " +
                    $"(expected at {DefaultSterileMaterialPath}).");
            }
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
            ReleaseBlendMaterials();
        }

        private void HandleSessionComplete()
        {
            for (int i = 0; i < _tracked.Count; i++)
                ForceFullySterile(_tracked[i]);
        }

        private void HandlePropPlaced(bool isPlayer, PropEntry prop, Vector3 worldPosition)
        {
            if (!isPlayer || _sandbox == null || !_sandbox.SandboxActive)
                return;

            if (_sterileMaterial == null)
                ResolveSterileMaterial();
            if (_sterileMaterial == null)
                return;

            EnsureSterileInstance();
            StartCoroutine(ProcessPlacementBleach());
        }

        private IEnumerator ProcessPlacementBleach()
        {
            yield return null;

            int placementNumber = _sandbox.StyleProfile?.PlayerPlacementCount ?? 0;
            RefreshTrackedPropsFromBudget(placementNumber);
            RunBleachPass(placementNumber);
        }

        private void RefreshTrackedPropsFromBudget(int placementNumber)
        {
            _propsScratch.Clear();
            PropBudget.Instance?.GetTrackedPlayerPlacedProps(_propsScratch);

            var seen = new HashSet<GameObject>();
            for (int i = 0; i < _propsScratch.Count; i++)
            {
                GameObject go = _propsScratch[i];
                if (go == null || !seen.Add(go))
                    continue;

                if (FindTracked(go) == null)
                    RegisterProp(go, placementNumber, spawnSterile: ShouldNewPropsSpawnSterile(placementNumber));
            }
        }

        private bool ShouldNewPropsSpawnSterile(int placementNumber) =>
            placementNumber > _phase2FastEnd;

        private int GetBleachCountForPlacement(int placementNumber)
        {
            if (placementNumber <= _phase1End)
                return 0;

            if (placementNumber <= _phase2SlowEnd)
                return _phase2SlowBleachPerClick;

            if (placementNumber <= _phase2FastEnd)
                return _phase2FastBleachPerClick;

            int phase3Start = _phase2FastEnd + 1;
            if (placementNumber <= _phase3End)
            {
                int placementsIntoPhase3 = placementNumber - phase3Start;
                int extra = placementsIntoPhase3 / Mathf.Max(1, _phase3ExtraBleachEveryNPlacements);
                return _phase3BaseBleachPerClick + extra;
            }

            return int.MaxValue;
        }

        private void RunBleachPass(int placementNumber)
        {
            int bleachCount = GetBleachCountForPlacement(placementNumber);
            if (bleachCount <= 0)
                return;

            CollectOldestUnsterile(_bleachScratch);
            if (_bleachScratch.Count == 0)
                return;

            if (bleachCount == int.MaxValue)
                bleachCount = _bleachScratch.Count;

            bleachCount = Mathf.Min(bleachCount, _bleachScratch.Count);
            for (int i = 0; i < bleachCount; i++)
                QueueFullSterilization(_bleachScratch[i]);
        }

        private void CollectOldestUnsterile(List<TrackedProp> buffer)
        {
            buffer.Clear();
            for (int i = 0; i < _tracked.Count; i++)
            {
                TrackedProp tracked = _tracked[i];
                if (tracked.Root == null)
                    continue;
                if (tracked.CurrentSterility >= _fullySterileThreshold
                    && tracked.TargetSterility >= _fullySterileThreshold)
                    continue;

                buffer.Add(tracked);
            }

            buffer.Sort((a, b) => a.PlacementIndex.CompareTo(b.PlacementIndex));
        }

        private void QueueFullSterilization(TrackedProp tracked)
        {
            if (tracked == null)
                return;

            bool wasAlreadySterile = tracked.CurrentSterility >= _fullySterileThreshold
                && tracked.TargetSterility >= _fullySterileThreshold;

            tracked.TargetSterility = 1f;
            if (tracked.CurrentSterility >= _fullySterileThreshold)
            {
                tracked.CurrentSterility = 1f;
                ApplySterility(tracked, 1f);
            }

            if (!wasAlreadySterile)
                tracked.PendingSterilizationSfx = true;
        }

        private void ForceFullySterile(TrackedProp tracked)
        {
            if (tracked == null || tracked.Root == null)
                return;

            bool wasAlreadySterile = tracked.CurrentSterility >= _fullySterileThreshold
                && tracked.TargetSterility >= _fullySterileThreshold;

            tracked.TargetSterility = 1f;
            tracked.CurrentSterility = 1f;
            if (!wasAlreadySterile)
                tracked.PendingSterilizationSfx = true;

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

        private void RegisterProp(GameObject root, int placementIndex, bool spawnSterile)
        {
            if (root == null || _sterileMaterial == null)
                return;

            EnsureSterileInstance();

            var tracked = new TrackedProp
            {
                Root = root,
                PlacementIndex = placementIndex,
                CurrentSterility = spawnSterile ? 1f : 0f,
                TargetSterility = spawnSterile ? 1f : 0f
            };

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                    continue;

                Material[] originals = renderer.sharedMaterials;
                if (originals == null || originals.Length == 0)
                    continue;

                var copies = new Material[originals.Length];
                var blends = new Material[originals.Length];
                for (int m = 0; m < originals.Length; m++)
                {
                    copies[m] = originals[m];
                    blends[m] = originals[m] != null
                        ? new Material(originals[m])
                        : new Material(_sterileMaterialInstance);
                }

                tracked.Slots.Add(new RendererSlot
                {
                    Renderer = renderer,
                    Originals = copies,
                    Blends = blends
                });
            }

            if (tracked.Slots.Count == 0)
                return;

            _tracked.Add(tracked);
            if (spawnSterile)
            {
                tracked.PendingSterilizationSfx = true;
                ApplySterility(tracked, 1f);
            }
        }

        private void EnsureSterileInstance()
        {
            if (_sterileMaterialInstance != null)
                return;

            _sterileMaterialInstance = new Material(_sterileMaterial)
            {
                name = "SterileObject_RuntimeInstance"
            };
        }

        private void ApplySterility(TrackedProp tracked, float t)
        {
            t = Mathf.Clamp01(t);
            bool playSfxOnSnap = tracked.PendingSterilizationSfx
                && tracked.Root != null
                && t >= _fullySterileThreshold;

            for (int s = 0; s < tracked.Slots.Count; s++)
            {
                RendererSlot slot = tracked.Slots[s];
                if (slot.Renderer == null)
                    continue;

                int count = slot.Originals.Length;
                var applied = new Material[count];

                for (int m = 0; m < count; m++)
                {
                    Material original = slot.Originals[m];
                    if (t >= _fullySterileThreshold || original == null)
                    {
                        applied[m] = _sterileMaterialInstance;
                        continue;
                    }

                    if (t <= 0.001f)
                    {
                        applied[m] = original;
                        continue;
                    }

                    Material blend = slot.Blends[m];
                    LerpMaterialTowardSterile(blend, original, t);
                    applied[m] = blend;
                }

                slot.Renderer.sharedMaterials = applied;
            }

            if (playSfxOnSnap)
            {
                tracked.PendingSterilizationSfx = false;
                StartCoroutine(PlaySterilizationSfxAfterVisualSnap(tracked.Root));
            }
        }

        private IEnumerator PlaySterilizationSfxAfterVisualSnap(GameObject root)
        {
            yield return null;
            if (root != null)
                RaiseSterilizationStarted(root);
        }

        private void LerpMaterialTowardSterile(Material dest, Material source, float t)
        {
            if (dest == null || source == null || _sterileMaterialInstance == null)
                return;

            if (source.HasProperty(BaseColorId) && dest.HasProperty(BaseColorId))
            {
                Color from = source.GetColor(BaseColorId);
                Color to = _sterileMaterialInstance.GetColor(BaseColorId);
                dest.SetColor(BaseColorId, Color.Lerp(from, to, t));
            }
            else if (source.HasProperty(ColorId) && dest.HasProperty(ColorId))
            {
                Color from = source.GetColor(ColorId);
                Color to = _sterileMaterialInstance.HasProperty(ColorId)
                    ? _sterileMaterialInstance.GetColor(ColorId)
                    : Color.white;
                dest.SetColor(ColorId, Color.Lerp(from, to, t));
            }

            if (source.HasProperty(SmoothnessId) && dest.HasProperty(SmoothnessId))
            {
                float from = source.GetFloat(SmoothnessId);
                float to = _sterileMaterialInstance.GetFloat(SmoothnessId);
                dest.SetFloat(SmoothnessId, Mathf.Lerp(from, to, t));
            }
            else if (source.HasProperty(GlossinessId) && dest.HasProperty(GlossinessId))
            {
                float from = source.GetFloat(GlossinessId);
                float to = _sterileMaterialInstance.HasProperty(GlossinessId)
                    ? _sterileMaterialInstance.GetFloat(GlossinessId)
                    : 0.5f;
                dest.SetFloat(GlossinessId, Mathf.Lerp(from, to, t));
            }

            if (source.HasProperty(MetallicId) && dest.HasProperty(MetallicId))
            {
                float from = source.GetFloat(MetallicId);
                float to = _sterileMaterialInstance.GetFloat(MetallicId);
                dest.SetFloat(MetallicId, Mathf.Lerp(from, to, t));
            }

            if (t >= 0.92f)
                dest.shader = _sterileMaterialInstance.shader;
        }

        private static void ReleaseTracked(TrackedProp tracked)
        {
            if (tracked == null)
                return;

            for (int i = 0; i < tracked.Slots.Count; i++)
            {
                Material[] blends = tracked.Slots[i].Blends;
                if (blends == null)
                    continue;

                for (int m = 0; m < blends.Length; m++)
                {
                    if (blends[m] != null)
                        Destroy(blends[m]);
                }
            }
        }

        private void ReleaseBlendMaterials()
        {
            for (int i = 0; i < _tracked.Count; i++)
                ReleaseTracked(_tracked[i]);
            _tracked.Clear();
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace AlgorithmicGallery.Corruption
{
    /// <summary>
    /// Escalating sanitization by placement count:
    /// Phase 1 (1–6): no clarity pass.
    /// Phase 2 (7–20): 1 then 2 oldest props clarified per click.
    /// Phase 3 (21+): 3+ oldest per click (accelerating) and new props spawn clarified.
    /// Clarified props keep their original materials, render on the clean overlay layer
    /// (no PSX screen post), and get a slight brightness boost with reduced saturation.
    /// </summary>
    public class PlacedPropSterilizationController : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        /// <summary>Fired when a prop snaps to the clear overlay look (SFX sync).</summary>
        public event Action<GameObject> OnPropSterilizationStarted;

        [Header("References")]
        [SerializeField] private SandboxManager _sandbox;
        [SerializeField] private PropPlacer _placer;

        [Header("Phase boundaries (placement count, 1-based)")]
        [SerializeField] private int _phase1End = 6;
        [SerializeField] private int _phase2SlowEnd = 13;
        [SerializeField] private int _phase2FastEnd = 20;
        [SerializeField] private int _phase3End = 35;

        [Header("Clarity rates")]
        [SerializeField] private int _phase2SlowBleachPerClick = 1;
        [SerializeField] private int _phase2FastBleachPerClick = 2;
        [SerializeField] private int _phase3BaseBleachPerClick = 3;
        [Tooltip("Every N placements in phase 3 adds +1 clarified prop (accelerating catch-up).")]
        [SerializeField] private int _phase3ExtraBleachEveryNPlacements = 2;

        [Header("Visuals")]
        [SerializeField] private float _blendSpeed = 0.22f;
        [Tooltip("Sterility level where props move to the clean overlay layer.")]
        [SerializeField] private float _clearVisualThreshold = 0.92f;
        [Tooltip("Brightness multiplier applied on top of the prop's existing material colours.")]
        [SerializeField] private float _clearBrightnessMultiplier = 1.28f;
        [Tooltip("Colour saturation at full sterilization (1 = unchanged, lower = more washed out).")]
        [SerializeField, Range(0f, 1f)] private float _clearSaturation = 0.5f;

        private readonly List<TrackedProp> _tracked = new();
        private readonly List<TrackedProp> _bleachScratch = new();
        private readonly List<GameObject> _propsScratch = new();
        private MaterialPropertyBlock _propertyBlockScratch;
        private bool _subscribed;

        private sealed class TrackedProp
        {
            public GameObject Root;
            public int PlacementIndex;
            public int OriginalLayer;
            public float CurrentSterility;
            public float TargetSterility;
            public bool PendingSterilizationSfx;
            public bool IsOnCleanLayer;
            public readonly List<RendererSlot> Slots = new();
        }

        private sealed class RendererSlot
        {
            public Renderer Renderer;
            public Material[] Originals;
        }

        void Awake()
        {
            _propertyBlockScratch = new MaterialPropertyBlock();
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
                    ClearVisualOverrides(tracked);
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
            for (int i = 0; i < _tracked.Count; i++)
                ClearVisualOverrides(_tracked[i]);
            _tracked.Clear();
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
                ClearVisualOverrides(_tracked[i]);
            _tracked.Clear();
        }

        private void HandleSessionComplete()
        {
            for (int i = 0; i < _tracked.Count; i++)
                ForceFullyClear(_tracked[i]);
        }

        private void HandlePropPlaced(bool isPlayer, PropEntry prop, Vector3 worldPosition)
        {
            if (!isPlayer || _sandbox == null || !_sandbox.SandboxActive)
                return;

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
                    RegisterProp(go, placementNumber, spawnClear: ShouldNewPropsSpawnClear(placementNumber));
            }
        }

        private bool ShouldNewPropsSpawnClear(int placementNumber) =>
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

            CollectOldestUnclear(_bleachScratch);
            if (_bleachScratch.Count == 0)
                return;

            if (bleachCount == int.MaxValue)
                bleachCount = _bleachScratch.Count;

            bleachCount = Mathf.Min(bleachCount, _bleachScratch.Count);
            for (int i = 0; i < bleachCount; i++)
                QueueFullClarity(_bleachScratch[i]);
        }

        private void CollectOldestUnclear(List<TrackedProp> buffer)
        {
            buffer.Clear();
            for (int i = 0; i < _tracked.Count; i++)
            {
                TrackedProp tracked = _tracked[i];
                if (tracked.Root == null)
                    continue;
                if (tracked.CurrentSterility >= _clearVisualThreshold
                    && tracked.TargetSterility >= _clearVisualThreshold)
                    continue;

                buffer.Add(tracked);
            }

            buffer.Sort((a, b) => a.PlacementIndex.CompareTo(b.PlacementIndex));
        }

        private void QueueFullClarity(TrackedProp tracked)
        {
            if (tracked == null)
                return;

            bool wasAlreadyClear = tracked.CurrentSterility >= _clearVisualThreshold
                && tracked.TargetSterility >= _clearVisualThreshold;

            tracked.TargetSterility = 1f;
            if (tracked.CurrentSterility >= _clearVisualThreshold)
            {
                tracked.CurrentSterility = 1f;
                ApplySterility(tracked, 1f);
            }

            if (!wasAlreadyClear)
                tracked.PendingSterilizationSfx = true;
        }

        private void ForceFullyClear(TrackedProp tracked)
        {
            if (tracked == null || tracked.Root == null)
                return;

            bool wasAlreadyClear = tracked.CurrentSterility >= _clearVisualThreshold
                && tracked.TargetSterility >= _clearVisualThreshold;

            tracked.TargetSterility = 1f;
            tracked.CurrentSterility = 1f;
            if (!wasAlreadyClear)
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

        private void RegisterProp(GameObject root, int placementIndex, bool spawnClear)
        {
            if (root == null)
                return;

            var tracked = new TrackedProp
            {
                Root = root,
                PlacementIndex = placementIndex,
                OriginalLayer = root.layer,
                CurrentSterility = spawnClear ? 1f : 0f,
                TargetSterility = spawnClear ? 1f : 0f
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
                for (int m = 0; m < originals.Length; m++)
                    copies[m] = originals[m];

                tracked.Slots.Add(new RendererSlot
                {
                    Renderer = renderer,
                    Originals = copies
                });
            }

            if (tracked.Slots.Count == 0)
                return;

            _tracked.Add(tracked);
            if (spawnClear)
            {
                tracked.PendingSterilizationSfx = true;
                ApplySterility(tracked, 1f);
            }
        }

        private void ApplySterility(TrackedProp tracked, float t)
        {
            if (tracked?.Root == null)
                return;

            t = Mathf.Clamp01(t);
            bool applyClearLook = t >= _clearVisualThreshold;
            bool playSfxOnSnap = tracked.PendingSterilizationSfx && applyClearLook;

            if (applyClearLook)
            {
                float brightness = GetBrightnessForSterility(t);
                float saturation = GetSaturationForSterility(t);
                ApplyClearVisual(tracked, brightness, saturation);
            }
            else
            {
                RestoreUnclearVisual(tracked);
            }

            if (playSfxOnSnap)
            {
                tracked.PendingSterilizationSfx = false;
                StartCoroutine(PlaySterilizationSfxAfterVisualSnap(tracked.Root));
            }
        }

        private float GetBrightnessForSterility(float t)
        {
            float target = Mathf.Max(1f, _clearBrightnessMultiplier);
            if (t >= 1f)
                return target;

            float ramp = Mathf.InverseLerp(_clearVisualThreshold, 1f, t);
            return Mathf.Lerp(1f, target, ramp);
        }

        private float GetSaturationForSterility(float t)
        {
            float target = Mathf.Clamp01(_clearSaturation);
            if (t >= 1f)
                return target;

            float ramp = Mathf.InverseLerp(_clearVisualThreshold, 1f, t);
            return Mathf.Lerp(1f, target, ramp);
        }

        private void ApplyClearVisual(TrackedProp tracked, float brightnessMultiplier, float saturation)
        {
            for (int s = 0; s < tracked.Slots.Count; s++)
            {
                RendererSlot slot = tracked.Slots[s];
                if (slot.Renderer == null)
                    continue;

                slot.Renderer.sharedMaterials = slot.Originals;
                ApplyClearMaterialOverrides(slot, brightnessMultiplier, saturation);
            }

            ApplyClearLayer(tracked);
        }

        private void RestoreUnclearVisual(TrackedProp tracked)
        {
            for (int s = 0; s < tracked.Slots.Count; s++)
                ClearBrightnessBoost(tracked.Slots[s]);

            RestoreOriginalLayer(tracked);
        }

        private void ApplyClearMaterialOverrides(RendererSlot slot, float brightnessMultiplier, float saturation)
        {
            bool needsOverride = brightnessMultiplier > 1.001f || saturation < 0.999f;
            if (slot.Renderer == null || !needsOverride)
            {
                ClearBrightnessBoost(slot);
                return;
            }

            if (_propertyBlockScratch == null)
                _propertyBlockScratch = new MaterialPropertyBlock();

            int count = slot.Originals.Length;
            for (int m = 0; m < count; m++)
            {
                Material material = slot.Originals[m];
                if (material == null)
                {
                    slot.Renderer.SetPropertyBlock(null, m);
                    continue;
                }

                _propertyBlockScratch.Clear();

                if (material.HasProperty(BaseColorId))
                {
                    Color baseColor = material.GetColor(BaseColorId);
                    _propertyBlockScratch.SetColor(
                        BaseColorId,
                        ApplyClearColorTreatment(baseColor, brightnessMultiplier, saturation));
                }
                else if (material.HasProperty(ColorId))
                {
                    Color baseColor = material.GetColor(ColorId);
                    _propertyBlockScratch.SetColor(
                        ColorId,
                        ApplyClearColorTreatment(baseColor, brightnessMultiplier, saturation));
                }

                slot.Renderer.SetPropertyBlock(_propertyBlockScratch, m);
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

        private static void ClearBrightnessBoost(RendererSlot slot)
        {
            if (slot.Renderer == null)
                return;

            for (int m = 0; m < slot.Originals.Length; m++)
                slot.Renderer.SetPropertyBlock(null, m);
        }

        private void ApplyClearLayer(TrackedProp tracked)
        {
            if (tracked.IsOnCleanLayer)
                return;

            SetLayerRecursive(tracked.Root, SystemPropCleanOverlaySetup.SystemPropLayer);
            tracked.IsOnCleanLayer = true;
        }

        private void RestoreOriginalLayer(TrackedProp tracked)
        {
            if (!tracked.IsOnCleanLayer)
                return;

            SetLayerRecursive(tracked.Root, tracked.OriginalLayer);
            tracked.IsOnCleanLayer = false;
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

        private static void ClearVisualOverrides(TrackedProp tracked)
        {
            if (tracked == null)
                return;

            for (int s = 0; s < tracked.Slots.Count; s++)
                ClearBrightnessBoost(tracked.Slots[s]);

            RestoreOriginalLayerStatic(tracked);
        }

        private static void RestoreOriginalLayerStatic(TrackedProp tracked)
        {
            if (tracked.Root == null || !tracked.IsOnCleanLayer)
                return;

            SetLayerRecursive(tracked.Root, tracked.OriginalLayer);
            tracked.IsOnCleanLayer = false;
        }
    }
}

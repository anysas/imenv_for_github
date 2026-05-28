using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace AlgorithmicGallery.Corruption
{
    /// <summary>
    /// Plays a short particle stream from the sandbox layout toward <c>glass-case/mainPedestal</c>,
    /// then fades in the combined exhibit on the pedestal.
    /// </summary>
    public class DioramaTransferVfxController : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        [Header("Enable")]
        [SerializeField] private bool _enabled = true;

        [Header("Timing")]
        [SerializeField] private float _transferDuration = 1.2f;
        [SerializeField] private float _exhibitFadeDuration = 0.8f;
        [SerializeField] private float _exhibitFadeStartDelay = 0.4f;
        [SerializeField] private AnimationCurve _exhibitFadeEase = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Particles")]
        [SerializeField] private Color _particleColor = new Color(0.72f, 0.86f, 1f, 0.55f);
        [SerializeField] private int _particleBurstCount = 48;
        [SerializeField] private float _particleStartSize = 0.06f;
        [SerializeField] private float _sourceLift = 0.35f;
        [SerializeField] private float _targetLift = 0.12f;

        [Header("References")]
        [SerializeField] private Transform _mainPedestal;
        [SerializeField] private string _mainPedestalHierarchyPath = "glass-case/mainPedestal";
        [SerializeField] private string _mainPedestalObjectName = "mainPedestal";

        private Material _runtimeParticleMaterial;
        private Coroutine _activeRoutine;
        private readonly List<FadeSlot> _fadeSlots = new();

        private sealed class FadeSlot
        {
            public Renderer Renderer;
            public Material[] OriginalShared;
            public Material[] RuntimeFade;
        }

        public bool EffectEnabled => _enabled;

        void OnDestroy()
        {
            if (_activeRoutine != null)
                StopCoroutine(_activeRoutine);

            RestoreFadeTargetsImmediate();
            if (_runtimeParticleMaterial != null)
                Destroy(_runtimeParticleMaterial);
        }

        /// <summary>
        /// Runs transfer VFX then fades in <paramref name="exhibitRoot"/> on the pedestal.
        /// </summary>
        public void PlayTransfer(GameObject exhibitRoot, Vector3 sourceWorldCenter, Vector3 targetWorldAnchor)
        {
            if (!_enabled || exhibitRoot == null)
            {
                if (exhibitRoot != null)
                    SetExhibitFullyVisible(exhibitRoot);
                return;
            }

            if (_activeRoutine != null)
            {
                StopCoroutine(_activeRoutine);
                RestoreFadeTargetsImmediate();
            }

            PrepareExhibitHidden(exhibitRoot);
            _activeRoutine = StartCoroutine(TransferRoutine(exhibitRoot, sourceWorldCenter, targetWorldAnchor));
        }

        public Vector3 ResolvePedestalTargetAnchor()
        {
            ResolvePedestalReference();
            if (_mainPedestal == null)
                return transform.position;

            if (SceneHierarchyLookup.TryGetPedestalTopSurface(_mainPedestal, out Vector3 top))
                return top + Vector3.up * _targetLift;

            return _mainPedestal.position + Vector3.up * _targetLift;
        }

        private void ResolvePedestalReference()
        {
            if (_mainPedestal != null)
                return;

            _mainPedestal = SceneHierarchyLookup.FindTransform(
                _mainPedestalHierarchyPath,
                _mainPedestalObjectName);
        }

        private IEnumerator TransferRoutine(GameObject exhibitRoot, Vector3 sourceCenter, Vector3 targetAnchor)
        {
            Vector3 source = sourceCenter + Vector3.up * _sourceLift;
            Vector3 target = targetAnchor;

            SpawnTransferParticles(source, target, _transferDuration);

            float transferDur = Mathf.Max(0.05f, _transferDuration);
            float fadeStart = Mathf.Clamp(_exhibitFadeStartDelay, 0f, transferDur * 0.95f);
            float fadeDur = Mathf.Max(0.05f, _exhibitFadeDuration);
            float elapsed = 0f;
            bool fadeStarted = false;

            while (elapsed < transferDur + fadeDur)
            {
                elapsed += Time.deltaTime;

                if (!fadeStarted && elapsed >= fadeStart)
                {
                    fadeStarted = true;
                    BeginExhibitFade(exhibitRoot);
                }

                if (fadeStarted)
                {
                    float fadeT = Mathf.Clamp01((elapsed - fadeStart) / fadeDur);
                    float eased = _exhibitFadeEase != null && _exhibitFadeEase.length > 0
                        ? _exhibitFadeEase.Evaluate(fadeT)
                        : fadeT;
                    ApplyFadeAlpha(eased);
                }

                yield return null;
            }

            FinalizeExhibitReveal(exhibitRoot);
            _activeRoutine = null;
            GameplayEventDebugLog.Push("DioramaTransfer", "exhibit reveal complete");
        }

        private void PrepareExhibitHidden(GameObject exhibitRoot)
        {
            _fadeSlots.Clear();
            CollectFadeSlots(exhibitRoot, _fadeSlots);

            for (int i = 0; i < _fadeSlots.Count; i++)
            {
                FadeSlot slot = _fadeSlots[i];
                if (slot.Renderer == null)
                    continue;

                slot.Renderer.enabled = true;
                ApplyFadeAlphaToSlot(slot, 0f);
            }
        }

        private void BeginExhibitFade(GameObject exhibitRoot)
        {
            if (_fadeSlots.Count == 0)
                CollectFadeSlots(exhibitRoot, _fadeSlots);
        }

        private void ApplyFadeAlpha(float alpha)
        {
            for (int i = 0; i < _fadeSlots.Count; i++)
                ApplyFadeAlphaToSlot(_fadeSlots[i], alpha);
        }

        private static void ApplyFadeAlphaToSlot(FadeSlot slot, float alpha)
        {
            if (slot?.RuntimeFade == null)
                return;

            alpha = Mathf.Clamp01(alpha);
            for (int m = 0; m < slot.RuntimeFade.Length; m++)
            {
                Material mat = slot.RuntimeFade[m];
                if (mat == null)
                    continue;

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
        }

        private void FinalizeExhibitReveal(GameObject exhibitRoot)
        {
            RestoreFadeTargetsImmediate();
            SetExhibitFullyVisible(exhibitRoot);
        }

        private static void SetExhibitFullyVisible(GameObject exhibitRoot)
        {
            if (exhibitRoot == null)
                return;

            var renderers = exhibitRoot.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                    renderers[i].enabled = true;
            }
        }

        private void RestoreFadeTargetsImmediate()
        {
            for (int i = 0; i < _fadeSlots.Count; i++)
            {
                FadeSlot slot = _fadeSlots[i];
                if (slot?.Renderer == null)
                    continue;

                if (slot.OriginalShared != null && slot.OriginalShared.Length > 0)
                    slot.Renderer.sharedMaterials = slot.OriginalShared;

                if (slot.RuntimeFade != null)
                {
                    for (int m = 0; m < slot.RuntimeFade.Length; m++)
                    {
                        if (slot.RuntimeFade[m] != null)
                            Destroy(slot.RuntimeFade[m]);
                    }
                }
            }

            _fadeSlots.Clear();
        }

        private static void CollectFadeSlots(GameObject root, List<FadeSlot> output)
        {
            output.Clear();
            if (root == null)
                return;

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int r = 0; r < renderers.Length; r++)
            {
                Renderer renderer = renderers[r];
                if (renderer == null)
                    continue;

                Material[] originals = renderer.sharedMaterials;
                if (originals == null || originals.Length == 0)
                    continue;

                var runtime = new Material[originals.Length];
                for (int m = 0; m < originals.Length; m++)
                    runtime[m] = CreateFadeMaterial(originals[m]);

                renderer.sharedMaterials = runtime;

                output.Add(new FadeSlot
                {
                    Renderer = renderer,
                    OriginalShared = originals,
                    RuntimeFade = runtime
                });
            }
        }

        private static Material CreateFadeMaterial(Material source)
        {
            if (source == null)
                return null;

            var runtime = new Material(source) { name = source.name + " (TransferFade)" };
            SetMaterialAlpha(runtime, 0f);

            // URP transparent surface when available.
            if (runtime.HasProperty("_Surface"))
            {
                runtime.SetFloat("_Surface", 1f);
                runtime.SetFloat("_Blend", 0f);
                runtime.SetOverrideTag("RenderType", "Transparent");
                runtime.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                runtime.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            }

            return runtime;
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

        private void SpawnTransferParticles(Vector3 source, Vector3 target, float duration)
        {
            Vector3 delta = target - source;
            float distance = delta.magnitude;
            if (distance < 0.05f)
                distance = 1f;

            Vector3 dir = delta / distance;
            float speed = distance / Mathf.Max(0.2f, duration * 0.85f);

            var fxGo = new GameObject("_DioramaTransferParticles");
            fxGo.transform.position = source;

            var ps = fxGo.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = duration;
            main.startLifetime = new ParticleSystem.MinMaxCurve(duration * 0.55f, duration * 0.95f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.75f, speed * 1.05f);
            main.startSize = new ParticleSystem.MinMaxCurve(_particleStartSize * 0.7f, _particleStartSize * 1.2f);
            main.startColor = _particleColor;
            main.maxParticles = Mathf.Max(16, _particleBurstCount * 2);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0f;

            var emission = ps.emission;
            emission.enabled = false;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = Mathf.Clamp(distance * 0.04f, 0.08f, 0.45f);

            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = dir.x * speed;
            velocity.y = dir.y * speed;
            velocity.z = dir.z * speed;

            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(_particleColor, 0f),
                    new GradientColorKey(Color.white, 1f),
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.18f, 0.12f),
                    new GradientAlphaKey(0.55f, 0.45f),
                    new GradientAlphaKey(0.2f, 0.85f),
                    new GradientAlphaKey(0f, 1f),
                });
            colorOverLifetime.color = gradient;

            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.4f),
                new Keyframe(0.35f, 1f),
                new Keyframe(1f, 0.15f)));

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = GetParticleMaterial();
            renderer.alignment = ParticleSystemRenderSpace.View;

            ps.Emit(Mathf.Max(16, _particleBurstCount));
            ps.Play();

            Destroy(fxGo, duration + 1.5f);
        }

        private Material GetParticleMaterial()
        {
            if (_runtimeParticleMaterial != null)
                return _runtimeParticleMaterial;

            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
                shader = Shader.Find("Particles/Standard Unlit");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");
            if (shader == null)
                return null;

            _runtimeParticleMaterial = new Material(shader)
            {
                name = "DioramaTransferParticleMat",
                color = _particleColor
            };
            return _runtimeParticleMaterial;
        }

    }
}

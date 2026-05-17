using UnityEngine;

namespace AlgorithmicGallery
{
    /// <summary>
    /// Controls per-fragment reveal and retreat within a sculpture based on parent growth level.
    /// Also drives per-part dissolve/growth shader properties via MaterialPropertyBlock
    /// so each fragment reveals with a "burning into existence" visual effect.
    /// </summary>
    public class GrowthPart : MonoBehaviour
    {
        private const float ProgressEpsilon = 0.001f;

        // Shader property IDs for per-part dissolve
        private static readonly int DissolveAmountId = Shader.PropertyToID("_DissolveAmount");
        private static readonly int GrowthProgressId = Shader.PropertyToID("_GrowthProgress");

        [SerializeField, Range(0f, 1f)]
        private float _revealThreshold = 0.5f;
        [SerializeField]
        private float _revealBand = 0.18f;
        [SerializeField]
        private float _growSpeed = 2f;
        [SerializeField]
        private float _shrinkSpeed = 2.6f;
        [SerializeField]
        private float _growSmoothTime = 0.25f;
        [SerializeField]
        private float _shrinkSmoothTime = 0.15f;
        [SerializeField]
        private float _emergenceDistance = 0.16f;
        [SerializeField]
        private float _hiddenScaleFactor = 0.05f;
        [SerializeField, Range(0f, 1f)]
        private float _decayShrivelStrength = 0.15f;
        [SerializeField]
        private float _decaySinkDistance = 0.08f;
        [SerializeField]
        private bool _flickerWhenNearStub = false;
        [SerializeField, Range(0f, 1f)]
        private float _flickerStartGrowth = 0.22f;
        [SerializeField]
        private float _flickerSpeed = 18f;

        [Header("Emergence Animation")]
        [SerializeField, Tooltip("Rotation wobble amplitude in degrees during reveal.")]
        private float _revealWobbleAmplitude = 12f;
        [SerializeField]
        private float _revealWobbleSpeed = 8f;
        [SerializeField, Tooltip("Brief scale overshoot when reveal completes (1.0 = no overshoot).")]
        private float _revealOvershootScale = 1.06f;
        [SerializeField]
        private float _overshootDecaySpeed = 4f;

        private float _progress;
        private float _progressVelocity;
        private float _lastAppliedProgress = -1f;
        private float _lastParentGrowthLevel;
        private bool _lastRendererState = true;
        private Vector3 _baseLocalPosition;
        private Quaternion _baseLocalRotation;
        private Vector3 _baseLocalScale;
        private Vector3 _emergenceDirection = Vector3.up;
        private Renderer[] _renderers;
        private MaterialPropertyBlock _partPropertyBlock;
        private float _overshootAmount;
        private bool _wasFullyRevealed;

        private void OnEnable()
        {
            _baseLocalPosition = transform.localPosition;
            _baseLocalRotation = transform.localRotation;
            _baseLocalScale = transform.localScale;
            _progress = 0f;
            _progressVelocity = 0f;
            _lastAppliedProgress = -1f;
            _lastRendererState = true;
            _renderers = GetComponentsInChildren<Renderer>(true);
            _partPropertyBlock = new MaterialPropertyBlock();
            _overshootAmount = 0f;
            _wasFullyRevealed = false;
            ApplyTransform();
            ApplyPartShaderProperties();
        }

        public void Configure(float revealThreshold, Vector3 emergenceDirection)
        {
            _revealThreshold = Mathf.Clamp01(revealThreshold);
            if (emergenceDirection.sqrMagnitude > 0.0001f)
            {
                _emergenceDirection = emergenceDirection.normalized;
            }
        }

        public void Tick(float parentGrowthLevel, float dt)
        {
            float targetProgress = EvaluateTargetProgress(parentGrowthLevel);

            float speed = targetProgress > _progress ? _growSpeed : _shrinkSpeed;
            float smoothTime = targetProgress > _progress
                ? Mathf.Max(0.01f, _growSmoothTime)
                : Mathf.Max(0.01f, _shrinkSmoothTime);
            float maxSpeed = Mathf.Max(0.01f, speed * 2f);
            float newProgress = Mathf.SmoothDamp(_progress, targetProgress, ref _progressVelocity, smoothTime, maxSpeed, dt);
            newProgress = Mathf.Clamp01(newProgress);

            bool changed = Mathf.Abs(newProgress - _progress) > ProgressEpsilon
                        || Mathf.Abs(parentGrowthLevel - _lastParentGrowthLevel) > ProgressEpsilon;

            _progress = newProgress;
            _lastParentGrowthLevel = parentGrowthLevel;

            if (changed)
            {
                ApplyTransform();
                ApplyPartShaderProperties();
            }

            // Track overshoot: trigger when progress crosses from <0.98 to >=0.98
            bool fullyRevealed = _progress >= 0.98f;
            if (fullyRevealed && !_wasFullyRevealed)
            {
                _overshootAmount = _revealOvershootScale - 1f;
            }
            _wasFullyRevealed = fullyRevealed;

            // Decay overshoot
            if (_overshootAmount > 0.001f)
            {
                _overshootAmount = Mathf.MoveTowards(_overshootAmount, 0f, dt * _overshootDecaySpeed);
                ApplyTransform(); // re-apply with updated overshoot
            }
        }

        public void SnapToGrowth(float parentGrowthLevel)
        {
            _lastParentGrowthLevel = parentGrowthLevel;
            _progress = EvaluateTargetProgress(parentGrowthLevel);
            _progressVelocity = 0f;
            _lastAppliedProgress = -1f;
            _overshootAmount = 0f;
            _wasFullyRevealed = _progress >= 0.98f;
            ApplyTransform();
            ApplyPartShaderProperties();
        }

        private float EvaluateTargetProgress(float parentGrowthLevel)
        {
            float lower = Mathf.Clamp01(_revealThreshold - _revealBand);
            float upper = Mathf.Clamp01(_revealThreshold + _revealBand);
            return upper <= lower
                ? (parentGrowthLevel >= _revealThreshold ? 1f : 0f)
                : Mathf.InverseLerp(lower, upper, parentGrowthLevel);
        }

        private void ApplyTransform()
        {
            float hiddenScale = Mathf.Clamp(_hiddenScaleFactor, 0.001f, 1f);
            float decayFactor = 1f - Mathf.Clamp01(_lastParentGrowthLevel);
            float shrivelMultiplier = Mathf.Lerp(1f, 1f - _decayShrivelStrength, decayFactor);
            float scaleLerp = Mathf.Lerp(hiddenScale, 1f, _progress) * shrivelMultiplier;

            bool transformChanged = Mathf.Abs(_progress - _lastAppliedProgress) > ProgressEpsilon;
            if (transformChanged)
            {
                float scaleLerpFinal = scaleLerp + _overshootAmount;
                transform.localScale = _baseLocalScale * scaleLerpFinal;
                Vector3 retreat = _emergenceDirection * (_emergenceDistance * (1f - _progress));
                Vector3 decaySink = Vector3.down * (_decaySinkDistance * decayFactor);
                transform.localPosition = _baseLocalPosition + retreat + decaySink;

                // Rotation wobble during reveal (0 when fully revealed or hidden)
                float wobbleEnvelope = Mathf.Sin(_progress * Mathf.PI); // peaks at 0.5 progress
                float wobbleAngle = Mathf.Sin(Time.time * _revealWobbleSpeed + _revealThreshold * 20f)
                                  * _revealWobbleAmplitude * wobbleEnvelope;
                transform.localRotation = _baseLocalRotation * Quaternion.Euler(0f, wobbleAngle, wobbleAngle * 0.3f);

                _lastAppliedProgress = _progress;
            }

            bool shouldRender = _progress > 0.01f;
            if (_flickerWhenNearStub && _lastParentGrowthLevel < _flickerStartGrowth && shouldRender)
            {
                float flicker = Mathf.Sin((Time.time + (float)(_renderers != null ? _renderers.Length : 0)) * _flickerSpeed);
                shouldRender = flicker > -0.05f;
            }

            if (shouldRender != _lastRendererState)
            {
                _lastRendererState = shouldRender;
                if (_renderers != null)
                {
                    for (int i = 0; i < _renderers.Length; i++)
                    {
                        if (_renderers[i] != null)
                        {
                            _renderers[i].enabled = shouldRender;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Drives per-part _DissolveAmount and _GrowthProgress on this part's renderers.
        /// This creates the "burning into existence" dissolve effect per-fragment.
        /// </summary>
        private void ApplyPartShaderProperties()
        {
            if (_renderers == null || _renderers.Length == 0 || _partPropertyBlock == null)
                return;

            // Part dissolve: inverse of part progress
            // Hidden = 1.0 (fully dissolved), revealed = 0.0 (fully visible)
            float partDissolve = Mathf.Lerp(0.85f, 0f, _progress);
            float partGrowth = _progress;

            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] == null)
                    continue;

                _renderers[i].GetPropertyBlock(_partPropertyBlock);
                _partPropertyBlock.SetFloat(DissolveAmountId, partDissolve);
                _partPropertyBlock.SetFloat(GrowthProgressId, partGrowth);
                _renderers[i].SetPropertyBlock(_partPropertyBlock);
            }
        }
    }
}

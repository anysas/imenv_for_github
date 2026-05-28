using UnityEngine;
using UnityEngine.Rendering;
using AlgorithmicGallery.Recommendation;

namespace AlgorithmicGallery
{
    /// <summary>
    /// Controls the continuous lifecycle of a sculpture based on layered attention signals.
    /// Growth increases while gaze is active and decays while ignored.
    /// Shader properties are driven via MaterialPropertyBlock for efficient rendering.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class SculptureController : MonoBehaviour
    {
        private const string EmissiveFactorProperty = "emissiveFactor";
        private const string BaseColorFactorProperty = "baseColorFactor";
        private const string UrpBaseColorProperty = "_BaseColor";
        private const string LegacyColorProperty = "_Color";

        // Growth shader properties (GrowthSculpture.shader)
        private static readonly int SaturationPropertyId = Shader.PropertyToID("_Saturation");
        private static readonly int EmissionPowerPropertyId = Shader.PropertyToID("_EmissionPower");
        private static readonly int DissolveAmountPropertyId = Shader.PropertyToID("_DissolveAmount");
        private static readonly int GrowthProgressPropertyId = Shader.PropertyToID("_GrowthProgress");
        private static readonly int PhaseHuePropertyId = Shader.PropertyToID("_PhaseHue");
        private static readonly int EmissionColorPropertyId = Shader.PropertyToID("_EmissionColor");

        private const float GrowthChangeEpsilon = 0.0005f;

        private enum DwellTier
        {
            Glance = 0,
            Study = 1,
            Fixation = 2
        }

        [Header("Configuration")]
        [SerializeField]
        private float _growthRate = 0.7f;
        [SerializeField]
        private float _decayRate = 0.2f;
        [SerializeField]
        private float _stubGrowthLevel = 0.45f;
        [SerializeField]
        private float _distantMaxGrowth = 0.55f;
        [SerializeField]
        private float _closeRangeDistance = 5f;
        [SerializeField]
        private float _farRangeDistance = 25f;
        [SerializeField]
        private float _breathePulseSpeed = 2f;
        [SerializeField]
        private float _breathePulseAmount = 0.05f;
        [SerializeField]
        private float _maxEmissionPower = 0.08f;
        [SerializeField]
        private float _maxEdgeSharpness = 1f;
        [SerializeField]
        private float _maxHighlightRim = 1f;
        [SerializeField]
        private float _maxDecayNoise = 1f;
        [SerializeField]
        private float _minOpacityAtStub = 1f;
        [SerializeField]
        private float _decayAccentThreshold = 0.38f;
        [SerializeField]
        private float _growthAccentThreshold = 0.68f;

        [Header("Growth Variation")]
        [SerializeField]
        private float _maxStretchFactor = 1.15f;
        [SerializeField]
        private float _maxBloomDrift = 0.12f;
        [SerializeField]
        private float _twistAmplitude = 0f;
        [SerializeField]
        private float _twistSpeedMin = 0.6f;
        [SerializeField]
        private float _twistSpeedMax = 1.8f;

        [Header("Presence Response")]
        [SerializeField]
        private Transform _playerTransform;
        [SerializeField]
        private float _proximityRadius = 8f;
        [SerializeField]
        private float _closeProximityRadius = 2f;
        [SerializeField]
        private float _proximityEmissionBoost = 0.04f;
        [SerializeField]
        private float _proximitySaturationBoost = 0.22f;
        [SerializeField]
        private float _visualGrowthSmoothTime = 0.3f;
        [SerializeField]
        private float _proximitySmoothTime = 0.2f;
        [SerializeField]
        private float _touchFlashDecaySeconds = 0.8f;
        [SerializeField]
        private float _touchFlashIntensityBoost = 0.18f;
        [SerializeField]
        private float _maxVelocityForMotionReaction = 14f;
        [SerializeField]
        private float _movementAgitationBoost = 0f;

        [Header("Dwell Tiers")]
        [SerializeField]
        private float _studyTierSeconds = 2f;
        [SerializeField]
        private float _fixationTierSeconds = 8f;
        [SerializeField]
        private float _studyWobbleAmount = 0.04f;
        [SerializeField]
        private float _fixationScaleBoost = 0.12f;

        [Header("Touch Audio")]
        [SerializeField]
        private AudioClip[] _touchAudioClips;
        [SerializeField]
        private float _touchVolume = 0.8f;
        [SerializeField]
        private Vector2 _touchPitchRange = new Vector2(0.92f, 1.14f);
        [SerializeField]
        private float _touchCooldownSeconds = 0.2f;

        [Header("Look-away Reach")]
        [SerializeField]
        private float _reachDwellThresholdSeconds = 3f;
        [SerializeField]
        private float _reachDuration = 0.5f;
        [SerializeField]
        private float _reachDistance = 0.2f;

        [Header("References")]
        [SerializeField]
        private Renderer _renderer;
        [SerializeField]
        private ParticleSystem _growthAccentParticles;
        [SerializeField]
        private ParticleSystem _decayAccentParticles;
        [SerializeField]
        private GalleryManager _galleryManager;

        [Header("Halo (ground ring)")]
        [SerializeField]
        private bool _enableAttentionHalo = true;
        [SerializeField]
        private int _haloSegments = 56;
        [SerializeField]
        private float _haloYOffset = 0.04f;
        [SerializeField]
        private float _haloRadiusScale = 1.06f;
        [SerializeField]
        private float _haloWidthMin = 0.012f;
        [SerializeField]
        private float _haloWidthMax = 0.07f;

        public string ModelId { get; set; }
        public float TotalDwellMs { get; private set; }
        public float GrowthLevel => _growthLevel;

        private float _growthLevel;
        private bool _isBeingGazedAt;
        private float _gazeCenteredness;
        private float _gazeDistance = float.MaxValue;
        private float _animTimer;
        private MaterialPropertyBlock _propertyBlock;
        private Vector3 _originalScale;
        private Vector3 _originalLocalPosition;
        private Quaternion _originalRotation;
        private Vector3 _stretchAxes = Vector3.one;
        private Vector3 _driftDirection = Vector3.zero;
        private float _twistSpeed;
        private float _phaseOffset;
        private float _lastGrowthLevel;
        private float _lastAppliedGrowthLevel = -1f;
        private float _lastTouchTime = float.NegativeInfinity;
        private float _touchFlashIntensity;
        private float _reachTimer;
        private Vector3 _reachOffset;
        private Vector3 _reachDirection = Vector3.forward;
        private Vector3 _lastPlayerPosition;
        private float _playerSpeed;
        private bool _hasPlayerPosition;
        private DwellTier _currentDwellTier;
        private AudioSource _touchAudioSource;
        private float _smoothedGrowthLevel;
        private float _smoothedGrowthVelocity;
        private float _smoothedProximityFactor;
        private float _smoothedProximityVelocity;
        private LineRenderer _attentionHalo;
        private Material _haloMaterial;

        private void Awake()
        {
            // With the custom GrowthSculpture shader, emission can be more expressive.
            _maxEmissionPower = 1.2f;
            _proximityEmissionBoost = 0.35f;
        }

        private Renderer[] _allRenderers;
        private GrowthPart[] _growthParts;
        private Color[] _rendererBaseColors;

        private void OnEnable()
        {
            if (_renderer == null)
            {
                _renderer = GetComponentInChildren<Renderer>();
            }

            _allRenderers = GetComponentsInChildren<Renderer>();
            _propertyBlock = new MaterialPropertyBlock();
            CacheRendererColorState();
            _originalScale = transform.localScale;
            _originalLocalPosition = transform.localPosition;
            _originalRotation = transform.localRotation;
            _growthLevel = Mathf.Clamp01(_stubGrowthLevel);
            _smoothedGrowthLevel = _growthLevel;
            _smoothedGrowthVelocity = 0f;
            _smoothedProximityFactor = 0f;
            _smoothedProximityVelocity = 0f;
            _isBeingGazedAt = false;
            _gazeCenteredness = 0f;
            _animTimer = 0f;
            TotalDwellMs = 0f;
            _lastGrowthLevel = _growthLevel;
            _lastAppliedGrowthLevel = -1f;
            _reachTimer = _reachDuration;
            _reachOffset = Vector3.zero;
            _touchFlashIntensity = 0f;
            _currentDwellTier = DwellTier.Glance;

            InitializeGrowthVariation();
            CacheGrowthParts();
            ResolvePlayerTransform();
            ResolveGalleryManager();
            EnsureTouchAudioSource();
            EnsureAttentionHalo();

            ApplyGrowthState(_growthLevel, 0f, forceApply: true);
            SnapGrowthPartsToCurrentLevel();
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            _animTimer += dt;
            UpdatePlayerMetrics(dt);
            _currentDwellTier = EvaluateDwellTier();

            float stubFloor = Mathf.Clamp01(_stubGrowthLevel);
            float growthCeiling = Mathf.Max(stubFloor, _distantMaxGrowth);
            float effectiveGrowthRate = _growthRate;
            float gazeInfluence = Mathf.Max(_isBeingGazedAt ? 1f : 0f, _gazeCenteredness);

            if (gazeInfluence > 0f)
            {
                float distanceFactor = _isBeingGazedAt
                    ? GetDistanceFactor01()
                    : Mathf.Clamp01(_gazeCenteredness);
                effectiveGrowthRate *= Mathf.Lerp(0.15f, 1f, distanceFactor);
                effectiveGrowthRate *= Mathf.Lerp(0.2f, 1f, gazeInfluence);
                growthCeiling = Mathf.Lerp(Mathf.Max(stubFloor, _distantMaxGrowth), 1f, Mathf.Max(distanceFactor, _gazeCenteredness));
            }

            float delta = (gazeInfluence > 0f ? effectiveGrowthRate : -_decayRate) * dt;
            float newGrowthLevel = Mathf.Clamp(_growthLevel + delta, stubFloor, Mathf.Max(stubFloor, growthCeiling));

            bool growthChanged = Mathf.Abs(newGrowthLevel - _growthLevel) > GrowthChangeEpsilon;
            _growthLevel = newGrowthLevel;
            _smoothedGrowthLevel = Mathf.SmoothDamp(
                _smoothedGrowthLevel,
                _growthLevel,
                ref _smoothedGrowthVelocity,
                Mathf.Max(0.01f, _visualGrowthSmoothTime),
                Mathf.Infinity,
                dt);
            _touchFlashIntensity = Mathf.MoveTowards(
                _touchFlashIntensity,
                0f,
                dt / Mathf.Max(0.05f, _touchFlashDecaySeconds));

            bool needsVisualUpdate = growthChanged || _isBeingGazedAt || _gazeCenteredness > 0.001f || _currentDwellTier != DwellTier.Glance;
            ApplyGrowthState(_smoothedGrowthLevel, dt, forceApply: !needsVisualUpdate);
            if (needsVisualUpdate)
            {
                TickGrowthParts(dt);
            }

            DetectAccentThresholds();
            UpdateReachOffset(dt);
            _lastGrowthLevel = _growthLevel;
        }

        public void OnGazeDwell(float dwellTimeSeconds)
        {
            TotalDwellMs = dwellTimeSeconds * 1000f;
        }

        public void SetGazeState(bool isActive, float gazeDistance)
        {
            if (isActive && !_isBeingGazedAt)
            {
                OnGazeStart();
            }

            if (gazeDistance >= 0f)
            {
                _gazeDistance = gazeDistance;
            }

            _isBeingGazedAt = isActive;
        }

        public void SetGazeCenteredness(float centeredness01)
        {
            _gazeCenteredness = Mathf.Clamp01(centeredness01);
        }

        public void SetGazeActive(bool isActive)
        {
            SetGazeState(isActive, _gazeDistance);
        }

        public void OnGazeStart()
        {
            PlayGrowthAccent();
        }

        public void OnGazeExit()
        {
            _isBeingGazedAt = false;
            if (TotalDwellMs / 1000f >= _reachDwellThresholdSeconds && _playerTransform != null)
            {
                _reachDirection = (_playerTransform.position - transform.position).normalized;
                if (_reachDirection.sqrMagnitude < 0.0001f)
                    _reachDirection = transform.forward;
                _reachTimer = 0f;
            }
        }

        public void RefreshGrowthParts()
        {
            CacheGrowthParts();
            SnapGrowthPartsToCurrentLevel();
        }

        private void CacheGrowthParts()
        {
            _growthParts = GetComponentsInChildren<GrowthPart>(true);
        }

        private void CacheRendererColorState()
        {
            if (_allRenderers == null || _allRenderers.Length == 0)
                return;

            _rendererBaseColors = new Color[_allRenderers.Length];

            for (int i = 0; i < _allRenderers.Length; i++)
            {
                var renderer = _allRenderers[i];
                if (renderer == null)
                    continue;

                Material referenceMaterial = renderer.sharedMaterial;
                if (referenceMaterial == null)
                {
                    _rendererBaseColors[i] = Color.white;
                    continue;
                }

                if (referenceMaterial.HasProperty(BaseColorFactorProperty))
                {
                    _rendererBaseColors[i] = referenceMaterial.GetColor(BaseColorFactorProperty);
                }
                else if (referenceMaterial.HasProperty(UrpBaseColorProperty))
                {
                    _rendererBaseColors[i] = referenceMaterial.GetColor(UrpBaseColorProperty);
                }
                else if (referenceMaterial.HasProperty(LegacyColorProperty))
                {
                    _rendererBaseColors[i] = referenceMaterial.GetColor(LegacyColorProperty);
                }
                else
                {
                    _rendererBaseColors[i] = Color.white;
                }
            }
        }

        private void ApplyGrowthState(float growthLevel, float dt, bool forceApply)
        {
            if (!forceApply && Mathf.Abs(growthLevel - _lastAppliedGrowthLevel) < GrowthChangeEpsilon && !_isBeingGazedAt && _gazeCenteredness <= 0.001f)
                return;

            _lastAppliedGrowthLevel = growthLevel;

            float stubFloor = Mathf.Clamp01(_stubGrowthLevel);
            float visualGrowth = Mathf.InverseLerp(stubFloor, 1f, growthLevel);
            float rawProximityFactor = GetProximityFactor01();
            _smoothedProximityFactor = Mathf.SmoothDamp(
                _smoothedProximityFactor,
                rawProximityFactor,
                ref _smoothedProximityVelocity,
                Mathf.Max(0.01f, _proximitySmoothTime),
                Mathf.Infinity,
                dt);
            float proximityFactor = _smoothedProximityFactor;
            float agitationFactor = 1f;
            float dwellWobble = _currentDwellTier >= DwellTier.Study ? _studyWobbleAmount : 0f;
            float tierScaleBoost = _currentDwellTier == DwellTier.Fixation ? _fixationScaleBoost : 0f;
            float phaseResponseMultiplier = GetPhaseResponseMultiplier();

            float gazePulse = (_isBeingGazedAt || _gazeCenteredness > 0.001f)
                ? Mathf.Sin((_animTimer + _phaseOffset) * (_breathePulseSpeed * agitationFactor * phaseResponseMultiplier)) * (_breathePulseAmount + dwellWobble)
                : 0f;
            float pulseScale = 1f + (gazePulse * visualGrowth);

            ApplyMaterialResponse(visualGrowth, proximityFactor, phaseResponseMultiplier);

            float targetScale = Mathf.Lerp(0.9f, 1f + tierScaleBoost, visualGrowth);
            float stretch = Mathf.Lerp(1f, _maxStretchFactor, visualGrowth);
            Vector3 directionalStretch = Vector3.Lerp(Vector3.one, _stretchAxes * stretch, visualGrowth);
            transform.localScale = Vector3.Scale(_originalScale * targetScale * pulseScale, directionalStretch);

            float twist = Mathf.Sin((_animTimer + _phaseOffset) * _twistSpeed * agitationFactor) * _twistAmplitude * visualGrowth;
            Quaternion rotation = _originalRotation * Quaternion.Euler(0f, twist, 0f);

            transform.localRotation = rotation;

            Vector3 proximityOffset = _driftDirection * (_maxBloomDrift * visualGrowth * Mathf.Lerp(0.35f, 1f, proximityFactor));
            transform.localPosition = _originalLocalPosition + proximityOffset + _reachOffset;

            UpdateAttentionHalo(visualGrowth, proximityFactor, phaseResponseMultiplier);
        }

        private void ApplyMaterialResponse(float visualGrowth, float proximityFactor, float phaseResponseMultiplier)
        {
            if (_allRenderers == null || _allRenderers.Length == 0)
                return;

            float gazeContribution = Mathf.Max(_isBeingGazedAt ? 1f : 0f, _gazeCenteredness);
            float dwellSeconds = TotalDwellMs / 1000f;
            float dwellProgress = Mathf.Clamp01(dwellSeconds / Mathf.Max(0.1f, _fixationTierSeconds));

            // --- Growth Shader properties ---
            // Saturation: greyscale at seed, full color when blooming
            float saturation = Mathf.Lerp(0.15f, 1f, visualGrowth)
                             + proximityFactor * _proximitySaturationBoost;
            saturation = Mathf.Clamp01(saturation);

            // Emission: driven by gaze + proximity + touch + phase
            float emissionPower = Mathf.Lerp(0f, _maxEmissionPower, visualGrowth)
                                * Mathf.Lerp(0.3f, 1f, gazeContribution)
                                * Mathf.Lerp(0.8f, 1f, dwellProgress)
                                * phaseResponseMultiplier;
            emissionPower += proximityFactor * _proximityEmissionBoost * phaseResponseMultiplier;
            emissionPower += _touchFlashIntensity * _touchFlashIntensityBoost * phaseResponseMultiplier;
            emissionPower = Mathf.Clamp(emissionPower, 0f, 3f);

            // Dissolve: inverse of growth — fully dissolved at seed, fully revealed at bloom
            float dissolveAmount = Mathf.Lerp(0.65f, 0f, visualGrowth);

            // Growth progress: direct mapping for vertex displacement
            float growthProgress = visualGrowth;

            // Phase hue: shift color across the emotional arc
            float phaseHue = GetPhaseHueShift();

            // Emission tint color: warm-shifted based on gaze and phase
            Color warmBiasColor = new Color(1f, 0.62f, 0.2f, 1f);
            Color coolBiasColor = new Color(0.4f, 0.8f, 1f, 1f);
            Color emissionTint = Color.Lerp(coolBiasColor, warmBiasColor,
                Mathf.Clamp01(gazeContribution * 0.6f + dwellProgress * 0.4f));

            // --- Apply to all renderers ---
            for (int i = 0; i < _allRenderers.Length; i++)
            {
                var renderer = _allRenderers[i];
                if (renderer == null)
                    continue;

                renderer.GetPropertyBlock(_propertyBlock);

                // Growth shader properties (GrowthSculpture.shader)
                _propertyBlock.SetFloat(SaturationPropertyId, saturation);
                _propertyBlock.SetFloat(EmissionPowerPropertyId, emissionPower);
                _propertyBlock.SetFloat(DissolveAmountPropertyId, dissolveAmount);
                _propertyBlock.SetFloat(GrowthProgressPropertyId, growthProgress);
                _propertyBlock.SetFloat(PhaseHuePropertyId, phaseHue);
                _propertyBlock.SetColor(EmissionColorPropertyId, emissionTint);

                // Legacy fallback for any non-growth-shader materials still in the scene
                Color legacyEmission = emissionTint * Mathf.Min(emissionPower, 0.1f);
                _propertyBlock.SetColor(EmissiveFactorProperty, legacyEmission);

                renderer.SetPropertyBlock(_propertyBlock);
            }
        }

        private float GetPhaseHueShift()
        {
            ResolveGalleryManager();
            if (_galleryManager == null)
                return 0f;

            float progress = Mathf.Clamp01(_galleryManager.PhaseProgress);
            return _galleryManager.CurrentPhase switch
            {
                ArcPhase.Fascination => Mathf.Lerp(0f, 0.05f, progress),   // cool, near neutral
                ArcPhase.Recognition => Mathf.Lerp(0.05f, 0.12f, progress), // warm shift
                ArcPhase.Unease => Mathf.Lerp(0.12f, 0.25f, progress),      // deep red shift
                _ => 0f
            };
        }

        private static Color NormalizeColorForEmission(Color color)
        {
            float luminance = (color.r + color.g + color.b) / 3f;
            if (luminance <= 0.001f)
                return Color.white;

            Color normalized = new Color(color.r / luminance, color.g / luminance, color.b / luminance, 1f);
            return new Color(
                Mathf.Clamp(normalized.r, 0f, 2.5f),
                Mathf.Clamp(normalized.g, 0f, 2.5f),
                Mathf.Clamp(normalized.b, 0f, 2.5f),
                1f);
        }

        private static Color SaturateColor(Color color, float saturationMultiplier)
        {
            float gray = (color.r + color.g + color.b) / 3f;
            Color grayscale = new Color(gray, gray, gray, 1f);
            return Color.LerpUnclamped(grayscale, color, Mathf.Max(0f, saturationMultiplier));
        }

        private static bool IsNearMonochrome(Color color)
        {
            float max = Mathf.Max(color.r, Mathf.Max(color.g, color.b));
            float min = Mathf.Min(color.r, Mathf.Min(color.g, color.b));
            return (max - min) < 0.08f;
        }

        private static Color ClampEmissionColor(Color color, float maxChannelValue)
        {
            float cap = Mathf.Max(0f, maxChannelValue);
            return new Color(
                Mathf.Clamp(color.r, 0f, cap),
                Mathf.Clamp(color.g, 0f, cap),
                Mathf.Clamp(color.b, 0f, cap),
                1f);
        }

        private void TickGrowthParts(float dt)
        {
            if (_growthParts == null || _growthParts.Length == 0)
                return;

            for (int i = 0; i < _growthParts.Length; i++)
            {
                if (_growthParts[i] != null)
                    _growthParts[i].Tick(_growthLevel, dt);
            }
        }

        private void SnapGrowthPartsToCurrentLevel()
        {
            if (_growthParts == null || _growthParts.Length == 0)
                return;

            for (int i = 0; i < _growthParts.Length; i++)
            {
                if (_growthParts[i] != null)
                    _growthParts[i].SnapToGrowth(_growthLevel);
            }
        }

        private void InitializeGrowthVariation()
        {
            float axisX = Random.Range(0.9f, 1.15f);
            float axisY = Random.Range(0.95f, 1.35f);
            float axisZ = Random.Range(0.9f, 1.15f);
            _stretchAxes = new Vector3(axisX, axisY, axisZ);

            Vector2 drift2D = Random.insideUnitCircle.normalized * Random.Range(0.2f, 1f);
            _driftDirection = new Vector3(drift2D.x, 0f, drift2D.y);

            _twistSpeed = Random.Range(_twistSpeedMin, _twistSpeedMax);
            _phaseOffset = Random.Range(0f, 10f);
        }

        private void DetectAccentThresholds()
        {
            float decayThreshold = Mathf.Clamp01(_decayAccentThreshold);
            float growthThreshold = Mathf.Clamp01(_growthAccentThreshold);

            if (_lastGrowthLevel > decayThreshold && _growthLevel <= decayThreshold)
                PlayDecayAccent();

            if (_lastGrowthLevel < growthThreshold && _growthLevel >= growthThreshold)
                PlayGrowthAccent();
        }

        private void PlayGrowthAccent()
        {
            if (_growthAccentParticles == null)
                return;

            if (_growthAccentParticles.isPlaying)
            {
                _growthAccentParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            _growthAccentParticles.Play(true);
        }

        private void PlayDecayAccent()
        {
            if (_decayAccentParticles == null)
                return;

            if (_decayAccentParticles.isPlaying)
            {
                _decayAccentParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            _decayAccentParticles.Play(true);
        }

        private void ResolvePlayerTransform()
        {
            if (_playerTransform != null)
                return;

            var taggedPlayer = GameObject.FindGameObjectWithTag("Player");
            if (taggedPlayer != null)
            {
                _playerTransform = taggedPlayer.transform;
                return;
            }

            if (Camera.main != null)
            {
                _playerTransform = Camera.main.transform;
            }
        }

        private void ResolveGalleryManager()
        {
            if (_galleryManager != null)
                return;

            _galleryManager = FindFirstObjectByType<GalleryManager>();
        }

        private float GetPhaseResponseMultiplier()
        {
            ResolveGalleryManager();
            if (_galleryManager == null)
                return 1f;

            float progress = Mathf.Clamp01(_galleryManager.PhaseProgress);
            return _galleryManager.CurrentPhase switch
            {
                ArcPhase.Fascination => 1f,
                ArcPhase.Recognition => Mathf.Lerp(1.04f, 1.1f, progress),
                ArcPhase.Unease => Mathf.Lerp(1.1f, 1.18f, progress),
                _ => 1f
            };
        }

        private void EnsureTouchAudioSource()
        {
            _touchAudioSource = GetComponent<AudioSource>();
            if (_touchAudioSource == null)
                _touchAudioSource = gameObject.AddComponent<AudioSource>();

            _touchAudioSource.spatialBlend = 1f;
            _touchAudioSource.playOnAwake = false;
            _touchAudioSource.rolloffMode = AudioRolloffMode.Linear;
            _touchAudioSource.maxDistance = Mathf.Max(8f, _proximityRadius * 1.5f);
        }

        private void UpdatePlayerMetrics(float dt)
        {
            ResolvePlayerTransform();
            if (_playerTransform == null || dt <= 0.0001f)
                return;

            Vector3 currentPos = _playerTransform.position;
            if (_hasPlayerPosition)
            {
                float speed = (currentPos - _lastPlayerPosition).magnitude / dt;
                _playerSpeed = Mathf.Lerp(_playerSpeed, speed, 0.25f);
            }

            _lastPlayerPosition = currentPos;
            _hasPlayerPosition = true;
        }

        private float GetProximityFactor01()
        {
            if (_playerTransform == null)
                return 0f;

            float near = Mathf.Max(0.1f, _closeProximityRadius);
            float far = Mathf.Max(near + 0.01f, _proximityRadius);
            return Mathf.InverseLerp(far, near, Vector3.Distance(transform.position, _playerTransform.position));
        }

        private DwellTier EvaluateDwellTier()
        {
            float dwellSeconds = TotalDwellMs / 1000f;
            if (dwellSeconds >= _fixationTierSeconds)
                return DwellTier.Fixation;
            if (dwellSeconds >= _studyTierSeconds)
                return DwellTier.Study;
            return DwellTier.Glance;
        }

        private void UpdateReachOffset(float dt)
        {
            if (_reachTimer >= _reachDuration || _reachDuration <= 0.001f)
            {
                _reachOffset = Vector3.Lerp(_reachOffset, Vector3.zero, dt * 8f);
                return;
            }

            _reachTimer += dt;
            float t = Mathf.Clamp01(_reachTimer / _reachDuration);
            float envelope = 1f - Mathf.Abs((t * 2f) - 1f);
            _reachOffset = (_reachDirection.normalized * _reachDistance) * envelope;
        }

        private bool IsPlayerCollider(Collider other)
        {
            if (other == null)
                return false;
            if (other.CompareTag("Player"))
                return true;
            return other.GetComponentInParent<CharacterController>() != null;
        }

        private void TryPlayTouchAudio()
        {
            if (_touchAudioSource == null || _touchAudioClips == null || _touchAudioClips.Length == 0)
                return;
            if (Time.time - _lastTouchTime < _touchCooldownSeconds)
                return;

            _lastTouchTime = Time.time;
            int clipIndex = Random.Range(0, _touchAudioClips.Length);
            AudioClip clip = _touchAudioClips[clipIndex];
            if (clip == null)
                return;

            _touchAudioSource.pitch = Random.Range(_touchPitchRange.x, _touchPitchRange.y);
            _touchAudioSource.PlayOneShot(clip, _touchVolume);
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (collision == null || !IsPlayerCollider(collision.collider))
                return;

            TryPlayTouchAudio();
            TriggerTouchFlash();
            PlayGrowthAccent();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!IsPlayerCollider(other))
                return;

            TryPlayTouchAudio();
            TriggerTouchFlash();
            PlayGrowthAccent();
        }

        private void TriggerTouchFlash()
        {
            _touchFlashIntensity = Mathf.Clamp01(_touchFlashIntensity + 1f);
        }

        private void EnsureAttentionHalo()
        {
            if (!_enableAttentionHalo)
                return;

            if (_attentionHalo != null)
                return;

            var haloGo = new GameObject("AttentionHalo");
            haloGo.transform.SetParent(transform, false);
            haloGo.layer = gameObject.layer;

            _attentionHalo = haloGo.AddComponent<LineRenderer>();
            _attentionHalo.loop = true;
            _attentionHalo.useWorldSpace = true;
            _attentionHalo.numCornerVertices = 3;
            _attentionHalo.numCapVertices = 2;
            int segments = Mathf.Clamp(_haloSegments, 12, 128);
            _attentionHalo.positionCount = segments;

            // Prefer custom HDR-additive HaloGlow shader for proper bloom contribution;
            // fall back to URP Unlit if not found.
            Shader s = Shader.Find("AlgorithmicGallery/HaloGlow");
            if (s == null)
                s = Shader.Find("Universal Render Pipeline/Unlit");
            if (s == null)
                s = Shader.Find("Unlit/Color");
            if (s == null)
                s = Shader.Find("Sprites/Default");

            _haloMaterial = s != null ? new Material(s) : new Material(Shader.Find("Hidden/Internal-Colored"));
            _attentionHalo.sharedMaterial = _haloMaterial;
            _attentionHalo.shadowCastingMode = ShadowCastingMode.Off;
            _attentionHalo.receiveShadows = false;
            _attentionHalo.textureMode = LineTextureMode.Stretch;
            _attentionHalo.sortingOrder = 1;
        }

        private void UpdateAttentionHalo(float visualGrowth, float proximityFactor, float phaseResponseMultiplier)
        {
            if (!_enableAttentionHalo || _attentionHalo == null)
                return;

            if (_allRenderers == null || _allRenderers.Length == 0)
            {
                _attentionHalo.enabled = false;
                return;
            }

            float gazeContribution = Mathf.Max(_isBeingGazedAt ? 1f : 0f, _gazeCenteredness);
            float haloStrength = Mathf.Clamp01(
                gazeContribution * Mathf.Lerp(0.08f, 0.75f, visualGrowth) * 0.55f
                + proximityFactor * 0.5f
                + _touchFlashIntensity * 0.45f);
            haloStrength *= Mathf.Lerp(0.88f, 1.08f, Mathf.InverseLerp(1f, 1.65f, Mathf.Clamp(phaseResponseMultiplier, 1f, 1.65f)));

            if (haloStrength < 0.02f)
            {
                _attentionHalo.enabled = false;
                return;
            }

            _attentionHalo.enabled = true;

            Bounds wb = _allRenderers[0].bounds;
            for (int r = 1; r < _allRenderers.Length; r++)
            {
                if (_allRenderers[r] != null)
                    wb.Encapsulate(_allRenderers[r].bounds);
            }

            float rx = wb.extents.x * _haloRadiusScale;
            float rz = wb.extents.z * _haloRadiusScale;
            float y = wb.min.y + _haloYOffset;
            int seg = _attentionHalo.positionCount;

            Color warm = new Color(1f, 0.55f, 0.2f, 1f);
            Color cool = new Color(0.35f, 0.75f, 1f, 1f);
            float warmMix = Mathf.Clamp01(gazeContribution * visualGrowth);
            float coolMix = Mathf.Clamp01(proximityFactor);
            Color baseStroke = Color.Lerp(Color.Lerp(cool, warm, warmMix), cool, coolMix * 0.65f);
            float alpha = Mathf.Lerp(0.15f, 0.85f, haloStrength);
            var c = baseStroke;
            c.a = alpha;
            _attentionHalo.startColor = c;
            _attentionHalo.endColor = c;
            _attentionHalo.startWidth = Mathf.Lerp(_haloWidthMin, _haloWidthMax, haloStrength);
            _attentionHalo.endWidth = _attentionHalo.startWidth;

            for (int i = 0; i < seg; i++)
            {
                float a = (i / (float)seg) * Mathf.PI * 2f;
                Vector3 wp = new Vector3(
                    wb.center.x + Mathf.Cos(a) * rx,
                    y,
                    wb.center.z + Mathf.Sin(a) * rz);
                _attentionHalo.SetPosition(i, wp);
            }
        }

        private float GetDistanceFactor01()
        {
            float near = Mathf.Max(0.01f, _closeRangeDistance);
            float far = Mathf.Max(near + 0.01f, _farRangeDistance);
            return Mathf.InverseLerp(far, near, _gazeDistance);
        }
    }
}

using UnityEngine;
using AlgorithmicGallery.Recommendation;

namespace AlgorithmicGallery
{
    /// <summary>
    /// [V1 LEGACY — DO NOT USE IN V2 SANDBOX]
    /// Central atmosphere controller for the V1 gallery environment.
    /// Drives URP fog, ambient color, floor shader, and phase-timed glitch bursts.
    ///
    /// V2 supersedes this with SandboxReactiveVfxDirector + AssistantSystem,
    /// which write to PSXRendererFeature.SetBaseGlitchIntensity instead of
    /// GlitchRendererFeature.GlobalGlitchIntensity (different render feature).
    ///
    /// Currently NOT referenced by any other script and NOT present in any V2 scene.
    /// Kept for reference / V1 reproducibility only. Slated for quarantine to
    /// Assets/scripts/_legacy/ in a future cleanup pass.
    /// </summary>
    public class EnvironmentFXManager : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private GalleryManager _galleryManager;
        [SerializeField] private Transform _playerTransform;

        [Header("Floor Shader")]
        [SerializeField] private Renderer _floorRenderer;
        [SerializeField] private bool _driveFloorShader = true;

        [Header("Fog")]
        [SerializeField] private bool _driveFog = true;
        [SerializeField] private float _fascinationFogDensity = 0.002f;
        [SerializeField] private float _recognitionFogDensity = 0.006f;
        [SerializeField] private float _uneaseFogDensity = 0.014f;
        [SerializeField] private Color _fascinationFogColor = new Color(0.02f, 0.04f, 0.06f, 1f);
        [SerializeField] private Color _recognitionFogColor = new Color(0.06f, 0.04f, 0.02f, 1f);
        [SerializeField] private Color _uneaseFogColor = new Color(0.08f, 0.02f, 0.02f, 1f);

        [Header("Ambient Light")]
        [SerializeField] private bool _driveAmbient = true;
        [SerializeField] private Color _fascinationAmbient = new Color(0.06f, 0.08f, 0.12f, 1f);
        [SerializeField] private Color _recognitionAmbient = new Color(0.10f, 0.07f, 0.04f, 1f);
        [SerializeField] private Color _uneaseAmbient = new Color(0.10f, 0.03f, 0.02f, 1f);

        [Header("Glitch Bursts")]
        [SerializeField] private bool _triggerGlitchOnPhaseChange = true;
        [SerializeField] private float _glitchBurstDuration = 0.4f;
        [SerializeField] private float _uneaseGlitchBaseIntensity = 0.08f;
        [Tooltip("Disable glitch writes in V2 sandbox scenes where SandboxReactiveVfxDirector owns GlitchRendererFeature.GlobalGlitchIntensity.")]
        [SerializeField] private bool _disableGlitchInSandbox = true;

        [Header("Transition")]
        [SerializeField] private float _transitionSpeed = 1.2f;

        // Floor shader property IDs
        private static readonly int PlayerWorldPosId = Shader.PropertyToID("_PlayerWorldPos");
        private static readonly int PhaseBlendId = Shader.PropertyToID("_PhaseBlend");

        private MaterialPropertyBlock _floorPropertyBlock;
        private ArcPhase _lastPhase = ArcPhase.Fascination;
        private float _glitchBurstTimer;

        // Smoothed values
        private float _currentFogDensity;
        private Color _currentFogColor;
        private Color _currentAmbient;

        private void Start()
        {
            ResolveReferences();
            _floorPropertyBlock = new MaterialPropertyBlock();

            // Initialize to Fascination
            _currentFogDensity = _fascinationFogDensity;
            _currentFogColor = _fascinationFogColor;
            _currentAmbient = _fascinationAmbient;
            _glitchBurstTimer = 0f;

            ApplyFog();
            ApplyAmbient();
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            ArcPhase phase = GetCurrentPhase();
            float progress = GetCurrentProgress();

            // Detect phase change → trigger glitch burst
            if (phase != _lastPhase && _triggerGlitchOnPhaseChange)
            {
                _glitchBurstTimer = _glitchBurstDuration;
            }
            _lastPhase = phase;

            // Glitch burst timer — gated so V2 sandbox scenes (SandboxReactiveVfxDirector) own this property
            if (!_disableGlitchInSandbox)
            {
                if (_glitchBurstTimer > 0f)
                {
                    _glitchBurstTimer -= dt;
                    float burstIntensity = Mathf.Clamp01(_glitchBurstTimer / Mathf.Max(0.01f, _glitchBurstDuration));
                    GlitchRendererFeature.GlobalGlitchIntensity = Mathf.Max(burstIntensity * 0.7f, GetUneaseBaseGlitch(phase, progress));
                }
                else
                {
                    GlitchRendererFeature.GlobalGlitchIntensity = GetUneaseBaseGlitch(phase, progress);
                }
            }
            else if (_glitchBurstTimer > 0f)
            {
                _glitchBurstTimer -= dt; // still drain the timer even when gated
            }

            // Compute targets
            float targetFogDensity;
            Color targetFogColor;
            Color targetAmbient;
            float phaseBlend;

            switch (phase)
            {
                case ArcPhase.Recognition:
                    targetFogDensity = Mathf.Lerp(_fascinationFogDensity, _recognitionFogDensity, progress);
                    targetFogColor = Color.Lerp(_fascinationFogColor, _recognitionFogColor, progress);
                    targetAmbient = Color.Lerp(_fascinationAmbient, _recognitionAmbient, progress);
                    phaseBlend = Mathf.Lerp(0f, 0.5f, progress);
                    break;
                case ArcPhase.Unease:
                    targetFogDensity = Mathf.Lerp(_recognitionFogDensity, _uneaseFogDensity, progress);
                    targetFogColor = Color.Lerp(_recognitionFogColor, _uneaseFogColor, progress);
                    targetAmbient = Color.Lerp(_recognitionAmbient, _uneaseAmbient, progress);
                    phaseBlend = Mathf.Lerp(0.5f, 1f, progress);
                    break;
                default: // Fascination
                    targetFogDensity = _fascinationFogDensity;
                    targetFogColor = _fascinationFogColor;
                    targetAmbient = _fascinationAmbient;
                    phaseBlend = 0f;
                    break;
            }

            // Smooth transition
            float t = 1f - Mathf.Exp(-_transitionSpeed * dt * 3f);
            _currentFogDensity = Mathf.Lerp(_currentFogDensity, targetFogDensity, t);
            _currentFogColor = Color.Lerp(_currentFogColor, targetFogColor, t);
            _currentAmbient = Color.Lerp(_currentAmbient, targetAmbient, t);

            ApplyFog();
            ApplyAmbient();
            ApplyFloorShader(phaseBlend);
        }

        private float GetUneaseBaseGlitch(ArcPhase phase, float progress)
        {
            if (phase != ArcPhase.Unease)
                return 0f;
            return _uneaseGlitchBaseIntensity * progress;
        }

        private void ApplyFog()
        {
            if (!_driveFog)
                return;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = _currentFogDensity;
            RenderSettings.fogColor = _currentFogColor;
        }

        private void ApplyAmbient()
        {
            if (!_driveAmbient)
                return;

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = _currentAmbient;
        }

        private void ApplyFloorShader(float phaseBlend)
        {
            if (!_driveFloorShader || _floorRenderer == null || _floorPropertyBlock == null)
                return;

            Vector4 playerPos = Vector4.zero;
            if (_playerTransform != null)
            {
                playerPos = new Vector4(
                    _playerTransform.position.x,
                    _playerTransform.position.y,
                    _playerTransform.position.z,
                    0f);
            }

            _floorRenderer.GetPropertyBlock(_floorPropertyBlock);
            _floorPropertyBlock.SetVector(PlayerWorldPosId, playerPos);
            _floorPropertyBlock.SetFloat(PhaseBlendId, phaseBlend);
            _floorRenderer.SetPropertyBlock(_floorPropertyBlock);
        }

        private ArcPhase GetCurrentPhase()
        {
            if (_galleryManager != null)
                return _galleryManager.CurrentPhase;
            return ArcPhase.Fascination;
        }

        private float GetCurrentProgress()
        {
            if (_galleryManager != null)
                return Mathf.Clamp01(_galleryManager.PhaseProgress);
            return 0f;
        }

        private void ResolveReferences()
        {
            if (_galleryManager == null)
                _galleryManager = FindFirstObjectByType<GalleryManager>();

            if (_playerTransform == null)
            {
                var player = GameObject.FindGameObjectWithTag("Player");
                if (player != null)
                    _playerTransform = player.transform;
                else if (Camera.main != null)
                    _playerTransform = Camera.main.transform;
            }
        }
    }
}

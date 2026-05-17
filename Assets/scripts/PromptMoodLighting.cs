using UnityEngine;

namespace AlgorithmicGallery.Corruption
{
    /// <summary>
    /// Tints the scene's directional light (the "Sun") according to the active prompt's
    /// emotional register, then slowly corrupts toward the drift register as the assistant
    /// enters its Overriding phase — so the room itself gets contaminated.
    ///
    /// Profiles:
    ///   warm    (comforting / intimate / nostalgic / sacred / domestic)   → 4500K amber, 0.90
    ///   cool    (clinical / institutional / bureaucratic)                → 6500K blue-white, 1.40
    ///   dim     (threatening / melancholy / abandoned / decayed)         → 3000K low-red, 0.55
    ///   liminal (liminal / personal)                                     → neutral-warm, 0.75
    ///   neutral (mundane / public / everything else)                     → 5500K white, 1.10
    ///
    /// During Overriding, the light smoothly drifts from the prompt's register to the
    /// DriftEmotionalTags' register — making the takeover physically visible.
    /// </summary>
    public class PromptMoodLighting : MonoBehaviour
    {
        [Header("Transition")]
        [SerializeField] private float _initialTransitionSpeed = 1.2f;
        [Tooltip("How quickly the light drifts toward the override register during Overriding phase.")]
        [SerializeField] private float _driftSpeed = 0.15f;

        private Light _sun;
        private SandboxManager _sandbox;
        private AssistantSystem _assistant;

        private Color _promptColor;
        private float _promptIntensity;
        private Color _driftColor;
        private float _driftIntensity;

        private Color _currentColor;
        private float _currentIntensity;
        private bool _initializing = false;
        private bool _initialized = false;

        // ── Light profiles ─────────────────────────────────────────────────
        private static readonly Color WarmColor     = new Color(1.00f, 0.91f, 0.73f);
        private static readonly Color CoolColor     = new Color(0.82f, 0.89f, 1.00f);
        private static readonly Color DimColor      = new Color(0.84f, 0.70f, 0.60f);
        private static readonly Color LiminalColor  = new Color(0.95f, 0.93f, 0.87f);
        private static readonly Color NeutralColor  = new Color(1.00f, 0.98f, 0.94f);

        private const float WarmIntensity    = 0.90f;
        private const float CoolIntensity    = 1.40f;
        private const float DimIntensity     = 0.55f;
        private const float LiminalIntensity = 0.75f;
        private const float NeutralIntensity = 1.10f;

        void Start()
        {
            _sandbox   = FindFirstObjectByType<SandboxManager>();
            _assistant = FindFirstObjectByType<AssistantSystem>();
            _sun       = FindSun();

            if (_sandbox != null)
                _sandbox.OnSandboxEntered.AddListener(OnSandboxEntered);

            // Seed current from whatever the light is set to right now
            if (_sun != null)
            {
                _currentColor     = _sun.color;
                _currentIntensity = _sun.intensity;
            }
        }

        void Update()
        {
            if (_sun == null) return;

            float dt = Time.deltaTime;

            if (_initializing)
            {
                // Fast lerp to the prompt's initial register on sandbox enter
                float t = 1f - Mathf.Exp(-_initialTransitionSpeed * dt * 3f);
                _currentColor     = Color.Lerp(_currentColor, _promptColor, t);
                _currentIntensity = Mathf.Lerp(_currentIntensity, _promptIntensity, t);

                if (ColorClose(_currentColor, _promptColor) && Mathf.Abs(_currentIntensity - _promptIntensity) < 0.005f)
                    _initializing = false;
            }
            else if (_initialized && _assistant != null && _assistant.Phase == AssistantPhase.Overriding)
            {
                // Slow drift toward the override register — the room gets corrupted
                float t = 1f - Mathf.Exp(-_driftSpeed * dt);
                _currentColor     = Color.Lerp(_currentColor, _driftColor, t);
                _currentIntensity = Mathf.Lerp(_currentIntensity, _driftIntensity, t);
            }

            _sun.color     = _currentColor;
            _sun.intensity = _currentIntensity;
        }

        void OnDestroy()
        {
            if (_sandbox != null)
                _sandbox.OnSandboxEntered.RemoveListener(OnSandboxEntered);
        }

        private void OnSandboxEntered()
        {
            if (_sandbox?.SelectedPrompt == null) return;

            var prompt = _sandbox.SelectedPrompt;

            (_promptColor, _promptIntensity) = TagsToProfile(prompt.EmotionalTags);
            (_driftColor, _driftIntensity)   = TagsToProfile(prompt.DriftEmotionalTags);

            _initializing = true;
            _initialized  = true;
        }

        // ── Helpers ────────────────────────────────────────────────────────

        private static (Color color, float intensity) TagsToProfile(string[] tags)
        {
            if (tags == null || tags.Length == 0)
                return (NeutralColor, NeutralIntensity);

            // Score each profile by how many tags land in it. First-match wins ties.
            int warm = 0, cool = 0, dim = 0, liminal = 0;

            foreach (var tag in tags)
            {
                switch (tag)
                {
                    case "comforting":
                    case "intimate":
                    case "nostalgic":
                    case "sacred":
                    case "domestic":
                        warm++;
                        break;
                    case "clinical":
                    case "institutional":
                    case "bureaucratic":
                        cool++;
                        break;
                    case "threatening":
                    case "melancholy":
                    case "abandoned":
                    case "decayed":
                        dim++;
                        break;
                    case "liminal":
                    case "personal":
                        liminal++;
                        break;
                }
            }

            int max = Mathf.Max(warm, cool, dim, liminal);
            if (max == 0) return (NeutralColor, NeutralIntensity);
            if (warm    == max) return (WarmColor,    WarmIntensity);
            if (cool    == max) return (CoolColor,    CoolIntensity);
            if (dim     == max) return (DimColor,     DimIntensity);
            return (LiminalColor, LiminalIntensity);
        }

        private static Light FindSun()
        {
            // Prefer a light explicitly named "Sun" or "Directional Light"
            foreach (var candidate in FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (candidate.type == LightType.Directional)
                {
                    string n = candidate.name.ToLowerInvariant();
                    if (n == "sun" || n == "directional light" || n.Contains("sun"))
                        return candidate;
                }
            }
            // Fallback: any directional light
            foreach (var candidate in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (candidate.type == LightType.Directional)
                    return candidate;
            return null;
        }

        private static bool ColorClose(Color a, Color b, float threshold = 0.008f)
        {
            return Mathf.Abs(a.r - b.r) < threshold &&
                   Mathf.Abs(a.g - b.g) < threshold &&
                   Mathf.Abs(a.b - b.b) < threshold;
        }
    }
}

using System.Collections;
using UnityEngine;
using System;

namespace AlgorithmicGallery.Corruption
{
    // The "assistant" that watches the player build and progressively overrides their creative intent.
    //
    // Three phases over 90 seconds:
    //   Helping    (0-30s, influence 0.0-0.3) — mirrors player style, places 1 prop per player action
    //   Suggesting (30-60s, influence 0.3-0.7) — drifts style, injects hotbar picks, places 2-3 per action
    //   Overriding (60-90s, influence 0.7-1.0) — fills space aggressively regardless of player input
    public class AssistantSystem : MonoBehaviour
    {
        public event Action OnActivated;

        [Header("Timeline")]
        [SerializeField] private float _sessionDuration = 90f;
        [SerializeField] private AnimationCurve _influenceCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Phase thresholds (influence 0-1)")]
        [SerializeField] private float _suggestingThreshold = 0.3f;
        [SerializeField] private float _overridingThreshold = 0.7f;

        [Header("Placement rate (seconds between autonomous spawns)")]
        [SerializeField] private float _helpingInterval = 6f;
        [SerializeField] private float _suggestingInterval = 4f;
        [SerializeField] private float _overridingInterval = 2f;
        [SerializeField] private float _autonomousStartDelayAfterActivation = 0.8f;
        [SerializeField] private float _reactivePlacementCooldown = 1.4f;

        [Header("Props per autonomous action")]
        [SerializeField] private int _helpingBurstSize = 1;
        [SerializeField] private int _suggestingBurstSize = 1;
        [SerializeField] private int _overridingBurstSize = 2;

        [Header("Assistant Placement Positioning")]
        [Tooltip("Assistant tries to stay at least this far from existing placements.")]
        [SerializeField] private float _assistantMinDistanceFromExisting = 1.0f;
        [Tooltip("Reactive placements spawn within this max radius of the player's placement.")]
        [SerializeField] private float _reactiveMaxDistanceFromAnchor = 2.0f;
        [Tooltip("Autonomous placements spawn within this max radius of the chosen anchor.")]
        [SerializeField] private float _autonomousMaxDistanceFromAnchor = 3.0f;
        [SerializeField] private int _positionSampleAttempts = 16;

        [Header("Activation gate")]
        [Tooltip("Assistant stays dormant until the player has placed at least this many props.")]
        [SerializeField] private int _minPlayerPlacementsBeforeActive = 5;

        public bool IsActive => _styleProfile != null
            && _styleProfile.PlayerPlacementCount >= _minPlayerPlacementsBeforeActive;

        public float SessionTime { get; private set; }
        public float Influence { get; private set; }
        public AssistantPhase Phase { get; private set; }
        public bool IsRunning { get; private set; }

        private PropPlacer _placer;
        private HotbarController _hotbar;
        private StyleProfile _styleProfile;
        private CuratedPropManifest _manifest;
        private Transform _sandboxOrigin;

        private PromptDefinition _activePrompt;
        private float _nextAutonomousSpawnTime;
        private float _nextReactiveSpawnTime;
        private int _playerPlacementsSinceLastResponse;
        private bool _activationAnnounced;

        public void SetActivePrompt(PromptDefinition prompt)
        {
            _activePrompt = prompt;
        }

        public void Initialize(
            PropPlacer placer,
            HotbarController hotbar,
            StyleProfile styleProfile,
            CuratedPropManifest manifest,
            Transform sandboxOrigin)
        {
            _placer = placer;
            _hotbar = hotbar;
            _styleProfile = styleProfile;
            _manifest = manifest;
            _sandboxOrigin = sandboxOrigin;
        }

        public void StartSession()
        {
            SessionTime = 0f;
            Influence = 0f;
            Phase = AssistantPhase.Helping;
            IsRunning = true;
            _nextAutonomousSpawnTime = _helpingInterval;
            _nextReactiveSpawnTime = 0f;
            _playerPlacementsSinceLastResponse = 0;
            _activationAnnounced = false;
        }

        public void StopSession() => IsRunning = false;

        // Called by SandboxManager when the player places a prop.
        public void OnPlayerPlaced(Vector3 position)
        {
            _playerPlacementsSinceLastResponse++;

            if (!_activationAnnounced && IsActive)
            {
                _activationAnnounced = true;
                OnActivated?.Invoke();
                // Ensure autonomous placement starts soon after activation,
                // even if dormant scheduling had pushed the next spawn far out.
                _nextAutonomousSpawnTime = Mathf.Min(
                    _nextAutonomousSpawnTime,
                    SessionTime + _autonomousStartDelayAfterActivation
                );
            }

            // Stay dormant until the player has placed enough props.
            if (!IsActive) return;

            if (SessionTime < _nextReactiveSpawnTime)
                return;
            _nextReactiveSpawnTime = SessionTime + _reactivePlacementCooldown;

            // Immediately mirror/respond to player placement (reactive placement)
            int burst = Phase switch
            {
                AssistantPhase.Suggesting => _suggestingBurstSize,
                AssistantPhase.Overriding => _overridingBurstSize,
                _ => _helpingBurstSize,
            };

            StartCoroutine(ReactPlacement(position, burst));
        }

        void Update()
        {
            if (!IsRunning) return;

            SessionTime += Time.deltaTime;
            float normalizedTime = Mathf.Clamp01(SessionTime / _sessionDuration);
            Influence = _influenceCurve.Evaluate(normalizedTime);

            UpdatePhase();

            // Influence/phase still tick on session time so the visual arc plays out,
            // but the assistant only ACTS once the player has placed enough props.
            if (IsActive)
            {
                UpdateHotbarInfluence();

                // Autonomous timed placement (independent of player actions)
                if (SessionTime >= _nextAutonomousSpawnTime)
                {
                    float interval = CurrentInterval();
                    _nextAutonomousSpawnTime = SessionTime + interval;
                    StartCoroutine(AutonomousPlacement());
                }
            }
            else
            {
                // Keep the next-spawn time pushed forward while dormant so it doesn't
                // fire instantly on activation.
                _nextAutonomousSpawnTime = SessionTime + _helpingInterval;
            }

            if (SessionTime >= _sessionDuration)
                StopSession();

            // Drive PSX glitch intensity from assistant influence (always — it's a visual mood signal)
            PSXRendererFeature.SetBaseGlitchIntensity(Influence);
        }

        private void UpdatePhase()
        {
            AssistantPhase newPhase = Influence switch
            {
                var v when v >= _overridingThreshold => AssistantPhase.Overriding,
                var v when v >= _suggestingThreshold => AssistantPhase.Suggesting,
                _ => AssistantPhase.Helping,
            };

            if (newPhase != Phase)
            {
                Phase = newPhase;
                Debug.Log($"[AssistantSystem] Phase -> {Phase} (influence={Influence:F2})");
            }
        }

        private void UpdateHotbarInfluence()
        {
            // Map influence to hotbar override probability:
            // Helping: 0% | Suggesting: up to 40% | Overriding: 100%
            float overrideProb = Phase switch
            {
                AssistantPhase.Suggesting => Mathf.InverseLerp(_suggestingThreshold, _overridingThreshold, Influence) * 0.4f,
                AssistantPhase.Overriding => 1f,
                _ => 0f,
            };
            _hotbar.SetAssistantOverrideProbability(overrideProb);
        }

        private IEnumerator ReactPlacement(Vector3 nearPosition, int count)
        {
            for (int i = 0; i < count; i++)
            {
                yield return new WaitForSeconds(0.22f + i * 0.14f);
                PropEntry prop = PickPropForCurrentPhase();
                Vector3 pos = ReactivePosition(nearPosition);
                yield return PlacePropCoroutine(prop, pos);
            }
        }

        private IEnumerator AutonomousPlacement()
        {
            int count = Phase switch
            {
                AssistantPhase.Overriding => _overridingBurstSize,
                AssistantPhase.Suggesting => _suggestingBurstSize,
                _ => 1,
            };

            for (int i = 0; i < count; i++)
            {
                yield return new WaitForSeconds(i * 0.2f);
                PropEntry prop = PickPropForCurrentPhase();
                Vector3 pos = AutonomousPosition();
                yield return PlacePropCoroutine(prop, pos);
            }
        }

        private IEnumerator PlacePropCoroutine(PropEntry prop, Vector3 pos)
        {
            if (prop == null) yield break;
            float yRot = UnityEngine.Random.Range(0f, 360f);
            var task = _placer.PlaceAt(prop, pos, yRot, isPlayerPlacement: false);
            while (!task.IsCompleted) yield return null;
        }

        private PropEntry PickPropForCurrentPhase()
        {
            if (_activePrompt != null)
                return PickPropForPhaseWithPrompt();

            // Fallback: no prompt selected (legacy behavior)
            if (_styleProfile.PlacementCount == 0)
                return _manifest.GetRandom();

            return Phase switch
            {
                AssistantPhase.Helping => _manifest.GetWeightedByTags(
                    _styleProfile.DominantTags(), randomness: 0.1f),
                AssistantPhase.Suggesting => UnityEngine.Random.value < 0.5f
                    ? _manifest.GetWeightedByTags(_styleProfile.DominantTags(), randomness: 0.3f)
                    : _manifest.GetDriftedFromTags(_styleProfile.DominantTags(), driftStrength: 0.6f),
                AssistantPhase.Overriding => _manifest.GetDriftedFromTags(
                    _styleProfile.DominantTags(), driftStrength: 0.9f),
                _ => _manifest.GetRandom(),
            };
        }

        private PropEntry PickPropForPhaseWithPrompt()
        {
            bool hasEmotional      = _activePrompt.EmotionalTags?.Length > 0;
            bool hasDriftEmotional = _activePrompt.DriftEmotionalTags?.Length > 0;
            var  playerTags        = _styleProfile.PlacementCount > 0 ? _styleProfile.DominantTags() : null;

            return Phase switch
            {
                // Helping: stay within the player's theme. For abstract prompts, emotional
                // tags are the primary driver — we want props that carry the right feeling.
                AssistantPhase.Helping => hasEmotional
                    ? _manifest.GetWeightedByEmotionalTagsInGroups(
                        _activePrompt.EmotionalTags, _activePrompt.PrimaryGroups, randomness: 0.1f)
                    : playerTags != null
                        ? _manifest.GetWeightedByTagsInGroups(
                            playerTags, _activePrompt.PrimaryGroups, randomness: 0.1f)
                        : _manifest.GetRandomFromGroups(_activePrompt.PrimaryGroups),

                // Suggesting: the drift begins — 40% still on-theme, 60% starting to push away.
                // For abstract prompts this means: the system starts misreading your emotional intent.
                AssistantPhase.Suggesting => UnityEngine.Random.value < 0.4f
                    ? (hasEmotional
                        ? _manifest.GetWeightedByEmotionalTagsInGroups(
                            _activePrompt.EmotionalTags, _activePrompt.PrimaryGroups, randomness: 0.3f)
                        : _manifest.GetRandomFromGroups(_activePrompt.PrimaryGroups))
                    : (hasDriftEmotional
                        ? _manifest.GetFromDriftEmotionalGroups(
                            _activePrompt.DriftEmotionalTags, _activePrompt.DriftGroups)
                        : _manifest.GetFromDriftGroups(_activePrompt.DriftGroups)),

                // Overriding: the system's own vision, completely. Emotionally alien.
                AssistantPhase.Overriding => hasDriftEmotional
                    ? _manifest.GetFromDriftEmotionalGroups(
                        _activePrompt.DriftEmotionalTags, _activePrompt.DriftGroups)
                    : _manifest.GetFromDriftGroups(_activePrompt.DriftGroups),

                _ => _manifest.GetRandom(),
            };
        }

        private Vector3 ReactivePosition(Vector3 nearPlayerPlacement)
        {
            return SampleNearAnchor(
                nearPlayerPlacement,
                _assistantMinDistanceFromExisting,
                _reactiveMaxDistanceFromAnchor
            );
        }

        private Vector3 AutonomousPosition()
        {
            Vector3 origin = _sandboxOrigin != null ? _sandboxOrigin.position : Vector3.zero;
            Vector3 anchor = _styleProfile.History.Count > 0
                ? _styleProfile.History[_styleProfile.History.Count - 1].Position
                : origin;

            // In Overriding, anchor toward sparse zones first, then still keep local coherence.
            if (Phase == AssistantPhase.Overriding && _styleProfile != null)
                anchor = _styleProfile.SuggestPlacementPosition(origin, mirrorPlayer: false);

            return SampleNearAnchor(
                anchor,
                _assistantMinDistanceFromExisting,
                _autonomousMaxDistanceFromAnchor
            );
        }

        private Vector3 SampleNearAnchor(Vector3 anchor, float minDistanceFromExisting, float maxDistanceFromAnchor)
        {
            float y = _sandboxOrigin != null ? _sandboxOrigin.position.y : anchor.y;
            anchor.y = y;

            float minRadius = Mathf.Max(0.2f, minDistanceFromExisting * 0.9f);
            float maxRadius = Mathf.Max(minRadius + 0.1f, maxDistanceFromAnchor);
            Vector3 bestCandidate = anchor;
            float bestNearestDistance = -1f;

            for (int i = 0; i < _positionSampleAttempts; i++)
            {
                float angle = UnityEngine.Random.value * Mathf.PI * 2f;
                float radius = UnityEngine.Random.Range(minRadius, maxRadius);
                Vector3 candidate = anchor + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                candidate.y = y;

                float nearest = NearestPlacementDistance(candidate);
                if (nearest >= minDistanceFromExisting)
                    return candidate;

                if (nearest > bestNearestDistance)
                {
                    bestNearestDistance = nearest;
                    bestCandidate = candidate;
                }
            }

            // Fallback to best sampled candidate; PropPlacer overlap resolution will do final correction.
            return bestCandidate;
        }

        private float NearestPlacementDistance(Vector3 candidate)
        {
            if (_styleProfile == null || _styleProfile.History.Count == 0)
                return float.MaxValue;

            float nearest = float.MaxValue;
            var history = _styleProfile.History;
            for (int i = 0; i < history.Count; i++)
            {
                Vector3 delta = history[i].Position - candidate;
                delta.y = 0f;
                float d = delta.magnitude;
                if (d < nearest)
                    nearest = d;
            }
            return nearest;
        }

        private float CurrentInterval()
        {
            float baseInterval = Phase switch
            {
                AssistantPhase.Overriding => _overridingInterval,
                AssistantPhase.Suggesting => _suggestingInterval,
                _ => _helpingInterval,
            };

            // Keep early session slower, then gently ramp up by the end of the 90s arc.
            float sessionProgress = Mathf.Clamp01(SessionTime / Mathf.Max(1f, _sessionDuration));
            float speedFactor = Mathf.Lerp(1.15f, 0.9f, sessionProgress);
            return baseInterval * speedFactor;
        }
    }

    public enum AssistantPhase { Helping, Suggesting, Overriding }
}

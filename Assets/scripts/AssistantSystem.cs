using System.Collections;
using UnityEngine;
using System;

namespace AlgorithmicGallery.Corruption
{
    // The "assistant" that watches the player build and progressively overrides their creative intent.
    //
    // Influence/phase still advance on session time for visuals (glitch, lighting), but the assistant
    // only places props after a randomized player-placement threshold (default 8–12) and never
    // exceeds a small cap of assistant-owned placements per sandbox.
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
        [SerializeField] private float _helpingInterval = 10f;
        [SerializeField] private float _suggestingInterval = 7f;
        [SerializeField] private float _overridingInterval = 4.5f;
        [SerializeField] private float _autonomousStartDelayAfterActivation = 0.75f;
        [SerializeField] private float _reactivePlacementCooldown = 0.2f;

        [Header("Props per autonomous action")]
        [SerializeField] private int _helpingBurstSize = 1;
        [SerializeField] private int _suggestingBurstSize = 1;
        [SerializeField] private int _overridingBurstSize = 1;

        [Header("Assistant Placement Positioning")]
        [Tooltip("Assistant tries to stay at least this far from existing placements.")]
        [SerializeField] private float _assistantMinDistanceFromExisting = 1.0f;
        [Tooltip("Reactive placements spawn within this max radius of the player's placement.")]
        [SerializeField] private float _reactiveMaxDistanceFromAnchor = 2.0f;
        [Tooltip("Autonomous placements spawn within this max radius of the chosen anchor.")]
        [SerializeField] private float _autonomousMaxDistanceFromAnchor = 3.0f;
        [SerializeField] private int _positionSampleAttempts = 16;

        [Header("Activation gate")]
        [Tooltip("Random inclusive range: assistant activates after this many player placements (rolled once per session).")]
        [SerializeField] private int _activationMinPlayerPlacements = 4;
        [SerializeField] private int _activationMaxPlayerPlacements = 6;

        [Header("Assistant placement cap")]
        [Tooltip("Maximum assistant placements (player + assistant total is capped separately by SandboxManager).")]
        [SerializeField] private int _maxAssistantPlacements = 25;

        [Header("Replacement behavior")]
        [Tooltip("Chance that an assistant placement replaces one of the player's current props when the session has just begun.")]
        [SerializeField, Range(0f, 1f)] private float _replacementChanceEarly = 0.18f;
        [Tooltip("Chance that an assistant placement replaces one of the player's current props near the end of the session.")]
        [SerializeField, Range(0f, 1f)] private float _replacementChanceLate = 1f;
        [Tooltip("Target number of player-owned props left in the sandbox near the end of the player's 25 placements.")]
        [SerializeField] private int _lateSessionPlayerRemainderTarget = 2;

        public bool IsActive => _styleProfile != null
            && _styleProfile.PlayerPlacementCount >= _activationPlayerThreshold;

        public float SessionTime { get; private set; }
        public float Influence { get; private set; }
        public AssistantPhase Phase { get; private set; }
        public bool IsRunning { get; private set; }

        private PropPlacer _placer;
        private HotbarController _hotbar;
        private StyleProfile _styleProfile;
        private CuratedPropManifest _manifest;
        private Transform _sandboxOrigin;
        private SandboxManager _sandbox;

        private PromptDefinition _activePrompt;
        private float _nextAutonomousSpawnTime;
        private float _nextReactiveSpawnTime;
        private int _playerPlacementsSinceLastResponse;
        private bool _activationAnnounced;
        private int _activationPlayerThreshold = 10;

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
            _sandbox = FindFirstObjectByType<SandboxManager>();
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

            int min = Mathf.Min(_activationMinPlayerPlacements, _activationMaxPlayerPlacements);
            int max = Mathf.Max(_activationMinPlayerPlacements, _activationMaxPlayerPlacements);
            _activationPlayerThreshold = UnityEngine.Random.Range(min, max + 1);
            Debug.Log($"[AssistantSystem] Will activate after {_activationPlayerThreshold} player placements (range {min}-{max}).");
        }

        public void StopSession() => IsRunning = false;

        // Called by SandboxManager when the player places a prop.
        public void OnPlayerPlaced(Vector3 position)
        {
            if (!IsRunning) return;

            _playerPlacementsSinceLastResponse++;

            if (!_activationAnnounced && IsActive)
            {
                _activationAnnounced = true;
                OnActivated?.Invoke();
                GameplayEventDebugLog.Push("Assistant", "OnActivated");
                // Ensure autonomous placement starts soon after activation,
                // even if dormant scheduling had pushed the next spawn far out.
                _nextAutonomousSpawnTime = Mathf.Min(
                    _nextAutonomousSpawnTime,
                    SessionTime + _autonomousStartDelayAfterActivation
                );
            }

            // Stay dormant until the player has placed enough props.
            if (!IsActive) return;

            if (!AssistantCanPlaceMore()) return;

            if (SessionTime < _nextReactiveSpawnTime)
                return;
            _nextReactiveSpawnTime = SessionTime + _reactivePlacementCooldown;

            // Shadow the player's actions and ramp up the system's response later in the run
            // so only a couple of player props survive by the end.
            StartCoroutine(ReactPlacement(position, DetermineReactiveBurstCount()));
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
                if (SessionTime >= _nextAutonomousSpawnTime && AssistantCanPlaceMore())
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
            if (!AssistantCanPlaceMore()) yield break;

            for (int i = 0; i < count; i++)
            {
                if (!AssistantCanPlaceMore()) yield break;
                yield return new WaitForSeconds(0.22f + i * 0.16f);
                PropEntry prop = PickPropForCurrentPhase();
                Vector3 pos = ReactivePosition(nearPosition);
                yield return PlacePropCoroutine(prop, pos);
            }
        }

        private IEnumerator AutonomousPlacement()
        {
            if (!AssistantCanPlaceMore()) yield break;

            int count = 1;

            for (int i = 0; i < count; i++)
            {
                if (!AssistantCanPlaceMore()) yield break;
                yield return new WaitForSeconds(i * 0.55f);
                PropEntry prop = PickPropForCurrentPhase();
                Vector3 pos = AutonomousPosition();
                yield return PlacePropCoroutine(prop, pos);
            }
        }

        private IEnumerator PlacePropCoroutine(PropEntry prop, Vector3 pos)
        {
            if (!AssistantCanPlaceMore()) yield break;
            if (prop == null) yield break;
            float yRot = UnityEngine.Random.Range(0f, 360f);
            Vector3 spawnPos = pos;

            if (ShouldReplacePlayerProp() &&
                PropBudget.Instance != null &&
                PropBudget.Instance.TryRemoveRandomPlayerPlaced(out Vector3 replacedPos, out float replacedYaw))
            {
                spawnPos = replacedPos;
                yRot = replacedYaw;
                GameplayEventDebugLog.Push("Assistant", $"replaced player prop at {spawnPos}");
            }

            var task = _placer.PlaceAt(prop, spawnPos, yRot, isPlayerPlacement: false);
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
            if (_manifest == null)
                return null;

            PromptScoringHelper.EnsureCorporateTarget(_activePrompt);
            string corp = _activePrompt.CorporateTargetTag?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(corp))
                corp = "marketable";

            // Corporate-only selection: no phase drift, no player StyleProfile weighting.
            // Pool is prompt primary groups when set; otherwise entire manifest.
            string[] groups = _activePrompt.PrimaryGroups;
            if (groups == null || groups.Length == 0)
                groups = null;

            const float randomness = 0.06f;
            var pick = _manifest.GetWeightedByCorporateTagInGroups(
                corp, groups, secondaryEmotionalTags: null, randomness: randomness);
            if (pick != null)
                return pick;

            pick = _manifest.GetWeightedByCorporateTagInGroups(
                corp, groups: null, secondaryEmotionalTags: null, randomness: randomness);
            if (pick != null)
                return pick;

            if (_activePrompt.PrimaryGroups != null && _activePrompt.PrimaryGroups.Length > 0)
                return _manifest.GetRandomFromGroups(_activePrompt.PrimaryGroups);

            return _manifest.GetRandom();
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

            // Keep early session slower, then gently ramp up by the end of the influence arc.
            float sessionProgress = Mathf.Clamp01(SessionTime / Mathf.Max(1f, _sessionDuration));
            float speedFactor = Mathf.Lerp(1.15f, 0.9f, sessionProgress);
            return baseInterval * speedFactor;
        }

        private bool ShouldReplacePlayerProp()
        {
            if (PropBudget.Instance == null || _styleProfile == null)
                return false;

            PropBudget.Instance.GetTrackedCounts(out int playerCount, out _);
            if (playerCount <= 0)
                return false;

            int maxPlayerPlacements = _sandbox != null ? Mathf.Max(1, _sandbox.MaxPlayerPlacements) : 25;
            float progress = Mathf.Clamp01(_styleProfile.PlayerPlacementCount / (float)maxPlayerPlacements);
            int desiredPlayerCount = Mathf.RoundToInt(Mathf.Lerp(maxPlayerPlacements, _lateSessionPlayerRemainderTarget, progress));

            if (playerCount > desiredPlayerCount)
                return true;

            float chance = Mathf.Lerp(_replacementChanceEarly, _replacementChanceLate, progress);
            return UnityEngine.Random.value < chance;
        }

        private int DetermineReactiveBurstCount()
        {
            if (PropBudget.Instance == null || _styleProfile == null)
                return 1;

            int maxPlayerPlacements = _sandbox != null ? Mathf.Max(1, _sandbox.MaxPlayerPlacements) : 25;
            float progress = Mathf.Clamp01(_styleProfile.PlayerPlacementCount / (float)maxPlayerPlacements);

            PropBudget.Instance.GetTrackedCounts(out int playerCount, out _);
            int desiredPlayerCount = Mathf.RoundToInt(Mathf.Lerp(maxPlayerPlacements, _lateSessionPlayerRemainderTarget, progress));
            int excessPlayerCount = Mathf.Max(0, playerCount - desiredPlayerCount);

            int maxBurst = progress switch
            {
                >= 0.82f => 3,
                >= 0.58f => 2,
                _ => 1
            };

            return Mathf.Clamp(Mathf.Max(1, excessPlayerCount), 1, maxBurst);
        }

        private bool AssistantCanPlaceMore()
        {
            if (_styleProfile == null || _placer == null)
                return false;
            if (!_placer.PlacementEnabled)
                return false;
            return _styleProfile.AssistantPlacementCount < _maxAssistantPlacements;
        }
    }

    public enum AssistantPhase { Helping, Suggesting, Overriding }
}

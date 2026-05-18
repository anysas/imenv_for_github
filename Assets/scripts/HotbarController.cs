using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AlgorithmicGallery.Corruption
{
    // Manages 3 prop slots. Player selects with 1-3 keys; after each placement a short "thinking"
    // delay runs before the used slot rerolls and selection advances.
    // AssistantSystem can override slot contents during the Suggesting/Overriding phase.
    public class HotbarController : MonoBehaviour
    {
        public const int SlotCount = 3;

        [SerializeField] private float _assistantOverrideProbability = 0f; // driven by AssistantSystem

        public PropEntry[] Slots { get; } = new PropEntry[SlotCount];
        public int ActiveSlot { get; private set; } = 0;
        public PropEntry ActiveProp => Slots[ActiveSlot];

        public event Action<int, PropEntry> OnSlotChanged;   // slot index, new prop
        public event Action<int> OnActiveSlotChanged;        // slot index
        /// <summary>True while waiting after a player placement before the next hotbar reroll.</summary>
        public event Action<bool> OnThinkingStateChanged;

        public bool IsInPostPlacementThinking { get; private set; }

        private CuratedPropManifest _manifest;
        private StyleProfile _styleProfile;
        private PromptDefinition _activePrompt;
        private readonly Queue<string> _seedQueue = new();
        private Coroutine _postPlacementThinkingRoutine;
        private bool _simpleRandomPicks;
        private int _maxSessionPlacements = int.MaxValue;
        private readonly HashSet<string> _placedPropIds = new();

        public void Initialize(
            CuratedPropManifest manifest,
            StyleProfile styleProfile,
            bool simpleRandomPicks = false,
            int maxSessionPlacements = int.MaxValue)
        {
            _manifest = manifest;
            _styleProfile = styleProfile;
            _simpleRandomPicks = simpleRandomPicks;
            _maxSessionPlacements = Mathf.Max(1, maxSessionPlacements);
            _placedPropIds.Clear();
            for (int i = 0; i < SlotCount; i++)
                Reroll(i);
        }

        /// <summary>Marks a player-placed prop so rerolls prefer unused catalog entries.</summary>
        public void RegisterPlayerPlacedProp(PropEntry prop)
        {
            string id = ResolvePropId(prop);
            if (string.IsNullOrEmpty(id)) return;
            _placedPropIds.Add(id);
        }

        public void SetActivePrompt(PromptDefinition prompt)
        {
            _activePrompt = prompt;
            _seedQueue.Clear();
            BuildSeedQueueFromPrompt(prompt);
            for (int i = 0; i < SlotCount; i++)
                Reroll(i);
        }

        void Update()
        {
            if (_manifest == null) return;
            if (IsInPostPlacementThinking) return;

            // Number keys 1-3
            for (int i = 0; i < SlotCount; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i))
                    SetActiveSlot(i);
            }

            // Scroll wheel — up = left slot, down = right slot (Minecraft convention)
            float scroll = Input.GetAxisRaw("Mouse ScrollWheel");
            if (scroll > 0f)
                SetActiveSlot((ActiveSlot - 1 + SlotCount) % SlotCount);
            else if (scroll < 0f)
                SetActiveSlot((ActiveSlot + 1) % SlotCount);
        }

        public void SetActiveSlot(int index)
        {
            if (index < 0 || index >= SlotCount) return;
            ActiveSlot = index;
            OnActiveSlotChanged?.Invoke(index);
            GameplayEventDebugLog.Push("Hotbar", $"active slot → {index + 1}");
        }

        /// <summary>
        /// After a successful player placement: blocks hotbar input for <paramref name="durationSeconds"/>,
        /// then rerolls every slot and advances selection to the next slot.
        /// </summary>
        public void BeginPostPlacementThinking(float durationSeconds)
        {
            if (_postPlacementThinkingRoutine != null)
                StopCoroutine(_postPlacementThinkingRoutine);
            _postPlacementThinkingRoutine = StartCoroutine(PostPlacementThinkingRoutine(durationSeconds));
        }

        private IEnumerator PostPlacementThinkingRoutine(float durationSeconds)
        {
            IsInPostPlacementThinking = true;
            OnThinkingStateChanged?.Invoke(true);
            GameplayEventDebugLog.Push("Hotbar", "thinking ON (post-placement)");

            float wait = Mathf.Max(0.01f, durationSeconds);
            yield return new WaitForSecondsRealtime(wait);

            IsInPostPlacementThinking = false;
            OnThinkingStateChanged?.Invoke(false);
            GameplayEventDebugLog.Push("Hotbar", "thinking OFF → reroll slots");

            for (int i = 0; i < SlotCount; i++)
                Reroll(i);
            SetActiveSlot((ActiveSlot + 1) % SlotCount);

            _postPlacementThinkingRoutine = null;
        }

        // AssistantSystem calls this to pre-select a slot for the player during Suggesting phase.
        public void ForceSlotContent(int slotIndex, PropEntry prop)
        {
            if (slotIndex < 0 || slotIndex >= SlotCount) return;
            Slots[slotIndex] = prop;
            OnSlotChanged?.Invoke(slotIndex, prop);
        }

        // Set how likely the assistant is to inject its own picks into rerolled slots (0-1).
        public void SetAssistantOverrideProbability(float probability)
        {
            _assistantOverrideProbability = Mathf.Clamp01(probability);
        }

        private void Reroll(int index)
        {
            if (_manifest == null) return;

            var excludeIds = BuildRerollExcludeIds(index);

            if (_simpleRandomPicks)
            {
                Slots[index] = _manifest.GetRandomExcluding(excludeIds);
                OnSlotChanged?.Invoke(index, Slots[index]);
                return;
            }

            PropEntry pick;
            bool assistantOverrides = UnityEngine.Random.value < _assistantOverrideProbability;

            // Faithful opening tools: consume deterministic seed queue first.
            if (!assistantOverrides && TryConsumeSeed(out var seeded))
            {
                pick = seeded;
            }
            else if (assistantOverrides && _activePrompt != null)
            {
                bool hasDriftEmotional = _activePrompt.DriftEmotionalTags?.Length > 0;
                pick = hasDriftEmotional
                    ? _manifest.GetFromDriftEmotionalGroups(
                        _activePrompt.DriftEmotionalTags, _activePrompt.DriftGroups)
                    : _manifest.GetFromDriftGroups(_activePrompt.DriftGroups);
            }
            else if (_activePrompt != null && !string.IsNullOrWhiteSpace(_activePrompt.CorporateTargetTag) && UnityEngine.Random.value < 0.52f)
            {
                float rand = _activePrompt.IsAbstract ? 0.08f : 0.16f;
                pick = _manifest.GetWeightedByCorporateTagInGroups(
                    _activePrompt.CorporateTargetTag,
                    _activePrompt.PrimaryGroups,
                    _activePrompt.EmotionalTags,
                    randomness: rand,
                    excludeIds: excludeIds);
            }
            else if (_activePrompt != null && _activePrompt.EmotionalTags?.Length > 0)
            {
                // Emotional/abstract prompt: drive by emotional vocabulary.
                // Lower randomness for abstract prompts — emotional precision matters.
                float rand = _activePrompt.IsAbstract ? 0.08f : 0.18f;
                pick = _manifest.GetWeightedByEmotionalTagsInGroups(
                    _activePrompt.EmotionalTags, _activePrompt.PrimaryGroups,
                    randomness: rand, excludeIds: excludeIds);
            }
            else if (_activePrompt != null && _styleProfile.PlacementCount > 0)
            {
                pick = _manifest.GetWeightedByTagsInGroups(
                    _styleProfile.DominantTags(), _activePrompt.PrimaryGroups,
                    randomness: 0.30f, excludeIds: excludeIds);
            }
            else if (_activePrompt != null)
            {
                // Abstract prompts: prefer high-confidence props even for random fallback.
                pick = _activePrompt.IsAbstract
                    ? _manifest.GetRandomHighConfFromGroups(_activePrompt.PrimaryGroups, excludeIds)
                    : _manifest.GetRandomFromGroups(_activePrompt.PrimaryGroups, excludeIds);
            }
            else
            {
                pick = _manifest.GetRandomHighConf();
            }

            Slots[index] = pick;
            OnSlotChanged?.Invoke(index, pick);
        }

        private void BuildSeedQueueFromPrompt(PromptDefinition prompt)
        {
            if (_manifest == null || prompt == null) return;

            var plan = PromptToolResolver.BuildSeedPlan(_manifest, prompt);
            var merged = plan.Combined().ToList();
            prompt.ResolvedSeedPropIds = merged.ToArray();
            for (int i = 0; i < merged.Count; i++)
                _seedQueue.Enqueue(merged[i]);
        }

        private bool TryConsumeSeed(out PropEntry prop)
        {
            prop = null;
            if (_manifest == null || _seedQueue.Count == 0) return false;

            while (_seedQueue.Count > 0)
            {
                string id = _seedQueue.Dequeue();
                if (ShouldExcludePlacedProps() && _placedPropIds.Contains(id))
                    continue;
                var candidate = _manifest.GetById(id);
                if (candidate == null) continue;
                prop = candidate;
                return true;
            }

            return false;
        }

        private HashSet<string> BuildRerollExcludeIds(int slotIndex)
        {
            var excludeIds = new HashSet<string>();
            for (int i = 0; i < SlotCount; i++)
            {
                if (i == slotIndex || Slots[i] == null) continue;
                string id = ResolvePropId(Slots[i]);
                if (!string.IsNullOrEmpty(id))
                    excludeIds.Add(id);
            }

            if (ShouldExcludePlacedProps())
            {
                foreach (string placedId in _placedPropIds)
                    excludeIds.Add(placedId);
            }

            return excludeIds;
        }

        /// <summary>
        /// Keeps placed props out of the hotbar while enough unused catalog entries remain
        /// for the rest of the session and the current 3-slot roll.
        /// </summary>
        private bool ShouldExcludePlacedProps()
        {
            if (_manifest == null || _manifest.Count == 0 || _placedPropIds.Count == 0)
                return false;

            int unusedCount = _manifest.Count - _placedPropIds.Count;
            if (unusedCount <= 0)
                return false;

            int placementsLeft = _maxSessionPlacements;
            if (_styleProfile != null)
                placementsLeft = Mathf.Max(0, _maxSessionPlacements - _styleProfile.PlayerPlacementCount);

            return unusedCount >= placementsLeft;
        }

        private static string ResolvePropId(PropEntry prop)
        {
            if (prop == null) return null;
            if (!string.IsNullOrEmpty(prop.Id)) return prop.Id;
            return prop.GlbPath;
        }
    }
}

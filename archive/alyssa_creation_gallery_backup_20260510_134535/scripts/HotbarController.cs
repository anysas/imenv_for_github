using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;

namespace AlgorithmicGallery.Corruption
{
    // Manages 5 prop slots. Player selects with 1-5 keys, slots reroll after placement.
    // AssistantSystem can override slot contents during the Suggesting/Overriding phase.
    public class HotbarController : MonoBehaviour
    {
        public const int SlotCount = 5;

        [SerializeField] private float _assistantOverrideProbability = 0f; // driven by AssistantSystem

        public PropEntry[] Slots { get; } = new PropEntry[SlotCount];
        public int ActiveSlot { get; private set; } = 0;
        public PropEntry ActiveProp => Slots[ActiveSlot];

        public event Action<int, PropEntry> OnSlotChanged;   // slot index, new prop
        public event Action<int> OnActiveSlotChanged;        // slot index

        private CuratedPropManifest _manifest;
        private StyleProfile _styleProfile;
        private PromptDefinition _activePrompt;
        private readonly Queue<string> _seedQueue = new();

        public void Initialize(CuratedPropManifest manifest, StyleProfile styleProfile)
        {
            _manifest = manifest;
            _styleProfile = styleProfile;
            for (int i = 0; i < SlotCount; i++)
                Reroll(i);
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

            // Number keys 1-5
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
        }

        // Called by PropPlacer after a prop is placed. Rerolls the consumed slot.
        public void ConsumeActiveSlot()
        {
            Reroll(ActiveSlot);
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

            // Collect IDs of props in other slots to avoid duplicates
            var excludeIds = new HashSet<string>();
            for (int i = 0; i < SlotCount; i++)
            {
                if (i != index && Slots[i] != null)
                    excludeIds.Add(Slots[i].Id);
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
                var candidate = _manifest.GetById(id);
                if (candidate == null) continue;
                prop = candidate;
                return true;
            }

            return false;
        }
    }
}

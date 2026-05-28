using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace AlgorithmicGallery
{
    /// <summary>
    /// Runtime controller for one generated room instance.
    /// Owns trigger responses and sculpture slot anchors.
    /// </summary>
    public class RoomRuntime : MonoBehaviour
    {
        [Header("Sculpture Anchors")]
        [SerializeField]
        private Transform _leftSculptureSlot;
        [SerializeField]
        private Transform _rightSculptureSlot;

        [Header("Door Runtime")]
        [SerializeField]
        private RoomDoor _entryDoor;
        [SerializeField]
        private RoomDoorTrigger _entryTrigger;
        [SerializeField]
        private RoomDoorTrigger _advanceTrigger;
        [FormerlySerializedAs("_leftChoiceTrigger")]
        [SerializeField]
        private RoomDoorTrigger _legacyChoiceLeftTrigger;
        [FormerlySerializedAs("_rightChoiceTrigger")]
        [SerializeField]
        private RoomDoorTrigger _legacyChoiceRightTrigger;
        [SerializeField]
        private bool _closeEntryDoorOnEnter = true;
        [FormerlySerializedAs("_lockChoiceAfterSelection")]
        [SerializeField]
        private bool _lockAdvanceAfterTrigger = true;

        [Header("Generation Anchors")]
        [SerializeField]
        private Transform _nextRoomSpawnPoint;
        [SerializeField]
        private Transform _roomEntryAnchor;
        [Header("Validation")]
        [SerializeField]
        private bool _allowAutoAssignBindings = true;

        private bool _hasEntered;
        private bool _hasAdvanced;

        public event Action<RoomRuntime> EnteredRoom;
        public event Action<RoomRuntime> AdvanceTriggered;

        private void Awake()
        {
            ValidateRoomConfiguration();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            ValidateRoomConfiguration();
        }
#endif

        public void Initialize(int roomIndex)
        {
            RoomIndex = roomIndex;
            _hasEntered = false;
            _hasAdvanced = false;

            if (_entryDoor != null)
                _entryDoor.SetClosed(false, instant: true);

            SetAdvanceTriggersLocked(false);
            if (_entryTrigger != null)
                _entryTrigger.SetLocked(false);

            ValidateRoomConfiguration();
        }

        public void NotifyDoorTriggered(RoomDoorTriggerType triggerType)
        {
            if (triggerType == RoomDoorTriggerType.Entry)
            {
                HandleEntryTrigger();
                return;
            }

            // If the entry trigger was missed, recover by treating first advance hit as room entry.
            if (!_hasEntered)
            {
                HandleEntryTrigger();
            }

            if (_hasAdvanced)
                return;

            switch (triggerType)
            {
                case RoomDoorTriggerType.Advance:
                case RoomDoorTriggerType.ChoiceRight:
                    HandleAdvanceTrigger();
                    break;
            }
        }

        private void HandleEntryTrigger()
        {
            if (_hasEntered)
                return;

            _hasEntered = true;
            if (_closeEntryDoorOnEnter && _entryDoor != null)
            {
                _entryDoor.SetClosed(true);
            }

            EnteredRoom?.Invoke(this);
        }

        private void HandleAdvanceTrigger()
        {
            _hasAdvanced = true;

            if (_lockAdvanceAfterTrigger)
            {
                SetAdvanceTriggersLocked(true);
            }

            AdvanceTriggered?.Invoke(this);
        }

        private void SetAdvanceTriggersLocked(bool locked)
        {
            if (_advanceTrigger != null)
                _advanceTrigger.SetLocked(locked);

            // Backward compatibility with prefabs still using the old trigger fields.
            if (_legacyChoiceLeftTrigger != null)
                _legacyChoiceLeftTrigger.SetLocked(locked);
            if (_legacyChoiceRightTrigger != null)
                _legacyChoiceRightTrigger.SetLocked(locked);
        }

        private void ValidateRoomConfiguration()
        {
            if (_allowAutoAssignBindings)
            {
                AutoAssignBindingsStrict();
            }

            if (_entryTrigger == null)
            {
                Debug.LogWarning($"RoomRuntime '{name}': Missing entry trigger reference.");
            }
            if (_advanceTrigger == null && _legacyChoiceLeftTrigger == null && _legacyChoiceRightTrigger == null)
            {
                Debug.LogWarning($"RoomRuntime '{name}': Missing advance trigger reference.");
            }
            if (_nextRoomSpawnPoint == null)
            {
                Debug.LogWarning($"RoomRuntime '{name}': Missing NextRoomSpawnPoint. Room chaining will fail in TriggerAnchorChain mode.");
            }
            if (_leftSculptureSlot == null && _rightSculptureSlot == null)
            {
                Debug.LogWarning($"RoomRuntime '{name}': No sculpture slots assigned. GalleryManager will not spawn sculptures for this room.");
            }
        }

        private void AutoAssignBindingsStrict()
        {
            if (_entryTrigger == null || _advanceTrigger == null)
            {
                var triggers = GetComponentsInChildren<RoomDoorTrigger>(true);
                var entryCandidates = new List<RoomDoorTrigger>();
                var advanceCandidates = new List<RoomDoorTrigger>();
                for (int i = 0; i < triggers.Length; i++)
                {
                    RoomDoorTrigger trigger = triggers[i];
                    if (trigger == null)
                        continue;

                    if (trigger.TriggerType == RoomDoorTriggerType.Entry)
                    {
                        entryCandidates.Add(trigger);
                    }
                    else
                    {
                        advanceCandidates.Add(trigger);
                    }
                }

                if (_entryTrigger == null && entryCandidates.Count == 1)
                    _entryTrigger = entryCandidates[0];
                if (_advanceTrigger == null && advanceCandidates.Count == 1)
                    _advanceTrigger = advanceCandidates[0];

                if (_entryTrigger == null && entryCandidates.Count > 1)
                {
                    Debug.LogWarning($"RoomRuntime '{name}': Multiple entry trigger candidates found; assign _entryTrigger explicitly.");
                }
                if (_advanceTrigger == null && advanceCandidates.Count > 1)
                {
                    Debug.LogWarning($"RoomRuntime '{name}': Multiple advance trigger candidates found; assign _advanceTrigger explicitly.");
                }
            }

            if (_nextRoomSpawnPoint == null)
            {
                _nextRoomSpawnPoint = FindUniqueChildByNameLike("nextroomspawnpoint", "next_room_spawn", "nextspawn");
            }

            if (_roomEntryAnchor == null)
            {
                _roomEntryAnchor = FindUniqueChildByNameLike("roomentryanchor", "room_entry_anchor", "entryanchor");
            }

            if (_leftSculptureSlot == null || _rightSculptureSlot == null)
            {
                if (_leftSculptureSlot == null)
                    _leftSculptureSlot = FindUniqueChildByNameLike("pedastal_1", "pedestal_1", "leftslot", "left_pedestal");
                if (_rightSculptureSlot == null)
                    _rightSculptureSlot = FindUniqueChildByNameLike("pedastal_2", "pedestal_2", "rightslot", "right_pedestal");
            }
        }

        private Transform FindUniqueChildByNameLike(params string[] tokens)
        {
            if (tokens == null || tokens.Length == 0)
                return null;

            var children = GetComponentsInChildren<Transform>(true);
            Transform singleMatch = null;
            int matchCount = 0;
            for (int i = 0; i < children.Length; i++)
            {
                Transform child = children[i];
                if (child == null)
                    continue;

                string n = child.name.ToLowerInvariant();
                for (int t = 0; t < tokens.Length; t++)
                {
                    if (n.Contains(tokens[t]))
                    {
                        matchCount++;
                        if (singleMatch == null)
                            singleMatch = child;
                        break;
                    }
                }
            }

            if (matchCount == 1)
                return singleMatch;

            if (matchCount > 1)
            {
                Debug.LogWarning($"RoomRuntime '{name}': Ambiguous auto-binding for tokens [{string.Join(", ", tokens)}]; assign explicitly.");
            }

            return null;
        }

        public Transform[] GetSculptureSlots()
        {
            if (_leftSculptureSlot != null && _rightSculptureSlot != null)
                return new[] { _leftSculptureSlot, _rightSculptureSlot };
            if (_leftSculptureSlot != null)
                return new[] { _leftSculptureSlot };
            if (_rightSculptureSlot != null)
                return new[] { _rightSculptureSlot };
            return Array.Empty<Transform>();
        }

        public Transform NextRoomSpawnPoint => _nextRoomSpawnPoint;
        public Transform RoomEntryAnchor => _roomEntryAnchor;
        public Transform AdvanceTriggerTransform =>
            _advanceTrigger != null ? _advanceTrigger.transform :
            _legacyChoiceLeftTrigger != null ? _legacyChoiceLeftTrigger.transform :
            _legacyChoiceRightTrigger != null ? _legacyChoiceRightTrigger.transform :
            null;
        public Transform EntryTriggerTransform => _entryTrigger != null ? _entryTrigger.transform : null;
        public int RoomIndex { get; private set; } = -1;
        public bool HasEntered => _hasEntered;
    }
}

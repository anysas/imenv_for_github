using UnityEngine;

namespace AlgorithmicGallery
{
    public enum RoomDoorTriggerType
    {
        Entry = 0,
        Advance = 1,
        ChoiceRight = 2
    }

    /// <summary>
    /// Trigger volume that notifies the owning room when the player crosses a door.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class RoomDoorTrigger : MonoBehaviour
    {
        [Header("Debug")]
        [SerializeField]
        private bool _debugLogs = true;

        [SerializeField]
        private RoomDoorTriggerType _triggerType = RoomDoorTriggerType.Entry;
        [SerializeField]
        private RoomRuntime _roomRuntime;
        [SerializeField]
        private bool _requirePlayerTag = true;
        [SerializeField]
        private string _playerTag = "Player";
        [SerializeField]
        private float _retriggerCooldownSeconds = 0.4f;

        private float _lastTriggerTime = -999f;
        private bool _isLocked;

        private void Reset()
        {
            var collider = GetComponent<Collider>();
            if (collider != null)
            {
                collider.isTrigger = true;
            }
        }

        private void Awake()
        {
            if (_roomRuntime == null)
            {
                _roomRuntime = GetComponentInParent<RoomRuntime>();
            }

            if (_debugLogs)
            {
                string roomName = _roomRuntime != null ? _roomRuntime.name : "null";
                Debug.Log($"RoomDoorTrigger[{name}] Awake: type={_triggerType}, roomRuntime={roomName}, isTrigger={GetComponent<Collider>()?.isTrigger}");
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_debugLogs)
            {
                string otherName = other != null ? other.name : "null";
                string otherTag = other != null ? other.tag : "null";
                Debug.Log($"RoomDoorTrigger[{name}] OnTriggerEnter: type={_triggerType}, other={otherName}, tag={otherTag}");
            }

            if (_isLocked || _roomRuntime == null || other == null)
            {
                if (_debugLogs)
                {
                    Debug.Log($"RoomDoorTrigger[{name}] ignored: locked={_isLocked}, roomRuntimeNull={_roomRuntime == null}, otherNull={other == null}");
                }
                return;
            }

            if (Time.time - _lastTriggerTime < Mathf.Max(0f, _retriggerCooldownSeconds))
            {
                if (_debugLogs)
                {
                    float elapsed = Time.time - _lastTriggerTime;
                    Debug.Log($"RoomDoorTrigger[{name}] ignored: cooldown active ({elapsed:F3}s < {_retriggerCooldownSeconds:F3}s)");
                }
                return;
            }

            if (_requirePlayerTag && !other.CompareTag(_playerTag))
            {
                if (_debugLogs)
                {
                    Debug.Log($"RoomDoorTrigger[{name}] ignored: requirePlayerTag=true, expected='{_playerTag}', actual='{other.tag}'");
                }
                return;
            }

            _lastTriggerTime = Time.time;
            if (_debugLogs)
            {
                Debug.Log($"RoomDoorTrigger[{name}] notifying RoomRuntime[{_roomRuntime.name}] with triggerType={_triggerType}");
            }
            _roomRuntime.NotifyDoorTriggered(_triggerType);
        }

        public void SetLocked(bool locked)
        {
            _isLocked = locked;
        }

        public RoomDoorTriggerType TriggerType => _triggerType;
    }
}

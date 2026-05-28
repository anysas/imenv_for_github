using UnityEngine;

namespace AlgorithmicGallery
{
    /// <summary>
    /// Bridge trigger for legacy GalleryManager rooms.
    /// Use this to begin the prefab-based managed room loop from an existing scene.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class LegacyRoomTransitionTrigger : MonoBehaviour
    {
        [Header("Mode")]
        [Tooltip("Disable for linear pre-placed demo rooms. When false, this trigger never starts RoomLoopManager.")]
        [SerializeField]
        private bool _enableManagedLoopHandoff = false;

        [SerializeField]
        private RoomLoopManager _roomLoopManager;
        [SerializeField]
        private Transform _spawnPointOverride;
        [SerializeField]
        private bool _requireSpawnPointOverride = true;
        [SerializeField]
        private bool _requirePlayerTag = true;
        [SerializeField]
        private string _playerTag = "Player";
        [SerializeField]
        private bool _oneShot = true;

        private bool _used;

        private void Reset()
        {
            var c = GetComponent<Collider>();
            if (c != null)
            {
                c.isTrigger = true;
            }
        }

        private void Awake()
        {
            if (_roomLoopManager == null)
            {
                _roomLoopManager = FindFirstObjectByType<RoomLoopManager>();
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!_enableManagedLoopHandoff)
                return;

            if (_used || _roomLoopManager == null || other == null)
                return;

            if (_requirePlayerTag && !other.CompareTag(_playerTag))
                return;

            if (_roomLoopManager.IsManagedLoopActive)
            {
                if (_oneShot)
                    _used = true;
                Debug.Log("LegacyRoomTransitionTrigger: Managed room loop already active, ignoring handoff.");
                return;
            }

            if (_requireSpawnPointOverride && _spawnPointOverride == null)
            {
                Debug.LogWarning("LegacyRoomTransitionTrigger: _spawnPointOverride is required but missing. Handoff aborted.");
                return;
            }

            bool started = _roomLoopManager.BeginManagedLoopFrom(_spawnPointOverride);
            if (started && _oneShot)
            {
                _used = true;
            }

            if (started)
            {
                Transform pose = _spawnPointOverride != null ? _spawnPointOverride : _roomLoopManager.transform;
                Debug.Log($"LegacyRoomTransitionTrigger: Managed handoff started at pos={pose.position}, rotY={pose.rotation.eulerAngles.y:F1}");
            }
        }
    }
}

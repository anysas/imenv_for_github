using System.Collections.Generic;
using UnityEngine;

namespace AlgorithmicGallery
{
    /// <summary>
    /// Maintains a rolling pool of generated room instances for endless progression.
    /// </summary>
    public class RoomLoopManager : MonoBehaviour
    {
        private enum PlacementMode
        {
            TriggerAnchorChain = 0,
            DeterministicDebug = 1
        }

        [Tooltip("When false (default), the room loop system is dormant and GalleryManager runs normally with its own pedestal slots.")]
        [SerializeField]
        private bool _enabledAtStart = false;

        [SerializeField]
        private RoomRuntime _roomPrefab;
        [Header("Initial Room Source")]
        [Tooltip("When enabled, the loop starts from a room already placed in the scene instead of instantiating _roomPrefab.")]
        [SerializeField]
        private bool _useSceneRoomAsFirstRoom = true;
        [Tooltip("Optional explicit scene room reference. If null, the manager will search in-scene.")]
        [SerializeField]
        private RoomRuntime _sceneFirstRoom;
        [Tooltip("If no RoomRuntime scene room is found, start managed rooms immediately by instantiating _roomPrefab.")]
        [SerializeField]
        private bool _autoSpawnManagedRoomWhenNoSceneRoom = false;
        [SerializeField]
        private RoomGalleryBridge _galleryBridge;
        [Header("Linear Loop Buffer")]
        [SerializeField]
        private int _roomsAheadBuffer = 2;
        [SerializeField]
        private int _roomsBehindKeep = 1;
        [Header("Safety Limits")]
        [Tooltip("Hard cap for active room instances; should be >= current + ahead buffer + behind keep.")]
        [SerializeField]
        private int _maxActiveRooms = 4;
        [Header("Placement")]
        [SerializeField]
        private PlacementMode _placementMode = PlacementMode.TriggerAnchorChain;
        [Header("Deterministic Debug Placement")]
        [Tooltip("Used only when placement mode is DeterministicDebug.")]
        [SerializeField]
        private Vector3 _deterministicDirection = new Vector3(-1f, 0f, 0f);
        [Tooltip("Used only when placement mode is DeterministicDebug.")]
        [SerializeField]
        private float _linearForwardStep = 28f;
        [Tooltip("Used only when placement mode is DeterministicDebug.")]
        [SerializeField]
        private float _linearVerticalStep = -1f;
        [Tooltip("Used only when placement mode is DeterministicDebug.")]
        [SerializeField]
        private bool _keepConstantHeading = true;
        [SerializeField]
        private bool _logRoomTransitions = true;

        private readonly List<RoomRuntime> _activeRooms = new List<RoomRuntime>();
        private RoomRuntime _currentRoom;
        private int _nextRoomIndex;
        private bool _transitionInFlight;
        private Vector3 _chainForward = Vector3.forward;
        private bool _chainHeadingInitialized;
        private Vector3 _chainStartPosition;
        private bool _chainStartInitialized;
        private Quaternion _chainBaseRotation = Quaternion.identity;
        private bool _chainBaseRotationInitialized;

        private void Awake()
        {
            if (_galleryBridge == null)
            {
                _galleryBridge = FindFirstObjectByType<RoomGalleryBridge>();
            }

            var managers = FindObjectsByType<RoomLoopManager>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            if (managers.Length > 1)
            {
                Debug.LogWarning($"RoomLoopManager: Expected exactly one RoomLoopManager in scene, found {managers.Length}.");
            }

            if (_galleryBridge == null)
            {
                Debug.LogWarning("RoomLoopManager: Missing RoomGalleryBridge reference in scene.");
            }
        }

        private void Start()
        {
            if (!_enabledAtStart)
                return;

            TryStartLoop();
        }

        private void TryStartLoop()
        {
            RoomRuntime firstRoom = TryResolveInitialSceneRoom();
            if (firstRoom != null)
            {
                RegisterRoom(firstRoom, initializeRoomState: true);
                SetCurrentRoom(firstRoom, spawnSculptures: true);
                InitializeChainHeading(firstRoom);
                EnsureAheadBuffer();
                RecycleRoomsWindow();
                return;
            }

            if (_roomPrefab == null)
                return;

            if (!_autoSpawnManagedRoomWhenNoSceneRoom)
                return;

            firstRoom = SpawnRoom(transform.position, transform.rotation);
            SetCurrentRoom(firstRoom, spawnSculptures: true);
            InitializeChainHeading(firstRoom);
            EnsureAheadBuffer();
            RecycleRoomsWindow();
        }

        private RoomRuntime SpawnRoom(Vector3 position, Quaternion rotation)
        {
            if (_roomPrefab == null)
            {
                Debug.LogWarning("RoomLoopManager: Cannot spawn room because _roomPrefab is not assigned.");
                return null;
            }

            RoomRuntime room = Instantiate(_roomPrefab, position, rotation);
            RegisterRoom(room, initializeRoomState: true);
            if (_logRoomTransitions)
            {
                Debug.Log($"RoomLoopManager: Spawned room index={room.RoomIndex}, activeRooms={_activeRooms.Count}");
            }
            return room;
        }

        private void SetCurrentRoom(RoomRuntime room, bool spawnSculptures)
        {
            RoomRuntime previousRoom = _currentRoom;
            _currentRoom = room;
            if (_galleryBridge != null && room != null)
            {
                _galleryBridge.BindRoom(room, clearSlotState: true, spawnImmediately: spawnSculptures);
            }
            else if (_logRoomTransitions && room != null)
            {
                Debug.LogWarning("RoomLoopManager: Missing RoomGalleryBridge reference, room pedestals were not bound to GalleryManager.");
            }

            if (_logRoomTransitions && room != null)
            {
                string previousLabel = previousRoom != null ? previousRoom.RoomIndex.ToString() : "none";
                string currentLabel = room != null ? room.RoomIndex.ToString() : "none";
                Debug.Log(
                    $"RoomLoopManager: Current room changed {previousLabel} -> {currentLabel}, " +
                    $"activeRooms={_activeRooms.Count}, pedestalSlots={room.GetSculptureSlots().Length}"
                );
            }
        }

        private RoomRuntime TryResolveInitialSceneRoom()
        {
            if (!_useSceneRoomAsFirstRoom)
                return null;

            if (_sceneFirstRoom != null)
                return _sceneFirstRoom;

            var candidates = FindObjectsByType<RoomRuntime>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < candidates.Length; i++)
            {
                RoomRuntime candidate = candidates[i];
                if (candidate == null)
                    continue;

                // Skip prefab assets accidentally serialized into the field.
                if (!candidate.gameObject.scene.IsValid())
                    continue;

                return candidate;
            }

            return null;
        }

        private void RegisterRoom(RoomRuntime room, bool initializeRoomState)
        {
            if (room == null)
                return;

            if (_activeRooms.Contains(room))
                return;

            if (initializeRoomState)
            {
                room.Initialize(_nextRoomIndex++);
            }

            room.EnteredRoom += OnRoomEntered;
            room.AdvanceTriggered += OnRoomAdvanceTriggered;
            _activeRooms.Add(room);
        }

        private void OnRoomEntered(RoomRuntime room)
        {
            if (room == null || _transitionInFlight)
                return;

            if (room != _currentRoom)
            {
                ExecuteTransition(() =>
                {
                    // Keep room/pedestal binding synchronized with where the player actually is.
                    SetCurrentRoom(room, spawnSculptures: true);
                    EnsureAheadBuffer();
                    RecycleRoomsWindow();
                });
            }
        }

        private void OnRoomAdvanceTriggered(RoomRuntime room)
        {
            if (room == null || room != _currentRoom || _transitionInFlight)
                return;

            ExecuteTransition(() =>
            {
                RoomRuntime nextRoom = TryGetNextBufferedRoom(room);
                if (nextRoom == null)
                {
                    nextRoom = SpawnNextRoomFrom(room);
                }

                if (nextRoom == null)
                    return;

                SetCurrentRoom(nextRoom, spawnSculptures: true);
                EnsureAheadBuffer();
                RecycleRoomsWindow();
            });
        }

        private void ExecuteTransition(System.Action transitionAction)
        {
            if (_transitionInFlight || transitionAction == null)
                return;

            _transitionInFlight = true;
            try
            {
                transitionAction();
            }
            finally
            {
                _transitionInFlight = false;
            }
        }

        private RoomRuntime TryGetNextBufferedRoom(RoomRuntime room)
        {
            int currentIndex = _activeRooms.IndexOf(room);
            if (currentIndex < 0)
                return null;

            int nextIndex = currentIndex + 1;
            if (nextIndex >= _activeRooms.Count)
                return null;

            return _activeRooms[nextIndex];
        }

        private void EnsureAheadBuffer()
        {
            if (_currentRoom == null)
                return;

            int targetAheadCount = Mathf.Max(0, _roomsAheadBuffer);
            int guard = 0;
            while (GetAheadCount(_currentRoom) < targetAheadCount && guard < 16)
            {
                guard++;

                RoomRuntime tail = GetTailRoom();
                if (tail == null)
                    break;

                RoomRuntime spawned = SpawnNextRoomFrom(tail);
                if (spawned == null)
                {
                    if (_logRoomTransitions)
                    {
                        Debug.LogWarning("RoomLoopManager: Failed to extend ahead buffer. Check _roomPrefab and NextRoomSpawnPoint setup.");
                    }
                    break;
                }
            }
        }

        private int GetAheadCount(RoomRuntime room)
        {
            int idx = _activeRooms.IndexOf(room);
            if (idx < 0)
                return 0;

            return Mathf.Max(0, _activeRooms.Count - idx - 1);
        }

        private RoomRuntime GetTailRoom()
        {
            if (_activeRooms.Count == 0)
                return null;

            return _activeRooms[_activeRooms.Count - 1];
        }

        private RoomRuntime SpawnNextRoomFrom(RoomRuntime sourceRoom)
        {
            if (sourceRoom == null)
                return null;

            if (!TryResolveNextRoomPose(sourceRoom, out Vector3 spawnPosition, out Quaternion spawnRotation, out string poseSource))
            {
                return null;
            }

            if (_logRoomTransitions)
            {
                float planarStep = Vector2.Distance(
                    new Vector2(sourceRoom.transform.position.x, sourceRoom.transform.position.z),
                    new Vector2(spawnPosition.x, spawnPosition.z)
                );
                float verticalStep = spawnPosition.y - sourceRoom.transform.position.y;
                Debug.Log(
                    $"RoomLoopManager: Next spawn from room {sourceRoom.RoomIndex} -> " +
                    $"targetPos={spawnPosition}, planarStep={planarStep:F2}, verticalStep={verticalStep:F2}, poseSource={poseSource}"
                );
            }

            RoomRuntime spawnedRoom = SpawnRoom(spawnPosition, spawnRotation);
            if (spawnedRoom == null)
                return null;

            if (_placementMode == PlacementMode.TriggerAnchorChain)
            {
                Transform alignTarget = spawnedRoom.EntryTriggerTransform;
                if (alignTarget == null)
                    alignTarget = spawnedRoom.RoomEntryAnchor;

                if (alignTarget != null)
                {
                    Vector3 offset = spawnPosition - alignTarget.position;
                    spawnedRoom.transform.position += offset;

                    if (_logRoomTransitions)
                    {
                        Debug.Log(
                            $"RoomLoopManager: Aligned room {spawnedRoom.RoomIndex} entry to {spawnPosition} " +
                            $"(shifted by {offset}, alignedVia={alignTarget.name})"
                        );
                    }
                }
                else if (_logRoomTransitions)
                {
                    Debug.LogWarning(
                        $"RoomLoopManager: Room {spawnedRoom.RoomIndex} has no entry trigger or entry anchor. " +
                        $"Root placed directly at advance trigger position."
                    );
                }
            }

            return spawnedRoom;
        }

        private bool TryResolveNextRoomPose(RoomRuntime sourceRoom, out Vector3 spawnPosition, out Quaternion spawnRotation, out string poseSource)
        {
            spawnPosition = default;
            spawnRotation = Quaternion.identity;
            poseSource = "Unknown";

            if (sourceRoom == null)
                return false;

            if (_placementMode == PlacementMode.TriggerAnchorChain)
            {
                Transform advanceTrigger = sourceRoom.AdvanceTriggerTransform;
                if (advanceTrigger == null)
                {
                    if (_logRoomTransitions)
                    {
                        Debug.LogWarning($"RoomLoopManager: TriggerAnchorChain requires an advance trigger on room index={sourceRoom.RoomIndex}; spawn aborted.");
                    }
                    return false;
                }

                spawnPosition = advanceTrigger.position;
                spawnRotation = GetLockedBaseRotation(sourceRoom);
                poseSource = "TriggerAnchorChain";
                return true;
            }

            InitializeChainHeading(sourceRoom);
            InitializeChainStart(sourceRoom);
            Vector3 horizontalForward = _chainForward;
            int nextRoomIndex = Mathf.Max(0, sourceRoom.RoomIndex + 1);
            spawnPosition = _chainStartPosition
                            + (horizontalForward * Mathf.Max(0.01f, _linearForwardStep) * nextRoomIndex)
                            + (Vector3.up * _linearVerticalStep * nextRoomIndex);

            spawnRotation = _keepConstantHeading
                ? Quaternion.LookRotation(horizontalForward, Vector3.up)
                : sourceRoom.transform.rotation;
            poseSource = "DeterministicDebug";
            return true;
        }

        private Quaternion GetLockedBaseRotation(RoomRuntime room)
        {
            if (!_chainBaseRotationInitialized)
            {
                _chainBaseRotation = room != null ? room.transform.rotation : Quaternion.identity;
                _chainBaseRotationInitialized = true;
            }
            return _chainBaseRotation;
        }

        private void InitializeChainHeading(RoomRuntime room)
        {
            if (room == null)
                return;

            if (_chainHeadingInitialized)
                return;

            Vector3 forward = _deterministicDirection;

            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
                forward = new Vector3(-1f, 0f, 0f);

            _chainForward = forward.normalized;
            _chainHeadingInitialized = true;

            if (_logRoomTransitions)
            {
                Debug.Log($"RoomLoopManager: Chain heading initialized to {_chainForward}");
            }
        }

        private void InitializeChainStart(RoomRuntime room)
        {
            if (room == null || _chainStartInitialized)
                return;

            _chainStartPosition = room.transform.position;
            _chainStartInitialized = true;

            if (_logRoomTransitions)
            {
                Debug.Log($"RoomLoopManager: Chain start initialized at {_chainStartPosition}");
            }
        }


        private void RecycleRoomsWindow()
        {
            if (_currentRoom == null || _activeRooms.Count == 0)
                return;

            int currentIndex = _activeRooms.IndexOf(_currentRoom);
            if (currentIndex < 0)
                return;

            int keepStart = Mathf.Max(0, currentIndex - Mathf.Max(0, _roomsBehindKeep));
            int keepEnd = Mathf.Min(_activeRooms.Count - 1, currentIndex + Mathf.Max(0, _roomsAheadBuffer));

            var toRecycle = new List<RoomRuntime>();
            for (int i = 0; i < _activeRooms.Count; i++)
            {
                if (i >= keepStart && i <= keepEnd)
                    continue;

                RoomRuntime room = _activeRooms[i];
                if (room != null && room != _currentRoom)
                {
                    toRecycle.Add(room);
                }
            }

            int hardCap = Mathf.Max(0, _maxActiveRooms);
            if (hardCap > 0)
            {
                int projectedCount = _activeRooms.Count - toRecycle.Count;
                int overflow = projectedCount - hardCap;
                for (int i = 0; i < overflow; i++)
                {
                    RoomRuntime candidate = GetOldestRecyclableRoom();
                    if (candidate == null)
                        break;

                    if (!toRecycle.Contains(candidate))
                    {
                        toRecycle.Add(candidate);
                    }
                }
            }

            for (int i = 0; i < toRecycle.Count; i++)
            {
                RoomRuntime room = toRecycle[i];
                UnregisterAndRecycleRoom(room);
            }
        }

        private RoomRuntime GetOldestRecyclableRoom()
        {
            for (int i = 0; i < _activeRooms.Count; i++)
            {
                RoomRuntime room = _activeRooms[i];
                if (room != null && room != _currentRoom)
                    return room;
            }

            return null;
        }

        private void UnregisterAndRecycleRoom(RoomRuntime room)
        {
            if (room == null)
                return;

            if (_activeRooms.Contains(room))
                _activeRooms.Remove(room);

            room.EnteredRoom -= OnRoomEntered;
            room.AdvanceTriggered -= OnRoomAdvanceTriggered;
            Destroy(room.gameObject);

            if (_logRoomTransitions)
            {
                Debug.Log($"RoomLoopManager: Recycled room index={room.RoomIndex}, activeRooms={_activeRooms.Count}");
            }
        }

        public bool BeginManagedLoop()
        {
            return BeginManagedLoopFrom(null);
        }

        public bool BeginManagedLoopFrom(Transform spawnPoint)
        {
            if (_currentRoom != null)
            {
                if (_logRoomTransitions)
                {
                    Debug.Log("RoomLoopManager: Managed room loop is already active.");
                }
                return false;
            }

            if (_roomPrefab == null)
            {
                Debug.LogWarning("RoomLoopManager: Cannot begin managed loop because room prefab is not assigned.");
                return false;
            }

            Vector3 position = spawnPoint != null ? spawnPoint.position : transform.position;
            Quaternion rotation = spawnPoint != null ? spawnPoint.rotation : transform.rotation;

            _chainHeadingInitialized = false;
            _chainStartInitialized = false;
            _chainBaseRotationInitialized = false;

            RoomRuntime firstRoom = SpawnRoom(position, rotation);
            SetCurrentRoom(firstRoom, spawnSculptures: true);
            InitializeChainHeading(firstRoom);
            EnsureAheadBuffer();
            RecycleRoomsWindow();

            if (_logRoomTransitions)
            {
                Debug.Log(
                    $"RoomLoopManager: Managed loop started at pos={position}, rotY={rotation.eulerAngles.y:F1}, " +
                    $"placementMode={_placementMode}"
                );
            }

            return true;
        }

        public bool IsManagedLoopActive => _currentRoom != null;
    }
}

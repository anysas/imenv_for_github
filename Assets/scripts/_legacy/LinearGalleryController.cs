using System;
using System.Collections.Generic;
using UnityEngine;

namespace AlgorithmicGallery
{
    /// <summary>
    /// Linear pre-placed gallery: one child transform per room in order.
    /// Room 0 (legacy first room): pedestals are <b>only</b> transforms named firstpedestal1 and firstpedestal2.
    /// Rooms with <see cref="RoomRuntime"/>: uses <see cref="RoomRuntime.GetSculptureSlots"/> and advances via
    /// <see cref="RoomDoorTrigger"/> / <see cref="RoomRuntime.AdvanceTriggered"/> (not LinearRoomTrigger).
    /// </summary>
    public class LinearGalleryController : MonoBehaviour
    {
        private class RoomBinding
        {
            public Transform roomRoot;
            public RoomRuntime roomRuntime;
            public Transform[] pedestalSlots;
        }

        [Header("References")]
        [SerializeField]
        private GalleryManager _galleryManager;
        [SerializeField]
        private Transform _playerTransform;

        [Header("Room Discovery")]
        [SerializeField]
        private bool _autoDiscoverRoomsFromChildren = true;

        [Tooltip("Room index 0 only: exact pedestal names (case-insensitive, spaces ignored).")]
        [SerializeField]
        private string _firstRoomPedestalName1 = "firstpedestal1";

        [Tooltip("Room index 0 only: exact pedestal names (case-insensitive, spaces ignored).")]
        [SerializeField]
        private string _firstRoomPedestalName2 = "firstpedestal2";

        [SerializeField]
        private string[] _pedestalNameHints = new[]
        {
            "Pedastal",
            "Pedestal",
            "SculptureSlot"
        };

        private readonly List<RoomBinding> _rooms = new List<RoomBinding>();
        private readonly List<RoomRuntime> _runtimeSubscriptions = new List<RoomRuntime>();
        private int _activeRoomIndex = -1;
        private int _legacyFirstRoomIndex = -1;

        private void Start()
        {
            if (_galleryManager == null)
                _galleryManager = FindFirstObjectByType<GalleryManager>();
            if (_playerTransform == null && Camera.main != null)
                _playerTransform = Camera.main.transform;

            // Hard-disable managed room generation path for linear demo flow.
            DisableManagedLoopHandoff();

            if (_autoDiscoverRoomsFromChildren)
                DiscoverRooms();

            if (_rooms.Count > 0)
                ActivateRoom(_legacyFirstRoomIndex >= 0 ? _legacyFirstRoomIndex : 0);
        }

        private void OnDestroy()
        {
            for (int i = 0; i < _runtimeSubscriptions.Count; i++)
            {
                RoomRuntime rr = _runtimeSubscriptions[i];
                if (rr != null)
                {
                    rr.EnteredRoom -= OnRoomEntered;
                    rr.AdvanceTriggered -= OnRoomAdvanceTriggered;
                }
            }

            _runtimeSubscriptions.Clear();
        }

        private void DisableManagedLoopHandoff()
        {
            RoomLoopManager loopManager = FindFirstObjectByType<RoomLoopManager>();
            if (loopManager != null && loopManager.enabled)
            {
                loopManager.enabled = false;
                Debug.Log("LinearGalleryController: Disabled RoomLoopManager for linear demo flow.");
            }

            LegacyRoomTransitionTrigger[] legacyTriggers = FindObjectsByType<LegacyRoomTransitionTrigger>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < legacyTriggers.Length; i++)
            {
                if (legacyTriggers[i] != null && legacyTriggers[i].enabled)
                    legacyTriggers[i].enabled = false;
            }
        }

        public void ActivateRoom(int roomIndex)
        {
            if (_galleryManager == null || roomIndex < 0 || roomIndex >= _rooms.Count)
                return;
            if (_activeRoomIndex == roomIndex)
                return;

            _activeRoomIndex = roomIndex;
            RoomBinding room = _rooms[roomIndex];
            if (room.pedestalSlots == null || room.pedestalSlots.Length == 0)
            {
                Debug.LogWarning($"LinearGalleryController: Room {roomIndex} has no pedestal slots.");
                return;
            }

            _galleryManager.SetPedestalSlots(room.pedestalSlots, clearSlotState: true, spawnImmediately: true);
            Debug.Log($"LinearGalleryController: Activated room index={roomIndex}, slots={room.pedestalSlots.Length}.");
        }

        /// <summary>Legacy path if you still use <see cref="LinearRoomTrigger"/> on a test object.</summary>
        public void OnRoomTriggerEntered(int roomIndex, Collider other)
        {
            if (!IsPlayerCollider(other))
                return;

            ActivateRoom(roomIndex);
        }

        private void OnRoomEntered(RoomRuntime room)
        {
            if (room == null)
                return;

            // Entry trigger of a room should always activate that room in linear demo mode.
            ActivateRoom(room.RoomIndex);
        }

        private void OnRoomAdvanceTriggered(RoomRuntime room)
        {
            if (room == null)
                return;

            int next = room.RoomIndex + 1;
            if (next < _rooms.Count)
                ActivateRoom(next);
        }

        private void DiscoverRooms()
        {
            _rooms.Clear();
            _legacyFirstRoomIndex = -1;

            for (int i = 0; i < transform.childCount; i++)
            {
                Transform room = transform.GetChild(i);
                if (room == null)
                    continue;

                var runtime = room.GetComponent<RoomRuntime>() ?? room.GetComponentInChildren<RoomRuntime>(true);
                if (runtime != null)
                {
                    runtime.Initialize(i);
                    runtime.EnteredRoom += OnRoomEntered;
                    runtime.AdvanceTriggered += OnRoomAdvanceTriggered;
                    _runtimeSubscriptions.Add(runtime);
                }
                else
                {
                    Debug.LogWarning(
                        $"LinearGalleryController: Child '{room.name}' has no RoomRuntime. Add RoomRuntime to use RoomDoorTrigger-based flow.");
                }

                Transform[] slots = ResolvePedestalSlots(room, i, runtime);

                _rooms.Add(new RoomBinding
                {
                    roomRoot = room,
                    roomRuntime = runtime,
                    pedestalSlots = slots
                });

                if (_legacyFirstRoomIndex < 0 && ContainsStrictFirstRoomPedestals(room))
                    _legacyFirstRoomIndex = i;
            }

            if (_legacyFirstRoomIndex >= 0)
            {
                Debug.Log($"LinearGalleryController: Legacy first room detected at child index {_legacyFirstRoomIndex} ('{_rooms[_legacyFirstRoomIndex].roomRoot.name}').");
            }
            else
            {
                Debug.Log("LinearGalleryController: Could not auto-detect legacy first room by firstpedestal names; defaulting to child index 0.");
            }
        }

        private Transform[] ResolvePedestalSlots(Transform roomRoot, int roomIndex, RoomRuntime runtime)
        {
            if (ContainsStrictFirstRoomPedestals(roomRoot))
                return FindFirstRoomPedestalsStrict(roomRoot);

            if (runtime != null)
            {
                Transform[] fromRuntime = runtime.GetSculptureSlots();
                if (fromRuntime != null && fromRuntime.Length > 0)
                    return fromRuntime;
            }

            return FindPedestalSlotsByHints(roomRoot);
        }

        private bool ContainsStrictFirstRoomPedestals(Transform roomRoot)
        {
            if (roomRoot == null)
                return false;

            string key1 = NormalizePedestalName(_firstRoomPedestalName1);
            string key2 = NormalizePedestalName(_firstRoomPedestalName2);
            bool found1 = false;
            bool found2 = false;

            foreach (Transform t in roomRoot.GetComponentsInChildren<Transform>(true))
            {
                if (t == null)
                    continue;

                string key = NormalizePedestalName(t.name);
                if (key == key1)
                    found1 = true;
                else if (key == key2)
                    found2 = true;
            }

            return found1 && found2;
        }

        /// <summary>Room 0 legacy room: only firstpedestal1 + firstpedestal2 (normalized names).</summary>
        private Transform[] FindFirstRoomPedestalsStrict(Transform roomRoot)
        {
            string key1 = NormalizePedestalName(_firstRoomPedestalName1);
            string key2 = NormalizePedestalName(_firstRoomPedestalName2);

            Transform t1 = null;
            Transform t2 = null;

            foreach (Transform t in roomRoot.GetComponentsInChildren<Transform>(true))
            {
                if (t == null)
                    continue;

                string key = NormalizePedestalName(t.name);
                if (key == key1)
                    t1 = t;
                else if (key == key2)
                    t2 = t;
            }

            var list = new List<Transform>(2);
            if (t1 != null)
                list.Add(t1);
            if (t2 != null)
                list.Add(t2);

            if (list.Count < 2)
            {
                Debug.LogWarning(
                    $"LinearGalleryController: Room 0 expected '{_firstRoomPedestalName1}' and '{_firstRoomPedestalName2}'. " +
                    $"Found {(t1 != null ? 1 : 0)} + {(t2 != null ? 1 : 0)}. Check spelling under '{roomRoot.name}'.");

                // Fallback so the linear demo still runs even when strict names are misconfigured.
                Transform[] fallback = FindPedestalSlotsByHints(roomRoot);
                if (fallback.Length > 0)
                {
                    Debug.LogWarning($"LinearGalleryController: Room 0 using fallback pedestal hints ({fallback.Length} slots).");
                    return fallback;
                }
            }

            return list.ToArray();
        }

        private static string NormalizePedestalName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return string.Empty;

            return name.Replace(" ", "").Trim().ToLowerInvariant();
        }

        private Transform[] FindPedestalSlotsByHints(Transform room)
        {
            var slots = new List<Transform>();
            Transform[] allChildren = room.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < allChildren.Length; i++)
            {
                Transform child = allChildren[i];
                string name = child.name;
                for (int h = 0; h < _pedestalNameHints.Length; h++)
                {
                    if (name.IndexOf(_pedestalNameHints[h], StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        slots.Add(child);
                        break;
                    }
                }
            }

            slots.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
            return slots.ToArray();
        }

        private bool IsPlayerCollider(Collider other)
        {
            if (other == null)
                return false;
            if (other.CompareTag("Player"))
                return true;
            if (_playerTransform == null)
                return other.GetComponentInParent<CharacterController>() != null;

            Transform root = other.transform.root;
            return root == _playerTransform.root || other.GetComponentInParent<CharacterController>() != null;
        }
    }
}

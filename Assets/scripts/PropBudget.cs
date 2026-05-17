using System.Collections.Generic;
using UnityEngine;

namespace AlgorithmicGallery.Corruption
{
    // Caps the total number of placed props in the sandbox to protect framerate.
    // When over budget, destroys the oldest non-floor child of the sandbox root.
    //
    // PropPlacer registers each spawn here. Player placements have priority — assistant
    // placements are culled first when over budget.
    public class PropBudget : MonoBehaviour
    {
        [SerializeField] private int _maxPlacedProps = int.MaxValue;
        [Tooltip("If false, props are never auto-deleted when the budget is exceeded.")]
        [SerializeField] private bool _autoEvictWhenOverBudget = false;
        [SerializeField] private SandboxManager _sandbox;

        private readonly List<Tracked> _tracked = new();
        private bool _warnedOverBudget;

        private struct Tracked
        {
            public GameObject Go;
            public bool IsPlayer;
            public float Time;
        }

        public static PropBudget Instance { get; private set; }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void Start()
        {
            if (_sandbox == null) _sandbox = FindFirstObjectByType<SandboxManager>();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void Register(GameObject go, bool isPlayerPlaced)
        {
            if (go == null) return;
            _tracked.Add(new Tracked { Go = go, IsPlayer = isPlayerPlaced, Time = Time.time });
            EnforceBudget();
        }

        // Called when a prop is manually removed by the player so the budget count stays accurate.
        public void Unregister(GameObject go)
        {
            for (int i = _tracked.Count - 1; i >= 0; i--)
            {
                if (_tracked[i].Go == go)
                {
                    _tracked.RemoveAt(i);
                    return;
                }
            }
        }

        /// <summary>
        /// Removes the player-placed prop nearest to <paramref name="preferredPosition"/> and returns
        /// its placement transform so the assistant can overwrite it with a replacement prop.
        /// </summary>
        public bool TryRemoveNearestPlayerPlaced(Vector3 preferredPosition, out Vector3 placementPosition, out float yRotation)
        {
            placementPosition = default;
            yRotation = 0f;

            CleanupDeadRefs();

            int victim = FindNearestPlayerPlaced(preferredPosition);
            if (victim < 0)
                return false;

            GameObject go = _tracked[victim].Go;
            if (go == null)
            {
                _tracked.RemoveAt(victim);
                return false;
            }

            Transform t = go.transform;
            placementPosition = t.position;
            yRotation = t.eulerAngles.y;

            _tracked.RemoveAt(victim);

            // Detach + deactivate immediately so subsequent overlap checks no longer see this prop.
            t.SetParent(null, worldPositionStays: true);
            go.SetActive(false);
            Destroy(go);
            return true;
        }

        public bool TryRemoveRandomPlayerPlaced(out Vector3 placementPosition, out float yRotation)
        {
            placementPosition = default;
            yRotation = 0f;

            CleanupDeadRefs();

            int newestPlayerIdx = FindMostRecentPlayerPlaced();
            var playerIndices = new List<int>();
            for (int i = 0; i < _tracked.Count; i++)
            {
                if (_tracked[i].IsPlayer && _tracked[i].Go != null && i != newestPlayerIdx)
                    playerIndices.Add(i);
            }

            if (playerIndices.Count == 0)
                return false;

            int victim = playerIndices[UnityEngine.Random.Range(0, playerIndices.Count)];
            GameObject go = _tracked[victim].Go;
            if (go == null)
            {
                _tracked.RemoveAt(victim);
                return false;
            }

            Transform t = go.transform;
            placementPosition = t.position;
            yRotation = t.eulerAngles.y;

            _tracked.RemoveAt(victim);
            t.SetParent(null, worldPositionStays: true);
            go.SetActive(false);
            Destroy(go);
            return true;
        }

        public void GetTrackedCounts(out int playerCount, out int assistantCount)
        {
            CleanupDeadRefs();
            playerCount = 0;
            assistantCount = 0;

            for (int i = 0; i < _tracked.Count; i++)
            {
                if (_tracked[i].IsPlayer)
                    playerCount++;
                else
                    assistantCount++;
            }
        }

        public void GetTrackedPlacedProps(List<GameObject> buffer)
        {
            if (buffer == null)
                return;

            CleanupDeadRefs();
            for (int i = 0; i < _tracked.Count; i++)
            {
                if (_tracked[i].Go != null)
                    buffer.Add(_tracked[i].Go);
            }
        }

        public void GetTrackedPlayerPlacedProps(List<GameObject> buffer)
        {
            if (buffer == null)
                return;

            CleanupDeadRefs();
            for (int i = 0; i < _tracked.Count; i++)
            {
                if (_tracked[i].IsPlayer && _tracked[i].Go != null)
                    buffer.Add(_tracked[i].Go);
            }
        }

        /// <summary>
        /// Used by placement raycasts to ignore stacked props — only the pedestal top should register.
        /// </summary>
        public bool IsTrackedPlacedPropCollider(Collider col)
        {
            if (col == null) return false;
            Transform t = col.transform;
            for (int i = _tracked.Count - 1; i >= 0; i--)
            {
                GameObject go = _tracked[i].Go;
                if (go == null) continue;
                if (t == go.transform || t.IsChildOf(go.transform))
                    return true;
            }
            return false;
        }

        private void EnforceBudget()
        {
            CleanupDeadRefs();

            // Unlimited cap mode.
            if (_maxPlacedProps >= int.MaxValue)
                return;

            if (!_autoEvictWhenOverBudget)
            {
                if (!_warnedOverBudget && _tracked.Count > _maxPlacedProps)
                {
                    _warnedOverBudget = true;
                    Debug.LogWarning($"[PropBudget] Placement count ({_tracked.Count}) exceeded budget ({_maxPlacedProps}), auto-eviction is disabled.");
                }
                return;
            }

            while (_tracked.Count > _maxPlacedProps)
            {
                int victim = FindOldestAssistantPlaced();
                if (victim < 0) victim = 0; // fallback: drop the oldest regardless
                if (_tracked[victim].Go != null)
                    Destroy(_tracked[victim].Go);
                _tracked.RemoveAt(victim);
            }
        }

        private int FindOldestAssistantPlaced()
        {
            int oldestIdx = -1;
            float oldestTime = float.MaxValue;
            for (int i = 0; i < _tracked.Count; i++)
            {
                if (_tracked[i].IsPlayer) continue;
                if (_tracked[i].Time < oldestTime)
                {
                    oldestTime = _tracked[i].Time;
                    oldestIdx = i;
                }
            }
            return oldestIdx;
        }

        private int FindNearestPlayerPlaced(Vector3 preferredPosition)
        {
            int bestIdx = -1;
            float bestDistSq = float.MaxValue;
            float bestTime = float.MaxValue;

            for (int i = 0; i < _tracked.Count; i++)
            {
                if (!_tracked[i].IsPlayer || _tracked[i].Go == null)
                    continue;

                Vector3 delta = _tracked[i].Go.transform.position - preferredPosition;
                delta.y = 0f;
                float distSq = delta.sqrMagnitude;
                if (distSq < bestDistSq - 0.0001f)
                {
                    bestDistSq = distSq;
                    bestTime = _tracked[i].Time;
                    bestIdx = i;
                    continue;
                }

                if (Mathf.Abs(distSq - bestDistSq) < 0.0001f && _tracked[i].Time < bestTime)
                {
                    bestTime = _tracked[i].Time;
                    bestIdx = i;
                }
            }

            return bestIdx;
        }

        private int FindMostRecentPlayerPlaced()
        {
            int newestIdx = -1;
            float newestTime = float.NegativeInfinity;

            for (int i = 0; i < _tracked.Count; i++)
            {
                if (!_tracked[i].IsPlayer || _tracked[i].Go == null)
                    continue;

                if (_tracked[i].Time > newestTime)
                {
                    newestTime = _tracked[i].Time;
                    newestIdx = i;
                }
            }

            return newestIdx;
        }

        private void CleanupDeadRefs()
        {
            for (int i = _tracked.Count - 1; i >= 0; i--)
                if (_tracked[i].Go == null) _tracked.RemoveAt(i);
        }
    }
}

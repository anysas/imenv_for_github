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
            // Drop dead refs
            for (int i = _tracked.Count - 1; i >= 0; i--)
                if (_tracked[i].Go == null) _tracked.RemoveAt(i);

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
    }
}

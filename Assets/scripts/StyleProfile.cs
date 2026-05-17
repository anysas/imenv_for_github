using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AlgorithmicGallery.Corruption
{
    public class PlacementRecord
    {
        public Vector3 Position;
        /// <summary>World-space rotation of the placed prop root after spawn.</summary>
        public Quaternion Rotation;
        /// <summary>Local scale of the placed prop root (under sandbox parent).</summary>
        public Vector3 LocalScale;
        public string Group;
        public string GlbPath;
        public List<string> Tags;
        public List<string> EmotionalTags;
        public float Timestamp;
        public bool IsPlayer;
    }

    // Tracks what and how the player builds. Read by AssistantSystem to mirror/corrupt their style.
    public class StyleProfile
    {
        private const int GridResolution = 10; // 10x10 density grid over sandbox bounds
        private const float GridWorldSize = 20f; // assumed sandbox floor is ~20x20m

        private readonly List<PlacementRecord> _history = new();
        private readonly Dictionary<string, int> _groupCounts = new();
        private readonly Dictionary<string, int> _tagCounts = new();
        private readonly Dictionary<string, int> _emotionalTagCounts = new();

        // Coarse 2D density grid [x,z]
        private readonly int[,] _densityGrid = new int[GridResolution, GridResolution];

        public int PlacementCount => _history.Count;
        public int PlayerPlacementCount { get; private set; }
        public int AssistantPlacementCount => _history.Count - PlayerPlacementCount;
        public IReadOnlyList<PlacementRecord> History => _history;

        public void RecordPlacement(Vector3 worldPos, PropEntry prop, float sessionTime, bool isPlayer = true)
        {
            RecordPlacement(worldPos, prop, sessionTime, isPlayer, Quaternion.identity, Vector3.one);
        }

        /// <summary>Records a placement including the spawned instance transform for exact replay export.</summary>
        public void RecordPlacement(
            Vector3 worldPos,
            PropEntry prop,
            float sessionTime,
            bool isPlayer,
            Quaternion rotation,
            Vector3 localScale)
        {
            var emotionalTags = prop.EmotionalTags != null ? new List<string>(prop.EmotionalTags) : new List<string>();
            var record = new PlacementRecord
            {
                Position = worldPos,
                Rotation = rotation,
                LocalScale = localScale,
                Group = prop.Group,
                GlbPath = prop.GlbPath,
                Tags = new List<string>(prop.Tags),
                EmotionalTags = emotionalTags,
                Timestamp = sessionTime,
                IsPlayer = isPlayer,
            };
            _history.Add(record);
            if (isPlayer) PlayerPlacementCount++;

            _groupCounts[prop.Group] = _groupCounts.GetValueOrDefault(prop.Group, 0) + 1;
            foreach (var tag in prop.Tags)
                _tagCounts[tag] = _tagCounts.GetValueOrDefault(tag, 0) + 1;
            foreach (var tag in emotionalTags)
                _emotionalTagCounts[tag] = _emotionalTagCounts.GetValueOrDefault(tag, 0) + 1;

            IncrementDensityCell(worldPos);
        }

        // Top N groups by placement count.
        public List<string> DominantGroups(int n = 3)
        {
            return _groupCounts
                .OrderByDescending(kv => kv.Value)
                .Take(n)
                .Select(kv => kv.Key)
                .ToList();
        }

        // Top N generic tags by frequency.
        public List<string> DominantTags(int n = 5)
        {
            return _tagCounts
                .OrderByDescending(kv => kv.Value)
                .Take(n)
                .Select(kv => kv.Key)
                .ToList();
        }

        // Top N emotional tags by frequency (used for index.json and session summary).
        public List<string> DominantEmotionalTags(int n = 3)
        {
            return _emotionalTagCounts
                .OrderByDescending(kv => kv.Value)
                .Take(n)
                .Select(kv => kv.Key)
                .ToList();
        }

        public IReadOnlyDictionary<string, int> GroupCounts => _groupCounts;
        public IReadOnlyDictionary<string, int> TagCounts => _tagCounts;
        public IReadOnlyDictionary<string, int> EmotionalTagCounts => _emotionalTagCounts;

        // Average seconds between placements. Returns 0 if fewer than 2 placements.
        public float AverageCadenceSeconds()
        {
            if (_history.Count < 2) return 0f;
            float total = 0f;
            for (int i = 1; i < _history.Count; i++)
                total += _history[i].Timestamp - _history[i - 1].Timestamp;
            return total / (_history.Count - 1);
        }

        // Returns a world-space position that is relatively sparse (low density cell center),
        // biased toward the player's own cluster when mirrorPlayer=true.
        public Vector3 SuggestPlacementPosition(Vector3 sandboxOrigin, bool mirrorPlayer)
        {
            if (mirrorPlayer && _history.Count > 0)
            {
                // Pick a position near the player's recent placements with some scatter
                var recent = _history[_history.Count - 1].Position;
                Vector2 scatter = UnityEngine.Random.insideUnitCircle * 2.5f;
                return new Vector3(recent.x + scatter.x, sandboxOrigin.y, recent.z + scatter.y);
            }

            // Find lowest-density cell and return its center
            int minVal = int.MaxValue;
            int minX = 0, minZ = 0;
            for (int x = 0; x < GridResolution; x++)
            {
                for (int z = 0; z < GridResolution; z++)
                {
                    if (_densityGrid[x, z] < minVal)
                    {
                        minVal = _densityGrid[x, z];
                        minX = x; minZ = z;
                    }
                }
            }

            float cellSize = GridWorldSize / GridResolution;
            float wx = sandboxOrigin.x - GridWorldSize / 2f + (minX + 0.5f) * cellSize;
            float wz = sandboxOrigin.z - GridWorldSize / 2f + (minZ + 0.5f) * cellSize;
            Vector2 jitter = UnityEngine.Random.insideUnitCircle * (cellSize * 0.4f);
            return new Vector3(wx + jitter.x, sandboxOrigin.y, wz + jitter.y);
        }

        private void IncrementDensityCell(Vector3 worldPos)
        {
            int cx = WorldToCell(worldPos.x, false);
            int cz = WorldToCell(worldPos.z, true);
            if (cx >= 0 && cx < GridResolution && cz >= 0 && cz < GridResolution)
                _densityGrid[cx, cz]++;
        }

        private static int WorldToCell(float worldVal, bool isZ)
        {
            // Assumes sandbox is centered at world origin
            float half = GridWorldSize / 2f;
            float normalized = (worldVal + half) / GridWorldSize;
            return Mathf.Clamp(Mathf.FloorToInt(normalized * GridResolution), 0, GridResolution - 1);
        }
    }
}

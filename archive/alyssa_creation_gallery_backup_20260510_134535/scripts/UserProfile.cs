using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AlgorithmicGallery.Recommendation
{
    public enum ArcPhase { Fascination, Recognition, Unease }

    public class GazeRecord
    {
        public string ModelId { get; set; }
        public float DwellMs { get; set; }
        public System.DateTime Timestamp { get; set; }

        public GazeRecord(string modelId, float dwellMs, System.DateTime timestamp)
        {
            ModelId = modelId;
            DwellMs = dwellMs;
            Timestamp = timestamp;
        }
    }

    public class UserProfile
    {
        private const float LearningRate = 0.15f;
        private const int FascinationToRecognitionCount = 12;
        private const int RecognitionToUneaseCount = 25;
        private const float FascinationToRecognitionSecs = 180f;
        private const float RecognitionToUneaseSecs = 360f;
        private const int DecliningDwellThreshold = 5;

        public Dictionary<string, float> PreferenceWeights { get; } = new();
        public List<GazeRecord> GazeHistory { get; } = new();
        public HashSet<string> ModelsShown { get; } = new();
        public int TotalSculpturesViewed { get; private set; }
        public float TotalSessionTimeSecs { get; private set; }
        public ArcPhase CurrentPhase { get; private set; } = ArcPhase.Fascination;
        public float PhaseProgress { get; private set; }
        public float AverageDwellMs { get; private set; }
        public float RecentDwellTrend { get; private set; }

        private readonly Queue<float> _recentDwells = new();
        private const int TrendWindow = 5;

        public void RecordGaze(string modelId, float dwellMs, IEnumerable<string> tags,
                               float elapsedSessionSecs, float maxDwellMs = 8000f)
        {
            GazeHistory.Add(new GazeRecord(modelId, dwellMs, System.DateTime.UtcNow));
            ModelsShown.Add(modelId);
            TotalSculpturesViewed++;
            TotalSessionTimeSecs = elapsedSessionSecs;
            if (AverageDwellMs == 0f) AverageDwellMs = dwellMs;
            else AverageDwellMs = AverageDwellMs * 0.7f + dwellMs * 0.3f;
            _recentDwells.Enqueue(dwellMs);
            if (_recentDwells.Count > TrendWindow) _recentDwells.Dequeue();
            RecentDwellTrend = ComputeTrend(_recentDwells.ToArray());
            float signal = Mathf.Clamp01(dwellMs / maxDwellMs);
            foreach (var tag in tags)
            {
                if (!PreferenceWeights.TryGetValue(tag, out float current))
                    current = 0f;
                PreferenceWeights[tag] = current * (1f - LearningRate) + signal * LearningRate;
            }
            UpdatePhase();
        }

        public List<string> GetTopNTags(int n)
        {
            return PreferenceWeights
                .OrderByDescending(kv => kv.Value)
                .Take(n)
                .Select(kv => kv.Key)
                .ToList();
        }

        public List<string> GetUnderexploredTags(IEnumerable<string> allTags)
        {
            var allList = allTags.ToList();
            return allList
                .OrderBy(t => GetValueOrDefault(PreferenceWeights, t, 0f))
                .Take(Math.Max(1, allList.Count / 3))
                .ToList();
        }

        private void UpdatePhase()
        {
            if (CurrentPhase == ArcPhase.Fascination)
            {
                PhaseProgress = Mathf.Clamp01((float)TotalSculpturesViewed / FascinationToRecognitionCount);
                bool countTriggered = TotalSculpturesViewed >= FascinationToRecognitionCount;
                bool timeTriggered = TotalSessionTimeSecs >= FascinationToRecognitionSecs;
                if (countTriggered || timeTriggered)
                {
                    CurrentPhase = ArcPhase.Recognition;
                    PhaseProgress = 0f;
                }
            }
            else if (CurrentPhase == ArcPhase.Recognition)
            {
                int viewedInPhase = TotalSculpturesViewed - FascinationToRecognitionCount;
                int phaseLen = RecognitionToUneaseCount - FascinationToRecognitionCount;
                PhaseProgress = Mathf.Clamp01((float)viewedInPhase / phaseLen);
                bool countTriggered = TotalSculpturesViewed >= RecognitionToUneaseCount;
                bool timeTriggered = TotalSessionTimeSecs >= RecognitionToUneaseSecs;
                bool boredTriggered = _recentDwells.Count >= DecliningDwellThreshold
                                    && RecentDwellTrend < -0.1f;
                if (countTriggered || timeTriggered || boredTriggered)
                {
                    CurrentPhase = ArcPhase.Unease;
                    PhaseProgress = 0f;
                }
            }
            else
            {
                int viewedInPhase = TotalSculpturesViewed - RecognitionToUneaseCount;
                PhaseProgress = Mathf.Clamp01(viewedInPhase / 15f);
            }
        }

        private static float ComputeTrend(float[] values)
        {
            if (values.Length < 2) return 0f;
            int n = values.Length;
            float sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0;
            for (int i = 0; i < n; i++)
            {
                sumX += i; sumY += values[i]; sumXY += i * values[i]; sumX2 += i * i;
            }
            float denom = n * sumX2 - sumX * sumX;
            if (Math.Abs(denom) < 1e-6f) return 0f;
            float slope = (n * sumXY - sumX * sumY) / denom;
            float avg = sumY / n;
            return avg > 0f ? Mathf.Clamp(slope / avg, -1f, 1f) : 0f;
        }

        private static float GetValueOrDefault(Dictionary<string, float> dict, string key, float defaultValue)
        {
            if (dict.TryGetValue(key, out float value))
                return value;
            return defaultValue;
        }
    }
}

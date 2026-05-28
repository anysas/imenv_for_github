using System;
using System.Collections.Generic;
using System.Linq;

namespace AlgorithmicGallery.Recommendation
{
    public class DesperationStrategy : IRecommendationStrategy
    {
        public ArcPhase Phase => ArcPhase.Unease;
        private const float ExploreRatio = 0.15f;
        private const int ExploitTagCount = 2;
        private const int PoolSize = 10;

        public ModelEntry GetNext(UserProfile profile, MetadataIndex index, System.Random rng)
        {
            var candidates = index.GetCandidates(profile.ModelsShown);
            if (candidates.Count == 0) return null;
            bool explore = (float)rng.NextDouble() < ExploreRatio;
            if (explore)
            {
                var scored = index.ScoreByPreferenceAscending(candidates, profile.PreferenceWeights);
                var pool = scored.Take(PoolSize).Select(x => x.model).ToList();
                return pool[rng.Next(pool.Count)];
            }
            else
            {
                var topTags = profile.GetTopNTags(ExploitTagCount);
                var scored = index.ScoreByPreference(candidates, profile.PreferenceWeights);
                var pool = scored.Take(PoolSize).Select(x => x.model).ToList();
                return WeightedRandomByScore(pool, profile.PreferenceWeights, rng);
            }
        }

        private static ModelEntry WeightedRandomByScore(List<ModelEntry> pool, Dictionary<string, float> weights, System.Random rng)
        {
            if (pool.Count == 0) throw new InvalidOperationException("Empty pool");
            var scores = pool.Select(m =>
                Math.Max(0.01f, m.FlatTags.Sum(t => GetValueOrDefault(weights, t, 0f)))).ToArray();
            float total = scores.Sum();
            float pick = (float)rng.NextDouble() * total;
            float acc = 0f;
            for (int i = 0; i < pool.Count; i++)
            {
                acc += scores[i];
                if (acc >= pick) return pool[i];
            }
            return pool[pool.Count - 1];
        }

        private static float GetValueOrDefault(Dictionary<string, float> dict, string key, float defaultValue)
        {
            if (dict.TryGetValue(key, out float value))
                return value;
            return defaultValue;
        }
    }
}

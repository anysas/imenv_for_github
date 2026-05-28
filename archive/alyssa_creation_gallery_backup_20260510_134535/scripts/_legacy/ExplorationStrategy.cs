using System;
using System.Collections.Generic;
using System.Linq;

namespace AlgorithmicGallery.Recommendation
{
    public class ExplorationStrategy : IRecommendationStrategy
    {
        public ArcPhase Phase => ArcPhase.Fascination;
        private const float ExploreRatio = 0.70f;
        private const int ExploitTagCount = 5;
        private const int PoolSize = 20;

        public ModelEntry GetNext(UserProfile profile, MetadataIndex index, System.Random rng)
        {
            var candidates = index.GetCandidates(profile.ModelsShown);
            if (candidates.Count == 0) return null;
            bool explore = (float)rng.NextDouble() < ExploreRatio;
            if (explore || profile.PreferenceWeights.Count == 0)
            {
                var underexplored = profile.GetUnderexploredTags(index.AllTagValues());
                var filtered = index.FilterByAnyTag(candidates, underexplored);
                var pool = filtered.Count > 0 ? filtered : candidates;
                return WeightedRandom(pool.Take(PoolSize).ToList(), rng);
            }
            else
            {
                var topTags = profile.GetTopNTags(ExploitTagCount);
                var scored = index.ScoreByPreference(candidates, profile.PreferenceWeights);
                var pool = scored.Take(PoolSize).Select(x => x.model).ToList();
                return WeightedRandom(pool, rng);
            }
        }

        private static ModelEntry WeightedRandom(List<ModelEntry> pool, System.Random rng)
        {
            return pool[rng.Next(pool.Count)];
        }
    }
}

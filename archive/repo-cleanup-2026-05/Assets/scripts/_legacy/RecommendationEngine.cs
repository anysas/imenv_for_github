using System;
using System.Collections.Generic;

namespace AlgorithmicGallery.Recommendation
{
    public class RecommendationEngine
    {
        private readonly MetadataIndex _index;
        private readonly System.Random _rng;
        public UserProfile Profile { get; } = new();
        private IRecommendationStrategy _strategy;
        private readonly Dictionary<ArcPhase, IRecommendationStrategy> _strategies;
        private ModelEntry _lastModel;

        public RecommendationEngine(MetadataIndex index, int seed = -1)
        {
            _index = index;
            _rng = seed >= 0 ? new System.Random(seed) : new System.Random();
            _strategies = new Dictionary<ArcPhase, IRecommendationStrategy>
            {
                [ArcPhase.Fascination] = new ExplorationStrategy(),
                [ArcPhase.Recognition] = new ExploitationStrategy(),
                [ArcPhase.Unease] = new DesperationStrategy(),
            };
            _strategy = _strategies[ArcPhase.Fascination];
        }

        public ModelEntry GetNext()
        {
            _lastModel = _strategy.GetNext(Profile, _index, _rng);
            return _lastModel;
        }

        public void ReportGaze(string modelId, float dwellMs, float elapsedSecs)
        {
            var model = _index.GetModelById(modelId);
            var tags = model?.FlatTags ?? new List<string>();
            Profile.RecordGaze(modelId, dwellMs, tags, elapsedSecs);
            SyncStrategy();
        }

        public ArcPhase CurrentPhase => Profile.CurrentPhase;
        public float PhaseProgress => Profile.PhaseProgress;

        private void SyncStrategy()
        {
            var targetPhase = Profile.CurrentPhase;
            if (_strategy.Phase != targetPhase)
                _strategy = _strategies[targetPhase];
        }
    }
}

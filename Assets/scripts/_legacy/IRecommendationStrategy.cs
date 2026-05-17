namespace AlgorithmicGallery.Recommendation
{
    public interface IRecommendationStrategy
    {
        ArcPhase Phase { get; }
        ModelEntry GetNext(UserProfile profile, MetadataIndex index, System.Random rng);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace AlgorithmicGallery.Corruption
{
    /// <summary>
    /// Built-in personal/corporate tag vocabularies for prompt scoring and curation UI.
    /// No external taxonomy file required.
    /// </summary>
    internal static class TagTaxonomy
    {
        private static readonly string[] FallbackPersonal =
        {
            "intimate","nostalgic","comforting","domestic","clinical","institutional","bureaucratic","threatening",
            "melancholy","abandoned","decayed","liminal","sacred","public","mundane","personal"
        };

        private static readonly string[] FallbackCorporate =
        {
            "engaging","sticky","discoverable","marketable","trend_aligned","conversion_ready","retention_friendly",
            "shareable","brand_safe","premium_feel","broad_appeal","niche_depth","monetizable","replayable",
            "recommendation_fit","campaign_ready"
        };

        private static readonly HashSet<string> PersonalSet = new(FallbackPersonal, StringComparer.Ordinal);
        private static readonly HashSet<string> CorporateSet = new(FallbackCorporate, StringComparer.Ordinal);

        public static IReadOnlyList<string> PersonalTags => FallbackPersonal;
        public static IReadOnlyList<string> CorporateTags => FallbackCorporate;

        public static void EnsureLoaded()
        {
            // No-op: tags are always in-memory fallbacks.
        }

        public static List<string> NormalizePersonal(IEnumerable<string> tags, out List<string> dropped)
            => NormalizeForSet(tags, PersonalSet, out dropped);

        public static List<string> NormalizeCorporate(IEnumerable<string> tags, out List<string> dropped)
            => NormalizeForSet(tags, CorporateSet, out dropped);

        private static List<string> NormalizeForSet(IEnumerable<string> tags, HashSet<string> allowed, out List<string> dropped)
        {
            var result = new List<string>();
            dropped = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var raw in tags ?? Enumerable.Empty<string>())
            {
                var tag = (raw ?? "").Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(tag) || !seen.Add(tag))
                    continue;
                if (allowed.Contains(tag))
                    result.Add(tag);
                else
                    dropped.Add(tag);
            }
            return result;
        }
    }
}

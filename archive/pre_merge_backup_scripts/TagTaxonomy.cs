using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace AlgorithmicGallery.Corruption
{
    [Serializable]
    internal class TagTaxonomyData
    {
        [JsonProperty("personal_tags")] public List<string> PersonalTags { get; set; } = new();
        [JsonProperty("corporate_tags")] public List<string> CorporateTags { get; set; } = new();
    }

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

        private static readonly HashSet<string> EmptySet = new();
        private static IReadOnlyList<string> _personal = FallbackPersonal;
        private static IReadOnlyList<string> _corporate = FallbackCorporate;
        private static HashSet<string> _personalSet = new(FallbackPersonal);
        private static HashSet<string> _corporateSet = new(FallbackCorporate);
        private static bool _loaded;

        public static IReadOnlyList<string> PersonalTags { get { EnsureLoaded(); return _personal; } }
        public static IReadOnlyList<string> CorporateTags { get { EnsureLoaded(); return _corporate; } }

        public static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                string path = Path.Combine(Application.streamingAssetsPath, "tag-taxonomy-v1.json");
                if (!File.Exists(path))
                {
                    Debug.LogWarning($"[TagTaxonomy] Missing taxonomy file at {path}; using fallback tags.");
                    return;
                }

                var data = JsonConvert.DeserializeObject<TagTaxonomyData>(File.ReadAllText(path));
                var personal = Normalize(data?.PersonalTags ?? new List<string>());
                var corporate = Normalize(data?.CorporateTags ?? new List<string>());
                if (personal.Count == 0 || corporate.Count == 0)
                {
                    Debug.LogWarning("[TagTaxonomy] Taxonomy parse yielded empty families; using fallback tags.");
                    return;
                }

                _personal = personal;
                _corporate = corporate;
                _personalSet = new HashSet<string>(personal, StringComparer.Ordinal);
                _corporateSet = new HashSet<string>(corporate, StringComparer.Ordinal);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TagTaxonomy] Failed to load taxonomy; using fallback tags. {e.Message}");
            }
        }

        public static List<string> NormalizePersonal(IEnumerable<string> tags, out List<string> dropped)
            => NormalizeForSet(tags, _personalSet, out dropped);

        public static List<string> NormalizeCorporate(IEnumerable<string> tags, out List<string> dropped)
            => NormalizeForSet(tags, _corporateSet, out dropped);

        private static List<string> NormalizeForSet(IEnumerable<string> tags, HashSet<string> allowed, out List<string> dropped)
        {
            EnsureLoaded();
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

        private static List<string> Normalize(IEnumerable<string> tags)
        {
            return tags
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim().ToLowerInvariant())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(t => t, StringComparer.Ordinal)
                .ToList();
        }
    }
}

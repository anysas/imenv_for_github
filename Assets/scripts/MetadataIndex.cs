using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace AlgorithmicGallery.Recommendation
{
    public class MetadataIndex
    {
        private List<ModelEntry> _all = new();
        private Dictionary<string, List<int>> _tagIndex = new();
        private Dictionary<string, ModelEntry> _modelLookup = new();

        private static readonly Dictionary<string, string[]> TagAdjacency = new()
        {
            ["tiny"] = new[] { "small" },
            ["small"] = new[] { "tiny", "medium" },
            ["medium"] = new[] { "small", "large" },
            ["large"] = new[] { "medium", "monumental" },
            ["monumental"] = new[] { "large" },
            ["low_poly"] = new[] { "medium_poly" },
            ["medium_poly"] = new[] { "low_poly", "high_poly" },
            ["high_poly"] = new[] { "medium_poly" },
            ["cubic"] = new[] { "wide", "flat" },
            ["wide"] = new[] { "cubic", "irregular" },
            ["tall"] = new[] { "irregular" },
            ["flat"] = new[] { "cubic", "wide" },
            ["irregular"] = new[] { "tall", "wide" },
            ["dark"] = new[] { "cool", "neutral" },
            ["cool"] = new[] { "dark", "neutral" },
            ["neutral"] = new[] { "cool", "warm" },
            ["warm"] = new[] { "neutral", "earth" },
            ["earth"] = new[] { "warm", "neutral" },
            ["matte"] = new[] { "rough" },
            ["rough"] = new[] { "matte" },
            ["glossy"] = new[] { "matte" },
            ["emissive"] = new[] { "glossy" },
            ["metallic"] = new[] { "glossy" },
            ["organic"] = new[] { "rough", "matte" },
            ["glass"] = new[] { "emissive", "glossy" },
            ["concrete"] = new[] { "rough", "matte" },
        };

        public int TotalCount => _all.Count;
        public IReadOnlyList<ModelEntry> AllModels => _all;

        public void LoadFromJsonString(string json)
        {
            var root = JsonConvert.DeserializeObject<MetadataRoot>(json)
                       ?? throw new System.Exception("Failed to deserialize metadata JSON");
            _all = root.Models;
            BuildIndex();
        }

        public List<ModelEntry> GetCandidates(HashSet<string> exclude)
        {
            var candidates = _all.Where(m => !exclude.Contains(m.Id)).ToList();
            Shuffle(candidates);
            return candidates;
        }

        private static void Shuffle<T>(List<T> list)
        {
            int n = list.Count;
            while (n > 1)
            {
                n--;
                int k = UnityEngine.Random.Range(0, n + 1);
                (list[k], list[n]) = (list[n], list[k]);
            }
        }

        public ModelEntry GetModelById(string modelId)
        {
            if (_modelLookup.TryGetValue(modelId, out var model))
                return model;
            return null;
        }

        public List<ModelEntry> FilterByAnyTag(List<ModelEntry> candidates, IEnumerable<string> tags)
        {
            var tagSet = new HashSet<string>(tags);
            return candidates.Where(m => m.FlatTags.Any(t => tagSet.Contains(t))).ToList();
        }

        public List<ModelEntry> FilterByAdjacentTags(List<ModelEntry> candidates, IEnumerable<string> topTags)
        {
            var adjacent = new HashSet<string>();
            foreach (var tag in topTags)
                if (TagAdjacency.TryGetValue(tag, out var neighbours))
                    foreach (var n in neighbours)
                        adjacent.Add(n);
            foreach (var tag in topTags)
                adjacent.Remove(tag);
            return FilterByAnyTag(candidates, adjacent);
        }

        public List<(ModelEntry model, float score)> ScoreByPreference(
            List<ModelEntry> candidates, Dictionary<string, float> weights)
        {
            return candidates
                .Select(m => (m, score: m.FlatTags.Sum(t => GetValueOrDefault(weights, t, 0f))))
                .OrderByDescending(x => x.score)
                .ToList();
        }

        public List<(ModelEntry model, float score)> ScoreByPreferenceAscending(
            List<ModelEntry> candidates, Dictionary<string, float> weights)
        {
            return candidates
                .Select(m => (m, score: m.FlatTags.Sum(t => GetValueOrDefault(weights, t, 0f))))
                .OrderBy(x => x.score)
                .ToList();
        }

        public IEnumerable<string> AllTagValues() => _tagIndex.Keys;

        private void BuildIndex()
        {
            _tagIndex.Clear();
            _modelLookup.Clear();
            foreach (var (model, idx) in _all.Select((m, i) => (m, i)))
            {
                var flat = new List<string>();
                flat.AddRange(model.Tags.MaterialTypes);
                if (!string.IsNullOrEmpty(model.Tags.ColorMood)) flat.Add(model.Tags.ColorMood);
                if (!string.IsNullOrEmpty(model.Tags.Scale)) flat.Add(model.Tags.Scale);
                if (!string.IsNullOrEmpty(model.Tags.Complexity)) flat.Add(model.Tags.Complexity);
                if (!string.IsNullOrEmpty(model.Tags.Silhouette)) flat.Add(model.Tags.Silhouette);
                model.FlatTags = flat;
                _modelLookup[model.Id] = model;
                foreach (var tag in flat)
                {
                    if (!_tagIndex.ContainsKey(tag))
                        _tagIndex[tag] = new List<int>();
                    _tagIndex[tag].Add(idx);
                }
            }
        }

        private static float GetValueOrDefault(Dictionary<string, float> dict, string key, float defaultValue)
        {
            if (dict.TryGetValue(key, out float value))
                return value;
            return defaultValue;
        }

        private class MetadataRoot
        {
            [JsonProperty("models")]
            public List<ModelEntry> Models { get; set; } = new();
        }
    }
}

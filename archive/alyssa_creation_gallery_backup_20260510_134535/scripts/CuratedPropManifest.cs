using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace AlgorithmicGallery.Corruption
{
    [Serializable]
    public class PropEntry
    {
        [JsonProperty("id")]       public string Id { get; set; }
        [JsonProperty("glb_path")] public string GlbPath { get; set; }
        [JsonProperty("display_name")] public string DisplayName { get; set; }
        [JsonProperty("group")]    public string Group { get; set; }
        [JsonProperty("category")] public string Category { get; set; }
        [JsonProperty("tags")]     public List<string> Tags { get; set; } = new();
        [JsonProperty("emotional_tags")] public List<string> EmotionalTags { get; set; } = new();
        [JsonProperty("poly_count")]    public int PolyCount { get; set; }
        [JsonProperty("dimensions")]    public PropDimensions Dimensions { get; set; } = new();
        [JsonProperty("size_category")] public string SizeCategory { get; set; } = "unknown";
        [JsonProperty("confidence")]    public float Confidence { get; set; } = 1f;
        [JsonProperty("vertex_count")]  public int VertexCount { get; set; }

        // Curation-overlay runtime fields (not in base JSON; applied by CurationOverlay.ApplyToManifest)
        [JsonProperty("scale_override")] public float ScaleOverride { get; set; } = 0f;  // 0 = auto
        [JsonProperty("custom_tags")]    public List<string> CustomTags { get; set; } = new();
        [JsonProperty("notes")]          public string Notes { get; set; } = "";

        // Convenience: longest axis in metres (0 if dimensions unknown)
        public float LongestAxis => Mathf.Max(
            Dimensions?.X ?? 0f,
            Mathf.Max(Dimensions?.Y ?? 0f, Dimensions?.Z ?? 0f));
    }

    [Serializable]
    public class PropDimensions
    {
        [JsonProperty("x")] public float X { get; set; }
        [JsonProperty("y")] public float Y { get; set; }
        [JsonProperty("z")] public float Z { get; set; }
    }

    public class CuratedPropManifest
    {
        private List<PropEntry> _all = new();
        private List<PropEntry> _allHighConf = new();   // confidence ≥ 0.8 (pipeline-validated)
        private Dictionary<string, List<PropEntry>> _byGroup = new();
        private Dictionary<string, List<PropEntry>> _byEmotionalTag = new();
        private Dictionary<string, PropEntry> _byId = new();
        private Dictionary<string, List<PropEntry>> _byNameToken = new();

        public int Count => _all.Count;
        public IReadOnlyList<PropEntry> All => _all;
        public IEnumerable<string> Groups => _byGroup.Keys;

        public static CuratedPropManifest LoadFromStreamingAssets()
        {
            string path = Path.Combine(Application.streamingAssetsPath, "curated-props.json");
            if (!File.Exists(path))
            {
                Debug.LogError($"CuratedPropManifest: file not found at {path}");
                return null;
            }

            string json = File.ReadAllText(path);
            var root = JsonConvert.DeserializeObject<ManifestRoot>(json);
            if (root?.Props == null)
            {
                Debug.LogError("CuratedPropManifest: failed to parse JSON");
                return null;
            }

            var manifest = new CuratedPropManifest();
            manifest._all = root.Props;
            manifest._allHighConf = root.Props.Where(p => p.Confidence >= 0.8f).ToList();
            foreach (var p in root.Props)
            {
                if (!string.IsNullOrEmpty(p.Id))
                    manifest._byId[p.Id] = p;

                if (!manifest._byGroup.TryGetValue(p.Group, out var list))
                    manifest._byGroup[p.Group] = list = new List<PropEntry>();
                list.Add(p);

                foreach (var etag in p.EmotionalTags ?? Enumerable.Empty<string>())
                {
                    if (!manifest._byEmotionalTag.TryGetValue(etag, out var tagList))
                        manifest._byEmotionalTag[etag] = tagList = new List<PropEntry>();
                    tagList.Add(p);
                }

                foreach (var token in TokenizeName(p.DisplayName))
                {
                    if (!manifest._byNameToken.TryGetValue(token, out var tokenList))
                        manifest._byNameToken[token] = tokenList = new List<PropEntry>();
                    tokenList.Add(p);
                }
            }

            // Log enrichment stats so we can see pipeline results at load time.
            int withDims  = root.Props.Count(p => p.LongestAxis > 0.001f);
            int highConf  = root.Props.Count(p => p.Confidence >= 0.8f);
            Debug.Log($"CuratedPropManifest: loaded {root.Props.Count} props across " +
                      $"{manifest._byGroup.Count} groups, " +
                      $"{manifest._byEmotionalTag.Count} emotional tags | " +
                      $"dims={withDims} conf≥0.8={highConf}");
            return manifest;
        }

        public PropEntry GetById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return _byId.TryGetValue(id, out var prop) ? prop : null;
        }

        public List<PropEntry> FindByNameTokens(IEnumerable<string> tokens, int max = 30, HashSet<string> excludeIds = null)
        {
            var tokenList = (tokens ?? Enumerable.Empty<string>())
                .Select(t => t?.Trim().ToLowerInvariant())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct()
                .ToList();

            if (tokenList.Count == 0) return new List<PropEntry>();

            var scores = new Dictionary<string, int>();
            foreach (var token in tokenList)
            {
                if (!_byNameToken.TryGetValue(token, out var candidates)) continue;
                foreach (var c in candidates)
                {
                    if (c == null || string.IsNullOrEmpty(c.Id)) continue;
                    if (excludeIds != null && excludeIds.Contains(c.Id)) continue;
                    scores[c.Id] = scores.TryGetValue(c.Id, out var s) ? s + 1 : 1;
                }
            }

            return scores
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key)
                .Take(Mathf.Max(1, max))
                .Select(kv => GetById(kv.Key))
                .Where(p => p != null)
                .ToList();
        }

        public PropEntry GetWeightedByPromptIntent(
            PromptDefinition prompt,
            IEnumerable<string> nameTokens,
            HashSet<string> excludeIds = null)
        {
            if (prompt == null) return GetRandomHighConf();

            // 1) Literal matches first
            var literal = FindByNameTokens(nameTokens, max: 24, excludeIds: excludeIds);
            if (literal.Count > 0)
            {
                int top = Mathf.Min(6, literal.Count);
                return literal[UnityEngine.Random.Range(0, top)];
            }

            // 2) Seed IDs if present
            if (prompt.ResolvedSeedPropIds != null && prompt.ResolvedSeedPropIds.Length > 0)
            {
                var pool = prompt.ResolvedSeedPropIds
                    .Select(GetById)
                    .Where(p => p != null && (excludeIds == null || !excludeIds.Contains(p.Id)))
                    .ToList();
                if (pool.Count > 0)
                    return pool[UnityEngine.Random.Range(0, pool.Count)];
            }

            // 3) Emotional+group fallback
            if (prompt.EmotionalTags != null && prompt.EmotionalTags.Length > 0)
                return GetWeightedByEmotionalTagsInGroups(
                    prompt.EmotionalTags,
                    prompt.PrimaryGroups,
                    randomness: prompt.IsAbstract ? 0.12f : 0.2f,
                    excludeIds: excludeIds
                );

            // 4) Safe random fallback
            return GetRandomHighConfFromGroups(prompt.PrimaryGroups, excludeIds);
        }

        public PropEntry GetRandom()
        {
            if (_all.Count == 0) return null;
            return _all[UnityEngine.Random.Range(0, _all.Count)];
        }

        // Returns a random prop with confidence ≥ 0.8 (pipeline-validated geometry).
        // Used as the fallback for abstract/emotional prompts where any prop must feel intentional.
        public PropEntry GetRandomHighConf()
        {
            var pool = _allHighConf.Count > 0 ? _allHighConf : _all;
            return pool[UnityEngine.Random.Range(0, pool.Count)];
        }

        // Returns a random prop from the given groups, constrained to high-confidence entries.
        public PropEntry GetRandomHighConfFromGroups(string[] groups, HashSet<string> excludeIds = null)
        {
            if (groups == null || groups.Length == 0) return GetRandomHighConf();
            var pool = _allHighConf.Count > 0 ? _allHighConf : _all;
            var candidates = pool.Where(p => Array.IndexOf(groups, p.Group) >= 0);
            if (excludeIds != null)
                candidates = candidates.Where(p => !excludeIds.Contains(p.Id));
            var list = candidates.ToList();
            if (list.Count == 0) return GetRandomFromGroups(groups, excludeIds);
            return list[UnityEngine.Random.Range(0, list.Count)];
        }

        public PropEntry GetRandomFromGroup(string group)
        {
            if (_byGroup.TryGetValue(group, out var list) && list.Count > 0)
                return list[UnityEngine.Random.Range(0, list.Count)];
            return GetRandom();
        }

        // Returns a random prop whose tags overlap with the given tag set (weighted toward best match).
        // Falls back to fully random if no match found.
        public PropEntry GetWeightedByTags(IEnumerable<string> preferredTags, float randomness = 0.2f)
        {
            if (_all.Count == 0) return null;
            if (UnityEngine.Random.value < randomness)
                return GetRandom();

            var tagSet = new HashSet<string>(preferredTags);
            var scored = _all
                .Select(p => (prop: p, score: p.Tags.Count(t => tagSet.Contains(t))))
                .Where(x => x.score > 0)
                .OrderByDescending(x => x.score)
                .ToList();

            if (scored.Count == 0) return GetRandom();

            // Weighted sample from top matches
            int topN = Mathf.Min(10, scored.Count);
            return scored[UnityEngine.Random.Range(0, topN)].prop;
        }

        public PropEntry GetRandomFromGroups(string[] groups, HashSet<string> excludeIds = null)
        {
            if (groups == null || groups.Length == 0) return GetRandom();
            var candidates = _all.Where(p => Array.IndexOf(groups, p.Group) >= 0);
            if (excludeIds != null)
                candidates = candidates.Where(p => !excludeIds.Contains(p.Id));
            var list = candidates.ToList();
            if (list.Count == 0) return GetRandom();
            return list[UnityEngine.Random.Range(0, list.Count)];
        }

        public PropEntry GetWeightedByTagsInGroups(IEnumerable<string> preferredTags, string[] groups,
            float randomness = 0.2f, HashSet<string> excludeIds = null)
        {
            if (_all.Count == 0) return null;
            if (UnityEngine.Random.value < randomness)
                return GetRandomFromGroups(groups, excludeIds);

            var tagSet = new HashSet<string>(preferredTags);
            var pool = groups != null && groups.Length > 0
                ? _all.Where(p => Array.IndexOf(groups, p.Group) >= 0)
                : (IEnumerable<PropEntry>)_all;

            if (excludeIds != null)
                pool = pool.Where(p => !excludeIds.Contains(p.Id));

            var scored = pool
                .Select(p => (prop: p, score: p.Tags.Count(t => tagSet.Contains(t))))
                .Where(x => x.score > 0)
                .OrderByDescending(x => x.score)
                .ToList();

            if (scored.Count == 0) return GetRandomFromGroups(groups, excludeIds);
            return WeightedPick(scored);
        }

        // Returns a prop from adjacent/different tags — used for assistant drift behavior.
        public PropEntry GetDriftedFromTags(IEnumerable<string> avoidTags, float driftStrength = 0.5f)
        {
            if (_all.Count == 0) return null;
            if (UnityEngine.Random.value > driftStrength)
                return GetRandom();

            var avoidSet = new HashSet<string>(avoidTags);
            var candidates = _all.Where(p => !p.Tags.Any(t => avoidSet.Contains(t))).ToList();
            if (candidates.Count == 0) return GetRandom();
            return candidates[UnityEngine.Random.Range(0, candidates.Count)];
        }

        public PropEntry GetFromDriftGroups(string[] driftGroups)
        {
            if (driftGroups == null || driftGroups.Length == 0) return GetRandom();
            var candidates = _all.Where(p => Array.IndexOf(driftGroups, p.Group) >= 0).ToList();
            if (candidates.Count == 0) return GetRandom();
            return candidates[UnityEngine.Random.Range(0, candidates.Count)];
        }

        // Selects a prop by scoring emotional_tags overlap within the given group filter.
        // Use this for abstract/emotional prompts where emotional texture matters more than category.
        // excludeIds: set of prop IDs to skip (for hotbar dedup).
        public PropEntry GetWeightedByEmotionalTagsInGroups(
            IEnumerable<string> emotionalTags, string[] groups, float randomness = 0.2f,
            HashSet<string> excludeIds = null)
        {
            if (_all.Count == 0) return null;
            if (UnityEngine.Random.value < randomness)
                return GetRandomFromGroups(groups, excludeIds);

            var tagSet = new HashSet<string>(emotionalTags);
            var pool = groups != null && groups.Length > 0
                ? _all.Where(p => Array.IndexOf(groups, p.Group) >= 0)
                : (IEnumerable<PropEntry>)_all;

            if (excludeIds != null)
                pool = pool.Where(p => !excludeIds.Contains(p.Id));

            var scored = pool
                .Select(p => (prop: p, score: p.EmotionalTags.Count(t => tagSet.Contains(t))))
                .Where(x => x.score > 0)
                .OrderByDescending(x => x.score)
                .ToList();

            if (scored.Count == 0) return GetRandomFromGroups(groups, excludeIds);
            return WeightedPick(scored);
        }

        // Selects a drift prop whose emotional_tags match the given drift vocabulary,
        // constrained to the drift groups. Used during assistant Suggesting/Overriding phases
        // with abstract prompts — the system pushes toward an emotionally alien register.
        public PropEntry GetFromDriftEmotionalGroups(
            string[] driftEmotionalTags, string[] driftGroups, float randomness = 0.2f)
        {
            if (_all.Count == 0) return null;
            if (driftEmotionalTags == null || driftEmotionalTags.Length == 0)
                return GetFromDriftGroups(driftGroups);
            if (UnityEngine.Random.value < randomness)
                return GetFromDriftGroups(driftGroups);

            var tagSet = new HashSet<string>(driftEmotionalTags);
            var pool = driftGroups != null && driftGroups.Length > 0
                ? _all.Where(p => Array.IndexOf(driftGroups, p.Group) >= 0)
                : (IEnumerable<PropEntry>)_all;

            var scored = pool
                .Select(p => (prop: p, score: p.EmotionalTags.Count(t => tagSet.Contains(t))))
                .Where(x => x.score > 0)
                .OrderByDescending(x => x.score)
                .ToList();

            if (scored.Count == 0) return GetFromDriftGroups(driftGroups);
            int topN = Mathf.Min(12, scored.Count);
            return scored[UnityEngine.Random.Range(0, topN)].prop;
        }

        // Score-weighted random: higher-scoring props are proportionally more likely.
        private static PropEntry WeightedPick(List<(PropEntry prop, int score)> scored)
        {
            if (scored.Count == 0) return null;
            int totalScore = 0;
            foreach (var s in scored) totalScore += s.score;
            int roll = UnityEngine.Random.Range(0, totalScore);
            int acc = 0;
            foreach (var s in scored)
            {
                acc += s.score;
                if (roll < acc) return s.prop;
            }
            return scored[scored.Count - 1].prop;
        }

        private class ManifestRoot
        {
            [JsonProperty("props")] public List<PropEntry> Props { get; set; }
        }

        private static IEnumerable<string> TokenizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                yield break;

            var chars = name.ToLowerInvariant()
                .Select(c => char.IsLetterOrDigit(c) ? c : ' ')
                .ToArray();
            foreach (var token in new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (token.Length >= 2)
                    yield return token;
            }
        }
    }
}

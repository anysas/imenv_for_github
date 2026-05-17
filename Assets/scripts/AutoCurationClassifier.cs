using System;
using System.Collections.Generic;
using System.Linq;

namespace AlgorithmicGallery.Corruption
{
    [Serializable]
    public class AutoCurationReason
    {
        public string Code;
        public string Detail;
        public float Weight;
    }

    [Serializable]
    public class AutoCurationDecision
    {
        public string Id;
        public string DisplayName;
        public string Category;
        public string GlbPath;
        public float Score;
        public float Threshold;
        public bool ShouldRemove;
        public string ConfidenceBand;
        public List<AutoCurationReason> Reasons = new();
    }

    [Serializable]
    public class AutoCurationRunResult
    {
        public string Preset;
        public float Threshold;
        public int TotalScanned;
        public int ProposedRemovals;
        public int HighConfidenceRemovals;
        public int MediumConfidenceRemovals;
        public int LowConfidenceRemovals;
        public List<AutoCurationDecision> Decisions = new();
    }

    public static class AutoCurationClassifier
    {
        public static AutoCurationRunResult Classify(
            IEnumerable<PropEntry> props,
            AutoCurationPreset preset)
        {
            var source = (props ?? Enumerable.Empty<PropEntry>()).Where(p => p != null).ToList();
            float threshold = AutoCurationConfig.ThresholdFor(preset);

            var decisions = source.Select(p => Evaluate(p, threshold)).ToList();

            var result = new AutoCurationRunResult
            {
                Preset = preset.ToString().ToLowerInvariant(),
                Threshold = threshold,
                TotalScanned = source.Count,
                ProposedRemovals = decisions.Count(d => d.ShouldRemove),
                HighConfidenceRemovals = decisions.Count(d => d.ShouldRemove && d.ConfidenceBand == "high"),
                MediumConfidenceRemovals = decisions.Count(d => d.ShouldRemove && d.ConfidenceBand == "medium"),
                LowConfidenceRemovals = decisions.Count(d => d.ShouldRemove && d.ConfidenceBand == "low"),
                Decisions = decisions.OrderByDescending(d => d.Score).ThenBy(d => d.Id).ToList(),
            };

            return result;
        }

        private static AutoCurationDecision Evaluate(PropEntry p, float threshold)
        {
            var cfg = AutoCurationConfig.Current;
            var reasons = new List<AutoCurationReason>();

            string corpus = BuildCorpus(p);
            AddKeywordHits(corpus, cfg.CharacterKeywords, "character_like", 0.85f, reasons);
            AddKeywordHits(corpus, cfg.ViewmodelKeywords, "viewmodel_like", 1.00f, reasons);
            AddKeywordHits(corpus, cfg.LargeStructureKeywords, "large_structure_named", 0.55f, reasons);

            float longestAxis = p.LongestAxis;
            if (longestAxis >= cfg.LargeAxisHardM)
            {
                reasons.Add(new AutoCurationReason
                {
                    Code = "large_axis_hard",
                    Detail = $"longest_axis={longestAxis:F2}m >= {cfg.LargeAxisHardM:F2}m",
                    Weight = 1.0f,
                });
            }
            else if (longestAxis >= cfg.LargeAxisSoftM)
            {
                reasons.Add(new AutoCurationReason
                {
                    Code = "large_axis_soft",
                    Detail = $"longest_axis={longestAxis:F2}m >= {cfg.LargeAxisSoftM:F2}m",
                    Weight = 0.55f,
                });
            }

            int vertices = Math.Max(p.VertexCount, p.PolyCount);
            if (vertices >= cfg.VertexHard)
            {
                reasons.Add(new AutoCurationReason
                {
                    Code = "vertex_hard",
                    Detail = $"vertex_or_poly={vertices} >= {cfg.VertexHard}",
                    Weight = 0.7f,
                });
            }
            else if (vertices >= cfg.VertexSoft)
            {
                reasons.Add(new AutoCurationReason
                {
                    Code = "vertex_soft",
                    Detail = $"vertex_or_poly={vertices} >= {cfg.VertexSoft}",
                    Weight = 0.35f,
                });
            }

            string size = (p.SizeCategory ?? string.Empty).ToLowerInvariant();
            if (size.Contains("xlarge") || size.Contains("xl") || size.Contains("huge"))
            {
                reasons.Add(new AutoCurationReason
                {
                    Code = "size_category_large",
                    Detail = $"size_category={size}",
                    Weight = 0.35f,
                });
            }

            float score = reasons.Sum(r => r.Weight);
            bool shouldRemove = score >= threshold;
            string band = ComputeBand(score, threshold, shouldRemove);

            return new AutoCurationDecision
            {
                Id = p.Id,
                DisplayName = p.DisplayName,
                Category = p.Category,
                GlbPath = p.GlbPath,
                Score = score,
                Threshold = threshold,
                ShouldRemove = shouldRemove,
                ConfidenceBand = band,
                Reasons = reasons,
            };
        }

        private static string BuildCorpus(PropEntry p)
        {
            var chunks = new[]
            {
                p.DisplayName ?? string.Empty,
                p.Category ?? string.Empty,
                p.GlbPath ?? string.Empty,
                p.SizeCategory ?? string.Empty,
                p.Group ?? string.Empty,
            };
            return string.Join(" ", chunks).ToLowerInvariant();
        }

        private static void AddKeywordHits(
            string corpus,
            IEnumerable<string> keywords,
            string code,
            float baseWeight,
            List<AutoCurationReason> reasons)
        {
            if (string.IsNullOrWhiteSpace(corpus) || keywords == null) return;

            foreach (var raw in keywords)
            {
                string k = (raw ?? string.Empty).Trim().ToLowerInvariant();
                if (k.Length < 2) continue;
                if (!corpus.Contains(k)) continue;

                reasons.Add(new AutoCurationReason
                {
                    Code = code,
                    Detail = $"keyword={k}",
                    Weight = baseWeight,
                });
            }
        }

        private static string ComputeBand(float score, float threshold, bool removed)
        {
            if (!removed) return "none";
            float margin = score - threshold;
            if (margin >= 0.60f) return "high";
            if (margin >= 0.25f) return "medium";
            return "low";
        }
    }
}

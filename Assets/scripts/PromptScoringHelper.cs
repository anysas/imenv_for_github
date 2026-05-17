using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace AlgorithmicGallery.Corruption
{
    /// <summary>
    /// Builds the 2 personal + 1 corporate scoring/flatten triplet and display strings.
    /// </summary>
    public static class PromptScoringHelper
    {
        /// <summary>Maps emotional register tags to corporate taxonomy slugs (weights sum arbitrarily).</summary>
        private static readonly Dictionary<string, (string slug, float w)[]> EmotionToCorporate =
            new(StringComparer.Ordinal)
            {
                ["nostalgic"] = new[] { ("shareable", 1.2f), ("recommendation_fit", 1f), ("broad_appeal", 0.9f), ("marketable", 0.6f) },
                ["personal"] = new[] { ("engaging", 1.1f), ("sticky", 1f), ("brand_safe", 0.8f), ("marketable", 0.7f) },
                ["intimate"] = new[] { ("sticky", 1.2f), ("premium_feel", 1f), ("retention_friendly", 0.9f) },
                ["comforting"] = new[] { ("sticky", 1.1f), ("brand_safe", 1f), ("premium_feel", 0.9f), ("retention_friendly", 0.7f) },
                ["domestic"] = new[] { ("broad_appeal", 1f), ("marketable", 0.9f), ("discoverable", 0.7f) },
                ["mundane"] = new[] { ("marketable", 1f), ("conversion_ready", 0.9f), ("campaign_ready", 0.8f) },
                ["clinical"] = new[] { ("monetizable", 1.2f), ("premium_feel", 1f), ("retention_friendly", 1f) },
                ["institutional"] = new[] { ("marketable", 1.2f), ("campaign_ready", 1.1f), ("brand_safe", 1f) },
                ["bureaucratic"] = new[] { ("campaign_ready", 1.2f), ("conversion_ready", 1f), ("brand_safe", 0.9f) },
                ["threatening"] = new[] { ("engaging", 1.3f), ("sticky", 1f), ("discoverable", 0.8f) },
                ["melancholy"] = new[] { ("retention_friendly", 1.1f), ("recommendation_fit", 1f), ("engaging", 0.8f) },
                ["abandoned"] = new[] { ("discoverable", 1f), ("trend_aligned", 1f), ("engaging", 0.9f) },
                ["decayed"] = new[] { ("trend_aligned", 1.1f), ("broad_appeal", 0.9f), ("marketable", 0.7f) },
                ["sacred"] = new[] { ("premium_feel", 1.2f), ("brand_safe", 1.1f), ("niche_depth", 0.9f) },
                ["liminal"] = new[] { ("discoverable", 1.1f), ("engaging", 1f), ("replayable", 0.9f) },
                ["public"] = new[] { ("broad_appeal", 1.2f), ("shareable", 1.1f), ("campaign_ready", 0.9f) },
            };

        private static readonly (Regex re, string slug, float w)[] CorporateKeywordHints =
        {
            (new Regex(@"\b(marketable|market\s*fit|sales|sell|brand)\b", RegexOptions.IgnoreCase), "marketable", 2.2f),
            (new Regex(@"\b(engage|engaging|attention|hook)\b", RegexOptions.IgnoreCase), "engaging", 2f),
            (new Regex(@"\b(retain|retention|sticky|return|habit)\b", RegexOptions.IgnoreCase), "retention_friendly", 2f),
            (new Regex(@"\b(monetis|monetiz|revenue|profit|conversion)\b", RegexOptions.IgnoreCase), "monetizable", 2.2f),
            (new Regex(@"\b(viral|share|social)\b", RegexOptions.IgnoreCase), "shareable", 1.6f),
            (new Regex(@"\b(trend|fashion|zeitgeist)\b", RegexOptions.IgnoreCase), "trend_aligned", 1.5f),
            (new Regex(@"\b(premium|luxury|high\s*end)\b", RegexOptions.IgnoreCase), "premium_feel", 1.4f),
            (new Regex(@"\b(campaign|ads|advert)\b", RegexOptions.IgnoreCase), "campaign_ready", 1.6f),
            (new Regex(@"\b(discover|discoverable|algorithm|feed)\b", RegexOptions.IgnoreCase), "discoverable", 1.4f),
        };

        private static readonly string[] CorporateFallbackOrder =
        {
            "marketable", "engaging", "retention_friendly", "monetizable", "sticky", "discoverable", "conversion_ready"
        };

        public static void EnsureCorporateTarget(PromptDefinition prompt)
        {
            if (prompt == null) return;
            if (!string.IsNullOrWhiteSpace(prompt.CorporateTargetTag)) return;

            prompt.CorporateTargetTag = PickCorporateFromEmotionalTags(prompt.EmotionalTags);
        }

        public static string PickCorporateFromUserTextAndEmotions(
            string userText,
            IReadOnlyList<string> topEmotionalTags,
            IReadOnlyDictionary<string, float> emotionalScores)
        {
            TagTaxonomy.EnsureLoaded();
            var allowed = new HashSet<string>(TagTaxonomy.CorporateTags, StringComparer.Ordinal);
            var scores = new Dictionary<string, float>(StringComparer.Ordinal);

            void Add(string slug, float w)
            {
                if (string.IsNullOrWhiteSpace(slug) || w <= 0f) return;
                var key = slug.Trim().ToLowerInvariant();
                if (allowed.Count > 0 && !allowed.Contains(key)) return;
                scores[key] = scores.GetValueOrDefault(key) + w;
            }

            string t = userText ?? "";
            foreach (var (re, slug, w) in CorporateKeywordHints)
            {
                int hits = re.Matches(t).Count;
                if (hits > 0)
                    Add(slug, w * hits);
            }

            if (topEmotionalTags != null && emotionalScores != null)
            {
                for (int i = 0; i < topEmotionalTags.Count; i++)
                {
                    string em = topEmotionalTags[i];
                    if (string.IsNullOrEmpty(em)) continue;
                    float tw = emotionalScores.TryGetValue(em, out var s) ? s : 1f;
                    float rank = Mathf.Lerp(1f, 0.65f, topEmotionalTags.Count > 1 ? i / (float)(topEmotionalTags.Count - 1) : 0f);
                    if (!EmotionToCorporate.TryGetValue(em, out var pairs)) continue;
                    foreach (var (slug, w) in pairs)
                        Add(slug, w * tw * rank);
                }
            }

            if (scores.Count == 0)
            {
                foreach (var slug in CorporateFallbackOrder)
                {
                    if (allowed.Count == 0 || allowed.Contains(slug))
                        return slug;
                }
                return allowed.FirstOrDefault() ?? "marketable";
            }

            float best = scores.Values.Max();
            var top = scores.Where(kv => Mathf.Abs(kv.Value - best) < 0.0001f).Select(kv => kv.Key).OrderBy(s => s, StringComparer.Ordinal).First();
            return top;
        }

        private static string PickCorporateFromEmotionalTags(string[] emotionalTags)
        {
            var list = (emotionalTags ?? Array.Empty<string>()).Where(s => !string.IsNullOrWhiteSpace(s)).Take(6).ToList();
            var scores = new Dictionary<string, float>(StringComparer.Ordinal);
            foreach (var em in list)
                scores[em] = scores.GetValueOrDefault(em) + 1f;
            return PickCorporateFromUserTextAndEmotions("", list, scores);
        }

        public static string[] BuildThreeFlattenLabels(PromptDefinition prompt)
        {
            if (prompt == null)
                return new[] { "Personal", "Nostalgic", "Marketable" };

            EnsureCorporateTarget(prompt);
            var emotional = (prompt.EmotionalTags ?? Array.Empty<string>())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            string p0 = emotional.Count > 0 ? TitleCaseEmotional(emotional[0]) : "Personal";
            string p1 = emotional.Count > 1 ? TitleCaseEmotional(emotional[1]) : NextDistinctPersonal(emotional, p0);

            string c0 = CorporateBarDisplayLabel(prompt);

            return new[] { p0, p1, c0 };
        }

        /// <summary>
        /// Authoritative short label for the corporate score bar (index 2) and matching floaters.
        /// </summary>
        public static string CorporateBarDisplayLabel(PromptDefinition prompt)
        {
            if (prompt == null) return CorporateShortDisplay("marketable");
            EnsureCorporateTarget(prompt);
            string slug = string.IsNullOrWhiteSpace(prompt.CorporateTargetTag)
                ? "marketable"
                : prompt.CorporateTargetTag.Trim().ToLowerInvariant();
            return CorporateShortDisplay(slug);
        }

        private static string NextDistinctPersonal(List<string> emotional, string alreadyDisplay)
        {
            foreach (var e in emotional.Skip(2))
            {
                var d = TitleCaseEmotional(e);
                if (!string.Equals(d, alreadyDisplay, StringComparison.OrdinalIgnoreCase))
                    return d;
            }
            return "Intimate";
        }

        public static string TitleCaseEmotional(string slugOrWord)
        {
            if (string.IsNullOrWhiteSpace(slugOrWord)) return "Personal";
            var s = slugOrWord.Trim().ToLowerInvariant();
            if (s.Contains('_'))
            {
                var parts = s.Split('_');
                return string.Join(" ", parts.Select(p => char.ToUpperInvariant(p[0]) + (p.Length > 1 ? p.Substring(1) : "")));
            }
            return char.ToUpperInvariant(s[0]) + (s.Length > 1 ? s.Substring(1) : "");
        }

        /// <summary>Short label for bars / popups (e.g. "Retention" not full slug).</summary>
        public static string CorporateShortDisplay(string slug)
        {
            if (string.IsNullOrWhiteSpace(slug)) return "Marketable";
            switch (slug.Trim().ToLowerInvariant())
            {
                case "marketable": return "Marketable";
                case "engaging": return "Engaging";
                case "retention_friendly": return "Retention";
                case "monetizable": return "Monetisable";
                case "sticky": return "Sticky";
                case "discoverable": return "Discoverable";
                case "shareable": return "Shareable";
                case "trend_aligned": return "Trending";
                case "conversion_ready": return "Conversion";
                case "campaign_ready": return "Campaign";
                case "brand_safe": return "Brand-safe";
                case "premium_feel": return "Premium";
                case "broad_appeal": return "Broad appeal";
                case "niche_depth": return "Niche";
                case "replayable": return "Replayable";
                case "recommendation_fit": return "Recommendable";
                default: return TitleCaseEmotional(slug);
            }
        }

        /// <summary>Noun for "Formatting …" status line.</summary>
        public static string CorporateFormattingNoun(string slug)
        {
            if (string.IsNullOrWhiteSpace(slug)) return "Marketability";
            switch (slug.Trim().ToLowerInvariant())
            {
                case "marketable": return "Marketability";
                case "engaging": return "Engagement";
                case "retention_friendly": return "Retention";
                case "monetizable": return "Monetisation";
                case "sticky": return "Stickiness";
                case "discoverable": return "Discoverability";
                case "shareable": return "Shareability";
                case "trend_aligned": return "Trend Alignment";
                case "conversion_ready": return "Conversion";
                case "campaign_ready": return "Campaign Readiness";
                case "brand_safe": return "Brand Safety";
                case "premium_feel": return "Premium Positioning";
                default: return CorporateShortDisplay(slug);
            }
        }

        public static (string slug0, string slug1) PersonalSlugsForScoring(PromptDefinition prompt)
        {
            if (prompt == null) return ("personal", "nostalgic");
            var emotional = (prompt.EmotionalTags ?? Array.Empty<string>())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            string s0 = emotional.Count > 0 ? emotional[0].Trim().ToLowerInvariant() : "personal";
            string s1 = emotional.Count > 1
                ? emotional[1].Trim().ToLowerInvariant()
                : (emotional.Count > 0 && emotional[0] != "nostalgic" ? "nostalgic" : "intimate");
            return (s0, s1);
        }
    }
}

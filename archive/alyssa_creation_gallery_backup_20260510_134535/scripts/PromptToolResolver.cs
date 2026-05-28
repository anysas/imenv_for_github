using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AlgorithmicGallery.Corruption
{
    /// <summary>
    /// Converts parsed prompt intent into concrete prop seeds for the hotbar.
    /// The resolver prefers literal/object matches first, then emotional/group fallbacks.
    /// </summary>
    public static class PromptToolResolver
    {
        public class HotbarSeedPlan
        {
            public List<string> AnchorPropIds = new();
            public List<string> SupportPropIds = new();
            public List<string> AtmospherePropIds = new();

            public IEnumerable<string> Combined()
            {
                return AnchorPropIds
                    .Concat(SupportPropIds)
                    .Concat(AtmospherePropIds)
                    .Distinct();
            }
        }

        public static HotbarSeedPlan BuildSeedPlan(CuratedPropManifest manifest, PromptDefinition prompt)
        {
            var plan = new HotbarSeedPlan();
            if (manifest == null || prompt == null) return plan;

            var excluded = new HashSet<string>();

            // Anchor: literal object tokens
            var anchorCandidates = manifest.FindByNameTokens(prompt.IntentObjects, max: 12);
            AddTop(plan.AnchorPropIds, anchorCandidates, excluded, maxCount: 3);

            // Support: setting and action words
            var supportTokens = (prompt.IntentSetting ?? System.Array.Empty<string>())
                .Concat(prompt.IntentActions ?? System.Array.Empty<string>())
                .Distinct();
            var supportCandidates = manifest.FindByNameTokens(supportTokens, max: 14, excludeIds: excluded);
            AddTop(plan.SupportPropIds, supportCandidates, excluded, maxCount: 4);

            // Atmosphere: emotional/style fallback
            for (int i = 0; i < 6; i++)
            {
                var pick = manifest.GetWeightedByPromptIntent(prompt, prompt.IntentStyle, excludeIds: excluded);
                if (pick == null || string.IsNullOrEmpty(pick.Id)) continue;
                if (excluded.Add(pick.Id))
                    plan.AtmospherePropIds.Add(pick.Id);
                if (plan.AtmospherePropIds.Count >= 5) break;
            }

            return plan;
        }

        private static void AddTop(List<string> outIds, List<PropEntry> candidates, HashSet<string> excluded, int maxCount)
        {
            if (candidates == null || candidates.Count == 0) return;
            int count = Mathf.Min(maxCount, candidates.Count);
            for (int i = 0; i < count; i++)
            {
                var c = candidates[i];
                if (c == null || string.IsNullOrEmpty(c.Id)) continue;
                if (excluded.Add(c.Id))
                    outIds.Add(c.Id);
            }
        }
    }
}

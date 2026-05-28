using System.Collections.Generic;
using UnityEngine;

namespace AlgorithmicGallery.Corruption
{
    // Computes and applies a uniform scale factor so a loaded prop's longest axis
    // lands within the natural size range for its group.
    //
    // Design rules:
    //   - If the prop is already within the group's natural range → no rescale.
    //   - "oversized" props (doors, large machinery) are scaled DOWN to the range ceiling.
    //   - "tiny" / "small" props are scaled UP only if they'd otherwise be invisible.
    //   - Absolute clamp [MinScale, MaxScale] prevents absurd outliers.
    //   - If dimensions are unknown (longest axis ≈ 0) → return scale 1 (trust the artist).
    //
    // Usage:
    //   float scale = PropScaler.ComputeScaleFactor(prop);
    //   go.transform.localScale = Vector3.one * scale;
    //   // — or —
    //   PropScaler.Apply(go, prop);
    public static class PropScaler
    {
        // Per-group natural range for the longest axis (metres).
        // Limits chosen from Source Engine prop scale conventions + gallery aesthetics.
        private static readonly Dictionary<string, (float min, float max)> _ranges =
            new Dictionary<string, (float, float)>
        {
            { "item",      (0.08f, 0.65f) },   // mugs → small appliances
            { "furniture", (0.45f, 1.80f) },   // chairs → wardrobes
            { "lab",       (0.08f, 1.40f) },   // test tubes → lab benches
            { "office",    (0.08f, 1.20f) },   // pens → standing shelves
            { "workshop",  (0.18f, 1.80f) },   // small tools → large machinery
            { "domestic",  (0.08f, 1.20f) },   // trinkets → cabinets
            { "retail",    (0.18f, 1.80f) },   // products → display racks
            { "tech",      (0.04f, 0.45f) },   // chips → desktop hardware
        };

        private static readonly (float min, float max) _defaultRange = (0.08f, 1.50f);

        // Hard clamp on the resulting scale factor (prevents truly broken props).
        private const float MinScaleFactor = 0.02f;
        private const float MaxScaleFactor = 15.0f;
        private const float GlobalModelScaleMultiplier = 4.0f;

        // ────────────────────────────────────────────────────────────────────

        /// Returns the uniform scale factor that brings <paramref name="prop"/>
        /// into its group's natural range, or 1 if it's already within range.
        public static float ComputeScaleFactor(PropEntry prop)
        {
            if (prop == null) return 1f;

            // Manual override from CurationLab takes absolute priority.
            if (prop.ScaleOverride > 0.001f)
                return Mathf.Clamp(prop.ScaleOverride * GlobalModelScaleMultiplier, MinScaleFactor, MaxScaleFactor);

            float longest = prop.LongestAxis;
            if (longest < 0.001f)
                return GlobalModelScaleMultiplier;   // unknown dims — still apply global art direction boost

            var range = _ranges.TryGetValue(prop.Group ?? "", out var r) ? r : _defaultRange;

            if (longest >= range.min && longest <= range.max)
                return GlobalModelScaleMultiplier;  // already natural, then apply global art direction boost

            // Clamp to nearest bound: scale down if oversized, scale up if tiny
            float target = longest < range.min ? range.min : range.max;
            float factor = target / longest;
            return Mathf.Clamp(factor * GlobalModelScaleMultiplier, MinScaleFactor, MaxScaleFactor);
        }

        /// Applies ComputeScaleFactor to <paramref name="go"/> in local space.
        /// Safe to call after LoadModel() — does not touch child transforms.
        public static void Apply(GameObject go, PropEntry prop)
        {
            if (go == null || prop == null) return;
            float scale = ComputeScaleFactor(prop);
            if (Mathf.Approximately(scale, 1f)) return;
            go.transform.localScale = Vector3.one * scale;
        }

        /// Returns the final world-space longest axis after scaling.
        /// Useful for collision box sizing or UI tooltips.
        public static float ScaledLongestAxis(PropEntry prop)
        {
            if (prop == null) return 0f;
            float longest = prop.LongestAxis;
            if (longest < 0.001f) return 0f;
            return longest * ComputeScaleFactor(prop);
        }
    }
}

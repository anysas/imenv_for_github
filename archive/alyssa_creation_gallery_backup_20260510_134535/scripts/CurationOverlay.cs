using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace AlgorithmicGallery.Corruption
{
    // ─────────────────────────────────────────────────────────────────────────
    // Data models
    // ─────────────────────────────────────────────────────────────────────────

    [Serializable]
    public class OverlayEntry
    {
        [JsonProperty("group")]          public string Group { get; set; }
        [JsonProperty("emotional_tags")] public List<string> EmotionalTags { get; set; }
        [JsonProperty("scale_override")] public float ScaleOverride { get; set; } = 0f;
        [JsonProperty("custom_tags")]    public List<string> CustomTags { get; set; }
        [JsonProperty("notes")]          public string Notes { get; set; } = "";
        [JsonProperty("removed")]        public bool Removed { get; set; } = false;
    }

    [Serializable]
    public class CurationOverlayData
    {
        [JsonProperty("version")]       public int Version { get; set; } = 1;
        [JsonProperty("reviewed_ids")]  public HashSet<string> ReviewedIds  { get; set; } = new();
        [JsonProperty("removed_ids")]   public HashSet<string> RemovedIds   { get; set; } = new();
        [JsonProperty("overrides")]     public Dictionary<string, OverlayEntry> Overrides { get; set; } = new();
        [JsonProperty("custom_groups")] public List<string> CustomGroups    { get; set; } = new();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Static I/O
    // ─────────────────────────────────────────────────────────────────────────

    public static class CurationOverlay
    {
        private static string OverlayPath =>
            Path.Combine(Application.streamingAssetsPath, "curation_overrides.json");

        public static CurationOverlayData Load()
        {
            if (!File.Exists(OverlayPath))
                return new CurationOverlayData();
            try
            {
                var data = JsonConvert.DeserializeObject<CurationOverlayData>(
                    File.ReadAllText(OverlayPath));
                return data ?? new CurationOverlayData();
            }
            catch (Exception e)
            {
                Debug.LogError($"CurationOverlay: load failed — {e.Message}");
                return new CurationOverlayData();
            }
        }

        public static void Save(CurationOverlayData data)
        {
            try
            {
                string json = JsonConvert.SerializeObject(data, Formatting.Indented);
                File.WriteAllText(OverlayPath, json);
            }
            catch (Exception e)
            {
                Debug.LogError($"CurationOverlay: save failed — {e.Message}");
            }
        }

        // Mutates PropEntry objects in the manifest in-place.
        // Call once after LoadFromStreamingAssets() so the rest of the runtime sees
        // curated groups/tags/scales.
        public static void ApplyToManifest(CuratedPropManifest manifest,
                                           CurationOverlayData overlay)
        {
            if (manifest == null || overlay == null) return;
            foreach (var prop in manifest.All)
            {
                if (!overlay.Overrides.TryGetValue(prop.Id, out var entry)) continue;
                if (!string.IsNullOrEmpty(entry.Group))   prop.Group = entry.Group;
                if (entry.EmotionalTags != null)           prop.EmotionalTags = new List<string>(entry.EmotionalTags);
                if (entry.ScaleOverride > 0.001f)          prop.ScaleOverride = entry.ScaleOverride;
                if (entry.CustomTags != null)              prop.CustomTags = new List<string>(entry.CustomTags);
                if (!string.IsNullOrEmpty(entry.Notes))    prop.Notes = entry.Notes;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace AlgorithmicGallery.Corruption
{
    [Serializable]
    public class AutoCurationPresetThresholds
    {
        [JsonProperty("conservative")] public float Conservative = 1.45f;
        [JsonProperty("balanced")] public float Balanced = 1.10f;
        [JsonProperty("aggressive")] public float Aggressive = 0.85f;
    }

    [Serializable]
    public class AutoCurationRulesData
    {
        [JsonProperty("version")] public int Version = 1;
        [JsonProperty("character_keywords")] public List<string> CharacterKeywords = new();
        [JsonProperty("viewmodel_keywords")] public List<string> ViewmodelKeywords = new();
        [JsonProperty("large_structure_keywords")] public List<string> LargeStructureKeywords = new();
        [JsonProperty("thresholds")] public AutoCurationPresetThresholds Thresholds = new();
        [JsonProperty("large_axis_soft_m")] public float LargeAxisSoftM = 6.5f;
        [JsonProperty("large_axis_hard_m")] public float LargeAxisHardM = 10.0f;
        [JsonProperty("vertex_soft")] public int VertexSoft = 160000;
        [JsonProperty("vertex_hard")] public int VertexHard = 350000;
    }

    public enum AutoCurationPreset
    {
        Conservative = 0,
        Balanced = 1,
        Aggressive = 2,
    }

    public static class AutoCurationConfig
    {
        private static AutoCurationRulesData _cached;

        public static AutoCurationRulesData Current => _cached ??= Load();

        public static float ThresholdFor(AutoCurationPreset preset)
        {
            var t = Current.Thresholds ?? new AutoCurationPresetThresholds();
            return preset switch
            {
                AutoCurationPreset.Conservative => t.Conservative,
                AutoCurationPreset.Aggressive => t.Aggressive,
                _ => t.Balanced,
            };
        }

        private static AutoCurationRulesData Load()
        {
            string path = Path.Combine(Application.streamingAssetsPath, "auto-curation-rules-v1.json");
            if (!File.Exists(path))
            {
                Debug.LogWarning($"AutoCurationConfig: rules file missing at {path}; using defaults.");
                return BuildDefaults();
            }

            try
            {
                var data = JsonConvert.DeserializeObject<AutoCurationRulesData>(File.ReadAllText(path));
                return data ?? BuildDefaults();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"AutoCurationConfig: failed to parse rules file ({e.Message}); using defaults.");
                return BuildDefaults();
            }
        }

        private static AutoCurationRulesData BuildDefaults()
        {
            return new AutoCurationRulesData
            {
                CharacterKeywords = new List<string>
                {
                    "character","npc","player","human","humanoid","person","body","head","face","hair","soldier","zombie","girl","boy"
                },
                ViewmodelKeywords = new List<string>
                {
                    "viewmodel","fp_","fps_","weapon","gun","rifle","pistol","shotgun","arms","hands","firstperson","1p","sleeves"
                },
                LargeStructureKeywords = new List<string>
                {
                    "building","house","tower","skyscraper","structure","castle","bridge","warehouse","hangar","facility","stadium"
                },
            };
        }
    }
}

using System.Collections.Generic;
using Newtonsoft.Json;

namespace AlgorithmicGallery.Recommendation
{
    public class ModelEntry
    {
        [JsonProperty("id")]
        public string Id { get; set; } = "";

        [JsonProperty("glb_path")]
        public string GlbPath { get; set; } = "";

        [JsonProperty("game")]
        public string Game { get; set; } = "";

        [JsonProperty("object_name")]
        public string ObjectName { get; set; } = "";

        [JsonProperty("category")]
        public string Category { get; set; } = "";

        [JsonProperty("tags")]
        public ModelTags Tags { get; set; } = new();

        [JsonProperty("vertex_count")]
        public int VertexCount { get; set; }

        [JsonProperty("poly_count")]
        public int PolyCount { get; set; }

        [JsonProperty("dimensions")]
        public ModelDimensions Dimensions { get; set; } = new();

        [JsonIgnore]
        public List<string> FlatTags { get; set; } = new();
    }

    public class ModelTags
    {
        [JsonProperty("material_types")]
        public List<string> MaterialTypes { get; set; } = new();

        [JsonProperty("dominant_colors")]
        public List<string> DominantColors { get; set; } = new();

        [JsonProperty("color_mood")]
        public string ColorMood { get; set; } = "";

        [JsonProperty("scale")]
        public string Scale { get; set; } = "";

        [JsonProperty("complexity")]
        public string Complexity { get; set; } = "";

        [JsonProperty("silhouette")]
        public string Silhouette { get; set; } = "";

        [JsonProperty("has_animation")]
        public bool HasAnimation { get; set; }

        [JsonProperty("opacity")]
        public bool Opacity { get; set; }

        [JsonProperty("emissive")]
        public bool Emissive { get; set; }
    }

    public class ModelDimensions
    {
        [JsonProperty("x")]
        public float X { get; set; }

        [JsonProperty("y")]
        public float Y { get; set; }

        [JsonProperty("z")]
        public float Z { get; set; }
    }
}

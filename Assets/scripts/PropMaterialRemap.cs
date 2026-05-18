using UnityEngine;

namespace AlgorithmicGallery
{
    /// <summary>
    /// Remaps runtime glTF materials to URP Lit using a build-safe shader reference.
    /// </summary>
    public static class PropMaterialRemap
    {
        private const string FallbackResourcePath = "PropGltfFallback";
        private const string GhostResourcePath = "PropGhostFallback";

        private static Material s_fallbackTemplate;
        private static Material s_ghostTemplate;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void PreloadFallbackMaterials()
        {
            LoadFallbackTemplate();
        }

        public static Shader ResolveUrpLitShader(Material templateOverride)
        {
            Material template = templateOverride != null ? templateOverride : LoadFallbackTemplate();
            if (template != null && template.shader != null)
                return template.shader;

            return Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("Universal Render Pipeline/Simple Lit");
        }

        public static Material LoadFallbackTemplate()
        {
            if (s_fallbackTemplate == null)
            {
                s_fallbackTemplate = Resources.Load<Material>(FallbackResourcePath);
                if (s_fallbackTemplate == null)
                    Debug.LogError(
                        $"[PropMaterialRemap] Missing Resources/{FallbackResourcePath}.mat — glTF props will be pink in builds.");
            }

            return s_fallbackTemplate;
        }

        public static Shader ResolveUrpUnlitShader()
        {
            if (s_ghostTemplate == null)
                s_ghostTemplate = Resources.Load<Material>(GhostResourcePath);

            if (s_ghostTemplate != null && s_ghostTemplate.shader != null)
                return s_ghostTemplate.shader;

            return Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Unlit/Color");
        }

        public static bool NeedsRemap(Material material, Material template)
        {
            if (material == null)
                return true;

            if (template == null)
                return true;

            Shader shader = material.shader;
            if (shader == null)
                return true;

            // Always replace glTF-generated materials; only skip our own URP Lit clones.
            if (shader == template.shader && material != template)
                return true;

            if (!shader.isSupported)
                return true;

            string name = shader.name;
            if (name.Contains("Error") || name.Contains("InternalErrorShader"))
                return true;

            if (name.IndexOf("glTF", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            if (name.IndexOf("Shader Graphs", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            return shader != template.shader;
        }

        public static Material CreateUrpLitMaterial(Material source, Material template)
        {
            template ??= LoadFallbackTemplate();
            if (template == null || template.shader == null)
                return source;

            var dest = new Material(template);

            Texture baseMap = null;
            if (source != null)
            {
                if (source.HasProperty("baseColorTexture"))
                    baseMap = source.GetTexture("baseColorTexture");
                else if (source.HasProperty("_BaseMap"))
                    baseMap = source.GetTexture("_BaseMap");
                else if (source.HasProperty("_MainTex"))
                    baseMap = source.GetTexture("_MainTex");
            }

            Color baseColor = Color.white;
            if (source != null)
            {
                if (source.HasProperty("baseColorFactor"))
                    baseColor = source.GetColor("baseColorFactor");
                else if (source.HasProperty("_BaseColor"))
                    baseColor = source.GetColor("_BaseColor");
                else if (source.HasProperty("_Color"))
                    baseColor = source.GetColor("_Color");
            }

            if (baseMap != null)
            {
                if (dest.HasProperty("_BaseMap"))
                    dest.SetTexture("_BaseMap", baseMap);
                if (dest.HasProperty("_MainTex"))
                    dest.SetTexture("_MainTex", baseMap);
            }

            if (dest.HasProperty("_BaseColor"))
                dest.SetColor("_BaseColor", baseColor);
            if (dest.HasProperty("_Color"))
                dest.SetColor("_Color", baseColor);

            if (source != null)
            {
                if (source.HasProperty("metallicFactor") && dest.HasProperty("_Metallic"))
                    dest.SetFloat("_Metallic", source.GetFloat("metallicFactor"));
                else if (source.HasProperty("_Metallic") && dest.HasProperty("_Metallic"))
                    dest.SetFloat("_Metallic", source.GetFloat("_Metallic"));

                if (source.HasProperty("roughnessFactor") && dest.HasProperty("_Smoothness"))
                    dest.SetFloat("_Smoothness", 1f - source.GetFloat("roughnessFactor"));
                else if (source.HasProperty("_Smoothness") && dest.HasProperty("_Smoothness"))
                    dest.SetFloat("_Smoothness", source.GetFloat("_Smoothness"));
            }

            if (!string.IsNullOrEmpty(source?.name))
                dest.name = source.name;

            return dest;
        }
    }
}

using GLTFast;
using GLTFast.Logging;
using GLTFast.Materials;
using GLTFast.Schema;
using UnityEngine;
using UnityEngine.Rendering;
using Material = UnityEngine.Material;

namespace AlgorithmicGallery
{
    using AlphaMode = MaterialBase.AlphaMode;

    /// <summary>
    /// Creates glTF materials as URP Lit clones so player builds keep a valid shader + variants.
    /// </summary>
    public sealed class UrpLitGltfMaterialGenerator : MaterialGenerator
    {
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int MetallicId = Shader.PropertyToID("_Metallic");
        private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
        private static readonly int MetallicGlossMapId = Shader.PropertyToID("_MetallicGlossMap");
        private static readonly int CutoffId = Shader.PropertyToID("_Cutoff");
        private static readonly int SurfaceId = Shader.PropertyToID("_Surface");
        private static readonly int BlendId = Shader.PropertyToID("_Blend");
        private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
        private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
        private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");

        private readonly Material _template;

        public UrpLitGltfMaterialGenerator(Material template = null)
        {
            _template = template;
        }

        protected override Material GenerateDefaultMaterial(bool pointsSupport = false)
        {
            if (pointsSupport)
                Logger?.Warning(LogCode.TopologyPointsMaterialUnsupported);
            return CreateFromTemplate(DefaultMaterialName);
        }

        public override Material GenerateMaterial(
            MaterialBase gltfMaterial,
            IGltfReadable gltf,
            bool pointsSupport = false)
        {
            if (gltfMaterial == null)
                return GenerateDefaultMaterial(pointsSupport);

            if (pointsSupport)
                Logger?.Warning(LogCode.TopologyPointsMaterialUnsupported);

            Material material = CreateFromTemplate(
                string.IsNullOrWhiteSpace(gltfMaterial.name) ? "glTF-Material" : gltfMaterial.name);
            if (material == null)
                return null;

            ApplyAlphaMode(gltfMaterial, material);

            if (gltfMaterial.PbrMetallicRoughness != null
                && gltfMaterial.Extensions?.KHR_materials_pbrSpecularGlossiness == null)
            {
                PbrMetallicRoughnessBase pbr = gltfMaterial.PbrMetallicRoughness;
                material.SetColor(BaseColorId, pbr.BaseColor.gamma);
                material.SetFloat(MetallicId, pbr.metallicFactor);
                material.SetFloat(SmoothnessId, 1f - pbr.roughnessFactor);

                TrySetTexture(pbr.BaseColorTexture, material, gltf, BaseMapId);
                TrySetTexture(pbr.MetallicRoughnessTexture, material, gltf, MetallicGlossMapId);
            }

            return material;
        }

        private Material CreateFromTemplate(string materialName)
        {
            Material template = _template != null ? _template : PropMaterialRemap.LoadFallbackTemplate();
            if (template == null || template.shader == null)
            {
                Logger?.Error(LogCode.ShaderMissing, "URP Lit template (Resources/PropGltfFallback)");
                return null;
            }

            var material = new Material(template) { name = materialName };
            return material;
        }

        private static void ApplyAlphaMode(MaterialBase gltfMaterial, Material material)
        {
            switch (gltfMaterial.GetAlphaMode())
            {
                case AlphaMode.Mask:
                    material.SetFloat(CutoffId, gltfMaterial.alphaCutoff);
                    material.EnableKeyword("_ALPHATEST_ON");
                    material.SetOverrideTag("RenderType", "TransparentCutout");
                    break;
                case AlphaMode.Blend:
                    material.SetOverrideTag("RenderType", "Transparent");
                    if (material.HasProperty(SurfaceId))
                        material.SetFloat(SurfaceId, 1f);
                    if (material.HasProperty(BlendId))
                        material.SetFloat(BlendId, 0f);
                    if (material.HasProperty(SrcBlendId))
                        material.SetFloat(SrcBlendId, (int)BlendMode.SrcAlpha);
                    if (material.HasProperty(DstBlendId))
                        material.SetFloat(DstBlendId, (int)BlendMode.OneMinusSrcAlpha);
                    if (material.HasProperty(ZWriteId))
                        material.SetFloat(ZWriteId, 0f);
                    material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    material.renderQueue = (int)RenderQueue.Transparent;
                    break;
            }
        }
    }
}

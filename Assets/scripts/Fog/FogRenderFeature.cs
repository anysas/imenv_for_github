using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PSX
{
    public class FogRenderFeature : ScriptableRendererFeature
    {
        [SerializeField] private Shader effectShader;
        private FogPass _pass;

        public override void Create() => _pass = new FogPass(effectShader, RenderPassEvent.BeforeRenderingPostProcessing);

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (_pass == null || renderingData.cameraData.renderType == CameraRenderType.Overlay)
                return;

            renderer.EnqueuePass(_pass);
        }

        public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
        {
#pragma warning disable CS0618
            _pass?.SetSource(renderer.cameraColorTargetHandle);
#pragma warning restore CS0618
        }

        protected override void Dispose(bool disposing) => _pass?.Dispose();
    }

    public sealed class FogPass : PsxVolumeRenderPass<Fog>
    {
        private static readonly int FogDensity = Shader.PropertyToID("_FogDensity");
        private static readonly int FogDistance = Shader.PropertyToID("_FogDistance");
        private static readonly int FogColor = Shader.PropertyToID("_FogColor");
        private static readonly int AmbientColor = Shader.PropertyToID("_AmbientColor");
        private static readonly int FogNear = Shader.PropertyToID("_FogNear");
        private static readonly int FogFar = Shader.PropertyToID("_FogFar");
        private static readonly int FogAltScale = Shader.PropertyToID("_FogAltScale");
        private static readonly int FogThinning = Shader.PropertyToID("_FogThinning");
        private static readonly int NoiseScale = Shader.PropertyToID("_NoiseScale");
        private static readonly int NoiseStrength = Shader.PropertyToID("_NoiseStrength");

        public FogPass(Shader shader, RenderPassEvent evt)
            : base(shader, "PSX Fog", evt, requiresDepth: true) { }

        protected override void ApplyMaterialProperties(Fog fog)
        {
            Material.SetFloat(FogDensity, fog.fogDensity.value);
            Material.SetFloat(FogDistance, fog.fogDistance.value);
            Material.SetColor(FogColor, fog.fogColor.value);
            Material.SetColor(AmbientColor, fog.ambientColor.value);
            Material.SetFloat(FogNear, fog.fogNear.value);
            Material.SetFloat(FogFar, fog.fogFar.value);
            Material.SetFloat(FogAltScale, fog.fogAltScale.value);
            Material.SetFloat(FogThinning, fog.fogThinning.value);
            Material.SetFloat(NoiseScale, fog.noiseScale.value);
            Material.SetFloat(NoiseStrength, fog.noiseStrength.value);
        }
    }
}

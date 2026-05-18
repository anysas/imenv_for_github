using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PSX
{
    public class DitheringRenderFeature : ScriptableRendererFeature
    {
        [SerializeField] private Shader effectShader;
        private DitheringPass _pass;

        public override void Create() => _pass = new DitheringPass(effectShader, RenderPassEvent.BeforeRenderingPostProcessing);

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

    public sealed class DitheringPass : PsxVolumeRenderPass<Dithering>
    {
        private static readonly int PatternIndex = Shader.PropertyToID("_PatternIndex");
        private static readonly int DitherThreshold = Shader.PropertyToID("_DitherThreshold");
        private static readonly int DitherStrength = Shader.PropertyToID("_DitherStrength");
        private static readonly int DitherScale = Shader.PropertyToID("_DitherScale");

        public DitheringPass(Shader shader, RenderPassEvent evt)
            : base(shader, "PSX Dithering", evt) { }

        protected override void ApplyMaterialProperties(Dithering dithering)
        {
            Material.SetInt(PatternIndex, dithering.patternIndex.value);
            Material.SetFloat(DitherThreshold, dithering.ditherThreshold.value);
            Material.SetFloat(DitherStrength, dithering.ditherStrength.value);
            Material.SetFloat(DitherScale, dithering.ditherScale.value);
        }
    }
}

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PSX
{
    public class PixelationRenderFeature : ScriptableRendererFeature
    {
        [SerializeField] private Shader effectShader;
        private PixelationPass _pass;

        public override void Create() => _pass = new PixelationPass(effectShader, RenderPassEvent.BeforeRenderingPostProcessing);

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

    public sealed class PixelationPass : PsxVolumeRenderPass<Pixelation>
    {
        private static readonly int WidthPixelation = Shader.PropertyToID("_WidthPixelation");
        private static readonly int HeightPixelation = Shader.PropertyToID("_HeightPixelation");
        private static readonly int ColorPrecision = Shader.PropertyToID("_ColorPrecision");

        public PixelationPass(Shader shader, RenderPassEvent evt)
            : base(shader, "PSX Pixelation", evt) { }

        protected override void ApplyMaterialProperties(Pixelation pixelation)
        {
            Material.SetFloat(WidthPixelation, pixelation.widthPixelation.value);
            Material.SetFloat(HeightPixelation, pixelation.heightPixelation.value);
            Material.SetFloat(ColorPrecision, pixelation.colorPrecision.value);
        }
    }
}

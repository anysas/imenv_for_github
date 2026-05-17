using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace AlgorithmicGallery
{
    /// <summary>
    /// Full-screen glitch render pass: RGB channel split, scan lines, and noise interference.
    /// </summary>
    public class GlitchPass : ScriptableRenderPass
    {
        private static readonly int IntensityId = Shader.PropertyToID("_GlitchIntensity");
        private static readonly int RGBSplitId = Shader.PropertyToID("_RGBSplitAmount");
        private static readonly int ScanLineId = Shader.PropertyToID("_ScanLineIntensity");
        private static readonly int NoiseId = Shader.PropertyToID("_NoiseIntensity");
        private static readonly int TimeId = Shader.PropertyToID("_GlitchTime");

        private readonly Material _material;
        private readonly GlitchRendererFeature.GlitchSettings _settings;
        private float _intensity;

        public GlitchPass(Material material, GlitchRendererFeature.GlitchSettings settings)
        {
            _material = material;
            _settings = settings;
            _intensity = 0f;
        }

        public void SetIntensity(float intensity)
        {
            _intensity = Mathf.Clamp01(intensity);
        }

        [System.Obsolete]
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (_material == null || _intensity < 0.001f)
                return;

            CommandBuffer cmd = CommandBufferPool.Get("ScreenGlitch");

            _material.SetFloat(IntensityId, _intensity);
            _material.SetFloat(RGBSplitId, _settings.rgbSplitAmount * _intensity);
            _material.SetFloat(ScanLineId, _settings.scanLineIntensity * _intensity);
            _material.SetFloat(NoiseId, _settings.noiseIntensity * _intensity);
            _material.SetFloat(TimeId, Time.time);

            // Blit with glitch material
            var cameraColorTarget = renderingData.cameraData.renderer.cameraColorTargetHandle;
            Blitter.BlitCameraTexture(cmd, cameraColorTarget, cameraColorTarget, _material, 0);

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }
}

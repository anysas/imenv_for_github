// Compatibility Mode required: Edit → Project Settings → Graphics → URP Global Settings → RenderGraph disabled
// The RenderGraph API (Unity 6 default) changes too frequently across URP minor versions.
// Compatibility Mode is stable, fully supported, and appropriate for this project.
using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace AlgorithmicGallery.Corruption
{
    public class PSXPass : ScriptableRenderPass, IDisposable
    {
        private static readonly int _colorBitsId      = Shader.PropertyToID("_ColorBits");
        private static readonly int _ditherStrengthId = Shader.PropertyToID("_DitherStrength");
        private static readonly int _glitchId         = Shader.PropertyToID("_GlitchIntensity");
        private static readonly int _pixelScaleId     = Shader.PropertyToID("_PixelScale");
        private static readonly int _timeId           = Shader.PropertyToID("_PSXTime");

        private Material _material;
        private RTHandle _tempRT;
        private PSXRendererFeature.PSXSettings _settings;

        public PSXPass(PSXRendererFeature.PSXSettings settings)
        {
            _settings = settings;
            _material = CoreUtils.CreateEngineMaterial("Hidden/PSXPost");
        }

        public void UpdateSettings(PSXRendererFeature.PSXSettings settings) => _settings = settings;

#pragma warning disable CS0672  // overriding obsolete Execute/OnCameraSetup — intentional, requires Compatibility Mode
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;
#pragma warning disable CS0618  // ReAllocateHandleIfNeeded replaces the old ReAllocateIfNeeded
            RenderingUtils.ReAllocateHandleIfNeeded(ref _tempRT, desc, name: "_PSXTemp");
#pragma warning restore CS0618
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (_material == null) return;

            var cmd = CommandBufferPool.Get("PSXPost");

            _material.SetFloat(_colorBitsId,      _settings.colorBits);
            _material.SetFloat(_ditherStrengthId, _settings.ditherStrength);
            _material.SetFloat(_glitchId,         _settings.glitchIntensity);
            _material.SetFloat(_pixelScaleId,     _settings.pixelScale);
            _material.SetFloat(_timeId,           Time.time);

#pragma warning disable CS0618  // cameraColorTargetHandle is Compatibility Mode only — correct here
            var source = renderingData.cameraData.renderer.cameraColorTargetHandle;
#pragma warning restore CS0618

            Blitter.BlitCameraTexture(cmd, source, _tempRT, _material, 0);
            Blitter.BlitCameraTexture(cmd, _tempRT, source);

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
#pragma warning restore CS0672

        public override void OnCameraCleanup(CommandBuffer cmd) { }

        public void Dispose()
        {
            CoreUtils.Destroy(_material);
            _tempRT?.Release();
        }
    }
}

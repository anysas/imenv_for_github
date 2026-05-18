using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PSX
{
    public class CRTRenderFeature : ScriptableRendererFeature
    {
        private CRTPass _pass;

        public override void Create() => _pass = new CRTPass(RenderPassEvent.BeforeRenderingPostProcessing);

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

    public sealed class CRTPass : PsxVolumeRenderPass<Crt>
    {
        private static readonly int ScanLinesWeight = Shader.PropertyToID("_ScanlinesWeight");
        private static readonly int NoiseWeight = Shader.PropertyToID("_NoiseWeight");
        private static readonly int ScreenBendX = Shader.PropertyToID("_ScreenBendX");
        private static readonly int ScreenBendY = Shader.PropertyToID("_ScreenBendY");
        private static readonly int VignetteAmount = Shader.PropertyToID("_VignetteAmount");
        private static readonly int VignetteSize = Shader.PropertyToID("_VignetteSize");
        private static readonly int VignetteRounding = Shader.PropertyToID("_VignetteRounding");
        private static readonly int VignetteSmoothing = Shader.PropertyToID("_VignetteSmoothing");
        private static readonly int ScanLinesDensity = Shader.PropertyToID("_ScanLinesDensity");
        private static readonly int ScanLinesSpeed = Shader.PropertyToID("_ScanLinesSpeed");
        private static readonly int NoiseAmount = Shader.PropertyToID("_NoiseAmount");
        private static readonly int ChromaticRed = Shader.PropertyToID("_ChromaticRed");
        private static readonly int ChromaticGreen = Shader.PropertyToID("_ChromaticGreen");
        private static readonly int ChromaticBlue = Shader.PropertyToID("_ChromaticBlue");
        private static readonly int GrilleOpacity = Shader.PropertyToID("_GrilleOpacity");
        private static readonly int GrilleCounterOpacity = Shader.PropertyToID("_GrilleCounterOpacity");
        private static readonly int GrilleResolution = Shader.PropertyToID("_GrilleResolution");
        private static readonly int GrilleCounterResolution = Shader.PropertyToID("_GrilleCounterResolution");
        private static readonly int GrilleBrightness = Shader.PropertyToID("_GrilleBrightness");
        private static readonly int GrilleUvRotation = Shader.PropertyToID("_GrilleUvRotation");
        private static readonly int GrilleUvMidPoint = Shader.PropertyToID("_GrilleUvMidPoint");
        private static readonly int GrilleShift = Shader.PropertyToID("_GrilleShift");

        public CRTPass(RenderPassEvent evt)
            : base("PostEffect/CRTShader", "PSX CRT", evt) { }

        protected override void ApplyMaterialProperties(Crt crt)
        {
            Material.SetFloat(ScanLinesWeight, crt.scanlinesWeight.value);
            Material.SetFloat(NoiseWeight, crt.noiseWeight.value);
            Material.SetFloat(ScreenBendX, crt.screenBendX.value);
            Material.SetFloat(ScreenBendY, crt.screenBendY.value);
            Material.SetFloat(VignetteAmount, crt.vignetteAmount.value);
            Material.SetFloat(VignetteSize, crt.vignetteSize.value);
            Material.SetFloat(VignetteRounding, crt.vignetteRounding.value);
            Material.SetFloat(VignetteSmoothing, crt.vignetteSmoothing.value);
            Material.SetFloat(ScanLinesDensity, crt.scanlinesDensity.value);
            Material.SetFloat(ScanLinesSpeed, crt.scanlinesSpeed.value);
            Material.SetFloat(NoiseAmount, crt.noiseAmount.value);
            Material.SetVector(ChromaticRed, crt.chromaticRed.value);
            Material.SetVector(ChromaticGreen, crt.chromaticGreen.value);
            Material.SetVector(ChromaticBlue, crt.chromaticBlue.value);
            Material.SetFloat(GrilleOpacity, crt.grilleOpacity.value);
            Material.SetFloat(GrilleCounterOpacity, crt.grilleCounterOpacity.value);
            Material.SetFloat(GrilleResolution, crt.grilleResolution.value);
            Material.SetFloat(GrilleCounterResolution, crt.grilleCounterResolution.value);
            Material.SetFloat(GrilleBrightness, crt.grilleBrightness.value);
            Material.SetFloat(GrilleUvRotation, crt.grilleUvRotation.value);
            Material.SetFloat(GrilleUvMidPoint, crt.grilleUvMidPoint.value);
            Material.SetVector(GrilleShift, crt.grilleShift.value);
        }
    }
}

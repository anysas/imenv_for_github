using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PSX
{
    /// <summary>
    /// Fullscreen volume post pass using CommandBuffer.Blit (URP compatibility mode).
    /// </summary>
    public abstract class PsxVolumeRenderPass<T> : ScriptableRenderPass, IDisposable
        where T : VolumeComponent, IPostProcessComponent
    {
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int CameraDepthTextureId = Shader.PropertyToID("_CameraDepthTexture");

        private readonly string _passName;
        private readonly int _tempTargetId;
        private readonly bool _requiresDepth;

        protected Material Material { get; private set; }
        private RenderTargetIdentifier _source;

        protected PsxVolumeRenderPass(Shader shader, string passName, RenderPassEvent evt, bool requiresDepth = false)
        {
            _passName = passName;
            _tempTargetId = Shader.PropertyToID($"_TempTarget_{passName}");
            _requiresDepth = requiresDepth;
            renderPassEvent = evt;

            if (shader == null)
            {
                Debug.LogError($"[PSX] Missing shader reference for pass \"{passName}\". Assign it on the renderer feature asset.");
                return;
            }

            Material = CoreUtils.CreateEngineMaterial(shader);
        }

        public void SetSource(RTHandle source)
        {
            if (source != null)
                _source = source.nameID;
        }

#pragma warning disable CS0672
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (Material == null || _source == default)
                return;

            if (!renderingData.cameraData.postProcessEnabled)
                return;

            if (renderingData.cameraData.isPreviewCamera)
                return;

            if (renderingData.cameraData.renderType == CameraRenderType.Overlay)
                return;

            var component = VolumeManager.instance.stack.GetComponent<T>();
            if (component == null || !component.IsActive())
                return;

            ref var cameraData = ref renderingData.cameraData;
            cameraData.camera.depthTextureMode |= DepthTextureMode.Depth;

            ApplyMaterialProperties(component);

            var w = cameraData.camera.scaledPixelWidth;
            var h = cameraData.camera.scaledPixelHeight;
            if (w <= 0 || h <= 0)
                return;

            var cmd = CommandBufferPool.Get(_passName);
            BindDepthIfNeeded(cmd, ref renderingData);
            cmd.SetGlobalTexture(MainTexId, _source);
            cmd.GetTemporaryRT(_tempTargetId, w, h, 0, FilterMode.Point, RenderTextureFormat.Default);
            cmd.Blit(_source, _tempTargetId);
            cmd.Blit(_tempTargetId, _source, Material, 0);
            cmd.ReleaseTemporaryRT(_tempTargetId);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
#pragma warning restore CS0672

        protected abstract void ApplyMaterialProperties(T component);

        private void BindDepthIfNeeded(CommandBuffer cmd, ref RenderingData renderingData)
        {
            if (!_requiresDepth)
                return;

#pragma warning disable CS0618
            var depthTarget = renderingData.cameraData.renderer.cameraDepthTargetHandle;
#pragma warning restore CS0618
            if (depthTarget != null)
                cmd.SetGlobalTexture(CameraDepthTextureId, depthTarget.nameID);
        }

        public void Dispose()
        {
            CoreUtils.Destroy(Material);
        }
    }
}

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace AlgorithmicGallery
{
    /// <summary>
    /// URP ScriptableRendererFeature that injects a full-screen glitch pass.
    /// Triggered on phase transitions and during Unease phase.
    /// Register this on your URP Renderer asset (PC_Renderer.asset).
    /// </summary>
    public class GlitchRendererFeature : ScriptableRendererFeature
    {
        [System.Serializable]
        public class GlitchSettings
        {
            [Range(0, 1)] public float intensity = 0f;
            [Range(0, 0.1f)] public float rgbSplitAmount = 0.02f;
            [Range(0, 1)] public float scanLineIntensity = 0.3f;
            [Range(0, 1)] public float noiseIntensity = 0.15f;
            public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
        }

        public GlitchSettings settings = new GlitchSettings();
        [SerializeField] private Shader glitchShader;
        private GlitchPass _glitchPass;
        private Material _material;

        /// <summary>
        /// Static accessor so scripts can drive glitch intensity without a direct reference.
        /// </summary>
        public static float GlobalGlitchIntensity { get; set; } = 0f;

        public override void Create()
        {
            if (glitchShader == null)
            {
                Debug.LogWarning("GlitchRendererFeature: assign ScreenGlitch shader on the renderer feature. Glitch effect disabled.");
                return;
            }

            _material = CoreUtils.CreateEngineMaterial(glitchShader);
            _glitchPass = new GlitchPass(_material, settings);
            _glitchPass.renderPassEvent = settings.renderPassEvent;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (_glitchPass == null || _material == null)
                return;

            // Use the higher of local settings intensity and global static intensity
            float effectiveIntensity = Mathf.Max(settings.intensity, GlobalGlitchIntensity);
            if (effectiveIntensity < 0.001f)
                return;

            _glitchPass.SetIntensity(effectiveIntensity);
            renderer.EnqueuePass(_glitchPass);
        }

        protected override void Dispose(bool disposing)
        {
            if (_material != null)
            {
                CoreUtils.Destroy(_material);
                _material = null;
            }
        }
    }
}

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace AlgorithmicGallery.Corruption
{
    // URP Renderer Feature: PSX-style post-process.
    // Adds Bayer-ordered dithering, color bit quantization, and glitch distortion.
    // Add this to your URP Forward Renderer asset in the Renderer Features list.
    // Glitch intensity is driven at runtime by AssistantSystem via PSXRendererFeature.SetGlitchIntensity().
    public class PSXRendererFeature : ScriptableRendererFeature
    {
        [System.Serializable]
        public class PSXSettings
        {
            [Range(1, 8)]  public int colorBits = 5;
            [Range(0f, 1f)] public float ditherStrength = 0.6f;
            [Range(0f, 1f)] public float glitchIntensity = 0f;
            [Range(1f, 4f)] public float pixelScale = 1f; // integer pixel downscaling factor
        }

        public PSXSettings settings = new();

        private PSXPass _pass;
        private static PSXRendererFeature _instance;
        private float _baseGlitchIntensity;
        private float _glitchImpulse;
        private float _glitchImpulseDecayPerSecond = 2.4f;
        private int _lastRuntimeUpdateFrame = -1;

        public static void SetGlitchIntensity(float intensity)
        {
            // Backward-compatible alias for existing callers.
            SetBaseGlitchIntensity(intensity);
        }

        public static void SetBaseGlitchIntensity(float intensity)
        {
            if (_instance != null)
                _instance._baseGlitchIntensity = Mathf.Clamp01(intensity);
        }

        public static void AddGlitchImpulse(float amplitude, float decayPerSecond = 2.4f)
        {
            if (_instance == null)
                return;

            _instance._glitchImpulse = Mathf.Clamp01(_instance._glitchImpulse + Mathf.Max(0f, amplitude));
            _instance._glitchImpulseDecayPerSecond = Mathf.Max(0.01f, decayPerSecond);
        }

        public override void Create()
        {
            _pass = new PSXPass(settings);
            _pass.renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
            _instance = this;
            _baseGlitchIntensity = Mathf.Clamp01(settings.glitchIntensity);
            _glitchImpulse = 0f;
            _lastRuntimeUpdateFrame = -1;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            UpdateRuntimeStateOncePerFrame();
            _pass.UpdateSettings(settings);
            renderer.EnqueuePass(_pass);
        }

        protected override void Dispose(bool disposing)
        {
            _pass?.Dispose();
            if (_instance == this) _instance = null;
        }

        private void UpdateRuntimeStateOncePerFrame()
        {
            if (_lastRuntimeUpdateFrame == Time.frameCount)
                return;
            _lastRuntimeUpdateFrame = Time.frameCount;

            _glitchImpulse = Mathf.MoveTowards(_glitchImpulse, 0f, _glitchImpulseDecayPerSecond * Time.deltaTime);
            settings.glitchIntensity = Mathf.Clamp01(_baseGlitchIntensity + _glitchImpulse);
        }
    }
}

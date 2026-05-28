using UnityEngine;

namespace PSX
{
    /// <summary>
    /// Build-safe references for fullscreen PSX post shaders (loaded from Resources).
    /// </summary>
    [CreateAssetMenu(fileName = "PsxPostEffectShaders", menuName = "PSX/Post Effect Shaders")]
    public sealed class PsxPostEffectShaders : ScriptableObject
    {
        private const string ResourcePath = "PsxPostEffectShaders";

        private static PsxPostEffectShaders s_Instance;

        [SerializeField] private Shader dithering;
        [SerializeField] private Shader pixelation;
        [SerializeField] private Shader fog;
        [SerializeField] private Shader crt;
        [SerializeField] private Shader psxPost;

        public static PsxPostEffectShaders Instance
        {
            get
            {
                if (s_Instance == null)
                    s_Instance = Resources.Load<PsxPostEffectShaders>(ResourcePath);
                return s_Instance;
            }
        }

        public static Shader ResolveDithering(Shader assigned = null) =>
            Resolve(assigned, Instance?.dithering, "PostEffect/Dithering");

        public static Shader ResolvePixelation(Shader assigned = null) =>
            Resolve(assigned, Instance?.pixelation, "PostEffect/Pixelation");

        public static Shader ResolveFog(Shader assigned = null) =>
            Resolve(assigned, Instance?.fog, "PostEffect/Fog");

        public static Shader ResolveCrt(Shader assigned = null) =>
            Resolve(assigned, Instance?.crt, "PostEffect/CRTShader");

        public static Shader ResolvePsxPost(Shader assigned = null) =>
            Resolve(assigned, Instance?.psxPost, "Hidden/PSXPost");

        private static Shader Resolve(Shader assigned, Shader bundled, string shaderName)
        {
            if (assigned != null)
                return assigned;

            if (bundled != null)
                return bundled;

            var found = Shader.Find(shaderName);
            if (found != null)
                return found;

            Debug.LogError($"[PSX] Missing shader \"{shaderName}\". Add Resources/{ResourcePath}.asset or include the shader in the build.");
            return null;
        }
    }
}

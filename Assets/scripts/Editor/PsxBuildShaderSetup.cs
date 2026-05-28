#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PSX.Editor
{
    /// <summary>
    /// Keeps PSX fullscreen shaders in the build and aligns the default URP asset with the PSX pipeline.
    /// </summary>
    public sealed class PsxBuildShaderSetup : IPreprocessBuildWithReport
    {
        private static readonly string[] RequiredShaderPaths =
        {
            "Assets/Shaders/Dithering.shader",
            "Assets/Shaders/Pixelation.shader",
            "Assets/Shaders/Fog.shader",
            "Assets/Shaders/CRTShader.shader",
            "Assets/Shaders/PSXPost.shader",
            "Assets/Shaders/ScreenGlitch.shader",
            "Assets/Shaders/URP_PSX_PBR_Master.shadergraph",
            "Assets/Shaders/URP_PSX_Unlit_Master.shadergraph",
            "Assets/Shaders/URP_PSX_PBR_Master_with_transparency.shadergraph",
        };

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report) => RunSetup();

        [MenuItem("PSX/Fix Build Shader Inclusion")]
        public static void RunSetup()
        {
            EnsureAlwaysIncludedShaders();
            EnsureResourcesBundle();
            AlignDefaultRenderPipeline();
        }

        private static void EnsureAlwaysIncludedShaders()
        {
            var graphicsSettings = GraphicsSettings.GetGraphicsSettings();
            var so = new SerializedObject(graphicsSettings);
            var prop = so.FindProperty("m_AlwaysIncludedShaders");
            if (prop == null)
            {
                Debug.LogWarning("[PSX] Could not access Always Included Shaders; verify ProjectSettings/GraphicsSettings.asset.");
                return;
            }

            var existing = new System.Collections.Generic.HashSet<string>();
            for (int i = 0; i < prop.arraySize; i++)
            {
                var shader = prop.GetArrayElementAtIndex(i).objectReferenceValue as Shader;
                if (shader != null)
                    existing.Add(AssetDatabase.GetAssetPath(shader));
            }

            int added = 0;
            foreach (string path in RequiredShaderPaths)
            {
                if (existing.Contains(path))
                    continue;

                var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                if (shader == null)
                {
                    Debug.LogWarning($"[PSX] Shader asset missing at {path}");
                    continue;
                }

                prop.InsertArrayElementAtIndex(prop.arraySize);
                prop.GetArrayElementAtIndex(prop.arraySize - 1).objectReferenceValue = shader;
                existing.Add(path);
                added++;
            }

            if (added > 0)
            {
                so.ApplyModifiedPropertiesWithoutUndo();
                Debug.Log($"[PSX] Added {added} shader(s) to Always Included Shaders.");
            }
        }

        private static void EnsureResourcesBundle()
        {
            const string assetPath = "Assets/Resources/PsxPostEffectShaders.asset";
            if (AssetDatabase.LoadAssetAtPath<PsxPostEffectShaders>(assetPath) != null)
                return;

            var bundle = ScriptableObject.CreateInstance<PsxPostEffectShaders>();
            var so = new SerializedObject(bundle);
            so.FindProperty("dithering").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/Dithering.shader");
            so.FindProperty("pixelation").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/Pixelation.shader");
            so.FindProperty("fog").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/Fog.shader");
            so.FindProperty("crt").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/CRTShader.shader");
            so.FindProperty("psxPost").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/PSXPost.shader");
            so.ApplyModifiedPropertiesWithoutUndo();

            AssetDatabase.CreateAsset(bundle, assetPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[PSX] Created {assetPath}");
        }

        private static void AlignDefaultRenderPipeline()
        {
            var psxPipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(
                "Assets/URP-PSX-Render Pipeline Asset.asset");
            if (psxPipeline == null || GraphicsSettings.defaultRenderPipeline == psxPipeline)
                return;

            GraphicsSettings.defaultRenderPipeline = psxPipeline;
            Debug.Log("[PSX] Set default render pipeline to URP-PSX.");
        }
    }
}
#endif

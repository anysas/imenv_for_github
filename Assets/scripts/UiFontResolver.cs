using UnityEngine;
using TMPro;

namespace AlgorithmicGallery.Corruption
{
    public static class UiFontResolver
    {
        private static bool _loggedWallLabelFontSource;
        private static bool _loggedWallTmpFontSource;
        private static bool _loggedWallTmpFontMissingError;

        // Alias used by runtime UI builders that don't care about the specific font name.
        public static Font GetDefault() => LoadVt323OrFallback();

        /// <summary>
        /// TMP font asset for world-space score wall labels. Prefer project defaults after TMP Essentials import.
        /// </summary>
        public static TMP_FontAsset LoadWallTmpFontAsset(bool logResolvedSource = false)
        {
            TMP_FontAsset asset = null;
            string source = "(none)";

            if (TMP_Settings.instance != null && TMP_Settings.defaultFontAsset != null)
            {
                asset = TMP_Settings.defaultFontAsset;
                source = "TMP_Settings.defaultFontAsset";
            }

            if (asset == null)
            {
                asset = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
                if (asset != null)
                    source = "Resources/Fonts & Materials/LiberationSans SDF";
            }

            if (asset == null)
            {
                asset = Resources.Load<TMP_FontAsset>("LiberationSans SDF");
                if (asset != null)
                    source = "Resources/LiberationSans SDF";
            }

            if (logResolvedSource && !_loggedWallTmpFontSource)
            {
                _loggedWallTmpFontSource = true;
                string name = asset != null ? asset.name : "(null)";
                Debug.Log($"[UiFontResolver] Score wall TMP font asset: \"{name}\" via {source}. " +
                          "If null, run Window > TextMeshPro > Import TMP Essential Resources.");
            }

            if (asset == null && !_loggedWallTmpFontMissingError)
            {
                _loggedWallTmpFontMissingError = true;
                Debug.LogError(
                    "[UiFontResolver] Score wall TMP font asset is missing — TextMeshProUGUI labels will be invisible. " +
                    "Fix: (1) Window > TextMeshPro > Import TMP Essential Resources. " +
                    "(2) Project Settings > TextMesh Pro > Default Font Asset, or include " +
                    "\"Resources/Fonts & Materials/LiberationSans SDF\" in the player build.");
            }

            return asset;
        }

        /// <summary>
        /// Legacy UI font (hotbar/floaters). Score wall uses <see cref="LoadWallTmpFontAsset"/> instead.
        /// </summary>
        public static Font LoadWallLabelFont(bool logResolvedSource = false)
        {
            Font font = Resources.Load<Font>("VT323-Regular");
            string source = "Resources/VT323-Regular";

            if (font == null)
            {
#if UNITY_EDITOR
                font = UnityEditor.AssetDatabase.LoadAssetAtPath<Font>("Assets/VT323-Regular.ttf");
                if (font != null)
                    source = "Assets/VT323-Regular.ttf (Editor only)";
#endif
            }

            if (font == null)
            {
                font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                source = "LegacyRuntime.ttf";
            }

            if (font == null)
            {
                font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                source = "Arial.ttf";
            }

            if (logResolvedSource && !_loggedWallLabelFontSource)
            {
                _loggedWallLabelFontSource = true;
                string name = font != null ? font.name : "(null)";
                Debug.Log($"[UiFontResolver] Score wall label font resolved: \"{name}\" via {source}. " +
                          "For builds, place VT323 under Resources/VT323-Regular for parity with Editor.");
            }

            return font;
        }

        public static Font LoadVt323OrFallback()
        {
            // Preferred runtime path (if user moves font under Assets/Resources/).
            Font font = Resources.Load<Font>("VT323-Regular");
            if (font != null)
                return font;

#if UNITY_EDITOR
            // Editor fallback for current project layout.
            font = UnityEditor.AssetDatabase.LoadAssetAtPath<Font>("Assets/VT323-Regular.ttf");
            if (font != null)
                return font;
#endif

            // Safety fallback so runtime UI never breaks.
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null)
                return font;

            return Resources.GetBuiltinResource<Font>("Arial.ttf");
        }
    }
}

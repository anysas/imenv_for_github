using UnityEngine;

namespace AlgorithmicGallery.Corruption
{
    public static class UiFontResolver
    {
        // Alias used by runtime UI builders that don't care about the specific font name.
        public static Font GetDefault() => LoadVt323OrFallback();

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

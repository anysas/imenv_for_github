using System.IO;
using UnityEngine;

namespace AlgorithmicGallery.Corruption
{
    /// <summary>
    /// Resolves prop model paths relative to <see cref="Application.streamingAssetsPath"/>.
    /// Supports legacy curated layout (models/…) and direct folders like RandomObjects/….
    /// </summary>
    public static class StreamingAssetModelPaths
    {
        public static bool Exists(string relativePath) => !string.IsNullOrEmpty(ResolveFullPath(relativePath));

        public static string ResolveFullPath(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                return null;

            string normalized = relativePath.Trim().Replace('\\', '/').TrimStart('/');
            string root = Application.streamingAssetsPath;

            string[] candidates =
            {
                Path.Combine(root, normalized),
                Path.Combine(root, "models", normalized),
            };

            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate))
                    return candidate;
            }

            return null;
        }

        /// <summary>Path relative to StreamingAssets root, forward slashes.</summary>
        public static string ToStreamingRelativePath(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
                return null;

            string root = Application.streamingAssetsPath;
            if (!fullPath.StartsWith(root, System.StringComparison.OrdinalIgnoreCase))
                return Path.GetFileName(fullPath)?.Replace('\\', '/');

            string rel = fullPath.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return rel.Replace('\\', '/');
        }
    }
}

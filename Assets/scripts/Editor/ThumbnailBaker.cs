#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using AlgorithmicGallery.Corruption;
using AlgorithmicGallery;

namespace AlgorithmicGallery.CorruptionEditor
{
    // Editor menu: Tools / Algorithmic Gallery / Bake Hotbar Thumbnails
    //
    // Loads each prop in curated-props.json, renders a thumbnail, writes PNG to
    // Assets/Resources/PropThumbnails/<id>.png. Run once before shipping; the
    // runtime can then load these via Resources.Load<Sprite> instead of
    // capturing live (faster startup, no GLB hit).
    //
    // NOTE: This iterates the entire curated set serially — expect several minutes.
    public static class ThumbnailBaker
    {
        [MenuItem("Tools/Algorithmic Gallery/Bake Hotbar Thumbnails")]
        public static void Bake()
        {
            if (!EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog(
                    "Enter Play mode first",
                    "Thumbnail baking uses runtime GLB loading. Enter Play mode in an empty scene, then run this menu item again.",
                    "OK");
                return;
            }

            _ = BakeAsync();
        }

        private static async Task BakeAsync()
        {
            var manifest = CuratedPropManifest.LoadFromStreamingAssets();
            if (manifest == null) { Debug.LogError("[ThumbnailBaker] No manifest."); return; }

            var capture = Object.FindFirstObjectByType<RuntimeThumbnailCapture>();
            if (capture == null)
            {
                var go = new GameObject("_ThumbnailBakerRuntime");
                capture = go.AddComponent<RuntimeThumbnailCapture>();
            }

            string outDir = Path.Combine(Application.dataPath, "Resources", "PropThumbnails");
            Directory.CreateDirectory(outDir);

            int total = manifest.All.Count;
            int done = 0;
            int written = 0;

            foreach (var prop in manifest.All)
            {
                bool gotResult = false;
                Sprite produced = null;
                capture.RequestThumbnail(prop, sprite =>
                {
                    produced = sprite;
                    gotResult = true;
                });

                while (!gotResult)
                {
                    await Task.Yield();
                    if (!EditorApplication.isPlaying) { Debug.Log("[ThumbnailBaker] Play mode exited; aborting."); return; }
                }

                if (produced != null && produced.texture != null)
                {
                    byte[] png = produced.texture.EncodeToPNG();
                    File.WriteAllBytes(Path.Combine(outDir, prop.Id + ".png"), png);
                    written++;
                }

                done++;
                if (done % 25 == 0)
                {
                    EditorUtility.DisplayProgressBar("Baking thumbnails", $"{done} / {total}", (float)done / total);
                    Debug.Log($"[ThumbnailBaker] {done}/{total} ({written} written)");
                }
            }

            EditorUtility.ClearProgressBar();
            AssetDatabase.Refresh();
            Debug.Log($"[ThumbnailBaker] DONE — {written} thumbnails written to Assets/Resources/PropThumbnails/");
        }
    }
}
#endif

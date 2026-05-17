#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using AlgorithmicGallery.Corruption;

namespace AlgorithmicGallery.CorruptionEditor
{
    public static class AutoCurationTool
    {
        [MenuItem("Tools/Algorithmic Gallery/Auto Curation/Dry Run/Balanced")]
        public static void DryRunBalanced() => Run(AutoCurationPreset.Balanced, apply: false);

        [MenuItem("Tools/Algorithmic Gallery/Auto Curation/Dry Run/Conservative")]
        public static void DryRunConservative() => Run(AutoCurationPreset.Conservative, apply: false);

        [MenuItem("Tools/Algorithmic Gallery/Auto Curation/Dry Run/Aggressive")]
        public static void DryRunAggressive() => Run(AutoCurationPreset.Aggressive, apply: false);

        [MenuItem("Tools/Algorithmic Gallery/Auto Curation/Apply/Balanced")]
        public static void ApplyBalanced() => Run(AutoCurationPreset.Balanced, apply: true);

        [MenuItem("Tools/Algorithmic Gallery/Auto Curation/Apply/Conservative")]
        public static void ApplyConservative() => Run(AutoCurationPreset.Conservative, apply: true);

        [MenuItem("Tools/Algorithmic Gallery/Auto Curation/Apply/Aggressive")]
        public static void ApplyAggressive() => Run(AutoCurationPreset.Aggressive, apply: true);

        private static void Run(AutoCurationPreset preset, bool apply)
        {
            var manifest = CuratedPropManifest.LoadFromStreamingAssets();
            if (manifest == null)
            {
                Debug.LogError("[AutoCuration] Could not load curated-props manifest.");
                return;
            }

            var overlay = CurationOverlay.Load();
            var result = AutoCurationClassifier.Classify(manifest.All, preset);
            var removeIds = result.Decisions
                .Where(d => d.ShouldRemove && !string.IsNullOrWhiteSpace(d.Id))
                .Select(d => d.Id)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();

            string reportDir = EnsureReportDirectory();
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string mode = apply ? "apply" : "dryrun";
            string root = Path.Combine(reportDir, $"auto-curation_{preset.ToString().ToLowerInvariant()}_{mode}_{stamp}");

            WriteJsonReport(root + ".json", result);
            WriteCsvReport(root + ".csv", result);

            if (apply)
            {
                int beforeRemoved = overlay.RemovedIds.Count;
                overlay.RemovedIds.UnionWith(removeIds);
                foreach (var id in removeIds)
                {
                    if (!overlay.Overrides.TryGetValue(id, out var entry))
                        overlay.Overrides[id] = entry = new OverlayEntry();
                    entry.Removed = true;
                }

                manifest.SaveToStreamingAssets(overlay.RemovedIds);
                CurationOverlay.Save(overlay);
                AssetDatabase.Refresh();

                Debug.Log(
                    $"[AutoCuration] APPLY complete. preset={preset} scanned={result.TotalScanned} " +
                    $"proposed={result.ProposedRemovals} removed_ids_before={beforeRemoved} removed_ids_after={overlay.RemovedIds.Count} " +
                    $"reports={root}.json/.csv");

                RunPostApplyValidation(removeIds);
            }
            else
            {
                var topReasons = result.Decisions
                    .Where(d => d.ShouldRemove)
                    .SelectMany(d => d.Reasons)
                    .GroupBy(r => r.Code)
                    .OrderByDescending(g => g.Count())
                    .Take(8)
                    .Select(g => $"{g.Key}:{g.Count()}");

                Debug.Log(
                    $"[AutoCuration] DRY RUN complete. preset={preset} scanned={result.TotalScanned} " +
                    $"proposed={result.ProposedRemovals} high={result.HighConfidenceRemovals} " +
                    $"med={result.MediumConfidenceRemovals} low={result.LowConfidenceRemovals} " +
                    $"top_reasons=[{string.Join(", ", topReasons)}] reports={root}.json/.csv");
            }
        }

        private static void RunPostApplyValidation(List<string> justRemovedIds)
        {
            var reloadedManifest = CuratedPropManifest.LoadFromStreamingAssets();
            var reloadedOverlay = CurationOverlay.Load();
            if (reloadedManifest == null || reloadedOverlay == null)
            {
                Debug.LogWarning("[AutoCuration] Validation skipped: manifest/overlay reload failed.");
                return;
            }

            var manifestIds = new HashSet<string>(
                reloadedManifest.All.Select(p => p.Id).Where(id => !string.IsNullOrWhiteSpace(id)),
                StringComparer.Ordinal);

            int leaked = justRemovedIds.Count(id => manifestIds.Contains(id));
            int activeCount = reloadedManifest.All.Count;
            int overlayRemoved = reloadedOverlay.RemovedIds.Count;

            Debug.Log(
                $"[AutoCuration] VALIDATION active_manifest_count={activeCount} overlay_removed_count={overlayRemoved} leaked_removed_ids={leaked}");
        }

        private static string EnsureReportDirectory()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string dir = Path.Combine(projectRoot, "curation-reports");
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static void WriteJsonReport(string path, AutoCurationRunResult result)
        {
            string json = JsonConvert.SerializeObject(result, Formatting.Indented);
            File.WriteAllText(path, json);
        }

        private static void WriteCsvReport(string path, AutoCurationRunResult result)
        {
            using var writer = new StreamWriter(path);
            writer.WriteLine("id,display_name,category,glb_path,score,threshold,should_remove,confidence_band,reasons");
            foreach (var d in result.Decisions)
            {
                string reasons = string.Join(" | ", d.Reasons.Select(r => $"{r.Code}:{r.Detail}:{r.Weight:F2}"));
                writer.WriteLine(string.Join(",",
                    Csv(d.Id),
                    Csv(d.DisplayName),
                    Csv(d.Category),
                    Csv(d.GlbPath),
                    d.Score.ToString("F3"),
                    d.Threshold.ToString("F3"),
                    d.ShouldRemove ? "1" : "0",
                    Csv(d.ConfidenceBand),
                    Csv(reasons)));
            }
        }

        private static string Csv(string value)
        {
            string v = value ?? string.Empty;
            if (v.Contains(",") || v.Contains("\"") || v.Contains("\n"))
                return $"\"{v.Replace("\"", "\"\"")}\"";
            return v;
        }
    }
}
#endif

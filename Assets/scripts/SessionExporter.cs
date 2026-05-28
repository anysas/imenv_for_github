using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace AlgorithmicGallery.Corruption
{
    // Writes the session's StyleProfile + assistant timeline to disk for playtest analysis.
    // Auto-subscribes to SandboxManager.OnSessionComplete.
    //
    // Output path: Application.persistentDataPath/sessions/session_<unix>.json
    public class SessionExporter : MonoBehaviour
    {
        [SerializeField] private SandboxManager _sandbox;
        [SerializeField] private bool _alsoWriteToDesktop = false;

        void Start()
        {
            if (_sandbox == null) _sandbox = FindFirstObjectByType<SandboxManager>();
            if (_sandbox != null)
                _sandbox.OnSessionComplete.AddListener(Export);
        }

        public void Export()
        {
            if (_sandbox?.StyleProfile == null)
            {
                GameplayEventDebugLog.Push("Export", "skipped (no StyleProfile)");
                return;
            }

            GameplayEventDebugLog.Push("Export", "session export started");

            var sp = _sandbox.StyleProfile;
            var assistant = _sandbox.Assistant;
            var prompt = _sandbox.SelectedPrompt;

            string timestamp = DateTime.UtcNow.ToString("o");
            long unixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string sessionId = $"session_{unixTime}";
            string filename = $"{sessionId}.json";

            var dominantEmotionalTags = sp.DominantEmotionalTags(3);

            Vector3 sandboxOrigin = _sandbox.SandboxFloor != null
                ? _sandbox.SandboxFloor.position
                : Vector3.zero;
            Quaternion sandboxRotation = _sandbox.SandboxFloor != null
                ? _sandbox.SandboxFloor.rotation
                : Quaternion.identity;
            Vector3 sandboxScale = _sandbox.SandboxFloor != null
                ? _sandbox.SandboxFloor.lossyScale
                : Vector3.one;

            var payload = new SessionRecord
            {
                SchemaVersion = SessionRecord.CurrentSchemaVersion,
                Id = sessionId,
                Timestamp = timestamp,
                SandboxOrigin = new[] { sandboxOrigin.x, sandboxOrigin.y, sandboxOrigin.z },
                SandboxRotation = new[] { sandboxRotation.x, sandboxRotation.y, sandboxRotation.z, sandboxRotation.w },
                SandboxScale = new[] { sandboxScale.x, sandboxScale.y, sandboxScale.z },
                PromptText = prompt?.DisplayText ?? "",
                PromptEmotionalTags = prompt?.EmotionalTags ?? new string[0],
                PromptIntentObjects = prompt?.IntentObjects ?? new string[0],
                PromptIntentSetting = prompt?.IntentSetting ?? new string[0],
                PromptIntentActions = prompt?.IntentActions ?? new string[0],
                PromptCollapsedTerms = prompt?.CollapsedTerms ?? new string[0],
                PromptDroppedTerms = prompt?.DroppedTerms ?? new string[0],
                PromptCollapseSeverity = prompt != null ? prompt.CollapseSeverity : 0f,
                PromptParseConfidence = prompt != null ? prompt.ParseConfidence : 0f,
                SessionDuration = assistant != null ? assistant.SessionTime : 0f,
                FinalInfluence = assistant != null ? assistant.Influence : 0f,
                FinalPhase = assistant != null ? assistant.Phase.ToString() : "Unknown",
                TotalPlacements = sp.PlacementCount,
                PlayerPlacements = sp.PlayerPlacementCount,
                AssistantPlacements = sp.AssistantPlacementCount,
                AverageCadenceSeconds = sp.AverageCadenceSeconds(),
                DominantGroups = sp.DominantGroups(5),
                DominantTags = dominantEmotionalTags,
                GroupCounts = sp.GroupCounts.ToDictionary(kv => kv.Key, kv => kv.Value),
                TagCounts = sp.TagCounts.ToDictionary(kv => kv.Key, kv => kv.Value),
                Placements = sp.History.Select(r => new PlacementSnapshot
                {
                    Position = new[] { r.Position.x, r.Position.y, r.Position.z },
                    Rotation = new[] { r.Rotation.x, r.Rotation.y, r.Rotation.z, r.Rotation.w },
                    Scale = new[] { r.LocalScale.x, r.LocalScale.y, r.LocalScale.z },
                    Group = r.Group,
                    GlbPath = r.GlbPath,
                    Tags = r.Tags,
                    EmotionalTags = r.EmotionalTags,
                    Timestamp = r.Timestamp,
                    IsPlayer = r.IsPlayer,
                }).ToList(),
            };

            string json = JsonConvert.SerializeObject(payload, Formatting.Indented);

            string sessionsDir = Path.Combine(Application.persistentDataPath, "sessions");
            Directory.CreateDirectory(sessionsDir);
            string fullPath = Path.Combine(sessionsDir, filename);
            File.WriteAllText(fullPath, json);
            Debug.Log($"[SessionExporter] Wrote {fullPath}");
            GameplayEventDebugLog.Push("Export", $"wrote {filename} ({payload.TotalPlacements} placements, schema v{payload.SchemaVersion}, origin={sandboxOrigin})");

            // Append to sessions/index.json so the hallway can find recent sessions
            AppendToIndex(sessionsDir, new IndexEntry
            {
                Id = sessionId,
                Timestamp = timestamp,
                Prompt = prompt?.DisplayText ?? "",
                PlayerPlacements = sp.PlayerPlacementCount,
                AssistantPlacements = sp.AssistantPlacementCount,
                FinalPhase = payload.FinalPhase,
                DominantEmotionalTags = dominantEmotionalTags,
                PromptCollapseSeverity = payload.PromptCollapseSeverity,
            });

            if (_alsoWriteToDesktop)
            {
                try
                {
                    string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                    File.WriteAllText(Path.Combine(desktop, filename), json);
                    Debug.Log($"[SessionExporter] Mirrored to Desktop/{filename}");
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[SessionExporter] Desktop mirror failed: {e.Message}");
                }
            }
        }

        private static void AppendToIndex(string sessionsDir, IndexEntry entry)
        {
            string indexPath = Path.Combine(sessionsDir, "index.json");
            var entries = new List<IndexEntry>();

            if (File.Exists(indexPath))
            {
                try
                {
                    string existing = File.ReadAllText(indexPath);
                    entries = JsonConvert.DeserializeObject<List<IndexEntry>>(existing) ?? new List<IndexEntry>();
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[SessionExporter] Couldn't read index.json, starting fresh: {e.Message}");
                }
            }

            entries.Add(entry);

            // Keep only the 50 most recent sessions so the file stays manageable
            if (entries.Count > 50)
                entries = entries.GetRange(entries.Count - 50, 50);

            try
            {
                File.WriteAllText(indexPath, JsonConvert.SerializeObject(entries, Formatting.Indented));
                Debug.Log($"[SessionExporter] Updated index.json ({entries.Count} entries)");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SessionExporter] Failed to write index.json: {e.Message}");
            }
        }

        [Serializable]
        private class SessionRecord
        {
            public const int CurrentSchemaVersion = 3;

            /// <summary>2+ includes SandboxOrigin and per-placement Rotation/Scale for exact replay; 3+ adds sandbox rotation/scale.</summary>
            public int SchemaVersion;
            public string Id;
            public string Timestamp;
            public float[] SandboxOrigin;
            public float[] SandboxRotation;
            public float[] SandboxScale;
            public string PromptText;
            public string[] PromptEmotionalTags;
            public string[] PromptIntentObjects;
            public string[] PromptIntentSetting;
            public string[] PromptIntentActions;
            public string[] PromptCollapsedTerms;
            public string[] PromptDroppedTerms;
            public float PromptCollapseSeverity;
            public float PromptParseConfidence;
            public float SessionDuration;
            public float FinalInfluence;
            public string FinalPhase;
            public int TotalPlacements;
            public int PlayerPlacements;
            public int AssistantPlacements;
            public float AverageCadenceSeconds;
            public List<string> DominantGroups;
            public List<string> DominantTags;
            public Dictionary<string, int> GroupCounts;
            public Dictionary<string, int> TagCounts;
            public List<PlacementSnapshot> Placements;
        }

        [Serializable]
        private class PlacementSnapshot
        {
            public float[] Position;
            public float[] Rotation;
            public float[] Scale;
            public string Group;
            public string GlbPath;
            public List<string> Tags;
            public List<string> EmotionalTags;
            public float Timestamp;
            public bool IsPlayer;
        }

        /// <summary>One entry in sessions/index.json — lightweight enough to scan without loading full sessions.</summary>
        [Serializable]
        public class IndexEntry
        {
            public string Id;
            public string Timestamp;
            public string Prompt;
            public int PlayerPlacements;
            public int AssistantPlacements;
            public string FinalPhase;
            public List<string> DominantEmotionalTags;
            public float PromptCollapseSeverity;
        }
    }
}

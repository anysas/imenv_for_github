using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AlgorithmicGallery.Corruption
{
    public enum CurationQueue
    {
        All        = 0,
        Oversized  = 1,
        LowConf    = 2,
        Unreviewed = 3,
        ByGroup    = 4,   // legacy: filter by single Group field
        Removed    = 5,
        ByTag      = 6,   // tag-first filter across unified tag set (group + personal + corporate + custom)
        Untagged   = 7,   // props with no tag overlap of any kind (cleanup bucket)
    }

    // Central state manager for the CurationLab scene.
    // Owns the manifest, overlay, and the active prop queue.
    // Does not depend on any scene geometry — pure data/logic.
    public class CurationManager : MonoBehaviour
    {
        [Header("Queue thresholds")]
        [SerializeField] private float _oversizedThreshold = 3.0f;   // longest axis > this (m)
        [SerializeField] private float _lowConfThreshold   = 0.50f;

        // ── Public state ──────────────────────────────────────────────────────
        public CuratedPropManifest Manifest { get; private set; }
        public CurationOverlayData Overlay  { get; private set; }

        public CurationQueue ActiveQueue { get; private set; } = CurationQueue.Oversized;
        public string        FilterGroup { get; private set; } = "";
        public string        FilterTag   { get; private set; } = "";

        public int  QueueCount    => _queue.Count;
        public int  CurrentIndex  => _cursor;
        public bool QueueEmpty    => _queue.Count == 0;

        public PropEntry CurrentProp =>
            _queue.Count > 0 && _cursor < _queue.Count ? _queue[_cursor] : null;

        // ── Events ────────────────────────────────────────────────────────────
        public event Action<PropEntry>  OnPropChanged;
        public event Action             OnOverlaySaved;
        public event Action             OnQueueRebuilt;

        // ── Internals ─────────────────────────────────────────────────────────
        private List<PropEntry> _queue  = new();
        private int             _cursor = 0;

        // ── Lifecycle ─────────────────────────────────────────────────────────
        void Awake()
        {
            Manifest = CuratedPropManifest.LoadFromStreamingAssets();
            Overlay  = CurationOverlay.Load();
            TagTaxonomy.EnsureLoaded();

            if (Manifest != null)
                CurationOverlay.ApplyToManifest(Manifest, Overlay);

            RebuildQueue();
        }

        // ── Navigation ────────────────────────────────────────────────────────
        public void Next()
        {
            if (_queue.Count == 0) return;
            _cursor = (_cursor + 1) % _queue.Count;
            OnPropChanged?.Invoke(CurrentProp);
        }

        public void Prev()
        {
            if (_queue.Count == 0) return;
            _cursor = (_cursor - 1 + _queue.Count) % _queue.Count;
            OnPropChanged?.Invoke(CurrentProp);
        }

        public void JumpTo(int index)
        {
            if (_queue.Count == 0) return;
            _cursor = Mathf.Clamp(index, 0, _queue.Count - 1);
            OnPropChanged?.Invoke(CurrentProp);
        }

        // ── Queue switching ───────────────────────────────────────────────────
        // For ByGroup: filter is the legacy single-group string (`PropEntry.Group`).
        // For ByTag:   filter is any tag value drawn from the unified tag space (taxonomy + group + custom).
        // For all other queues: filter is ignored and both filter strings are cleared.
        public void SetQueue(CurationQueue queue, string filter = "")
        {
            ActiveQueue = queue;
            string normalized = (filter ?? "").Trim().ToLowerInvariant();
            switch (queue)
            {
                case CurationQueue.ByGroup:
                    FilterGroup = normalized;
                    FilterTag = "";
                    break;
                case CurationQueue.ByTag:
                    FilterTag = normalized;
                    FilterGroup = "";
                    break;
                default:
                    FilterGroup = "";
                    FilterTag = "";
                    break;
            }
            RebuildQueue();
            _cursor = 0;
            OnQueueRebuilt?.Invoke();
            OnPropChanged?.Invoke(CurrentProp);
        }

        // ── Actions ───────────────────────────────────────────────────────────

        // Mark as reviewed without changes and advance.
        public void KeepAndAdvance()
        {
            var prop = CurrentProp;
            if (prop == null) return;
            Overlay.ReviewedIds.Add(prop.Id);
            RemoveCurrentAndAdvance();
            SaveOverlay();
        }

        // Remove from manifest and advance.
        public void DeleteAndAdvance()
        {
            var prop = CurrentProp;
            if (prop == null) return;
            Overlay.RemovedIds.Add(prop.Id);
            Overlay.ReviewedIds.Add(prop.Id);
            GetOrCreateEntry(prop.Id).Removed = true;
            RemoveCurrentAndAdvance();
            SaveOverlay();
        }

        // Save current overrides and advance.
        public void SaveAndAdvance()
        {
            var prop = CurrentProp;
            if (prop == null) return;
            Overlay.ReviewedIds.Add(prop.Id);
            RemoveCurrentAndAdvance();
            SaveOverlay();
        }

        // Restore a removed prop (used from the Removed queue view).
        public void RestoreProp(string id)
        {
            Overlay.RemovedIds.Remove(id);
            var entry = GetOrCreateEntry(id);
            entry.Removed = false;
            RebuildQueue();
            OnQueueRebuilt?.Invoke();
            OnPropChanged?.Invoke(CurrentProp);
            SaveOverlay();
        }

        // Apply edits to the current prop's overlay entry. Pass null to leave a field unchanged.
        // scaleOverride < 0 means "leave unchanged"; 0 means "clear override / use auto".
        public void ApplyOverride(
            string       group         = null,
            List<string> personalTags  = null,
            List<string> corporateTags = null,
            float        scaleOverride = -1f,
            List<string> customTags    = null,
            string       notes         = null)
        {
            var prop = CurrentProp;
            if (prop == null) return;

            var entry = GetOrCreateEntry(prop.Id);

            if (group != null)
            {
                entry.Group = group;
                prop.Group  = group;
            }
            if (personalTags != null)
            {
                var normalizedPersonal = TagTaxonomy.NormalizePersonal(personalTags, out var droppedPersonal);
                entry.EmotionalTags = new List<string>(normalizedPersonal);
                entry.PersonalTags = new List<string>(normalizedPersonal);
                prop.EmotionalTags  = new List<string>(normalizedPersonal);
                prop.PersonalTags  = new List<string>(normalizedPersonal);
                if (droppedPersonal.Count > 0)
                    Debug.LogWarning($"[CurationManager] Dropped invalid personal tags for '{prop.Id}': {string.Join(", ", droppedPersonal)}");
            }
            if (corporateTags != null)
            {
                var normalizedCorporate = TagTaxonomy.NormalizeCorporate(corporateTags, out var droppedCorporate);
                entry.CorporateTags = new List<string>(normalizedCorporate);
                prop.CorporateTags = new List<string>(normalizedCorporate);
                if (droppedCorporate.Count > 0)
                    Debug.LogWarning($"[CurationManager] Dropped invalid corporate tags for '{prop.Id}': {string.Join(", ", droppedCorporate)}");
            }
            if (scaleOverride >= 0f)
            {
                entry.ScaleOverride = scaleOverride;
                prop.ScaleOverride  = scaleOverride;
            }
            if (customTags != null)
            {
                entry.CustomTags = new List<string>(customTags);
                prop.CustomTags  = new List<string>(customTags);
            }
            if (notes != null)
            {
                entry.Notes = notes;
                prop.Notes  = notes;
            }
        }

        // ── Custom groups ─────────────────────────────────────────────────────
        public void AddCustomGroup(string groupName)
        {
            groupName = groupName.Trim().ToLower().Replace(" ", "_");
            if (string.IsNullOrEmpty(groupName)) return;
            if (!Overlay.CustomGroups.Contains(groupName))
            {
                Overlay.CustomGroups.Add(groupName);
                SaveOverlay();
            }
        }

        public void RemoveCustomGroup(string groupName)
        {
            Overlay.CustomGroups.Remove(groupName);
            SaveOverlay();
        }

        public IEnumerable<string> AllGroups()
        {
            var builtIn = new[] { "item", "furniture", "lab", "office",
                                  "workshop", "domestic", "retail", "tech" };
            var manifestGroups = Manifest?.All
                ?.Select(p => p?.Group)
                .Where(g => !string.IsNullOrWhiteSpace(g))
                .Select(g => g.Trim().ToLowerInvariant())
                ?? Enumerable.Empty<string>();

            var custom = (Overlay?.CustomGroups ?? new List<string>())
                .Where(g => !string.IsNullOrWhiteSpace(g))
                .Select(g => g.Trim().ToLowerInvariant());

            return builtIn
                .Concat(manifestGroups)
                .Concat(custom)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(g => g, StringComparer.Ordinal);
        }

        // ── Unified tag space ─────────────────────────────────────────────────
        // Returns the unified tag set for a prop: group + personal + corporate +
        // custom (with `corp:` prefix stripped) + emotional + freeform tags.
        // Empty/whitespace tags are dropped, casing is normalized.
        public static IEnumerable<string> GetUnifiedTagsForProp(PropEntry p)
        {
            if (p == null) yield break;
            var seen = new HashSet<string>(StringComparer.Ordinal);

            string Norm(string s) => (s ?? "").Trim().ToLowerInvariant();

            string g = Norm(p.Group);
            if (g.Length > 0 && seen.Add(g)) yield return g;

            if (p.PersonalTags != null)
                foreach (var t in p.PersonalTags) { var n = Norm(t); if (n.Length > 0 && seen.Add(n)) yield return n; }
            if (p.EmotionalTags != null)
                foreach (var t in p.EmotionalTags) { var n = Norm(t); if (n.Length > 0 && seen.Add(n)) yield return n; }
            if (p.CorporateTags != null)
                foreach (var t in p.CorporateTags) { var n = Norm(t); if (n.Length > 0 && seen.Add(n)) yield return n; }
            if (p.CustomTags != null)
                foreach (var raw in p.CustomTags)
                {
                    var n = Norm(raw);
                    if (n.StartsWith("corp:", StringComparison.Ordinal)) n = n.Substring(5);
                    if (n.Length > 0 && seen.Add(n)) yield return n;
                }
            if (p.Tags != null)
                foreach (var t in p.Tags) { var n = Norm(t); if (n.Length > 0 && seen.Add(n)) yield return n; }
        }

        // Convenience: hashed lookup of unified tags for a prop.
        public static HashSet<string> GetUnifiedTagSetForProp(PropEntry p)
            => new HashSet<string>(GetUnifiedTagsForProp(p), StringComparer.Ordinal);

        // Returns true when a prop has zero unified tags.
        public static bool IsPropUntagged(PropEntry p)
        {
            using (var e = GetUnifiedTagsForProp(p).GetEnumerator())
                return !e.MoveNext();
        }

        // Universe of tags presented to the curator: taxonomy (always shown,
        // even at zero count) + every tag actually used in the manifest +
        // overlay custom groups. Sorted alphabetically.
        public IEnumerable<string> AllTags()
        {
            TagTaxonomy.EnsureLoaded();
            var taxonomy = (TagTaxonomy.PersonalTags ?? new List<string>())
                .Concat(TagTaxonomy.CorporateTags ?? new List<string>());

            var manifestTags = Manifest?.All
                ?.Where(p => p != null)
                .SelectMany(p => GetUnifiedTagsForProp(p))
                ?? Enumerable.Empty<string>();

            var customs = (Overlay?.CustomGroups ?? new List<string>())
                .Where(g => !string.IsNullOrWhiteSpace(g))
                .Select(g => g.Trim().ToLowerInvariant());

            return taxonomy
                .Concat(manifestTags)
                .Concat(customs)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(t => t, StringComparer.Ordinal);
        }

        // Returns count of active (non-removed) props per tag.
        // Includes every taxonomy tag even when count is zero, so the curator
        // sees the full filterable set rather than only tags that already exist.
        public IEnumerable<(string tag, int count)> GetTagBreakdown()
        {
            if (Manifest == null) yield break;
            var active = Manifest.All.Where(p => !Overlay.RemovedIds.Contains(p.Id)).ToList();
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var p in active)
            {
                foreach (var tag in GetUnifiedTagsForProp(p))
                {
                    counts[tag] = counts.TryGetValue(tag, out var c) ? c + 1 : 1;
                }
            }

            // Fold in the full known tag universe so zero-count tags still surface.
            foreach (var t in AllTags())
            {
                if (!counts.ContainsKey(t))
                    counts[t] = 0;
            }

            foreach (var kv in counts
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key, StringComparer.Ordinal))
            {
                yield return (kv.Key, kv.Value);
            }
        }

        // ── Stats for UI ──────────────────────────────────────────────────────
        public Dictionary<CurationQueue, int> GetQueueCounts()
        {
            if (Manifest == null) return new Dictionary<CurationQueue, int>();

            var all     = Manifest.All.ToList();
            var removed = Overlay.RemovedIds;
            var reviewed = Overlay.ReviewedIds;
            var active  = all.Where(p => !removed.Contains(p.Id)).ToList();

            return new Dictionary<CurationQueue, int>
            {
                [CurationQueue.All]        = active.Count,
                [CurationQueue.Oversized]  = active.Count(p => p.LongestAxis > _oversizedThreshold),
                [CurationQueue.LowConf]    = active.Count(p => p.Confidence < _lowConfThreshold),
                [CurationQueue.Unreviewed] = active.Count(p => !reviewed.Contains(p.Id)),
                [CurationQueue.ByGroup]    = string.IsNullOrEmpty(FilterGroup) ? 0
                    : active.Count(p => p.Group == FilterGroup),
                [CurationQueue.ByTag]      = string.IsNullOrEmpty(FilterTag) ? 0
                    : active.Count(p => GetUnifiedTagSetForProp(p).Contains(FilterTag)),
                [CurationQueue.Untagged]   = active.Count(IsPropUntagged),
                [CurationQueue.Removed]    = removed.Count,
            };
        }

        // Returns the groups present in the current manifest for the ByGroup selector dropdown.
        public IEnumerable<(string group, int count)> GetGroupBreakdown()
        {
            if (Manifest == null) yield break;
            var active = Manifest.All.Where(p => !Overlay.RemovedIds.Contains(p.Id));
            foreach (var g in active.GroupBy(p => p.Group).OrderByDescending(g => g.Count()))
                yield return (g.Key, g.Count());
        }

        // ── Save ──────────────────────────────────────────────────────────────
        public void SaveOverlay()
        {
            CurationOverlay.Save(Overlay);
            Manifest?.SaveToStreamingAssets(Overlay.RemovedIds);
            if (Manifest != null)
            {
                int withPersonal = Manifest.All.Count(p => p.PersonalTags != null && p.PersonalTags.Count > 0);
                int withCorporate = Manifest.All.Count(p => p.CorporateTags != null && p.CorporateTags.Count > 0);
                Debug.Log($"[CurationManager] Saved overlay + manifest. personal_tags={withPersonal}/{Manifest.All.Count} corporate_tags={withCorporate}/{Manifest.All.Count}");
            }
            OnOverlaySaved?.Invoke();
        }

        // ── Internals ─────────────────────────────────────────────────────────
        private void RebuildQueue()
        {
            if (Manifest == null) { _queue = new List<PropEntry>(); return; }

            IEnumerable<PropEntry> source = Manifest.All;

            if (ActiveQueue != CurationQueue.Removed)
                source = source.Where(p => !Overlay.RemovedIds.Contains(p.Id));

            switch (ActiveQueue)
            {
                case CurationQueue.Oversized:
                    source = source.Where(p => p.LongestAxis > _oversizedThreshold);
                    break;
                case CurationQueue.LowConf:
                    source = source.Where(p => p.Confidence < _lowConfThreshold);
                    break;
                case CurationQueue.Unreviewed:
                    source = source.Where(p => !Overlay.ReviewedIds.Contains(p.Id));
                    break;
                case CurationQueue.ByGroup:
                    if (!string.IsNullOrEmpty(FilterGroup))
                        source = source.Where(p => p.Group == FilterGroup);
                    break;
                case CurationQueue.ByTag:
                    if (string.IsNullOrEmpty(FilterTag))
                        source = Enumerable.Empty<PropEntry>();
                    else
                        source = source.Where(p => GetUnifiedTagSetForProp(p).Contains(FilterTag));
                    break;
                case CurationQueue.Untagged:
                    source = source.Where(IsPropUntagged);
                    break;
                case CurationQueue.Removed:
                    source = Manifest.All.Where(p => Overlay.RemovedIds.Contains(p.Id));
                    break;
                case CurationQueue.All:
                default:
                    break;
            }

            _queue = source.ToList();
        }

        private void RemoveCurrentAndAdvance()
        {
            if (_queue.Count == 0) return;
            _queue.RemoveAt(_cursor);
            if (_cursor >= _queue.Count && _cursor > 0)
                _cursor--;
            OnPropChanged?.Invoke(CurrentProp);
        }

        private OverlayEntry GetOrCreateEntry(string id)
        {
            if (!Overlay.Overrides.TryGetValue(id, out var entry))
                Overlay.Overrides[id] = entry = new OverlayEntry();
            return entry;
        }
    }
}

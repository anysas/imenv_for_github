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
        ByGroup    = 4,
        Removed    = 5,
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
        public void SetQueue(CurationQueue queue, string groupFilter = "")
        {
            ActiveQueue = queue;
            FilterGroup = groupFilter ?? "";
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
            List<string> emotionalTags = null,
            float        scaleOverride = -1f,
            List<string> customTags   = null,
            string       notes        = null)
        {
            var prop = CurrentProp;
            if (prop == null) return;

            var entry = GetOrCreateEntry(prop.Id);

            if (group != null)
            {
                entry.Group = group;
                prop.Group  = group;
            }
            if (emotionalTags != null)
            {
                entry.EmotionalTags = new List<string>(emotionalTags);
                prop.EmotionalTags  = new List<string>(emotionalTags);
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
            return builtIn.Concat(Overlay.CustomGroups).Distinct().OrderBy(g => g);
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

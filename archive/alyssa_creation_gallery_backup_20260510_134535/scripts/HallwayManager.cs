using System;
using System.Collections.Generic;
using System.IO;
using System.Collections;
using Newtonsoft.Json;
using UnityEngine;

namespace AlgorithmicGallery.Corruption
{
    /// <summary>
    /// Spawns hallway pedestal dioramas from prior sessions.
    ///
    /// Order of preference for session sources:
    ///   1. sessions/index.json in persistentDataPath (most recent N sessions)
    ///   2. Seed sessions in StreamingAssets/seed_sessions/ (shipped with the build)
    ///
    /// Scene setup (Alyssa-led):
    ///   - Create an empty GameObject named "HallwayPedestals" in the hallway scene.
    ///   - Optionally add N child GameObjects as explicit pedestal anchor positions.
    ///     If no children exist, HallwayManager spawns anchors at evenly spaced intervals
    ///     along the hallway forward axis.
    ///
    /// HallwayManager reads the index, picks the most recent sessions, and assigns one
    /// session JSON per pedestal. Falls back to seed sessions when the live index is empty.
    /// </summary>
    public class HallwayManager : MonoBehaviour
    {
        [Header("Scene anchors")]
        [Tooltip("Root containing pedestal anchor child GameObjects. If null, searches by name 'HallwayPedestals'.")]
        [SerializeField] private Transform _pedestalsRoot;
        [Tooltip("Number of pedestals to spawn if no explicit anchor children are present.")]
        [SerializeField] private int _autoPedestalCount = 3;
        [Tooltip("Spacing between auto-generated pedestals along the local Z axis.")]
        [SerializeField] private float _autoPedestalSpacing = 2.5f;

        [Header("Session loading")]
        [Tooltip("Max sessions to display. Normally matches the number of pedestal anchors.")]
        [SerializeField] private int _maxSessions = 3;

        [Header("Live pedestal")]
        [Tooltip("The designated empty pedestal that shows the current visitor's creation after their session ends.")]
        [SerializeField] private Transform _livePedestalAnchor;

        [Header("Pedestal mesh (optional)")]
        [Tooltip("If assigned, instantiated under each anchor as a visual base.")]
        [SerializeField] private GameObject _pedestalMeshPrefab;

        private const string SeedSessionsDir = "seed_sessions";
        private SandboxManager _sandbox;

        void Start()
        {
            if (_pedestalsRoot == null)
            {
                var go = GameObject.Find("HallwayPedestals");
                if (go != null) _pedestalsRoot = go.transform;
            }

            _sandbox = FindFirstObjectByType<SandboxManager>();
            if (_sandbox != null)
                _sandbox.OnSessionComplete.AddListener(HandleSessionComplete);

            var anchors = CollectOrCreateAnchors();
            var sessions = CollectSessions(Mathf.Min(anchors.Count, _maxSessions));

            for (int i = 0; i < anchors.Count && i < sessions.Count; i++)
                SpawnPedestal(anchors[i], sessions[i]);
        }

        void OnDestroy()
        {
            if (_sandbox != null)
                _sandbox.OnSessionComplete.RemoveListener(HandleSessionComplete);
        }

        private void HandleSessionComplete()
        {
            if (_livePedestalAnchor == null)
                return;
            StartCoroutine(RefreshLivePedestal());
        }

        private IEnumerator RefreshLivePedestal()
        {
            // Allow SessionExporter to finish writing sessions/index.json and the session JSON.
            yield return new WaitForSeconds(1f);

            if (_livePedestalAnchor == null)
                yield break;

            var sessions = CollectSessions(1);
            if (sessions.Count == 0)
                yield break;

            // Clear existing diorama content under the live anchor
            var children = new List<Transform>();
            foreach (Transform child in _livePedestalAnchor)
                children.Add(child);
            for (int i = 0; i < children.Count; i++)
                Destroy(children[i].gameObject);

            SpawnPedestal(_livePedestalAnchor, sessions[0]);
        }

        // ── Anchor resolution ──────────────────────────────────────────────

        private List<Transform> CollectOrCreateAnchors()
        {
            var anchors = new List<Transform>();

            if (_pedestalsRoot != null && _pedestalsRoot.childCount > 0)
            {
                for (int i = 0; i < _pedestalsRoot.childCount; i++)
                    anchors.Add(_pedestalsRoot.GetChild(i));
                return anchors;
            }

            // No explicit anchors: generate them along this object's forward axis
            Transform parent = _pedestalsRoot != null ? _pedestalsRoot : transform;
            for (int i = 0; i < _autoPedestalCount; i++)
            {
                var anchor = new GameObject($"PedestalAnchor_{i}");
                anchor.transform.SetParent(parent, worldPositionStays: false);
                anchor.transform.localPosition = new Vector3(0f, 0f, i * _autoPedestalSpacing);
                anchors.Add(anchor.transform);
            }
            return anchors;
        }

        // ── Session resolution ─────────────────────────────────────────────

        private List<string> CollectSessions(int count)
        {
            var paths = new List<string>();

            // Try live index first
            string indexPath = Path.Combine(Application.persistentDataPath, "sessions", "index.json");
            if (File.Exists(indexPath))
            {
                try
                {
                    string json = File.ReadAllText(indexPath);
                    var entries = JsonConvert.DeserializeObject<List<SessionExporter.IndexEntry>>(json);
                    if (entries != null && entries.Count > 0)
                    {
                        // Take the N most recent (last in list = most recent)
                        int start = Mathf.Max(0, entries.Count - count);
                        string sessionsDir = Path.Combine(Application.persistentDataPath, "sessions");
                        for (int i = entries.Count - 1; i >= start && paths.Count < count; i--)
                        {
                            string p = Path.Combine(sessionsDir, entries[i].Id + ".json");
                            if (File.Exists(p))
                                paths.Add(p);
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[HallwayManager] Failed to read index.json: {e.Message}");
                }
            }

            // Fill remaining slots from seed sessions
            if (paths.Count < count)
            {
                string seedDir = Path.Combine(Application.streamingAssetsPath, SeedSessionsDir);
                if (Directory.Exists(seedDir))
                {
                    string[] seeds = Directory.GetFiles(seedDir, "*.json");
                    int seedIdx = 0;
                    while (paths.Count < count && seedIdx < seeds.Length)
                    {
                        paths.Add(seeds[seedIdx]);
                        seedIdx++;
                    }
                }
            }

            if (paths.Count == 0)
                Debug.LogWarning("[HallwayManager] No session files found (live or seed). Pedestals will be empty.");

            return paths;
        }

        // ── Pedestal instantiation ─────────────────────────────────────────

        private void SpawnPedestal(Transform anchor, string sessionPath)
        {
            // Optional visual base mesh
            if (_pedestalMeshPrefab != null)
                Instantiate(_pedestalMeshPrefab, anchor.position, anchor.rotation, anchor);

            // Diorama pedestal
            var pedestalGO = new GameObject("HallwayDioramaPedestal");
            pedestalGO.transform.SetParent(anchor, worldPositionStays: false);
            pedestalGO.transform.localPosition = Vector3.zero;
            var pedestal = pedestalGO.AddComponent<HallwayDioramaPedestal>();
            pedestal.SetSessionPath(sessionPath);
        }
    }
}

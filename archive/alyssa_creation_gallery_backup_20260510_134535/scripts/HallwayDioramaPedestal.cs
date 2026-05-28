using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace AlgorithmicGallery.Corruption
{
    /// <summary>
    /// Loads a prior session JSON and renders a scaled-down ghost diorama on a pedestal.
    ///
    /// Scene setup: place a GameObject in the hallway as the pedestal anchor. The diorama
    /// spawns parented to this transform. HallwayManager assigns the session path at runtime.
    ///
    /// The diorama is ghost-rendered (translucent, emissive blue-white) so it reads as memory
    /// rather than a full scene recreation. A text plate above the pedestal shows the prompt
    /// text and override level from the session.
    /// </summary>
    public class HallwayDioramaPedestal : MonoBehaviour
    {
        [Header("Session source")]
        [Tooltip("Absolute path to the session JSON, OR leave empty to let HallwayManager assign it.")]
        [SerializeField] private string _sessionJsonPath = "";

        [Header("Diorama sizing")]
        [Tooltip("Target footprint diameter for the diorama in world units.")]
        [SerializeField] private float _dioramaFootprintDiameter = 0.28f;
        [Tooltip("Additional Y offset above this transform for the diorama content.")]
        [SerializeField] private float _dioramaYOffset = 0.02f;
        [Tooltip("Max props to render in the diorama (performance cap).")]
        [SerializeField] private int _maxProps = 30;

        [Header("Plate text")]
        [SerializeField] private float _plateFontSize = 0.04f;
        [SerializeField] private Color _plateTextColor = new Color(1f, 0.55f, 0.2f);

        [Header("Ghost appearance")]
        [SerializeField] private Color _ghostColor = new Color(0.55f, 0.82f, 1f, 0.30f);

        private SculptureSpawner _spawner;
        private Material _ghostMaterial;
        private bool _loaded = false;

        void Start()
        {
            // SculptureSpawner is needed to load GLBs. Find or add one.
            _spawner = FindFirstObjectByType<AlgorithmicGallery.SculptureSpawner>();
            if (_spawner == null)
            {
                _spawner = gameObject.AddComponent<AlgorithmicGallery.SculptureSpawner>();
            }

            if (!string.IsNullOrEmpty(_sessionJsonPath))
                StartCoroutine(LoadDiorama(_sessionJsonPath));
        }

        /// <summary>Assign a session path from HallwayManager and kick off loading.</summary>
        public void SetSessionPath(string absolutePath)
        {
            if (_loaded) return;
            _sessionJsonPath = absolutePath;
            StartCoroutine(LoadDiorama(absolutePath));
        }

        private IEnumerator LoadDiorama(string path)
        {
            _loaded = true;

            if (!File.Exists(path))
            {
                Debug.LogWarning($"[HallwayDioramaPedestal] Session file not found: {path}");
                yield break;
            }

            DioramaSession session = null;
            try
            {
                string json = File.ReadAllText(path);
                session = JsonConvert.DeserializeObject<DioramaSession>(json);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[HallwayDioramaPedestal] Failed to parse {path}: {e.Message}");
                yield break;
            }

            if (session?.Placements == null || session.Placements.Count == 0)
                yield break;

            // Build the plate before the props load so there's something to see immediately
            BuildPlate(session);

            // ── Compute bounding box of placements ──────────────────────────
            Vector3 boundsMin = new Vector3(float.MaxValue, 0f, float.MaxValue);
            Vector3 boundsMax = new Vector3(float.MinValue, 0f, float.MinValue);
            foreach (var p in session.Placements)
            {
                if (p.Position == null || p.Position.Length < 3) continue;
                float x = p.Position[0], z = p.Position[2];
                if (x < boundsMin.x) boundsMin.x = x;
                if (z < boundsMin.z) boundsMin.z = z;
                if (x > boundsMax.x) boundsMax.x = x;
                if (z > boundsMax.z) boundsMax.z = z;
            }
            float sourceWidth  = Mathf.Max(boundsMax.x - boundsMin.x, 0.01f);
            float sourceDepth  = Mathf.Max(boundsMax.z - boundsMin.z, 0.01f);
            float sourceLargest = Mathf.Max(sourceWidth, sourceDepth);
            float scale = _dioramaFootprintDiameter / sourceLargest;

            Vector3 sourceCenter = new Vector3(
                (boundsMin.x + boundsMax.x) * 0.5f,
                0f,
                (boundsMin.z + boundsMax.z) * 0.5f);

            // Diorama root — parented to pedestal, scale applied here
            var dioramaRoot = new GameObject("DioramaRoot");
            dioramaRoot.transform.SetParent(transform, worldPositionStays: false);
            dioramaRoot.transform.localPosition = new Vector3(0f, _dioramaYOffset, 0f);
            dioramaRoot.transform.localScale    = Vector3.one * scale;

            var ghostMat = GetGhostMaterial();

            // ── Spawn props ──────────────────────────────────────────────────
            int spawned = 0;
            int limit = Mathf.Min(session.Placements.Count, _maxProps);
            for (int i = 0; i < limit; i++)
            {
                var placement = session.Placements[i];
                if (placement.Position == null || placement.Position.Length < 3) continue;
                if (string.IsNullOrEmpty(placement.GlbPath)) continue;

                var task = _spawner.LoadModel(
                    placement.GlbPath,
                    parent: dioramaRoot.transform,
                    addSculptureController: false,
                    addCollider: false,
                    normalizeScale: false,
                    scaleMultiplier: 2f); // match PropPlacer's default scale

                while (!task.IsCompleted)
                    yield return null;

                var go = task.Result;
                if (go == null) continue;

                // Position relative to source center (in source world space, then scaled by root)
                float relX = placement.Position[0] - sourceCenter.x;
                float relZ = placement.Position[2] - sourceCenter.z;
                go.transform.localPosition = new Vector3(relX, 0f, relZ);
                go.transform.localRotation = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);
                go.name = "_DioProp";

                // Disable all colliders — these are decorative
                foreach (var col in go.GetComponentsInChildren<Collider>(true))
                    col.enabled = false;

                // Apply ghost appearance
                if (ghostMat != null)
                {
                    foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                    {
                        if (r == null) continue;
                        var mats = r.sharedMaterials;
                        for (int m = 0; m < mats.Length; m++) mats[m] = ghostMat;
                        r.sharedMaterials = mats;
                    }
                }

                spawned++;
                // Yield every few props so we don't spike a single frame
                if (spawned % 5 == 0)
                    yield return null;
            }

            Debug.Log($"[HallwayDioramaPedestal] Loaded diorama: {spawned} props from \"{session.PromptText}\"");
        }

        private void BuildPlate(DioramaSession session)
        {
            // A small text mesh floats above the pedestal
            var plate = new GameObject("DioramaPlate");
            plate.transform.SetParent(transform, worldPositionStays: false);
            plate.transform.localPosition = new Vector3(0f, 0.12f, 0f);
            plate.transform.localRotation = Quaternion.identity;

            var tm = plate.AddComponent<TextMesh>();
            tm.anchor      = TextAnchor.LowerCenter;
            tm.alignment   = TextAlignment.Center;
            tm.characterSize = _plateFontSize;
            tm.fontSize    = 48;
            tm.color       = _plateTextColor;

            string promptLine = string.IsNullOrEmpty(session.PromptText)
                ? "—"
                : $"\"{session.PromptText}\"";
            string phaseLine = string.IsNullOrEmpty(session.FinalPhase)
                ? ""
                : $"\n{session.FinalPhase.ToLower()}";

            tm.text = $"{promptLine}{phaseLine}";
        }

        private Material GetGhostMaterial()
        {
            if (_ghostMaterial != null) return _ghostMaterial;

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("Standard");
            if (shader == null) return null;

            _ghostMaterial = new Material(shader);
            _ghostMaterial.name = "DioramaGhostMaterial";
            _ghostMaterial.color = _ghostColor;
            _ghostMaterial.SetFloat("_Surface", 1f);
            _ghostMaterial.SetFloat("_Blend", 0f);
            _ghostMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _ghostMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _ghostMaterial.SetInt("_ZWrite", 0);
            _ghostMaterial.renderQueue = 3000;
            _ghostMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            return _ghostMaterial;
        }

        void OnDestroy()
        {
            if (_ghostMaterial != null)
                Destroy(_ghostMaterial);
        }

        // ── Minimal JSON deserialization types ────────────────────────────────
        // Mirrors enough of SessionRecord to render the diorama.

        [Serializable]
        private class DioramaSession
        {
            public string PromptText;
            public string FinalPhase;
            public List<DioramaPlacement> Placements;
        }

        [Serializable]
        private class DioramaPlacement
        {
            [JsonProperty("Position")]    public float[] Position;
            [JsonProperty("GlbPath")]     public string GlbPath;
            [JsonProperty("Group")]       public string Group;
            [JsonProperty("IsPlayer")]    public bool IsPlayer;
        }
    }
}

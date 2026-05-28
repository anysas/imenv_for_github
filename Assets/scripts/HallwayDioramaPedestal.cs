using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace AlgorithmicGallery.Corruption
{
    /// <summary>
    /// Loads a prior session JSON and renders a scaled-down diorama on a pedestal.
    ///
    /// Scene setup: place a GameObject in the hallway as the pedestal anchor. The diorama
    /// spawns parented to this transform. HallwayManager assigns the session path at runtime.
    ///
    /// Schema v2+: replays placements one-to-one from <see cref="SessionExporter"/> data.
    /// Exact path uses one diorama root with uniform scale so spacing and prop sizes shrink together.
    /// Older sessions fall back to footprint fitting + legacy layout.
    /// </summary>
    public class HallwayDioramaPedestal : MonoBehaviour
    {
        private const int SessionSchemaVersionExactReplay = 2;

        [Header("Session source")]
        [Tooltip("Absolute path to the session JSON, OR leave empty to let HallwayManager assign it.")]
        [SerializeField] private string _sessionJsonPath = "";

        [Header("Exact replay (schema v2+)")]
        [Tooltip("Uniform scale on the diorama root only. Positions and saved prop scales shrink together (spacing preserved).")]
        [SerializeField] private float _uniformShrink = 0.075f;

        [Header("Hallway orientation")]
        [Tooltip("Rotate each pedestal diorama to face inward toward the average center of pedestal anchors.")]
        [SerializeField] private bool _faceInwardToHallwayCenter = true;

        [Header("Legacy sizing (pre-v2 sessions)")]
        [Tooltip("Target footprint diameter for the diorama in world units.")]
        [SerializeField] private float _dioramaFootprintDiameter = 0.28f;

        [Tooltip("Multiplies legacy diorama root scale after footprint fit (e.g. 3 = three times larger on the pedestal).")]
        [SerializeField] private float _legacyScaleMultiplier = 3f;

        [Tooltip("Additional Y offset above this transform for the diorama content.")]
        [SerializeField] private float _dioramaYOffset = 0.02f;

        [Tooltip("Extra clearance (world metres) above the detected pedestal mesh top for exact replay.")]
        [SerializeField] private float _exactReplayTopMargin = 0.05f;

        [Tooltip("Max props to render in the diorama (performance cap).")]
        [SerializeField] private int _maxProps = 30;

        [Header("Plate text")]
        [SerializeField] private float _plateFontSize = 0.04f;
        [SerializeField] private Color _plateTextColor = new Color(0.52f, 0.66f, 0.82f, 1f);

        [Header("Placeholder (missing GLB)")]
        [SerializeField] private Color _proxyPlaceholderColor = new Color(0.55f, 0.82f, 1f, 0.85f);

        [Tooltip("If true, render simple proxy cubes when GLB files are missing or fail to load.")]
        [SerializeField] private bool _spawnProxyWhenModelMissing = true;

        private SculptureSpawner _spawner;
        private Material _proxyMaterial;
        private Coroutine _loadCoroutine;

        void Awake()
        {
            EnsureSpawner();
        }

        void Start()
        {
            EnsureSpawner();
            if (_loadCoroutine != null)
                return;
            if (!string.IsNullOrEmpty(_sessionJsonPath))
                BeginLoad(_sessionJsonPath);
        }

        public void SetSessionPath(string absolutePath)
        {
            _sessionJsonPath = absolutePath;
            BeginLoad(absolutePath);
        }

        private void EnsureSpawner()
        {
            if (_spawner != null) return;

            _spawner = FindFirstObjectByType<AlgorithmicGallery.SculptureSpawner>();
            if (_spawner == null)
                _spawner = gameObject.AddComponent<AlgorithmicGallery.SculptureSpawner>();
        }

        private void BeginLoad(string path)
        {
            EnsureSpawner();
            if (_spawner == null)
            {
                Debug.LogError("[HallwayDioramaPedestal] SculptureSpawner unavailable; cannot load diorama.");
                return;
            }

            if (string.IsNullOrEmpty(path))
                return;

            if (_loadCoroutine != null)
            {
                StopCoroutine(_loadCoroutine);
                _loadCoroutine = null;
            }

            ClearGeneratedChildrenImmediate();

            _loadCoroutine = StartCoroutine(LoadDiorama(path));
        }

        private void ClearGeneratedChildrenImmediate()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;
                if (Application.isPlaying)
                    Destroy(child);
                else
                    DestroyImmediate(child);
            }
        }

        private IEnumerator LoadDiorama(string path)
        {
            if (_spawner == null)
            {
                Debug.LogError("[HallwayDioramaPedestal] LoadDiorama: spawner is null.");
                _loadCoroutine = null;
                yield break;
            }

            if (!File.Exists(path))
            {
                Debug.LogWarning($"[HallwayDioramaPedestal] Session file not found: {path}");
                _loadCoroutine = null;
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
                _loadCoroutine = null;
                yield break;
            }

            if (session?.Placements == null || session.Placements.Count == 0)
            {
                _loadCoroutine = null;
                yield break;
            }

            bool exact = HasExactReplayData(session);
            GameplayEventDebugLog.Push(
                "Diorama",
                $"{Path.GetFileName(path)} schema={session.SchemaVersion} exact={exact} props={session.Placements.Count}");

            BuildPlate(session);

            if (exact)
                yield return LoadExact(session);
            else
                yield return LoadLegacy(session);

            _loadCoroutine = null;
        }

        private static bool HasExactReplayData(DioramaSession session)
        {
            if (session.SchemaVersion < SessionSchemaVersionExactReplay)
                return false;
            if (session.SandboxOrigin == null || session.SandboxOrigin.Length < 3)
                return false;

            foreach (var p in session.Placements)
            {
                if (!PlacementHasExactFields(p))
                    return false;
            }

            return true;
        }

        private static bool PlacementHasExactFields(DioramaPlacement p)
        {
            return p.Position != null && p.Position.Length >= 3
                   && p.Rotation != null && p.Rotation.Length >= 4
                   && p.Scale != null && p.Scale.Length >= 3
                   && !string.IsNullOrEmpty(p.GlbPath);
        }

        private IEnumerator LoadExact(DioramaSession session)
        {
            Transform pedestal = transform;

            float s = Mathf.Max(1e-5f, _uniformShrink);
            float liftWorld = ComputeExactReplayBaseLiftWorld(pedestal);
            Quaternion rootRot = ResolveInwardHallwayRotation(pedestal);
            Vector3 rootPos = pedestal.position + pedestal.up * liftWorld;

            // Single root: uniform scale s scales both layout (positions) and prop sizes together.
            // worldPos_i = rootPos + rootRot * (s * localPos_i), with localPos_i = Inv(rootRot) * (savedPos_i - centroid).
            var dioramaRoot = new GameObject("DioramaRoot_Exact");
            dioramaRoot.transform.SetParent(pedestal, worldPositionStays: true);
            dioramaRoot.transform.SetPositionAndRotation(rootPos, rootRot);
            dioramaRoot.transform.localScale = Vector3.one * s;

            int limit = Mathf.Min(session.Placements.Count, _maxProps);
            Vector3 centroid = Vector3.zero;
            int validForCentroid = 0;
            Vector3 minWorld = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 maxWorld = new Vector3(float.MinValue, float.MinValue, float.MinValue);

            for (int i = 0; i < limit; i++)
            {
                if (!TryReadPlacementTransform(session.Placements[i], out Vector3 wp, out _, out _))
                    continue;
                centroid += wp;
                validForCentroid++;
                minWorld = Vector3.Min(minWorld, wp);
                maxWorld = Vector3.Max(maxWorld, wp);
            }

            if (validForCentroid == 0)
            {
                Debug.LogWarning("[HallwayDioramaPedestal] Exact replay: no valid placements.");
                yield break;
            }

            centroid /= validForCentroid;
            Vector3 sourceSpan = maxWorld - minWorld;
            Quaternion invRoot = Quaternion.Inverse(rootRot);

            int spawned = 0;
            for (int i = 0; i < limit; i++)
            {
                var placement = session.Placements[i];
                if (!TryReadPlacementTransform(placement, out Vector3 savedPos, out Quaternion savedRot, out Vector3 savedScale))
                    continue;

                string fullPath = Path.Combine(Application.streamingAssetsPath, "models", placement.GlbPath);
                GameObject go = null;

                if (File.Exists(fullPath))
                {
                    var task = _spawner.LoadModel(
                        placement.GlbPath,
                        parent: dioramaRoot.transform,
                        addSculptureController: false,
                        addCollider: false,
                        normalizeScale: false,
                        scaleMultiplier: 1f);

                    while (!task.IsCompleted)
                        yield return null;

                    go = task.Result;
                }
                else if (_spawnProxyWhenModelMissing)
                {
                    go = CreateProxyCube(GetProxyMaterial());
                    go.transform.SetParent(dioramaRoot.transform, worldPositionStays: false);
                }

                if (go == null)
                    continue;

                Vector3 worldOffsetFromCentroid = savedPos - centroid;
                go.transform.localPosition = invRoot * worldOffsetFromCentroid;
                go.transform.localRotation = invRoot * savedRot;
                go.transform.localScale = savedScale;

                go.name = "_DioProp";
                DisableCollidersUnder(go);
                spawned++;

                if (spawned % 5 == 0)
                    yield return null;
            }

            Vector3 shrunkSpan = sourceSpan * s;
            Debug.Log(
                $"[HallwayDioramaPedestal] Exact replay: {spawned} props from \"{session.PromptText}\" " +
                $"(rootUniformScale={s:F3}, lift={liftWorld:F3}m, sourceSpanXZ={sourceSpan.x:F2}/{sourceSpan.z:F2}, shrunkSpanXZ={shrunkSpan.x:F2}/{shrunkSpan.z:F2})");
        }

        /// <summary>
        /// World-space distance along <paramref name="anchor"/>.up from anchor.position to sit props above the pedestal slab.
        /// Uses parent mesh bounds when present so scaled hallway prefabs (large localScale) do not bury props inside the mesh.
        /// </summary>
        private float ComputeExactReplayBaseLiftWorld(Transform anchor)
        {
            Vector3 up = anchor.up;
            float upLen = up.magnitude;
            if (upLen < 1e-5f)
                up = Vector3.up;
            else
                up /= upLen;

            // Match nameplate: plate uses local (0, 0.12, 0) — convert to world along scaled hierarchy.
            float plateLift = Vector3.Dot(anchor.TransformVector(new Vector3(0f, 0.12f, 0f)), up);
            float configured = Vector3.Dot(anchor.TransformVector(new Vector3(0f, _dioramaYOffset, 0f)), up);
            float lift = Mathf.Max(configured, plateLift, 0.02f);

            var selfRenderer = anchor.GetComponent<MeshRenderer>();
            if (selfRenderer != null)
            {
                float topAlongUp = MaxBoundsCornerDotAlongAxis(selfRenderer.bounds, anchor.position, up);
                lift = Mathf.Max(lift, topAlongUp + _exactReplayTopMargin);
            }

            Transform host = anchor.parent;
            if (host != null)
            {
                var parentRenderer = host.GetComponent<MeshRenderer>() ?? host.GetComponentInChildren<MeshRenderer>(true);
                if (parentRenderer != null)
                {
                    float topAlongUp = MaxBoundsCornerDotAlongAxis(parentRenderer.bounds, anchor.position, up);
                    lift = Mathf.Max(lift, topAlongUp + _exactReplayTopMargin);
                }
            }

            return lift;
        }

        /// <summary>Legacy diorama root uses local Y; map pedestal-aware world lift into anchor local space.</summary>
        private float ComputeLegacyDioramaRootLocalY()
        {
            float liftWorld = ComputeExactReplayBaseLiftWorld(transform);
            Vector3 worldOffset = transform.up.normalized * liftWorld;
            Vector3 local = transform.InverseTransformVector(worldOffset);
            return Mathf.Max(_dioramaYOffset, local.y);
        }

        private Quaternion ResolveInwardHallwayRotation(Transform pedestal)
        {
            if (!_faceInwardToHallwayCenter)
                return pedestal.rotation;

            var anchor = pedestal.parent;
            if (anchor == null || anchor.parent == null)
                return pedestal.rotation;

            var anchorsRoot = anchor.parent;
            int count = anchorsRoot.childCount;
            if (count <= 1)
                return pedestal.rotation;

            Vector3 center = Vector3.zero;
            for (int i = 0; i < count; i++)
                center += anchorsRoot.GetChild(i).position;
            center /= count;

            Vector3 toCenter = center - pedestal.position;
            Vector3 up = pedestal.up.sqrMagnitude > 1e-5f ? pedestal.up : Vector3.up;
            Vector3 projected = Vector3.ProjectOnPlane(toCenter, up);
            if (projected.sqrMagnitude < 1e-4f)
                return pedestal.rotation;

            return Quaternion.LookRotation(projected.normalized, up);
        }

        private static float MaxBoundsCornerDotAlongAxis(Bounds b, Vector3 origin, Vector3 axisNormalized)
        {
            Vector3 min = b.min;
            Vector3 max = b.max;
            float best = float.NegativeInfinity;
            for (int ix = 0; ix < 2; ix++)
            for (int iy = 0; iy < 2; iy++)
            for (int iz = 0; iz < 2; iz++)
            {
                var c = new Vector3(ix == 0 ? min.x : max.x, iy == 0 ? min.y : max.y, iz == 0 ? min.z : max.z);
                best = Mathf.Max(best, Vector3.Dot(c - origin, axisNormalized));
            }

            return best;
        }

        private static bool TryReadPlacementTransform(
            DioramaPlacement p,
            out Vector3 position,
            out Quaternion rotation,
            out Vector3 scale)
        {
            position = default;
            rotation = Quaternion.identity;
            scale = Vector3.one;

            if (p.Position == null || p.Position.Length < 3)
                return false;

            position = new Vector3(p.Position[0], p.Position[1], p.Position[2]);

            if (p.Rotation != null && p.Rotation.Length >= 4)
            {
                rotation = new Quaternion(p.Rotation[0], p.Rotation[1], p.Rotation[2], p.Rotation[3]);
                float mag2 = rotation.x * rotation.x + rotation.y * rotation.y + rotation.z * rotation.z + rotation.w * rotation.w;
                if (mag2 > 1e-8f)
                    rotation = rotation.normalized;
                else
                    rotation = Quaternion.identity;
            }

            if (p.Scale != null && p.Scale.Length >= 3)
                scale = new Vector3(p.Scale[0], p.Scale[1], p.Scale[2]);

            return true;
        }

        private IEnumerator LoadLegacy(DioramaSession session)
        {
            Vector3 boundsMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 boundsMax = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            foreach (var p in session.Placements)
            {
                if (p.Position == null || p.Position.Length < 3) continue;
                float x = p.Position[0], y = p.Position[1], z = p.Position[2];
                if (x < boundsMin.x) boundsMin.x = x;
                if (y < boundsMin.y) boundsMin.y = y;
                if (z < boundsMin.z) boundsMin.z = z;
                if (x > boundsMax.x) boundsMax.x = x;
                if (y > boundsMax.y) boundsMax.y = y;
                if (z > boundsMax.z) boundsMax.z = z;
            }

            float sourceWidth = Mathf.Max(boundsMax.x - boundsMin.x, 0.01f);
            float sourceDepth = Mathf.Max(boundsMax.z - boundsMin.z, 0.01f);
            float sourceLargest = Mathf.Max(sourceWidth, sourceDepth);

            Vector3 sourceCenter = new Vector3(
                (boundsMin.x + boundsMax.x) * 0.5f,
                0f,
                (boundsMin.z + boundsMax.z) * 0.5f);

            var dioramaRoot = new GameObject("DioramaRoot");
            dioramaRoot.transform.SetParent(transform, worldPositionStays: false);
            // Use pedestal-aware lift so legacy props clear scaled pedestal colliders (same issue as exact replay).
            float liftLocalY = ComputeLegacyDioramaRootLocalY();
            dioramaRoot.transform.localPosition = new Vector3(0f, liftLocalY, 0f);
            Quaternion inward = ResolveInwardHallwayRotation(transform);
            dioramaRoot.transform.rotation = inward;
            float legacyScale = (_dioramaFootprintDiameter / sourceLargest) * Mathf.Max(0.01f, _legacyScaleMultiplier);
            dioramaRoot.transform.localScale = Vector3.one * legacyScale;

            int limit = Mathf.Min(session.Placements.Count, _maxProps);
            int spawned = 0;
            int fallbackSpawned = 0;

            for (int i = 0; i < limit; i++)
            {
                var placement = session.Placements[i];
                if (placement.Position == null || placement.Position.Length < 3) continue;
                if (string.IsNullOrEmpty(placement.GlbPath)) continue;

                float relX = placement.Position[0] - sourceCenter.x;
                float relY = Mathf.Max(0f, placement.Position[1] - boundsMin.y);
                float relZ = placement.Position[2] - sourceCenter.z;
                Vector3 localPos = new Vector3(relX, relY * 0.15f, relZ);

                GameObject go = null;
                string fullPath = Path.Combine(Application.streamingAssetsPath, "models", placement.GlbPath);
                if (File.Exists(fullPath))
                {
                    var task = _spawner.LoadModel(
                        placement.GlbPath,
                        parent: dioramaRoot.transform,
                        addSculptureController: false,
                        addCollider: false,
                        normalizeScale: false,
                        scaleMultiplier: 2f / 3f);

                    while (!task.IsCompleted)
                        yield return null;

                    go = task.Result;
                }
                else if (_spawnProxyWhenModelMissing)
                {
                    go = CreateProxyProp(dioramaRoot.transform, sourceLargest, GetProxyMaterial());
                    fallbackSpawned++;
                }

                if (go == null) continue;

                go.transform.localPosition = localPos;
                go.transform.localRotation = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);
                go.name = "_DioProp";

                DisableCollidersUnder(go);
                spawned++;

                if (spawned % 5 == 0)
                    yield return null;
            }

            Debug.Log($"[HallwayDioramaPedestal] Legacy layout: {spawned} props ({fallbackSpawned} proxy) from \"{session.PromptText}\"");
        }

        private static GameObject CreateProxyCube(Material ghostMat)
        {
            var proxy = GameObject.CreatePrimitive(PrimitiveType.Cube);
            proxy.transform.localScale = Vector3.one * 0.04f;
            var col = proxy.GetComponent<Collider>();
            if (col != null) col.enabled = false;
            if (ghostMat != null)
            {
                var r = proxy.GetComponent<Renderer>();
                if (r != null) r.sharedMaterial = ghostMat;
            }

            return proxy;
        }

        private static GameObject CreateProxyProp(Transform parent, float sourceLargest, Material ghostMat)
        {
            var proxy = GameObject.CreatePrimitive(PrimitiveType.Cube);
            proxy.transform.SetParent(parent, worldPositionStays: false);
            float localSize = Mathf.Clamp(sourceLargest * 0.05f, 0.8f, 2.2f);
            proxy.transform.localScale = Vector3.one * localSize;

            var col = proxy.GetComponent<Collider>();
            if (col != null) col.enabled = false;

            if (ghostMat != null)
            {
                var r = proxy.GetComponent<Renderer>();
                if (r != null)
                    r.sharedMaterial = ghostMat;
            }

            return proxy;
        }

        private static void DisableCollidersUnder(GameObject go)
        {
            if (go == null) return;
            foreach (var col in go.GetComponentsInChildren<Collider>(true))
                col.enabled = false;
        }

        private void BuildPlate(DioramaSession session)
        {
            var plate = new GameObject("DioramaPlate");
            plate.transform.SetParent(transform, worldPositionStays: true);
            float lift = ComputeExactReplayBaseLiftWorld(transform);
            plate.transform.position = transform.position + transform.up * (lift + 0.05f);
            plate.transform.rotation = ResolveInwardHallwayRotation(transform);

            var tm = plate.AddComponent<TextMesh>();
            tm.anchor = TextAnchor.LowerCenter;
            tm.alignment = TextAlignment.Center;
            tm.characterSize = _plateFontSize;
            tm.fontSize = 48;
            tm.color = _plateTextColor;

            string promptLine = string.IsNullOrEmpty(session.PromptText)
                ? "—"
                : $"\"{session.PromptText}\"";
            string phaseLine = string.IsNullOrEmpty(session.FinalPhase)
                ? ""
                : $"\n{session.FinalPhase.ToLower()}";

            tm.text = $"{promptLine}{phaseLine}";
        }

        private Material GetProxyMaterial()
        {
            if (_proxyMaterial != null) return _proxyMaterial;

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("Standard");
            if (shader == null) return null;

            _proxyMaterial = new Material(shader);
            _proxyMaterial.name = "DioramaProxyMaterial";
            _proxyMaterial.color = _proxyPlaceholderColor;
            if (_proxyMaterial.HasProperty("_BaseColor"))
                _proxyMaterial.SetColor("_BaseColor", _proxyPlaceholderColor);
            if (_proxyMaterial.HasProperty("_Color"))
                _proxyMaterial.SetColor("_Color", _proxyPlaceholderColor);
            return _proxyMaterial;
        }

        void OnDestroy()
        {
            if (_proxyMaterial != null)
                Destroy(_proxyMaterial);
        }

        [Serializable]
        private class DioramaSession
        {
            [JsonProperty("SchemaVersion")] public int SchemaVersion;
            [JsonProperty("SandboxOrigin")] public float[] SandboxOrigin;
            [JsonProperty("SandboxRotation")] public float[] SandboxRotation;
            [JsonProperty("SandboxScale")] public float[] SandboxScale;
            public string PromptText;
            public string FinalPhase;
            public List<DioramaPlacement> Placements;
        }

        [Serializable]
        private class DioramaPlacement
        {
            [JsonProperty("Position")] public float[] Position;
            [JsonProperty("GlbPath")] public string GlbPath;
            [JsonProperty("Group")] public string Group;
            [JsonProperty("IsPlayer")] public bool IsPlayer;
            [JsonProperty("Rotation")] public float[] Rotation;
            [JsonProperty("Scale")] public float[] Scale;
        }
    }
}

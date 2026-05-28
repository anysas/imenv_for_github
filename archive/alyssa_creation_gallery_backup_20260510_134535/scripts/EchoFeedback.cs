using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace AlgorithmicGallery.Corruption
{
    public class EchoFeedback : MonoBehaviour
    {
        [Header("Echo Settings")]
        [SerializeField] private int _baseEchoCount = 3;
        [SerializeField] private int _maxEchoCount = 7;
        [SerializeField] private float _echoDuration = 4f;
        [SerializeField] private float _echoSpawnRadius = 1.5f;
        [SerializeField] private float _echoFloatHeight = 2.8f;
        [SerializeField] private float _echoFloatSpeed = 0.9f;
        [SerializeField] private float _echoRotateSpeed = 15f;
        [SerializeField] private float _echoScale = 0.62f;
        [SerializeField] private Color _echoColor = new Color(0.70f, 0.88f, 1f, 0.26f);

        [Header("Echo Whisper Sound")]
        [SerializeField] private float _echoPingBaseVolume = 0.12f;
        [Tooltip("At full assistant influence the echo ping pitch is multiplied by this.")]
        [SerializeField] private float _echoPingMaxPitchMultiplier = 2.0f;

        private PropPlacer _placer;
        private AssistantSystem _assistant;
        private AlgorithmicGallery.SculptureSpawner _spawner;
        private AudioSource _echoAudioSource;
        private AudioClip _echoPingClip;
        private static readonly Color[] EchoPalette =
        {
            new Color(0.80f, 0.92f, 1.00f, 0.23f), // pale cyan
            new Color(0.93f, 0.78f, 1.00f, 0.24f), // soft violet
            new Color(1.00f, 0.83f, 0.86f, 0.22f), // rose
            new Color(0.78f, 1.00f, 0.90f, 0.24f), // mint
            new Color(1.00f, 0.94f, 0.75f, 0.22f), // warm cream
        };

        void Start()
        {
            _placer = FindFirstObjectByType<PropPlacer>();
            _assistant = FindFirstObjectByType<AssistantSystem>();
            _spawner = FindFirstObjectByType<AlgorithmicGallery.SculptureSpawner>();

            if (_placer != null)
                _placer.OnPropPlaced += OnPropPlaced;

            // Prepare whisper audio source
            _echoAudioSource = GetComponent<AudioSource>();
            if (_echoAudioSource == null)
                _echoAudioSource = gameObject.AddComponent<AudioSource>();
            _echoAudioSource.spatialBlend = 0f;
            _echoAudioSource.playOnAwake = false;
            _echoAudioSource.loop = false;
            _echoPingClip = CreateEchoPingClip();
        }

        void OnDestroy()
        {
            if (_placer != null)
                _placer.OnPropPlaced -= OnPropPlaced;
        }

        private void OnPropPlaced(bool isPlayer)
        {
            if (!isPlayer) return;

            var profile = FindFirstObjectByType<SandboxManager>()?.StyleProfile;
            if (profile == null || profile.History.Count == 0) return;

            var lastPlacement = profile.History[profile.History.Count - 1];
            float influence = _assistant != null ? _assistant.Influence : 0f;
            int echoCount = Mathf.RoundToInt(Mathf.Lerp(_baseEchoCount, _maxEchoCount, influence));

            for (int i = 0; i < echoCount; i++)
                StartCoroutine(SpawnEcho(lastPlacement, i));
        }

        private IEnumerator SpawnEcho(PlacementRecord record, int index)
        {
            if (_spawner == null) yield break;

            // Small stagger between multiple echoes
            if (index > 0)
                yield return new WaitForSeconds(index * 0.2f);

            // Whisper ping — pitch rises with assistant influence so it gets more dissonant over time
            if (_echoAudioSource != null && _echoPingClip != null)
            {
                float influence = _assistant != null ? Mathf.Clamp01(_assistant.Influence) : 0f;
                float pitch = Mathf.Lerp(1.0f, _echoPingMaxPitchMultiplier, influence);
                // Stagger each simultaneous echo slightly in pitch for a layering chorus feel
                pitch *= Mathf.Pow(1.015f, index);
                float prevPitch = _echoAudioSource.pitch;
                _echoAudioSource.pitch = pitch;
                _echoAudioSource.PlayOneShot(_echoPingClip, _echoPingBaseVolume);
                _echoAudioSource.pitch = prevPitch;
            }

            // Find the exact placed model so echoes are faithful to what the player just placed.
            var manifest = FindFirstObjectByType<SandboxManager>()?.Manifest;
            if (manifest == null) yield break;

            PropEntry prop = null;
            foreach (var p in manifest.All)
            {
                if (!string.IsNullOrEmpty(record.GlbPath) && p.GlbPath == record.GlbPath)
                {
                    prop = p;
                    break;
                }
            }
            // Fallback for records that don't carry the path.
            if (prop == null)
            {
                foreach (var p in manifest.All)
                {
                    if (p.Group == record.Group)
                    {
                        prop = p;
                        break;
                    }
                }
            }
            if (prop == null) yield break;

            Color echoTint = PickEchoColor(index);
            var task = _spawner.LoadModel(
                prop.GlbPath,
                parent: transform,
                addSculptureController: false,
                addCollider: false,
                normalizeScale: false,
                scaleMultiplier: PropScaler.ComputeScaleFactor(prop) * _echoScale);
            while (!task.IsCompleted) yield return null;

            var go = task.Result;
            if (go == null) yield break;

            // Disable all colliders
            foreach (var col in go.GetComponentsInChildren<Collider>(true))
                col.enabled = false;

            // Apply ghost material
            var runtimeMaterials = ApplyEchoAppearance(go, echoTint);

            // Position: offset from original placement
            float angle = Random.Range(0f, Mathf.PI * 2f);
            float radius = Random.Range(0.5f, _echoSpawnRadius);
            Vector3 offset = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            Vector3 startPos = record.Position + offset + Vector3.up * Random.Range(0.3f, 0.6f);
            go.transform.position = startPos;
            go.transform.rotation = Quaternion.Euler(
                Random.Range(-10f, 10f),
                Random.Range(0f, 360f),
                Random.Range(-10f, 10f));

            go.name = "_Echo";

            // Animate: float upward and fade out
            float elapsed = 0f;
            Vector3 basePos = startPos;
            Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
            float swirlSign = Random.value < 0.5f ? -1f : 1f;
            float swirlRadius = Random.Range(0.03f, 0.09f);
            float swirlSpeed = Random.Range(1.1f, 2.4f);

            while (elapsed < _echoDuration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / _echoDuration;

                // Float upward with spiral drift so echoes feel like they're being pulled away.
                float floatY = t * _echoFloatHeight + Mathf.Sin(elapsed * (2f + _echoFloatSpeed)) * 0.08f;
                float swirlPhase = elapsed * swirlSpeed * swirlSign;
                Vector3 swirlOffset = new Vector3(
                    Mathf.Cos(swirlPhase) * swirlRadius,
                    0f,
                    Mathf.Sin(swirlPhase) * swirlRadius
                );
                go.transform.position = basePos + Vector3.up * floatY + swirlOffset;
                go.transform.Rotate(Vector3.up, _echoRotateSpeed * Time.deltaTime);

                // Fade out alpha
                float alpha = Mathf.Lerp(echoTint.a, 0f, t * t);
                Color c = new Color(echoTint.r, echoTint.g, echoTint.b, alpha);
                foreach (var r in renderers)
                {
                    if (r == null) continue;
                    foreach (var mat in r.materials)
                    {
                        if (mat != null)
                            mat.color = c;
                    }
                }

                yield return null;
            }

            for (int i = 0; i < runtimeMaterials.Count; i++)
                if (runtimeMaterials[i] != null) Destroy(runtimeMaterials[i]);
            Destroy(go);
        }

        private List<Material> ApplyEchoAppearance(GameObject root, Color tint)
        {
            var runtime = new List<Material>();
            var baseMat = CreateEchoMaterial(tint);
            if (baseMat == null) return runtime;

            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var clone = new Material(baseMat);
                    mats[i] = clone;
                    runtime.Add(clone);
                }
                r.sharedMaterials = mats;
            }

            Destroy(baseMat);
            return runtime;
        }

        /// <summary>
        /// Synthesizes a soft bell ping: a 550 Hz fundamental with a 825 Hz fifth,
        /// shaped with a fast attack and slow exponential decay. Used as the echo whisper.
        /// </summary>
        private static AudioClip CreateEchoPingClip()
        {
            int sampleRate = 44100;
            float duration = 0.55f;
            float amplitude = 0.28f;
            float freq1 = 550f;  // fundamental
            float freq2 = 825f;  // perfect fifth — warm but not dissonant at rest

            int n = Mathf.RoundToInt(sampleRate * duration);
            float[] data = new float[n];
            float phase1 = 0f, phase2 = 0f;

            for (int i = 0; i < n; i++)
            {
                float k = i / (float)(n - 1);
                // Fast attack (first 3%), then exponential decay
                float env = k < 0.03f
                    ? k / 0.03f
                    : Mathf.Pow(1f - ((k - 0.03f) / 0.97f), 2.8f);

                phase1 += (2f * Mathf.PI * freq1) / sampleRate;
                phase2 += (2f * Mathf.PI * freq2) / sampleRate;
                float sample = (Mathf.Sin(phase1) + Mathf.Sin(phase2) * 0.4f) * amplitude * env;
                data[i] = Mathf.Clamp(sample, -1f, 1f);
            }

            var clip = AudioClip.Create("sfx_echo_ping", n, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private Material CreateEchoMaterial(Color tint)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("Standard");
            if (shader == null) return null;

            var mat = new Material(shader);
            mat.name = "EchoGhostMaterial";
            mat.color = tint;

            // Enable transparency
            mat.SetFloat("_Surface", 1f); // Transparent
            mat.SetFloat("_Blend", 0f);   // Alpha
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.renderQueue = 3000;
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            if (mat.HasProperty("_EmissionColor"))
                mat.SetColor("_EmissionColor", tint * 0.9f);

            return mat;
        }

        private Color PickEchoColor(int index)
        {
            if (EchoPalette.Length == 0)
                return _echoColor;

            int paletteIndex = (index + UnityEngine.Random.Range(0, EchoPalette.Length)) % EchoPalette.Length;
            Color baseColor = EchoPalette[paletteIndex];
            float jitter = UnityEngine.Random.Range(-0.04f, 0.04f);
            return new Color(
                Mathf.Clamp01(baseColor.r + jitter),
                Mathf.Clamp01(baseColor.g + jitter),
                Mathf.Clamp01(baseColor.b + jitter),
                baseColor.a
            );
        }
    }
}

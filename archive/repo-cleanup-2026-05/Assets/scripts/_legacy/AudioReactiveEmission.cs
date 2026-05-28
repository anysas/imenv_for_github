using UnityEngine;

namespace AlgorithmicGallery
{
    /// <summary>
    /// Drives emission intensity on nearby sculptures from audio spectrum data.
    /// Bass boosts emission power, beats trigger brief flash pulses.
    /// Radius-gated: only affects sculptures within audible range.
    /// </summary>
    public class AudioReactiveEmission : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private AudioSpectrumAnalyzer _analyzer;

        [Header("Emission Mapping")]
        [SerializeField] private float _bassEmissionBoost = 0.6f;
        [SerializeField] private float _beatFlashBoost = 1.2f;
        [SerializeField] private float _beatFlashDecaySpeed = 8f;

        [Header("Radius")]
        [SerializeField] private float _effectRadius = 15f;
        [SerializeField] private float _effectFalloff = 2f;

        private static readonly int EmissionPowerId = Shader.PropertyToID("_EmissionPower");
        private MaterialPropertyBlock _mpb;
        private SculptureController[] _sculptures;
        private float _sculptureRefreshTimer;
        private float _beatFlash;

        private void Start()
        {
            _mpb = new MaterialPropertyBlock();
            _sculptureRefreshTimer = 0f;

            if (_analyzer == null)
                _analyzer = FindFirstObjectByType<AudioSpectrumAnalyzer>();

            RefreshSculptureCache();
        }

        private void Update()
        {
            if (_analyzer == null)
                return;

            float dt = Time.deltaTime;

            // Refresh sculpture cache periodically
            _sculptureRefreshTimer -= dt;
            if (_sculptureRefreshTimer <= 0f)
            {
                RefreshSculptureCache();
                _sculptureRefreshTimer = 1f;
            }

            // Beat flash
            if (_analyzer.BeatDetected)
            {
                _beatFlash = 1f;
            }
            _beatFlash = Mathf.MoveTowards(_beatFlash, 0f, dt * _beatFlashDecaySpeed);

            // Apply to nearby sculptures
            float bassBoost = _analyzer.Bass * _bassEmissionBoost;
            float flashBoost = _beatFlash * _beatFlashBoost;
            float totalBoost = bassBoost + flashBoost;

            if (totalBoost < 0.001f || _sculptures == null)
                return;

            Vector3 sourcePos = transform.position;
            for (int i = 0; i < _sculptures.Length; i++)
            {
                var sc = _sculptures[i];
                if (sc == null || !sc.isActiveAndEnabled)
                    continue;

                float dist = Vector3.Distance(sourcePos, sc.transform.position);
                if (dist > _effectRadius)
                    continue;

                float attenuation = 1f - Mathf.Pow(dist / _effectRadius, _effectFalloff);
                float boost = totalBoost * attenuation;

                // Additively boost emission on sculpture renderers
                var renderers = sc.GetComponentsInChildren<Renderer>();
                for (int r = 0; r < renderers.Length; r++)
                {
                    if (renderers[r] == null)
                        continue;

                    renderers[r].GetPropertyBlock(_mpb);
                    float currentEmission = _mpb.GetFloat(EmissionPowerId);
                    _mpb.SetFloat(EmissionPowerId, currentEmission + boost);
                    renderers[r].SetPropertyBlock(_mpb);
                }
            }
        }

        private void RefreshSculptureCache()
        {
            _sculptures = FindObjectsByType<SculptureController>(FindObjectsSortMode.None);
        }
    }
}

using System.Collections;
using UnityEngine;

namespace AlgorithmicGallery.Corruption
{
    /// <summary>
    /// Controls the intake-room gate that opens into the hallway once the desire prompt
    /// has been committed. Designed to be scene-safe: if references are missing it no-ops.
    /// </summary>
    public class IntakeRoomGateController : MonoBehaviour
    {
        [SerializeField] private SandboxManager _sandbox;
        [SerializeField] private Transform _gate;
        [SerializeField] private Vector3 _openOffset = new Vector3(0f, 0f, -2.6f);
        [SerializeField] private float _openDuration = 1.2f;
        [SerializeField] private bool _disableColliderWhenOpen = true;
        [SerializeField] private Collider _gateCollider;
        [SerializeField] private AudioSource _audio;
        [SerializeField] private AudioClip _openSfx;
        [SerializeField] private Light _indicatorLight;
        [SerializeField] private Color _lockedColor = new Color(1f, 0.25f, 0.2f);
        [SerializeField] private Color _unlockedColor = new Color(0.55f, 1f, 0.72f);
        [SerializeField] private bool _openOnStartIfAlreadyUnlocked = true;

        [Tooltip("If true, this gate also opens when SandboxManager.OnSessionComplete fires. Used for the sandbox-exit door.")]
        [SerializeField] private bool _openOnSessionComplete = false;

        private Vector3 _closedLocalPos;
        private Coroutine _openRoutine;

        void Start()
        {
            if (_sandbox == null)
                _sandbox = FindFirstObjectByType<SandboxManager>();

            if (_gate == null)
                _gate = transform;

            if (_gateCollider == null && _gate != null)
                _gateCollider = _gate.GetComponent<Collider>();

            _closedLocalPos = _gate != null ? _gate.localPosition : Vector3.zero;
            SetIndicator(_lockedColor);

            if (_sandbox != null)
            {
                _sandbox.OnHallwayUnlocked.AddListener(HandleHallwayUnlocked);
                if (_openOnSessionComplete)
                    _sandbox.OnSessionComplete.AddListener(HandleHallwayUnlocked);
            }

            if (_openOnStartIfAlreadyUnlocked && _sandbox != null && _sandbox.HallwayUnlocked)
                HandleHallwayUnlocked();
        }

        void OnDestroy()
        {
            if (_sandbox != null)
            {
                _sandbox.OnHallwayUnlocked.RemoveListener(HandleHallwayUnlocked);
                if (_openOnSessionComplete)
                    _sandbox.OnSessionComplete.RemoveListener(HandleHallwayUnlocked);
            }
        }

        private void HandleHallwayUnlocked()
        {
            if (_openRoutine != null)
                StopCoroutine(_openRoutine);
            _openRoutine = StartCoroutine(OpenGate());
        }

        private IEnumerator OpenGate()
        {
            if (_gate == null) yield break;

            if (_audio != null && _openSfx != null)
                _audio.PlayOneShot(_openSfx, 0.8f);

            Vector3 start = _closedLocalPos;
            Vector3 end = _closedLocalPos + _openOffset;
            float t = 0f;
            float duration = Mathf.Max(0.01f, _openDuration);

            while (t < duration)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / duration);
                // Ease out cubic
                float eased = 1f - Mathf.Pow(1f - k, 3f);
                _gate.localPosition = Vector3.LerpUnclamped(start, end, eased);
                yield return null;
            }

            _gate.localPosition = end;
            if (_disableColliderWhenOpen && _gateCollider != null)
                _gateCollider.enabled = false;
            SetIndicator(_unlockedColor);
        }

        private void SetIndicator(Color c)
        {
            if (_indicatorLight != null)
                _indicatorLight.color = c;
        }
    }
}

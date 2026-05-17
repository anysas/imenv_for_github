using UnityEngine;

namespace AlgorithmicGallery.Corruption
{
    // Threshold collider at the hallway → sandbox transition.
    // OnTriggerEnter, calls SandboxManager.BeginSandbox().
    [RequireComponent(typeof(Collider))]
    public class HallwayTrigger : MonoBehaviour
    {
        [SerializeField] private SandboxManager _sandbox;
        [SerializeField] private string _playerTag = "Player";
        [Tooltip("If true, this object disables itself after triggering once.")]
        [SerializeField] private bool _oneShot = true;

        void Awake()
        {
            var col = GetComponent<Collider>();
            col.isTrigger = true;
        }

        void Start()
        {
            if (_sandbox == null) _sandbox = FindFirstObjectByType<SandboxManager>();
        }

        void OnTriggerEnter(Collider other)
        {
            if (!IsPlayerCollider(other)) return;
            TryBeginSandbox(other);
        }

        void OnTriggerStay(Collider other)
        {
            // Player may already be inside this volume when the hallway unlocks (no second OnTriggerEnter).
            if (!IsPlayerCollider(other)) return;
            TryBeginSandbox(other);
        }

        private bool IsPlayerCollider(Collider other)
        {
            if (other == null) return false;
            return other.CompareTag(_playerTag)
                || other.GetComponentInParent<SimplePlayerRig>() != null
                || other.GetComponentInParent<CharacterController>() != null
                || other.GetComponentInParent<PlayerMovement>() != null;
        }

        private void TryBeginSandbox(Collider other)
        {
            if (_sandbox == null) return;

            if (!_sandbox.HallwayUnlocked)
            {
                Debug.Log("[HallwayTrigger] Hallway still locked — submit desire at terminal first.");
                return;
            }

            if (_sandbox.SandboxActive)
                return;

            _sandbox.BeginSandbox();

            if (_oneShot && _sandbox.SandboxActive)
                gameObject.SetActive(false);
        }
    }
}

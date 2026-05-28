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
            // Be lenient about tag — also fire if the entering object has SimplePlayerRig in parents
            bool isPlayer = other.CompareTag(_playerTag)
                || other.GetComponentInParent<SimplePlayerRig>() != null
                || other.GetComponentInParent<CharacterController>() != null;

            if (!isPlayer) return;

            bool triggered = _sandbox != null && _sandbox.HallwayUnlocked;
            if (triggered)
            {
                _sandbox.BeginSandbox();
            }
            else
            {
                Debug.Log("[HallwayTrigger] Hallway still locked — submit desire at terminal first.");
            }

            if (_oneShot && triggered)
                gameObject.SetActive(false);
        }
    }
}

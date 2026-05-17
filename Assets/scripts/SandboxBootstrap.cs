using UnityEngine;

namespace AlgorithmicGallery.Corruption
{
    // One-component entry point. Drop this on an empty GameObject in a fresh scene and play.
    // It creates a SandboxManager (which then auto-creates everything else).
    [DefaultExecutionOrder(-100)]
    public class SandboxBootstrap : MonoBehaviour
    {
        [Tooltip("If true, ensures a SandboxManager exists at scene start.")]
        [SerializeField] private bool _ensureSandboxManager = true;

        void Awake()
        {
            if (_ensureSandboxManager && FindFirstObjectByType<SandboxManager>() == null)
            {
                var go = new GameObject("SandboxManager");
                go.AddComponent<SandboxManager>();
                Debug.Log("[SandboxBootstrap] Created SandboxManager.");
            }
        }
    }
}

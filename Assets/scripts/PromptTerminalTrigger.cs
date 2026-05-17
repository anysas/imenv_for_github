using UnityEngine;

namespace AlgorithmicGallery.Corruption
{
    /// <summary>
    /// Attach to an intake terminal trigger volume.
    /// When the player approaches, this opens ThemeSelectionUI.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class PromptTerminalTrigger : MonoBehaviour
    {
        [SerializeField] private ThemeSelectionUI _themeSelectionUI;
        [SerializeField] private string _playerTag = "Player";
        [SerializeField] private bool _oneShot = false;

        void Awake()
        {
            var col = GetComponent<Collider>();
            col.isTrigger = true;
        }

        void Start()
        {
            EnsureThemeSelectionUi();
        }

        void OnTriggerEnter(Collider other)
        {
            TryOpenPromptHud(other);
        }

        void OnTriggerStay(Collider other)
        {
            // Backup for setups where Enter can be missed due physics timing.
            TryOpenPromptHud(other);
        }

        private void TryOpenPromptHud(Collider other)
        {
            bool isPlayer = other.CompareTag(_playerTag)
                || other.GetComponentInParent<SimplePlayerRig>() != null
                || other.GetComponentInParent<CharacterController>() != null;
            if (!isPlayer) return;

            EnsureThemeSelectionUi();
            if (_themeSelectionUI == null || _themeSelectionUI.HasSelected)
                return;

            if (!_themeSelectionUI.IsOpen)
                _themeSelectionUI.OpenPromptHud();

            if (_oneShot)
                gameObject.SetActive(false);
        }

        private void EnsureThemeSelectionUi()
        {
            if (_themeSelectionUI != null)
                return;

            _themeSelectionUI = FindFirstObjectByType<ThemeSelectionUI>();
            if (_themeSelectionUI != null)
                return;

            var uiGo = new GameObject("ThemeSelectionUI");
            _themeSelectionUI = uiGo.AddComponent<ThemeSelectionUI>();
        }
    }
}

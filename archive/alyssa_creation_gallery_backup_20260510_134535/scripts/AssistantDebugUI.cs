using System.Linq;
using UnityEngine;

namespace AlgorithmicGallery.Corruption
{
    // IMGUI debug overlay. Toggle with F1.
    // Replaces RecommendationDebugUI from v1.
    public class AssistantDebugUI : MonoBehaviour
    {
        [SerializeField] private SandboxManager _sandbox;
        [SerializeField] private AssistantSystem _assistant;
        [SerializeField] private HotbarController _hotbar;

        private bool _visible;

        void Start()
        {
            // Auto-resolve references when added at runtime by SandboxManager bootstrap
            if (_sandbox == null)   _sandbox   = FindFirstObjectByType<SandboxManager>();
            if (_assistant == null) _assistant = FindFirstObjectByType<AssistantSystem>();
            if (_hotbar == null)    _hotbar    = FindFirstObjectByType<HotbarController>();
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.F1))
                _visible = !_visible;
        }

        void OnGUI()
        {
            if (!_visible) return;

            GUI.color = new Color(0f, 0f, 0f, 0.75f);
            GUI.DrawTexture(new Rect(10, 10, 340, 400), Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUILayout.BeginArea(new Rect(16, 16, 328, 390));

            GUILayout.Label("=== ASSISTANT DEBUG (F1) ===");
            GUILayout.Space(6);

            if (_assistant != null)
            {
                GUILayout.Label($"Session time : {_assistant.SessionTime:F1}s / 90s");
                GUILayout.Label($"Influence    : {_assistant.Influence:F3}");
                GUILayout.Label($"Phase        : {_assistant.Phase}");
                GUILayout.Label($"Running      : {_assistant.IsRunning}");
            }

            GUILayout.Space(6);

            if (_sandbox?.StyleProfile != null)
            {
                var sp = _sandbox.StyleProfile;
                GUILayout.Label($"Player placements : {sp.PlayerPlacementCount}");
                GUILayout.Label($"Assistant places  : {sp.AssistantPlacementCount}");
                GUILayout.Label($"Avg cadence       : {sp.AverageCadenceSeconds():F1}s");

                var groups = sp.DominantGroups(3);
                GUILayout.Label($"Top groups        : {string.Join(", ", groups)}");

                var tags = sp.DominantTags(5);
                GUILayout.Label($"Top tags          : {string.Join(", ", tags)}");

                GUILayout.Space(4);
                GUILayout.Label("Group counts:");
                foreach (var kv in sp.GroupCounts.OrderByDescending(x => x.Value).Take(6))
                    GUILayout.Label($"  {kv.Key,-18} {kv.Value}");
            }

            GUILayout.Space(6);

            if (_hotbar != null)
            {
                GUILayout.Label($"Active slot  : {_hotbar.ActiveSlot}");
                GUILayout.Label("Slots:");
                for (int i = 0; i < HotbarController.SlotCount; i++)
                {
                    var p = _hotbar.Slots[i];
                    string marker = i == _hotbar.ActiveSlot ? ">" : " ";
                    GUILayout.Label($"  {marker} [{i+1}] {(p != null ? p.DisplayName + " (" + p.Group + ")" : "empty")}");
                }
            }

            GUILayout.EndArea();
        }
    }
}

using UnityEngine;
using AlgorithmicGallery.Recommendation;
using System.Linq;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace AlgorithmicGallery
{
    /// <summary>
    /// Runtime debug overlay using Unity's IMGUI (OnGUI).
    /// Displays current phase, progress, statistics, and user preferences.
    /// Toggle with F1 key.
    /// </summary>
    public class RecommendationDebugUI : MonoBehaviour
    {
        [SerializeField]
        private GalleryManager _galleryManager;

        [SerializeField]
        private KeyCode _toggleKey = KeyCode.F1;

        private bool _isVisible = true;

        private void Start()
        {
            if (_galleryManager == null)
            {
                _galleryManager = FindFirstObjectByType<GalleryManager>();
            }
        }

        private void Update()
        {
            if (IsTogglePressed())
            {
                _isVisible = !_isVisible;
            }
        }

        private void OnGUI()
        {
            if (!_isVisible || _galleryManager == null)
                return;

            GUILayout.BeginArea(new Rect(10, 10, 400, 600), "Algorithmic Gallery Debug", GUI.skin.box);

            // Phase information
            DrawPhaseInfo();

            GUILayout.Space(10);

            // Statistics
            DrawStatistics();

            GUILayout.Space(10);

            // Preferences
            DrawPreferences();

            GUILayout.FlexibleSpace();

            GUILayout.Label("Press F1 to toggle debug UI", GUI.skin.label);

            GUILayout.EndArea();
        }

        private void DrawPhaseInfo()
        {
            ArcPhase phase = _galleryManager.CurrentPhase;
            float progress = _galleryManager.PhaseProgress;

            GUILayout.Label("CURRENT PHASE", GUI.skin.box);

            // Phase name with color
            string phaseName = phase.ToString();
            Color phaseColor = GetPhaseColor(phase);
            GUI.backgroundColor = phaseColor;
            GUILayout.Label(phaseName, GUI.skin.box);
            GUI.backgroundColor = Color.white;

            // Phase progress bar
            GUILayout.Label($"Phase Progress: {progress:P0}");
            DrawProgressBar(progress, 300, 20);
        }

        private void DrawStatistics()
        {
            GUILayout.Label("SESSION STATISTICS", GUI.skin.box);

            GUILayout.Label($"Sculptures Viewed: {_galleryManager.SculpturesViewed}");
            GUILayout.Label($"Session Time: {_galleryManager.SessionElapsedTime:F1}s");
            GUILayout.Label($"Average Dwell: {_galleryManager.AverageDwellTime * 1000f:F0}ms");
        }

        private void DrawPreferences()
        {
            GUILayout.Label("USER PROFILE", GUI.skin.box);

            UserProfile profile = _galleryManager.CurrentProfile;
            if (profile != null && profile.PreferenceWeights != null)
            {
                // Show top 5 preferences
                int count = 0;
                foreach (var kvp in profile.PreferenceWeights.OrderByDescending(x => x.Value))
                {
                    if (count >= 5)
                        break;

                    string tagName = kvp.Key;
                    float weight = kvp.Value;

                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"{tagName}", GUILayout.Width(100));
                    DrawBar(weight, 150, 15);
                    GUILayout.Label($"{weight:F2}", GUILayout.Width(50));
                    GUILayout.EndHorizontal();

                    count++;
                }
            }
            else
            {
                GUILayout.Label("(No profile data yet)");
            }
        }

        private void DrawProgressBar(float value, float width, float height)
        {
            Rect barRect = GUILayoutUtility.GetRect(width, height);
            GUI.Box(barRect, "");
            Rect fillRect = new Rect(barRect.x, barRect.y, barRect.width * value, barRect.height);
            GUI.Box(fillRect, "");
        }

        private void DrawBar(float value, float width, float height)
        {
            Rect barRect = GUILayoutUtility.GetRect(width, height);
            GUI.Box(barRect, "");
            Rect fillRect = new Rect(barRect.x, barRect.y, barRect.width * Mathf.Clamp01(value), barRect.height);
            GUI.Box(fillRect, "");
        }

        private Color GetPhaseColor(ArcPhase phase)
        {
            return phase switch
            {
                ArcPhase.Fascination => new Color(0.2f, 0.8f, 1f), // Cyan
                ArcPhase.Recognition => new Color(1f, 0.8f, 0.2f), // Yellow
                ArcPhase.Unease => new Color(1f, 0.2f, 0.2f),       // Red
                _ => Color.gray
            };
        }

        private bool IsTogglePressed()
        {
            bool pressed = false;
#if ENABLE_LEGACY_INPUT_MANAGER
            pressed = pressed || Input.GetKeyDown(_toggleKey);
#endif
#if ENABLE_INPUT_SYSTEM
            pressed = pressed || IsInputSystemTogglePressed();
#endif
            return pressed;
        }

#if ENABLE_INPUT_SYSTEM
        private bool IsInputSystemTogglePressed()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return false;

            return _toggleKey switch
            {
                KeyCode.F1 => keyboard.f1Key.wasPressedThisFrame,
                KeyCode.F2 => keyboard.f2Key.wasPressedThisFrame,
                KeyCode.F3 => keyboard.f3Key.wasPressedThisFrame,
                KeyCode.F4 => keyboard.f4Key.wasPressedThisFrame,
                KeyCode.F5 => keyboard.f5Key.wasPressedThisFrame,
                KeyCode.F6 => keyboard.f6Key.wasPressedThisFrame,
                KeyCode.F7 => keyboard.f7Key.wasPressedThisFrame,
                KeyCode.F8 => keyboard.f8Key.wasPressedThisFrame,
                KeyCode.F9 => keyboard.f9Key.wasPressedThisFrame,
                KeyCode.F10 => keyboard.f10Key.wasPressedThisFrame,
                KeyCode.F11 => keyboard.f11Key.wasPressedThisFrame,
                KeyCode.F12 => keyboard.f12Key.wasPressedThisFrame,
                KeyCode.Tab => keyboard.tabKey.wasPressedThisFrame,
                KeyCode.Escape => keyboard.escapeKey.wasPressedThisFrame,
                _ => false
            };
        }
#endif
    }
}

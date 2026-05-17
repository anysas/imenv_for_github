using UnityEngine;

namespace AlgorithmicGallery.Corruption
{
    // Visualizes the player's placement density grid and recent placements in the Scene view.
    // Add to the SandboxManager GameObject (or anywhere — auto-finds the SandboxManager).
    public class StyleProfileGizmo : MonoBehaviour
    {
        [SerializeField] private SandboxManager _sandbox;
        [SerializeField] private Color _emptyCell  = new Color(0.2f, 0.4f, 1f, 0.05f);
        [SerializeField] private Color _filledCell = new Color(1f,   0.4f, 0.2f, 0.35f);
        [SerializeField] private Color _placementMarker = new Color(0.2f, 1f, 0.4f, 0.8f);
        [SerializeField] private float _gridWorldSize = 20f;
        [SerializeField] private int _resolution = 10;

        void OnDrawGizmos()
        {
            if (!Application.isPlaying) return;
            if (_sandbox == null) _sandbox = FindFirstObjectByType<SandboxManager>();
            if (_sandbox?.StyleProfile == null) return;

            // We can't read the private density grid directly; instead, derive it from history.
            // For perf in editor, re-bin all placements every gizmo call (history is small per session).
            int[,] grid = new int[_resolution, _resolution];
            int maxCount = 0;
            float cellSize = _gridWorldSize / _resolution;
            float half = _gridWorldSize / 2f;
            Vector3 origin = _sandbox.SandboxFloor != null ? _sandbox.SandboxFloor.position : Vector3.zero;

            foreach (var rec in _sandbox.StyleProfile.History)
            {
                int x = Mathf.Clamp(Mathf.FloorToInt(((rec.Position.x - origin.x) + half) / _gridWorldSize * _resolution), 0, _resolution - 1);
                int z = Mathf.Clamp(Mathf.FloorToInt(((rec.Position.z - origin.z) + half) / _gridWorldSize * _resolution), 0, _resolution - 1);
                grid[x, z]++;
                if (grid[x, z] > maxCount) maxCount = grid[x, z];
            }

            for (int x = 0; x < _resolution; x++)
            {
                for (int z = 0; z < _resolution; z++)
                {
                    Vector3 center = new Vector3(
                        origin.x - half + (x + 0.5f) * cellSize,
                        origin.y + 0.02f,
                        origin.z - half + (z + 0.5f) * cellSize);

                    float t = maxCount > 0 ? (float)grid[x, z] / maxCount : 0f;
                    Gizmos.color = Color.Lerp(_emptyCell, _filledCell, t);
                    Gizmos.DrawCube(center, new Vector3(cellSize * 0.92f, 0.01f, cellSize * 0.92f));
                }
            }

            // Recent placement markers
            Gizmos.color = _placementMarker;
            int n = _sandbox.StyleProfile.History.Count;
            for (int i = Mathf.Max(0, n - 20); i < n; i++)
            {
                var rec = _sandbox.StyleProfile.History[i];
                Gizmos.DrawWireSphere(rec.Position + Vector3.up * 0.05f, 0.18f);
            }
        }
    }
}

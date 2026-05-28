using UnityEngine;

namespace AlgorithmicGallery.Corruption
{
    /// <summary>
    /// Resolves scene objects by hierarchy path (e.g. <c>glass-case/mainPedestal</c>).
    /// </summary>
    public static class SceneHierarchyLookup
    {
        public static Transform FindTransform(string hierarchyPath, string fallbackObjectName = null)
        {
            Transform found = FindByHierarchyPath(hierarchyPath);
            if (found != null)
                return found;

            if (string.IsNullOrWhiteSpace(fallbackObjectName))
                return null;

            var go = GameObject.Find(fallbackObjectName);
            return go != null ? go.transform : null;
        }

        public static Transform FindByHierarchyPath(string hierarchyPath)
        {
            if (string.IsNullOrWhiteSpace(hierarchyPath))
                return null;

            string[] parts = hierarchyPath.Split('/');
            if (parts.Length == 0 || string.IsNullOrWhiteSpace(parts[0]))
                return null;

            var root = GameObject.Find(parts[0].Trim());
            if (root == null)
                return null;

            Transform current = root.transform;
            for (int i = 1; i < parts.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(parts[i]))
                    continue;

                current = current.Find(parts[i].Trim());
                if (current == null)
                    return null;
            }

            return current;
        }

        /// <summary>World point on the top of the pedestal mesh (bounds center XZ, max Y).</summary>
        public static bool TryGetPedestalTopSurface(Transform pedestal, out Vector3 topSurface)
        {
            topSurface = pedestal != null ? pedestal.position : Vector3.zero;
            if (pedestal == null)
                return false;

            var col = pedestal.GetComponent<Collider>();
            if (col != null)
            {
                Bounds b = col.bounds;
                topSurface = new Vector3(b.center.x, b.max.y, b.center.z);
                return true;
            }

            var renderers = pedestal.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
                return false;

            Bounds bounds = default;
            bool hasBounds = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !renderer.enabled)
                    continue;

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            if (!hasBounds)
                return false;

            topSurface = new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
            return true;
        }
    }
}

using UnityEngine;

namespace AlgorithmicGallery.Corruption
{
    /// <summary>
    /// Central freeze for first-person locomotion during fullscreen UI (title, intake terminal, etc.).
    /// Handles both SimplePlayerRig and legacy PlayerMovement if present.
    /// </summary>
    public static class PlayerInputFreeze
    {
        public static void FreezePlayerLocomotion()
        {
            foreach (var rig in Object.FindObjectsByType<SimplePlayerRig>(FindObjectsSortMode.None))
            {
                if (rig != null)
                    rig.enabled = false;
            }

            foreach (var pm in Object.FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None))
            {
                if (pm != null)
                    pm.enabled = false;
            }
        }

        public static void RestorePlayerLocomotion()
        {
            foreach (var rig in Object.FindObjectsByType<SimplePlayerRig>(FindObjectsSortMode.None))
            {
                if (rig != null)
                    rig.enabled = true;
            }

            foreach (var pm in Object.FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None))
            {
                if (pm != null)
                    pm.enabled = true;
            }
        }
    }
}

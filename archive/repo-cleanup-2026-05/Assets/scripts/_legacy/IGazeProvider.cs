using UnityEngine;

namespace AlgorithmicGallery
{
    /// <summary>
    /// Interface for abstracting gaze input across different platforms (desktop, VR, etc).
    /// Implementations provide a ray representing where the user is looking.
    /// </summary>
    public interface IGazeProvider
    {
        /// <summary>
        /// Returns the current gaze ray in world space.
        /// </summary>
        Ray GetGazeRay();

        /// <summary>
        /// Whether this gaze provider is active and available for this platform.
        /// </summary>
        bool IsAvailable { get; }
    }
}

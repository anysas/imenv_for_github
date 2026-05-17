using UnityEngine;

namespace AlgorithmicGallery
{
    /// <summary>
    /// Routes active room sculpture slots into the GalleryManager spawn system.
    /// </summary>
    public class RoomGalleryBridge : MonoBehaviour
    {
        [SerializeField]
        private GalleryManager _galleryManager;

        private void Awake()
        {
            if (_galleryManager == null)
            {
                _galleryManager = FindFirstObjectByType<GalleryManager>();
            }

            var galleries = FindObjectsByType<GalleryManager>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            if (galleries.Length != 1)
            {
                Debug.LogWarning($"RoomGalleryBridge: Expected exactly one GalleryManager in scene, found {galleries.Length}.");
            }

            if (_galleryManager == null)
            {
                Debug.LogWarning("RoomGalleryBridge: Missing GalleryManager reference. Room pedestal binding will fail.");
            }
        }

        public void BindRoom(RoomRuntime room, bool clearSlotState = true, bool spawnImmediately = true)
        {
            if (_galleryManager == null || room == null)
                return;

            _galleryManager.SetPedestalSlots(room.GetSculptureSlots(), clearSlotState, spawnImmediately);
        }
    }
}

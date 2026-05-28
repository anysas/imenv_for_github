using UnityEngine;

namespace AlgorithmicGallery
{
    /// <summary>
    /// Forwards trigger entry events to the linear gallery controller.
    /// </summary>
    public class LinearRoomTrigger : MonoBehaviour
    {
        private LinearGalleryController _controller;
        private int _roomIndex = -1;

        public void Configure(LinearGalleryController controller, int roomIndex)
        {
            _controller = controller;
            _roomIndex = roomIndex;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_controller == null || _roomIndex < 0)
                return;

            _controller.OnRoomTriggerEntered(_roomIndex, other);
        }
    }
}

using UnityEngine;

namespace AlgorithmicGallery.Corruption
{
    // Self-contained first-person rig: CharacterController + WASD + mouse look.
    // Builds a Camera child if none exists. Drop on an empty GameObject and play.
    public class SimplePlayerRig : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField] private float _moveSpeed = 4.5f;
        [SerializeField] private float _gravity = 18f;

        [Header("Look")]
        [SerializeField] private float _lookSensitivity = 2f;
        [SerializeField] private float _maxPitch = 80f;
        [SerializeField] private bool _captureCursor = true;

        [Header("Camera")]
        [SerializeField] private float _cameraHeight = 1.6f;

        private CharacterController _controller;
        private Camera _camera;
        private float _pitch;
        private float _verticalVelocity;

        void Awake()
        {
            _controller = GetComponent<CharacterController>();
            if (_controller == null)
            {
                _controller = gameObject.AddComponent<CharacterController>();
                _controller.height = 1.8f;
                _controller.radius = 0.35f;
                _controller.center = new Vector3(0f, 0.9f, 0f);
            }

            _camera = GetComponentInChildren<Camera>();
            if (_camera == null)
            {
                var camGO = new GameObject("PlayerCamera");
                camGO.transform.SetParent(transform);
                camGO.transform.localPosition = new Vector3(0f, _cameraHeight, 0f);
                _camera = camGO.AddComponent<Camera>();
                camGO.AddComponent<AudioListener>();
                _camera.tag = "MainCamera";
            }

            // Make sure we have an EventSystem so UI raycast filtering works in PropPlacer
            if (UnityEngine.EventSystems.EventSystem.current == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<UnityEngine.EventSystems.EventSystem>();
                es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            }
        }

        void Start()
        {
            if (_captureCursor)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        void Update()
        {
            HandleLook();
            HandleMove();

            // Toggle cursor with Tab for testing
            if (Input.GetKeyDown(KeyCode.Tab))
            {
                Cursor.lockState = Cursor.lockState == CursorLockMode.Locked
                    ? CursorLockMode.None : CursorLockMode.Locked;
                Cursor.visible = Cursor.lockState != CursorLockMode.Locked;
            }
        }

        private void HandleLook()
        {
            if (Cursor.lockState != CursorLockMode.Locked) return;
            float yaw = Input.GetAxisRaw("Mouse X") * _lookSensitivity;
            float pitchDelta = Input.GetAxisRaw("Mouse Y") * _lookSensitivity;
            transform.Rotate(0f, yaw, 0f);
            _pitch = Mathf.Clamp(_pitch - pitchDelta, -_maxPitch, _maxPitch);
            if (_camera != null)
                _camera.transform.localEulerAngles = new Vector3(_pitch, 0f, 0f);
        }

        private void HandleMove()
        {
            // Don't move when cursor is free (UI active, prompt selection, end card)
            if (Cursor.lockState != CursorLockMode.Locked)
            {
                // Still apply gravity so the player doesn't float
                if (_controller.isGrounded && _verticalVelocity < 0f)
                    _verticalVelocity = -2f;
                _verticalVelocity -= _gravity * Time.deltaTime;
                _controller.Move(Vector3.up * _verticalVelocity * Time.deltaTime);
                return;
            }

            float h = Input.GetAxisRaw("Horizontal");
            float v = Input.GetAxisRaw("Vertical");
            Vector3 wishDir = (transform.right * h + transform.forward * v).normalized;
            float speed = _moveSpeed;

            if (_controller.isGrounded && _verticalVelocity < 0f)
                _verticalVelocity = -2f;

            _verticalVelocity -= _gravity * Time.deltaTime;

            Vector3 motion = wishDir * speed + Vector3.up * _verticalVelocity;
            _controller.Move(motion * Time.deltaTime);
        }
    }
}

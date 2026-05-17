using System.Collections;
using UnityEngine;

namespace AlgorithmicGallery
{
    /// <summary>
    /// Lightweight door animation helper for prototype room transitions.
    /// Supports local-position sliding and local-rotation pivoting.
    /// </summary>
    public class RoomDoor : MonoBehaviour
    {
        [SerializeField]
        private Transform _doorTransform;
        [SerializeField]
        private bool _animatePosition = true;
        [SerializeField]
        private bool _animateRotation;
        [SerializeField]
        private Vector3 _openLocalPosition;
        [SerializeField]
        private Vector3 _closedLocalPosition;
        [SerializeField]
        private Vector3 _openLocalEuler;
        [SerializeField]
        private Vector3 _closedLocalEuler;
        [SerializeField]
        private float _transitionDuration = 0.45f;
        [SerializeField]
        private bool _startClosed;

        private bool _isClosed;
        private Coroutine _transitionRoutine;

        private void Awake()
        {
            if (_doorTransform == null)
            {
                _doorTransform = transform;
            }

            if (_openLocalPosition == Vector3.zero && _closedLocalPosition == Vector3.zero)
            {
                _openLocalPosition = _doorTransform.localPosition;
                _closedLocalPosition = _doorTransform.localPosition;
            }

            if (_openLocalEuler == Vector3.zero && _closedLocalEuler == Vector3.zero)
            {
                _openLocalEuler = _doorTransform.localEulerAngles;
                _closedLocalEuler = _doorTransform.localEulerAngles;
            }

            SetClosed(_startClosed, instant: true);
        }

        public void SetClosed(bool closed, bool instant = false)
        {
            _isClosed = closed;
            if (_transitionRoutine != null)
            {
                StopCoroutine(_transitionRoutine);
                _transitionRoutine = null;
            }

            if (instant || _transitionDuration <= 0.001f || _doorTransform == null)
            {
                ApplyState(closed ? 1f : 0f);
                return;
            }

            _transitionRoutine = StartCoroutine(AnimateDoor(closed));
        }

        private IEnumerator AnimateDoor(bool closed)
        {
            float duration = Mathf.Max(0.01f, _transitionDuration);
            float start = closed ? 0f : 1f;
            float end = closed ? 1f : 0f;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float alpha = Mathf.Lerp(start, end, t);
                ApplyState(alpha);
                yield return null;
            }

            ApplyState(end);
            _transitionRoutine = null;
        }

        private void ApplyState(float closedAmount)
        {
            if (_doorTransform == null)
                return;

            if (_animatePosition)
            {
                _doorTransform.localPosition = Vector3.Lerp(_openLocalPosition, _closedLocalPosition, closedAmount);
            }

            if (_animateRotation)
            {
                Quaternion openRot = Quaternion.Euler(_openLocalEuler);
                Quaternion closedRot = Quaternion.Euler(_closedLocalEuler);
                _doorTransform.localRotation = Quaternion.Slerp(openRot, closedRot, closedAmount);
            }
        }

        public bool IsClosed => _isClosed;
    }
}

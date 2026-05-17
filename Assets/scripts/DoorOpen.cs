using UnityEngine;
using System.Collections;

namespace AlgorithmicGallery.Corruption
{
    public class DoorOpen : MonoBehaviour
    {
        [Header("Door Settings")]
        [Tooltip("Drag your Door object (with the Animator) here. If empty, will try to get Animator from this GameObject.")]
        public Animator doorAnimator;

        [Header("Close Settings")]
        [Tooltip("Delay (in seconds) before closing the door after player enters threshold")]
        [SerializeField] private float _delayBeforeDoorClose = 2f;
        [Tooltip("If true, the door can auto-close when player enters the trigger.")]
        [SerializeField] private bool _autoCloseOnTriggerEnter = true;
        [Tooltip("If true, door stays open after session-complete reopening (prevents slamming shut on walkback).")]
        [SerializeField] private bool _keepOpenAfterSessionComplete = true;

        [Header("Audio Settings")]
        public AudioClip doorOpenSound;
        public AudioClip doorCloseSound;
        [Tooltip("If not assigned, will try to get AudioSource from this GameObject")]
        public AudioSource audioSource;

        [Header("Animation Settings")]
        [Tooltip("Optional delay (in seconds) before the door animation plays after prompt selection")]
        [SerializeField] private float _delayBeforeDoorOpen = 0f;

        [Header("Open Triggers")]
        [Tooltip("Open the door after the player selects a prompt and the prompt UI fades out. Drives the entry beat.")]
        [SerializeField] private bool _openOnPromptSelected = true;
        [Tooltip("Re-open the door when SandboxManager.OnSessionComplete fires. Drives the walkback beat for a single shared door.")]
        [SerializeField] private bool _openOnSessionComplete = true;
        [Tooltip("Optional explicit reference. If left null and OnSessionComplete is enabled, the door auto-finds the SandboxManager at runtime.")]
        [SerializeField] private SandboxManager _sandbox;

        private ThemeSelectionUI _themeUI;
        private bool _doorHasOpened;
        private bool _isDoorOpen;
        private Coroutine _doorOpenRoutine;
        private Coroutine _doorCloseRoutine;
        private bool _isSubscribed;
        private bool _promptWasSelected;
        private bool _isSandboxSubscribed;
        private bool _openedFromSessionComplete;

        void Start()
        {
            Debug.Log("Door Script: Starting...");
            
            // 1. Check if we have the Animator
            if (doorAnimator == null)
            {
                doorAnimator = GetComponent<Animator>();
                Debug.Log("Door Script: Found Animator on this GameObject");
            }
            else
            {
                Debug.Log("Door Script: Animator already assigned in Inspector");
            }
            
            // Check AudioSource
            if (audioSource == null)
            {
                audioSource = GetComponent<AudioSource>();
                if (audioSource == null)
                {
                    audioSource = gameObject.AddComponent<AudioSource>();
                    Debug.Log("Door Script: Added AudioSource component");
                }
                else
                {
                    Debug.Log("Door Script: Found AudioSource on this GameObject");
                }
            }
            else
            {
                Debug.Log("Door Script: AudioSource already assigned in Inspector");
            }
            
            // CRITICAL: Clear any pending Open triggers to prevent auto-play
            if (doorAnimator != null)
            {
                doorAnimator.ResetTrigger("Open");
                doorAnimator.ResetTrigger("Close");
                Debug.Log("Door Script: Cleared any pending Open and Close triggers");
            }

            // 2. Find and subscribe to the configured open triggers.
            if (_openOnPromptSelected)
                FindAndSubscribeToUI();
            if (_openOnSessionComplete)
                FindAndSubscribeToSandbox();

            Debug.Log($"Door Script: Initialized. promptOpen={_openOnPromptSelected}, sessionEndOpen={_openOnSessionComplete}, _doorHasOpened={_doorHasOpened}, _isDoorOpen={_isDoorOpen}");
        }

        void Update()
        {
            // Keep trying to find the dependencies if we haven't yet (scene load order safety).
            if (_openOnPromptSelected && _themeUI == null && !_isSubscribed)
                FindAndSubscribeToUI();
            if (_openOnSessionComplete && _sandbox == null && !_isSandboxSubscribed)
                FindAndSubscribeToSandbox();
        }

        private void FindAndSubscribeToUI()
        {
            if (_themeUI != null && _isSubscribed) return;

            _themeUI = FindFirstObjectByType<ThemeSelectionUI>();
            if (_themeUI != null && !_isSubscribed)
            {
                Debug.Log("Door Script: Found ThemeSelectionUI, subscribing to events.");
                _themeUI.OnPromptSelected += OnPromptSelected;
                _themeUI.OnUiFadeOutComplete += OnUiFadeOutComplete;
                _isSubscribed = true;
            }
        }

        private void FindAndSubscribeToSandbox()
        {
            if (_sandbox == null)
                _sandbox = FindFirstObjectByType<SandboxManager>();

            if (_sandbox != null && !_isSandboxSubscribed)
            {
                Debug.Log("Door Script: Found SandboxManager, subscribing to OnSessionComplete.");
                _sandbox.OnSessionComplete.AddListener(OnSessionComplete);
                _isSandboxSubscribed = true;
            }
        }

        private void OnSessionComplete()
        {
            Debug.Log("Door Script: SandboxManager.OnSessionComplete received — re-opening door for walkback.");
            _openedFromSessionComplete = _keepOpenAfterSessionComplete;

            // If the door is already open (player hasn't crossed the threshold yet), leave it alone.
            if (_isDoorOpen)
            {
                Debug.Log("Door Script: Door already open at session end — no re-open needed.");
                return;
            }

            // Cancel any in-flight close routine so it doesn't slam shut on us mid-reopen.
            if (_doorCloseRoutine != null)
            {
                StopCoroutine(_doorCloseRoutine);
                _doorCloseRoutine = null;
            }

            // Reset gates so DelayedDoorOpen will run a fresh open pass.
            // _promptWasSelected stays true (it bypasses the safety check inside DelayedDoorOpen).
            _doorHasOpened = false;
            _promptWasSelected = true;

            if (_doorOpenRoutine != null)
                StopCoroutine(_doorOpenRoutine);

            _doorHasOpened = true;
            _doorOpenRoutine = StartCoroutine(DelayedDoorOpen());
        }

        private void OnPromptSelected(PromptDefinition prompt)
        {
            Debug.Log("Door Script: User selected a prompt, marking for door open on UI fade.");
            _promptWasSelected = true;
            _openedFromSessionComplete = false;
        }

        private void OnUiFadeOutComplete()
        {
            Debug.Log("Door Script: UI fade out complete.");

            // Only open door if the user actually selected a prompt
            if (!_promptWasSelected)
            {
                Debug.LogWarning("Door Script: UI faded out but no prompt was selected. Door will not open.");
                return;
            }

            // Prevent multiple door opens
            if (_doorHasOpened)
            {
                Debug.LogWarning("Door Script: Door is already opening/opened!");
                return;
            }

            _doorHasOpened = true;
            Debug.Log("Door Script: Opening door now.");

            // Stop any existing open routine
            if (_doorOpenRoutine != null)
                StopCoroutine(_doorOpenRoutine);

            // Start delayed door open
            _doorOpenRoutine = StartCoroutine(DelayedDoorOpen());
        }

        private IEnumerator DelayedDoorOpen()
        {
            if (_delayBeforeDoorOpen > 0f)
            {
                Debug.Log($"Door Script: Waiting {_delayBeforeDoorOpen}s before opening door...");
                yield return new WaitForSeconds(_delayBeforeDoorOpen);
            }

            if (doorAnimator != null)
            {
                Debug.Log($"Door Script: About to set Open trigger. Prompt selected: {_promptWasSelected}");
                
                // CRITICAL: Double-check that prompt was selected before actually triggering
                if (!_promptWasSelected)
                {
                    Debug.LogError("CRITICAL: Attempted to open door WITHOUT prompt selection! Blocking animation trigger.");
                    _doorHasOpened = false; // Reset so it doesn't repeat
                    yield break;
                }
                
                // Enable animator momentarily to play animation, then disable if needed
                if (!doorAnimator.enabled)
                {
                    doorAnimator.enabled = true;
                    Debug.Log("Door Script: Re-enabled Animator for door opening animation");
                }
                
                doorAnimator.SetTrigger("Open");
                _isDoorOpen = true;
                Debug.Log("Door Script: 'Open' trigger sent to Animator.");

                // Play open sound
                if (doorOpenSound != null && audioSource != null)
                {
                    audioSource.PlayOneShot(doorOpenSound);
                    Debug.Log("Door Script: Played door open sound.");
                }
            }
            else
            {
                Debug.LogError("Door Script: Cannot open door because Animator is missing!");
            }
        }

        private void CloseDoor()
        {
            if (!_isDoorOpen)
            {
                Debug.Log("Door Script: Door is already closed.");
                return;
            }

            if (_doorCloseRoutine != null)
                StopCoroutine(_doorCloseRoutine);

            _doorCloseRoutine = StartCoroutine(DelayedDoorClose());
        }

        private IEnumerator DelayedDoorClose()
        {
            if (_delayBeforeDoorClose > 0f)
            {
                Debug.Log($"Door Script: Waiting {_delayBeforeDoorClose}s before closing door...");
                yield return new WaitForSeconds(_delayBeforeDoorClose);
            }

            if (doorAnimator != null)
            {
                Debug.Log("Door Script: About to set Close trigger.");
                
                doorAnimator.SetTrigger("Close");
                _isDoorOpen = false;
                Debug.Log("Door Script: 'Close' trigger sent to Animator.");

                // Play close sound
                if (doorCloseSound != null && audioSource != null)
                {
                    audioSource.PlayOneShot(doorCloseSound);
                    Debug.Log("Door Script: Played door close sound.");
                }
            }
            else
            {
                Debug.LogError("Door Script: Cannot close door because Animator is missing!");
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!_autoCloseOnTriggerEnter)
                return;
            if (_openedFromSessionComplete)
                return;

            if (other.CompareTag("Player") && _isDoorOpen)
            {
                Debug.Log("Door Script: Player entered threshold, closing door.");
                CloseDoor();
            }
        }

        void OnDestroy()
        {
            if (_doorOpenRoutine != null)
                StopCoroutine(_doorOpenRoutine);

            if (_doorCloseRoutine != null)
                StopCoroutine(_doorCloseRoutine);

            if (_themeUI != null && _isSubscribed)
            {
                _themeUI.OnPromptSelected -= OnPromptSelected;
                _themeUI.OnUiFadeOutComplete -= OnUiFadeOutComplete;
                _isSubscribed = false;
                Debug.Log("Door Script: Unsubscribed from ThemeSelectionUI events.");
            }

            if (_sandbox != null && _isSandboxSubscribed)
            {
                _sandbox.OnSessionComplete.RemoveListener(OnSessionComplete);
                _isSandboxSubscribed = false;
                Debug.Log("Door Script: Unsubscribed from SandboxManager.OnSessionComplete.");
            }
        }
    }
}
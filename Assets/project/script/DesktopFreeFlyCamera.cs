using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Desktop Free-Fly Camera Controller.
    /// 
    /// Key Controls (Desktop only):
    /// - Grab Key: Hold Left Alt (or Left Ctrl, or RMB while pressing movement keys)
    /// - Translation:
    ///     W: Forward (horizontal along camera view)
    ///     S: Backward
    ///     A: Left
    ///     D: Right
    ///     Q: Down (pure world -Y)
    ///     E: Up (pure world +Y)
    ///     Shift: Speed Boost (2.5x)
    /// 
    /// Constraints:
    /// - Translation only. Camera rotation is 100% frozen while grab key is held.
    /// - Smooth acceleration / deceleration ramp.
    /// - Returns control to previous camera mode (RoverCameraRig / OrbitCamera) upon release.
    /// - Strictly disabled when a VR headset is active (zero conflict with TrackedPoseDriver / XR Origin).
    /// </summary>
    [DisallowMultipleComponent]
    public class DesktopFreeFlyCamera : MonoBehaviour
    {
        public static DesktopFreeFlyCamera Instance { get; private set; }

        public static bool IsFreeFlyGrabActive => Instance != null && Instance.isGrabActive;

        [Header("Speed & Dynamics")]
        [Tooltip("Base translation speed in meters/second")]
        public float moveSpeed = 12f;
        [Tooltip("Speed multiplier when holding Shift")]
        public float boostMultiplier = 2.5f;
        [Tooltip("Acceleration / deceleration smoothing responsiveness")]
        public float acceleration = 8f;

        [Header("State")]
        public bool isGrabActive = false;

        private Vector3 currentVelocity = Vector3.zero;
        private Quaternion frozenRotation;
        private bool wasGrabActiveLastFrame = false;

        // Cached external scripts to pause during free-fly grab
        private OrbitCamera cachedOrbitCamera;
        private ProjectName.Rover.RoverCameraRig cachedRoverCameraRig;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else if (Instance != this)
            {
                Destroy(this);
                return;
            }

            cachedOrbitCamera = GetComponent<OrbitCamera>();
            if (cachedOrbitCamera == null) cachedOrbitCamera = GetComponentInParent<OrbitCamera>();

            cachedRoverCameraRig = GetComponent<ProjectName.Rover.RoverCameraRig>();
            if (cachedRoverCameraRig == null) cachedRoverCameraRig = GetComponentInParent<ProjectName.Rover.RoverCameraRig>();
        }

        private void Update()
        {
            // Gate VR: strictly disable on active VR headsets to prevent camera fighting with TrackedPoseDriver
            if (IsVRHeadsetActive())
            {
                if (isGrabActive)
                {
                    EndGrabMode();
                }
                return;
            }

            bool grabHeld = CheckGrabKeyHeld();
            bool movementKeysPressed = CheckMovementKeysPressed();

            // Check if grab mode should be active:
            // 1. Explicit grab key (Left Alt or Left Ctrl) is held.
            // 2. OR RMB is held while movement keys (W/A/S/D/Q/E) are being pressed.
            bool shouldGrab = grabHeld || (IsRMBHeld() && movementKeysPressed);

            if (shouldGrab)
            {
                if (!isGrabActive)
                {
                    StartGrabMode();
                }

                HandleTranslation();
            }
            else
            {
                if (isGrabActive)
                {
                    EndGrabMode();
                }
            }

            wasGrabActiveLastFrame = isGrabActive;
        }

        private void LateUpdate()
        {
            // Guarantee rotation remains 100% frozen while grab mode is engaged
            if (isGrabActive && !IsVRHeadsetActive())
            {
                transform.rotation = frozenRotation;
            }
        }

        private void StartGrabMode()
        {
            isGrabActive = true;
            frozenRotation = transform.rotation;
            currentVelocity = Vector3.zero;

            // Pause OrbitCamera if present
            if (cachedOrbitCamera == null) cachedOrbitCamera = FindAnyObjectByType<OrbitCamera>();
            if (cachedOrbitCamera != null && cachedOrbitCamera.enabled)
            {
                cachedOrbitCamera.enabled = false;
            }

            Debug.Log("[DesktopFreeFlyCamera] Grab mode engaged — translation active, rotation locked.");
        }

        private void EndGrabMode()
        {
            isGrabActive = false;
            currentVelocity = Vector3.zero;

            // Restore OrbitCamera if present
            if (cachedOrbitCamera != null && !cachedOrbitCamera.enabled)
            {
                cachedOrbitCamera.enabled = true;
            }

            Debug.Log("[DesktopFreeFlyCamera] Grab mode released — returning to previous camera mode.");
        }

        private void HandleTranslation()
        {
            // Compute horizontal forward & right vectors relative to camera orientation
            Vector3 camForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            if (camForward.sqrMagnitude < 0.001f) camForward = transform.forward;

            Vector3 camRight = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;
            if (camRight.sqrMagnitude < 0.001f) camRight = transform.right;

            Vector3 targetInput = Vector3.zero;

            // Horizontal W / S / A / D
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null)
            {
                if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) targetInput += camForward;
                if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) targetInput -= camForward;
                if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) targetInput += camRight;
                if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) targetInput -= camRight;

                // Vertical Q (Down) / E (Up) in pure world Y
                if (Keyboard.current.eKey.isPressed) targetInput += Vector3.up;
                if (Keyboard.current.qKey.isPressed) targetInput -= Vector3.up;
            }
            else
#endif
            {
                if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) targetInput += camForward;
                if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) targetInput -= camForward;
                if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) targetInput += camRight;
                if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) targetInput -= camRight;

                if (Input.GetKey(KeyCode.E)) targetInput += Vector3.up;
                if (Input.GetKey(KeyCode.Q)) targetInput -= Vector3.up;
            }

            // Speed multiplier (Shift)
            bool isBoosted = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed))
            {
                isBoosted = true;
            }
#endif
            float targetSpeed = moveSpeed * (isBoosted ? boostMultiplier : 1.0f);
            Vector3 targetVelocity = targetInput.normalized * targetSpeed;

            // Smooth acceleration and deceleration
            currentVelocity = Vector3.Lerp(currentVelocity, targetVelocity, Time.deltaTime * acceleration);
            transform.position += currentVelocity * Time.deltaTime;
        }

        private bool CheckGrabKeyHeld()
        {
            // Primary Grab Key: Left Alt (or Left Ctrl)
            bool altHeld = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt) || Input.GetKey(KeyCode.LeftControl);
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null)
            {
                if (Keyboard.current.leftAltKey.isPressed || Keyboard.current.rightAltKey.isPressed || Keyboard.current.leftCtrlKey.isPressed)
                {
                    altHeld = true;
                }
            }
#endif
            return altHeld;
        }

        private bool IsRMBHeld()
        {
            bool rmbHeld = Input.GetMouseButton(1);
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null && Mouse.current.rightButton.isPressed)
            {
                rmbHeld = true;
            }
#endif
            return rmbHeld;
        }

        private bool CheckMovementKeysPressed()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null)
            {
                if (Keyboard.current.wKey.isPressed || Keyboard.current.sKey.isPressed ||
                    Keyboard.current.aKey.isPressed || Keyboard.current.dKey.isPressed ||
                    Keyboard.current.qKey.isPressed || Keyboard.current.eKey.isPressed ||
                    Keyboard.current.upArrowKey.isPressed || Keyboard.current.downArrowKey.isPressed ||
                    Keyboard.current.leftArrowKey.isPressed || Keyboard.current.rightArrowKey.isPressed)
                {
                    return true;
                }
            }
#endif
            return Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.S) ||
                   Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.D) ||
                   Input.GetKey(KeyCode.Q) || Input.GetKey(KeyCode.E) ||
                   Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.DownArrow) ||
                   Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.RightArrow);
        }

        public static bool IsVRHeadsetActive()
        {
            if (UnityEngine.XR.XRSettings.isDeviceActive) return true;
            var displays = new List<UnityEngine.XR.XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(displays);
            for (int i = 0; i < displays.Count; i++)
            {
                if (displays[i].running) return true;
            }
            return false;
        }
    }

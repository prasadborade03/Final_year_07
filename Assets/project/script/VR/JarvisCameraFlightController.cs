using System;
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using UnityEngine.XR;
using ProjectName.Rover;

namespace ProjectName.VR
{
    /// <summary>
    /// Jarvis Camera Flight & Free-Look Controller.
    /// 
    /// Solves the camera vs rover input conflict:
    /// - Rover Input Focus (Default): WASD / Arrows drive the active Rover.
    ///   Camera smoothly follows the rover in the selected perspective (Rear, Left, Front, Right, Top).
    ///   Pressing WASD will NEVER break camera follow.
    /// - Quick Camera Flight (Hold RMB):
    ///   Holding Right Mouse Button temporarily gives WASD + Mouse to the Camera for 360° look and flight,
    ///   while simultaneously muting rover drive. Releasing RMB instantly restores rover driving.
    /// - Dedicated Toggle [C]:
    ///   Press [C] to lock keyboard WASD onto Camera Flight or Rover Drive without holding RMB.
    /// - Perspective Presets [V]:
    ///   Press [V] or click HUD buttons to cycle views (Rear -> Left -> Front -> Right -> Top -> FreeFly).
    /// - Focus Rover [F]:
    ///   Press [F] to immediately snap camera to active rover and set focus to Rover Drive.
    /// - VR Auto-Handoff:
    ///   Yields 6DOF tracking to Meta Quest 2 / OpenXR headset when VR device is active.
    /// </summary>
    [DisallowMultipleComponent]
    public class JarvisCameraFlightController : MonoBehaviour
    {
        public static JarvisCameraFlightController Instance { get; private set; }

        public enum InputFocus
        {
            Rover,   // WASD drives rover; camera follows or stays stationary
            Camera   // WASD flies camera; rover is parked
        }

        public enum PerspectiveMode
        {
            Rear,
            Left,
            Front,
            Right,
            Top,
            FreeFly
        }

        [Header("Input Focus & Mode Separation")]
        [Tooltip("Active target of keyboard WASD/Arrow inputs.")]
        public InputFocus activeInputFocus = InputFocus.Rover;

        /// <summary>
        /// True if camera is currently consuming flight inputs (muting rover driving).
        /// Queried by DesktopFreeFlyCamera.IsFreeFlyGrabActive to block Rover controllers.
        /// </summary>
        public static bool IsCameraFlyingActive
        {
            get
            {
                if (Instance == null) return false;
                return Instance.activeInputFocus == InputFocus.Camera || Instance.isQuickRMBFlying;
            }
        }

        public static event Action<InputFocus> OnInputFocusChanged;

        [Header("Flight Dynamics")]
        [Tooltip("Normal flight speed in meters per second.")]
        public float moveSpeed = 14f;

        [Tooltip("Boost multiplier when holding Shift.")]
        public float boostMultiplier = 2.5f;

        [Tooltip("Vertical climb / descend speed.")]
        public float climbSpeed = 8f;

        [Tooltip("Acceleration responsiveness.")]
        public float acceleration = 12f;

        [Header("Mouse Look")]
        [Tooltip("Mouse rotation sensitivity.")]
        public float mouseSensitivity = 2.5f;

        [Tooltip("Smooth look interpolation.")]
        public bool smoothLook = true;
        public float lookSmoothFactor = 20f;

        [Header("Rover Follow & Perspectives")]
        public PerspectiveMode activePerspective = PerspectiveMode.Rear;
        [Tooltip("Distance from rover for orbit perspectives.")]
        public float roverFollowDistance = 4.8f;
        [Tooltip("Elevation height above rover.")]
        public float roverFollowHeight = 2.0f;
        [Tooltip("Smooth follow damping for perspectives.")]
        public float followSmoothSpeed = 8.0f;

        [Header("VR Detection")]
        [Tooltip("Automatically disable desktop mouse/WASD when VR headset tracking is active.")]
        public bool autoDetectVR = true;

        private float currentYaw = 0f;
        private float currentPitch = 0f;
        private float targetYaw = 0f;
        private float targetPitch = 0f;

        private Vector3 currentVelocity = Vector3.zero;
        private bool isLooking = false;
        private bool isQuickRMBFlying = false;

        private UnityEngine.InputSystem.XR.TrackedPoseDriver trackedPoseDriver;

        private void Awake()
        {
            Instance = this;
            trackedPoseDriver = GetComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
        }

        private void OnEnable()
        {
            RoverCameraRig.OnPerspectiveChanged += HandleRigPerspectiveChanged;
        }

        private void OnDisable()
        {
            RoverCameraRig.OnPerspectiveChanged -= HandleRigPerspectiveChanged;
        }

        private void Start()
        {
            Vector3 angles = transform.eulerAngles;
            currentPitch = angles.x;
            currentYaw = angles.y;
            targetPitch = currentPitch;
            targetYaw = currentYaw;
        }

        private void Update()
        {
            bool vrActive = IsVRHeadsetActive();

            // When in VR, let TrackedPoseDriver handle 6DOF tracking natively
            if (vrActive && autoDetectVR)
            {
                if (trackedPoseDriver != null && !trackedPoseDriver.enabled)
                {
                    trackedPoseDriver.enabled = true;
                }
                return;
            }

            // On Desktop, disable TrackedPoseDriver so it doesn't fight mouse rotation
            if (trackedPoseDriver != null && trackedPoseDriver.enabled)
            {
                trackedPoseDriver.enabled = false;
            }

            HandleHotkeys();
            HandleMouseLook();

            // Flight movement is only handled if the camera is actively being flown:
            // 1. User toggled activeInputFocus to Camera, OR
            // 2. User is holding RMB (quick look & fly)
            if (IsCameraFlyingActive)
            {
                HandleFlightMovement();
            }
            else
            {
                // Decelerate flight velocity smoothly to zero
                currentVelocity = Vector3.Lerp(currentVelocity, Vector3.zero, Time.unscaledDeltaTime * acceleration);
            }

            // If in an orbit perspective preset and not holding RMB to break out, follow the rover!
            if (activePerspective != PerspectiveMode.FreeFly && !isQuickRMBFlying)
            {
                UpdateRoverPerspectiveFollow();
            }
        }

        private void HandleHotkeys()
        {
            bool cPressed = false;
            bool vPressed = false;
            bool fPressed = false;

#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null)
            {
                if (Keyboard.current.cKey.wasPressedThisFrame && !Keyboard.current.leftShiftKey.isPressed) cPressed = true;
                if (Keyboard.current.vKey.wasPressedThisFrame) vPressed = true;
                if (Keyboard.current.fKey.wasPressedThisFrame) fPressed = true;
            }
#endif
            if (!cPressed && Input.GetKeyDown(KeyCode.C) && !Input.GetKey(KeyCode.LeftShift)) cPressed = true;
            if (!vPressed && Input.GetKeyDown(KeyCode.V)) vPressed = true;
            if (!fPressed && Input.GetKeyDown(KeyCode.F)) fPressed = true;

            // [C] Toggle Input Focus between Rover and Camera
            if (cPressed)
            {
                ToggleInputFocus();
            }

            // [F] Focus active rover
            if (fPressed)
            {
                FocusRover();
            }
            // [V] Cycle perspective presets
            else if (vPressed)
            {
                int next = ((int)activePerspective + 1) % 6;
                SetPerspective((PerspectiveMode)next);
            }
        }

        public void ToggleInputFocus()
        {
            var newFocus = (activeInputFocus == InputFocus.Rover) ? InputFocus.Camera : InputFocus.Rover;
            SetInputFocus(newFocus);
        }

        public void SetInputFocus(InputFocus newFocus)
        {
            activeInputFocus = newFocus;
            Debug.Log($"[JarvisCameraFlightController] Input Focus changed to: <b><color={(newFocus == InputFocus.Rover ? "#00E5FF" : "#FFB300")}>{newFocus.ToString().ToUpperInvariant()}</color></b>");
            OnInputFocusChanged?.Invoke(newFocus);

            if (JarvisAudioFeedback.Instance != null)
            {
                JarvisAudioFeedback.Instance.PlayPerspectiveSwitch();
            }
        }

        public void FocusRover()
        {
            activeInputFocus = InputFocus.Rover;
            SetPerspective(PerspectiveMode.Rear);

            if (JarvisAudioFeedback.Instance != null)
            {
                JarvisAudioFeedback.Instance.PlayRoverFocus();
            }
        }

        private void HandleMouseLook()
        {
            bool rmbHeld = false;

#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null)
            {
                rmbHeld = Mouse.current.rightButton.isPressed;
            }
#endif
            if (!rmbHeld)
            {
                rmbHeld = Input.GetMouseButton(1);
            }

            if (rmbHeld)
            {
                isLooking = true;
                isQuickRMBFlying = true;

                float mouseX = 0f;
                float mouseY = 0f;

#if ENABLE_INPUT_SYSTEM
                if (Mouse.current != null)
                {
                    Vector2 delta = Mouse.current.delta.ReadValue();
                    mouseX = delta.x * 0.12f * mouseSensitivity;
                    mouseY = delta.y * 0.12f * mouseSensitivity;
                }
                else
#endif
                {
                    mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
                    mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;
                }

                targetYaw += mouseX;
                targetPitch -= mouseY;
                targetPitch = Mathf.Clamp(targetPitch, -89f, 89f);

                if (smoothLook)
                {
                    currentYaw = Mathf.Lerp(currentYaw, targetYaw, Time.unscaledDeltaTime * lookSmoothFactor);
                    currentPitch = Mathf.Lerp(currentPitch, targetPitch, Time.unscaledDeltaTime * lookSmoothFactor);
                }
                else
                {
                    currentYaw = targetYaw;
                    currentPitch = targetPitch;
                }

                transform.rotation = Quaternion.Euler(currentPitch, currentYaw, 0f);
            }
            else
            {
                if (isLooking)
                {
                    isLooking = false;
                    isQuickRMBFlying = false;

                    Vector3 angles = transform.eulerAngles;
                    currentPitch = angles.x;
                    currentYaw = angles.y;
                    targetPitch = currentPitch;
                    targetYaw = currentYaw;
                }
            }
        }

        private void HandleFlightMovement()
        {
            Vector3 inputDir = Vector3.zero;

            Vector3 forward = transform.forward;
            Vector3 right = transform.right;

            // Keyboard input
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null)
            {
                if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) inputDir += forward;
                if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) inputDir -= forward;
                if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) inputDir += right;
                if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) inputDir -= right;

                if (Keyboard.current.eKey.isPressed || Keyboard.current.spaceKey.isPressed) inputDir += Vector3.up;
                if (Keyboard.current.qKey.isPressed) inputDir -= Vector3.up;
            }
            else
#endif
            {
                if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) inputDir += forward;
                if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) inputDir -= forward;
                if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) inputDir += right;
                if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) inputDir -= right;

                if (Input.GetKey(KeyCode.E) || Input.GetKey(KeyCode.Space)) inputDir += Vector3.up;
                if (Input.GetKey(KeyCode.Q)) inputDir -= Vector3.up;
            }

            // Boost
            bool isBoosted = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed))
            {
                isBoosted = true;
            }
#endif

            // Scroll wheel speed adjust
            float scroll = 0f;
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null)
            {
                scroll = Mouse.current.scroll.ReadValue().y * 0.01f;
            }
            else
#endif
            {
                scroll = Input.mouseScrollDelta.y;
            }

            if (Mathf.Abs(scroll) > 0.01f)
            {
                moveSpeed = Mathf.Clamp(moveSpeed + scroll * 2f, 2f, 80f);
            }

            float speed = moveSpeed * (isBoosted ? boostMultiplier : 1f);
            Vector3 targetVel = inputDir.normalized * speed;

            currentVelocity = Vector3.Lerp(currentVelocity, targetVel, Time.unscaledDeltaTime * acceleration);
            transform.position += currentVelocity * Time.unscaledDeltaTime;
        }

        public void SetPerspective(PerspectiveMode mode)
        {
            activePerspective = mode;
            Debug.Log($"[JarvisCameraFlightController] Perspective set to: <b><color=#00E5FF>{mode}</color></b>");

            var rover = GetActiveRoverTransform();
            if (rover != null && mode != PerspectiveMode.FreeFly)
            {
                Vector3 targetPos = CalculatePerspectiveTargetPos(rover, mode);
                transform.position = targetPos;
                transform.LookAt(rover.position + Vector3.up * 0.6f);

                Vector3 angles = transform.eulerAngles;
                currentPitch = angles.x;
                currentYaw = angles.y;
                targetPitch = currentPitch;
                targetYaw = currentYaw;
            }
        }

        private void UpdateRoverPerspectiveFollow()
        {
            var rover = GetActiveRoverTransform();
            if (rover == null) return;

            Vector3 targetPos = CalculatePerspectiveTargetPos(rover, activePerspective);
            Vector3 lookTarget = rover.position + Vector3.up * 0.6f;

            transform.position = Vector3.Lerp(transform.position, targetPos, Time.unscaledDeltaTime * followSmoothSpeed);

            Quaternion targetRot = Quaternion.LookRotation((lookTarget - transform.position).normalized, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.unscaledDeltaTime * followSmoothSpeed);

            Vector3 angles = transform.eulerAngles;
            currentPitch = angles.x;
            currentYaw = angles.y;
            targetPitch = currentPitch;
            targetYaw = currentYaw;
        }

        private Vector3 CalculatePerspectiveTargetPos(Transform rover, PerspectiveMode mode)
        {
            Vector3 forward = Vector3.ProjectOnPlane(rover.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;

            Vector3 roverPos = rover.position;

            switch (mode)
            {
                case PerspectiveMode.Front:
                    return roverPos + (forward * roverFollowDistance) + (Vector3.up * roverFollowHeight);
                case PerspectiveMode.Left:
                    return roverPos - (right * roverFollowDistance) + (Vector3.up * roverFollowHeight);
                case PerspectiveMode.Right:
                    return roverPos + (right * roverFollowDistance) + (Vector3.up * roverFollowHeight);
                case PerspectiveMode.Top:
                    return roverPos - (forward * 1.5f) + (Vector3.up * (roverFollowDistance * 1.6f));
                case PerspectiveMode.Rear:
                default:
                    return roverPos - (forward * roverFollowDistance) + (Vector3.up * roverFollowHeight);
            }
        }

        private void HandleRigPerspectiveChanged(RoverCameraRig.Perspective p)
        {
            switch (p)
            {
                case RoverCameraRig.Perspective.Front: SetPerspective(PerspectiveMode.Front); break;
                case RoverCameraRig.Perspective.Left: SetPerspective(PerspectiveMode.Left); break;
                case RoverCameraRig.Perspective.Right: SetPerspective(PerspectiveMode.Right); break;
                case RoverCameraRig.Perspective.Top: SetPerspective(PerspectiveMode.Top); break;
                case RoverCameraRig.Perspective.Free: SetPerspective(PerspectiveMode.FreeFly); break;
                case RoverCameraRig.Perspective.Rear:
                default: SetPerspective(PerspectiveMode.Rear); break;
            }
        }

        public Transform GetActiveRoverTransform()
        {
            if (ActiveRoverContext.HasActiveRover && ActiveRoverContext.Current != null && ActiveRoverContext.Current.rootGameObject != null)
            {
                return ActiveRoverContext.Current.rootGameObject.transform;
            }

            var husky = UnityEngine.Object.FindAnyObjectByType<RoverController_husky>();
            if (husky != null) return husky.transform;

            var m20 = UnityEngine.Object.FindAnyObjectByType<RoverController_m20>();
            if (m20 != null) return m20.transform;

            var m2020 = UnityEngine.Object.FindAnyObjectByType<RoverController_m2020>();
            if (m2020 != null) return m2020.transform;

            return null;
        }

        public static bool IsVRHeadsetActive()
        {
            if (UnityEngine.XR.XRSettings.isDeviceActive) return true;

            var displays = new List<XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(displays);
            for (int i = 0; i < displays.Count; i++)
            {
                if (displays[i].running) return true;
            }
            return false;
        }
    }
}

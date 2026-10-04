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
    /// Solves camera sticking and follow issues:
    /// - Starts right behind the real GeneratedHusky rover on the terrain (ground-level).
    /// - Follows the rover in LateUpdate() as the rover drives across the terrain.
    /// - Smoothly orbits around the rover for Back, Front, Left, Right, and Up perspectives.
    /// - Clean input separation: WASD drives rover, holding RMB flies camera freely, [C] toggles focus.
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
        public float moveSpeed = 14f;
        public float boostMultiplier = 2.5f;
        public float climbSpeed = 8f;
        public float acceleration = 12f;

        [Header("Mouse Look")]
        public float mouseSensitivity = 2.5f;
        public bool smoothLook = true;
        public float lookSmoothFactor = 20f;

        [Header("Rover Follow & Perspectives")]
        public PerspectiveMode activePerspective = PerspectiveMode.Rear;
        public float roverFollowDistance = 4.8f;
        public float roverFollowHeight = 1.8f;
        public float followSmoothSpeed = 12.0f;

        [Header("VR Detection")]
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
            // Position camera behind active rover on the ground
            var rover = GetActiveRoverTransform();
            if (rover != null)
            {
                Vector3 targetPos = CalculatePerspectiveTargetPos(rover, activePerspective);
                transform.position = targetPos;
                transform.LookAt(rover.position + Vector3.up * 0.5f);
            }

            Vector3 angles = transform.eulerAngles;
            currentPitch = angles.x;
            currentYaw = angles.y;
            targetPitch = currentPitch;
            targetYaw = currentYaw;
        }

        private void Update()
        {
            bool vrActive = IsVRHeadsetActive();

            if (vrActive && autoDetectVR)
            {
                if (trackedPoseDriver != null && !trackedPoseDriver.enabled)
                {
                    trackedPoseDriver.enabled = true;
                }
                return;
            }

            if (trackedPoseDriver != null && trackedPoseDriver.enabled)
            {
                trackedPoseDriver.enabled = false;
            }

            HandleHotkeys();
            HandleMouseLook();

            if (IsCameraFlyingActive)
            {
                HandleFlightMovement();
            }
            else
            {
                currentVelocity = Vector3.Lerp(currentVelocity, Vector3.zero, Time.unscaledDeltaTime * acceleration);
            }
        }

        private void LateUpdate()
        {
            // Follow rover smoothly after physics update
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

            if (cPressed)
            {
                ToggleInputFocus();
            }

            if (fPressed)
            {
                FocusRover();
            }
            else if (vPressed)
            {
                // Cycle: Rear -> Front -> Left -> Right -> Top -> FreeFly
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

            bool isBoosted = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed))
            {
                isBoosted = true;
            }
#endif

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
                transform.LookAt(rover.position + Vector3.up * 0.5f);

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
            Vector3 lookTarget = rover.position + Vector3.up * 0.5f;

            // Smooth position follow
            transform.position = Vector3.Lerp(transform.position, targetPos, Time.unscaledDeltaTime * followSmoothSpeed);

            // Smooth rotation look-at
            Vector3 lookDir = (lookTarget - transform.position).normalized;
            if (lookDir.sqrMagnitude > 0.001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(lookDir, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.unscaledDeltaTime * followSmoothSpeed);
            }

            Vector3 angles = transform.eulerAngles;
            currentPitch = angles.x;
            currentYaw = angles.y;
            targetPitch = currentPitch;
            targetYaw = currentYaw;
        }

        private Vector3 CalculatePerspectiveTargetPos(Transform rover, PerspectiveMode mode)
        {
            // Project rover forward vector onto horizontal plane
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
                    return roverPos - (forward * 0.8f) + (Vector3.up * (roverFollowDistance * 1.5f));
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
            // 1. Check ActiveRoverContext
            if (ActiveRoverContext.HasActiveRover && ActiveRoverContext.Current != null && ActiveRoverContext.Current.rootGameObject != null)
            {
                return ActiveRoverContext.Current.rootGameObject.transform;
            }

            // 2. Direct search for pre-spawned GeneratedHusky or rover gameobjects
            var husky = GameObject.Find("GeneratedHusky");
            if (husky != null) return husky.transform;

            var m20 = GameObject.Find("GeneratedM20");
            if (m20 != null) return m20.transform;

            var m2020 = GameObject.Find("GeneratedM2020");
            if (m2020 != null) return m2020.transform;

            // 3. Search for root ArticulationBody sitting on the terrain
            var bodies = UnityEngine.Object.FindObjectsByType<ArticulationBody>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var b in bodies)
            {
                if (b.isRoot && b.name.IndexOf("origin", StringComparison.OrdinalIgnoreCase) < 0 && b.transform.position.y < 200f)
                {
                    return b.transform;
                }
            }

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

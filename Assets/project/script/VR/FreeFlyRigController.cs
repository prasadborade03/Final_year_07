using System;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;
using ProjectName.Rover;

namespace ProjectName.VR
{
    /// <summary>
    /// FreeFlyRigController implements the 6DOF Free-Fly Camera modifier specified in §4.3 of the VR Control Scheme.
    ///
    /// ARCHITECTURAL DECISIONS & SPEC COMPLIANCE:
    /// 1. Why Right Grip instead of Right Trigger (§4.3):
    ///    The trigger (index finger) is dedicated to Select / Click / Grab (§4.1).
    ///    Using the trigger for free-fly would mean every grab or UI click risked accidental camera flight.
    ///    In professional VR design, Grip (middle finger squeeze) is the standard modifier for sustained holds.
    ///
    /// 2. Moving the XR Origin, NOT the Camera directly (§4.3):
    ///    The VR Camera transform is continuously overwritten every frame by headset hardware tracking.
    ///    Moving the camera directly causes jitter and gets discarded. The parent XR Origin rig
    ///    must be translated so the user and all tracked devices translate together.
    ///
    /// 3. Thumbstick Consumption & Snap-Turn Suppression (§4.3, §5):
    ///    While Right Grip is held, the Right Thumbstick drives flight translation.
    ///    The SnapTurnProvider is temporarily suppressed/disabled so the player does not snap-rotate
    ///    while attempting to fly. Upon releasing Grip, normal snap-turning immediately resumes.
    ///
    /// 4. Rover Camera Rig Integration:
    ///    If RoverCameraRig is in follow mode (Rear/Front/Left/Right/Top), activating Free-Fly
    ///    switches perspective to Perspective.Free so the chassis follower does not fight user flight.
    /// </summary>
    [DisallowMultipleComponent]
    public class FreeFlyRigController : MonoBehaviour
    {
        [Header("Rig & Tracking References")]
        [Tooltip("The root XR Origin transform to translate. If null, uses this gameObject's transform.")]
        public Transform xrOriginTransform;

        [Tooltip("The XR tracking camera providing forward/right look vectors. If null, resolves Camera.main.")]
        public Camera xrCamera;

        [Tooltip("Optional reference to the SnapTurnProvider to suppress while flying. If null, auto-resolves on rig.")]
        public SnapTurnProvider snapTurnProvider;

        [Header("Flight Dynamics (§4.3 Comfort Settings)")]
        [Tooltip("Maximum cruising speed in meters per second.")]
        public float flySpeed = 6.0f;

        [Tooltip("Speed when boosting (e.g. thumbstick click or full deflection).")]
        public float fastFlySpeed = 14.0f;

        [Tooltip("Acceleration responsiveness (lerp speed).")]
        public float acceleration = 8.0f;

        [Tooltip("Deceleration damping when thumbstick is released.")]
        public float deceleration = 10.0f;

        [Tooltip("Grip squeeze threshold to engage free-fly mode.")]
        [Range(0.1f, 0.9f)]
        public float gripThreshold = 0.5f;

        [Header("Input System Bindings")]
#if ENABLE_INPUT_SYSTEM
        [Tooltip("Action for Right Hand Grip (Button or Float).")]
        public InputActionProperty rightGripAction;

        [Tooltip("Action for Right Hand Thumbstick (Vector2).")]
        public InputActionProperty rightThumbstickAction;
#endif

        [Header("Desktop / Simulator Fallbacks")]
        [Tooltip("Hold this key on desktop to simulate holding the Right Grip modifier.")]
        public KeyCode desktopGripKey = KeyCode.G;

        [Header("Runtime State")]
        [SerializeField]
        private bool isFlying = false;
        private Vector3 currentVelocity = Vector3.zero;
        private bool wasGripHeld = false;

        public bool IsFlying => isFlying;

        private void Awake()
        {
            if (xrOriginTransform == null)
            {
                xrOriginTransform = transform;
            }

            if (xrCamera == null)
            {
                ResolveXRCamera();
            }

            if (snapTurnProvider == null)
            {
                snapTurnProvider = GetComponentInChildren<SnapTurnProvider>();
            }
        }

        private void Start()
        {
            if (xrCamera == null)
            {
                ResolveXRCamera();
            }

            if (snapTurnProvider == null)
            {
                snapTurnProvider = GetComponentInChildren<SnapTurnProvider>();
            }
        }

        private void OnEnable()
        {
#if ENABLE_INPUT_SYSTEM
            if (rightGripAction.action != null) rightGripAction.action.Enable();
            if (rightThumbstickAction.action != null) rightThumbstickAction.action.Enable();
#endif
        }

        private void OnDisable()
        {
#if ENABLE_INPUT_SYSTEM
            if (rightGripAction.action != null) rightGripAction.action.Disable();
            if (rightThumbstickAction.action != null) rightThumbstickAction.action.Disable();
#endif
            // Ensure snap turn is restored if component is disabled during flight
            RestoreSnapTurn();
        }

        private void Update()
        {
            bool gripHeld = IsRightGripHeld();
            Vector2 thumbstick = ReadRightThumbstick();

            if (gripHeld)
            {
                if (!wasGripHeld)
                {
                    OnFlyModeEntered();
                }

                ExecuteFlightMovement(thumbstick);
            }
            else
            {
                if (wasGripHeld)
                {
                    OnFlyModeExited();
                }

                // Smoothly decelerate to zero when not flying
                if (currentVelocity.sqrMagnitude > 0.0001f)
                {
                    currentVelocity = Vector3.Lerp(currentVelocity, Vector3.zero, Time.deltaTime * deceleration);
                    xrOriginTransform.position += currentVelocity * Time.deltaTime;
                }
            }

            wasGripHeld = gripHeld;
            isFlying = gripHeld;
        }

        /// <summary>
        /// Reads whether the Right Grip is currently held via Input System, OpenXR device, or desktop key.
        /// </summary>
        private bool IsRightGripHeld()
        {
#if ENABLE_INPUT_SYSTEM
            if (rightGripAction.action != null)
            {
                // Can be a float value or a button
                if (rightGripAction.action.type == InputActionType.Button)
                {
                    if (rightGripAction.action.IsPressed()) return true;
                }
                else
                {
                    float gripVal = rightGripAction.action.ReadValue<float>();
                    if (gripVal >= gripThreshold) return true;
                }
            }
#endif

            // OpenXR device fallback for Quest 2 Right Controller
            var rightHand = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            if (rightHand.isValid)
            {
                if (rightHand.TryGetFeatureValue(UnityEngine.XR.CommonUsages.grip, out float gripVal))
                {
                    if (gripVal >= gripThreshold) return true;
                }
                if (rightHand.TryGetFeatureValue(UnityEngine.XR.CommonUsages.gripButton, out bool gripPressed))
                {
                    if (gripPressed) return true;
                }
            }

            // Desktop / Device Simulator fallback
            if (Input.GetKey(desktopGripKey))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Reads the 2D directional thumbstick input from the Right Controller.
        /// </summary>
        private Vector2 ReadRightThumbstick()
        {
            Vector2 stick = Vector2.zero;

#if ENABLE_INPUT_SYSTEM
            if (rightThumbstickAction.action != null)
            {
                stick = rightThumbstickAction.action.ReadValue<Vector2>();
            }
#endif

            if (stick.sqrMagnitude < 0.01f)
            {
                // OpenXR device fallback
                var rightHand = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
                if (rightHand.isValid && rightHand.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out Vector2 axis))
                {
                    stick = axis;
                }
            }

            // Desktop fallback: Arrow keys or I/K/J/L when desktop grip is held
            if (stick.sqrMagnitude < 0.01f && Input.GetKey(desktopGripKey))
            {
                if (Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.I)) stick.y += 1f;
                if (Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.K)) stick.y -= 1f;
                if (Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.L)) stick.x += 1f;
                if (Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.J)) stick.x -= 1f;
            }

            return stick;
        }

        /// <summary>
        /// Executes full 3D translation along camera look and strafe vectors (§4.3).
        /// </summary>
        private void ExecuteFlightMovement(Vector2 thumbstick)
        {
            if (xrCamera == null)
            {
                ResolveXRCamera();
                if (xrCamera == null) return;
            }

            // Spec §4.3: full 3D direction here — you WANT vertical movement while flying
            // moveDir = camera.transform.forward * moveInput.y + camera.transform.right * moveInput.x
            Vector3 camForward = xrCamera.transform.forward;
            Vector3 camRight = xrCamera.transform.right;

            Vector3 targetDirection = (camForward * thumbstick.y + camRight * thumbstick.x);
            float targetSpeed = flySpeed;

            Vector3 targetVelocity = targetDirection * targetSpeed;

            // Ease towards target velocity
            currentVelocity = Vector3.Lerp(currentVelocity, targetVelocity, Time.deltaTime * acceleration);

            // Translate the XR Origin rig root
            xrOriginTransform.position += currentVelocity * Time.deltaTime;
        }

        private void OnFlyModeEntered()
        {
            // 1. Suppress Snap-Turn so thumbstick input does not snap rotate while flying
            SuppressSnapTurn();

            // 2. Disengage Rover follow camera if active so it does not pull the camera back
            if (RoverCameraRig.Instance != null && RoverCameraRig.Instance.currentPerspective != RoverCameraRig.Perspective.Free)
            {
                RoverCameraRig.Instance.SetPerspective(RoverCameraRig.Perspective.Free, false);
            }

            Debug.Log("[FreeFlyRigController] Free-Fly mode engaged (Right Grip held). Snap-turn suppressed.");
        }

        private void OnFlyModeExited()
        {
            // Restore Snap-Turn when Grip is released
            RestoreSnapTurn();
            Debug.Log("[FreeFlyRigController] Free-Fly mode exited (Right Grip released). Snap-turn restored.");
        }

        private void SuppressSnapTurn()
        {
            if (snapTurnProvider == null)
            {
                snapTurnProvider = GetComponentInChildren<SnapTurnProvider>();
            }

            if (snapTurnProvider != null && snapTurnProvider.enabled)
            {
                snapTurnProvider.enabled = false;
            }
        }

        private void RestoreSnapTurn()
        {
            if (snapTurnProvider != null && !snapTurnProvider.enabled)
            {
                snapTurnProvider.enabled = true;
            }
        }

        private void ResolveXRCamera()
        {
            xrCamera = Camera.main;
            if (xrCamera == null)
            {
                xrCamera = FindAnyObjectByType<Camera>();
            }
        }
    }
}

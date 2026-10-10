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
    /// FreeFlyRigController implements 6DOF Free-Fly Camera translation for VR.
    ///
    /// Spec §5 Compliance:
    /// 1. LEFT JOYSTICK: continues controlling the rover drive, completely independent.
    /// 2. RIGHT JOYSTICK: controls horizontal free-camera translation ONLY when FREE camera mode
    ///    is active and the Right Grip (Grab) control is held.
    /// 3. HMD / Head Tracking: continues controlling the user's view naturally as normal;
    ///    headset tracking is NEVER overwritten.
    /// 4. VERTICAL TRANSLATION:
    ///    - Button B (Right Hand Secondary Button): Move camera UP (+Y)
    ///    - Button A (Right Hand Primary Button): Move camera DOWN (-Y)
    /// 5. WHEN GRAB IS RELEASED:
    ///    - Right joystick stops moving camera.
    ///    - Camera translation halts immediately.
    ///    - SnapTurnProvider resumes normally.
    /// 6. SPEED TUNER: dynamically driven by RoverCameraRig.FreeFlySpeed.
    /// </summary>
    [DisallowMultipleComponent]
    public class FreeFlyRigController : MonoBehaviour
    {
        private static FreeFlyRigController _instance;
        public static FreeFlyRigController Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = UnityEngine.Object.FindAnyObjectByType<FreeFlyRigController>();
                }
                return _instance;
            }
            private set => _instance = value;
        }

        [Header("Rig & Tracking References")]
        [Tooltip("The root XR Origin transform to translate. If null, auto-resolves parent/self.")]
        public Transform xrOriginTransform;

        [Tooltip("The XR tracking camera providing look vectors. If null, resolves Camera.main.")]
        public Camera xrCamera;

        [Tooltip("Optional reference to the SnapTurnProvider to suppress while flying.")]
        public SnapTurnProvider snapTurnProvider;

        [Header("Flight Dynamics")]
        [Tooltip("Flight speed in meters per second (synced with HUD speed tuner)")]
        public float flySpeed = 12.0f;

        [Tooltip("Boost flight speed in meters per second")]
        public float fastFlySpeed = 24.0f;

        [Tooltip("Acceleration responsiveness (lerp speed)")]
        public float acceleration = 8.0f;

        [Tooltip("Deceleration damping when thumbstick is released")]
        public float deceleration = 10.0f;

        [Tooltip("Grip squeeze threshold to engage free-fly mode")]
        [Range(0.1f, 0.9f)]
        public float gripThreshold = 0.5f;

        [Header("Input System Bindings")]
#if ENABLE_INPUT_SYSTEM
        [Tooltip("Action for Right Hand Grip (Button or Float)")]
        public InputActionProperty rightGripAction;

        [Tooltip("Action for Right Hand Thumbstick (Vector2)")]
        public InputActionProperty rightThumbstickAction;

        [Tooltip("Action for Right Hand Button B (Up)")]
        public InputActionProperty rightButtonBAction;

        [Tooltip("Action for Right Hand Button A (Down)")]
        public InputActionProperty rightButtonAAction;

        [Tooltip("Action for Right Hand Thumbstick Click (Cycle Perspective)")]
        public InputActionProperty rightStickClickAction;
#endif

        [Header("Controller Ray Visuals")]
        [Tooltip("Line visual component on the Right Controller (hidden during Free Fly flight)")]
        [SerializeField] private Behaviour rightLineVisual;
        [SerializeField] private LineRenderer rightLineRenderer;

        [Header("Desktop / Simulator Fallbacks")]
        [Tooltip("Hold this key on desktop to simulate holding the Right Grip modifier")]
        public KeyCode desktopGripKey = KeyCode.G;

        [Header("Runtime State")]
        [SerializeField]
        private bool isFlying = false;
        private Vector3 currentVelocity = Vector3.zero;
        private bool wasGripHeld = false;
        private bool wasStickClickHeld = false;

        public bool IsFlying => isFlying;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else if (Instance != this)
            {
                Destroy(this);
                return;
            }

            if (xrOriginTransform == null)
            {
                var origin = GameObject.Find("XR Origin");
                xrOriginTransform = origin != null ? origin.transform : transform;
            }

            if (xrCamera == null)
            {
                ResolveXRCamera();
            }

            if (snapTurnProvider == null)
            {
                snapTurnProvider = GetComponentInChildren<SnapTurnProvider>();
                if (snapTurnProvider == null && xrOriginTransform != null)
                {
                    snapTurnProvider = xrOriginTransform.GetComponentInChildren<SnapTurnProvider>();
                }
            }

            CacheRightControllerVisuals();
        }

        private void Start()
        {
            if (xrCamera == null)
            {
                ResolveXRCamera();
            }

            if (snapTurnProvider == null && xrOriginTransform != null)
            {
                snapTurnProvider = xrOriginTransform.GetComponentInChildren<SnapTurnProvider>();
            }

            flySpeed = RoverCameraRig.FreeFlySpeed;
            CacheRightControllerVisuals();
        }

        private void OnEnable()
        {
            RoverCameraRig.OnCameraSpeedChanged += HandleCameraSpeedChanged;
            RoverCameraRig.OnPerspectiveChanged += HandlePerspectiveChanged;
            flySpeed = RoverCameraRig.FreeFlySpeed;

#if ENABLE_INPUT_SYSTEM
            if (rightGripAction.action != null) rightGripAction.action.Enable();
            if (rightThumbstickAction.action != null) rightThumbstickAction.action.Enable();
            if (rightButtonBAction.action != null) rightButtonBAction.action.Enable();
            if (rightButtonAAction.action != null) rightButtonAAction.action.Enable();
            if (rightStickClickAction.action != null) rightStickClickAction.action.Enable();
#endif
            CacheRightControllerVisuals();
        }

        private void OnDisable()
        {
            RoverCameraRig.OnCameraSpeedChanged -= HandleCameraSpeedChanged;
            RoverCameraRig.OnPerspectiveChanged -= HandlePerspectiveChanged;

#if ENABLE_INPUT_SYSTEM
            if (rightGripAction.action != null) rightGripAction.action.Disable();
            if (rightThumbstickAction.action != null) rightThumbstickAction.action.Disable();
            if (rightButtonBAction.action != null) rightButtonBAction.action.Disable();
            if (rightButtonAAction.action != null) rightButtonAAction.action.Disable();
            if (rightStickClickAction.action != null) rightStickClickAction.action.Disable();
#endif
            RestoreSnapTurn();
            SetRightRayVisualVisible(true);
            currentVelocity = Vector3.zero;
            isFlying = false;
            wasGripHeld = false;
            wasStickClickHeld = false;
        }

        private void OnDestroy()
        {
            SetRightRayVisualVisible(true);
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
            {
                SetRightRayVisualVisible(true);
            }
        }

        private void HandlePerspectiveChanged(RoverCameraRig.Perspective newPerspective)
        {
            if (newPerspective != RoverCameraRig.Perspective.Free)
            {
                if (wasGripHeld)
                {
                    OnFlyModeExited();
                }
                SetRightRayVisualVisible(true);
            }
        }

        private void HandleCameraSpeedChanged(float newSpeed)
        {
            flySpeed = newSpeed;
        }

        private void Update()
        {
            // Sync speed directly from HUD speed tuner
            flySpeed = RoverCameraRig.FreeFlySpeed;

            // Check Right Thumbstick click for camera perspective cycling
            bool stickClicked = false;
#if ENABLE_INPUT_SYSTEM
            if (rightStickClickAction.action != null && rightStickClickAction.action.enabled)
            {
                stickClicked = rightStickClickAction.action.IsPressed();
            }
#endif
            if (!stickClicked)
            {
                var rightHand = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
                if (rightHand.isValid && rightHand.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxisClick, out bool clickVal) && clickVal)
                {
                    stickClicked = true;
                }
            }

            if (stickClicked && !wasStickClickHeld)
            {
                RoverCameraRig.Instance?.CyclePerspective();
            }
            wasStickClickHeld = stickClicked;

            // FreeFly mode must be active in RoverCameraRig
            bool isFreeMode = RoverCameraRig.Instance != null &&
                              RoverCameraRig.Instance.currentPerspective == RoverCameraRig.Perspective.Free;

            bool gripHeld = IsRightGripHeld();
            bool shouldFly = isFreeMode && gripHeld;

            if (shouldFly)
            {
                if (!wasGripHeld)
                {
                    OnFlyModeEntered();
                }

                ExecuteFlightMovement();
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
                    if (xrOriginTransform != null)
                    {
                        xrOriginTransform.position += currentVelocity * Time.deltaTime;
                    }
                }
            }

            wasGripHeld = shouldFly;
            isFlying = shouldFly;
        }

        /// <summary>
        /// Reads whether the Right Grip is currently held via Input System, OpenXR device, or desktop key.
        /// </summary>
        public bool IsRightGripHeld()
        {
#if ENABLE_INPUT_SYSTEM
            if (rightGripAction.action != null && rightGripAction.action.enabled)
            {
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

            // Desktop fallback
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
            if (rightThumbstickAction.action != null && rightThumbstickAction.action.enabled)
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
        /// Reads Button B on Right Controller (Up).
        /// </summary>
        private bool IsButtonBPressed()
        {
#if ENABLE_INPUT_SYSTEM
            if (rightButtonBAction.action != null && rightButtonBAction.action.enabled)
            {
                if (rightButtonBAction.action.IsPressed()) return true;
            }
#endif
            var rightHand = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            if (rightHand.isValid && rightHand.TryGetFeatureValue(UnityEngine.XR.CommonUsages.secondaryButton, out bool bVal))
            {
                if (bVal) return true;
            }

            // Desktop fallback: KeyCode.B
            if (Input.GetKey(KeyCode.B) || Input.GetKey(KeyCode.E)) return true;

            return false;
        }

        /// <summary>
        /// Reads Button A on Right Controller (Down).
        /// </summary>
        private bool IsButtonAPressed()
        {
#if ENABLE_INPUT_SYSTEM
            if (rightButtonAAction.action != null && rightButtonAAction.action.enabled)
            {
                if (rightButtonAAction.action.IsPressed()) return true;
            }
#endif
            var rightHand = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            if (rightHand.isValid && rightHand.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out bool aVal))
            {
                if (aVal) return true;
            }

            // Desktop fallback: KeyCode.A (when grip is held) or KeyCode.Q
            if (Input.GetKey(KeyCode.Q) || (Input.GetKey(KeyCode.N) && Input.GetKey(desktopGripKey))) return true;

            return false;
        }

        /// <summary>
        /// Executes full 3D translation:
        /// Horizontal movement along horizontal camera forward/right.
        /// Vertical movement via Button B (Up) and Button A (Down).
        /// Headset tracking is NEVER overwritten.
        /// </summary>
        private void ExecuteFlightMovement()
        {
            if (xrCamera == null)
            {
                ResolveXRCamera();
                if (xrCamera == null) return;
            }

            Vector2 thumbstick = ReadRightThumbstick();

            // Project forward and right onto horizontal XZ plane so head pitch does not alter altitude
            Vector3 camForward = Vector3.ProjectOnPlane(xrCamera.transform.forward, Vector3.up).normalized;
            if (camForward.sqrMagnitude < 0.001f) camForward = xrCamera.transform.forward;

            Vector3 camRight = Vector3.ProjectOnPlane(xrCamera.transform.right, Vector3.up).normalized;
            if (camRight.sqrMagnitude < 0.001f) camRight = xrCamera.transform.right;

            Vector3 targetDirection = (camForward * thumbstick.y + camRight * thumbstick.x);

            // Vertical movement: Button B = Up (+Y), Button A = Down (-Y)
            if (IsButtonBPressed())
            {
                targetDirection += Vector3.up;
            }
            if (IsButtonAPressed())
            {
                targetDirection -= Vector3.up;
            }

            Vector3 targetVelocity = targetDirection * flySpeed;

            currentVelocity = Vector3.Lerp(currentVelocity, targetVelocity, Time.deltaTime * acceleration);

            if (xrOriginTransform != null)
            {
                xrOriginTransform.position += currentVelocity * Time.deltaTime;
            }
        }

        private void OnFlyModeEntered()
        {
            SuppressSnapTurn();
            SetRightRayVisualVisible(false);
            Debug.Log("[FreeFlyRigController] VR Free-Fly translation engaged (Right Grip held). Controller ray visual hidden. Snap-turn suppressed.");
        }

        private void OnFlyModeExited()
        {
            RestoreSnapTurn();
            SetRightRayVisualVisible(true);
            currentVelocity = Vector3.zero;
            Debug.Log("[FreeFlyRigController] VR Free-Fly translation released. Controller ray visual restored. Snap-turn restored.");
        }

        private void SuppressSnapTurn()
        {
            if (snapTurnProvider == null && xrOriginTransform != null)
            {
                snapTurnProvider = xrOriginTransform.GetComponentInChildren<SnapTurnProvider>();
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

        private void CacheRightControllerVisuals()
        {
            if (xrOriginTransform == null) return;

            Transform rightController = xrOriginTransform.Find("Camera Offset/Right Controller");
            if (rightController == null)
            {
                foreach (var t in xrOriginTransform.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == "Right Controller")
                    {
                        rightController = t;
                        break;
                    }
                }
            }

            if (rightController != null)
            {
                if (rightLineRenderer == null)
                {
                    rightLineRenderer = rightController.GetComponentInChildren<LineRenderer>(true);
                }

                if (rightLineVisual == null)
                {
                    foreach (var comp in rightController.GetComponentsInChildren<MonoBehaviour>(true))
                    {
                        if (comp != null && comp.GetType().Name.Contains("LineVisual"))
                        {
                            rightLineVisual = comp;
                            break;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Toggles visibility of the right controller ray line visual without disabling
        /// the underlying XRRayInteractor, ensuring grab and interaction remain intact.
        /// </summary>
        public void SetRightRayVisualVisible(bool visible)
        {
            if (rightLineVisual == null && rightLineRenderer == null)
            {
                CacheRightControllerVisuals();
            }

            if (rightLineVisual != null && rightLineVisual.enabled != visible)
            {
                rightLineVisual.enabled = visible;
            }

            if (rightLineRenderer != null && rightLineRenderer.enabled != visible)
            {
                rightLineRenderer.enabled = visible;
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

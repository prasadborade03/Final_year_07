using System;
using UnityEngine;
using UnityEngine.XR;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace ProjectName.Rover
{
    /// <summary>
    /// Centralized Drive Input Provider for both Desktop (Keyboard/Mouse) and Virtual Reality (Meta Quest / OpenXR).
    /// 
    /// Mappings:
    /// - Desktop: W/S or Up/Down = Throttle [-1, 1], A/D or Left/Right = Steer [-1, 1]
    /// - VR: Left Thumbstick Y = Throttle [-1, 1], Left Thumbstick X = Steer [-1, 1]
    /// - Speed Regimes:
    ///     Desktop: Keys 1 (Stop 0%), 2 (Precision 25%), 3 (Explore 60%), 4 (Cruise 100%)
    ///     VR: Button A (Right controller) or Button X (Left controller) cycles 1 -> 2 -> 3 -> 4 -> 1
    /// - Deadzone: ~0.15 with smooth remapping to eliminate stick drift
    /// - Safety Gates: Muted during Placement Mode, Desktop Free-Fly Camera grab, or UI text editing
    /// </summary>
    [DisallowMultipleComponent]
    public class RoverInputProvider : MonoBehaviour
    {
        public enum SpeedRegime
        {
            Stop = 0,
            Precision = 1,
            Explore = 2,
            Cruise = 3
        }

        private static RoverInputProvider _instance;
        public static RoverInputProvider Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = UnityEngine.Object.FindAnyObjectByType<RoverInputProvider>();
                    if (_instance == null)
                    {
                        var go = new GameObject("RoverInputProvider");
                        _instance = go.AddComponent<RoverInputProvider>();
                        if (Application.isPlaying)
                        {
                            UnityEngine.Object.DontDestroyOnLoad(go);
                        }
                    }
                }
                return _instance;
            }
            private set { _instance = value; }
        }

        [Header("Tuning")]
        [Tooltip("Thumbstick deadzone threshold (0.15 recommended)")]
        [Range(0.05f, 0.35f)]
        public float deadzone = 0.15f;

        [Header("State")]
        [SerializeField]
        private SpeedRegime currentRegime = SpeedRegime.Cruise;

        public static SpeedRegime CurrentRegime
        {
            get => Instance != null ? Instance.currentRegime : SpeedRegime.Cruise;
            private set
            {
                if (Instance != null) Instance.currentRegime = value;
            }
        }

        public static string CurrentDriveModeString => CurrentRegime.ToString().ToUpperInvariant();

        // Event for active rover controllers to subscribe to
        public static event Action<string> OnDriveModeChanged;

        // Input Actions for New Input System
#if ENABLE_INPUT_SYSTEM
        private InputAction leftStickAction;
        private InputAction rightAAction;
        private InputAction leftXAction;
#endif

        private bool wasRightAPressed = false;
        private bool wasLeftXPressed = false;

        private void Awake()
        {
            if (_instance == null)
            {
                _instance = this;
            }
            else if (_instance != this)
            {
                Destroy(gameObject);
                return;
            }

            InitializeInputActions();
        }

        private void OnEnable()
        {
            EnableInputActions();
        }

        private void OnDisable()
        {
            DisableInputActions();
        }

        private void OnDestroy()
        {
            DisposeInputActions();
            if (_instance == this)
            {
                _instance = null;
            }
        }

        private void InitializeInputActions()
        {
#if ENABLE_INPUT_SYSTEM
            try
            {
                if (leftStickAction == null)
                {
                    leftStickAction = new InputAction("VR_LeftThumbstick", InputActionType.Value, expectedControlType: "Vector2");
                    leftStickAction.AddBinding("<XRController>{LeftHand}/thumbstick");
                    leftStickAction.AddBinding("<Gamepad>/leftStick");
                }

                if (rightAAction == null)
                {
                    rightAAction = new InputAction("VR_ButtonA", InputActionType.Button);
                    rightAAction.AddBinding("<XRController>{RightHand}/primaryButton");
                }

                if (leftXAction == null)
                {
                    leftXAction = new InputAction("VR_ButtonX", InputActionType.Button);
                    leftXAction.AddBinding("<XRController>{LeftHand}/primaryButton");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RoverInputProvider] InputActions initialization notice: {ex.Message}");
            }
#endif
        }

        private void EnableInputActions()
        {
#if ENABLE_INPUT_SYSTEM
            try
            {
                leftStickAction?.Enable();
                rightAAction?.Enable();
                leftXAction?.Enable();
            }
            catch {}
#endif
        }

        private void DisableInputActions()
        {
#if ENABLE_INPUT_SYSTEM
            try
            {
                leftStickAction?.Disable();
                rightAAction?.Disable();
                leftXAction?.Disable();
            }
            catch {}
#endif
        }

        private void DisposeInputActions()
        {
#if ENABLE_INPUT_SYSTEM
            try
            {
                leftStickAction?.Dispose();
                rightAAction?.Dispose();
                leftXAction?.Dispose();
                leftStickAction = null;
                rightAAction = null;
                leftXAction = null;
            }
            catch {}
#endif
        }

        private void Update()
        {
            CheckSpeedRegimeInputs();
        }

        // =========================================================================
        // PUBLIC API
        // =========================================================================

        /// <summary>
        /// Reads current continuous drive vector:
        /// x = Steer [-1 left, +1 right]
        /// y = Throttle [-1 reverse, +1 forward]
        /// Automatically returns Vector2.zero if placement is active or inputs are blocked.
        /// </summary>
        public static Vector2 GetDriveInput()
        {
            if (IsInputBlocked())
                return Vector2.zero;

            return Instance.CalculateDriveInput();
        }

        /// <summary>
        /// True if inputs should be suppressed (placement mode, camera grab, UI typing, or no rover).
        /// </summary>
        public static bool IsInputBlocked()
        {
            // 1. Desktop free-fly camera grab is active
            if (DesktopFreeFlyCamera.IsFreeFlyGrabActive)
                return true;

            // 2. Rover placement mode is active (thumbsticks only rotate ghost marker heading)
            if (RoverPlacementController.Instance != null && RoverPlacementController.Instance.IsPlacementActive)
                return true;

            var flow = UnityEngine.Object.FindAnyObjectByType<SimulationFlowController>();
            if (flow != null && flow.CurrentState == SimulationFlowController.State.WaitingForClickToPlace)
                return true;

            // 3. UI text input field focused
            if (IsTextInputFocused())
                return true;

            return false;
        }

        public static void SetSpeedRegime(SpeedRegime regime)
        {
            Instance.currentRegime = regime;
            string modeStr = CurrentDriveModeString;
            Debug.Log($"[RoverInputProvider] Speed regime set to: {modeStr} ({GetSpeedMultiplier(regime):P0})");
            OnDriveModeChanged?.Invoke(modeStr);
        }

        public static void SetDriveMode(string modeName)
        {
            if (string.IsNullOrEmpty(modeName)) return;
            switch (modeName.Trim().ToUpperInvariant())
            {
                case "STOP":
                    SetSpeedRegime(SpeedRegime.Stop);
                    break;
                case "PRECISION":
                    SetSpeedRegime(SpeedRegime.Precision);
                    break;
                case "EXPLORE":
                    SetSpeedRegime(SpeedRegime.Explore);
                    break;
                case "CRUISE":
                default:
                    SetSpeedRegime(SpeedRegime.Cruise);
                    break;
            }
        }

        public static void CycleSpeedRegime()
        {
            int next = ((int)CurrentRegime + 1) % 4;
            SetSpeedRegime((SpeedRegime)next);
        }

        public static float GetSpeedMultiplier(SpeedRegime regime)
        {
            switch (regime)
            {
                case SpeedRegime.Stop: return 0.0f;
                case SpeedRegime.Precision: return 0.25f;
                case SpeedRegime.Explore: return 0.60f;
                case SpeedRegime.Cruise:
                default: return 1.0f;
            }
        }

        // =========================================================================
        // INTERNAL INPUT PROCESSING
        // =========================================================================

        private Vector2 CalculateDriveInput()
        {
            Vector2 drive = Vector2.zero;

            // 1. VR XR Left Thumbstick
            Vector2 vrStick = ReadXRLeftStick();
            if (vrStick.sqrMagnitude > 0.0001f)
            {
                drive = vrStick;
            }

            // 2. Desktop Keyboard (WASD / Arrows)
            Vector2 kb = ReadKeyboardInput();
            if (kb.sqrMagnitude > 0.0001f)
            {
                drive.x = Mathf.Clamp(drive.x + kb.x, -1f, 1f);
                drive.y = Mathf.Clamp(drive.y + kb.y, -1f, 1f);
            }

            // 3. Gamepad Left Stick (if VR stick was idle)
            if (vrStick.sqrMagnitude <= 0.0001f)
            {
                Vector2 gp = ReadGamepadLeftStick();
                if (gp.sqrMagnitude > 0.0001f)
                {
                    drive.x = Mathf.Clamp(drive.x + gp.x, -1f, 1f);
                    drive.y = Mathf.Clamp(drive.y + gp.y, -1f, 1f);
                }
            }

            return drive;
        }

        private Vector2 ReadXRLeftStick()
        {
            Vector2 rawStick = Vector2.zero;

#if ENABLE_INPUT_SYSTEM
            if (leftStickAction != null && leftStickAction.enabled)
            {
                Vector2 val = leftStickAction.ReadValue<Vector2>();
                if (val.sqrMagnitude > rawStick.sqrMagnitude)
                {
                    rawStick = val;
                }
            }
#endif

            // OpenXR Subsystem fallback for Meta Quest Left Controller
            var leftHand = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            if (leftHand.isValid && leftHand.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out Vector2 openXrStick))
            {
                if (openXrStick.sqrMagnitude > rawStick.sqrMagnitude)
                {
                    rawStick = openXrStick;
                }
            }

            return ApplyDeadzone(rawStick, deadzone);
        }

        private Vector2 ReadKeyboardInput()
        {
            Vector2 kb = Vector2.zero;

#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null)
            {
                if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) kb.y += 1f;
                if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) kb.y -= 1f;
                if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) kb.x += 1f;
                if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) kb.x -= 1f;
            }
            else
#endif
            {
                try
                {
                    kb.y += Input.GetAxisRaw("Vertical");
                    kb.x += Input.GetAxisRaw("Horizontal");
                }
                catch {}
            }

            return kb;
        }

        private Vector2 ReadGamepadLeftStick()
        {
#if ENABLE_INPUT_SYSTEM
            if (Gamepad.current != null)
            {
                Vector2 stick = Gamepad.current.leftStick.ReadValue();
                return ApplyDeadzone(stick, deadzone);
            }
#endif
            return Vector2.zero;
        }

        public static Vector2 ApplyDeadzone(Vector2 input, float deadzoneThreshold)
        {
            float mag = input.magnitude;
            if (mag < deadzoneThreshold) return Vector2.zero;

            // Remap smoothly from [deadzoneThreshold, 1] to [0, 1]
            float remapped = (mag - deadzoneThreshold) / (1f - deadzoneThreshold);
            return input.normalized * Mathf.Clamp01(remapped);
        }

        private void CheckSpeedRegimeInputs()
        {
            bool cyclePressed = false;

#if ENABLE_INPUT_SYSTEM
            if (rightAAction != null && rightAAction.WasPressedThisFrame()) cyclePressed = true;
            if (leftXAction != null && leftXAction.WasPressedThisFrame()) cyclePressed = true;

            if (Keyboard.current != null && !IsTextInputFocused())
            {
                if (Keyboard.current.digit1Key.wasPressedThisFrame || Keyboard.current.numpad1Key.wasPressedThisFrame) SetSpeedRegime(SpeedRegime.Stop);
                else if (Keyboard.current.digit2Key.wasPressedThisFrame || Keyboard.current.numpad2Key.wasPressedThisFrame) SetSpeedRegime(SpeedRegime.Precision);
                else if (Keyboard.current.digit3Key.wasPressedThisFrame || Keyboard.current.numpad3Key.wasPressedThisFrame) SetSpeedRegime(SpeedRegime.Explore);
                else if (Keyboard.current.digit4Key.wasPressedThisFrame || Keyboard.current.numpad4Key.wasPressedThisFrame) SetSpeedRegime(SpeedRegime.Cruise);
            }
#endif

            // OpenXR primaryButton check on Right Hand (Quest 'A' button)
            var rightHand = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            if (rightHand.isValid && rightHand.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out bool rPressed))
            {
                if (rPressed && !wasRightAPressed)
                {
                    cyclePressed = true;
                }
                wasRightAPressed = rPressed;
            }

            // OpenXR primaryButton check on Left Hand (Quest 'X' button)
            var leftHand = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            if (leftHand.isValid && leftHand.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out bool lPressed))
            {
                if (lPressed && !wasLeftXPressed)
                {
                    cyclePressed = true;
                }
                wasLeftXPressed = lPressed;
            }

            // Legacy keyboard fallback
            if (!IsTextInputFocused())
            {
                if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1)) SetSpeedRegime(SpeedRegime.Stop);
                else if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2)) SetSpeedRegime(SpeedRegime.Precision);
                else if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3)) SetSpeedRegime(SpeedRegime.Explore);
                else if (Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.Keypad4)) SetSpeedRegime(SpeedRegime.Cruise);
            }

            if (cyclePressed)
            {
                CycleSpeedRegime();
            }
        }

        private static bool IsTextInputFocused()
        {
            var ui = UnityEngine.Object.FindAnyObjectByType<ProjectName.Terrain.UploadHeightmapUIToolkit>();
            return ui != null && ui.IsTextInputFocused();
        }
    }
}

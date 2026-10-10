using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Desktop Free-Fly Camera Controller for Flat Mode.
/// 
/// Controls:
/// - Hold RIGHT MOUSE BUTTON (Mouse 1):
///     * Mouse Drag: Look / rotate view (Yaw & Pitch)
///     * W / S: Move Forward / Backward relative to horizontal view direction
///     * A / D: Move Left / Right (Strafe)
///     * Q: Move Down (world -Y)
///     * E: Move Up (world +Y)
///     * Shift: Speed Boost (2.5x)
/// - Releasing RMB: Look capture ends immediately. Cursor returns to unlocked/visible.
/// - When RMB is NOT held: WASD drives the rover normally (zero conflict).
/// - Speed is dynamically tuned via the UI HUD slider (RoverCameraRig.FreeFlySpeed).
/// - Strictly disabled when a VR headset is active.
/// </summary>
[DisallowMultipleComponent]
public class DesktopFreeFlyCamera : MonoBehaviour
{
    private static DesktopFreeFlyCamera _instance;
    public static DesktopFreeFlyCamera Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = UnityEngine.Object.FindAnyObjectByType<DesktopFreeFlyCamera>();
            }
            return _instance;
        }
        private set => _instance = value;
    }

    public static bool IsFreeFlyGrabActive => Instance != null && Instance.isGrabActive;

    [Header("Speed & Dynamics")]
    [Tooltip("Base translation speed in meters/second (synced from HUD speed tuner)")]
    public float moveSpeed = 12f;
    [Tooltip("Speed multiplier when holding Shift")]
    public float boostMultiplier = 2.5f;
    [Tooltip("Acceleration / deceleration smoothing responsiveness")]
    public float acceleration = 8f;
    [Tooltip("Mouse look rotation sensitivity")]
    public float mouseSensitivity = 2.5f;

    [Header("Runtime State")]
    public bool isGrabActive = false;

    private Vector3 currentVelocity = Vector3.zero;
    private float yaw = 0f;
    private float pitch = 0f;
    private Transform targetMoveTransform;

    private void Awake()
    {
        if (_instance == null)
        {
            _instance = this;
        }
        else if (Instance != this)
        {
            Destroy(this);
            return;
        }

        // Determine if there is a parent XR Origin rig root to move
        var origin = GameObject.Find("XR Origin");
        if (origin != null && transform.IsChildOf(origin.transform))
        {
            targetMoveTransform = origin.transform;
        }
        else
        {
            targetMoveTransform = transform;
        }

        Vector3 euler = transform.eulerAngles;
        yaw = euler.y;
        pitch = euler.x > 180f ? euler.x - 360f : euler.x;
    }

    private void OnEnable()
    {
        ProjectName.Rover.RoverCameraRig.OnCameraSpeedChanged += HandleCameraSpeedChanged;
        moveSpeed = ProjectName.Rover.RoverCameraRig.FreeFlySpeed;
    }

    private void OnDisable()
    {
        ProjectName.Rover.RoverCameraRig.OnCameraSpeedChanged -= HandleCameraSpeedChanged;
        EndGrabMode();
    }

    private void HandleCameraSpeedChanged(float newSpeed)
    {
        moveSpeed = newSpeed;
    }

    private void Start()
    {
        moveSpeed = ProjectName.Rover.RoverCameraRig.FreeFlySpeed;
    }

    private void Update()
    {
        // Gate VR: strictly disabled on active VR headsets
        if (IsVRHeadsetActive())
        {
            if (isGrabActive)
            {
                EndGrabMode();
            }
            return;
        }

        // Keep move speed synced from HUD speed tuner
        moveSpeed = ProjectName.Rover.RoverCameraRig.FreeFlySpeed;

        bool rmbHeld = IsRMBHeld();
        bool altHeld = CheckAltGrabKeyHeld();

        bool shouldGrab = rmbHeld || altHeld;

        if (shouldGrab)
        {
            if (!isGrabActive)
            {
                StartGrabMode(rmbHeld);
            }

            if (rmbHeld)
            {
                HandleMouseLook();
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
    }

    private void StartGrabMode(bool lockCursor)
    {
        isGrabActive = true;
        currentVelocity = Vector3.zero;

        if (lockCursor)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        Vector3 currentAngles = transform.eulerAngles;
        yaw = currentAngles.y;
        pitch = currentAngles.x > 180f ? currentAngles.x - 360f : currentAngles.x;

        Debug.Log("[DesktopFreeFlyCamera] Free flight engaged (RMB held).");
    }

    public void EndGrabMode()
    {
        isGrabActive = false;
        currentVelocity = Vector3.zero;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        Debug.Log("[DesktopFreeFlyCamera] Free flight released.");
    }



    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus && isGrabActive)
        {
            EndGrabMode();
        }
    }

    /// <summary>
    /// Preserves current camera position and orientation when switching into Free mode.
    /// Eliminates teleporting or angle popping.
    /// </summary>
    public void SyncPoseFromCurrent(Vector3 worldPos, Quaternion worldRot)
    {
        Transform moveRoot = targetMoveTransform != null ? targetMoveTransform : transform;
        moveRoot.position = worldPos;
        transform.rotation = worldRot;

        Vector3 euler = worldRot.eulerAngles;
        yaw = euler.y;
        pitch = euler.x > 180f ? euler.x - 360f : euler.x;
        currentVelocity = Vector3.zero;
    }

    private void HandleMouseLook()
    {
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

        yaw += mouseX;
        pitch -= mouseY;
        pitch = Mathf.Clamp(pitch, -89f, 89f);

        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
    }

    private void HandleTranslation()
    {
        Transform moveRoot = targetMoveTransform != null ? targetMoveTransform : transform;

        Vector3 camForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        if (camForward.sqrMagnitude < 0.001f) camForward = transform.forward;

        Vector3 camRight = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;
        if (camRight.sqrMagnitude < 0.001f) camRight = transform.right;

        Vector3 targetInput = Vector3.zero;

#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) targetInput += camForward;
            if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) targetInput -= camForward;
            if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) targetInput += camRight;
            if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) targetInput -= camRight;

            // Q: Down (-Y), E: Up (+Y)
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

        bool isBoosted = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed))
        {
            isBoosted = true;
        }
#endif
        float targetSpeed = moveSpeed * (isBoosted ? boostMultiplier : 1.0f);
        Vector3 targetVelocity = targetInput.normalized * targetSpeed;

        currentVelocity = Vector3.Lerp(currentVelocity, targetVelocity, Time.deltaTime * acceleration);
        moveRoot.position += currentVelocity * Time.deltaTime;
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

    private bool CheckAltGrabKeyHeld()
    {
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

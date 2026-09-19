using UnityEngine;

/// <summary>
/// FreeFlyCamera controller matching the original intuitive controls:
/// - Horizontal: 8, 5, 4, 6 (both Keypad and Alpha)
/// - Vertical: 9 (Up), 7 (Down) (both Keypad and Alpha)
/// - Boost: Shift (fastSpeed)
/// - Look: Right Mouse Button (Mouse Button 1) + Mouse drag
///
/// Speed can be tuned via the UI Toolkit workbench (slider & presets).
/// </summary>
public class FreeFlyCamera : MonoBehaviour
{
    public static FreeFlyCamera Instance { get; private set; }

    [Header("Movement Settings")]
    public float normalSpeed = 10f;     // Normal flying speed
    public float fastSpeed = 30f;       // Speed when holding Shift
    public float climbSpeed = 10f;      // Speed for moving up/down (7/9)

    [Header("Look Settings")]
    public float mouseSensitivity = 3f; // Speed of mouse rotation

    // Event for UI Toolkit synchronization (baseSpeed, activeSpeed)
    public static System.Action<float, float> OnCameraSpeedChanged;

    /// <summary>
    /// Property for compatibility with UI Toolkit bindings
    /// </summary>
    public float movementSpeed
    {
        get => normalSpeed;
        set => SetMovementSpeed(value);
    }

    private float pitch = 0f;
    private float yaw = 0f;

    private void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        // Set the initial rotation based on the camera's current rotation in the scene
        Vector3 angles = transform.eulerAngles;
        pitch = angles.x;
        yaw = angles.y;

        OnCameraSpeedChanged?.Invoke(normalSpeed, normalSpeed);
    }

    void Update()
    {
        HandleRotation();
        HandleMovement();
    }

    private void HandleRotation()
    {
        // Look around only when holding the right mouse button (Mouse Button 1)
        if (Input.GetMouseButton(1))
        {
            yaw += Input.GetAxis("Mouse X") * mouseSensitivity;
            pitch -= Input.GetAxis("Mouse Y") * mouseSensitivity;

            // Clamp pitch to prevent the camera from flipping upside down
            pitch = Mathf.Clamp(pitch, -89f, 89f);

            transform.eulerAngles = new Vector3(pitch, yaw, 0f);
        }
    }

    private void HandleMovement()
    {
        // Choose speed based on whether the Shift key is held
        float currentSpeed = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? fastSpeed : normalSpeed;

        float moveX = 0f;
        float moveZ = 0f;

        // Key Remapping for Horizontal Movement (8, 5, 4, 6)
        if (Input.GetKey(KeyCode.Keypad8) || Input.GetKey(KeyCode.Alpha8)) moveZ += 1f; // Forward
        if (Input.GetKey(KeyCode.Keypad5) || Input.GetKey(KeyCode.Alpha5)) moveZ -= 1f; // Backward
        if (Input.GetKey(KeyCode.Keypad6) || Input.GetKey(KeyCode.Alpha6)) moveX += 1f; // Right
        if (Input.GetKey(KeyCode.Keypad4) || Input.GetKey(KeyCode.Alpha4)) moveX -= 1f; // Left

        // Move horizontally and forward/backward relative to the camera's rotation
        Vector3 moveDirection = new Vector3(moveX, 0, moveZ).normalized;
        transform.Translate(moveDirection * currentSpeed * Time.deltaTime, Space.Self);

        // Vertical Movement (7 = Down, 9 = Up)
        if (Input.GetKey(KeyCode.Keypad9) || Input.GetKey(KeyCode.Alpha9))
        {
            transform.position += Vector3.up * climbSpeed * Time.deltaTime; // Up
        }
        if (Input.GetKey(KeyCode.Keypad7) || Input.GetKey(KeyCode.Alpha7))
        {
            transform.position -= Vector3.up * climbSpeed * Time.deltaTime; // Down
        }
    }

    /// <summary>
    /// Changes speed from UI slider or external call.
    /// Fast speed (Shift) and climb speed scale proportionally.
    /// </summary>
    public void SetMovementSpeed(float speed)
    {
        normalSpeed = Mathf.Max(0.5f, speed);
        fastSpeed = normalSpeed * 3f;
        climbSpeed = normalSpeed;
        OnCameraSpeedChanged?.Invoke(normalSpeed, normalSpeed);
    }

    // UI Preset methods
    public void SetPrecisionPreset() => SetMovementSpeed(2.0f);
    public void SetStandardPreset() => SetMovementSpeed(10.0f);
    public void SetFastPreset() => SetMovementSpeed(50.0f);
    public void SetWarpPreset() => SetMovementSpeed(120.0f);

    public float GetMovementSpeed() => normalSpeed;
    public float GetCurrentCalculatedSpeed() => normalSpeed;

    public enum CameraPerspective
    {
        Front,
        Rear,
        Left,
        Right,
        Top
    }

    public void SetCameraPerspective(CameraPerspective view)
    {
        Transform targetTransform = null;
        var roverSync = FindAnyObjectByType<ProjectName.Rover.RoverPositionSync>();
        if (roverSync != null) targetTransform = roverSync.transform;

        if (targetTransform == null)
        {
            var telem = FindAnyObjectByType<ProjectName.Rover.RoverTelemetryProvider>();
            if (telem != null) targetTransform = telem.transform;
        }

        if (targetTransform == null)
        {
            var artBodies = FindObjectsByType<ArticulationBody>();
            foreach (var ab in artBodies)
            {
                if (ab.gameObject.name.Contains("Husky") || ab.gameObject.name.Contains("M20") || ab.isRoot)
                {
                    targetTransform = ab.transform;
                    break;
                }
            }
        }

        Vector3 targetCenter = targetTransform != null ? targetTransform.position + Vector3.up * 0.5f : transform.position + transform.forward * 5f;
        Vector3 forward = targetTransform != null ? targetTransform.forward : Vector3.forward;
        Vector3 right = targetTransform != null ? targetTransform.right : Vector3.right;
        Vector3 up = Vector3.up;

        float dist = 4.5f;
        float height = 2.0f;
        Vector3 newPos = targetCenter;
        Quaternion newRot = transform.rotation;

        switch (view)
        {
            case CameraPerspective.Front:
                newPos = targetCenter + forward * dist + up * (height * 0.6f);
                newRot = Quaternion.LookRotation((targetCenter - newPos).normalized, up);
                break;
            case CameraPerspective.Rear:
                newPos = targetCenter - forward * dist + up * height;
                newRot = Quaternion.LookRotation((targetCenter - newPos).normalized, up);
                break;
            case CameraPerspective.Left:
                newPos = targetCenter - right * dist + up * height;
                newRot = Quaternion.LookRotation((targetCenter - newPos).normalized, up);
                break;
            case CameraPerspective.Right:
                newPos = targetCenter + right * dist + up * height;
                newRot = Quaternion.LookRotation((targetCenter - newPos).normalized, up);
                break;
            case CameraPerspective.Top:
                newPos = targetCenter + up * (dist * 1.5f);
                newRot = Quaternion.LookRotation(Vector3.down, forward);
                break;
        }

        transform.position = newPos;
        transform.rotation = newRot;
        Vector3 euler = transform.eulerAngles;
        pitch = euler.x > 180f ? euler.x - 360f : euler.x;
        yaw = euler.y;
    }
}

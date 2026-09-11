using UnityEngine;

public class FreeFlyCamera : MonoBehaviour
{
    [Header("Movement Settings")]
    public float normalSpeed = 10f;     // Normal flying speed
    public float fastSpeed = 30f;       // Speed when holding Shift
    public float climbSpeed = 10f;      // Speed for moving up/down (7/9)

    [Header("Look Settings")]
    public float mouseSensitivity = 3f; // Speed of mouse rotation

    private float pitch = 0f;
    private float yaw = 0f;

    void Start()
    {
        // Set the initial rotation based on the camera's current rotation in the scene
        Vector3 angles = transform.eulerAngles;
        pitch = angles.x;
        yaw = angles.y;
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
}
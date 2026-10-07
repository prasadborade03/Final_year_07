using UnityEngine;
using ProjectName.Rover;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Production controller for Deep Robotics M20 Wheeled Quadruped.
/// 
/// Controls:
/// VR: Left Thumbstick (Y = Throttle, X = Steer)
/// Desktop: W/A/S/D or Arrow keys
/// Speed Regimes: VR Button A/X cycles 1->2->3->4->1, Desktop 1/2/3/4 keys
/// Q / E: Raise / lower all 4 knees
/// </summary>
public class RoverController_m20 : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Drag the RoverImporter_m20 component here")]
    public RoverImporter_m20 roverImporter;

    [Header("Wheel Drive & Speed Regimes")]
    [Tooltip("Current operational drive mode")]
    public string currentDriveMode = "CRUISE";

    [Tooltip("Base rated wheel speed in rad/s")]
    public float baseWheelSpeed = 25f;

    [Tooltip("Effective wheel speed in rad/s")]
    public float maxWheelSpeed = 25f;

    [Tooltip("Tick this if the robot drives backward when you press W")]
    public bool invertWheelMapping = false;

    [Header("Knee Control")]
    [Tooltip("How fast the knees move when holding Q or E (rad/s)")]
    public float kneeMoveSpeed = 1.5f;

    [Tooltip("Starting knee angle (radians). Same value used by the importer.")]
    public float kneeTarget = -1.2f;

    [Tooltip("Clamp range so the knees don't go crazy")]
    public float kneeMin = -2.5f;
    public float kneeMax = 0.5f;

    [Header("Input Blocking (UI Focus)")]
    public bool isInputBlocked = false;

    [Header("Overrides (Automation / Testing)")]
    public float overrideThrottle = 0f;
    public float overrideSteer = 0f;

    [Header("Analog Joystick Simulation")]
    [Tooltip("Rate at which virtual joystick deflects and returns to center (units/sec)")]
    public float joystickRampSpeed = 6.0f;
    [Tooltip("Current continuous analog joystick vector: x = steer [-1, 1], y = throttle [-1, 1]")]
    public Vector2 virtualJoystick = Vector2.zero;

    private void OnEnable()
    {
        RoverInputProvider.OnDriveModeChanged += SetDriveMode;
        SetDriveMode(RoverInputProvider.CurrentDriveModeString);
    }

    private void OnDisable()
    {
        RoverInputProvider.OnDriveModeChanged -= SetDriveMode;
    }

    /// <summary>
    /// Programmatic / VR controller input injection (acts as virtual joystick).
    /// </summary>
    public void SetInputs(float throttle, float steer)
    {
        virtualJoystick.y = Mathf.Clamp(throttle, -1f, 1f);
        virtualJoystick.x = Mathf.Clamp(steer, -1f, 1f);
    }

    // Cached knee bodies (found once after the importer has spawned the robot)
    private ArticulationBody flKnee, frKnee, hlKnee, hrKnee;
    private bool kneesFound = false;

    public void SetDriveMode(string mode)
    {
        currentDriveMode = mode.ToUpperInvariant();
        switch (currentDriveMode)
        {
            case "STOP":
                maxWheelSpeed = 0f;
                break;
            case "PRECISION":
                maxWheelSpeed = baseWheelSpeed * 0.25f; // 6.25 rad/s
                break;
            case "EXPLORE":
                maxWheelSpeed = baseWheelSpeed * 0.60f; // 15 rad/s
                break;
            case "CRUISE":
            default:
                maxWheelSpeed = baseWheelSpeed; // 25 rad/s
                break;
        }
        Debug.Log($"[M20] Drive mode set to: {currentDriveMode} (maxWheelSpeed: {maxWheelSpeed})");
    }

    public void SetSpeedLimit(float factor)
    {
        maxWheelSpeed = baseWheelSpeed * Mathf.Clamp01(factor);
    }

    void Update()
    {
        if (roverImporter == null)
            return;

        // If typing in UI, DesktopFreeFlyCamera grab is active, or placement is active, block driving inputs
        if (isInputBlocked || DesktopFreeFlyCamera.IsFreeFlyGrabActive || RoverInputProvider.IsInputBlocked())
        {
            virtualJoystick = Vector2.MoveTowards(virtualJoystick, Vector2.zero, joystickRampSpeed * Time.deltaTime);
            roverImporter.SetWheelSpeeds(0f, 0f, 0f, 0f);
            return;
        }

        // -------------------------------------------------
        // 1. Wheel drive (RoverInputProvider - VR Left Stick & Desktop WASD)
        // -------------------------------------------------
        Vector2 driveInput = RoverInputProvider.GetDriveInput();
        float targetThrottle = Mathf.Clamp(driveInput.y + overrideThrottle, -1f, 1f);
        float targetSteer = Mathf.Clamp(driveInput.x + overrideSteer, -1f, 1f);

        // Continuous analog smoothing
        virtualJoystick.x = Mathf.MoveTowards(virtualJoystick.x, targetSteer, joystickRampSpeed * Time.deltaTime);
        virtualJoystick.y = Mathf.MoveTowards(virtualJoystick.y, targetThrottle, joystickRampSpeed * Time.deltaTime);

        // Classic tank / skid-steer mix for 4 wheels
        float left  = (virtualJoystick.y + virtualJoystick.x) * maxWheelSpeed;
        float right = (virtualJoystick.y - virtualJoystick.x) * maxWheelSpeed;

        if (!invertWheelMapping)
            roverImporter.SetWheelSpeeds(left, right, left, right);   // FL, FR, HL, HR
        else
            roverImporter.SetWheelSpeeds(-left, -right, -left, -right);

        // -------------------------------------------------
        // 2. Knee up / down (Q / E)
        // -------------------------------------------------
        if (!kneesFound)
            TryCacheKnees();

        if (kneesFound && !DesktopFreeFlyCamera.IsFreeFlyGrabActive)
        {
            bool changed = false;

#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null)
            {
                if (Keyboard.current.qKey.isPressed)
                {
                    kneeTarget += kneeMoveSpeed * Time.deltaTime;
                    changed = true;
                }
                if (Keyboard.current.eKey.isPressed)
                {
                    kneeTarget -= kneeMoveSpeed * Time.deltaTime;
                    changed = true;
                }
            }
            else
#endif
            {
                if (Input.GetKey(KeyCode.Q))
                {
                    kneeTarget += kneeMoveSpeed * Time.deltaTime;
                    changed = true;
                }
                if (Input.GetKey(KeyCode.E))
                {
                    kneeTarget -= kneeMoveSpeed * Time.deltaTime;
                    changed = true;
                }
            }

            if (changed)
            {
                kneeTarget = Mathf.Clamp(kneeTarget, kneeMin, kneeMax);
                ApplyKneeTarget(kneeTarget);
            }
        }
    }

    // -------------------------------------------------
    // Helpers
    // -------------------------------------------------
    private void TryCacheKnees()
    {
        flKnee = FindBody("fl_knee");
        frKnee = FindBody("fr_knee");
        hlKnee = FindBody("hl_knee");
        hrKnee = FindBody("hr_knee");

        if (flKnee && frKnee && hlKnee && hrKnee)
        {
            kneesFound = true;
            Debug.Log("[M20 Controller] Knees cached successfully");
        }
    }

    private ArticulationBody FindBody(string linkName)
    {
        var bodies = FindObjectsByType<ArticulationBody>();
        foreach (var b in bodies)
        {
            if (b.name == linkName || b.gameObject.name == linkName)
                return b;
        }
        return null;
    }

    private void ApplyKneeTarget(float angle)
    {
        SetDriveTarget(flKnee, angle);
        SetDriveTarget(frKnee, angle);
        SetDriveTarget(hlKnee, angle);
        SetDriveTarget(hrKnee, angle);
    }

    private void SetDriveTarget(ArticulationBody body, float angle)
    {
        if (body == null) return;

        ArticulationDrive drive = body.xDrive;
        drive.stiffness = 2000f;
        drive.damping = 200f;
        drive.forceLimit = 200f;
        drive.target = angle;
        drive.targetVelocity = 0f;
        body.xDrive = drive;
    }
}
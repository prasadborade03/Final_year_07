using UnityEngine;
using ProjectName.Rover;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Production keyboard & VR controller for Clearpath Husky A200 skid-steer rover.
/// 
/// Controls:
/// VR: Left Thumbstick (Y = Throttle, X = Steer)
/// Desktop: W/A/S/D or Arrow keys
/// Speed Regimes: VR Button A/X cycles 1->2->3->4->1, Desktop 1/2/3/4 keys
/// </summary>
public class RoverController_husky : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Drag the RoverImporter_husky component here")]
    public RoverImporter_husky roverImporter;

    [Header("Drive & Speed Regimes")]
    [Tooltip("Current operational drive mode")]
    public string currentDriveMode = "CRUISE";

    [Tooltip("Base rated wheel speed at 100% throttle")]
    public float baseWheelSpeed = 400f;

    [Tooltip("Effective wheel speed limit")]
    public float maxWheelSpeed = 400f;

    [Tooltip("Tick this if the robot drives backward when you press W")]
    public bool invertWheelMapping = false;

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

    public void SetDriveMode(string mode)
    {
        currentDriveMode = mode.ToUpperInvariant();
        switch (currentDriveMode)
        {
            case "STOP":
                maxWheelSpeed = 0f;
                break;
            case "PRECISION":
                maxWheelSpeed = baseWheelSpeed * 0.25f; // 100 rad/s
                break;
            case "EXPLORE":
                maxWheelSpeed = baseWheelSpeed * 0.60f; // 240 rad/s
                break;
            case "CRUISE":
            default:
                maxWheelSpeed = baseWheelSpeed; // 400 rad/s
                break;
        }
        Debug.Log($"[Husky] Drive mode set to: {currentDriveMode} (maxWheelSpeed: {maxWheelSpeed})");
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
            roverImporter.SetWheelSpeeds(0f, 0f);
            return;
        }

        // 1. Read target analog inputs from central provider (VR Left Stick + Desktop WASD + Gamepad)
        Vector2 driveInput = RoverInputProvider.GetDriveInput();
        float targetThrottle = Mathf.Clamp(driveInput.y + overrideThrottle, -1f, 1f);
        float targetSteer = Mathf.Clamp(driveInput.x + overrideSteer, -1f, 1f);

        // 2. Continuous analog joystick smoothing (ramps from 0 to 1 like physical stick)
        virtualJoystick.x = Mathf.MoveTowards(virtualJoystick.x, targetSteer, joystickRampSpeed * Time.deltaTime);
        virtualJoystick.y = Mathf.MoveTowards(virtualJoystick.y, targetThrottle, joystickRampSpeed * Time.deltaTime);

        // 3. Classic tank / skid-steer mix scaled by active speed regime
        float left  = (virtualJoystick.y + virtualJoystick.x) * maxWheelSpeed;
        float right = (virtualJoystick.y - virtualJoystick.x) * maxWheelSpeed;

        // 4. Send to importer
        if (!invertWheelMapping)
            roverImporter.SetWheelSpeeds(left, right);
        else
            roverImporter.SetWheelSpeeds(-left, -right);
    }
}

public class HuskyController : RoverController_husky {}
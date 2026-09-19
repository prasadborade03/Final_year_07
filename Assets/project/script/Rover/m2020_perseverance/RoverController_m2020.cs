using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Production Perseverance M2020 Rover Controller.
/// Pairs with RoverImporter_m2020 to provide multiple steering modes:
/// 1. Ackermann 4-Wheel Turn (Smooth curved paths with speed differential)
/// 2. Point Turn / Pivot In-Place (Zero radius 360 spin)
/// 3. Crab Steering (Diagonal translation)
/// 4. Differential / Tank Drive
/// Supports both New Input System (Keyboard.current) and legacy Input fallback.
/// </summary>
public class RoverController_m2020 : MonoBehaviour
{
    public enum DriveMode
    {
        Ackermann = 0,
        PointTurn = 1,
        Crab = 2,
        TankDrive = 3
    }

    [Header("Importer Reference")]
    public RoverImporter_m2020 importer;

    [Header("Control Parameters")]
    public DriveMode driveMode = DriveMode.Ackermann;
    public string currentSpeedMode = "CRUISE";
    public float baseSpeedDegPerSec = 240f;
    public float maxSpeedDegPerSec = 240f; // Wheel rotation velocity in deg/s
    public float maxSteerAngleDeg = 40f;   // Max steer angle in degrees
    public float inputSmoothing = 8f;      // Smooth acceleration ramps

    [Header("Dimensions for Ackermann Math (Meters)")]
    public float wheelBase = 2.2f;  // Distance between front & rear wheel axles
    public float trackWidth = 2.0f; // Distance between left & right wheels

    [Header("Keyboard / Input Mapping")]
    public string throttleAxis = "Vertical";
    public string steerAxis = "Horizontal";
    public KeyCode toggleModeKey = KeyCode.M;
    public KeyCode liftChassisKey = KeyCode.Space;

    [Header("Input Blocking (UI Focus)")]
    public bool isInputBlocked = false;

    [Header("Overrides (Automation / Testing)")]
    public float overrideThrottle = 0f;
    public float overrideSteer = 0f;

    // Smoothed inputs
    private float currentThrottle = 0f;
    private float currentSteering = 0f;
    private bool isChassisLifted = false;

    void Start()
    {
        if (importer == null)
        {
            importer = GetComponent<RoverImporter_m2020>();
            if (importer == null)
            {
                importer = FindAnyObjectByType<RoverImporter_m2020>();
            }
        }
    }

    public void SetSteeringMode(DriveMode mode)
    {
        driveMode = mode;
        Debug.Log($"[RoverController_m2020] Steering mode set to: {driveMode}");
    }

    public void SetDriveMode(string mode)
    {
        currentSpeedMode = mode.ToUpperInvariant();
        switch (currentSpeedMode)
        {
            case "STOP":
                maxSpeedDegPerSec = 0f;
                break;
            case "PRECISION":
                maxSpeedDegPerSec = baseSpeedDegPerSec * 0.25f; // 60 deg/s
                break;
            case "EXPLORE":
                maxSpeedDegPerSec = baseSpeedDegPerSec * 0.60f; // 144 deg/s
                break;
            case "CRUISE":
            default:
                maxSpeedDegPerSec = baseSpeedDegPerSec; // 240 deg/s
                break;
        }
        Debug.Log($"[RoverController_m2020] Speed regime set to: {currentSpeedMode} (maxSpeed: {maxSpeedDegPerSec} deg/s)");
    }

    void Update()
    {
        if (importer == null) return;

        if (isInputBlocked)
        {
            importer.SetWheelSpeeds(0f, 0f);
            return;
        }

        float rawThrottle = overrideThrottle;
        float rawSteer = overrideSteer;
        bool modeToggled = false;
        bool liftToggled = false;

#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) rawThrottle += 1f;
            if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) rawThrottle -= 1f;
            if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) rawSteer += 1f;
            if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) rawSteer -= 1f;

            if (Keyboard.current.mKey.wasPressedThisFrame) modeToggled = true;
            if (Keyboard.current.spaceKey.wasPressedThisFrame) liftToggled = true;

            // Numeric keys 1..4 select steering mode directly
            if (Keyboard.current.digit1Key.wasPressedThisFrame) SetSteeringMode(DriveMode.Ackermann);
            else if (Keyboard.current.digit2Key.wasPressedThisFrame) SetSteeringMode(DriveMode.PointTurn);
            else if (Keyboard.current.digit3Key.wasPressedThisFrame) SetSteeringMode(DriveMode.Crab);
            else if (Keyboard.current.digit4Key.wasPressedThisFrame) SetSteeringMode(DriveMode.TankDrive);
        }
        else
#endif
        {
            try
            {
                rawThrottle += Input.GetAxisRaw(throttleAxis);
                rawSteer += Input.GetAxisRaw(steerAxis);
                if (Input.GetKeyDown(toggleModeKey)) modeToggled = true;
                if (Input.GetKeyDown(liftChassisKey)) liftToggled = true;

                if (Input.GetKeyDown(KeyCode.Alpha1)) SetSteeringMode(DriveMode.Ackermann);
                else if (Input.GetKeyDown(KeyCode.Alpha2)) SetSteeringMode(DriveMode.PointTurn);
                else if (Input.GetKeyDown(KeyCode.Alpha3)) SetSteeringMode(DriveMode.Crab);
                else if (Input.GetKeyDown(KeyCode.Alpha4)) SetSteeringMode(DriveMode.TankDrive);
            }
            catch
            {
                // Fallback if legacy axes are unmapped
            }
        }

        // Toggle Steering Mode with key press [M]
        if (modeToggled)
        {
            driveMode = (DriveMode)(((int)driveMode + 1) % 4);
            Debug.Log($"[RoverController_m2020] Switched Drive Mode to: {driveMode}");
        }

        // Lift/Lower chassis ground clearance [Space]
        if (liftToggled)
        {
            isChassisLifted = !isChassisLifted;
            float targetOffset = isChassisLifted ? 8f : 0f; // 8 degrees lift
            if (importer != null)
            {
                importer.AdjustGroundClearance(targetOffset);
            }
        }

        // Smooth inputs to prevent abrupt physical jerks
        currentThrottle = Mathf.Lerp(currentThrottle, Mathf.Clamp(rawThrottle, -1f, 1f), Time.deltaTime * inputSmoothing);
        currentSteering = Mathf.Lerp(currentSteering, Mathf.Clamp(rawSteer, -1f, 1f), Time.deltaTime * inputSmoothing);

        ProcessDriveAndSteer();
    }

    private void ProcessDriveAndSteer()
    {
        if (importer == null) return;

        float targetSteerRad = 0f;
        float leftSpeed = 0f;
        float rightSpeed = 0f;

        switch (driveMode)
        {
            case DriveMode.Ackermann:
                targetSteerRad = currentSteering * (maxSteerAngleDeg * Mathf.Deg2Rad);
                
                // Calculate speed differential for inner and outer wheels
                if (Mathf.Abs(currentSteering) > 0.05f)
                {
                    float turnRadius = (wheelBase / 2f) / Mathf.Tan(Mathf.Abs(targetSteerRad) + 0.001f);
                    float speedInner = currentThrottle * maxSpeedDegPerSec * ((turnRadius - trackWidth/2f) / turnRadius);
                    float speedOuter = currentThrottle * maxSpeedDegPerSec * ((turnRadius + trackWidth/2f) / turnRadius);

                    if (currentSteering > 0) // Turning Right
                    {
                        leftSpeed = speedOuter;
                        rightSpeed = speedInner;
                    }
                    else // Turning Left
                    {
                        leftSpeed = speedInner;
                        rightSpeed = speedOuter;
                    }
                }
                else
                {
                    leftSpeed = currentThrottle * maxSpeedDegPerSec;
                    rightSpeed = currentThrottle * maxSpeedDegPerSec;
                }
                break;

            case DriveMode.PointTurn:
                // Corner wheels set to ~45 degrees (0.785 rad)
                targetSteerRad = 45f * Mathf.Deg2Rad;
                
                // Drive wheels in opposite directions to spin in place
                leftSpeed = currentSteering * maxSpeedDegPerSec;
                rightSpeed = -currentSteering * maxSpeedDegPerSec;
                break;

            case DriveMode.Crab:
                // All wheels turn in parallel direction
                targetSteerRad = currentSteering * (maxSteerAngleDeg * Mathf.Deg2Rad);
                leftSpeed = currentThrottle * maxSpeedDegPerSec;
                rightSpeed = currentThrottle * maxSpeedDegPerSec;
                break;

            case DriveMode.TankDrive:
                targetSteerRad = 0f; // Fixed straight wheels
                leftSpeed = (currentThrottle + currentSteering) * maxSpeedDegPerSec;
                rightSpeed = (currentThrottle - currentSteering) * maxSpeedDegPerSec;
                break;
        }

        // Send commands to importer
        importer.SetSteerAngle(targetSteerRad);
        importer.SetWheelSpeeds(leftSpeed, rightSpeed);
    }

    /// <summary>
    /// External API to set control inputs programmatically (e.g. from UI buttons or ROS bridge)
    /// </summary>
    public void SetInputs(float throttle, float steering)
    {
        currentThrottle = Mathf.Clamp(throttle, -1f, 1f);
        currentSteering = Mathf.Clamp(steering, -1f, 1f);
        ProcessDriveAndSteer();
    }
}

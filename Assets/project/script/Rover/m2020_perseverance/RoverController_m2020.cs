using UnityEngine;

/// <summary>
/// Production Perseverance M2020 Rover Controller.
/// Pairs with RoverImporter_m2020 to provide multiple steering modes:
/// 1. Ackermann 4-Wheel Turn (Smooth curved paths with speed differential)
/// 2. Point Turn / Pivot In-Place (Zero radius 360 spin)
/// 3. Crab Steering (Diagonal translation)
/// 4. Differential / Tank Drive
/// </summary>
public class RoverController_m2020 : MonoBehaviour
{
    public enum DriveMode
    {
        Ackermann,
        PointTurn,
        Crab,
        TankDrive
    }

    [Header("Importer Reference")]
    public RoverImporter_m2020 importer;

    [Header("Control Parameters")]
    public DriveMode driveMode = DriveMode.Ackermann;
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
                importer = FindFirstObjectByType<RoverImporter_m2020>();
            }
        }
    }

    void Update()
    {
        // Toggle Steering Mode with key press
        if (Input.GetKeyDown(toggleModeKey))
        {
            driveMode = (DriveMode)(((int)driveMode + 1) % 4);
            Debug.Log($"[RoverController] Switched Drive Mode to: {driveMode}");
        }

        // Lift/Lower chassis ground clearance
        if (Input.GetKeyDown(liftChassisKey))
        {
            isChassisLifted = !isChassisLifted;
            float targetOffset = isChassisLifted ? 8f : 0f; // 8 degrees lift
            if (importer != null)
            {
                importer.AdjustGroundClearance(targetOffset);
            }
        }

        // Raw inputs
        float rawThrottle = Input.GetAxisRaw(throttleAxis);
        float rawSteer = Input.GetAxisRaw(steerAxis);

        // Smooth inputs to prevent abrupt physical jerks
        currentThrottle = Mathf.Lerp(currentThrottle, rawThrottle, Time.deltaTime * inputSmoothing);
        currentSteering = Mathf.Lerp(currentSteering, rawSteer, Time.deltaTime * inputSmoothing);

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

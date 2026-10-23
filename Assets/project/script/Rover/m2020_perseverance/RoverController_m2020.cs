using UnityEngine;
using ProjectName.Rover;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Production Perseverance M2020 Rover Controller.
/// Pairs with RoverImporter_m2020 to provide multiple steering modes:
/// 1. Ackermann 4-Wheel Turn (Smooth curved paths with individual speed/geometry differential)
/// 2. Point Turn / Pivot In-Place (Zero radius 360 spin with geometric toe-in)
/// 3. Crab Steering (Diagonal translation with straight middle wheels)
/// 4. Differential / Tank Drive (Fixed straight wheels, differential velocity)
/// Supports VR Left Stick, Desktop WASD/Arrows, and Gamepad.
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

    public struct WheelCommands
    {
        public float steerLF; // in degrees
        public float steerRF;
        public float steerLR;
        public float steerRR;

        public float speedLF; // in deg/s
        public float speedRF;
        public float speedLM;
        public float speedRM;
        public float speedLR;
        public float speedRR;
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
    public float steerSlewRateDegPerSec = 90f; // Smooth steer servo transitions

    [Header("Dimensions for Ackermann Math (Fallback, Meters)")]
    public float wheelBase = 2.2f;  // Fallback distance between front & rear wheel axles
    public float trackWidth = 2.0f; // Fallback distance between left & right wheels

    [Header("Keyboard / Input Mapping")]
    public string throttleAxis = "Vertical";
    public string steerAxis = "Horizontal";
    public KeyCode toggleModeKey = KeyCode.M;
    public KeyCode liftChassisKey = KeyCode.Space;

    [Header("Input Blocking (UI Focus)")]
    public bool isInputBlocked = false;

    // Smoothed inputs
    private float currentThrottle = 0f;
    private float currentSteering = 0f;
    private bool isChassisLifted = false;

    // Active smoothed steer targets (degrees)
    private float activeSteerLF = 0f;
    private float activeSteerRF = 0f;
    private float activeSteerLR = 0f;
    private float activeSteerRR = 0f;

    private void OnEnable()
    {
        RoverInputProvider.OnDriveModeChanged += SetDriveMode;
        SetDriveMode(RoverInputProvider.CurrentDriveModeString);
    }

    private void OnDisable()
    {
        RoverInputProvider.OnDriveModeChanged -= SetDriveMode;
    }

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

    [Header("Analog Joystick Simulation")]
    public Vector2 virtualJoystick => new Vector2(currentSteering, currentThrottle);

    void Update()
    {
        if (importer == null) return;

        // If typing in UI, DesktopFreeFlyCamera grab is active, or placement is active, block driving inputs
        if (isInputBlocked || DesktopFreeFlyCamera.IsFreeFlyGrabActive || RoverInputProvider.IsInputBlocked())
        {
            currentThrottle = Mathf.Lerp(currentThrottle, 0f, Time.deltaTime * inputSmoothing);
            currentSteering = Mathf.Lerp(currentSteering, 0f, Time.deltaTime * inputSmoothing);
            importer.SetWheelSpeeds(0f, 0f, 0f, 0f, 0f, 0f);
            return;
        }

        Vector2 driveInput = RoverInputProvider.GetDriveInput();
        float rawThrottle = driveInput.y;
        float rawSteer = driveInput.x;
        bool modeToggled = false;
        bool liftToggled = false;

#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            if (Keyboard.current.mKey.wasPressedThisFrame) modeToggled = true;
            if (Keyboard.current.spaceKey.wasPressedThisFrame) liftToggled = true;
        }
        else
#endif
        {
            try
            {
                if (Input.GetKeyDown(toggleModeKey)) modeToggled = true;
                if (Input.GetKeyDown(liftChassisKey)) liftToggled = true;
            }
            catch {}
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

        // Smooth inputs to prevent abrupt physical jerks (analog joystick ramp)
        currentThrottle = Mathf.Lerp(currentThrottle, Mathf.Clamp(rawThrottle, -1f, 1f), Time.deltaTime * inputSmoothing);
        currentSteering = Mathf.Lerp(currentSteering, Mathf.Clamp(rawSteer, -1f, 1f), Time.deltaTime * inputSmoothing);

        ProcessDriveAndSteer();
    }

    private void ProcessDriveAndSteer()
    {
        if (importer == null) return;

        WheelCommands targetCmd;
        ComputeWheelCommands(driveMode, currentThrottle, currentSteering, out targetCmd);

        // Smoothly slew steer angles to eliminate physical jerk and prevent jumps across mode changes
        float step = steerSlewRateDegPerSec * Time.deltaTime;
        activeSteerLF = Mathf.MoveTowards(activeSteerLF, targetCmd.steerLF, step);
        activeSteerRF = Mathf.MoveTowards(activeSteerRF, targetCmd.steerRF, step);
        activeSteerLR = Mathf.MoveTowards(activeSteerLR, targetCmd.steerLR, step);
        activeSteerRR = Mathf.MoveTowards(activeSteerRR, targetCmd.steerRR, step);

        importer.SetSteerAnglesDeg(activeSteerLF, activeSteerRF, activeSteerLR, activeSteerRR);
        importer.SetWheelSpeeds(
            targetCmd.speedLF, targetCmd.speedRF,
            targetCmd.speedLM, targetCmd.speedRM,
            targetCmd.speedLR, targetCmd.speedRR
        );
    }

    /// <summary>
    /// Student Reference: Computes per-wheel steering angles (degrees) and wheel velocities (deg/s)
    /// based on the selected DriveMode and driver inputs (throttle, steer).
    /// Rover Coordinate Frame: X forward, Y left.
    /// Logical Steering Convention: +steer = right / clockwise turn from above.
    /// </summary>
    private void ComputeWheelCommands(DriveMode mode, float throttle, float steer, out WheelCommands cmd)
    {
        cmd = new WheelCommands();

        // Dynamic wheel positions measured directly from transforms (or fallback defaults)
        Vector2 pLF = importer != null && importer.isRoverReady ? importer.posLF : new Vector2(1.19f, 1.06f);
        Vector2 pRF = importer != null && importer.isRoverReady ? importer.posRF : new Vector2(1.19f, -1.06f);
        Vector2 pLM = importer != null && importer.isRoverReady ? importer.posLM : new Vector2(0.00f, 1.18f);
        Vector2 pRM = importer != null && importer.isRoverReady ? importer.posRM : new Vector2(0.00f, -1.18f);
        Vector2 pLR = importer != null && importer.isRoverReady ? importer.posLR : new Vector2(-1.07f, 1.06f);
        Vector2 pRR = importer != null && importer.isRoverReady ? importer.posRR : new Vector2(-1.07f, -1.06f);

        switch (mode)
        {
            // -----------------------------------------------------------------
            // 1. ACKERMANN 4-WHEEL COUNTER-STEER (Default)
            // -----------------------------------------------------------------
            // Front wheels turn toward the turn (+steer = turn right); rear wheels counter-steer.
            // Turn center lies on the middle axle line (x = 0).
            // Inner wheel turns sharper than outer wheel.
            // Each wheel's rolling speed is proportional to its distance from the instantaneous turn center.
            case DriveMode.Ackermann:
            {
                if (Mathf.Abs(steer) < 0.01f)
                {
                    // Pure straight driving
                    cmd.steerLF = 0f;
                    cmd.steerRF = 0f;
                    cmd.steerLR = 0f;
                    cmd.steerRR = 0f;

                    float straightSpeed = throttle * maxSpeedDegPerSec;
                    cmd.speedLF = straightSpeed;
                    cmd.speedRF = straightSpeed;
                    cmd.speedLM = straightSpeed;
                    cmd.speedRM = straightSpeed;
                    cmd.speedLR = straightSpeed;
                    cmd.speedRR = straightSpeed;
                }
                else
                {
                    float s = Mathf.Clamp(steer, -1f, 1f);
                    float absS = Mathf.Abs(s);
                    float steerSign = Mathf.Sign(s); // +1 = turn right, -1 = turn left

                    // Inner track distance along Y
                    float yInner = (s > 0f) ? Mathf.Abs(pRF.y) : Mathf.Abs(pLF.y);
                    float xFront = pLF.x;

                    // Compute turn radius from rover centerline to ensure innermost wheel reaches maxSteerAngleDeg at |steer| = 1
                    float maxSteerRad = maxSteerAngleDeg * Mathf.Deg2Rad;
                    float R_turn = (xFront / Mathf.Tan(absS * maxSteerRad)) + yInner;

                    // In rover frame (Y left): turning right (+steer) places the turn center to the right (negative Y)
                    float R_center = -R_turn * steerSign;

                    // Per-wheel steer angles from instantaneous center of curvature (0, R_center)
                    // steerLF, steerRF (front): turn toward curve (+ for right turn)
                    // steerLR, steerRR (rear): counter-steer opposite (- for right turn)
                    cmd.steerLF = Mathf.Atan2(pLF.x, Mathf.Abs(pLF.y - R_center)) * steerSign * Mathf.Rad2Deg;
                    cmd.steerRF = Mathf.Atan2(pRF.x, Mathf.Abs(pRF.y - R_center)) * steerSign * Mathf.Rad2Deg;
                    cmd.steerLR = Mathf.Atan2(pLR.x, Mathf.Abs(pLR.y - R_center)) * steerSign * Mathf.Rad2Deg;
                    cmd.steerRR = Mathf.Atan2(pRR.x, Mathf.Abs(pRR.y - R_center)) * steerSign * Mathf.Rad2Deg;

                    // Per-wheel speed proportional to distance from turn center
                    float dLF = Mathf.Sqrt(pLF.x * pLF.x + (pLF.y - R_center) * (pLF.y - R_center));
                    float dRF = Mathf.Sqrt(pRF.x * pRF.x + (pRF.y - R_center) * (pRF.y - R_center));
                    float dLM = Mathf.Sqrt(pLM.x * pLM.x + (pLM.y - R_center) * (pLM.y - R_center));
                    float dRM = Mathf.Sqrt(pRM.x * pRM.x + (pRM.y - R_center) * (pRM.y - R_center));
                    float dLR = Mathf.Sqrt(pLR.x * pLR.x + (pLR.y - R_center) * (pLR.y - R_center));
                    float dRR = Mathf.Sqrt(pRR.x * pRR.x + (pRR.y - R_center) * (pRR.y - R_center));

                    float baseVel = throttle * maxSpeedDegPerSec;
                    cmd.speedLF = baseVel * (dLF / R_turn);
                    cmd.speedRF = baseVel * (dRF / R_turn);
                    cmd.speedLM = baseVel * (dLM / R_turn);
                    cmd.speedRM = baseVel * (dRM / R_turn);
                    cmd.speedLR = baseVel * (dLR / R_turn);
                    cmd.speedRR = baseVel * (dRR / R_turn);
                }
                break;
            }

            // -----------------------------------------------------------------
            // 2. POINT TURN (In-Place 360 Spin)
            // -----------------------------------------------------------------
            // All 4 corner wheels align with the tangent of the circle about rover center (toe-in ~45-48 deg).
            // Throttle is ignored. D spins clockwise, A spins counter-clockwise.
            // Speed is proportional to radius from rover center (corner wheels faster than middle wheels).
            case DriveMode.PointTurn:
            {
                float thetaLF = Mathf.Atan2(Mathf.Abs(pLF.x), Mathf.Abs(pLF.y)) * Mathf.Rad2Deg;
                float thetaRF = Mathf.Atan2(Mathf.Abs(pRF.x), Mathf.Abs(pRF.y)) * Mathf.Rad2Deg;
                float thetaLR = Mathf.Atan2(Mathf.Abs(pLR.x), Mathf.Abs(pLR.y)) * Mathf.Rad2Deg;
                float thetaRR = Mathf.Atan2(Mathf.Abs(pRR.x), Mathf.Abs(pRR.y)) * Mathf.Rad2Deg;

                // Point-turn tangent toe-in geometry: LF +, RF -, LR -, RR +
                cmd.steerLF =  thetaLF;
                cmd.steerRF = -thetaRF;
                cmd.steerLR = -thetaLR;
                cmd.steerRR =  thetaRR;

                // Distances from center (0, 0)
                float rCorner = Mathf.Sqrt(pLF.x * pLF.x + pLF.y * pLF.y);
                float rMid = Mathf.Abs(pLM.y);
                float kMid = rCorner > 0.001f ? (rMid / rCorner) : 0.74f;

                // Left side drives forward (+steer), Right side drives backward (-steer) to spin clockwise
                float vCorner = steer * maxSpeedDegPerSec;
                float vMid = vCorner * kMid;

                cmd.speedLF =  vCorner;
                cmd.speedLM =  vMid;
                cmd.speedLR =  vCorner;

                cmd.speedRF = -vCorner;
                cmd.speedRM = -vMid;
                cmd.speedRR = -vCorner;
                break;
            }

            // -----------------------------------------------------------------
            // 3. CRAB STEERING (Diagonal Translation)
            // -----------------------------------------------------------------
            // All four corner wheels turn parallel to the steer heading (A/D).
            // W/S drives along that heading.
            // Middle wheels stay straight (0 deg) and receive only the forward cosine component.
            case DriveMode.Crab:
            {
                float crabAngleDeg = steer * maxSteerAngleDeg;

                cmd.steerLF = crabAngleDeg;
                cmd.steerRF = crabAngleDeg;
                cmd.steerLR = crabAngleDeg;
                cmd.steerRR = crabAngleDeg;

                float vCorner = throttle * maxSpeedDegPerSec;
                float vForward = vCorner * Mathf.Cos(crabAngleDeg * Mathf.Deg2Rad);

                cmd.speedLF = vCorner;
                cmd.speedRF = vCorner;
                cmd.speedLM = vForward;
                cmd.speedRM = vForward;
                cmd.speedLR = vCorner;
                cmd.speedRR = vCorner;
                break;
            }

            // -----------------------------------------------------------------
            // 4. TANK DRIVE (Skid Steer)
            // -----------------------------------------------------------------
            // All corner steer wheels fixed at 0 deg.
            // Left = throttle + steer, Right = throttle - steer.
            // Same sign convention on both sides: positive velocity = forward.
            case DriveMode.TankDrive:
            {
                cmd.steerLF = 0f;
                cmd.steerRF = 0f;
                cmd.steerLR = 0f;
                cmd.steerRR = 0f;

                float vLeft = Mathf.Clamp(throttle + steer, -1f, 1f) * maxSpeedDegPerSec;
                float vRight = Mathf.Clamp(throttle - steer, -1f, 1f) * maxSpeedDegPerSec;

                cmd.speedLF = vLeft;
                cmd.speedLM = vLeft;
                cmd.speedLR = vLeft;

                cmd.speedRF = vRight;
                cmd.speedRM = vRight;
                cmd.speedRR = vRight;
                break;
            }
        }
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

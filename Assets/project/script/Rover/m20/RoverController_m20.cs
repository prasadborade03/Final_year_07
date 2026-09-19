using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Production controller for Deep Robotics M20 Wheeled Quadruped.
/// 
/// Controls:
/// W/A/S/D: Drive 4 wheels (skid-steer mix)
/// Q / E: Raise / lower all 4 knees
/// 1 / 2 / 3 / 4: Drive Mode presets (Stop, Precision, Explore, Cruise)
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

        if (isInputBlocked)
        {
            roverImporter.SetWheelSpeeds(0f, 0f, 0f, 0f);
            return;
        }

        // Numeric key shortcuts (1 = STOP, 2 = PRECISION, 3 = EXPLORE, 4 = CRUISE)
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            if (Keyboard.current.digit1Key.wasPressedThisFrame) SetDriveMode("STOP");
            else if (Keyboard.current.digit2Key.wasPressedThisFrame) SetDriveMode("PRECISION");
            else if (Keyboard.current.digit3Key.wasPressedThisFrame) SetDriveMode("EXPLORE");
            else if (Keyboard.current.digit4Key.wasPressedThisFrame) SetDriveMode("CRUISE");
        }
#endif
        if (Input.GetKeyDown(KeyCode.Alpha1)) SetDriveMode("STOP");
        else if (Input.GetKeyDown(KeyCode.Alpha2)) SetDriveMode("PRECISION");
        else if (Input.GetKeyDown(KeyCode.Alpha3)) SetDriveMode("EXPLORE");
        else if (Input.GetKeyDown(KeyCode.Alpha4)) SetDriveMode("CRUISE");

        // -------------------------------------------------
        // 1. Wheel drive (W A S D)
        // -------------------------------------------------
        float throttle = overrideThrottle;
        float steer = overrideSteer;

#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) throttle += 1f;
            if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) throttle -= 1f;
            if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) steer += 1f;
            if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) steer -= 1f;
        }
        else
#endif
        {
            throttle += Input.GetAxis("Vertical");
            steer += Input.GetAxis("Horizontal");
        }

        throttle = Mathf.Clamp(throttle, -1f, 1f);
        steer = Mathf.Clamp(steer, -1f, 1f);

        // Classic tank / skid-steer mix for 4 wheels
        float left  = (throttle + steer) * maxWheelSpeed;
        float right = (throttle - steer) * maxWheelSpeed;

        if (!invertWheelMapping)
            roverImporter.SetWheelSpeeds(left, right, left, right);   // FL, FR, HL, HR
        else
            roverImporter.SetWheelSpeeds(-left, -right, -left, -right);

        // -------------------------------------------------
        // 2. Knee up / down (Q / E)
        // -------------------------------------------------
        if (!kneesFound)
            TryCacheKnees();

        if (kneesFound)
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
        var bodies = FindObjectsByType<ArticulationBody>(FindObjectsSortMode.None);
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
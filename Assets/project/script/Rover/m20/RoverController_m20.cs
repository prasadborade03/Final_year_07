using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Simple keyboard controller for the Deep Robotics M20.
/// 
/// W/A/S/D  = drive the 4 wheels (tank / skid-steer mix)
/// Q / E    = raise / lower all knees
/// </summary>
public class RoverController_m20 : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Drag the RoverImporter_m20 component here")]
    public RoverImporter_m20 roverImporter;

    [Header("Wheel Drive")]
    [Tooltip("Wheel speed in rad/s (or deg/s depending on your Articulation setup)")]
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

    // Cached knee bodies (found once after the importer has spawned the robot)
    private ArticulationBody flKnee, frKnee, hlKnee, hrKnee;
    private bool kneesFound = false;

    void Update()
    {
        if (roverImporter == null || Keyboard.current == null)
            return;

        // -------------------------------------------------
        // 1. Wheel drive (W A S D)
        // -------------------------------------------------
        float throttle = 0f;
        if (Keyboard.current.wKey.isPressed) throttle += 1f;
        if (Keyboard.current.sKey.isPressed) throttle -= 1f;

        float steer = 0f;
        if (Keyboard.current.dKey.isPressed) steer += 1f;
        if (Keyboard.current.aKey.isPressed) steer -= 1f;

        // Classic tank / skid-steer mix for 4 wheels
        float left  = (throttle + steer) * maxWheelSpeed;
        float right = (throttle - steer) * maxWheelSpeed;

        // All four wheel axes in the URDF are the same direction (0 -1 0),
        // so we can send the same left/right values to front and rear.
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
        // The robot is spawned by the importer, so we search from the scene root
        // or from the importer's generated object if you expose it later.
        // For simplicity we search the whole scene for the named links.
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
        // Search under every ArticulationBody in the scene (safe for one robot)
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
        // Keep high stiffness so the knee holds the new angle
        drive.stiffness = 2000f;
        drive.damping = 200f;
        drive.forceLimit = 200f;
        drive.target = angle;
        drive.targetVelocity = 0f;
        body.xDrive = drive;
    }
}
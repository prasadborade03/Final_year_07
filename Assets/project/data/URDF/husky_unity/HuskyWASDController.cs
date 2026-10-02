using UnityEngine;

/// <summary>
/// Simple WASD differential-drive controller for Clearpath Husky (Unity ArticulationBody).
/// Attach this to the root of the robot.
/// </summary>
public class HuskyWASDController : MonoBehaviour
{
    [Header("Wheel ArticulationBodies")]
    [Tooltip("Front-left wheel ArticulationBody")]
    public ArticulationBody frontLeftWheel;
    [Tooltip("Front-right wheel ArticulationBody")]
    public ArticulationBody frontRightWheel;
    [Tooltip("Rear-left wheel ArticulationBody")]
    public ArticulationBody rearLeftWheel;
    [Tooltip("Rear-right wheel ArticulationBody")]
    public ArticulationBody rearRightWheel;

    [Header("Drive Parameters (tune these)")]
    [Tooltip("Maximum linear speed in m/s")]
    public float maxLinearSpeed = 1.5f;

    [Tooltip("Maximum angular speed in rad/s")]
    public float maxAngularSpeed = 2.0f;

    [Tooltip("Wheel radius in metres (Husky ≈ 0.1651)")]
    public float wheelRadius = 0.1651f;

    [Tooltip("Distance between left and right wheels (track width) in metres (Husky ≈ 0.555)")]
    public float trackWidth = 0.555f;

    [Header("Articulation Drive Settings")]
    [Tooltip("How stiff the velocity drive is")]
    public float driveStiffness = 100000f;

    [Tooltip("Damping of the velocity drive")]
    public float driveDamping = 10000f;

    [Tooltip("Maximum force the drive can apply")]
    public float driveForceLimit = 1000000f;

    [Header("Input Smoothing")]
    [Tooltip("How quickly the robot accelerates / decelerates (0 = instant)")]
    public float acceleration = 4f;

    // Internal state
    private float currentLinear = 0f;
    private float currentAngular = 0f;

    void Start()
    {
        // Configure all four wheels once at start
        ConfigureWheel(frontLeftWheel);
        ConfigureWheel(frontRightWheel);
        ConfigureWheel(rearLeftWheel);
        ConfigureWheel(rearRightWheel);
    }

    void Update()
    {
        // ----- Read WASD -----
        float targetLinear  = 0f;
        float targetAngular = 0f;

        if (Input.GetKey(KeyCode.W)) targetLinear  += maxLinearSpeed;
        if (Input.GetKey(KeyCode.S)) targetLinear  -= maxLinearSpeed;
        if (Input.GetKey(KeyCode.A)) targetAngular += maxAngularSpeed;   // left turn
        if (Input.GetKey(KeyCode.D)) targetAngular -= maxAngularSpeed;   // right turn

        // Smooth the commands
        currentLinear  = Mathf.MoveTowards(currentLinear,  targetLinear,  acceleration * Time.deltaTime);
        currentAngular = Mathf.MoveTowards(currentAngular, targetAngular, acceleration * Time.deltaTime);

        // Convert to left / right wheel speeds (rad/s)
        // v_left  = (v - ω * L/2) / r
        // v_right = (v + ω * L/2) / r
        float halfTrack = trackWidth * 0.5f;
        float leftSpeed  = (currentLinear - currentAngular * halfTrack) / wheelRadius;
        float rightSpeed = (currentLinear + currentAngular * halfTrack) / wheelRadius;

        // Apply to all wheels on each side
        SetWheelVelocity(frontLeftWheel,  leftSpeed);
        SetWheelVelocity(rearLeftWheel,   leftSpeed);
        SetWheelVelocity(frontRightWheel, rightSpeed);
        SetWheelVelocity(rearRightWheel,  rightSpeed);
    }

    // ---------- Helpers ----------

    void ConfigureWheel(ArticulationBody wheel)
    {
        if (wheel == null) return;

        var drive = wheel.xDrive;          // continuous joints use the X drive in Unity
        drive.stiffness   = driveStiffness;
        drive.damping     = driveDamping;
        drive.forceLimit  = driveForceLimit;
        drive.driveType   = ArticulationDriveType.Velocity;   // important!
        wheel.xDrive = drive;
    }

    void SetWheelVelocity(ArticulationBody wheel, float velocity)
    {
        if (wheel == null) return;

        var drive = wheel.xDrive;
        drive.targetVelocity = velocity;
        wheel.xDrive = drive;
    }

    // Optional: visual feedback in the Scene view
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, 0.3f);
    }
}
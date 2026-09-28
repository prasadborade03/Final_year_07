using UnityEngine;

/// <summary>
/// Attach together with M20RoverImporter on the robot root.
/// Controls the four wheels (velocity) + leg joints (position) for basic driving & posture.
/// </summary>
[RequireComponent(typeof(M20RoverImporter))]
public class M20RoverController : MonoBehaviour
{
    [Header("References")]
    public M20RoverImporter importer;

    [Header("Drive")]
    public float maxWheelSpeed = 25f;      // rad/s  (≈ 2.25 m/s with 0.09 m radius)
    public float turnSpeedMultiplier = 1.2f;

    [Header("Stance / Height")]
    [Tooltip("Neutral hipY angle (rad)")]
    public float neutralHipY = 0.6f;
    [Tooltip("Neutral knee angle (rad)")]
    public float neutralKnee = -1.2f;
    public float heightStep = 0.15f;       // how much Q/E changes the angles
    public float maxHeightOffset = 0.6f;

    float currentHeightOffset = 0f;

    // Convenience arrays
    ArticulationBody[] wheels;
    ArticulationBody[] hipYs;
    ArticulationBody[] knees;
    ArticulationBody[] hipXs;

    void Start()
    {
        if (importer == null)
            importer = GetComponent<M20RoverImporter>();

        wheels = importer.wheel;
        hipYs  = importer.hipY;
        knees  = importer.knee;
        hipXs  = importer.hipX;

        // Start in a reasonable standing pose
        SetStandingPose(0f);
    }

    void Update()
    {
        // ----- Drive input -----
        float forward = Input.GetAxis("Vertical");   // W/S or Up/Down
        float turn    = Input.GetAxis("Horizontal"); // A/D or Left/Right

        float leftSpeed  = (forward - turn * turnSpeedMultiplier) * maxWheelSpeed;
        float rightSpeed = (forward + turn * turnSpeedMultiplier) * maxWheelSpeed;

        // FL + HL = left side, FR + HR = right side
        SetWheelVelocity(0, leftSpeed);   // fl
        SetWheelVelocity(2, leftSpeed);   // hl
        SetWheelVelocity(1, rightSpeed);  // fr
        SetWheelVelocity(3, rightSpeed);  // hr

        // ----- Height / posture -----
        if (Input.GetKey(KeyCode.Q))
            currentHeightOffset = Mathf.Clamp(currentHeightOffset + heightStep * Time.deltaTime, -maxHeightOffset, maxHeightOffset);
        if (Input.GetKey(KeyCode.E))
            currentHeightOffset = Mathf.Clamp(currentHeightOffset - heightStep * Time.deltaTime, -maxHeightOffset, maxHeightOffset);

        if (Input.GetKeyDown(KeyCode.R))
            currentHeightOffset = 0f;

        SetStandingPose(currentHeightOffset);
    }

    void SetWheelVelocity(int idx, float velocity)
    {
        if (wheels[idx] == null) return;
        var drive = wheels[idx].xDrive;
        drive.targetVelocity = velocity;
        wheels[idx].xDrive = drive;
    }

    void SetStandingPose(float heightOffset)
    {
        // Simple symmetric pose – good enough for driving on flat ground
        float hipYTarget = neutralHipY + heightOffset;
        float kneeTarget = neutralKnee - heightOffset * 1.2f; // compensate so feet stay roughly under the body

        for (int i = 0; i < 4; i++)
        {
            SetJointTarget(hipYs[i], hipYTarget);
            SetJointTarget(knees[i], kneeTarget);

            // Keep hipX near zero (no side splay)
            SetJointTarget(hipXs[i], 0f);
        }
    }

    void SetJointTarget(ArticulationBody body, float targetRadians)
    {
        if (body == null) return;
        var drive = body.xDrive;
        drive.target = targetRadians * Mathf.Rad2Deg;   // ArticulationDrive uses degrees
        body.xDrive = drive;
    }
}
using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// FIXED Perseverance M2020 Importer – ArticulationBody Rocker-Bogie System.
/// Key Fixes:
/// 1. Converts Radians -> Degrees for ArticulationDrive.target (fixes steering scale).
/// 2. Scales suspension stiffness (1800 N·m/deg) & neutral target to keep chassis elevated.
/// 3. Removes jointPosition target override in velocity drives.
/// 4. Auto-ignores internal collisions between wheel / bogie / chassis colliders.
/// 5. Includes invert option for right-side wheel revolute axes.
/// </summary>
public class RoverImporter_m2020 : MonoBehaviour
{
    [Header("Prefab & Spawn")]
    public GameObject roverPrefab;
    public Vector3 spawnPosition = new Vector3(0f, 2.5f, 0f);

    [Header("Physics Damping & Friction")]
    public float linearDamping = 0.5f;
    public float angularDamping = 0.5f;
    public float jointFriction = 0.05f;

    [Header("Wheel Drive")]
    public float wheelDamping = 2500f;
    public float wheelForceLimit = 2500f;
    public bool invertRightSideDrives = true; // URDF joint axis correction

    [Header("Steer Drive (Position Controlled)")]
    public float steerStiffness = 5000f;
    public float steerDamping = 500f;
    public float steerForceLimit = 1000f;

    [Header("Suspension (Rocker-Bogie Passive Hold)")]
    [Tooltip("Sufficient stiffness prevents chassis from collapsing flat onto the ground")]
    public float suspStiffness = 1800f;
    public float suspDamping = 350f;
    public float suspForceLimit = 3000f;
    [Tooltip("Target elevation offset angle in degrees (higher = lifts chassis)")]
    public float suspTargetElevationDeg = 0f;

    [Header("Safety & Collision")]
    public bool ignoreInternalCollisions = true;

    private GameObject currentRoverObject;
    private readonly List<ArticulationBody> leftWheels = new List<ArticulationBody>();
    private readonly List<ArticulationBody> rightWheels = new List<ArticulationBody>();

    private ArticulationBody steerLF, steerLR, steerRF, steerRR;
    private float lastLeftSpeed, lastRightSpeed;
    private float lastSteerAngle; // in radians

    void Start()
    {
        SpawnRover();
    }

    public void SpawnRover()
    {
        if (currentRoverObject != null)
            Destroy(currentRoverObject);

        if (roverPrefab == null)
        {
            Debug.LogError("[M2020] roverPrefab is not assigned in Inspector!");
            return;
        }

        currentRoverObject = Instantiate(roverPrefab, spawnPosition, Quaternion.identity);
        currentRoverObject.name = "GeneratedPerseverance_Fixed";

        var allBodies = currentRoverObject.GetComponentsInChildren<ArticulationBody>(true);
        Debug.Log($"[M2020] Found {allBodies.Length} ArticulationBodies.");

        foreach (var body in allBodies)
        {
            body.useGravity = true;
            body.linearDamping = linearDamping;
            body.angularDamping = angularDamping;
            body.jointFriction = jointFriction;
        }

        ArticulationBody root = null;
        foreach (var body in allBodies)
        {
            if (body.isRoot) { root = body; break; }
        }

        if (root != null)
        {
            root.immovable = false;
            root.useGravity = true;
            root.TeleportRoot(spawnPosition, Quaternion.identity);
            Debug.Log($"[M2020] Configured Root={root.name}, Mass={root.mass:F1}kg");
        }

        if (ignoreInternalCollisions)
        {
            IgnoreInternalCollisions(allBodies);
        }

        StartCoroutine(SetupAfterPhysics(allBodies));
    }

    private void IgnoreInternalCollisions(ArticulationBody[] bodies)
    {
        for (int i = 0; i < bodies.Length; i++)
        {
            var colsA = bodies[i].GetComponents<Collider>();
            for (int j = i + 1; j < bodies.Length; j++)
            {
                var colsB = bodies[j].GetComponents<Collider>();
                foreach (var cA in colsA)
                {
                    foreach (var cB in colsB)
                    {
                        Physics.IgnoreCollision(cA, cB, true);
                    }
                }
            }
        }
    }

    private IEnumerator SetupAfterPhysics(ArticulationBody[] allBodies)
    {
        yield return new WaitForFixedUpdate();
        yield return new WaitForFixedUpdate();

        leftWheels.Clear();
        rightWheels.Clear();

        foreach (var body in allBodies)
        {
            string n = body.name;

            // --- Drive Wheels ---
            if (n.StartsWith("Body_Wheel") && !body.isRoot && body.dofCount >= 1)
            {
                ConfigureVelocityDrive(body);
                if (n.Contains("Left"))
                {
                    leftWheels.Add(body);
                    Debug.Log($"[M2020] Connected LEFT drive: {n}");
                }
                else if (n.Contains("Right"))
                {
                    rightWheels.Add(body);
                    Debug.Log($"[M2020] Connected RIGHT drive: {n}");
                }
                continue;
            }

            // --- Steer Actuators (4 Corners) ---
            if (n == "Body_SteerLeftFront")  { steerLF = ConfigurePositionDriveDeg(body, 0f, steerStiffness, steerDamping, steerForceLimit); continue; }
            if (n == "Body_SteerLeftRear")   { steerLR = ConfigurePositionDriveDeg(body, 0f, steerStiffness, steerDamping, steerForceLimit); continue; }
            if (n == "Body_SteerRightFront") { steerRF = ConfigurePositionDriveDeg(body, 0f, steerStiffness, steerDamping, steerForceLimit); continue; }
            if (n == "Body_SteerRightRear")  { steerRR = ConfigurePositionDriveDeg(body, 0f, steerStiffness, steerDamping, steerForceLimit); continue; }

            // --- Rocker / Bogie / Differential Suspension ---
            if (n == "Body_RockerLeft" || n == "Body_RockerRight"
                || n == "Body_BogieLeft" || n == "Body_BogieRight"
                || n == "Body_Differential")
            {
                ConfigurePositionDriveDeg(body, suspTargetElevationDeg, suspStiffness, suspDamping, suspForceLimit);
                Debug.Log($"[M2020] Suspension link initialized: {n}");
            }
        }

        Debug.Log($"[M2020] Setup Complete: Drive Left={leftWheels.Count}, Right={rightWheels.Count} | Steers LF={steerLF!=null} LR={steerLR!=null} RF={steerRF!=null} RR={steerRR!=null}");
    }

    private void ConfigureVelocityDrive(ArticulationBody body)
    {
        var drive = body.xDrive;
        drive.stiffness = 0f;
        drive.damping = wheelDamping;
        drive.forceLimit = wheelForceLimit;
        drive.targetVelocity = 0f;
        drive.target = 0f;
        body.xDrive = drive;
    }

    private ArticulationBody ConfigurePositionDriveDeg(ArticulationBody body, float targetDeg,
        float stiffness, float damping, float forceLimit)
    {
        if (body == null || body.dofCount < 1) return body;
        var drive = body.xDrive;
        drive.stiffness = stiffness;
        drive.damping = damping;
        drive.forceLimit = forceLimit;
        drive.target = targetDeg; // ArticulationDrive expects DEGREES
        drive.targetVelocity = 0f;
        body.xDrive = drive;
        return body;
    }

    /// <summary>
    /// Set drive wheel angular speeds in deg/sec.
    /// </summary>
    public void SetWheelSpeeds(float leftSpeed, float rightSpeed)
    {
        lastLeftSpeed = leftSpeed;
        lastRightSpeed = rightSpeed;
    }

    /// <summary>
    /// Set 4-wheel steering angle in RADIANS. (Converted to Degrees internally!)
    /// </summary>
    public void SetSteerAngle(float angleRad)
    {
        lastSteerAngle = angleRad;
    }

    void FixedUpdate()
    {
        foreach (var w in leftWheels)
            ApplyVelocity(w, lastLeftSpeed);

        float rightSpeedToApply = invertRightSideDrives ? -lastRightSpeed : lastRightSpeed;
        foreach (var w in rightWheels)
            ApplyVelocity(w, rightSpeedToApply);

        // Standard 4-wheel pivot steering:
        // LF and RR steer +angle, RF and LR steer -angle
        ApplySteer(steerLF,  lastSteerAngle);
        ApplySteer(steerRF, -lastSteerAngle);
        ApplySteer(steerLR, -lastSteerAngle);
        ApplySteer(steerRR,  lastSteerAngle);
    }

    private void ApplyVelocity(ArticulationBody body, float speedDegPerSec)
    {
        if (body == null) return;
        var drive = body.xDrive;
        drive.stiffness = 0f;
        drive.damping = wheelDamping;
        drive.forceLimit = wheelForceLimit;
        drive.targetVelocity = speedDegPerSec;
        drive.target = 0f; // Kept 0 to prevent fighting velocity solver
        body.xDrive = drive;
    }

    private void ApplySteer(ArticulationBody body, float angleRad)
    {
        if (body == null || body.dofCount < 1) return;
        var drive = body.xDrive;
        drive.stiffness = steerStiffness;
        drive.damping = steerDamping;
        drive.forceLimit = steerForceLimit;
        
        // FIX: Convert input Radians to Degrees for Unity ArticulationDrive.target!
        drive.target = angleRad * Mathf.Rad2Deg; 
        drive.targetVelocity = 0f;
        body.xDrive = drive;
    }

    /// <summary>
    /// Dynamically adjust suspension elevation in degrees to change ground clearance.
    /// </summary>
    public void AdjustGroundClearance(float elevationOffsetDeg)
    {
        suspTargetElevationDeg = elevationOffsetDeg;
        var allBodies = currentRoverObject.GetComponentsInChildren<ArticulationBody>(true);
        foreach (var body in allBodies)
        {
            string n = body.name;
            if (n == "Body_RockerLeft" || n == "Body_RockerRight" || n == "Body_BogieLeft" || n == "Body_BogieRight")
            {
                var drive = body.xDrive;
                drive.target = suspTargetElevationDeg;
                body.xDrive = drive;
            }
        }
    }
}

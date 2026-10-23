using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using ProjectName.Rover;
using System.Linq;

/// <summary>
/// Production Perseverance M2020 Importer – ArticulationBody Rocker-Bogie System.
/// Key Features:
/// 1. Converts Radians/Degrees for ArticulationDrive.target.
/// 2. Scales suspension stiffness (1800 N·m/deg) & neutral target to keep chassis elevated.
/// 3. Individual 6-wheel velocity drive control (no right-side inversion).
/// 4. Individual 4-corner steer drive position control (steer targets in degrees).
/// 5. Measures wheel geometry at runtime in rover frame (X forward, Y left).
/// 6. Auto-ignores internal collisions between wheel / bogie / chassis colliders.
/// 7. Registers into ActiveRoverContext with 6-wheel telemetry.
/// </summary>
public class RoverImporter_m2020 : MonoBehaviour
{
    [Header("Profile & Prefab")]
    public RoverProfile profile;
    public GameObject roverPrefab;
    public Vector3 spawnPosition = new Vector3(0f, 2.5f, 0f);

    [Header("Physics Damping & Friction")]
    public float linearDamping = 0.5f;
    public float angularDamping = 0.5f;
    public float jointFriction = 0.05f;

    [Header("Wheel Drive")]
    public float wheelDamping = 2500f;
    public float wheelForceLimit = 2500f;

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

    [Header("Runtime Spawn State")]
    public bool autoSpawnOnStart = false;

    // Active rover object
    private GameObject currentRoverObject;

    // 6 Drive Wheels
    public ArticulationBody wLF, wRF, wLM, wRM, wLR, wRR;

    // 4 Corner Steer Actuators
    public ArticulationBody steerLF, steerRF, steerLR, steerRR;

    // Measured wheel positions in rover frame (X forward, Y left, relative to middle axle)
    [Header("Dynamic Wheel Geometry (Rover Frame: X forward, Y left)")]
    public Vector2 posLF = new Vector2(1.19f, 1.06f);
    public Vector2 posRF = new Vector2(1.19f, -1.06f);
    public Vector2 posLM = new Vector2(0.00f, 1.18f);
    public Vector2 posRM = new Vector2(0.00f, -1.18f);
    public Vector2 posLR = new Vector2(-1.07f, 1.06f);
    public Vector2 posRR = new Vector2(-1.07f, -1.06f);
    public bool isRoverReady = false;

    // Per-wheel commands (velocities in deg/s, steer in degrees)
    private float speedLF, speedRF, speedLM, speedRM, speedLR, speedRR;
    private float steerDegLF, steerDegRF, steerDegLR, steerDegRR;

    void Start()
    {
        if (autoSpawnOnStart)
            SpawnRover();
    }

    public GameObject SpawnRover()
    {
        if (currentRoverObject != null)
        {
            ActiveRoverContext.Unregister();
            Destroy(currentRoverObject);
            isRoverReady = false;
        }

        if (profile == null)
        {
            profile = Resources.Load<RoverProfile>("RoverProfiles/M2020Profile");
        }

        if (roverPrefab == null && profile != null)
        {
            roverPrefab = profile.prefab;
        }

        if (roverPrefab == null)
        {
            Debug.LogError("[M2020] roverPrefab is not assigned in Inspector!");
            return null;
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

        StartCoroutine(SetupAfterPhysics(allBodies, root));
        return currentRoverObject;
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

    private IEnumerator SetupAfterPhysics(ArticulationBody[] allBodies, ArticulationBody root)
    {
        yield return new WaitForFixedUpdate();
        yield return new WaitForFixedUpdate();

        foreach (var body in allBodies)
        {
            string n = body.name;

            // --- Drive Wheels ---
            if (n.StartsWith("Body_Wheel") && !body.isRoot && body.dofCount >= 1)
            {
                ConfigureVelocityDrive(body);
                if (n == "Body_WheelLeftFront") wLF = body;
                else if (n == "Body_WheelRightFront") wRF = body;
                else if (n == "Body_WheelLeftMiddle") wLM = body;
                else if (n == "Body_WheelRightMiddle") wRM = body;
                else if (n == "Body_WheelLeftRear") wLR = body;
                else if (n == "Body_WheelRightRear") wRR = body;
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

        // Measure wheel positions dynamically from actual spawned transforms in rover frame (X forward, Y left)
        if (wLF != null && wRF != null && wLM != null && wRM != null && wLR != null && wRR != null)
        {
            Vector3 roverUp = Vector3.up;
            Vector3 frontMid = (wLF.transform.position + wRF.transform.position) * 0.5f;
            Vector3 rearMid = (wLR.transform.position + wRR.transform.position) * 0.5f;
            Vector3 frontDir = Vector3.ProjectOnPlane(frontMid - rearMid, roverUp).normalized;
            Vector3 leftDir = -Vector3.Cross(roverUp, frontDir).normalized;
            Vector3 midAxle = (wLM.transform.position + wRM.transform.position) * 0.5f;

            posLF = new Vector2(Vector3.Dot(wLF.transform.position - midAxle, frontDir), Vector3.Dot(wLF.transform.position - midAxle, leftDir));
            posRF = new Vector2(Vector3.Dot(wRF.transform.position - midAxle, frontDir), Vector3.Dot(wRF.transform.position - midAxle, leftDir));
            posLM = new Vector2(Vector3.Dot(wLM.transform.position - midAxle, frontDir), Vector3.Dot(wLM.transform.position - midAxle, leftDir));
            posRM = new Vector2(Vector3.Dot(wRM.transform.position - midAxle, frontDir), Vector3.Dot(wRM.transform.position - midAxle, leftDir));
            posLR = new Vector2(Vector3.Dot(wLR.transform.position - midAxle, frontDir), Vector3.Dot(wLR.transform.position - midAxle, leftDir));
            posRR = new Vector2(Vector3.Dot(wRR.transform.position - midAxle, frontDir), Vector3.Dot(wRR.transform.position - midAxle, leftDir));
            isRoverReady = true;

            Debug.Log($"[M2020] Measured Wheel Geometry (Rover Frame: X forward, Y left):\n" +
                      $"  LF: {posLF:F3} | RF: {posRF:F3}\n" +
                      $"  LM: {posLM:F3} | RM: {posRM:F3}\n" +
                      $"  LR: {posLR:F3} | RR: {posRR:F3}");
        }

        Debug.Log($"[M2020] Setup Complete: Steers LF={steerLF!=null} LR={steerLR!=null} RF={steerRF!=null} RR={steerRR!=null}");

        // Rule R2: Order wheels for telemetry: FL, FR, ML, MR, RL, RR
        var orderedWheels = new ArticulationBody[] { wLF, wRF, wLM, wRM, wLR, wRR };

        var handle = new RoverHandle
        {
            rootGameObject = currentRoverObject,
            rootBody = root,
            profile = profile,
            wheelBodies = orderedWheels,
            wheelDefs = profile != null ? profile.wheels : null,
            isFrozen = false
        };

        var telemetry = currentRoverObject.AddComponent<RoverTelemetry>();
        telemetry.Initialize(handle);
        handle.telemetry = telemetry;

        ActiveRoverContext.Register(handle);
    }

    private void OnDestroy()
    {
        if (currentRoverObject != null)
        {
            ActiveRoverContext.Unregister();
        }
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
    /// Set individual 6-wheel drive velocities in deg/sec.
    /// All wheel joints share the same anchor rotation, so positive velocity drives every wheel forward.
    /// </summary>
    public void SetWheelSpeeds(float vLF, float vRF, float vLM, float vRM, float vLR, float vRR)
    {
        speedLF = vLF; speedRF = vRF;
        speedLM = vLM; speedRM = vRM;
        speedLR = vLR; speedRR = vRR;
    }

    /// <summary>
    /// Backward-compatible overload for 2-side speed commands.
    /// </summary>
    public void SetWheelSpeeds(float leftSpeed, float rightSpeed)
    {
        SetWheelSpeeds(leftSpeed, rightSpeed, leftSpeed, rightSpeed, leftSpeed, rightSpeed);
    }

    /// <summary>
    /// Set individual 4-corner steer angles in DEGREES.
    /// Logical positive angle (+deg) steers clockwise / right.
    /// </summary>
    public void SetSteerAnglesDeg(float degLF, float degRF, float degLR, float degRR)
    {
        steerDegLF = degLF;
        steerDegRF = degRF;
        steerDegLR = degLR;
        steerDegRR = degRR;
    }

    /// <summary>
    /// Backward-compatible overload for steer angle in RADIANS.
    /// </summary>
    public void SetSteerAngle(float angleRad)
    {
        float deg = angleRad * Mathf.Rad2Deg;
        SetSteerAnglesDeg(deg, deg, deg, deg);
    }

    void FixedUpdate()
    {
        // Drive velocity applied directly to all 6 wheels with consistent forward sign
        ApplyVelocity(wLF, speedLF);
        ApplyVelocity(wRF, speedRF);
        ApplyVelocity(wLM, speedLM);
        ApplyVelocity(wRM, speedRM);
        ApplyVelocity(wLR, speedLR);
        ApplyVelocity(wRR, speedRR);

        // Position steer drive applied in degrees
        ApplySteerDeg(steerLF, steerDegLF);
        ApplySteerDeg(steerRF, steerDegRF);
        ApplySteerDeg(steerLR, steerDegLR);
        ApplySteerDeg(steerRR, steerDegRR);
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

    private void ApplySteerDeg(ArticulationBody body, float targetDeg)
    {
        if (body == null || body.dofCount < 1) return;
        var drive = body.xDrive;
        drive.stiffness = steerStiffness;
        drive.damping = steerDamping;
        drive.forceLimit = steerForceLimit;
        drive.target = targetDeg; 
        drive.targetVelocity = 0f;
        body.xDrive = drive;
    }

    /// <summary>
    /// Dynamically adjust suspension elevation in degrees to change ground clearance.
    /// </summary>
    public void AdjustGroundClearance(float elevationOffsetDeg)
    {
        suspTargetElevationDeg = elevationOffsetDeg;
        if (currentRoverObject == null) return;
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

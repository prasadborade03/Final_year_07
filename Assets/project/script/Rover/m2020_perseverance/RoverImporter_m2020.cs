using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Perseverance M2020 importer – drive + soft rocker-bogie + 4-wheel steer.
///
/// Suspension joints are free (low stiffness hold at 0) so wheels can rise over
/// obstacles like a real rocker-bogie, without the chassis collapsing flat.
/// Steer joints are position-controlled from the controller.
/// Only Body_Wheel* with dof>=1 are velocity-driven.
/// </summary>
public class RoverImporter_m2020 : MonoBehaviour
{
    [Header("Prefab")]
    public GameObject roverPrefab;

    [Header("Spawn")]
    public Vector3 spawnPosition = new Vector3(0f, 2.2f, 0f);

    [Header("Physics")]
    public float linearDamping = 0.5f;
    public float angularDamping = 0.5f;
    public float jointFriction = 0.05f;

    [Header("Wheel Drive")]
    public float wheelDamping = 2500f;
    public float wheelForceLimit = 1500f;

    [Header("Steer (position control)")]
    public float steerStiffness = 2000f;
    public float steerDamping = 200f;
    public float steerForceLimit = 250f;

    [Header("Suspension (soft passive hold)")]
    [Tooltip("Low stiffness lets rockers/bogies flex over obstacles while resisting full collapse")]
    public float suspStiffness = 80f;
    public float suspDamping = 40f;
    public float suspForceLimit = 500f;

    private GameObject currentRoverObject;
    private readonly List<ArticulationBody> leftWheels = new List<ArticulationBody>();
    private readonly List<ArticulationBody> rightWheels = new List<ArticulationBody>();

    private ArticulationBody steerLF, steerLR, steerRF, steerRR;
    private float lastLeftSpeed, lastRightSpeed;
    private float lastSteerAngle; // radians

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
            Debug.LogError("[M2020] roverPrefab not assigned");
            return;
        }

        currentRoverObject = Instantiate(roverPrefab, spawnPosition, Quaternion.identity);
        currentRoverObject.name = "GeneratedPerseverance";

        var allBodies = currentRoverObject.GetComponentsInChildren<ArticulationBody>(true);
        Debug.Log($"[M2020] ArticulationBodies: {allBodies.Length}");

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
            Debug.Log($"[M2020] Root={root.name} mass={root.mass:F1}");
        }

        StartCoroutine(SetupAfterPhysics(allBodies));
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

            // --- Drive wheels ---
            if (n.StartsWith("Body_Wheel") && !body.isRoot && body.dofCount >= 1)
            {
                ConfigureVelocityDrive(body);
                if (n.Contains("Left"))
                {
                    leftWheels.Add(body);
                    Debug.Log($"[M2020] LEFT drive {n}");
                }
                else if (n.Contains("Right"))
                {
                    rightWheels.Add(body);
                    Debug.Log($"[M2020] RIGHT drive {n}");
                }
                continue;
            }

            // --- Steer actuators ---
            if (n == "Body_SteerLeftFront")  { steerLF = ConfigurePositionDrive(body, 0f, steerStiffness, steerDamping, steerForceLimit); continue; }
            if (n == "Body_SteerLeftRear")   { steerLR = ConfigurePositionDrive(body, 0f, steerStiffness, steerDamping, steerForceLimit); continue; }
            if (n == "Body_SteerRightFront") { steerRF = ConfigurePositionDrive(body, 0f, steerStiffness, steerDamping, steerForceLimit); continue; }
            if (n == "Body_SteerRightRear")  { steerRR = ConfigurePositionDrive(body, 0f, steerStiffness, steerDamping, steerForceLimit); continue; }

            // --- Soft suspension (rocker / bogie / differential) ---
            if (n == "Body_RockerLeft" || n == "Body_RockerRight"
                || n == "Body_BogieLeft" || n == "Body_BogieRight"
                || n == "Body_Differential")
            {
                ConfigurePositionDrive(body, 0f, suspStiffness, suspDamping, suspForceLimit);
                Debug.Log($"[M2020] Soft susp: {n}");
            }
        }

        Debug.Log($"[M2020] Drives left={leftWheels.Count} right={rightWheels.Count} | steers LF={steerLF!=null} LR={steerLR!=null} RF={steerRF!=null} RR={steerRR!=null}");
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

    private ArticulationBody ConfigurePositionDrive(ArticulationBody body, float targetRad,
        float stiffness, float damping, float forceLimit)
    {
        if (body == null || body.dofCount < 1) return body;
        var drive = body.xDrive;
        drive.stiffness = stiffness;
        drive.damping = damping;
        drive.forceLimit = forceLimit;
        drive.target = targetRad;
        drive.targetVelocity = 0f;
        body.xDrive = drive;
        return body;
    }

    /// <summary>
    /// leftSpeed/rightSpeed in deg/s. SAME sign on both sides = forward (for this URDF).
    /// </summary>
    public void SetWheelSpeeds(float leftSpeed, float rightSpeed)
    {
        lastLeftSpeed = leftSpeed;
        lastRightSpeed = rightSpeed;
    }

    /// <summary>
    /// Four-wheel steer angle in radians. Positive = turn left (counter-clockwise from top).
    /// Front and rear steer in opposite directions for pivot-in-place style.
    /// </summary>
    public void SetSteerAngle(float angleRad)
    {
        lastSteerAngle = angleRad;
    }

    void FixedUpdate()
    {
        foreach (var w in leftWheels)
            ApplyVelocity(w, lastLeftSpeed);
        foreach (var w in rightWheels)
            ApplyVelocity(w, lastRightSpeed);

        // 4-wheel steer: front +angle, rear -angle (NASA-style pivot / arc)
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
        if (body.jointPosition.dofCount > 0)
            drive.target = body.jointPosition[0];
        body.xDrive = drive;
    }

    private void ApplySteer(ArticulationBody body, float angleRad)
    {
        if (body == null || body.dofCount < 1) return;
        var drive = body.xDrive;
        drive.stiffness = steerStiffness;
        drive.damping = steerDamping;
        drive.forceLimit = steerForceLimit;
        drive.target = angleRad;
        drive.targetVelocity = 0f;
        body.xDrive = drive;
    }
}

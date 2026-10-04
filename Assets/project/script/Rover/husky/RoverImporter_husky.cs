using UnityEngine;
using System.Collections;

using ProjectName.Rover;

/// <summary>
/// Runtime importer & physics setup for the Clearpath Husky A200.
/// 
/// Responsibilities:
/// 1. Spawn the Husky prefab
/// 2. Make the true root movable + gravity ON (prevents floating)
/// 3. Configure the 4 continuous wheels for velocity control
/// 4. Register with ActiveRoverContext and attach RoverTelemetry
/// 5. Provide SetWheelSpeeds() for the controller
/// </summary>
public class RoverImporter_husky : MonoBehaviour
{
    [Header("Profile & Prefab")]
    public RoverProfile profile;

    [Tooltip("Drag the imported Husky prefab here (after URDF import)")]
    public GameObject roverPrefab;

    [Header("Physics Tuning")]
    public float linearDamping = 1.5f;
    public float angularDamping = 1.5f;
    public float jointFriction = 0.05f;

    [Header("Wheel Drive")]
    [Tooltip("Damping is the real motor strength. Keep >= 300")]
    public float wheelDamping = 10000f;          // matches the README recommendation
    public float wheelForceLimit = 1000000f;     // high force so it can push the chassis
    public float wheelStiffness = 0f;            // 0 = pure velocity control

    // -------------------------------------------------
    private GameObject currentRoverObject;

    private ArticulationBody frontLeftWheel;
    private ArticulationBody frontRightWheel;
    private ArticulationBody rearLeftWheel;
    private ArticulationBody rearRightWheel;

    public GameObject SpawnRover()
    {
        if (currentRoverObject != null)
        {
            ActiveRoverContext.Unregister();
            Destroy(currentRoverObject);
        }

        if (profile == null)
        {
            profile = Resources.Load<RoverProfile>("RoverProfiles/HuskyProfile");
        }

        if (roverPrefab == null && profile != null)
        {
            roverPrefab = profile.prefab;
        }

        if (roverPrefab == null)
        {
            Debug.LogError("[Husky] roverPrefab is not assigned!");
            return null;
        }

        currentRoverObject = Instantiate(roverPrefab);
        currentRoverObject.name = "GeneratedHusky";

        // Apply basic physics to every ArticulationBody
        var allBodies = currentRoverObject.GetComponentsInChildren<ArticulationBody>();
        foreach (var body in allBodies)
        {
            body.useGravity = true;
            body.linearDamping = linearDamping;
            body.angularDamping = angularDamping;
            body.jointFriction = jointFriction;
        }

        // Make the REAL root movable (critical – prevents floating)
        ArticulationBody trueRoot = null;
        foreach (var body in allBodies)
        {
            if (body.isRoot)
            {
                trueRoot = body;
                break;
            }
        }

        if (trueRoot != null)
        {
            trueRoot.immovable = false;
            trueRoot.useGravity = true;
            trueRoot.linearDamping = linearDamping;
            trueRoot.angularDamping = angularDamping;
            Debug.Log($"[Husky] Root found: {trueRoot.name} → gravity ON, movable");
        }
        else
        {
            Debug.LogWarning("[Husky] No root ArticulationBody found!");
        }

        // Wait a few physics frames so Unity finishes building the articulation tree
        StartCoroutine(SetupAfterPhysicsFrame(currentRoverObject, trueRoot));

        return currentRoverObject;
    }

    private IEnumerator SetupAfterPhysicsFrame(GameObject rover, ArticulationBody trueRoot)
    {
        yield return new WaitForFixedUpdate();
        yield return new WaitForFixedUpdate();
        yield return new WaitForFixedUpdate();

        // Husky wheel link names from the URDF
        frontLeftWheel  = SetupWheelDrive(rover, "front_left_wheel_link");
        frontRightWheel = SetupWheelDrive(rover, "front_right_wheel_link");
        rearLeftWheel   = SetupWheelDrive(rover, "rear_left_wheel_link");
        rearRightWheel  = SetupWheelDrive(rover, "rear_right_wheel_link");

        if (frontLeftWheel && frontRightWheel && rearLeftWheel && rearRightWheel)
            Debug.Log("[Husky] All 4 wheels configured for velocity control");
        else
            Debug.LogError("[Husky] One or more wheels not found! Check link names.");

        // Rule R2: Register into ActiveRoverContext
        var handle = new RoverHandle
        {
            rootGameObject = rover,
            rootBody = trueRoot,
            profile = profile,
            wheelBodies = new ArticulationBody[] { frontLeftWheel, frontRightWheel, rearLeftWheel, rearRightWheel },
            wheelDefs = profile != null ? profile.wheels : null,
            isFrozen = false
        };

        var telemetry = rover.AddComponent<RoverTelemetry>();
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

    // -------------------------------------------------
    // Wheel setup – pure velocity control (no snap-back)
    // -------------------------------------------------
    private ArticulationBody SetupWheelDrive(GameObject rover, string linkName)
    {
        Transform t = FindChildByName(rover.transform, linkName);
        if (t == null)
        {
            // Fallback: try searching by partial name (same style as the controller)
            t = FindChildContaining(rover.transform, linkName.Replace("_link", ""));
        }

        if (t == null)
        {
            Debug.LogWarning($"[Husky] Wheel link not found: {linkName}");
            return null;
        }

        if (!t.TryGetComponent<ArticulationBody>(out var body))
            return null;

        // Force healthy values – never allow damping = 0
        float damp  = Mathf.Max(wheelDamping, 500f);
        float force = Mathf.Max(wheelForceLimit, 1000f);

        ArticulationDrive drive = body.xDrive;
        drive.stiffness   = wheelStiffness;   // 0 = pure velocity
        drive.damping     = damp;
        drive.forceLimit  = force;
        drive.target      = 0f;
        drive.targetVelocity = 0f;

        // Prefer the modern Velocity drive type when available
        drive.driveType = ArticulationDriveType.Velocity;

        body.xDrive = drive;

        Debug.Log($"[Husky] {linkName} → damp={damp}  forceLimit={force}");
        return body;
    }

    // -------------------------------------------------
    // Public API – call from the controller every frame
    // -------------------------------------------------
    /// <summary>
    /// Tank-drive style: left side and right side speeds (deg/s or rad/s).
    /// Order is handled internally (FL + RL = left, FR + RR = right).
    /// </summary>
    public void SetWheelSpeeds(float leftSpeed, float rightSpeed)
    {
        ApplyVelocity(frontLeftWheel,  leftSpeed);
        ApplyVelocity(rearLeftWheel,   leftSpeed);
        ApplyVelocity(frontRightWheel, rightSpeed);
        ApplyVelocity(rearRightWheel,  rightSpeed);
    }

    /// <summary>
    /// Full 4-wheel control if you ever need individual speeds.
    /// </summary>
    public void SetWheelSpeeds(float fl, float fr, float rl, float rr)
    {
        ApplyVelocity(frontLeftWheel,  fl);
        ApplyVelocity(frontRightWheel, fr);
        ApplyVelocity(rearLeftWheel,   rl);
        ApplyVelocity(rearRightWheel,  rr);
    }

    private void ApplyVelocity(ArticulationBody body, float speed)
    {
        if (body == null) return;

        float damp  = Mathf.Max(wheelDamping, 500f);
        float force = Mathf.Max(wheelForceLimit, 1000f);

        ArticulationDrive drive = body.xDrive;
        drive.stiffness      = 0f;
        drive.damping        = damp;
        drive.forceLimit     = force;
        drive.targetVelocity = speed;
        drive.driveType      = ArticulationDriveType.Velocity;

        // Keep position error = 0 → prevents any restoring spring / snap-back
        if (body.jointPosition.dofCount > 0)
            drive.target = body.jointPosition[0];

        body.xDrive = drive;
    }

    // -------------------------------------------------
    // Utilities
    // -------------------------------------------------
    private Transform FindChildByName(Transform parent, string name)
    {
        if (parent.name == name) return parent;
        foreach (Transform child in parent)
        {
            Transform result = FindChildByName(child, name);
            if (result != null) return result;
        }
        return null;
    }

    private Transform FindChildContaining(Transform parent, string partial)
    {
        if (parent.name.ToLowerInvariant().Contains(partial.ToLowerInvariant()))
            return parent;
        foreach (Transform child in parent)
        {
            Transform result = FindChildContaining(child, partial);
            if (result != null) return result;
        }
        return null;
    }
}
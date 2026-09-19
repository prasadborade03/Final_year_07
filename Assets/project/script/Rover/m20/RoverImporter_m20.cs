using UnityEngine;
using System.Collections;
using System.Collections.Generic;

using ProjectName.Rover;

/// <summary>
/// Runtime importer & physics setup for the Deep Robotics M20.
/// 
/// Responsibilities:
/// 1. Spawn the M20 prefab
/// 2. Make the true root movable + gravity ON (prevents floating)
/// 3. Configure the 4 continuous wheels for pure velocity control
/// 4. Optionally lock the leg joints (hipx / hipy / knee) in a standing pose
/// 5. Register with ActiveRoverContext and attach RoverTelemetry
/// </summary>
public class RoverImporter_m20 : MonoBehaviour
{
    [Header("Profile & Prefab")]
    public RoverProfile profile;

    [Tooltip("Drag the imported M20 prefab here (after URDF import)")]
    public GameObject roverPrefab;

    [Header("Physics Tuning")]
    public float linearDamping = 1.5f;
    public float angularDamping = 1.5f;
    public float jointFriction = 0.05f;

    [Header("Wheel Drive (Velocity Control)")]
    public float wheelDamping = 800f;
    public float wheelForceLimit = 120f;

    [Header("Leg Locking (Standing Pose)")]
    [Tooltip("If true, hip & knee joints are held at fixed angles so the robot stands")]
    public bool lockLegsInStandingPose = true;

    [Tooltip("Stiffness used to hold the leg joints")]
    public float legHoldStiffness = 2000f;
    public float legHoldDamping = 200f;

    // Approximate standing pose (radians) – tweak these if the robot sits too low/high
    public float hipxAngle = 0.0f;
    public float hipyAngle = 0.6f;   // positive = legs spread / raised
    public float kneeAngle = -1.2f;  // negative = bent

    // -------------------------------------------------
    private GameObject currentRoverObject;

    private ArticulationBody flWheel, frWheel, hlWheel, hrWheel;

    // Optional: keep references to leg joints if you want to unlock later
    private List<ArticulationBody> legBodies = new List<ArticulationBody>();

    public GameObject SpawnRover()
    {
        if (currentRoverObject != null)
        {
            ActiveRoverContext.Unregister();
            Destroy(currentRoverObject);
        }

        if (profile == null)
        {
            profile = Resources.Load<RoverProfile>("RoverProfiles/M20Profile");
        }

        if (roverPrefab == null && profile != null)
        {
            roverPrefab = profile.prefab;
        }

        if (roverPrefab == null)
        {
            Debug.LogError("[M20] roverPrefab is not assigned!");
            return null;
        }

        currentRoverObject = Instantiate(roverPrefab);
        currentRoverObject.name = "GeneratedM20";

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
            Debug.Log($"[M20] Root found: {trueRoot.name} → gravity ON, movable");
        }
        else
        {
            Debug.LogWarning("[M20] No root ArticulationBody found!");
        }

        // Wait a couple of physics frames so Unity finishes building the articulation tree
        StartCoroutine(SetupAfterPhysicsFrame(currentRoverObject, trueRoot));

        return currentRoverObject;
    }

    private IEnumerator SetupAfterPhysicsFrame(GameObject rover, ArticulationBody trueRoot)
    {
        yield return new WaitForFixedUpdate();
        yield return new WaitForFixedUpdate();

        // ---- Wheels (continuous joints) ----
        flWheel = SetupWheelDrive(rover, "fl_wheel");
        frWheel = SetupWheelDrive(rover, "fr_wheel");
        hlWheel = SetupWheelDrive(rover, "hl_wheel");
        hrWheel = SetupWheelDrive(rover, "hr_wheel");

        if (flWheel && frWheel && hlWheel && hrWheel)
            Debug.Log("[M20] All 4 wheels configured for pure velocity control");
        else
            Debug.LogError("[M20] One or more wheels not found! Check link names.");

        // ---- Legs (optional lock) ----
        if (lockLegsInStandingPose)
        {
            LockLeg(rover, "fl_hipx", hipxAngle);
            LockLeg(rover, "fl_hipy", hipyAngle);
            LockLeg(rover, "fl_knee", kneeAngle);

            LockLeg(rover, "fr_hipx", -hipxAngle);   // mirrored
            LockLeg(rover, "fr_hipy", hipyAngle);
            LockLeg(rover, "fr_knee", kneeAngle);

            LockLeg(rover, "hl_hipx", hipxAngle);
            LockLeg(rover, "hl_hipy", -hipyAngle);   // rear usually mirrored
            LockLeg(rover, "hl_knee", kneeAngle);

            LockLeg(rover, "hr_hipx", -hipxAngle);
            LockLeg(rover, "hr_hipy", -hipyAngle);
            LockLeg(rover, "hr_knee", kneeAngle);

            Debug.Log("[M20] Legs locked in standing pose");
        }

        // Rule R2: Register into ActiveRoverContext
        var handle = new RoverHandle
        {
            rootGameObject = rover,
            rootBody = trueRoot,
            profile = profile,
            wheelBodies = new ArticulationBody[] { flWheel, frWheel, hlWheel, hrWheel },
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
            Debug.LogWarning($"[M20] Wheel link not found: {linkName}");
            return null;
        }

        if (!t.TryGetComponent<ArticulationBody>(out var body))
            return null;

        ArticulationDrive drive = body.xDrive;
        drive.stiffness = 0f;                 // pure velocity
        drive.damping = wheelDamping;
        drive.forceLimit = wheelForceLimit;
        drive.target = 0f;
        drive.targetVelocity = 0f;
        body.xDrive = drive;

        return body;
    }

    // -------------------------------------------------
    // Leg locking – hold at a fixed angle
    // -------------------------------------------------
    private void LockLeg(GameObject rover, string linkName, float targetAngleRad)
    {
        Transform t = FindChildByName(rover.transform, linkName);
        if (t == null) return;

        if (!t.TryGetComponent<ArticulationBody>(out var body))
            return;

        ArticulationDrive drive = body.xDrive;
        drive.stiffness = legHoldStiffness;
        drive.damping = legHoldDamping;
        drive.forceLimit = 200f;
        drive.target = targetAngleRad;
        drive.targetVelocity = 0f;
        body.xDrive = drive;

        legBodies.Add(body);
    }

    // -------------------------------------------------
    // Public API – call this from the controller every frame
    // -------------------------------------------------
    /// <summary>
    /// Set target velocities for the four wheels (degrees or rad/s depending on your units).
    /// Order: Front-Left, Front-Right, Hind-Left, Hind-Right
    /// </summary>
    public void SetWheelSpeeds(float fl, float fr, float hl, float hr)
    {
        ApplyVelocity(flWheel, fl);
        ApplyVelocity(frWheel, fr);
        ApplyVelocity(hlWheel, hl);
        ApplyVelocity(hrWheel, hr);
    }

    private void ApplyVelocity(ArticulationBody body, float speed)
    {
        if (body == null) return;

        ArticulationDrive drive = body.xDrive;
        drive.stiffness = 0f;
        drive.damping = wheelDamping;
        drive.forceLimit = wheelForceLimit;
        drive.targetVelocity = speed;

        // Keep position error at zero → prevents any restoring spring / snap-back
        if (body.jointPosition.dofCount > 0)
            drive.target = body.jointPosition[0];

        body.xDrive = drive;
    }

    // -------------------------------------------------
    // Utility
    // -------------------------------------------------
    private Transform FindChildByName(Transform parent, string name)
    {
        if (parent.name == name)
            return parent;

        foreach (Transform child in parent)
        {
            Transform result = FindChildByName(child, name);
            if (result != null)
                return result;
        }
        return null;
    }

    /// <summary>
    /// Optional helper – unlock all legs (set stiffness to 0)
    /// </summary>
    public void UnlockLegs()
    {
        foreach (var body in legBodies)
        {
            if (body == null) continue;
            var drive = body.xDrive;
            drive.stiffness = 0f;
            drive.damping = 10f;
            body.xDrive = drive;
        }
        Debug.Log("[M20] Legs unlocked");
    }
}
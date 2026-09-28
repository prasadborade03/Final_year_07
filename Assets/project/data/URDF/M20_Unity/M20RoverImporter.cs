using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Attach this to the root of the imported M20 robot (or to your empty _RoverModule).
/// It finds all ArticulationBodies and configures them for good simulation quality.
/// </summary>
[DefaultExecutionOrder(-100)]
public class M20RoverImporter : MonoBehaviour
{
    [Header("Base")]
    public bool makeBaseImmovable = false;   // set true only if you want a fixed base for testing

    [Header("Leg Joints (Position Control)")]
    public float legStiffness = 20000f;
    public float legDamping = 200f;
    public float legForceLimit = 200f;

    [Header("Wheel Joints (Velocity Control)")]
    public float wheelStiffness = 0f;         // velocity drive → stiffness usually 0
    public float wheelDamping = 50f;
    public float wheelForceLimit = 50f;

    [Header("Debug")]
    public bool logFoundJoints = true;

    // Cached references (filled by the importer)
    public ArticulationBody baseBody;
    public ArticulationBody[] hipX  = new ArticulationBody[4]; // FL, FR, HL, HR
    public ArticulationBody[] hipY  = new ArticulationBody[4];
    public ArticulationBody[] knee  = new ArticulationBody[4];
    public ArticulationBody[] wheel = new ArticulationBody[4];

    static readonly string[] LegPrefixes = { "fl", "fr", "hl", "hr" };

    void Awake()
    {
        ConfigureRobot();
    }

    [ContextMenu("Configure Robot Now")]
    public void ConfigureRobot()
    {
        // Find base
        baseBody = FindArticulation("base_link");
        if (baseBody != null)
        {
            baseBody.immovable = makeBaseImmovable;
        }

        for (int i = 0; i < 4; i++)
        {
            string p = LegPrefixes[i];

            hipX[i]  = FindArticulation($"{p}_hipx");
            hipY[i]  = FindArticulation($"{p}_hipy");
            knee[i]  = FindArticulation($"{p}_knee");
            wheel[i] = FindArticulation($"{p}_wheel");

            ConfigurePositionDrive(hipX[i],  legStiffness, legDamping, legForceLimit);
            ConfigurePositionDrive(hipY[i],  legStiffness, legDamping, legForceLimit);
            ConfigurePositionDrive(knee[i],  legStiffness, legDamping, legForceLimit);
            ConfigureVelocityDrive(wheel[i], wheelStiffness, wheelDamping, wheelForceLimit);
        }

        if (logFoundJoints)
        {
            Debug.Log($"[M20RoverImporter] Configured robot under {name}");
        }
    }

    ArticulationBody FindArticulation(string linkName)
    {
        // Search recursively for a child whose name contains the link name
        var bodies = GetComponentsInChildren<ArticulationBody>(true);
        foreach (var b in bodies)
        {
            if (b.name.ToLower().Contains(linkName.ToLower()))
                return b;
        }
        Debug.LogWarning($"[M20RoverImporter] Could not find ArticulationBody for '{linkName}'");
        return null;
    }

    void ConfigurePositionDrive(ArticulationBody body, float stiffness, float damping, float forceLimit)
    {
        if (body == null) return;

        var drive = body.xDrive;
        drive.stiffness  = stiffness;
        drive.damping    = damping;
        drive.forceLimit = forceLimit;
        // keep existing lower/upper limits from URDF importer
        body.xDrive = drive;

        body.jointFriction = 0.05f;
        body.angularDamping = 0.05f;
    }

    void ConfigureVelocityDrive(ArticulationBody body, float stiffness, float damping, float forceLimit)
    {
        if (body == null) return;

        var drive = body.xDrive;
        drive.stiffness  = stiffness;
        drive.damping    = damping;
        drive.forceLimit = forceLimit;
        drive.target     = 0f;          // not used in velocity mode
        body.xDrive = drive;

        body.jointFriction = 0.02f;
        body.angularDamping = 0.02f;
    }
}
using UnityEngine;
using System.Collections;

public class M20_WheelDebug : MonoBehaviour
{
    [Tooltip("Names of the wheel links exactly as they appear in the hierarchy")]
    public string[] wheelNames = { "fl_wheel", "fr_wheel", "hl_wheel", "hr_wheel" };

    IEnumerator Start()
    {
        // Wait until the importer has spawned the robot
        yield return new WaitForSeconds(1.5f);

        Debug.Log("========== M20 WHEEL DEBUG ==========");

        foreach (string name in wheelNames)
        {
            ArticulationBody body = FindBody(name);
            if (body == null)
            {
                Debug.LogError($"❌ Could not find ArticulationBody named '{name}'");
                continue;
            }

            Debug.Log($"✅ Found: {body.name}");
            Debug.Log($"   jointType     = {body.jointType}");
            Debug.Log($"   dofCount      = {body.dofCount}");
            Debug.Log($"   isRoot        = {body.isRoot}");
            Debug.Log($"   immovable     = {body.immovable}");

            // Print all three possible drives
            PrintDrive("xDrive", body.xDrive);
            PrintDrive("yDrive", body.yDrive);
            PrintDrive("zDrive", body.zDrive);

            // ---- LIVE TEST ----
            // Try spinning each axis for 2 seconds so you can SEE which one works
            StartCoroutine(TestAxis(body, "x", 2f));
            StartCoroutine(TestAxis(body, "y", 2f));
            StartCoroutine(TestAxis(body, "z", 2f));
        }

        Debug.Log("========== END DEBUG ==========");
    }

    IEnumerator TestAxis(ArticulationBody body, string axis, float duration)
    {
        yield return new WaitForSeconds(0.3f); // small stagger

        ArticulationDrive drive = GetDrive(body, axis);
        drive.stiffness = 0f;
        drive.damping = 500f;
        drive.forceLimit = 200f;
        drive.targetVelocity = 30f;          // strong spin
        SetDrive(body, axis, drive);

        Debug.Log($"🔄 Testing {body.name} → {axis}Drive  targetVelocity = 30");

        yield return new WaitForSeconds(duration);

        // stop
        drive.targetVelocity = 0f;
        SetDrive(body, axis, drive);
    }

    ArticulationDrive GetDrive(ArticulationBody b, string axis)
    {
        if (axis == "y") return b.yDrive;
        if (axis == "z") return b.zDrive;
        return b.xDrive;
    }

    void SetDrive(ArticulationBody b, string axis, ArticulationDrive d)
    {
        if (axis == "y") b.yDrive = d;
        else if (axis == "z") b.zDrive = d;
        else b.xDrive = d;
    }

    void PrintDrive(string label, ArticulationDrive d)
    {
        Debug.Log($"   {label}:  stiff={d.stiffness:F1}  damp={d.damping:F1}  " +
                  $"forceLimit={d.forceLimit:F1}  targetVel={d.targetVelocity:F1}");
    }

    ArticulationBody FindBody(string linkName)
    {
        var bodies = FindObjectsByType<ArticulationBody>(FindObjectsSortMode.None);
        foreach (var b in bodies)
        {
            if (b.name == linkName || b.gameObject.name == linkName)
                return b;
        }
        return null;
    }
}
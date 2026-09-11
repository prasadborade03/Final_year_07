using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Fixed control mapping for Perseverance M2020.
///
/// W / S  → forward / backward  (same-sign wheel speeds)
/// A / D  → turn left / right   (opposite-sign wheels + 4-wheel steering)
///
/// Previous bug: A/D produced same-sign speeds (drove forward/back) and
/// W/S produced opposite-sign speeds (yawed). Signs are corrected here.
/// </summary>
public class RoverController_m2020 : MonoBehaviour
{
    public RoverImporter_m2020 roverImporter;

    [Header("Drive")]
    [Tooltip("Wheel joint speed in degrees/second")]
    public float maxWheelSpeed = 400f;

    [Tooltip("If still inverted after this fix, tick this")]
    public bool invertDriveDirection = false;

    [Header("Turning")]
    [Tooltip("How much A/D adds differential wheel speed (0–1)")]
    [Range(0f, 1f)]
    public float skidSteerMix = 0.5f;

    [Tooltip("Max steer joint angle in degrees (4-wheel steering)")]
    public float maxSteerDegrees = 45f;

    void Update()
    {
        if (roverImporter == null || Keyboard.current == null)
            return;

        // --- Explicit mapping: W/S = throttle, A/D = turn ---
        float forward = 0f;
        if (Keyboard.current.wKey.isPressed) forward += 1f;
        if (Keyboard.current.sKey.isPressed) forward -= 1f;

        float turn = 0f;
        if (Keyboard.current.dKey.isPressed) turn += 1f; // D = turn right
        if (Keyboard.current.aKey.isPressed) turn -= 1f; // A = turn left

        // Drive: SAME sign on left and right for forward (matches this URDF axis setup).
        // Differential for turn: left decreases when turning right, right increases.
        float left  = (forward - turn * skidSteerMix) * maxWheelSpeed;
        float right = (forward + turn * skidSteerMix) * maxWheelSpeed;

        if (invertDriveDirection)
        {
            left  = -left;
            right = -right;
        }

        roverImporter.SetWheelSpeeds(left, right);

        // 4-wheel steering (radians). Positive angle = left turn convention in importer.
        float steerRad = -turn * maxSteerDegrees * Mathf.Deg2Rad;
        roverImporter.SetSteerAngle(steerRad);
    }
}

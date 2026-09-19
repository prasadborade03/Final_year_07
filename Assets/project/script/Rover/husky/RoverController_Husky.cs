using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Production keyboard controller for Clearpath Husky A200 skid-steer rover.
/// 
/// Controls:
/// W/A/S/D: Skid-steer drive mix
/// 1 / 2 / 3 / 4: Drive Mode presets (Stop, Precision, Explore, Cruise)
/// </summary>
public class RoverController_husky : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Drag the RoverImporter_husky component here")]
    public RoverImporter_husky roverImporter;

    [Header("Drive & Speed Regimes")]
    [Tooltip("Current operational drive mode")]
    public string currentDriveMode = "CRUISE";

    [Tooltip("Base rated wheel speed at 100% throttle")]
    public float baseWheelSpeed = 400f;

    [Tooltip("Effective wheel speed limit")]
    public float maxWheelSpeed = 400f;

    [Tooltip("Tick this if the robot drives backward when you press W")]
    public bool invertWheelMapping = false;

    [Header("Input Blocking (UI Focus)")]
    public bool isInputBlocked = false;

    [Header("Overrides (Automation / Testing)")]
    public float overrideThrottle = 0f;
    public float overrideSteer = 0f;

    public void SetDriveMode(string mode)
    {
        currentDriveMode = mode.ToUpperInvariant();
        switch (currentDriveMode)
        {
            case "STOP":
                maxWheelSpeed = 0f;
                break;
            case "PRECISION":
                maxWheelSpeed = baseWheelSpeed * 0.25f; // 100 rad/s
                break;
            case "EXPLORE":
                maxWheelSpeed = baseWheelSpeed * 0.60f; // 240 rad/s
                break;
            case "CRUISE":
            default:
                maxWheelSpeed = baseWheelSpeed; // 400 rad/s
                break;
        }
        Debug.Log($"[Husky] Drive mode set to: {currentDriveMode} (maxWheelSpeed: {maxWheelSpeed})");
    }

    public void SetSpeedLimit(float factor)
    {
        maxWheelSpeed = baseWheelSpeed * Mathf.Clamp01(factor);
    }

    void Update()
    {
        if (roverImporter == null)
            return;

        // If typing in UI, block driving inputs
        if (isInputBlocked)
        {
            roverImporter.SetWheelSpeeds(0f, 0f);
            return;
        }

        // Numeric key shortcuts (1 = STOP, 2 = PRECISION, 3 = EXPLORE, 4 = CRUISE)
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            if (Keyboard.current.digit1Key.wasPressedThisFrame) SetDriveMode("STOP");
            else if (Keyboard.current.digit2Key.wasPressedThisFrame) SetDriveMode("PRECISION");
            else if (Keyboard.current.digit3Key.wasPressedThisFrame) SetDriveMode("EXPLORE");
            else if (Keyboard.current.digit4Key.wasPressedThisFrame) SetDriveMode("CRUISE");
        }
#endif
        if (Input.GetKeyDown(KeyCode.Alpha1)) SetDriveMode("STOP");
        else if (Input.GetKeyDown(KeyCode.Alpha2)) SetDriveMode("PRECISION");
        else if (Input.GetKeyDown(KeyCode.Alpha3)) SetDriveMode("EXPLORE");
        else if (Input.GetKeyDown(KeyCode.Alpha4)) SetDriveMode("CRUISE");

        // 1. Read keys with fallback
        float throttle = overrideThrottle;
        float steer = overrideSteer;

#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) throttle += 1f;
            if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) throttle -= 1f;
            if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) steer += 1f;
            if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) steer -= 1f;
        }
        else
#endif
        {
            throttle += Input.GetAxis("Vertical");
            steer += Input.GetAxis("Horizontal");
        }

        throttle = Mathf.Clamp(throttle, -1f, 1f);
        steer = Mathf.Clamp(steer, -1f, 1f);

        // 2. Classic tank / skid-steer mix
        float left  = (throttle + steer) * maxWheelSpeed;
        float right = (throttle - steer) * maxWheelSpeed;

        // 3. Send to importer
        if (!invertWheelMapping)
            roverImporter.SetWheelSpeeds(left, right);
        else
            roverImporter.SetWheelSpeeds(-left, -right);
    }
}

public class HuskyController : RoverController_husky {}
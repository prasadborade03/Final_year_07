using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Simple keyboard controller for the Clearpath Husky A200.
/// 
/// W/A/S/D = tank / skid-steer drive
/// Same pattern as RoverController_m20 and RoverController_robot.
/// </summary>
public class RoverController_husky : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Drag the RoverImporter_husky component here")]
    public RoverImporter_husky roverImporter;

    [Header("Drive")]
    [Tooltip("Wheel speed at full throttle")]
    public float maxWheelSpeed = 400f;

    [Tooltip("Tick this if the robot drives backward when you press W")]
    public bool invertWheelMapping = false;

    [Header("Overrides (Automation / Testing)")]
    public float overrideThrottle = 0f;
    public float overrideSteer = 0f;

    void Update()
    {
        if (roverImporter == null)
            return;

        // 1. Read keys with fallback
        float throttle = overrideThrottle;
        float steer = overrideSteer;

        if (Keyboard.current != null)
        {
            if (Keyboard.current.wKey.isPressed) throttle += 1f;
            if (Keyboard.current.sKey.isPressed) throttle -= 1f;
            if (Keyboard.current.dKey.isPressed) steer += 1f;
            if (Keyboard.current.aKey.isPressed) steer -= 1f;
        }
        else
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
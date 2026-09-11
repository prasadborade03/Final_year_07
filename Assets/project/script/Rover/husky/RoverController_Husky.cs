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

    void Update()
    {
        if (roverImporter == null || Keyboard.current == null)
            return;

        // 1. Read keys
        float throttle = 0f;
        if (Keyboard.current.wKey.isPressed) throttle += 1f;
        if (Keyboard.current.sKey.isPressed) throttle -= 1f;

        float steer = 0f;
        if (Keyboard.current.dKey.isPressed) steer += 1f;
        if (Keyboard.current.aKey.isPressed) steer -= 1f;

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
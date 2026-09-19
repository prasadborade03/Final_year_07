using System;
using UnityEngine;

namespace ProjectName.UI
{
    [Serializable]
    public struct WheelTelemetry
    {
        public string wheelName;
        public float rpm;
        public float angularVelocityRad;
        public float slipRatio;          // 0 = perfect grip, 1 = total slip / spin-out
        public float motorTorque;        // In Nm or drive effort %
        public bool isGrounded;
    }

    [Serializable]
    public class RoverTelemetryData
    {
        [Header("Rover Identification")]
        public string roverName = "Clearpath Husky A200";
        public string driveMode = "Skid Steer (Differential)";

        [Header("Kinematics")]
        public float speedMps = 0f;          // Linear speed in meters/second
        public float speedKmph = 0f;         // Linear speed in km/h
        public float forwardAcceleration = 0f; // m/s^2
        public float gForce = 1.0f;

        [Header("Attitude & Incline")]
        public float pitchDeg = 0f;          // Tilt forward/backward (-90 to +90)
        public float rollDeg = 0f;           // Tilt left/right (-90 to +90)
        public float headingDeg = 0f;        // Compass bearing (0 to 360)
        public string headingCardinal = "N"; // N, NE, E, SE, etc.
        public float terrainSlopeDeg = 0f;   // Ground slope angle at rover contact
        public bool rolloverWarning = false; // True if pitch/roll exceeds safe threshold

        [Header("Navigation")]
        public Vector3 worldPosition = Vector3.zero;
        public float altitudeMeters = 0f;
        public float odometerMeters = 0f;    // Cumulative distance traversed
        public float missionTimeSeconds = 0f;// Active operation time

        [Header("Power & Subsystems")]
        public float batteryPercent = 100f;  // State of charge (0 - 100%)
        public float powerDrawWatts = 150f;  // Instantaneous power consumption
        public float motorTempCelsius = 24f; // Motor temperature (deg C)
        public bool thermalWarning = false;

        [Header("Wheels")]
        public WheelTelemetry[] wheels = new WheelTelemetry[0];

        [Header("Planetary Environment (Live Engine)")]
        public string planetName = "Mars";
        public float liveGravityMagnitude = 3.72f;
        public float terrainStaticFriction = 0.70f;
        public float terrainDynamicFriction = 0.55f;
        public float roverDrag = 0.02f;
        public float atmosDensityProxy = 0.008f;
        public float liveWindSpeed = 4.5f;
    }
}

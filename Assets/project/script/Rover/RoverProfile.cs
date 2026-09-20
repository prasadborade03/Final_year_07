using System;
using UnityEngine;

namespace ProjectName.Rover
{
    public enum DriveModeKind
    {
        Velocity,
        Position,
        Custom
    }

    [Serializable]
    public class DriveModeDefinition
    {
        public string label;
        public DriveModeKind kind;
        public float value;
        public string description;
    }

    [Serializable]
    public class CameraOffsetDefinition
    {
        public string perspectiveName; // "Front", "Rear", "Left", "Right", "Top"
        public Vector3 positionOffset;
        public Vector3 lookAtOffset;
    }

    [Serializable]
    public class WheelDefinition
    {
        public string linkName;
        public string displayName; // e.g. "FL", "FR", "ML", "MR", "RL", "RR"
        public bool isSteered;
        public string abbreviation => displayName;
    }

    /// <summary>
    /// Master data container for a simulated planetary rover.
    /// Non-negotiable rule R4: All rover physical, visual, and operational facts live here.
    /// Adding a new rover to the studio requires one RoverProfile asset + one prefab, zero UI code changes.
    /// </summary>
    [CreateAssetMenu(fileName = "NewRoverProfile", menuName = "Rover Studio/Rover Profile")]
    public class RoverProfile : ScriptableObject
    {
        [Header("Identification")]
        [Tooltip("Unique lowercase ID used in commands and UI tags (e.g. 'husky', 'm20', 'm2020')")]
        public string id = "husky";
        public string roverId => id;

        [Tooltip("Human-readable full name for mission control header")]
        public string displayName = "Clearpath Husky A200";

        [Tooltip("Steering architecture chip text (e.g. 'SKID-STEER 4WD', 'ROCKER-BOGIE 6WD · 4-WS')")]
        public string steeringLabel = "SKID-STEER 4WD";

        [Header("Prefab Reference")]
        [Tooltip("URDF-imported prefab (treated as read-only per rule R8)")]
        public GameObject prefab;

        [Header("Wheel Actuators")]
        [Tooltip("Physical rolling radius in meters")]
        public float wheelRadius = 0.165f;

        [Tooltip("Link names and abbreviations for all driving wheels")]
        public WheelDefinition[] wheels = new WheelDefinition[0];

        [Header("Kinematics & Performance Limits")]
        [Tooltip("Maximum designed ground speed in m/s")]
        public float maxSpeed = 1.0f;

        [Tooltip("Vertical spawn clearance above ground to avoid collider penetration")]
        public float spawnClearance = 0.35f;

        [Tooltip("Maximum safe operational incline in degrees before rollover danger")]
        public float maxSafeSlopeDeg = 30f;

        [Header("Traction & Slip Thresholds")]
        [Tooltip("Slip ratio below which traction is marked GOOD (e.g. 0.15 = 15%)")]
        [Range(0.01f, 0.5f)]
        public float slipGoodThreshold = 0.15f;

        [Tooltip("Slip ratio above which traction is marked SLIP/POOR (e.g. 0.40 = 40%)")]
        [Range(0.1f, 1.0f)]
        public float slipFairThreshold = 0.40f;

        [Header("Subsystems & Power")]
        [Tooltip("Nominal rover mass in kilograms")]
        public float massKg = 50f;

        [Tooltip("Standard battery pack capacity in Watt-hours")]
        public float batteryWh = 1200f;

        [Header("Operational Drive Modes (Used in Phase 2)")]
        public DriveModeDefinition[] driveModes = new DriveModeDefinition[0];

        [Header("Camera Preset Offsets (Used in Phase 2)")]
        public CameraOffsetDefinition[] cameraOffsets = new CameraOffsetDefinition[0];
    }
}

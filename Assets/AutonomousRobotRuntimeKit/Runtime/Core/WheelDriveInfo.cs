using System;
using UnityEngine;

namespace AutonomousRobotKit
{
    public enum WheelAxis { X, Y, Z }

    /// <summary>
    /// Represents a detected robot wheel paired with its active ArticulationDrive axis.
    /// Fully serializable for inspector debugging and runtime fine-tuning.
    /// </summary>
    [Serializable]
    public class WheelDriveInfo
    {
        public string wheelName;
        public ArticulationBody body;
        public WheelAxis axis = WheelAxis.X;
        public bool isRightSide;
        public bool invertDirection;

        public WheelDriveInfo() { }

        public WheelDriveInfo(ArticulationBody body, WheelAxis axis, bool isRightSide, bool invertDirection = false)
        {
            this.body = body;
            this.wheelName = body != null ? body.name : "Unknown Wheel";
            this.axis = axis;
            this.isRightSide = isRightSide;
            this.invertDirection = invertDirection;
        }

        public override string ToString()
        {
            return $"Wheel '{wheelName}' [Axis={axis}, Side={(isRightSide ? "Right" : "Left")}, Invert={invertDirection}]";
        }
    }
}

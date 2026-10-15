using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AutonomousRobotKit
{
    /// <summary>
    /// Analyzes an imported URDF GameObject hierarchy to universally identify the robot's
    /// architecture (Wheeled Mobile Rover, Articulated Legged Quadruped/Biped, Robotic Arm,
    /// or Generic Multi-Joint System), classifying the chassis/base and all functional joints.
    /// </summary>
    public static class RoverAnalyzer
    {
        private static readonly string[] WheelKeywords = {
            "wheel", "tire", "tyre", "track", "caster", "rotor"
        };

        private static readonly string[] LegKeywords = {
            "hip", "thigh", "calf", "knee", "foot", "shin", "ankle", "leg",
            "abduction", "coxa", "femur", "tibia", "haa", "hfe", "kfe"
        };

        private static readonly string[] ArmKeywords = {
            "shoulder", "elbow", "wrist", "forearm", "arm", "gripper", "finger",
            "flange", "link1", "link2", "link3", "link4", "link5", "link6"
        };

        private static readonly string[] SteeringKeywords = {
            "steer", "steering", "knuckle", "swivel", "pivot", "yaw", "base_wheel"
        };

        private static readonly string[] SuspensionKeywords = {
            "rocker", "bogie", "bogey", "suspension", "strut", "diff_bar", "diff_box", "rocker_bogie", "trailing_arm", "wishbone"
        };

        private static readonly string[] NonWheelKeywords = {
            "arm", "shoulder", "elbow", "wrist", "finger", "gripper",
            "pan", "tilt", "gimbal", "lidar", "laser", "camera", "sensor", "bumper",
            "steer", "steering", "base_wheel", "rocker", "bogie", "diff", "differential", "suspension",
            "hip", "knee", "thigh", "calf", "leg", "foot", "abduction", "femur", "tibia",
            "mast", "rsm", "hga", "antenna", "turret", "drill", "corer", "bit",
            "sherloc", "watson", "pixl", "head", "neck"
        };

        public static RoverDescriptor Analyze(GameObject root)
        {
            var desc = new RoverDescriptor { Root = root };

            if (root == null)
            {
                desc.Warnings.Add("Provided root GameObject is null.");
                return desc;
            }

            var bodies = root.GetComponentsInChildren<ArticulationBody>(true);
            if (bodies == null || bodies.Length == 0)
            {
                desc.Warnings.Add("No ArticulationBody components found under root hierarchy.");
                return desc;
            }

            desc.AllBodies = bodies.ToList();

            // Identify root ArticulationBody
            desc.RootArticulationBody = bodies.FirstOrDefault(b => b.isRoot);

            // Identify Chassis / Base
            desc.Chassis = bodies
                .OrderByDescending(b => b.mass)
                .ThenByDescending(b => b.isRoot)
                .FirstOrDefault();

            if (desc.Chassis == null || (desc.RootArticulationBody != null && desc.RootArticulationBody.mass >= desc.Chassis.mass * 0.5f))
            {
                desc.Chassis = desc.RootArticulationBody ?? desc.Chassis;
            }

            if (desc.Chassis == null)
            {
                desc.Warnings.Add("No chassis or root ArticulationBody found.");
                return desc;
            }

            // Identify functional joint roles
            var namedWheels = new List<ArticulationBody>();
            var candidateFreeDrives = new List<ArticulationBody>();
            var nonWheelJoints = new List<ArticulationBody>();

            int legKeywordHits = 0;
            int armKeywordHits = 0;

            foreach (var body in bodies)
            {
                if (body == desc.Chassis || (desc.RootArticulationBody != null && body == desc.RootArticulationBody))
                    continue;

                string lowerName = body.name.ToLowerInvariant();

                if (LegKeywords.Any(kw => lowerName.Contains(kw))) legKeywordHits++;
                if (ArmKeywords.Any(kw => lowerName.Contains(kw))) armKeywordHits++;

                // 1. FixedJoint Handling
                if (body.jointType == ArticulationJointType.FixedJoint)
                {
                    bool isDirectFixedWheel = WheelKeywords.Any(kw => lowerName.Contains(kw)) && body.transform.parent != null &&
                        (body.transform.parent.GetComponent<ArticulationBody>() == desc.Chassis ||
                         body.transform.parent.GetComponent<ArticulationBody>() == desc.RootArticulationBody ||
                         body.transform.parent.name.ToLowerInvariant().Contains("base") ||
                         body.transform.parent.name.ToLowerInvariant().Contains("chassis"));

                    if (isDirectFixedWheel)
                    {
                        namedWheels.Add(body);
                    }
                    continue;
                }

                // 2. Steering knuckles and pivots
                bool isSteering = SteeringKeywords.Any(kw => lowerName.Contains(kw));
                if (isSteering)
                {
                    desc.SteeringJoints.Add(body);
                    continue;
                }

                // 3. Suspension (rocker, bogie, strut, etc.)
                bool isSuspension = SuspensionKeywords.Any(kw => lowerName.Contains(kw));
                if (isSuspension)
                {
                    desc.SuspensionJoints.Add(body);
                    continue;
                }

                // 4. Wheeled quadruped foot wheels (Go2W, B2W: continuous foot joint or explicit wheel)
                bool isContinuous = body.jointType == ArticulationJointType.RevoluteJoint &&
                    (Mathf.Approximately(body.xDrive.lowerLimit, 0f) && Mathf.Approximately(body.xDrive.upperLimit, 0f));
                bool isFootWheel = (lowerName.Contains("wheel") || lowerName.Contains("tire")) ||
                    (lowerName.Contains("foot") && isContinuous);

                // 5. Upkie ankle rotor wheel
                bool isAnkleWheel = lowerName.Contains("ankle") && lowerName.Contains("rotor") &&
                    body.GetComponentsInChildren<Transform>(true).Any(t => t.name.ToLowerInvariant().Contains("wheel_hub"));

                // 6. Upper limb joints - strictly not wheels
                bool isUpperLimb = NonWheelKeywords.Any(kw => lowerName.Contains(kw));
                if (isUpperLimb && !lowerName.Contains("wheel") && !lowerName.Contains("tire") && !isFootWheel && !isAnkleWheel)
                {
                    nonWheelJoints.Add(body);
                    continue;
                }

                bool hasWheelName = WheelKeywords.Any(kw => lowerName.Contains(kw));
                if (hasWheelName || isFootWheel || isAnkleWheel)
                {
                    namedWheels.Add(body);
                    continue;
                }

                if (IsDriveFree(body))
                {
                    candidateFreeDrives.Add(body);
                }
                else
                {
                    nonWheelJoints.Add(body);
                }
            }

            // Architecture Classification
            int wheelCount = namedWheels.Count;
            int legCount = legKeywordHits;
            int armCount = armKeywordHits;
            int nonWheelCount = nonWheelJoints.Count + candidateFreeDrives.Count;
            string rootName = root.name.ToLowerInvariant();

            if (wheelCount >= 2 && (legCount >= 4 || nonWheelCount >= 4))
            {
                // Architecture A: Wheeled Quadruped / Biped (M20, Go2-W, Upkie)
                desc.RobotType = RobotArchitectureType.WheeledQuadruped;
                desc.Wheels = namedWheels;
                desc.NonWheelJoints = nonWheelJoints.Concat(candidateFreeDrives).Distinct().ToList();
            }
            else if (wheelCount >= 2)
            {
                // Architecture B: Pure Wheeled Mobile Rover (Husky, Perseverance, RB-Vogui)
                desc.RobotType = RobotArchitectureType.WheeledMobile;
                desc.Wheels = namedWheels;
                desc.NonWheelJoints = nonWheelJoints.Concat(candidateFreeDrives).Distinct().ToList();
            }
            else if (armCount > legCount || rootName.Contains("arm") || rootName.Contains("manipulator") || rootName.Contains("gripper") || rootName.Contains("arduinobot"))
            {
                // Architecture D: Robotic Arm / Fixed Articulated Mechanism (Panda, UR5, Arduinobot)
                desc.RobotType = RobotArchitectureType.RoboticArm;
                desc.Wheels = new List<ArticulationBody>();
                desc.NonWheelJoints = nonWheelJoints.Concat(candidateFreeDrives).Concat(namedWheels).Distinct().ToList();
            }
            else if (legCount >= 4 || (legCount > 0 && nonWheelCount >= 6))
            {
                // Architecture C: Pure Legged Quadruped / Multiped (Spot, ANYmal, Barkour, Go1/Go2)
                desc.RobotType = RobotArchitectureType.ArticulatedLegged;
                desc.Wheels = new List<ArticulationBody>();
                desc.NonWheelJoints = nonWheelJoints.Concat(candidateFreeDrives).Concat(namedWheels).Distinct().ToList();
            }
            else if (armCount >= 1 || nonWheelCount >= 1)
            {
                desc.RobotType = RobotArchitectureType.RoboticArm;
                desc.Wheels = new List<ArticulationBody>();
                desc.NonWheelJoints = nonWheelJoints.Concat(candidateFreeDrives).Concat(namedWheels).Distinct().ToList();
            }
            else
            {
                desc.RobotType = RobotArchitectureType.GeneralArticulated;
            }

            desc.NonWheelJoints = desc.NonWheelJoints.Distinct().ToList();

            // Sort wheels front-to-back, left-to-right relative to chassis
            if (desc.Chassis != null && desc.Wheels.Count > 0)
            {
                desc.Wheels = desc.Wheels
                    .OrderByDescending(w => desc.Chassis.transform.InverseTransformPoint(w.transform.position).z)
                    .ThenBy(w => desc.Chassis.transform.InverseTransformPoint(w.transform.position).x)
                    .ToList();
            }

            if (desc.RobotType == RobotArchitectureType.WheeledMobile)
            {
                if (desc.Wheels.Count < 2)
                {
                    desc.Warnings.Add($"Only {desc.Wheels.Count} potential wheel(s) detected. Expected >= 2 for a mobile rover.");
                }
                else if (desc.Wheels.Count > 12)
                {
                    desc.Warnings.Add($"Unusually high number ({desc.Wheels.Count}) of wheel candidates detected - review hierarchy.");
                }
            }

            desc.ModelType = DetectModel(root, desc);
            return desc;
        }

        public static KnownRobotModel DetectModel(GameObject root, RoverDescriptor desc)
        {
            if (root == null) return KnownRobotModel.Generic;

            string rootName = root.name.ToLowerInvariant();
            var allNames = desc != null && desc.AllBodies != null
                ? desc.AllBodies.Select(b => b.name.ToLowerInvariant()).ToList()
                : root.GetComponentsInChildren<ArticulationBody>(true).Select(b => b.name.ToLowerInvariant()).ToList();

            int wheelCount = desc != null && desc.Wheels != null
                ? desc.Wheels.Count
                : allNames.Count(n => n.Contains("wheel") || n.Contains("tire"));

            if (rootName.Contains("husky") || allNames.Any(n => n.Contains("husky") || n.Contains("front_left_wheel_link")))
                return KnownRobotModel.ClearpathHusky;

            if (rootName.Contains("m2020") || rootName.Contains("perseverance") || rootName.Contains("curiosity") ||
                allNames.Any(n => n.Contains("body_rocker") || n.Contains("body_wheel_fl") || n.Contains("body_steer_fl")))
                return KnownRobotModel.NasaPerseverance;

            if ((rootName.Contains("m20") && !rootName.Contains("m2020")) ||
                allNames.Any(n => n == "fl_knee" || n == "fl_hipx" || n == "hl_hipx" || n == "fl_wheel"))
                return KnownRobotModel.DeepRoboticsM20;

            if (rootName.Contains("go2w") || rootName.Contains("go2_w") || rootName.Contains("b2w") || rootName.Contains("b2_w") || rootName.Contains("magicdog") ||
                ((rootName.Contains("go2") || rootName.Contains("b2") || allNames.Any(n => n.Contains("fl_calf") || n.Contains("fl_thigh"))) && wheelCount >= 2))
                return KnownRobotModel.UnitreeGo2W;

            if (rootName.Contains("go2") || allNames.Any(n => n.Contains("go2") || n.StartsWith("fl_hip") || n.StartsWith("fl_thigh") || n.StartsWith("fl_calf")))
            {
                if (wheelCount >= 2) return KnownRobotModel.UnitreeGo2W;
                return KnownRobotModel.UnitreeGo2;
            }

            if (rootName.Contains("go1") || allNames.Any(n => n.Contains("go1"))) return KnownRobotModel.UnitreeGo1;

            if (rootName.Contains("anymal") || allNames.Any(n => n.Contains("anymal") || n.Contains("lf_kfe") || n.Contains("lh_kfe") || n.Contains("lf_haa") || n.Contains("lf_hfe")))
                return KnownRobotModel.AnymalD;

            if (rootName.Contains("spot") || allNames.Any(n => n.Contains("spot") || n.Contains("fl.hx") || n.Contains("fl.hy") || n.Contains("fl.kn") ||
                (n.Contains("front_left_upper_leg") && allNames.Any(m => m.Contains("front_left_lower_leg")))))
                return KnownRobotModel.BostonDynamicsSpot;

            if (rootName.Contains("barkour") || allNames.Any(n => n.Contains("barkour") || n.Contains("torso_asm") || (n.Contains("abduction_") && allNames.Any(m => m.Contains("upper_leg_")))))
                return KnownRobotModel.GoogleBarkour;

            if (rootName.Contains("pickerbot") || rootName.Contains("mec_arm") ||
                allNames.Any(n => n.Contains("arm_a_link") || n.Contains("left_front_link")))
                return KnownRobotModel.PickerbotMini;

            if (rootName.Contains("rbvogui") || allNames.Any(n => n.Contains("rbvogui") || n.Contains("base_wheel")))
                return KnownRobotModel.RbvoguiMobileRover;

            if (rootName.Contains("upkie") || allNames.Any(n => n.Contains("upkie") || n.Contains("mj5208") || n.Contains("qdd100")))
                return KnownRobotModel.UpkieWheeledBiped;

            if (desc != null && desc.RobotType == RobotArchitectureType.RoboticArm)
                return KnownRobotModel.RoboticArm;

            if (rootName.Contains("arm") || rootName.Contains("arduinobot") || rootName.Contains("manipulator") ||
                allNames.Any(n => n.Contains("arduinobot") || n.Contains("shoulder_link") || n.Contains("elbow_link") || n.Contains("wrist_link") || n == "link1" || n == "link2"))
                return KnownRobotModel.RoboticArm;

            return KnownRobotModel.Generic;
        }

        public static bool IsRightSideWheel(ArticulationBody wheel, ArticulationBody chassis)
        {
            if (wheel == null) return false;

            string name = wheel.name.ToLowerInvariant();
            if (name.Contains("right") || name.Contains("_r_") || name.Contains(".r.") || name.StartsWith("r_") || name.EndsWith("_r") || name.Contains("_right"))
                return true;
            if (name.Contains("left") || name.Contains("_l_") || name.Contains(".l.") || name.StartsWith("l_") || name.EndsWith("_l") || name.Contains("_left"))
                return false;

            if (chassis != null)
            {
                Vector3 localPos = chassis.transform.InverseTransformPoint(wheel.transform.position);
                return localPos.x > 0.001f;
            }

            return wheel.transform.localPosition.x > 0.001f;
        }

        public static bool DetectWheelInversion(ArticulationBody wheel, ArticulationBody chassis)
        {
            if (wheel == null || chassis == null) return false;

            Vector3 wheelSpinAxisWorld = wheel.transform.rotation * wheel.anchorRotation * Vector3.right;
            Vector3 chassisRight = chassis.transform.right;

            return Vector3.Dot(wheelSpinAxisWorld, chassisRight) < -0.3f;
        }

        private static bool IsDriveFree(ArticulationBody body)
        {
            if (body.twistLock == ArticulationDofLock.FreeMotion ||
                body.swingYLock == ArticulationDofLock.FreeMotion ||
                body.swingZLock == ArticulationDofLock.FreeMotion)
            {
                return true;
            }

            bool xFree = body.xDrive.lowerLimit < body.xDrive.upperLimit - 0.01f || (Mathf.Approximately(body.xDrive.lowerLimit, 0f) && Mathf.Approximately(body.xDrive.upperLimit, 0f));
            bool yFree = body.yDrive.lowerLimit < body.yDrive.upperLimit - 0.01f;
            bool zFree = body.zDrive.lowerLimit < body.zDrive.upperLimit - 0.01f;

            return xFree || yFree || zFree;
        }
    }
}

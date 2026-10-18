using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AutonomousRobotKit
{
    public enum RoverMassProfile
    {
        AutoDetect,
        HeavyHusky, // ~3000 Nm, damping 150 (Chassis ~46-80kg)
        Medium,     // ~1000 Nm, damping 80 (Chassis ~10-25kg)
        Light       // ~250 Nm, damping 30 (Chassis <10kg)
    }

    /// <summary>
    /// Configures an imported URDF rover for ArticulationBody velocity-based driving:
    /// 1. Unpins the chassis (immovable = false, gravity = true).
    /// 2. Resolves and unlocks the true spin DOF (Twist X = FreeMotion on RevoluteJoint).
    /// 3. Sets pure velocity control (stiffness = 0) with heavy-rover torque limits and damping.
    /// 4. Disables conflicting default URDF Importer position controllers.
    /// 5. Ensures wheel colliders are convex and have appropriate skid-steer friction materials.
    /// 6. Attaches the SimpleDifferentialDrive controller and marks the robot as configured.
    /// </summary>
    public static class RoverAutoConfigurator
    {
        // Defaults tuned for real-world research rovers (Clearpath Husky scale: ~46-80 kg)
        public static float DefaultWheelForceLimit = 5000f; // High torque in Nm to overcome terrain scrubbing
        public static float DefaultWheelDamping = 500f;     // Velocity control gain in PhysX
        public static float DefaultMaxSpeed = 400f;         // Max angular velocity in deg/s (~6.98 rad/s)
        public static bool MakeChassisImmovable = false;    // Set true only for stationary bench testing
        public static bool EnsureConvexColliders = true;

        private static PhysicsMaterial _wheelPhysicMaterial;

        public static void Configure(GameObject robotRoot)
        {
            Configure(robotRoot, RoverMassProfile.AutoDetect);
        }

        public static void Configure(GameObject robotRoot, RoverMassProfile profile)
        {
            if (robotRoot == null)
            {
                Debug.LogError("[Rover Compatibility] Cannot configure a null GameObject.");
                return;
            }

            var desc = RoverAnalyzer.Analyze(robotRoot);

            Debug.Log($"[Rover Compatibility] Analyzing '{robotRoot.name}' -> " +
                      $"Chassis: {(desc.Chassis != null ? desc.Chassis.name : "None")} " +
                      $"(Mass: {(desc.Chassis != null ? desc.Chassis.mass.ToString("F1") : "0")} kg), " +
                      $"Total Mass: {desc.TotalMass:F1} kg, Wheels Found: {desc.Wheels.Count}");

            foreach (var warning in desc.Warnings)
            {
                Debug.LogWarning($"[Rover Compatibility] {warning}");
            }

            if (!desc.IsCompatible)
            {
                Debug.LogError($"[Rover Compatibility] '{robotRoot.name}' has no recognized ArticulationBody hierarchy.");
                return;
            }

            // 1. Unpin the chassis and root links
            // The URDF Importer defaults the root ArticulationBody to immovable = true
            // (designed for stationary robot arms). For mobile rovers/quadrupeds, this MUST be false!
            var rootBody = desc.RootArticulationBody != null && desc.RootArticulationBody.isRoot
                ? desc.RootArticulationBody
                : (desc.Chassis != null && desc.Chassis.isRoot ? desc.Chassis : null);

            if (rootBody != null)
            {
                bool shouldBeImmovable = desc.RobotType == RobotArchitectureType.RoboticArm && MakeChassisImmovable;
                rootBody.immovable = shouldBeImmovable;
                rootBody.useGravity = true;
                rootBody.linearDamping = 0.5f;
                rootBody.angularDamping = 0.5f;
            }
            if (desc.Chassis != null)
            {
                desc.Chassis.useGravity = true;
            }

            // 2. Remove / disable conflicting URDF Importer default controllers
            CleanConflictingImporterControllers(robotRoot);

            // 3. Attach runtime physics stabilizer (permanently eliminates internal self-collisions)
            var stabilizer = robotRoot.GetComponent<RobotPhysicsStabilizer>();
            if (stabilizer == null)
            {
                stabilizer = robotRoot.AddComponent<RobotPhysicsStabilizer>();
            }
            if (stabilizer != null)
            {
                stabilizer.ApplyStabilization();
            }
            // Determine mass-based torque and damping
            float targetTorque;
            float targetDamping;

            switch (profile)
            {
                case RoverMassProfile.HeavyHusky:
                    targetTorque = 5000f;
                    targetDamping = 500f;
                    break;
                case RoverMassProfile.Medium:
                    targetTorque = 2500f;
                    targetDamping = 250f;
                    break;
                case RoverMassProfile.Light:
                    targetTorque = 600f;
                    targetDamping = 60f;
                    break;
                default: // AutoDetect
                    if (desc.TotalMass > 150f)
                    {
                        targetTorque = Mathf.Max(6000f, desc.TotalMass * 30f);
                        targetDamping = Mathf.Clamp(desc.TotalMass * 1.5f, 200f, 600f);
                    }
                    else if (desc.TotalMass > 35f)
                    {
                        targetTorque = 4000f;
                        targetDamping = 300f;
                    }
                    else if (desc.TotalMass >= 10f)
                    {
                        targetTorque = 2000f;
                        targetDamping = 180f;
                    }
                    else
                    {
                        targetTorque = 600f;
                        targetDamping = 60f;
                    }
                    break;
            }

            // 4. Dynamic Kinematic & Architecture Generation
            // Generates controllers universally based on detected anatomy, not hardcoded model names!
            bool hasWheels = desc.Wheels != null && desc.Wheels.Count >= 1;
            int nonWheelCount = desc.NonWheelJoints != null ? desc.NonWheelJoints.Count : 0;

            Debug.Log($"[Rover Compatibility] Dynamic Kinematic Generation for '{robotRoot.name}': " +
                      $"Wheels={desc.Wheels.Count}, Steering={desc.SteeringJoints.Count}, Suspension={desc.SuspensionJoints.Count}, " +
                      $"NonWheelJoints={nonWheelCount}, Mass={desc.TotalMass:F1}kg");

            if (hasWheels && nonWheelCount >= 2)
            {
                // Architecture A: Wheeled Quadruped / Wheeled Articulated Robot (e.g. M20, Go2-W, Upkie, mobile manipulator)
                // RULE: Wheels perform 100% of locomotion via velocity drive.
                // Articulated leg/arm joints hold firm stable stance. Strictly NO human-like leg trotting!
                ConfigureGenericWheeledQuadruped(robotRoot, desc);
            }
            else if (hasWheels)
            {
                // Architecture B: Pure Wheeled Mobile Rover (e.g. 2, 4, 6, 8-wheel rovers, Rocker-Bogie, Ackermann/steered)
                // Differential skid-steer or active corner steering with passive suspension.
                ConfigureGenericWheeledRover(robotRoot, desc, profile, targetTorque, targetDamping);
            }
            else if (nonWheelCount >= 8)
            {
                // Architecture C: Legged Quadruped / Multiped (e.g. Go2, Go1, ANYmal, Spot, Barkour, Hexapod)
                // True quadruped trotting gait with WASD and physical posture stabilization.
                ConfigureGenericQuadruped(robotRoot, desc);
            }
            else
            {
                // Architecture D: Robotic Arm / Fixed Articulated Mechanism (< 8 non-wheel joints, no wheels)
                // Firm position holding across all articulated joints without trotting.
                ConfigureGenericArticulated(robotRoot, desc, profile, targetTorque, targetDamping);
            }

            // 5. Universal Ground Clearance Protection:
            // Prevents chassis/battery/bracket colliders from protruding below wheels and beaching the rover.
            EnsureWheelGroundClearance(robotRoot, desc);
        }

        #region Generic Physics-Driven Kinematic Configurations

        private static void ConfigureGenericWheeledQuadruped(GameObject robotRoot, RoverDescriptor desc)
        {
            float wheelTorque = 2500f;
            float wheelDamping = 250f;

            var wheelInfos = new List<WheelDriveInfo>();
            foreach (var wheel in desc.Wheels)
            {
                WheelAxis axis = ResolveAndUnlockWheelDriveAxis(wheel);
                ConfigureWheelDrivePhysics(wheel, axis, wheelTorque, wheelDamping);
                bool isRight = RoverAnalyzer.IsRightSideWheel(wheel, desc.Chassis);
                float driveMult = RoverAnalyzer.CalculateWheelDriveMultiplier(wheel, desc.Chassis);
                wheelInfos.Add(new WheelDriveInfo(wheel, axis, isRight, false, driveMult));
            }

            var drive = GetOrAddComponent<SimpleDifferentialDrive>(robotRoot);
            drive.SetWheels(wheelInfos);
            drive.maxSpeed = 350f;
            drive.acceleration = 700f;
            drive.maxTorque = wheelTorque;
            drive.wheelDamping = wheelDamping;
            drive.turnFactor = 0.8f;
            drive.enabled = true;

            var controller = GetOrAddComponent<ArticulatedRobotController>(robotRoot);
            controller.jointStiffness = 3000f;
            controller.jointDamping = 220f;
            controller.maxTorque = 2000f;
            controller.enableInteractiveDrive = false; // Never trot while wheels drive
            controller.enableKeyboardHotkeys = true;
            controller.InitializeJoints();
            controller.joints.RemoveAll(j => wheelInfos.Any(w => w.body == j.body));
            controller.currentPreset = ArticulatedRobotController.StancePreset.Stand;
            controller.ApplyStandStance();

            // Guarantee wheels remain pure velocity drives
            foreach (var w in wheelInfos)
            {
                ConfigureWheelDrivePhysics(w.body, w.axis, wheelTorque, wheelDamping);
            }

            var marker = GetOrAddComponent<RoverCompatibilityMarker>(robotRoot);
            marker.Stamp(wheelInfos.Count, desc.Chassis != null ? desc.Chassis.name : "None", desc.TotalMass);
            Debug.Log($"<color=green>[Rover Compatibility] SUCCESS:</color> Wheeled Quadruped '{robotRoot.name}' configured with {wheelInfos.Count} active wheels and firm leg posture.");
        }

        private static void ConfigureGenericQuadruped(GameObject robotRoot, RoverDescriptor desc)
        {
            RemoveComponentIfExists<SimpleDifferentialDrive>(robotRoot);

            var controller = GetOrAddComponent<ArticulatedRobotController>(robotRoot);
            controller.jointStiffness = 2500f;
            controller.jointDamping = 180f;
            controller.maxTorque = 1500f;
            controller.stepAmplitudeDeg = 18.0f; // Realistic animal trotting amplitude
            controller.gaitCycleSpeed = 1.8f;   // Natural ~1.8 Hz trotting frequency
            controller.walkSpeed = 1.2f;        // 1.2 m/s forward walking speed
            controller.turnSpeed = 60f;         // 60 deg/s yaw rate
            controller.enableInteractiveDrive = true;
            controller.enableKeyboardHotkeys = true;
            controller.InitializeJoints();
            controller.currentPreset = ArticulatedRobotController.StancePreset.Stand;
            controller.ApplyStandStance();

            var marker = GetOrAddComponent<RoverCompatibilityMarker>(robotRoot);
            marker.Stamp(desc.RobotType.ToString(), controller.joints.Count, 0, desc.Chassis != null ? desc.Chassis.name : "None", desc.TotalMass);
            Debug.Log($"<color=green>[Rover Compatibility] SUCCESS:</color> Quadruped '{robotRoot.name}' configured with {controller.joints.Count} calibrated joints.");
        }

        private static void ConfigureGenericWheeledRover(GameObject robotRoot, RoverDescriptor desc, RoverMassProfile profile, float targetTorque, float targetDamping)
        {
            RemoveComponentIfExists<ArticulatedRobotController>(robotRoot);

            foreach (var susp in desc.SuspensionJoints)
            {
                if (susp == null || susp.isRoot) continue;
                var suspDrive = susp.xDrive;
                suspDrive.driveType = ArticulationDriveType.Target;
                suspDrive.stiffness = 0f;
                suspDrive.damping = 60f;
                suspDrive.forceLimit = 2000f;
                susp.xDrive = suspDrive;
                susp.twistLock = ArticulationDofLock.FreeMotion;
                susp.linearLockX = ArticulationDofLock.LockedMotion;
                susp.linearLockY = ArticulationDofLock.LockedMotion;
                susp.linearLockZ = ArticulationDofLock.LockedMotion;
                susp.useGravity = true;
            }

            foreach (var steer in desc.SteeringJoints)
            {
                if (steer == null || steer.isRoot) continue;
                float steerStiffness = Mathf.Max(6000f, desc.TotalMass * 25f);
                float steerForce = Mathf.Max(6000f, desc.TotalMass * 25f);
                var steerDrive = steer.xDrive;
                steerDrive.driveType = ArticulationDriveType.Target;
                steerDrive.stiffness = steerStiffness;
                steerDrive.damping = 400f;
                steerDrive.forceLimit = steerForce;
                steerDrive.target = 0f;
                steer.xDrive = steerDrive;
                steer.twistLock = ArticulationDofLock.LimitedMotion;
                steer.linearLockX = ArticulationDofLock.LockedMotion;
                steer.linearLockY = ArticulationDofLock.LockedMotion;
                steer.linearLockZ = ArticulationDofLock.LockedMotion;
                steer.useGravity = true;
            }

            var wheelInfos = new List<WheelDriveInfo>();
            foreach (var wheel in desc.Wheels)
            {
                WheelAxis axis = ResolveAndUnlockWheelDriveAxis(wheel);
                ConfigureWheelDrivePhysics(wheel, axis, targetTorque, targetDamping);

                bool isRight = RoverAnalyzer.IsRightSideWheel(wheel, desc.Chassis);
                float driveMult = RoverAnalyzer.CalculateWheelDriveMultiplier(wheel, desc.Chassis);
                var info = new WheelDriveInfo(wheel, axis, isRight, false, driveMult);
                wheelInfos.Add(info);

                Debug.Log($"[Rover Compatibility] Wheel '{wheel.name}' configured on Axis {axis} | Side: {(isRight ? "Right" : "Left")} | DriveMult: {driveMult}");
            }

            if (wheelInfos.Count == 0)
            {
                Debug.LogError("[Rover Compatibility] Failed to configure any wheel drives - aborting controller setup.");
                return;
            }

            var drive = GetOrAddComponent<SimpleDifferentialDrive>(robotRoot);
            drive.SetWheels(wheelInfos);
            drive.SetSteeringJoints(desc.SteeringJoints);
            drive.maxSpeed = DefaultMaxSpeed;
            drive.maxTorque = targetTorque;
            drive.wheelDamping = targetDamping;
            drive.enabled = true;

            var marker = GetOrAddComponent<RoverCompatibilityMarker>(robotRoot);
            marker.Stamp(wheelInfos.Count, desc.Chassis != null ? desc.Chassis.name : "None", desc.TotalMass);

            Debug.Log($"<color=green>[Rover Compatibility] SUCCESS:</color> Wheeled Rover '{robotRoot.name}' configured with {wheelInfos.Count} active wheels and {desc.SuspensionJoints.Count} suspension / {desc.SteeringJoints.Count} steering joints.");
        }

        private static void ConfigureGenericArticulated(GameObject robotRoot, RoverDescriptor desc, RoverMassProfile profile, float targetTorque, float targetDamping)
        {
            float holdingStiffness = Mathf.Max(3000f, desc.TotalMass * 300f);
            float holdingDamping = Mathf.Max(250f, Mathf.Sqrt(holdingStiffness * Mathf.Max(10f, desc.TotalMass)) * 1.4f);
            float holdingForce = Mathf.Max(3000f, desc.TotalMass * 600f);

            var targetBodies = (desc.NonWheelJoints.Count > 0 ? desc.NonWheelJoints : desc.AllBodies.Where(b => !b.isRoot).ToList())
                .Where(b => desc.Wheels == null || !desc.Wheels.Contains(b))
                .ToList();

            foreach (var body in targetBodies)
            {
                if (body == null || body.isRoot) continue;
                if (body.jointType != ArticulationJointType.RevoluteJoint && body.jointType != ArticulationJointType.PrismaticJoint) continue;

                var drive = body.xDrive;
                drive.driveType = ArticulationDriveType.Target;
                drive.stiffness = holdingStiffness;
                drive.damping = holdingDamping;
                drive.forceLimit = holdingForce;
                drive.target = Mathf.Clamp(drive.target, drive.lowerLimit, drive.upperLimit);

                body.xDrive = drive;
                body.useGravity = true;

                body.linearLockX = ArticulationDofLock.LockedMotion;
                body.linearLockY = ArticulationDofLock.LockedMotion;
                body.linearLockZ = ArticulationDofLock.LockedMotion;
                body.swingYLock = ArticulationDofLock.LockedMotion;
                body.swingZLock = ArticulationDofLock.LockedMotion;
            }

            var controller = GetOrAddComponent<ArticulatedRobotController>(robotRoot);
            controller.jointStiffness = holdingStiffness;
            controller.jointDamping = holdingDamping;
            controller.maxTorque = holdingForce;
            controller.enableInteractiveDrive = false; // Arms & generic articulated trees never walk like a human!
            controller.enableKeyboardHotkeys = false;
            controller.InitializeJoints();
            controller.currentPreset = ArticulatedRobotController.StancePreset.Stand;
            controller.ApplyStandStance();

            var marker = GetOrAddComponent<RoverCompatibilityMarker>(robotRoot);
            marker.Stamp(desc.RobotType.ToString(), targetBodies.Count, 0, desc.Chassis != null ? desc.Chassis.name : "None", desc.TotalMass);
        }

        private static T GetOrAddComponent<T>(GameObject go) where T : Component
        {
            var comp = go.GetComponent<T>();
            if (comp == null) comp = go.AddComponent<T>();
            return comp;
        }

        private static void RemoveComponentIfExists<T>(GameObject go) where T : Component
        {
            var comp = go.GetComponent<T>();
            if (comp != null)
            {
#if UNITY_EDITOR
                UnityEngine.Object.DestroyImmediate(comp);
#else
                UnityEngine.Object.Destroy(comp);
#endif
            }
        }

        #endregion

        /// <summary>
        /// In Unity's official URDF Importer, revolute and continuous joints are imported
        /// as ArticulationJointType.RevoluteJoint. In Unity PhysX, a RevoluteJoint possesses
        /// ONLY ONE rotational degree of freedom: the TWIST (X) axis!
        ///
        /// The official importer aligns the URDF joint axis to the Twist (X) axis using anchorRotation.
        /// Therefore, attempting to drive yDrive or zDrive on a RevoluteJoint is completely ignored by PhysX,
        /// and locking twistLock completely freezes the wheel.
        ///
        /// This method guarantees that twistLock is set to FreeMotion (or the chosen axis for non-revolute),
        /// locking all non-driving axes to prevent wobble.
        /// </summary>
        public static WheelAxis ResolveAndUnlockWheelDriveAxis(ArticulationBody wheel)
        {
            if (wheel.jointType == ArticulationJointType.FixedJoint)
            {
                wheel.jointType = ArticulationJointType.RevoluteJoint;
            }

            WheelAxis chosenAxis = WheelAxis.X;

            if (wheel.jointType == ArticulationJointType.RevoluteJoint)
            {
                // In Unity PhysX, RevoluteJoint DOF is strictly on the Twist (X) axis
                chosenAxis = WheelAxis.X;
            }
            else if (wheel.swingYLock == ArticulationDofLock.FreeMotion)
            {
                chosenAxis = WheelAxis.Y;
            }
            else if (wheel.swingZLock == ArticulationDofLock.FreeMotion)
            {
                chosenAxis = WheelAxis.Z;
            }
            else
            {
                chosenAxis = WheelAxis.X;
            }

            // CRITICAL: Explicitly unlock the chosen axis in PhysX and lock all other axes.
            // This prevents the class of bugs where the wheel is locked or spins on the wrong axis.
            switch (chosenAxis)
            {
                case WheelAxis.X:
                    wheel.twistLock = ArticulationDofLock.FreeMotion;
                    wheel.swingYLock = ArticulationDofLock.LockedMotion;
                    wheel.swingZLock = ArticulationDofLock.LockedMotion;
                    break;
                case WheelAxis.Y:
                    wheel.swingYLock = ArticulationDofLock.FreeMotion;
                    wheel.twistLock = ArticulationDofLock.LockedMotion;
                    wheel.swingZLock = ArticulationDofLock.LockedMotion;
                    break;
                case WheelAxis.Z:
                    wheel.swingZLock = ArticulationDofLock.FreeMotion;
                    wheel.twistLock = ArticulationDofLock.LockedMotion;
                    wheel.swingYLock = ArticulationDofLock.LockedMotion;
                    break;
            }

            wheel.linearLockX = ArticulationDofLock.LockedMotion;
            wheel.linearLockY = ArticulationDofLock.LockedMotion;
            wheel.linearLockZ = ArticulationDofLock.LockedMotion;

            return chosenAxis;
        }

        public static void ConfigureWheelDrivePhysics(ArticulationBody wheel, WheelAxis axis, float forceLimit = -1f, float damping = -1f)
        {
            if (forceLimit <= 0f) forceLimit = DefaultWheelForceLimit;
            if (damping <= 0f) damping = DefaultWheelDamping;

            // Note: xDrive, yDrive, and zDrive are struct properties in Unity C#
            // and cannot be passed by ref. We read the struct, modify it, and write it back.
            ArticulationDrive drive = GetDrive(wheel, axis);

            drive.stiffness = 0f;                           // Pure velocity control (mandatory: stiffness > 0 creates a spring holding its position)
            drive.damping = damping;                        // Acts as velocity control gain in PhysX
            drive.forceLimit = forceLimit;                  // Torque limit in Nm
            drive.target = 0f;
            drive.targetVelocity = 0f;
            drive.driveType = ArticulationDriveType.Velocity;

            SetDrive(wheel, axis, drive);

            wheel.useGravity = true;

            // Ensure colliders on the wheel are convex (mandatory for PhysX ArticulationBody)
            // and assign an optimized PhysicMaterial for smooth skid-steering
            var wheelMat = GetOrCreateWheelPhysicMaterial();

            if (EnsureConvexColliders)
            {
                var meshColliders = wheel.GetComponentsInChildren<MeshCollider>(true);
                foreach (var mc in meshColliders)
                {
                    mc.convex = true;
                    if (wheelMat != null) mc.sharedMaterial = wheelMat;
                }
            }

            var allColliders = wheel.GetComponentsInChildren<Collider>(true);
            foreach (var col in allColliders)
            {
                if (wheelMat != null) col.sharedMaterial = wheelMat;
            }
        }

        public static PhysicsMaterial GetOrCreateWheelPhysicMaterial()
        {
            if (_wheelPhysicMaterial == null)
            {
                _wheelPhysicMaterial = new PhysicsMaterial("RoverWheelMaterial")
                {
                    dynamicFriction = 0.40f,
                    staticFriction = 0.45f,
                    frictionCombine = PhysicsMaterialCombine.Minimum,
                    bounciness = 0.0f,
                    bounceCombine = PhysicsMaterialCombine.Minimum
                };
            }
            return _wheelPhysicMaterial;
        }

        private static void CleanConflictingImporterControllers(GameObject root)
        {
            // Clean conflicting URDF Importer default controllers and old dedicated per-model controllers
            var jointControls = root.GetComponentsInChildren<MonoBehaviour>(true);
            foreach (var mb in jointControls)
            {
                if (mb == null) continue;
                string typeName = mb.GetType().Name;
                string typeNs = mb.GetType().Namespace ?? "";
                if (typeNs == "RoverCompatibility" ||
                    typeName == "JointControl" || typeName == "Controller" || typeName == "FKRobot" ||
                    typeName.StartsWith("RoverController_") || typeName.StartsWith("RoverImporter_"))
                {
                    mb.enabled = false;
#if UNITY_EDITOR
                    UnityEngine.Object.DestroyImmediate(mb);
#else
                    UnityEngine.Object.Destroy(mb);
#endif
                }
            }
        }

        public static ArticulationDrive GetDrive(ArticulationBody body, WheelAxis axis)
        {
            switch (axis)
            {
                case WheelAxis.X: return body.xDrive;
                case WheelAxis.Y: return body.yDrive;
                default: return body.zDrive;
            }
        }

        public static void SetDrive(ArticulationBody body, WheelAxis axis, ArticulationDrive drive)
        {
            switch (axis)
            {
                case WheelAxis.X: body.xDrive = drive; break;
                case WheelAxis.Y: body.yDrive = drive; break;
                default: body.zDrive = drive; break;
            }
        }

        /// <summary>
        /// Universally guarantees that wheel colliders form the lowest contact points of the robot.
        /// If CAD collision hulls on the chassis underbelly, battery, or knuckle frames extend lower
        /// than or flush with the wheels, they beach the vehicle (absorbing all normal force and leaving
        /// spinning wheels with zero traction).
        /// This method automatically detects any non-wheel colliders intruding into the ground contact plane
        /// and lifts their collision hulls so the wheels maintain full physical traction with terrain.
        /// </summary>
        public static void EnsureWheelGroundClearance(GameObject robotRoot, RoverDescriptor desc)
        {
            if (robotRoot == null || desc == null || desc.Wheels == null || desc.Wheels.Count == 0) return;

            // 1. Determine lowest point among all wheel colliders
            float lowestWheelY = float.MaxValue;
            var wheelColliders = new HashSet<Collider>();

            foreach (var wheel in desc.Wheels)
            {
                if (wheel == null) continue;
                var cols = wheel.GetComponentsInChildren<Collider>(true);
                foreach (var c in cols)
                {
                    if (c == null) continue;
                    wheelColliders.Add(c);
                    if (c.bounds.min.y < lowestWheelY) lowestWheelY = c.bounds.min.y;
                }
            }

            if (lowestWheelY == float.MaxValue) return;

            // Frictionless low-friction material for non-wheel chassis parts so they slide smoothly over bumps
            var slickMat = new PhysicsMaterial("ChassisUnderbellySlick")
            {
                dynamicFriction = 0.05f,
                staticFriction = 0.05f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounciness = 0.0f,
                bounceCombine = PhysicsMaterialCombine.Minimum
            };

            float minRequiredClearance = 0.02f; // Minimum 2 cm ground clearance for underbelly / brackets
            var allColliders = robotRoot.GetComponentsInChildren<Collider>(true);

            foreach (var col in allColliders)
            {
                if (col == null || wheelColliders.Contains(col)) continue;

                // Assign low-friction material to chassis to eliminate high-centering drag
                col.sharedMaterial = slickMat;

                float clearance = col.bounds.min.y - lowestWheelY;
                if (clearance < minRequiredClearance)
                {
                    float liftAmount = minRequiredClearance - clearance;
                    col.transform.position += new Vector3(0f, liftAmount, 0f);
                    Debug.Log($"[Rover Compatibility] Ground Clearance Auto-Heal: Lifted non-wheel collider '{col.name}' by {liftAmount * 100f:F1} cm to prevent vehicle high-centering.");
                }
            }

            Physics.SyncTransforms();
        }
    }
}


using System;
using System.Collections.Generic;
using UnityEngine;

namespace AutonomousRobotKit
{
    /// <summary>
    /// Universal differential and steered drive velocity controller for ArticulationBody-based rovers.
    /// Supports:
    /// 1. Skid-steer rovers (Husky, M20, Upkie, Turtlebot, etc.).
    /// 2. Ackermann and 4-corner steered rovers (Perseverance, Curiosity, RB-Vogui).
    /// Features:
    /// - Smooth acceleration curves to eliminate tipping and lateral chatter.
    /// - Auto-resolves steering knuckle joint rotation axes so wheels never fight each other.
    /// - Velocity clamping preventing high-speed spin spikes during turns.
    /// - Compatible with both New Input System and Legacy Input Manager.
    /// </summary>
    [DisallowMultipleComponent]
    public class SimpleDifferentialDrive : MonoBehaviour
    {
        [Header("Wheel Configuration")]
        [Tooltip("List of detected rover wheels and their drive axes (supports 2, 4, 6, 8 or N wheels).")]
        public List<WheelDriveInfo> wheels = new List<WheelDriveInfo>();

        [Header("Steering Configuration (Optional)")]
        [Tooltip("Active steering knuckle joints (e.g. for Ackermann or 4/6-corner steered rovers).")]
        public List<ArticulationBody> steeringJoints = new List<ArticulationBody>();

        [Tooltip("Maximum steering angle for steering knuckle joints in degrees.")]
        public float maxSteerAngle = 35f;

        [Header("Drive Parameters")]
        [Tooltip("Maximum wheel rotational speed in degrees per second (e.g. 400 deg/s ≈ 6.98 rad/s).")]
        public float maxSpeed = 400f;

        [Tooltip("Acceleration in deg/s^2 to smooth torque delivery and prevent vehicle tipping.")]
        public float acceleration = 800f;

        [Tooltip("Maximum motor torque per wheel in Nm.")]
        public float maxTorque = 5000f;

        [Tooltip("Drive damping (velocity gain in PhysX). Keep > 0 to maintain torque under velocity control.")]
        public float wheelDamping = 500f;

        [Tooltip("Steering agility multiplier for differential skid-steering.")]
        [Range(0.1f, 2f)]
        public float turnFactor = 0.75f;

        [Header("Direction Inversion")]
        public bool invertForward = false;
        public bool invertTurn = false;
        public bool invertLeftSide = false;
        public bool invertRightSide = false;

        [Header("External / UI Drive Input")]
        public bool useExternalInput = false;
        public float externalForwardInput = 0f;
        public float externalTurnInput = 0f;

        [Header("Live Diagnostics")]
        [SerializeField] private float currentForwardInput = 0f;
        [SerializeField] private float currentTurnInput = 0f;
        [SerializeField] private float smoothedForwardVelocity = 0f;
        [SerializeField] private float smoothedTurnVelocity = 0f;
        [SerializeField] private float smoothedSteerAngle = 0f;

        public float CurrentForwardInput => currentForwardInput;
        public float CurrentTurnInput => currentTurnInput;
        public float SmoothedForwardVelocity => smoothedForwardVelocity;

        public void SetWheels(List<WheelDriveInfo> detectedWheels)
        {
            wheels = new List<WheelDriveInfo>(detectedWheels);
        }

        public void SetSteeringJoints(List<ArticulationBody> detectedSteeringJoints)
        {
            steeringJoints = new List<ArticulationBody>(detectedSteeringJoints);
        }

        private void FixedUpdate()
        {
            if (wheels == null || wheels.Count == 0) return;

            ReadInput(out float forward, out float turn);

            if (useExternalInput)
            {
                if (Mathf.Abs(externalForwardInput) > Mathf.Abs(forward)) forward = externalForwardInput;
                if (Mathf.Abs(externalTurnInput) > Mathf.Abs(turn)) turn = externalTurnInput;
            }

            currentForwardInput = forward;
            currentTurnInput = turn;

            if (invertForward) forward *= -1f;
            if (invertTurn) turn *= -1f;

            float targetLongitudinal = forward * maxSpeed;
            float targetTurn = turn * maxSpeed * turnFactor;

            float dt = Time.fixedDeltaTime;
            smoothedForwardVelocity = Mathf.MoveTowards(smoothedForwardVelocity, targetLongitudinal, acceleration * dt);
            smoothedTurnVelocity = Mathf.MoveTowards(smoothedTurnVelocity, targetTurn, acceleration * dt);

            // Update active steering knuckle angles with smooth slew-rate limiting
            if (steeringJoints != null && steeringJoints.Count > 0)
            {
                float targetAngle = currentTurnInput * maxSteerAngle;
                smoothedSteerAngle = Mathf.MoveTowards(smoothedSteerAngle, targetAngle, 90f * dt);
                for (int s = 0; s < steeringJoints.Count; s++)
                {
                    var steer = steeringJoints[s];
                    if (steer == null) continue;
                    var drive = steer.xDrive;
                    drive.target = GetSteerJointAngle(steer, smoothedSteerAngle);
                    steer.xDrive = drive;
                }
            }

            for (int i = 0; i < wheels.Count; i++)
            {
                var info = wheels[i];
                if (info == null || info.body == null) continue;

                float targetVel = smoothedForwardVelocity;

                if (info.isRightSide)
                {
                    targetVel -= smoothedTurnVelocity;
                    if (invertRightSide) targetVel *= -1f;
                }
                else
                {
                    targetVel += smoothedTurnVelocity;
                    if (invertLeftSide) targetVel *= -1f;
                }

                if (info.invertDirection)
                {
                    targetVel *= -1f;
                }

                targetVel = Mathf.Clamp(targetVel, -maxSpeed, maxSpeed);
                ApplyWheelVelocity(info, targetVel);
            }
        }

        private float GetSteerJointAngle(ArticulationBody steer, float steerAngleDeg)
        {
            if (steer == null) return 0f;

            Transform rootTransform = transform;
            Vector3 localPos = rootTransform.InverseTransformPoint(steer.transform.position);
            string name = steer.name.ToLowerInvariant();

            bool isFront = localPos.z >= 0f;
            if (name.Contains("front") || name.Contains("_f") || name.StartsWith("f")) isFront = true;
            else if (name.Contains("rear") || name.Contains("back") || name.Contains("_r") || name.Contains("_h")) isFront = false;

            Vector3 worldAxis = steer.transform.rotation * steer.anchorRotation * Vector3.right;
            float upDot = Vector3.Dot(worldAxis, rootTransform.up);
            float axisSign = upDot >= 0f ? 1f : -1f;

            float longitudinalSign = isFront ? 1f : -1f;
            return steerAngleDeg * longitudinalSign * axisSign;
        }

        private void ApplyWheelVelocity(WheelDriveInfo info, float targetVel)
        {
            if (info == null || info.body == null) return;

            if (info.body.jointType == ArticulationJointType.FixedJoint)
            {
                info.body.jointType = ArticulationJointType.RevoluteJoint;
            }

            if (info.axis == WheelAxis.X && info.body.twistLock != ArticulationDofLock.FreeMotion)
                info.body.twistLock = ArticulationDofLock.FreeMotion;
            else if (info.axis == WheelAxis.Y && info.body.swingYLock != ArticulationDofLock.FreeMotion)
                info.body.swingYLock = ArticulationDofLock.FreeMotion;
            else if (info.axis == WheelAxis.Z && info.body.swingZLock != ArticulationDofLock.FreeMotion)
                info.body.swingZLock = ArticulationDofLock.FreeMotion;

            ArticulationDrive drive = RoverAutoConfigurator.GetDrive(info.body, info.axis);
            drive.targetVelocity = targetVel;
            drive.forceLimit = maxTorque;
            drive.stiffness = 0f;
            drive.damping = wheelDamping;
            drive.driveType = ArticulationDriveType.Velocity;
            RoverAutoConfigurator.SetDrive(info.body, info.axis, drive);
        }

        private void ReadInput(out float forward, out float turn)
        {
            forward = 0f;
            turn = 0f;

            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null)
            {
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) forward += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) forward -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) turn += 1f;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) turn -= 1f;
            }

            try
            {
                if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) forward += 1f;
                if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) forward -= 1f;
                if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) turn += 1f;
                if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) turn -= 1f;

                float v = Input.GetAxisRaw("Vertical");
                float h = Input.GetAxisRaw("Horizontal");
                if (Mathf.Abs(v) > Mathf.Abs(forward)) forward = v;
                if (Mathf.Abs(h) > Mathf.Abs(turn)) turn = h;
            }
            catch (InvalidOperationException) { }

            forward = Mathf.Clamp(forward, -1f, 1f);
            turn = Mathf.Clamp(turn, -1f, 1f);
        }
    }
}

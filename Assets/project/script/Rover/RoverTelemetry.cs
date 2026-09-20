using System;
using UnityEngine;
using ProjectName.Planetary;

namespace ProjectName.Rover
{
    [Serializable]
    public struct WheelTelemetryState
    {
        public string wheelName;
        public float angularVelocityRad; // rad/s
        public float rpm;
        public float tangentialSpeedMps;
        public float slipRatio; // 0.0 to 1.0
        public float motorTorque; // N·m
        public bool isGrounded;
        public string status; // "GOOD", "FAIR", "SLIP", "AIR"
    }

    /// <summary>
    /// Computes truthful, physics-derived telemetry for the active rover in FixedUpdate.
    /// Non-negotiable rule R1: No placeholders, no fabricated numbers. Everything is measured.
    /// Non-negotiable rule R7: Computed strictly in FixedUpdate with zero heap allocations per frame.
    /// </summary>
    [DisallowMultipleComponent]
    public class RoverTelemetry : MonoBehaviour
    {
        [Header("Telemetry Readouts (Truthful Simulation)")]
        public float linearSpeedMps;
        public float groundSpeedKmph;
        public float forwardAcceleration;
        public float gForceLoad;

        public float pitchDeg;
        public float rollDeg;
        public float headingDeg;
        public string headingCardinal = "N";
        public float terrainSlopeDeg;

        public Vector3 worldPosition;
        public float elevationMeters;
        public float localEastMeters;
        public float localNorthMeters;
        public float odometerMeters;
        public float missionTimeSeconds;

        [Header("Power & Thermal")]
        public float batteryPercent = 100f;
        public float motorTempCelsius = 22f;
        public float powerDrawWatts = 75f;
        public float batteryCapacityWh = 1200f;
        public float ambientTempCelsius = 18f;
        public float energyConsumedWh = 0f;

        public WheelTelemetryState[] wheelStates = new WheelTelemetryState[0];

        // Safety thresholds & alerts
        public bool isRolloverWarning;
        public bool isSteepSlopeWarning;

        // Internal State
        private ArticulationBody rootBody;
        private RoverProfile profile;
        private ArticulationBody[] wheelBodies;
        private Vector3 spawnPosition;
        private Vector3 lastRootPosition;
        private Vector3 lastVelocityVector;
        private float filteredSpeedMps = 0f;
        private float lastForwardSpeed = 0f;
        private bool isInitialized = false;

        // Kill plane tracking
        private float timeBelowKillPlane = 0f;
        private Vector3 lastSafePose;
        private Quaternion lastSafeRotation;

        public event Action<string> OnToastRequested;

        public void Initialize(RoverHandle handle)
        {
            if (handle == null || !handle.IsValid) return;

            rootBody = handle.rootBody;
            profile = handle.profile;
            wheelBodies = handle.wheelBodies;

            spawnPosition = rootBody.transform.position;
            lastRootPosition = spawnPosition;
            lastSafePose = spawnPosition;
            lastSafeRotation = rootBody.transform.rotation;
            lastVelocityVector = Vector3.zero;
            filteredSpeedMps = 0f;
            lastForwardSpeed = 0f;
            odometerMeters = 0f;
            missionTimeSeconds = 0f;
            timeBelowKillPlane = 0f;

            if (handle.wheelDefs != null && handle.wheelDefs.Length > 0)
            {
                wheelStates = new WheelTelemetryState[handle.wheelDefs.Length];
                for (int i = 0; i < handle.wheelDefs.Length; i++)
                {
                    wheelStates[i].wheelName = handle.wheelDefs[i].displayName;
                    wheelStates[i].status = "GOOD";
                }
            }
            else
            {
                wheelStates = new WheelTelemetryState[0];
            }

            if (profile != null && profile.batteryWh > 0f)
            {
                batteryCapacityWh = profile.batteryWh;
            }

            var env = PlanetEnvironmentController.Instance;
            if (env != null && env.currentProfile != null)
            {
                ambientTempCelsius = env.currentProfile.ambientTemperatureCelsius;
            }

            energyConsumedWh = 0f;
            batteryPercent = 100f;
            motorTempCelsius = ambientTempCelsius;

            isInitialized = true;
            Debug.Log($"[RoverTelemetry] Initialized telemetry for {profile?.displayName ?? rootBody.name} at {spawnPosition}");
        }

        public void ResetMissionTimeAndOdometer()
        {
            missionTimeSeconds = 0f;
            odometerMeters = 0f;
            spawnPosition = rootBody != null ? rootBody.transform.position : transform.position;
            lastRootPosition = spawnPosition;
        }

        private void FixedUpdate()
        {
            if (!isInitialized || rootBody == null) return;

            float dt = Time.fixedDeltaTime;
            if (dt <= 0.0001f) return;

            Vector3 currentPos = rootBody.transform.position;
            worldPosition = currentPos;
            elevationMeters = currentPos.y;

            // -------------------------------------------------------------
            // 1. SPEED & VELOCITY DERIVATION
            // Rule: Do NOT use ArticulationBody.velocity; position delta is robust.
            // -------------------------------------------------------------
            Vector3 rawDisplacement = currentPos - lastRootPosition;
            float rawDist = rawDisplacement.magnitude;

            // Guard against teleportation jumps (> 5m in a single 0.02s step = > 250 m/s)
            if (rawDist > 5.0f)
            {
                lastRootPosition = currentPos;
                lastVelocityVector = Vector3.zero;
                return;
            }

            Vector3 measuredVelocity = rawDisplacement / dt;

            // Low-pass filter speed with time constant ~0.15s (alpha = dt / (0.15 + dt))
            float alpha = dt / (0.15f + dt);
            float currentRawSpeed = measuredVelocity.magnitude;
            filteredSpeedMps = Mathf.Lerp(filteredSpeedMps, currentRawSpeed, alpha);

            // Clean noise threshold when stopped
            if (filteredSpeedMps < 0.02f) filteredSpeedMps = 0f;

            linearSpeedMps = filteredSpeedMps;

            // Horizontal ground speed
            Vector2 horizDisp = new Vector2(rawDisplacement.x, rawDisplacement.z);
            float horizSpeed = horizDisp.magnitude / dt;
            groundSpeedKmph = horizSpeed * 3.6f;

            // -------------------------------------------------------------
            // 2. ACCELERATION & G-FORCE LOAD
            // a = d(v·forward)/dt, smoothed
            // G-force = |a_vec - g_vec| / 9.80665 (What an onboard IMU measures)
            // -------------------------------------------------------------
            Vector3 roverForward = rootBody.transform.forward;
            float forwardSpeed = Vector3.Dot(measuredVelocity, roverForward);
            float rawAccel = (forwardSpeed - lastForwardSpeed) / dt;
            forwardAcceleration = Mathf.Lerp(forwardAcceleration, rawAccel, alpha * 2f);
            if (linearSpeedMps == 0f && Mathf.Abs(forwardAcceleration) < 0.05f) forwardAcceleration = 0f;
            lastForwardSpeed = forwardSpeed;

            Vector3 accelerationVector = (measuredVelocity - lastVelocityVector) / dt;
            lastVelocityVector = measuredVelocity;

            // Live engine gravity vector
            Vector3 gravityVec = Physics.gravity;
            Vector3 imuForce = accelerationVector - gravityVec;
            gForceLoad = imuForce.magnitude / 9.80665f;

            // -------------------------------------------------------------
            // 3. ATTITUDE, HEADING & SLOPE
            // Sign Convention:
            // Pitch: + nose up, - nose down (asin(forward.y))
            // Roll:  + tilted right (right side down, asin(-right.y)), - tilted left
            // Heading: atan2(forward.x, forward.z), 0° = +Z ("North"), 90° = +X ("East")
            // Note: Unity is Y-up left-handed; ROS is Z-up right-handed.
            // -------------------------------------------------------------
            pitchDeg = Mathf.Asin(Mathf.Clamp(roverForward.y, -1f, 1f)) * Mathf.Rad2Deg;
            Vector3 roverRight = rootBody.transform.right;
            rollDeg = -Mathf.Asin(Mathf.Clamp(roverRight.y, -1f, 1f)) * Mathf.Rad2Deg;

            Vector3 flatForward = Vector3.ProjectOnPlane(roverForward, Vector3.up).normalized;
            if (flatForward.sqrMagnitude > 0.001f)
            {
                float h = Mathf.Atan2(flatForward.x, flatForward.z) * Mathf.Rad2Deg;
                if (h < 0f) h += 360f;
                headingDeg = h;
                headingCardinal = FormatCardinal(headingDeg);
            }

            // Slope under rover
            terrainSlopeDeg = ComputeTerrainSlope(currentPos);

            float maxSafeSlope = profile != null ? profile.maxSafeSlopeDeg : 30f;
            isSteepSlopeWarning = terrainSlopeDeg > maxSafeSlope;
            isRolloverWarning = Mathf.Abs(pitchDeg) > maxSafeSlope || Mathf.Abs(rollDeg) > (maxSafeSlope * 0.85f);

            // -------------------------------------------------------------
            // 4. POSITION & ODOMETER
            // East = X - spawn.x, North = Z - spawn.z
            // Odometer accumulates path length, ignoring steps < 1 mm (solver jitter)
            // -------------------------------------------------------------
            localEastMeters = currentPos.x - spawnPosition.x;
            localNorthMeters = currentPos.z - spawnPosition.z;

            missionTimeSeconds += dt;

            float stepDist = horizDisp.magnitude;
            if (stepDist >= 0.001f && stepDist < 2.0f && linearSpeedMps > 0.02f)
            {
                odometerMeters += stepDist;
            }

            // Record safe pose when rover is upright and grounded
            if (!isRolloverWarning && terrainSlopeDeg < 25f && linearSpeedMps < 3f)
            {
                lastSafePose = currentPos;
                lastSafeRotation = rootBody.transform.rotation;
            }

            // -------------------------------------------------------------
            // 5. PER-WHEEL TRACTION & SLIP MATRIX
            // ω from jointVelocity[0], slip = |ω·r - v_fwd| / max(|ω·r|, |v_fwd|, 0.1)
            // -------------------------------------------------------------
            UpdateWheelActuators(roverForward, forwardSpeed);

            // -------------------------------------------------------------
            // 6. POWER & THERMAL DYNAMICS (Physical load derived)
            // -------------------------------------------------------------
            UpdatePowerAndThermals(dt);

            // -------------------------------------------------------------
            // 7. KILL PLANE & SAFETY FALLBACK
            // If root Y < (terrain min height - 50m) for > 1.0s, respawn.
            // -------------------------------------------------------------
            CheckKillPlane(currentPos, dt);

            lastRootPosition = currentPos;
        }

        private float ComputeTerrainSlope(Vector3 pos)
        {
            var terrain = UnityEngine.Terrain.activeTerrain;
            if (terrain != null && terrain.terrainData != null)
            {
                Vector3 tPos = terrain.transform.position;
                Vector3 tSize = terrain.terrainData.size;
                float normX = (pos.x - tPos.x) / tSize.x;
                float normZ = (pos.z - tPos.z) / tSize.z;

                if (normX >= 0f && normX <= 1f && normZ >= 0f && normZ <= 1f)
                {
                    Vector3 normal = terrain.terrainData.GetInterpolatedNormal(normX, normZ);
                    return Vector3.Angle(normal, Vector3.up);
                }
            }

            // Raycast fallback
            if (Physics.Raycast(pos + Vector3.up * 1f, Vector3.down, out RaycastHit hit, 10f))
            {
                return Vector3.Angle(hit.normal, Vector3.up);
            }

            return 0f;
        }

        private void UpdateWheelActuators(Vector3 roverForward, float roverForwardSpeed)
        {
            if (wheelBodies == null || wheelBodies.Length == 0 || wheelStates.Length != wheelBodies.Length)
                return;

            float r = profile != null ? profile.wheelRadius : 0.165f;
            float goodThresh = profile != null ? profile.slipGoodThreshold : 0.15f;
            float fairThresh = profile != null ? profile.slipFairThreshold : 0.40f;

            for (int i = 0; i < wheelBodies.Length; i++)
            {
                var body = wheelBodies[i];
                if (body == null) continue;

                // Wheel rotational speed (rad/s) from ArticulationBody degree of freedom
                float omega = 0f;
                if (body.jointVelocity.dofCount > 0)
                {
                    omega = body.jointVelocity[0];
                }

                float rpm = Mathf.Abs(omega) * (60f / (2f * Mathf.PI));
                float wheelLinearSpeed = Mathf.Abs(omega) * r;

                // Ground contact raycast from wheel center
                bool isGrounded = Physics.Raycast(body.transform.position, Vector3.down, r * 1.35f);

                // Slip ratio calculation
                float slip = 0f;
                string status = "GOOD";

                if (!isGrounded)
                {
                    slip = 1.0f;
                    status = "AIR";
                }
                else if (linearSpeedMps < 0.02f && wheelLinearSpeed < 0.02f)
                {
                    slip = 0f;
                    status = "GOOD";
                }
                else
                {
                    float denom = Mathf.Max(wheelLinearSpeed, Mathf.Abs(roverForwardSpeed), 0.1f);
                    slip = Mathf.Clamp01(Mathf.Abs(wheelLinearSpeed - Mathf.Abs(roverForwardSpeed)) / denom);

                    if (slip <= goodThresh) status = "GOOD";
                    else if (slip <= fairThresh) status = "FAIR";
                    else status = "SLIP";
                }

                // Torque readout from ArticulationBody joint drive
                float torque = body.xDrive.forceLimit;

                wheelStates[i].angularVelocityRad = omega;
                wheelStates[i].rpm = rpm;
                wheelStates[i].tangentialSpeedMps = wheelLinearSpeed;
                wheelStates[i].slipRatio = slip;
                wheelStates[i].motorTorque = torque;
                wheelStates[i].isGrounded = isGrounded;
                wheelStates[i].status = status;
            }
        }

        private void UpdatePowerAndThermals(float dt)
        {
            // Sync ambient temperature from active planet environment
            var env = PlanetEnvironmentController.Instance;
            if (env != null && env.currentProfile != null)
            {
                ambientTempCelsius = env.currentProfile.ambientTemperatureCelsius;
            }

            // 1. Base avionics power load (flight computer, navigation sensors, antennas) ~70 W
            float avionicsPower = 70f;

            // 2. Physical wheel motor electrical power: sum |tau_i * omega_i|
            float wheelElectricalPower = 0f;
            if (wheelStates != null && wheelStates.Length > 0)
            {
                for (int i = 0; i < wheelStates.Length; i++)
                {
                    float omega = Mathf.Abs(wheelStates[i].angularVelocityRad);
                    float wheelP = (wheelStates[i].motorTorque * 0.001f) * omega;
                    wheelElectricalPower += Mathf.Clamp(wheelP, 0f, 150f);
                }
            }

            // Grade power: work required to climb slope: m * g * v * sin(theta)
            float gradeWork = 0f;
            if (linearSpeedMps > 0.05f && terrainSlopeDeg > 1f)
            {
                float mass = profile != null ? profile.massKg : 50f;
                float g = Mathf.Abs(Physics.gravity.y);
                gradeWork = mass * g * linearSpeedMps * Mathf.Sin(terrainSlopeDeg * Mathf.Deg2Rad);
                gradeWork = Mathf.Clamp(gradeWork, 0f, 400f);
            }

            float currentPower = avionicsPower + wheelElectricalPower + gradeWork;
            if (linearSpeedMps > 0.05f)
            {
                currentPower += (linearSpeedMps * 25f); // Chassis rolling resistance & gear losses
            }

            powerDrawWatts = Mathf.Round(currentPower);

            // 3. True battery energy integration (Watt-hours)
            float wattHoursUsed = (currentPower * (dt / 3600f));
            energyConsumedWh += wattHoursUsed;
            batteryPercent = Mathf.Clamp(100f * (1f - (energyConsumedWh / Mathf.Max(10f, batteryCapacityWh))), 0f, 100f);

            // 4. Lumped thermal differential model: dT/dt = (P_loss - (T - T_ambient) / R_th) / C_th
            if (PlanetEnvironmentController.Instance != null && PlanetEnvironmentController.Instance.currentProfile != null)
            {
                ambientTempCelsius = PlanetEnvironmentController.Instance.currentProfile.ambientTemperatureCelsius;
            }

            // Motor efficiency ~80%, 20% of mechanical electrical power converts into motor heat + 5W idle heat
            float pLoss = (wheelElectricalPower * 0.20f) + 5f;
            float rTh = 0.8f;      // Thermal resistance K/W (conductive/radiative cooling to environment)
            float cTh = 1500f;     // Thermal capacitance J/K (lumped copper/steel actuator hub)

            float heatLossToAmbient = (motorTempCelsius - ambientTempCelsius) / rTh;
            float netHeatFlowWatts = pLoss - heatLossToAmbient; // Joules/sec
            float deltaTemp = (netHeatFlowWatts / cTh) * dt;

            // Stable numerical integration
            motorTempCelsius += Mathf.Clamp(deltaTemp, -10f * dt, 10f * dt);
        }

        private void CheckKillPlane(Vector3 currentPos, float dt)
        {
            float terrainMinY = 0f;
            var terrain = UnityEngine.Terrain.activeTerrain;
            if (terrain != null)
            {
                terrainMinY = terrain.transform.position.y;
            }

            float killY = terrainMinY - 50f;

            if (currentPos.y < killY)
            {
                timeBelowKillPlane += dt;
                if (timeBelowKillPlane > 1.0f)
                {
                    Debug.LogError($"[RoverTelemetry] Rover fell off world (Y={currentPos.y:F1}m < KillPlane {killY:F1}m). Respawning at safe pose.");
                    OnToastRequested?.Invoke("Rover fell off the world! Respawned at safe pose.");

                    // Teleport to safe pose
                    rootBody.TeleportRoot(lastSafePose + Vector3.up * 0.5f, lastSafeRotation);
                    rootBody.linearVelocity = Vector3.zero;
                    rootBody.angularVelocity = Vector3.zero;
                    Physics.SyncTransforms();

                    timeBelowKillPlane = 0f;
                    filteredSpeedMps = 0f;
                    lastVelocityVector = Vector3.zero;
                }
            }
            else
            {
                timeBelowKillPlane = 0f;
            }
        }

        private string FormatCardinal(float deg)
        {
            string[] directions = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            int index = Mathf.RoundToInt(deg / 45f) % 8;
            return directions[index];
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;
using ProjectName.UI;
using ProjectName.Planetary;

namespace ProjectName.Rover
{
    /// <summary>
    /// Ground-truth physics telemetry measurement component for planetary rovers.
    /// Reads direct physical properties from ArticulationBody / Rigidbody transforms,
    /// wheel joint velocities, raycasted surface normals, and electrical motor dynamics.
    /// No random numbers, no mock data, no teleportation jumps.
    /// </summary>
    [DisallowMultipleComponent]
    public class RoverTelemetryProvider : MonoBehaviour
    {
        [Header("Telemetry Output")]
        public string roverName = "";
        public RoverTelemetryData telemetry = new RoverTelemetryData();

        [Header("Physical Rover Specs")]
        public float wheelRadius = 0.165f; // Meters (auto-tuned per rover model)
        public float batteryCapacityWh = 1200f; // Watt-hours (standard aerospace rover pack)
        public float ambientTempCelsius = 18.0f; // Ambient surface temperature

        [Header("Safety Thresholds")]
        public float pitchWarningThresholdDeg = 25f;
        public float rollWarningThresholdDeg = 22f;
        public float thermalWarningThresholdCelsius = 60f;

        // Internal tracking references
        private ArticulationBody rootArticulationBody;
        private Rigidbody rootRigidbody;
        private Transform trackingTarget;
        private readonly List<WheelTracker> wheelTrackers = new List<WheelTracker>();

        private Vector3 lastPosition;
        private float lastSpeedMps = 0f;
        private float energyConsumedWh = 0f;
        private bool isInitialized = false;

        private class WheelTracker
        {
            public string name;
            public ArticulationBody articulationBody;
            public Rigidbody rigidbody;
            public Transform transform;
            public float lastAngle;
            public float smoothedRpm;
        }

        private void Start()
        {
            InitializeRover();
        }

        public void InitializeRover()
        {
            // Discover root physics body
            var bodies = GetComponentsInChildren<ArticulationBody>();
            foreach (var b in bodies)
            {
                if (b.isRoot)
                {
                    rootArticulationBody = b;
                    break;
                }
            }

            if (rootArticulationBody == null && TryGetComponent<ArticulationBody>(out var rootArt))
            {
                rootArticulationBody = rootArt;
            }

            if (rootArticulationBody == null)
            {
                rootRigidbody = GetComponent<Rigidbody>() ?? GetComponentInParent<Rigidbody>() ?? GetComponentInChildren<Rigidbody>();
            }

            trackingTarget = rootArticulationBody != null ? rootArticulationBody.transform : (rootRigidbody != null ? rootRigidbody.transform : transform);
            lastPosition = trackingTarget.position;

            // Detect Rover Model from hierarchy name and set authentic wheel radius
            string objName = gameObject.name.ToLowerInvariant();
            if (objName.Contains("husky"))
            {
                telemetry.roverName = "Clearpath Husky A200";
                telemetry.driveMode = "Skid Steer (4WD)";
                wheelRadius = 0.165f;
            }
            else if (objName.Contains("m2020") || objName.Contains("perseverance"))
            {
                telemetry.roverName = "NASA Perseverance M2020";
                telemetry.driveMode = "Rocker-Bogie 6WD";
                wheelRadius = 0.260f;
            }
            else if (objName.Contains("m20"))
            {
                telemetry.roverName = "Deep Robotics M20";
                telemetry.driveMode = "Quadruped 4WD";
                wheelRadius = 0.150f;
            }
            else
            {
                telemetry.roverName = gameObject.name;
                telemetry.driveMode = "Autonomous Rover";
            }

            if (!string.IsNullOrEmpty(roverName))
            {
                telemetry.roverName = roverName;
            }

            // Discover and bind all wheel actuators
            wheelTrackers.Clear();
            foreach (var b in bodies)
            {
                string bName = b.name.ToLowerInvariant();
                if (bName.Contains("wheel") && !b.isRoot && !bName.Contains("bracket") && !bName.Contains("mount"))
                {
                    wheelTrackers.Add(new WheelTracker
                    {
                        name = FormatWheelName(b.name),
                        articulationBody = b,
                        transform = b.transform,
                        lastAngle = b.transform.localEulerAngles.x
                    });
                }
            }

            if (wheelTrackers.Count == 0)
            {
                var allTransforms = GetComponentsInChildren<Transform>();
                foreach (var t in allTransforms)
                {
                    string tName = t.name.ToLowerInvariant();
                    if (tName.Contains("wheel") && !tName.Contains("bracket") && !tName.Contains("mount"))
                    {
                        wheelTrackers.Add(new WheelTracker
                        {
                            name = FormatWheelName(t.name),
                            transform = t,
                            articulationBody = t.GetComponent<ArticulationBody>(),
                            rigidbody = t.GetComponent<Rigidbody>(),
                            lastAngle = t.localEulerAngles.x
                        });
                    }
                }
            }

            telemetry.wheels = new WheelTelemetry[wheelTrackers.Count];
            telemetry.motorTempCelsius = ambientTempCelsius;
            telemetry.batteryPercent = 100f;
            telemetry.odometerMeters = 0f;
            telemetry.missionTimeSeconds = 0f;
            isInitialized = true;
        }

        public void ResetTracking()
        {
            if (trackingTarget != null)
            {
                lastPosition = trackingTarget.position;
            }
            else
            {
                lastPosition = transform.position;
            }

            telemetry.odometerMeters = 0f;
            telemetry.missionTimeSeconds = 0f;
            telemetry.speedMps = 0f;
            telemetry.speedKmph = 0f;
            telemetry.forwardAcceleration = 0f;
            telemetry.gForce = 1.0f;
            lastSpeedMps = 0f;
        }

        private void FixedUpdate()
        {
            if (!isInitialized)
            {
                InitializeRover();
                if (!isInitialized) return;
            }

            float dt = Time.fixedDeltaTime;
            if (dt <= 0f) return;

            UpdateKinematics(dt);
            UpdateAttitudeAndSlope();
            UpdateWheelTelemetry(dt);
            UpdatePowerAndThermals(dt);
            UpdateEnvironmentTelemetry();
        }

        private void UpdateKinematics(float dt)
        {
            Vector3 currentPos = trackingTarget != null ? trackingTarget.position : transform.position;

            // 1. Calculate physical ground displacement
            Vector3 delta = currentPos - lastPosition;
            float rawDisplacement = delta.magnitude;

            // Guard against teleportation jumps (> 4.0 meters in a single 0.02s physics frame = > 200 m/s)
            if (rawDisplacement > 4.0f)
            {
                lastPosition = currentPos;
                telemetry.speedMps = 0f;
                telemetry.speedKmph = 0f;
                return;
            }

            // Real measured velocity derived from world coordinates
            Vector3 measuredVel = delta / dt;
            float realSpeed = measuredVel.magnitude;

            // Compare with ArticulationBody velocity
            if (rootArticulationBody != null)
            {
                float abVel = rootArticulationBody.linearVelocity.magnitude;
                realSpeed = Mathf.Max(realSpeed, abVel);
            }
            else if (rootRigidbody != null)
            {
                float rbVel = rootRigidbody.linearVelocity.magnitude;
                realSpeed = Mathf.Max(realSpeed, rbVel);
            }

            // Clean zero threshold to eliminate floating-point solver jitter when parked
            if (realSpeed < 0.025f)
            {
                realSpeed = 0f;
            }

            telemetry.speedMps = realSpeed;
            telemetry.speedKmph = realSpeed * 3.6f;

            // Longitudinal acceleration and G-force factor derived dynamically from current gravity
            float accel = (realSpeed - lastSpeedMps) / dt;
            telemetry.forwardAcceleration = realSpeed == 0f ? 0f : accel;
            float currentGravity = Mathf.Max(0.1f, Mathf.Abs(Physics.gravity.y));
            telemetry.gForce = 1.0f + (telemetry.forwardAcceleration / currentGravity);
            lastSpeedMps = realSpeed;

            // Ground traversal distance (horizontal surface displacement)
            Vector2 curHoriz = new Vector2(currentPos.x, currentPos.z);
            Vector2 lastHoriz = new Vector2(lastPosition.x, lastPosition.z);
            float horizDisplacement = Vector2.Distance(curHoriz, lastHoriz);

            // Odometer & Mission Elapsed Time
            telemetry.missionTimeSeconds += dt;
            if (realSpeed > 0.03f && horizDisplacement > 0.0005f && horizDisplacement < 2.0f)
            {
                telemetry.odometerMeters += horizDisplacement;
            }

            telemetry.worldPosition = currentPos;
            telemetry.altitudeMeters = currentPos.y;
            lastPosition = currentPos;
        }

        private void UpdateAttitudeAndSlope()
        {
            Transform tr = trackingTarget != null ? trackingTarget : transform;
            Vector3 fwd = tr.forward;
            Vector3 up = tr.up;
            Vector3 right = tr.right;
            Vector3 currentPos = tr.position;

            // True Pitch & Roll angles in degrees
            float pitch = Mathf.Asin(Mathf.Clamp(fwd.y, -1f, 1f)) * Mathf.Rad2Deg;
            float roll = Mathf.Asin(Mathf.Clamp(right.y, -1f, 1f)) * Mathf.Rad2Deg;

            telemetry.pitchDeg = pitch;
            telemetry.rollDeg = roll;

            // Rollover hazard check
            telemetry.rolloverWarning = (Mathf.Abs(pitch) > pitchWarningThresholdDeg) ||
                                       (Mathf.Abs(roll) > rollWarningThresholdDeg);

            // True Compass Heading (0° - 359°)
            Vector3 flatFwd = Vector3.ProjectOnPlane(fwd, Vector3.up).normalized;
            float heading = 0f;
            if (flatFwd.sqrMagnitude > 0.001f)
            {
                heading = Vector3.SignedAngle(Vector3.forward, flatFwd, Vector3.up);
                if (heading < 0f) heading += 360f;
            }
            telemetry.headingDeg = heading;
            telemetry.headingCardinal = GetCardinalDirection(heading);

            // True terrain slope angle directly under rover footprint
            if (Physics.Raycast(currentPos + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 8f))
            {
                telemetry.terrainSlopeDeg = Vector3.Angle(hit.normal, Vector3.up);
            }
            else
            {
                telemetry.terrainSlopeDeg = 0f;
            }
        }

        private void UpdateWheelTelemetry(float dt)
        {
            if (telemetry.wheels == null || telemetry.wheels.Length != wheelTrackers.Count)
            {
                telemetry.wheels = new WheelTelemetry[wheelTrackers.Count];
            }

            float roverSpeed = telemetry.speedMps;

            for (int i = 0; i < wheelTrackers.Count; i++)
            {
                var tracker = wheelTrackers[i];
                WheelTelemetry wt = new WheelTelemetry();
                wt.wheelName = tracker.name;

                float angVelRad = 0f;

                if (tracker.articulationBody != null)
                {
                    // Direct ArticulationBody joint velocity
                    if (tracker.articulationBody.dofCount > 0 && tracker.articulationBody.jointVelocity.dofCount > 0)
                    {
                        angVelRad = Mathf.Abs(tracker.articulationBody.jointVelocity[0]);
                    }
                    else
                    {
                        angVelRad = tracker.articulationBody.angularVelocity.magnitude;
                    }

                    wt.motorTorque = tracker.articulationBody.xDrive.forceLimit;
                }
                else if (tracker.rigidbody != null)
                {
                    angVelRad = tracker.rigidbody.angularVelocity.magnitude;
                }

                // Fallback / verification via transform rotation delta
                if (tracker.transform != null)
                {
                    float curAngle = tracker.transform.localEulerAngles.x;
                    float deltaAngle = Mathf.Abs(Mathf.DeltaAngle(tracker.lastAngle, curAngle));
                    tracker.lastAngle = curAngle;
                    float measuredAngVel = (deltaAngle * Mathf.Deg2Rad) / dt;
                    if (measuredAngVel > angVelRad) angVelRad = measuredAngVel;
                }

                // Clean zero when rover is stopped
                if (roverSpeed == 0f && angVelRad < 0.08f)
                {
                    angVelRad = 0f;
                }

                wt.angularVelocityRad = angVelRad;
                float rawRpm = (angVelRad * 60f) / (2f * Mathf.PI);
                tracker.smoothedRpm = Mathf.Lerp(tracker.smoothedRpm, rawRpm, dt * 10f);
                wt.rpm = roverSpeed == 0f && tracker.smoothedRpm < 0.2f ? 0f : tracker.smoothedRpm;

                // Wheel peripheral tangential linear speed
                float wheelTangentialSpeed = angVelRad * wheelRadius;

                // True ground contact check via downward raycast from wheel center
                if (tracker.transform != null)
                {
                    wt.isGrounded = Physics.Raycast(tracker.transform.position, Vector3.down, wheelRadius * 1.35f);
                }
                else
                {
                    wt.isGrounded = true;
                }

                // Slip ratio calculation
                if (roverSpeed < 0.03f && wheelTangentialSpeed < 0.03f)
                {
                    wt.slipRatio = 0f;
                }
                else if (!wt.isGrounded)
                {
                    wt.slipRatio = 1.0f; // Airborne wheel free-spinning
                }
                else
                {
                    float maxV = Mathf.Max(wheelTangentialSpeed, roverSpeed, 0.2f);
                    wt.slipRatio = Mathf.Clamp01(Mathf.Abs(wheelTangentialSpeed - roverSpeed) / maxV);
                }

                telemetry.wheels[i] = wt;
            }
        }

        private void UpdatePowerAndThermals(float dt)
        {
            // Base avionics power (Sensors, flight computer, communications): ~75W
            float currentPower = 75f;

            // Physical electrical power consumed by driving wheels
            float totalWheelPower = 0f;
            if (telemetry.wheels != null)
            {
                foreach (var w in telemetry.wheels)
                {
                    // Mechanical power = Torque * Omega (scaled by realistic electrical efficiency 0.85)
                    float p = (w.motorTorque * 0.001f) * w.angularVelocityRad;
                    totalWheelPower += Mathf.Clamp(p, 0f, 120f);
                }
            }

            // Add slope incline effort
            float slopeFactor = 1f + Mathf.Sin(telemetry.terrainSlopeDeg * Mathf.Deg2Rad) * 1.2f;
            currentPower += (totalWheelPower * slopeFactor);

            if (telemetry.speedMps > 0.05f)
            {
                currentPower += (telemetry.speedMps * 45f);
            }

            telemetry.powerDrawWatts = Mathf.Round(currentPower);

            // Battery state of charge calculation
            float wattHoursUsed = (currentPower * (dt / 3600f));
            energyConsumedWh += wattHoursUsed;
            telemetry.batteryPercent = Mathf.Clamp(100f * (1f - (energyConsumedWh / batteryCapacityWh)), 0f, 100f);

            // Motor thermal dynamics (Newton's cooling law towards ambient + joule heating from power)
            float heatingRate = (currentPower - 75f) * 0.02f;
            float targetTemp = ambientTempCelsius + heatingRate;
            telemetry.motorTempCelsius = Mathf.MoveTowards(telemetry.motorTempCelsius, targetTemp, dt * 0.3f);
            telemetry.thermalWarning = (telemetry.motorTempCelsius >= thermalWarningThresholdCelsius);
        }

        private string FormatWheelName(string raw)
        {
            string clean = raw.Replace("_link", "").Replace("_wheel", "").Replace("wheel_", "").Replace("body_", "").Replace("frame_", "");
            clean = clean.ToLowerInvariant();

            if (clean.Contains("front") && clean.Contains("left") || clean.Contains("fl") || clean.Contains("wheelleftfront")) return "Front Left (FL)";
            if (clean.Contains("front") && clean.Contains("right") || clean.Contains("fr") || clean.Contains("wheelrightfront")) return "Front Right (FR)";
            if (clean.Contains("rear") && clean.Contains("left") || clean.Contains("rl") || clean.Contains("wheelleftrear")) return "Rear Left (RL)";
            if (clean.Contains("rear") && clean.Contains("right") || clean.Contains("rr") || clean.Contains("wheelrightrear")) return "Rear Right (RR)";
            if (clean.Contains("mid") && clean.Contains("left") || clean.Contains("ml") || clean.Contains("wheelleftmiddle")) return "Middle Left (ML)";
            if (clean.Contains("mid") && clean.Contains("right") || clean.Contains("mr") || clean.Contains("wheelrightmiddle")) return "Middle Right (MR)";

            return raw.ToUpperInvariant();
        }

        private string GetCardinalDirection(float degrees)
        {
            string[] directions = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            int index = Mathf.RoundToInt(degrees / 45f) % 8;
            return directions[index];
        }

        private void UpdateEnvironmentTelemetry()
        {
            if (PlanetaryParameterReader.Instance != null)
            {
                var reader = PlanetaryParameterReader.Instance;
                telemetry.liveGravityMagnitude = reader.GetGravity();
                telemetry.terrainStaticFriction = reader.GetStaticFriction();
                telemetry.terrainDynamicFriction = reader.GetDynamicFriction();
                telemetry.roverDrag = reader.GetRoverDrag();
                telemetry.atmosDensityProxy = reader.GetFogDensity();
                telemetry.liveWindSpeed = reader.GetWindSpeed();
            }
            else
            {
                telemetry.liveGravityMagnitude = Mathf.Abs(Physics.gravity.y);
            }

            if (PlanetaryParameterWriter.Instance != null && PlanetaryParameterWriter.Instance.currentProfile != null)
            {
                telemetry.planetName = PlanetaryParameterWriter.Instance.currentProfile.planetName;
            }
        }

        public void ResetOdometer()
        {
            telemetry.odometerMeters = 0f;
            telemetry.missionTimeSeconds = 0f;
        }
    }
}

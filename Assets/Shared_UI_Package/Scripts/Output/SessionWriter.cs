using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.SceneManagement;
using ProjectName.Rover;
using ProjectName.Planetary;

namespace ProjectName.Output
{
    public class SessionWriter : IDisposable
    {
        private readonly CultureInfo inv = CultureInfo.InvariantCulture;
        private readonly StringBuilder sb = new StringBuilder(1024);

        private string sessionDirectory;
        private string sessionId;
        private string telemetryPath;
        private string wheelsPath;
        private string jsonPath;
        private string reportPath;

        private StreamWriter telemetryWriter;
        private StreamWriter wheelsWriter;
        private bool hasWheelsFile = false;

        private float lastFlushTime = 0f;
        private int totalSamplesWritten = 0;
        private int totalWheelRowsWritten = 0;
        private int nanOrInfSamplesCount = 0;
        private long bytesWritten = 0;

        public string SessionId => sessionId;
        public string SessionDirectory => sessionDirectory;
        public int TotalSamplesWritten => totalSamplesWritten;
        public int TotalWheelRowsWritten => totalWheelRowsWritten;
        public long BytesWritten => bytesWritten;
        public bool HasWheelsFile => hasWheelsFile;

        public static string GetOutputsRoot()
        {
#if UNITY_EDITOR
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string dir = Path.Combine(projectRoot, "Outputs");
#else
            string dir = Path.Combine(Application.persistentDataPath, "Outputs");
#endif
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            return dir;
        }

        public bool StartSession(
            string roverId,
            string planetName,
            int sampleRateHz,
            Vector3 initialPos,
            float initialHeading,
            bool includeWheelData,
            out string error)
        {
            error = null;
            try
            {
                string root = GetOutputsRoot();
                string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                string safeRover = Regex.Replace(roverId ?? "rover", @"[^A-Za-z0-9_-]", "_");
                string safePlanet = Regex.Replace(planetName ?? "planet", @"[^A-Za-z0-9_-]", "_");

                string baseName = $"{timestamp}_{safeRover}_{safePlanet}";
                sessionId = baseName;
                sessionDirectory = Path.Combine(root, sessionId);

                int counter = 2;
                while (Directory.Exists(sessionDirectory))
                {
                    sessionId = $"{baseName}_{counter}";
                    sessionDirectory = Path.Combine(root, sessionId);
                    counter++;
                }

                Directory.CreateDirectory(sessionDirectory);

                telemetryPath = Path.Combine(sessionDirectory, "telemetry.csv");
                wheelsPath = Path.Combine(sessionDirectory, "wheels.csv");
                jsonPath = Path.Combine(sessionDirectory, "session.json");
                reportPath = Path.Combine(sessionDirectory, "report.md");

                var utf8NoBom = new UTF8Encoding(false);
                telemetryWriter = new StreamWriter(telemetryPath, false, utf8NoBom, 65536);
                telemetryWriter.NewLine = "\n";
                WriteTelemetryHeader();

                hasWheelsFile = includeWheelData;
                if (hasWheelsFile)
                {
                    wheelsWriter = new StreamWriter(wheelsPath, false, utf8NoBom, 65536);
                    wheelsWriter.NewLine = "\n";
                    WriteWheelsHeader();
                }

                totalSamplesWritten = 0;
                totalWheelRowsWritten = 0;
                nanOrInfSamplesCount = 0;
                bytesWritten = 0;
                lastFlushTime = Time.unscaledTime;

                // Write initial placeholder session.json with status "recording"
                WriteInitialJson(roverId, planetName, sampleRateHz, initialPos, initialHeading);

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Debug.LogError($"[SessionWriter] Failed to start session: {ex}");
                CloseWriters();
                return false;
            }
        }

        private void WriteTelemetryHeader()
        {
            string header = "Timestamp_s,FrameIndex,MissionTime_s," +
                            "PosX_m,PosY_m,PosZ_m,Elevation_m,Odometer_m," +
                            "LinearSpeed_mps,GroundSpeed_kmph,ForwardAccel_mps2,GForce_G," +
                            "Pitch_deg,Roll_deg,Heading_deg,Slope_deg," +
                            "Battery_pct,Power_W,EnergyConsumed_Wh,MotorTemp_C,AmbientTemp_C," +
                            "MaxWheelSlip_pct,AvgWheelSlip_pct,GroundedWheelCount," +
                            "Gravity_mps2,RolloverWarn,SteepSlopeWarn,Planet,Rover";
            telemetryWriter.WriteLine(header);
            bytesWritten += header.Length + 1;
        }

        private void WriteWheelsHeader()
        {
            string header = "Timestamp_s,FrameIndex,Wheel,RPM,AngularVel_radps,TangentialSpeed_mps,SlipRatio_pct,MotorTorqueEst_Nm,Grounded,Status";
            wheelsWriter.WriteLine(header);
            bytesWritten += header.Length + 1;
        }

        public void WriteTelemetryRow(
            float simTime,
            int frameIndex,
            float missionTime,
            Vector3 pos,
            float elevation,
            float odo,
            float speed,
            float groundSpeedKmph,
            float accel,
            float gForce,
            float pitch,
            float roll,
            float heading,
            float slope,
            float batt,
            float power,
            float energy,
            float motorTemp,
            float ambientTemp,
            float? maxSlip,
            float? avgSlip,
            int groundedWheelCount,
            float gravity,
            bool rolloverWarn,
            bool steepSlopeWarn,
            string planet,
            string rover)
        {
            if (telemetryWriter == null) return;

            sb.Clear();
            AppendF3(simTime); sb.Append(',');
            sb.Append(frameIndex); sb.Append(',');
            AppendF3(missionTime); sb.Append(',');
            AppendF3(pos.x); sb.Append(',');
            AppendF3(pos.y); sb.Append(',');
            AppendF3(pos.z); sb.Append(',');
            AppendF3(elevation); sb.Append(',');
            AppendF3(odo); sb.Append(',');
            AppendF3(speed); sb.Append(',');
            AppendF3(groundSpeedKmph); sb.Append(',');
            AppendF3(accel); sb.Append(',');
            AppendF3(gForce); sb.Append(',');
            AppendF3(pitch); sb.Append(',');
            AppendF3(roll); sb.Append(',');
            AppendF3(heading); sb.Append(',');
            AppendF3(slope); sb.Append(',');
            AppendF2(batt); sb.Append(',');
            AppendF1(power); sb.Append(',');
            AppendF2(energy); sb.Append(',');
            AppendF2(motorTemp); sb.Append(',');
            AppendF2(ambientTemp); sb.Append(',');

            if (maxSlip.HasValue) AppendF2(maxSlip.Value);
            sb.Append(',');

            if (avgSlip.HasValue) AppendF2(avgSlip.Value);
            sb.Append(',');

            sb.Append(groundedWheelCount); sb.Append(',');
            AppendF3(gravity); sb.Append(',');
            sb.Append(rolloverWarn ? "1" : "0"); sb.Append(',');
            sb.Append(steepSlopeWarn ? "1" : "0"); sb.Append(',');
            sb.Append(planet ?? ""); sb.Append(',');
            sb.Append(rover ?? "");

            string line = sb.ToString();
            telemetryWriter.WriteLine(line);
            bytesWritten += line.Length + 1;
            totalSamplesWritten++;

            PeriodicFlush();
        }

        public void WriteWheelRow(
            float simTime,
            int frameIndex,
            string wheelName,
            float rpm,
            float angularVel,
            float tangentialSpeed,
            float slipRatioPct,
            float torque,
            bool isGrounded,
            string status)
        {
            if (wheelsWriter == null) return;

            sb.Clear();
            AppendF3(simTime); sb.Append(',');
            sb.Append(frameIndex); sb.Append(',');
            sb.Append(wheelName ?? ""); sb.Append(',');
            AppendF2(rpm); sb.Append(',');
            AppendF3(angularVel); sb.Append(',');
            AppendF3(tangentialSpeed); sb.Append(',');
            AppendF2(slipRatioPct); sb.Append(',');
            AppendF2(torque); sb.Append(',');
            sb.Append(isGrounded ? "True" : "False"); sb.Append(',');
            sb.Append(status ?? "");

            string line = sb.ToString();
            wheelsWriter.WriteLine(line);
            bytesWritten += line.Length + 1;
            totalWheelRowsWritten++;
        }

        private void AppendF3(float val)
        {
            if (float.IsNaN(val) || float.IsInfinity(val))
            {
                nanOrInfSamplesCount++;
                return;
            }
            sb.Append(val.ToString("F3", inv));
        }

        private void AppendF2(float val)
        {
            if (float.IsNaN(val) || float.IsInfinity(val))
            {
                nanOrInfSamplesCount++;
                return;
            }
            sb.Append(val.ToString("F2", inv));
        }

        private void AppendF1(float val)
        {
            if (float.IsNaN(val) || float.IsInfinity(val))
            {
                nanOrInfSamplesCount++;
                return;
            }
            sb.Append(val.ToString("F1", inv));
        }

        private void PeriodicFlush()
        {
            if (Time.unscaledTime - lastFlushTime >= 2.0f)
            {
                Flush();
            }
        }

        public void Flush()
        {
            try
            {
                telemetryWriter?.Flush();
                wheelsWriter?.Flush();
                lastFlushTime = Time.unscaledTime;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SessionWriter] Flush warning: {ex.Message}");
            }
        }

        public void FinalizeSession(
            string status,
            string startedUtc,
            string endedUtc,
            float durationSimS,
            List<float[]> pausedSpans,
            string autoStoppedReason,
            RoverProfile roverProfile,
            PlanetProfile planetProfile,
            Vector3 terrainSize,
            string terrainPreset,
            string heightmapFile,
            Vector3 initialPos,
            float initialHeading,
            int sampleRateHz,
            bool wheelDataAvailable,
            List<string> unavailableColumns,
            SessionSummary summary,
            List<SessionEvent> events)
        {
            try
            {
                CloseWriters();

                string jsonContent = BuildSessionJson(
                    status,
                    startedUtc,
                    endedUtc,
                    durationSimS,
                    pausedSpans,
                    autoStoppedReason,
                    roverProfile,
                    planetProfile,
                    terrainSize,
                    terrainPreset,
                    heightmapFile,
                    initialPos,
                    initialHeading,
                    sampleRateHz,
                    wheelDataAvailable,
                    unavailableColumns,
                    summary,
                    events);

                File.WriteAllText(jsonPath, jsonContent, new UTF8Encoding(false));

                string reportMd = SessionStats.GenerateReportMarkdown(
                    sessionId,
                    roverProfile?.displayName ?? roverProfile?.id ?? "Rover",
                    planetProfile?.displayName ?? planetProfile?.planetName ?? "Planet",
                    Mathf.Abs(Physics.gravity.y),
                    terrainPreset ?? (heightmapFile != null ? Path.GetFileName(heightmapFile) : "Procedural Terrain"),
                    sampleRateHz,
                    totalSamplesWritten,
                    startedUtc,
                    summary,
                    events,
                    unavailableColumns,
                    pausedSpans,
                    hasWheelsFile);

                File.WriteAllText(reportPath, reportMd, new UTF8Encoding(false));

                UpdateTotalBytes();
                Debug.Log($"[SessionWriter] Session {sessionId} finalized with status '{status}'. Total samples: {totalSamplesWritten}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SessionWriter] Error finalizing session {sessionId}: {ex}");
            }
        }

        private void UpdateTotalBytes()
        {
            try
            {
                long size = 0;
                if (File.Exists(telemetryPath)) size += new FileInfo(telemetryPath).Length;
                if (File.Exists(wheelsPath)) size += new FileInfo(wheelsPath).Length;
                if (File.Exists(jsonPath)) size += new FileInfo(jsonPath).Length;
                if (File.Exists(reportPath)) size += new FileInfo(reportPath).Length;
                bytesWritten = size;
            }
            catch { }
        }

        private void WriteInitialJson(
            string roverId,
            string planetName,
            int sampleRateHz,
            Vector3 initialPos,
            float initialHeading)
        {
            var initialJson = new StringBuilder();
            initialJson.AppendLine("{");
            initialJson.AppendLine("  \"schema_version\": \"1.0\",");
            initialJson.AppendLine($"  \"session_id\": \"{EscapeJson(sessionId)}\",");
            initialJson.AppendLine("  \"status\": \"recording\",");
            initialJson.AppendLine("  \"app\": {");
            initialJson.AppendLine($"    \"unity_version\": \"{EscapeJson(Application.unityVersion)}\",");
            initialJson.AppendLine($"    \"platform\": \"{EscapeJson(Application.platform.ToString())}\",");
            initialJson.AppendLine($"    \"scene\": \"{EscapeJson(SceneManager.GetActiveScene().name)}\",");
            initialJson.AppendLine($"    \"fixed_dt_s\": {Time.fixedDeltaTime.ToString("F4", inv)}");
            initialJson.AppendLine("  },");
            initialJson.AppendLine("  \"rover\": {");
            initialJson.AppendLine($"    \"id\": \"{EscapeJson(roverId)}\"");
            initialJson.AppendLine("  },");
            initialJson.AppendLine("  \"environment\": {");
            initialJson.AppendLine($"    \"planet\": \"{EscapeJson(planetName)}\"");
            initialJson.AppendLine("  },");
            initialJson.AppendLine("  \"recording\": {");
            initialJson.AppendLine($"    \"sample_rate_hz\": {sampleRateHz},");
            initialJson.AppendLine($"    \"started_utc\": \"{DateTime.UtcNow:o}\"");
            initialJson.AppendLine("  }");
            initialJson.AppendLine("}");

            File.WriteAllText(jsonPath, initialJson.ToString(), new UTF8Encoding(false));
        }

        private string BuildSessionJson(
            string status,
            string startedUtc,
            string endedUtc,
            float durationSimS,
            List<float[]> pausedSpans,
            string autoStoppedReason,
            RoverProfile rover,
            PlanetProfile env,
            Vector3 terrainSize,
            string terrainPreset,
            string heightmapFile,
            Vector3 initialPos,
            float initialHeading,
            int sampleRateHz,
            bool wheelDataAvailable,
            List<string> unavailableColumns,
            SessionSummary summary,
            List<SessionEvent> events)
        {
            var js = new StringBuilder(4096);
            js.AppendLine("{");
            js.AppendLine("  \"schema_version\": \"1.0\",");
            js.AppendLine($"  \"session_id\": \"{EscapeJson(sessionId)}\",");
            js.AppendLine($"  \"status\": \"{EscapeJson(status)}\",");

            // App
            js.AppendLine("  \"app\": {");
            js.AppendLine($"    \"unity_version\": \"{EscapeJson(Application.unityVersion)}\",");
            js.AppendLine($"    \"platform\": \"{EscapeJson(Application.platform.ToString())}\",");
            js.AppendLine($"    \"scene\": \"{EscapeJson(SceneManager.GetActiveScene().name)}\",");
            js.AppendLine($"    \"fixed_dt_s\": {Time.fixedDeltaTime.ToString("F4", inv)}");
            js.AppendLine("  },");

            // Rover
            js.AppendLine("  \"rover\": {");
            js.AppendLine($"    \"id\": \"{EscapeJson(rover?.id ?? "unknown")}\",");
            js.AppendLine($"    \"display_name\": \"{EscapeJson(rover?.displayName ?? "Rover")}\",");
            js.AppendLine($"    \"mass_kg\": {(rover != null ? rover.massKg.ToString("F1", inv) : "0")},");
            js.AppendLine($"    \"wheel_radius_m\": {(rover != null ? rover.wheelRadius.ToString("F3", inv) : "0")},");
            js.AppendLine($"    \"battery_capacity_wh\": {(rover != null ? rover.batteryWh.ToString("F1", inv) : "0")},");
            js.AppendLine($"    \"max_speed_mps\": {(rover != null ? rover.maxSpeed.ToString("F2", inv) : "0")}");
            js.AppendLine("  },");

            // Environment
            js.AppendLine("  \"environment\": {");
            js.AppendLine($"    \"planet\": \"{EscapeJson(env?.planetName ?? "Mars")}\",");
            js.AppendLine($"    \"gravity_mps2\": {Mathf.Abs(Physics.gravity.y).ToString("F3", inv)},");
            js.AppendLine($"    \"ambient_temp_c\": {(env != null ? env.ambientTemperatureCelsius.ToString("F1", inv) : "0")},");
            js.AppendLine($"    \"soil_static_friction\": {(env != null ? env.soilStaticFriction.ToString("F2", inv) : "0")},");
            js.AppendLine($"    \"soil_dynamic_friction\": {(env != null ? env.soilDynamicFriction.ToString("F2", inv) : "0")},");
            js.AppendLine($"    \"drag_multiplier\": {(env != null ? env.roverDragMultiplier.ToString("F2", inv) : "0")},");
            js.AppendLine($"    \"wind_main\": {(env != null ? env.windMain.ToString("F2", inv) : "0")},");
            js.AppendLine($"    \"pressure_kpa\": {(env != null ? env.pressureKPa.ToString("F2", inv) : "0")}");
            js.AppendLine("  },");

            // Terrain
            js.AppendLine("  \"terrain\": {");
            js.AppendLine($"    \"size_m\": [{terrainSize.x.ToString("F1", inv)}, {terrainSize.y.ToString("F1", inv)}, {terrainSize.z.ToString("F1", inv)}],");
            js.AppendLine($"    \"preset\": {(terrainPreset != null ? $"\"{EscapeJson(terrainPreset)}\"" : "null")},");
            js.AppendLine($"    \"heightmap_file\": {(heightmapFile != null ? $"\"{EscapeJson(heightmapFile)}\"" : "null")}");
            js.AppendLine("  },");

            // Initial State
            js.AppendLine("  \"initial_state\": {");
            js.AppendLine($"    \"pos_m\": [{initialPos.x.ToString("F3", inv)}, {initialPos.y.ToString("F3", inv)}, {initialPos.z.ToString("F3", inv)}],");
            js.AppendLine($"    \"heading_deg\": {initialHeading.ToString("F2", inv)}");
            js.AppendLine("  },");

            // Recording
            js.AppendLine("  \"recording\": {");
            js.AppendLine($"    \"sample_rate_hz\": {sampleRateHz},");
            js.AppendLine($"    \"started_utc\": \"{EscapeJson(startedUtc)}\",");
            js.AppendLine($"    \"ended_utc\": \"{EscapeJson(endedUtc)}\",");
            js.AppendLine($"    \"duration_sim_s\": {durationSimS.ToString("F3", inv)},");
            js.AppendLine($"    \"samples\": {totalSamplesWritten},");
            js.AppendLine($"    \"wheel_rows\": {totalWheelRowsWritten},");

            js.Append("    \"paused_spans\": [");
            if (pausedSpans != null && pausedSpans.Count > 0)
            {
                for (int i = 0; i < pausedSpans.Count; i++)
                {
                    js.Append($"[{pausedSpans[i][0].ToString("F3", inv)}, {pausedSpans[i][1].ToString("F3", inv)}]");
                    if (i < pausedSpans.Count - 1) js.Append(", ");
                }
            }
            js.AppendLine("],");

            js.AppendLine($"    \"auto_stopped_reason\": {(autoStoppedReason != null ? $"\"{EscapeJson(autoStoppedReason)}\"" : "null")}");
            js.AppendLine("  },");

            // Provenance
            js.AppendLine("  \"provenance\": {");
            js.AppendLine("    \"telemetry_source\": \"RoverTelemetry (FixedUpdate, physics-derived)\",");
            js.AppendLine("    \"validated\": false,");
            js.AppendLine("    \"note\": \"Logging working does not mean the model is validated. Channels marked 'modeled' are simulation estimates.\"");
            js.AppendLine("  },");

            // Columns Data Dictionary
            js.AppendLine("  \"columns\": {");
            js.AppendLine("    \"Timestamp_s\": { \"unit\": \"s\", \"kind\": \"derived\", \"desc\": \"Simulation time elapsed since recording start excluding paused spans\" },");
            js.AppendLine("    \"FrameIndex\": { \"unit\": \"\", \"kind\": \"derived\", \"desc\": \"Monotonically increasing sample sequence counter\" },");
            js.AppendLine("    \"MissionTime_s\": { \"unit\": \"s\", \"kind\": \"derived\", \"desc\": \"Rover onboard mission time counter\" },");
            js.AppendLine("    \"PosX_m\": { \"unit\": \"m\", \"kind\": \"measured\", \"desc\": \"World X position coordinate\" },");
            js.AppendLine("    \"PosY_m\": { \"unit\": \"m\", \"kind\": \"measured\", \"desc\": \"World Y position coordinate (vertical)\" },");
            js.AppendLine("    \"PosZ_m\": { \"unit\": \"m\", \"kind\": \"measured\", \"desc\": \"World Z position coordinate\" },");
            js.AppendLine("    \"Elevation_m\": { \"unit\": \"m\", \"kind\": \"measured\", \"desc\": \"Chassis elevation above origin (world Y)\" },");
            js.AppendLine("    \"Odometer_m\": { \"unit\": \"m\", \"kind\": \"derived\", \"desc\": \"Accumulated distance traveled from step displacement integration\" },");
            js.AppendLine("    \"LinearSpeed_mps\": { \"unit\": \"m/s\", \"kind\": \"derived\", \"desc\": \"Filtered position-delta speed\" },");
            js.AppendLine("    \"GroundSpeed_kmph\": { \"unit\": \"km/h\", \"kind\": \"derived\", \"desc\": \"Horizontal plane ground speed\" },");
            js.AppendLine("    \"ForwardAccel_mps2\": { \"unit\": \"m/s²\", \"kind\": \"derived\", \"desc\": \"Smoothed derivative of forward longitudinal speed\" },");
            js.AppendLine("    \"GForce_G\": { \"unit\": \"G\", \"kind\": \"derived\", \"desc\": \"Apparent IMU load vector magnitude normalized by standard gravity\" },");
            js.AppendLine("    \"Pitch_deg\": { \"unit\": \"deg\", \"kind\": \"measured\", \"desc\": \"Chassis pitch attitude angle (+ nose up, - nose down)\" },");
            js.AppendLine("    \"Roll_deg\": { \"unit\": \"deg\", \"kind\": \"measured\", \"desc\": \"Chassis roll attitude angle (+ right down, - left down)\" },");
            js.AppendLine("    \"Heading_deg\": { \"unit\": \"deg\", \"kind\": \"measured\", \"desc\": \"Chassis compass heading (0 = +Z North, 90 = +X East)\" },");
            js.AppendLine("    \"Slope_deg\": { \"unit\": \"deg\", \"kind\": \"measured\", \"desc\": \"Local terrain surface slope angle beneath chassis\" },");
            js.AppendLine("    \"Battery_pct\": { \"unit\": \"%\", \"kind\": \"modeled\", \"desc\": \"Simulated battery state of charge from integrated energy consumption\" },");
            js.AppendLine("    \"Power_W\": { \"unit\": \"W\", \"kind\": \"modeled\", \"desc\": \"Simulated total power draw (avionics base + actuators + grade work + rolling resistance)\" },");
            js.AppendLine("    \"EnergyConsumed_Wh\": { \"unit\": \"Wh\", \"kind\": \"modeled\", \"desc\": \"Numerical integration of power draw over mission duration\" },");
            js.AppendLine("    \"MotorTemp_C\": { \"unit\": \"°C\", \"kind\": \"modeled\", \"desc\": \"Lumped differential thermal actuator model with environmental dissipation\" },");
            js.AppendLine("    \"AmbientTemp_C\": { \"unit\": \"°C\", \"kind\": \"measured\", \"desc\": \"Atmospheric ambient temperature from planetary profile\" },");
            js.AppendLine("    \"MaxWheelSlip_pct\": { \"unit\": \"%\", \"kind\": \"derived\", \"desc\": \"Peak slip ratio among currently grounded wheels\" },");
            js.AppendLine("    \"AvgWheelSlip_pct\": { \"unit\": \"%\", \"kind\": \"derived\", \"desc\": \"Mean slip ratio across currently grounded wheels\" },");
            js.AppendLine("    \"GroundedWheelCount\": { \"unit\": \"\", \"kind\": \"measured\", \"desc\": \"Count of wheels in physical contact with terrain\" },");
            js.AppendLine("    \"Gravity_mps2\": { \"unit\": \"m/s²\", \"kind\": \"measured\", \"desc\": \"Magnitude of active physics engine gravity vector\" },");
            js.AppendLine("    \"RolloverWarn\": { \"unit\": \"\", \"kind\": \"derived\", \"desc\": \"Boolean threshold flag for excessive pitch or roll tilt\" },");
            js.AppendLine("    \"SteepSlopeWarn\": { \"unit\": \"\", \"kind\": \"derived\", \"desc\": \"Boolean threshold flag for slope exceeding safe rover capability\" },");
            js.AppendLine("    \"Planet\": { \"unit\": \"\", \"kind\": \"measured\", \"desc\": \"Active planetary profile identifier\" },");
            js.AppendLine("    \"Rover\": { \"unit\": \"\", \"kind\": \"measured\", \"desc\": \"Active rover profile identifier\" },");
            js.AppendLine("    \"Wheel\": { \"unit\": \"\", \"kind\": \"measured\", \"desc\": \"Wheel actuator identifier name (in wheels.csv)\" },");
            js.AppendLine("    \"RPM\": { \"unit\": \"rpm\", \"kind\": \"derived\", \"desc\": \"Wheel rotational velocity in revolutions per minute\" },");
            js.AppendLine("    \"AngularVel_radps\": { \"unit\": \"rad/s\", \"kind\": \"measured\", \"desc\": \"Wheel articulation joint angular velocity\" },");
            js.AppendLine("    \"TangentialSpeed_mps\": { \"unit\": \"m/s\", \"kind\": \"derived\", \"desc\": \"Linear wheel tread speed computed as angular velocity times wheel radius\" },");
            js.AppendLine("    \"SlipRatio_pct\": { \"unit\": \"%\", \"kind\": \"derived\", \"desc\": \"Kinematic slip ratio computed relative to vehicle longitudinal velocity\" },");
            js.AppendLine("    \"MotorTorqueEst_Nm\": { \"unit\": \"N·m\", \"kind\": \"estimate\", \"desc\": \"Joint drive torque capacity limit from physics actuator\" },");
            js.AppendLine("    \"Grounded\": { \"unit\": \"\", \"kind\": \"measured\", \"desc\": \"Contact boolean state via surface raycast\" },");
            js.AppendLine("    \"Status\": { \"unit\": \"\", \"kind\": \"derived\", \"desc\": \"Traction status classification (GOOD, FAIR, SLIP, AIR)\" }");
            js.AppendLine("  },");

            // Data Quality
            js.AppendLine("  \"data_quality\": {");
            js.AppendLine($"    \"wheel_data_available\": {(wheelDataAvailable ? "true" : "false")},");
            js.Append("    \"unavailable_columns\": [");
            if (unavailableColumns != null && unavailableColumns.Count > 0)
            {
                for (int i = 0; i < unavailableColumns.Count; i++)
                {
                    js.Append($"\"{EscapeJson(unavailableColumns[i])}\"");
                    if (i < unavailableColumns.Count - 1) js.Append(", ");
                }
            }
            js.AppendLine("],");
            js.AppendLine($"    \"nan_or_inf_samples\": {nanOrInfSamplesCount}");
            js.AppendLine("  },");

            // Summary
            js.AppendLine("  \"summary\": {");
            js.AppendLine($"    \"duration_s\": {summary.duration_s.ToString("F3", inv)},");
            js.AppendLine($"    \"distance_m\": {summary.distance_m.ToString("F3", inv)},");
            js.AppendLine($"    \"avg_speed_mps\": {summary.avg_speed_mps.ToString("F3", inv)},");
            js.AppendLine($"    \"max_speed_mps\": {summary.max_speed_mps.ToString("F3", inv)},");
            js.AppendLine($"    \"avg_abs_accel_mps2\": {summary.avg_abs_accel_mps2.ToString("F3", inv)},");
            js.AppendLine($"    \"max_g\": {summary.max_g.ToString("F3", inv)},");
            js.AppendLine($"    \"max_slope_deg\": {summary.max_slope_deg.ToString("F2", inv)},");
            js.AppendLine($"    \"max_abs_pitch_deg\": {summary.max_abs_pitch_deg.ToString("F2", inv)},");
            js.AppendLine($"    \"max_abs_roll_deg\": {summary.max_abs_roll_deg.ToString("F2", inv)},");
            js.AppendLine($"    \"avg_slip_pct\": {summary.avg_slip_pct.ToString("F2", inv)},");
            js.AppendLine($"    \"max_slip_pct\": {summary.max_slip_pct.ToString("F2", inv)},");
            js.AppendLine($"    \"pct_time_any_wheel_airborne\": {summary.pct_time_any_wheel_airborne.ToString("F2", inv)},");
            js.AppendLine($"    \"energy_wh\": {summary.energy_wh.ToString("F2", inv)},");
            string epStr = summary.energy_per_m_wh.HasValue ? summary.energy_per_m_wh.Value.ToString("F4", inv) : "null";
            js.AppendLine($"    \"energy_per_m_wh\": {epStr},");
            js.AppendLine($"    \"avg_power_w\": {summary.avg_power_w.ToString("F1", inv)},");
            js.AppendLine($"    \"battery_start_pct\": {summary.battery_start_pct.ToString("F2", inv)},");
            js.AppendLine($"    \"battery_end_pct\": {summary.battery_end_pct.ToString("F2", inv)},");
            js.AppendLine($"    \"motor_temp_start_c\": {summary.motor_temp_start_c.ToString("F2", inv)},");
            js.AppendLine($"    \"motor_temp_max_c\": {summary.motor_temp_max_c.ToString("F2", inv)},");
            js.AppendLine($"    \"motor_temp_rise_c\": {summary.motor_temp_rise_c.ToString("F2", inv)},");
            js.AppendLine($"    \"rollover_warning_count\": {summary.rollover_warning_count},");
            js.AppendLine($"    \"steep_slope_warning_count\": {summary.steep_slope_warning_count}");
            js.AppendLine("  },");

            // Events
            js.AppendLine("  \"events\": [");
            if (events != null && events.Count > 0)
            {
                int cap = Math.Min(500, events.Count);
                for (int i = 0; i < cap; i++)
                {
                    js.Append($"    {{ \"t_s\": {events[i].t_s.ToString("F3", inv)}, \"type\": \"{EscapeJson(events[i].type)}\" }}");
                    if (i < cap - 1) js.AppendLine(",");
                    else js.AppendLine();
                }
            }
            js.AppendLine("  ]");

            js.Append("}");
            return js.ToString();
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
        }

        public void CloseWriters()
        {
            try
            {
                if (telemetryWriter != null)
                {
                    telemetryWriter.Flush();
                    telemetryWriter.Dispose();
                    telemetryWriter = null;
                }
                if (wheelsWriter != null)
                {
                    wheelsWriter.Flush();
                    wheelsWriter.Dispose();
                    wheelsWriter = null;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SessionWriter] Error closing writers: {ex.Message}");
            }
        }

        public void Dispose()
        {
            CloseWriters();
        }
    }
}

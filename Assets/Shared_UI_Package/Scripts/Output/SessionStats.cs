using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace ProjectName.Output
{
    [Serializable]
    public class SessionSummary
    {
        public float duration_s;
        public float distance_m;
        public float avg_speed_mps;
        public float max_speed_mps;
        public float avg_abs_accel_mps2;
        public float max_g;
        public float max_slope_deg;
        public float max_abs_pitch_deg;
        public float max_abs_roll_deg;
        public float avg_slip_pct;
        public float max_slip_pct;
        public float pct_time_any_wheel_airborne;
        public float energy_wh;
        public float? energy_per_m_wh;
        public float avg_power_w;
        public float battery_start_pct = 100f;
        public float battery_end_pct = 100f;
        public float motor_temp_start_c;
        public float motor_temp_max_c;
        public float motor_temp_rise_c;
        public int rollover_warning_count;
        public int steep_slope_warning_count;
    }

    [Serializable]
    public struct SessionEvent
    {
        public float t_s;
        public string type;
    }

    public class SessionStats
    {
        private bool isFirstSample = true;
        private int sampleCount = 0;
        private float startOdometer = 0f;
        private float lastOdometer = 0f;
        private float startEnergy = 0f;
        private float lastEnergy = 0f;

        private double sumSpeed = 0;
        private float maxSpeed = 0f;
        private double sumAbsAccel = 0;
        private float maxG = 0f;
        private float maxSlope = 0f;
        private float maxAbsPitch = 0f;
        private float maxAbsRoll = 0f;

        private double sumPower = 0;
        private float battStart = 100f;
        private float battEnd = 100f;

        private float tempStart = 0f;
        private float tempMax = float.MinValue;

        private double sumGroundedSlip = 0;
        private int groundedSlipSamples = 0;
        private float maxGroundedSlip = 0f;
        private int airborneSamples = 0;

        private bool prevRollover = false;
        private bool prevSteepSlope = false;
        private int rolloverWarnCount = 0;
        private int steepSlopeWarnCount = 0;

        public void Reset()
        {
            isFirstSample = true;
            sampleCount = 0;
            startOdometer = 0f;
            lastOdometer = 0f;
            startEnergy = 0f;
            lastEnergy = 0f;
            sumSpeed = 0;
            maxSpeed = 0f;
            sumAbsAccel = 0;
            maxG = 0f;
            maxSlope = 0f;
            maxAbsPitch = 0f;
            maxAbsRoll = 0f;
            sumPower = 0;
            battStart = 100f;
            battEnd = 100f;
            tempStart = 0f;
            tempMax = float.MinValue;
            sumGroundedSlip = 0;
            groundedSlipSamples = 0;
            maxGroundedSlip = 0f;
            airborneSamples = 0;
            prevRollover = false;
            prevSteepSlope = false;
            rolloverWarnCount = 0;
            steepSlopeWarnCount = 0;
        }

        public void AddSample(
            float odo,
            float speed,
            float accel,
            float gForce,
            float pitch,
            float roll,
            float slope,
            float batt,
            float power,
            float energy,
            float motorTemp,
            float maxSlip,
            float avgSlip,
            int groundedWheels,
            int totalWheels,
            bool rolloverWarn,
            bool steepSlopeWarn)
        {
            if (isFirstSample)
            {
                startOdometer = odo;
                startEnergy = energy;
                battStart = batt;
                tempStart = motorTemp;
                tempMax = motorTemp;
                isFirstSample = false;
            }

            sampleCount++;
            lastOdometer = odo;
            lastEnergy = energy;
            battEnd = batt;

            sumSpeed += speed;
            if (speed > maxSpeed) maxSpeed = speed;

            sumAbsAccel += Mathf.Abs(accel);
            if (gForce > maxG) maxG = gForce;
            if (slope > maxSlope) maxSlope = slope;

            float absPitch = Mathf.Abs(pitch);
            if (absPitch > maxAbsPitch) maxAbsPitch = absPitch;

            float absRoll = Mathf.Abs(roll);
            if (absRoll > maxAbsRoll) maxAbsRoll = absRoll;

            sumPower += power;
            if (motorTemp > tempMax) tempMax = motorTemp;

            if (groundedWheels > 0)
            {
                sumGroundedSlip += avgSlip;
                groundedSlipSamples++;
                if (maxSlip > maxGroundedSlip) maxGroundedSlip = maxSlip;
            }

            if (totalWheels > 0 && groundedWheels < totalWheels)
            {
                airborneSamples++;
            }

            if (rolloverWarn && !prevRollover) rolloverWarnCount++;
            if (steepSlopeWarn && !prevSteepSlope) steepSlopeWarnCount++;

            prevRollover = rolloverWarn;
            prevSteepSlope = steepSlopeWarn;
        }

        public SessionSummary ComputeSummary(float durationSeconds)
        {
            var summary = new SessionSummary();
            summary.duration_s = durationSeconds;
            summary.distance_m = Mathf.Max(0f, lastOdometer - startOdometer);

            if (sampleCount > 0)
            {
                summary.avg_speed_mps = (float)(sumSpeed / sampleCount);
                summary.max_speed_mps = maxSpeed;
                summary.avg_abs_accel_mps2 = (float)(sumAbsAccel / sampleCount);
                summary.max_g = maxG;
                summary.max_slope_deg = maxSlope;
                summary.max_abs_pitch_deg = maxAbsPitch;
                summary.max_abs_roll_deg = maxAbsRoll;
                summary.avg_power_w = (float)(sumPower / sampleCount);
                summary.pct_time_any_wheel_airborne = (100f * airborneSamples) / sampleCount;
            }

            if (groundedSlipSamples > 0)
            {
                summary.avg_slip_pct = (float)(sumGroundedSlip / groundedSlipSamples);
                summary.max_slip_pct = maxGroundedSlip;
            }
            else
            {
                summary.avg_slip_pct = 0f;
                summary.max_slip_pct = 0f;
            }

            summary.energy_wh = Mathf.Max(0f, lastEnergy - startEnergy);
            if (summary.distance_m >= 1.0f)
            {
                summary.energy_per_m_wh = summary.energy_wh / summary.distance_m;
            }
            else
            {
                summary.energy_per_m_wh = null;
            }

            summary.battery_start_pct = battStart;
            summary.battery_end_pct = battEnd;

            summary.motor_temp_start_c = tempStart;
            summary.motor_temp_max_c = tempMax == float.MinValue ? tempStart : tempMax;
            summary.motor_temp_rise_c = summary.motor_temp_max_c - summary.motor_temp_start_c;

            summary.rollover_warning_count = rolloverWarnCount;
            summary.steep_slope_warning_count = steepSlopeWarnCount;

            return summary;
        }

        public static string GenerateReportMarkdown(
            string sessionId,
            string roverName,
            string planetName,
            float gravity,
            string terrainDesc,
            int sampleRateHz,
            int totalSamples,
            string startedUtc,
            SessionSummary s,
            List<SessionEvent> events,
            List<string> unavailableColumns,
            List<float[]> pausedSpans,
            bool hasWheelsCsv)
        {
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();

            sb.AppendLine($"# Mission Telemetry Report: {sessionId}");
            sb.AppendLine();
            sb.AppendLine("Automated execution and telemetry summary recorded in Unity simulation.");
            sb.AppendLine();

            sb.AppendLine("## 1. Mission Configuration");
            sb.AppendLine();
            sb.AppendLine("| Parameter | Value |");
            sb.AppendLine("| --- | --- |");
            sb.AppendLine($"| Rover | {roverName} |");
            sb.AppendLine($"| Planet | {planetName} |");
            sb.AppendLine($"| Gravity | {gravity.ToString("F2", inv)} m/s² |");
            sb.AppendLine($"| Terrain | {terrainDesc} |");
            sb.AppendLine($"| Duration | {s.duration_s.ToString("F2", inv)} s |");
            sb.AppendLine($"| Total Samples | {totalSamples} |");
            sb.AppendLine($"| Sample Rate | {sampleRateHz} Hz |");
            sb.AppendLine($"| Started (UTC) | {startedUtc} |");
            sb.AppendLine();

            sb.AppendLine("## 2. Key Results");
            sb.AppendLine();
            sb.AppendLine("| Metric | Computed Value | Unit |");
            sb.AppendLine("| --- | --- | --- |");
            sb.AppendLine($"| Distance Traveled | {s.distance_m.ToString("F3", inv)} | m |");
            sb.AppendLine($"| Average Speed | {s.avg_speed_mps.ToString("F3", inv)} | m/s |");
            sb.AppendLine($"| Maximum Speed | {s.max_speed_mps.ToString("F3", inv)} | m/s |");
            sb.AppendLine($"| Mean Abs Acceleration | {s.avg_abs_accel_mps2.ToString("F3", inv)} | m/s² |");
            sb.AppendLine($"| Peak Load (G-Force) | {s.max_g.ToString("F3", inv)} | G |");
            sb.AppendLine($"| Maximum Slope | {s.max_slope_deg.ToString("F2", inv)} | deg |");
            sb.AppendLine($"| Maximum Abs Pitch | {s.max_abs_pitch_deg.ToString("F2", inv)} | deg |");
            sb.AppendLine($"| Maximum Abs Roll | {s.max_abs_roll_deg.ToString("F2", inv)} | deg |");
            sb.AppendLine($"| Average Wheel Slip | {s.avg_slip_pct.ToString("F2", inv)} | % |");
            sb.AppendLine($"| Maximum Wheel Slip | {s.max_slip_pct.ToString("F2", inv)} | % |");
            sb.AppendLine($"| Airborne Time Ratio | {s.pct_time_any_wheel_airborne.ToString("F2", inv)} | % |");
            sb.AppendLine($"| Total Energy Consumed | {s.energy_wh.ToString("F2", inv)} | Wh |");
            string ePerMStr = s.energy_per_m_wh.HasValue ? s.energy_per_m_wh.Value.ToString("F4", inv) : "N/A (< 1 m)";
            sb.AppendLine($"| Specific Energy | {ePerMStr} | Wh/m |");
            sb.AppendLine($"| Average Power Draw | {s.avg_power_w.ToString("F1", inv)} | W |");
            sb.AppendLine($"| Battery State (Start / End) | {s.battery_start_pct.ToString("F1", inv)} / {s.battery_end_pct.ToString("F1", inv)} | % |");
            sb.AppendLine($"| Motor Temperature (Start / Max / Rise) | {s.motor_temp_start_c.ToString("F1", inv)} / {s.motor_temp_max_c.ToString("F1", inv)} / {s.motor_temp_rise_c.ToString("F1", inv)} | °C |");
            sb.AppendLine($"| Rollover Warnings Triggered | {s.rollover_warning_count} | count |");
            sb.AppendLine($"| Steep Slope Warnings Triggered | {s.steep_slope_warning_count} | count |");
            sb.AppendLine();

            sb.AppendLine("## 3. Mission Events (First 20)");
            sb.AppendLine();
            if (events == null || events.Count == 0)
            {
                sb.AppendLine("No critical warning or transition events logged during session.");
            }
            else
            {
                sb.AppendLine("| Time (s) | Event Description |");
                sb.AppendLine("| --- | --- |");
                int count = Math.Min(20, events.Count);
                for (int i = 0; i < count; i++)
                {
                    sb.AppendLine($"| {events[i].t_s.ToString("F2", inv)} | {events[i].type} |");
                }
            }
            sb.AppendLine();

            sb.AppendLine("## 4. Data Quality & Coverage");
            sb.AppendLine();
            if (unavailableColumns != null && unavailableColumns.Count > 0)
            {
                sb.AppendLine($"- **Unavailable Columns**: {string.Join(", ", unavailableColumns)}");
            }
            else
            {
                sb.AppendLine("- **Unavailable Columns**: None (all channels available).");
            }

            if (pausedSpans != null && pausedSpans.Count > 0)
            {
                sb.AppendLine($"- **Paused Spans Count**: {pausedSpans.Count}");
                for (int i = 0; i < pausedSpans.Count; i++)
                {
                    float p0 = pausedSpans[i][0];
                    float p1 = pausedSpans[i][1];
                    sb.AppendLine($"  - Pause {i + 1}: {p0.ToString("F2", inv)} s to {p1.ToString("F2", inv)} s (duration: {(p1 - p0).ToString("F2", inv)} s)");
                }
            }
            else
            {
                sb.AppendLine("- **Paused Spans**: None (continuous recording).");
            }
            sb.AppendLine();

            sb.AppendLine("## 5. Provenance & Model Disclaimer");
            sb.AppendLine();
            sb.AppendLine("> Power, energy, battery and motor temperature are simulation model estimates and are not hardware-validated.");
            sb.AppendLine();

            sb.AppendLine("## 6. Artifact Files");
            sb.AppendLine();
            sb.AppendLine("- `telemetry.csv` (Synchronous time-series telemetry at fixed sample rate)");
            if (hasWheelsCsv)
            {
                sb.AppendLine("- `wheels.csv` (Long-format per-wheel kinematic and actuator state)");
            }
            sb.AppendLine("- `session.json` (Machine-readable metadata, summary metrics, events, and data dictionary)");
            sb.AppendLine("- `report.md` (Self-contained markdown report)");
            sb.AppendLine();

            return sb.ToString();
        }
    }
}

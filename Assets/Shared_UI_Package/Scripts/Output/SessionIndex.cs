using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using UnityEngine;

namespace ProjectName.Output
{
    [Serializable]
    public class SessionEntry
    {
        public string sessionId;
        public string folderPath;
        public string roverId;
        public string roverDisplayName;
        public string planetName;
        public string status;
        public bool isIncomplete;
        public float durationSimS;
        public int totalSamples;
        public long totalSizeBytes;
        public DateTime startedUtc;
        public SessionSummary summary;
    }

    public static class SessionIndex
    {
        public static List<SessionEntry> ScanSessions()
        {
            var list = new List<SessionEntry>();
            try
            {
                string root = SessionWriter.GetOutputsRoot();
                if (!Directory.Exists(root)) return list;

                string[] dirs = Directory.GetDirectories(root);
                Array.Sort(dirs);
                Array.Reverse(dirs); // Newest first

                foreach (string dir in dirs)
                {
                    string jsonPath = Path.Combine(dir, "session.json");
                    if (!File.Exists(jsonPath)) continue;

                    try
                    {
                        string json = File.ReadAllText(jsonPath);
                        var entry = ParseSessionJson(dir, json);
                        if (entry != null)
                        {
                            entry.totalSizeBytes = CalculateDirectorySize(dir);
                            list.Add(entry);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[SessionIndex] Could not parse session at '{dir}': {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SessionIndex] Error scanning sessions: {ex}");
            }

            return list;
        }

        private static SessionEntry ParseSessionJson(string dir, string json)
        {
            var entry = new SessionEntry
            {
                folderPath = dir,
                sessionId = Path.GetFileName(dir),
                status = "unknown",
                roverId = "rover",
                planetName = "Mars",
                summary = new SessionSummary()
            };

            // Fast regex parsing robust against schema variations
            var mSessionId = Regex.Match(json, "\"session_id\"\\s*:\\s*\"([^\"]+)\"");
            if (mSessionId.Success) entry.sessionId = mSessionId.Groups[1].Value;

            var mStatus = Regex.Match(json, "\"status\"\\s*:\\s*\"([^\"]+)\"");
            if (mStatus.Success) entry.status = mStatus.Groups[1].Value;

            entry.isIncomplete = entry.status == "recording" || entry.status.StartsWith("aborted_") || entry.status.StartsWith("auto_stopped_");

            var mRover = Regex.Match(json, "\"rover\"\\s*:\\s*\\{[^}]*\"id\"\\s*:\\s*\"([^\"]+)\"");
            if (mRover.Success) entry.roverId = mRover.Groups[1].Value;

            var mRoverName = Regex.Match(json, "\"display_name\"\\s*:\\s*\"([^\"]+)\"");
            if (mRoverName.Success) entry.roverDisplayName = mRoverName.Groups[1].Value;
            else entry.roverDisplayName = entry.roverId;

            var mPlanet = Regex.Match(json, "\"planet\"\\s*:\\s*\"([^\"]+)\"");
            if (mPlanet.Success) entry.planetName = mPlanet.Groups[1].Value;

            var mDuration = Regex.Match(json, "\"duration_sim_s\"\\s*:\\s*([0-9.]+)");
            if (mDuration.Success && float.TryParse(mDuration.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float dur))
            {
                entry.durationSimS = dur;
            }

            var mSamples = Regex.Match(json, "\"samples\"\\s*:\\s*([0-9]+)");
            if (mSamples.Success && int.TryParse(mSamples.Groups[1].Value, out int smp))
            {
                entry.totalSamples = smp;
            }

            var mStarted = Regex.Match(json, "\"started_utc\"\\s*:\\s*\"([^\"]+)\"");
            if (mStarted.Success && DateTime.TryParse(mStarted.Groups[1].Value, out DateTime st))
            {
                entry.startedUtc = st;
            }

            // Summary metrics extraction
            ExtractSummaryMetrics(json, entry.summary);

            return entry;
        }

        private static void ExtractSummaryMetrics(string json, SessionSummary s)
        {
            var inv = CultureInfo.InvariantCulture;
            Match m;

            m = Regex.Match(json, "\"duration_s\"\\s*:\\s*([0-9.]+)");
            if (m.Success && float.TryParse(m.Groups[1].Value, NumberStyles.Float, inv, out float dur)) s.duration_s = dur;

            m = Regex.Match(json, "\"distance_m\"\\s*:\\s*([0-9.]+)");
            if (m.Success && float.TryParse(m.Groups[1].Value, NumberStyles.Float, inv, out float dist)) s.distance_m = dist;

            m = Regex.Match(json, "\"avg_speed_mps\"\\s*:\\s*([0-9.]+)");
            if (m.Success && float.TryParse(m.Groups[1].Value, NumberStyles.Float, inv, out float avgV)) s.avg_speed_mps = avgV;

            m = Regex.Match(json, "\"max_speed_mps\"\\s*:\\s*([0-9.]+)");
            if (m.Success && float.TryParse(m.Groups[1].Value, NumberStyles.Float, inv, out float maxV)) s.max_speed_mps = maxV;

            m = Regex.Match(json, "\"energy_wh\"\\s*:\\s*([0-9.]+)");
            if (m.Success && float.TryParse(m.Groups[1].Value, NumberStyles.Float, inv, out float eWh)) s.energy_wh = eWh;

            m = Regex.Match(json, "\"energy_per_m_wh\"\\s*:\\s*([0-9.]+)");
            if (m.Success && float.TryParse(m.Groups[1].Value, NumberStyles.Float, inv, out float ePerM)) s.energy_per_m_wh = ePerM;
            else s.energy_per_m_wh = null;

            m = Regex.Match(json, "\"avg_slip_pct\"\\s*:\\s*([0-9.]+)");
            if (m.Success && float.TryParse(m.Groups[1].Value, NumberStyles.Float, inv, out float slip)) s.avg_slip_pct = slip;

            m = Regex.Match(json, "\"max_slope_deg\"\\s*:\\s*([0-9.]+)");
            if (m.Success && float.TryParse(m.Groups[1].Value, NumberStyles.Float, inv, out float slope)) s.max_slope_deg = slope;

            m = Regex.Match(json, "\"max_abs_pitch_deg\"\\s*:\\s*([0-9.]+)");
            if (m.Success && float.TryParse(m.Groups[1].Value, NumberStyles.Float, inv, out float pitch)) s.max_abs_pitch_deg = pitch;

            m = Regex.Match(json, "\"max_abs_roll_deg\"\\s*:\\s*([0-9.]+)");
            if (m.Success && float.TryParse(m.Groups[1].Value, NumberStyles.Float, inv, out float roll)) s.max_abs_roll_deg = roll;

            m = Regex.Match(json, "\"max_g\"\\s*:\\s*([0-9.]+)");
            if (m.Success && float.TryParse(m.Groups[1].Value, NumberStyles.Float, inv, out float g)) s.max_g = g;

            m = Regex.Match(json, "\"battery_end_pct\"\\s*:\\s*([0-9.]+)");
            if (m.Success && float.TryParse(m.Groups[1].Value, NumberStyles.Float, inv, out float batt)) s.battery_end_pct = batt;

            int warnCount = 0;
            m = Regex.Match(json, "\"rollover_warning_count\"\\s*:\\s*([0-9]+)");
            if (m.Success && int.TryParse(m.Groups[1].Value, out int rw))
            {
                s.rollover_warning_count = rw;
                warnCount += rw;
            }
            m = Regex.Match(json, "\"steep_slope_warning_count\"\\s*:\\s*([0-9]+)");
            if (m.Success && int.TryParse(m.Groups[1].Value, out int sw))
            {
                s.steep_slope_warning_count = sw;
                warnCount += sw;
            }
        }

        private static long CalculateDirectorySize(string dir)
        {
            long size = 0;
            try
            {
                var files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories);
                foreach (var f in files)
                {
                    size += new FileInfo(f).Length;
                }
            }
            catch { }
            return size;
        }

        public static bool DeleteSession(SessionEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.folderPath)) return false;
            try
            {
                if (Directory.Exists(entry.folderPath))
                {
                    Directory.Delete(entry.folderPath, true);
                }

                string zipPath = entry.folderPath + ".zip";
                if (File.Exists(zipPath))
                {
                    File.Delete(zipPath);
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SessionIndex] Failed to delete session '{entry.sessionId}': {ex}");
                return false;
            }
        }

        public static string ExportZip(SessionEntry entry)
        {
            if (entry == null || !Directory.Exists(entry.folderPath)) return null;
            try
            {
                string zipPath = entry.folderPath + ".zip";
                if (File.Exists(zipPath))
                {
                    File.Delete(zipPath);
                }
                ZipFile.CreateFromDirectory(entry.folderPath, zipPath, System.IO.Compression.CompressionLevel.Optimal, false);
                return zipPath;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SessionIndex] Failed to export zip for '{entry.sessionId}': {ex}");
                return null;
            }
        }

        public static string ExportAll(List<SessionEntry> entries)
        {
            try
            {
                string root = SessionWriter.GetOutputsRoot();
                string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                string zipPath = Path.Combine(root, $"Outputs_Export_{timestamp}.zip");

                if (File.Exists(zipPath))
                {
                    File.Delete(zipPath);
                }

                using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
                {
                    foreach (var entry in entries)
                    {
                        if (entry == null || !Directory.Exists(entry.folderPath)) continue;
                        string folderName = Path.GetFileName(entry.folderPath);
                        string[] files = Directory.GetFiles(entry.folderPath, "*", SearchOption.AllDirectories);
                        foreach (string file in files)
                        {
                            string relative = Path.GetRelativePath(entry.folderPath, file);
                            string entryName = Path.Combine(folderName, relative).Replace('\\', '/');
                            archive.CreateEntryFromFile(file, entryName, System.IO.Compression.CompressionLevel.Optimal);
                        }
                    }
                }
                return zipPath;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SessionIndex] Failed to export all sessions: {ex}");
                return null;
            }
        }
    }
}

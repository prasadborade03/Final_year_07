using System;
using System.Collections.Generic;
using UnityEngine;
using ProjectName.Rover;
using ProjectName.Planetary;
using ProjectName.Core;

namespace ProjectName.Output
{
    public enum RecordingState
    {
        Idle,
        Recording,
        Paused
    }

    public class FloatRingBuffer
    {
        private float[] buffer;
        private int capacity;
        private int head;
        private int count;

        public int Capacity => capacity;
        public int Count => count;

        public FloatRingBuffer(int capacity)
        {
            this.capacity = Mathf.Max(10, capacity);
            this.buffer = new float[this.capacity];
            this.head = 0;
            this.count = 0;
        }

        public void Resize(int newCapacity)
        {
            if (newCapacity <= 0) return;
            var newBuf = new float[newCapacity];
            int copyCount = Mathf.Min(count, newCapacity);
            for (int i = 0; i < copyCount; i++)
            {
                newBuf[i] = Get(count - copyCount + i);
            }
            buffer = newBuf;
            capacity = newCapacity;
            head = copyCount % capacity;
            count = copyCount;
        }

        public void Push(float value)
        {
            buffer[head] = value;
            head = (head + 1) % capacity;
            if (count < capacity) count++;
        }

        public float Get(int index)
        {
            if (index < 0 || index >= count) return 0f;
            int start = (head - count + capacity) % capacity;
            return buffer[(start + index) % capacity];
        }

        public void Clear()
        {
            head = 0;
            count = 0;
        }

        public void GetMinMax(out float min, out float max)
        {
            if (count == 0)
            {
                min = 0f;
                max = 1f;
                return;
            }
            min = float.MaxValue;
            max = float.MinValue;
            for (int i = 0; i < count; i++)
            {
                float v = Get(i);
                if (v < min) min = v;
                if (v > max) max = v;
            }
            if (Mathf.Abs(max - min) < 0.001f)
            {
                max = min + 1f;
            }
        }
    }

    [DefaultExecutionOrder(100)]
    public class MissionRecorder : MonoBehaviour
    {
        private static MissionRecorder instance;
        public static MissionRecorder Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = FindAnyObjectByType<MissionRecorder>();
                    if (instance == null)
                    {
                        var go = new GameObject("MissionRecorder");
                        DontDestroyOnLoad(go);
                        instance = go.AddComponent<MissionRecorder>();
                    }
                }
                return instance;
            }
            private set => instance = value;
        }

        public const int TRACE_CHANNEL_COUNT = 6;
        public const int CHANNEL_SPEED = 0;
        public const int CHANNEL_SLOPE = 1;
        public const int CHANNEL_SLIP = 2;
        public const int CHANNEL_POWER = 3;
        public const int CHANNEL_BATTERY = 4;
        public const int CHANNEL_MOTOR_TEMP = 5;

        [Header("State")]
        [SerializeField] private RecordingState state = RecordingState.Idle;
        [SerializeField] private int sampleRateHz = 10;
        [SerializeField] private bool autoStartOnFirstMovement = false;
        [SerializeField] private bool includeWheelData = true;
        [SerializeField] private float maxDurationMinutes = 30f;

        private float sampleTimer = 0f;
        private float simTime = 0f;
        private float pauseStartTime = 0f;
        private int frameIndex = 0;

        private string startedUtc;
        private Vector3 initialPos;
        private float initialHeading;
        private PlanetProfile lastPlanetProfile;
        private float lastMissionTime = -1f;
        private float lastOdometer = -1f;

        private SessionWriter writer;
        private SessionStats stats = new SessionStats();
        private List<SessionEvent> events = new List<SessionEvent>();
        private List<float[]> pausedSpans = new List<float[]>();
        private List<string> unavailableColumns = new List<string>();

        private FloatRingBuffer[] traceBuffers;

        public RecordingState State => state;
        public int SampleRateHz => sampleRateHz;
        public bool AutoStartOnFirstMovement
        {
            get => autoStartOnFirstMovement;
            set => autoStartOnFirstMovement = value;
        }
        public bool IncludeWheelData
        {
            get => includeWheelData;
            set => includeWheelData = value;
        }
        public float SimTime => simTime;
        public int SampleCount => writer != null ? writer.TotalSamplesWritten : 0;
        public long BytesWritten => writer != null ? writer.BytesWritten : 0;
        public SessionStats Stats => stats;
        public FloatRingBuffer[] TraceBuffers => traceBuffers;

        public event Action<RecordingState> OnStateChanged;
        public event Action<string> OnStatusMessage;
        public event Action OnSampleRecorded;
        public event Action<string> OnSessionSaved;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance == null)
            {
                var go = new GameObject("MissionRecorder");
                DontDestroyOnLoad(go);
                Instance = go.AddComponent<MissionRecorder>();
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            InitBuffers();
            ActiveRoverContext.OnRoverDestroyed += HandleRoverDestroyed;
        }

        private void OnDestroy()
        {
            ActiveRoverContext.OnRoverDestroyed -= HandleRoverDestroyed;
            if (state != RecordingState.Idle)
            {
                StopAndSave("aborted_scene_unload");
            }
        }

        private void OnApplicationQuit()
        {
            if (state != RecordingState.Idle)
            {
                StopAndSave("aborted_scene_unload");
            }
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus && state != RecordingState.Idle)
            {
                writer?.Flush();
            }
        }

        private void InitBuffers()
        {
            int bufSize = sampleRateHz * 60; // 60 seconds rolling window
            traceBuffers = new FloatRingBuffer[TRACE_CHANNEL_COUNT];
            for (int i = 0; i < TRACE_CHANNEL_COUNT; i++)
            {
                traceBuffers[i] = new FloatRingBuffer(bufSize);
            }
        }

        public void SetSampleRate(int hz)
        {
            if (state != RecordingState.Idle)
            {
                Debug.LogWarning("[MissionRecorder] Cannot change sample rate while recording.");
                return;
            }
            if (hz != 5 && hz != 10 && hz != 20) hz = 10;
            sampleRateHz = hz;
            int bufSize = sampleRateHz * 60;
            for (int i = 0; i < TRACE_CHANNEL_COUNT; i++)
            {
                traceBuffers[i].Resize(bufSize);
            }
        }

        public bool CanStartRecording(out string reason)
        {
            if (!ActiveRoverContext.HasActiveRover)
            {
                reason = "Spawn a rover first";
                return false;
            }
            var handle = ActiveRoverContext.Current;
            if (handle == null || handle.telemetry == null)
            {
                reason = "Spawn a rover first";
                return false;
            }
            reason = null;
            return true;
        }

        public bool StartRecording(out string error)
        {
            error = null;
            if (state != RecordingState.Idle)
            {
                error = "Recorder already running";
                return false;
            }

            if (!CanStartRecording(out error))
            {
                return false;
            }

            var handle = ActiveRoverContext.Current;
            var tel = handle.telemetry;
            string roverId = handle.profile != null ? handle.profile.id : "rover";

            var env = PlanetEnvironmentController.Instance;
            string planetName = (env != null && env.currentProfile != null) ? env.currentProfile.planetName : "Mars";
            lastPlanetProfile = env != null ? env.currentProfile : null;

            initialPos = tel.worldPosition;
            initialHeading = tel.headingDeg;
            startedUtc = DateTime.UtcNow.ToString("o");

            simTime = 0f;
            sampleTimer = 0f;
            frameIndex = 0;
            lastMissionTime = tel.missionTimeSeconds;
            lastOdometer = tel.odometerMeters;

            stats.Reset();
            events.Clear();
            pausedSpans.Clear();
            unavailableColumns.Clear();

            if (traceBuffers == null) InitBuffers();

            for (int i = 0; i < TRACE_CHANNEL_COUNT; i++)
            {
                traceBuffers[i]?.Clear();
            }

            writer = new SessionWriter();
            bool ok = writer.StartSession(
                roverId,
                planetName,
                sampleRateHz,
                initialPos,
                initialHeading,
                includeWheelData,
                out error);

            if (!ok)
            {
                writer = null;
                return false;
            }

            state = RecordingState.Recording;
            OnStateChanged?.Invoke(state);
            OnStatusMessage?.Invoke($"Recording started: {writer.SessionId}");
            return true;
        }

        public void Pause()
        {
            if (state != RecordingState.Recording) return;
            state = RecordingState.Paused;
            pauseStartTime = simTime;
            LogEvent("paused");
            writer?.Flush();
            OnStateChanged?.Invoke(state);
            OnStatusMessage?.Invoke("Recording paused");
        }

        public void Resume()
        {
            if (state != RecordingState.Paused) return;
            state = RecordingState.Recording;
            pausedSpans.Add(new float[] { pauseStartTime, simTime });
            LogEvent("resumed");
            OnStateChanged?.Invoke(state);
            OnStatusMessage?.Invoke("Recording resumed");
        }

        public void StopAndSave(string reason = "completed")
        {
            if (state == RecordingState.Idle) return;

            if (state == RecordingState.Paused)
            {
                pausedSpans.Add(new float[] { pauseStartTime, simTime });
            }

            state = RecordingState.Idle;

            if (writer != null)
            {
                var handle = ActiveRoverContext.Current;
                RoverProfile rProfile = handle?.profile;
                var env = PlanetEnvironmentController.Instance;
                PlanetProfile pProfile = env?.currentProfile;

                Vector3 tSize = SimulationContext.TerrainSize;
                string tPreset = SimulationContext.SelectedTerrainPreset;
                string endedUtc = DateTime.UtcNow.ToString("o");

                bool wheelDataAvail = includeWheelData && handle != null && handle.wheelDefs != null && handle.wheelDefs.Length > 0;
                if (!wheelDataAvail && includeWheelData)
                {
                    unavailableColumns.Add("wheels.csv (no wheel actuators on active rover)");
                }

                SessionSummary finalSummary = stats.ComputeSummary(simTime);

                string savedDir = writer.SessionDirectory;
                writer.FinalizeSession(
                    reason,
                    startedUtc,
                    endedUtc,
                    simTime,
                    pausedSpans,
                    reason == "completed" ? null : reason,
                    rProfile,
                    pProfile,
                    tSize,
                    tPreset,
                    null,
                    initialPos,
                    initialHeading,
                    sampleRateHz,
                    wheelDataAvail,
                    unavailableColumns,
                    finalSummary,
                    events);

                writer = null;
                OnSessionSaved?.Invoke(savedDir);
                OnStatusMessage?.Invoke($"Saved: {savedDir}");
            }

            OnStateChanged?.Invoke(state);
        }

        private void LogEvent(string type)
        {
            if (events.Count >= 500) return;
            events.Add(new SessionEvent { t_s = simTime, type = type });
        }

        private void HandleRoverDestroyed()
        {
            if (state == RecordingState.Recording || state == RecordingState.Paused)
            {
                StopAndSave("aborted_rover_reset");
                OnStatusMessage?.Invoke("Recording stopped: rover reset. Session saved.");
            }
        }

        private void FixedUpdate()
        {
            if (state == RecordingState.Idle)
            {
                if (autoStartOnFirstMovement && ActiveRoverContext.HasActiveRover)
                {
                    var tel = ActiveRoverContext.Current?.telemetry;
                    if (tel != null && tel.linearSpeedMps > 0.05f)
                    {
                        StartRecording(out _);
                    }
                }
                return;
            }

            if (state == RecordingState.Paused)
            {
                return;
            }

            // State is Recording
            if (!ActiveRoverContext.HasActiveRover)
            {
                StopAndSave("aborted_rover_reset");
                OnStatusMessage?.Invoke("Recording stopped: rover reset. Session saved.");
                return;
            }

            var roverHandle = ActiveRoverContext.Current;
            var telemetry = roverHandle?.telemetry;
            if (telemetry == null)
            {
                StopAndSave("aborted_rover_reset");
                OnStatusMessage?.Invoke("Recording stopped: rover reset. Session saved.");
                return;
            }

            // Detect rover reset where object was kept
            if (telemetry.missionTimeSeconds < lastMissionTime - 0.5f || telemetry.odometerMeters < lastOdometer - 0.5f)
            {
                StopAndSave("aborted_rover_reset");
                OnStatusMessage?.Invoke("Recording stopped: rover reset. Session saved.");
                return;
            }
            lastMissionTime = telemetry.missionTimeSeconds;
            lastOdometer = telemetry.odometerMeters;

            // Detect planet environment profile change
            var env = PlanetEnvironmentController.Instance;
            if (env != null && env.currentProfile != lastPlanetProfile)
            {
                string pName = env.currentProfile != null ? env.currentProfile.planetName : "Unknown";
                LogEvent($"planet_changed:{pName}");
                lastPlanetProfile = env.currentProfile;
            }

            // Fixed rate sampling accumulator
            sampleTimer += Time.fixedDeltaTime;
            float sampleInterval = 1f / sampleRateHz;

            while (sampleTimer >= sampleInterval)
            {
                sampleTimer -= sampleInterval;
                Sample(telemetry, roverHandle, sampleInterval);
            }

            // Max duration guard
            if (simTime >= maxDurationMinutes * 60f)
            {
                StopAndSave("auto_stopped_max_duration");
                OnStatusMessage?.Invoke("Recording auto-stopped: reached max duration (30m).");
            }
        }

        private void Sample(RoverTelemetry tel, RoverHandle handle, float dt)
        {
            simTime += dt;

            // Rising edge detection for events
            if (tel.isRolloverWarning)
            {
                LogEvent("rollover_warning_on");
            }
            if (tel.isSteepSlopeWarning)
            {
                LogEvent("steep_slope_warning_on");
            }

            // Slip calculation across grounded wheels
            int groundedCount = 0;
            int totalWheels = tel.wheelStates != null ? tel.wheelStates.Length : 0;
            float maxSlip = 0f;
            float sumSlip = 0f;

            if (tel.wheelStates != null && tel.wheelStates.Length > 0)
            {
                for (int i = 0; i < tel.wheelStates.Length; i++)
                {
                    if (tel.wheelStates[i].isGrounded)
                    {
                        groundedCount++;
                        float slipPct = tel.wheelStates[i].slipRatio * 100f;
                        sumSlip += slipPct;
                        if (slipPct > maxSlip) maxSlip = slipPct;
                    }
                }
            }

            float? avgSlipVal = groundedCount > 0 ? (float?)(sumSlip / groundedCount) : null;
            float? maxSlipVal = groundedCount > 0 ? (float?)maxSlip : null;

            float liveGravity = Mathf.Abs(Physics.gravity.y);
            string planetName = (PlanetEnvironmentController.Instance?.currentProfile != null)
                ? PlanetEnvironmentController.Instance.currentProfile.planetName
                : "Mars";
            string roverId = handle.profile != null ? handle.profile.id : "rover";

            // Write telemetry row
            writer.WriteTelemetryRow(
                simTime,
                frameIndex,
                tel.missionTimeSeconds,
                tel.worldPosition,
                tel.elevationMeters,
                tel.odometerMeters,
                tel.linearSpeedMps,
                tel.groundSpeedKmph,
                tel.forwardAcceleration,
                tel.gForceLoad,
                tel.pitchDeg,
                tel.rollDeg,
                tel.headingDeg,
                tel.terrainSlopeDeg,
                tel.batteryPercent,
                tel.powerDrawWatts,
                tel.energyConsumedWh,
                tel.motorTempCelsius,
                tel.ambientTempCelsius,
                maxSlipVal,
                avgSlipVal,
                groundedCount,
                liveGravity,
                tel.isRolloverWarning,
                tel.isSteepSlopeWarning,
                planetName,
                roverId);

            // Write wheel rows if enabled
            if (includeWheelData && tel.wheelStates != null)
            {
                for (int i = 0; i < tel.wheelStates.Length; i++)
                {
                    var ws = tel.wheelStates[i];
                    writer.WriteWheelRow(
                        simTime,
                        frameIndex,
                        ws.wheelName,
                        ws.rpm,
                        ws.angularVelocityRad,
                        ws.tangentialSpeedMps,
                        ws.slipRatio * 100f,
                        ws.motorTorque,
                        ws.isGrounded,
                        ws.status);
                }
            }

            // Update stats
            stats.AddSample(
                tel.odometerMeters,
                tel.linearSpeedMps,
                tel.forwardAcceleration,
                tel.gForceLoad,
                tel.pitchDeg,
                tel.rollDeg,
                tel.terrainSlopeDeg,
                tel.batteryPercent,
                tel.powerDrawWatts,
                tel.energyConsumedWh,
                tel.motorTempCelsius,
                maxSlipVal ?? 0f,
                avgSlipVal ?? 0f,
                groundedCount,
                totalWheels,
                tel.isRolloverWarning,
                tel.isSteepSlopeWarning);

            // Push to rolling ring buffers for live trace
            traceBuffers[CHANNEL_SPEED].Push(tel.linearSpeedMps);
            traceBuffers[CHANNEL_SLOPE].Push(tel.terrainSlopeDeg);
            traceBuffers[CHANNEL_SLIP].Push(avgSlipVal ?? 0f);
            traceBuffers[CHANNEL_POWER].Push(tel.powerDrawWatts);
            traceBuffers[CHANNEL_BATTERY].Push(tel.batteryPercent);
            traceBuffers[CHANNEL_MOTOR_TEMP].Push(tel.motorTempCelsius);

            frameIndex++;
            OnSampleRecorded?.Invoke();
        }
    }
}

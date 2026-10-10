using System;
using UnityEngine;
using ProjectName.Rover;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace ProjectName.Rover
{
    /// <summary>
    /// Individual realistic 3D audio controller for all three rovers in KALPANA:
    /// 1. Rover 1: Clearpath Husky A200 (4WD DC motor whine, pitch-scaled speed, skid-steer scrubbing)
    /// 2. Rover 2: Deep Robotics M20 (Brushless hub motor hum, Q/E knee joint servo whir)
    /// 3. Rover 3: NASA Perseverance M2020 (Planetary gear & regolith cleat crunch, 4-corner steer servos, clearance lift, mode switch)
    ///
    /// Respects project architecture:
    /// - Listens to ActiveRoverContext (one source of truth)
    /// - Reads physical telemetry and truthful input vectors passively
    /// - Does not modify rover physics, URDFs, joints, or controllers
    /// - 3D spatialized with smooth volume and pitch cross-fading (no clicks or physics flutter)
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)]
    public class RoverAudioController : MonoBehaviour
    {
        private static RoverAudioController _activeController;
        public static RoverAudioController ActiveController => _activeController;

        [Header("Rover Identification")]
        [SerializeField] private string currentRoverId = "";

        [Header("Audio Sources")]
        [SerializeField] private AudioSource driveAudioSource;
        [SerializeField] private AudioSource secondaryAudioSource;
        [SerializeField] private AudioSource fxAudioSource;

        [Header("Audio Clips (Resolved Dynamically or via Inspector)")]
        [SerializeField] private AudioClip huskyDriveClip;
        [SerializeField] private AudioClip huskySkidClip;
        [SerializeField] private AudioClip m20DriveClip;
        [SerializeField] private AudioClip m20KneeServoClip;
        [SerializeField] private AudioClip m2020DriveClip;
        [SerializeField] private AudioClip m2020SteerServoClip;
        [SerializeField] private AudioClip m2020SuspensionClip;
        [SerializeField] private AudioClip m2020ModeSwitchClip;

        [Header("Master Volume Limits (Subtle Realism)")]
        [Range(0.05f, 0.50f)] public float maxDriveVolume = 0.22f;
        [Range(0.05f, 0.40f)] public float maxSecondaryVolume = 0.18f;
        [Range(0.05f, 0.40f)] public float maxFxVolume = 0.20f;

        [Header("Smoothing Speeds")]
        public float driveFadeSpeed = 4.0f;
        public float secondaryFadeSpeed = 6.0f;

        // Runtime Context
        private RoverHandle boundHandle;
        private RoverTelemetry telemetry;
        private RoverProfile profile;

        // Controllers
        private RoverController_husky huskyController;
        private RoverController_m20 m20Controller;
        private RoverController_m2020 m2020Controller;
        private RoverImporter_m2020 m2020Importer;

        // Audio state
        private float targetDriveVolume = 0f;
        private float currentDriveVolume = 0f;
        private float targetSecondaryVolume = 0f;
        private float currentSecondaryVolume = 0f;

        // Rover state cache for event detection
        private float lastKneeTarget = -1.2f;
        private float lastSteerInput = 0f;
        private float lastSuspensionElevation = 0f;
        private int lastDriveModeInt = -1;
        private bool isInitialized = false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoInitializeLifecycle()
        {
            ActiveRoverContext.OnRoverActivated += HandleRoverActivatedGlobal;
            ActiveRoverContext.OnRoverDestroyed += HandleRoverDestroyedGlobal;

            // If a rover is already active when scene loads
            if (ActiveRoverContext.HasActiveRover)
            {
                HandleRoverActivatedGlobal(ActiveRoverContext.Current);
            }
        }

        private static void HandleRoverActivatedGlobal(RoverHandle handle)
        {
            if (handle == null || handle.rootGameObject == null) return;

            var audioCtrl = handle.rootGameObject.GetComponent<RoverAudioController>();
            if (audioCtrl == null)
            {
                audioCtrl = handle.rootGameObject.AddComponent<RoverAudioController>();
            }
            audioCtrl.Initialize(handle);
        }

        private static void HandleRoverDestroyedGlobal()
        {
            if (_activeController != null)
            {
                _activeController.StopAllAudio();
                _activeController = null;
            }
        }

        private void Awake()
        {
            EnsureAudioSources();
            LoadAudioClips();
        }

        public void Initialize(RoverHandle handle)
        {
            boundHandle = handle;
            _activeController = this;

            if (boundHandle != null)
            {
                profile = boundHandle.profile;
                telemetry = boundHandle.telemetry;
                currentRoverId = boundHandle.roverId.ToLowerInvariant();
            }

            // Cache controller references
            huskyController = FindAnyObjectByType<RoverController_husky>();
            m20Controller = FindAnyObjectByType<RoverController_m20>();
            m2020Controller = FindAnyObjectByType<RoverController_m2020>();
            m2020Importer = FindAnyObjectByType<RoverImporter_m2020>();

            if (m20Controller != null)
            {
                lastKneeTarget = m20Controller.kneeTarget;
            }

            if (m2020Importer != null)
            {
                lastSuspensionElevation = m2020Importer.suspTargetElevationDeg;
            }

            if (m2020Controller != null)
            {
                lastDriveModeInt = (int)m2020Controller.driveMode;
            }

            ConfigureRoverClips();
            isInitialized = true;
            Debug.Log($"[RoverAudioController] Successfully initialized audio for active rover: {currentRoverId}");
        }

        private void EnsureAudioSources()
        {
            if (driveAudioSource == null)
            {
                var driveObj = new GameObject("Audio_Drive");
                driveObj.transform.SetParent(transform, false);
                driveAudioSource = driveObj.AddComponent<AudioSource>();
                Configure3DAudioSource(driveAudioSource, true);
            }

            if (secondaryAudioSource == null)
            {
                var secObj = new GameObject("Audio_Secondary");
                secObj.transform.SetParent(transform, false);
                secondaryAudioSource = secObj.AddComponent<AudioSource>();
                Configure3DAudioSource(secondaryAudioSource, true);
            }

            if (fxAudioSource == null)
            {
                var fxObj = new GameObject("Audio_FX");
                fxObj.transform.SetParent(transform, false);
                fxAudioSource = fxObj.AddComponent<AudioSource>();
                Configure3DAudioSource(fxAudioSource, false);
            }
        }

        private void Configure3DAudioSource(AudioSource src, bool loop)
        {
            src.playOnAwake = false;
            src.loop = loop;
            src.spatialBlend = 1.0f; // 3D spatialized
            src.rolloffMode = AudioRolloffMode.Logarithmic;
            src.minDistance = 1.8f;
            src.maxDistance = 35.0f;
            src.dopplerLevel = 0.0f;
            src.volume = 0f;
        }

        private void LoadAudioClips()
        {
            // 1. Husky clips
            huskyDriveClip = LoadClip("husky_drive_loop");
            huskySkidClip = LoadClip("husky_skid_loop");

            // 2. M20 clips
            m20DriveClip = LoadClip("m20_drive_loop");
            m20KneeServoClip = LoadClip("m20_servo_knee");

            // 3. M2020 Perseverance clips
            m2020DriveClip = LoadClip("m2020_drive_loop");
            m2020SteerServoClip = LoadClip("m2020_steer_servo");
            m2020SuspensionClip = LoadClip("m2020_suspension_adjust");
            m2020ModeSwitchClip = LoadClip("m2020_mode_switch");
        }

        private AudioClip LoadClip(string clipName)
        {
            // Try Resources first
            var clip = Resources.Load<AudioClip>($"Audio/Rover/{clipName}");
            if (clip != null) return clip;

#if UNITY_EDITOR
            // Fallback Editor lookup
            clip = AssetDatabase.LoadAssetAtPath<AudioClip>($"Assets/project/sfx/rover/{clipName}.wav");
            if (clip != null) return clip;
#endif
            return null;
        }

        private void ConfigureRoverClips()
        {
            StopAllAudio();

            if (currentRoverId == "husky")
            {
                if (driveAudioSource != null && huskyDriveClip != null)
                {
                    driveAudioSource.clip = huskyDriveClip;
                }
                if (secondaryAudioSource != null && huskySkidClip != null)
                {
                    secondaryAudioSource.clip = huskySkidClip;
                }
            }
            else if (currentRoverId == "m20")
            {
                if (driveAudioSource != null && m20DriveClip != null)
                {
                    driveAudioSource.clip = m20DriveClip;
                }
                if (secondaryAudioSource != null && m20KneeServoClip != null)
                {
                    secondaryAudioSource.clip = m20KneeServoClip;
                }
            }
            else if (currentRoverId == "m2020")
            {
                if (driveAudioSource != null && m2020DriveClip != null)
                {
                    driveAudioSource.clip = m2020DriveClip;
                }
                if (secondaryAudioSource != null && m2020SteerServoClip != null)
                {
                    secondaryAudioSource.clip = m2020SteerServoClip;
                }
            }
        }

        private void Update()
        {
            if (!isInitialized || boundHandle == null || boundHandle.rootGameObject == null)
            {
                FadeOutAudioSources();
                return;
            }

            // If rover is frozen/relocating, mute movement audio
            if (boundHandle.isFrozen)
            {
                targetDriveVolume = 0f;
                targetSecondaryVolume = 0f;
                ApplySmoothedVolumes();
                return;
            }

            // Dispatch update per rover model
            if (currentRoverId == "husky")
            {
                UpdateHuskyAudio();
            }
            else if (currentRoverId == "m20")
            {
                UpdateM20Audio();
            }
            else if (currentRoverId == "m2020")
            {
                UpdateM2020Audio();
            }

            ApplySmoothedVolumes();
        }

        // =========================================================================
        // ROVER 1: HUSKY A200
        // =========================================================================
        private void UpdateHuskyAudio()
        {
            float speed = telemetry != null ? telemetry.linearSpeedMps : 0f;
            Vector2 input = huskyController != null ? huskyController.virtualJoystick : Vector2.zero;
            float inputMag = input.magnitude;

            // Driving condition
            bool isDriving = (speed > 0.02f || inputMag > 0.05f);

            if (isDriving)
            {
                float maxSpd = profile != null ? profile.maxSpeed : 1.0f;
                float normalizedSpeed = Mathf.Clamp01(Mathf.Max(speed, inputMag * 0.4f) / maxSpd);

                targetDriveVolume = Mathf.Lerp(0.08f, maxDriveVolume, normalizedSpeed);
                float targetPitch = Mathf.Lerp(0.85f, 1.25f, normalizedSpeed);
                driveAudioSource.pitch = Mathf.Lerp(driveAudioSource.pitch, targetPitch, Time.deltaTime * 5f);
            }
            else
            {
                targetDriveVolume = 0f;
            }

            // Skid-steer turn scrubbing
            bool isSteering = Mathf.Abs(input.x) > 0.15f && isDriving;
            if (isSteering)
            {
                targetSecondaryVolume = Mathf.Lerp(0.04f, maxSecondaryVolume, Mathf.Abs(input.x));
            }
            else
            {
                targetSecondaryVolume = 0f;
            }
        }

        // =========================================================================
        // ROVER 2: DEEP ROBOTICS M20
        // =========================================================================
        private void UpdateM20Audio()
        {
            float speed = telemetry != null ? telemetry.linearSpeedMps : 0f;
            Vector2 input = m20Controller != null ? m20Controller.virtualJoystick : Vector2.zero;
            float inputMag = input.magnitude;

            // 4-Wheel Drive
            bool isDriving = (speed > 0.02f || inputMag > 0.05f);

            if (isDriving)
            {
                float maxSpd = profile != null ? profile.maxSpeed : 2.5f;
                float normalizedSpeed = Mathf.Clamp01(Mathf.Max(speed, inputMag * 0.5f) / maxSpd);

                targetDriveVolume = Mathf.Lerp(0.08f, maxDriveVolume, normalizedSpeed);
                float targetPitch = Mathf.Lerp(0.90f, 1.30f, normalizedSpeed);
                driveAudioSource.pitch = Mathf.Lerp(driveAudioSource.pitch, targetPitch, Time.deltaTime * 5f);
            }
            else
            {
                targetDriveVolume = 0f;
            }

            // Knee Joint Actuation (Q/E)
            bool isKneeMoving = false;
            if (m20Controller != null)
            {
                float kneeDelta = Mathf.Abs(m20Controller.kneeTarget - lastKneeTarget);
                lastKneeTarget = m20Controller.kneeTarget;

                // Motion detected when knee target is shifting
                isKneeMoving = (kneeDelta > 0.0001f);
            }

            if (isKneeMoving)
            {
                targetSecondaryVolume = maxSecondaryVolume;
            }
            else
            {
                targetSecondaryVolume = 0f;
            }
        }

        // =========================================================================
        // ROVER 3: NASA PERSEVERANCE M2020
        // =========================================================================
        private void UpdateM2020Audio()
        {
            float speed = telemetry != null ? telemetry.linearSpeedMps : 0f;
            Vector2 input = m2020Controller != null ? m2020Controller.virtualJoystick : Vector2.zero;
            float inputMag = input.magnitude;

            // 6-Wheel Crawl Drive (Perseverance top speed is 0.15 m/s)
            bool isDriving = (speed > 0.006f || inputMag > 0.03f);

            if (isDriving)
            {
                float maxSpd = profile != null ? profile.maxSpeed : 0.15f;
                float normalizedSpeed = Mathf.Clamp01(Mathf.Max(speed, inputMag * 0.15f) / maxSpd);

                targetDriveVolume = Mathf.Lerp(0.09f, maxDriveVolume, normalizedSpeed);
                float targetPitch = Mathf.Lerp(0.90f, 1.15f, normalizedSpeed);
                driveAudioSource.pitch = Mathf.Lerp(driveAudioSource.pitch, targetPitch, Time.deltaTime * 4f);
            }
            else
            {
                targetDriveVolume = 0f;
            }

            // 4-Corner Steer Knuckle Servo Actuators
            float currentSteer = input.x;
            float steerDelta = Mathf.Abs(currentSteer - lastSteerInput);
            lastSteerInput = currentSteer;

            bool isSteerActuating = (steerDelta > 0.004f);
            if (isSteerActuating)
            {
                targetSecondaryVolume = maxSecondaryVolume;
            }
            else
            {
                targetSecondaryVolume = 0f;
            }

            // Suspension Clearance Lift (Space key)
            if (m2020Importer != null)
            {
                float currentSusp = m2020Importer.suspTargetElevationDeg;
                if (Mathf.Abs(currentSusp - lastSuspensionElevation) > 0.5f)
                {
                    lastSuspensionElevation = currentSusp;
                    PlayOneShot(m2020SuspensionClip, maxFxVolume);
                }
            }

            // Steering Mode Change (M key)
            if (m2020Controller != null)
            {
                int currentMode = (int)m2020Controller.driveMode;
                if (currentMode != lastDriveModeInt)
                {
                    lastDriveModeInt = currentMode;
                    PlayOneShot(m2020ModeSwitchClip, maxFxVolume * 0.85f);
                }
            }
        }

        // =========================================================================
        // VOLUME FADING & PLAYBACK HELPERS
        // =========================================================================
        private void ApplySmoothedVolumes()
        {
            // Drive source volume smoothing
            currentDriveVolume = Mathf.MoveTowards(currentDriveVolume, targetDriveVolume, driveFadeSpeed * Time.deltaTime);
            if (driveAudioSource != null)
            {
                driveAudioSource.volume = currentDriveVolume;
                if (currentDriveVolume > 0.001f && !driveAudioSource.isPlaying && driveAudioSource.clip != null)
                {
                    driveAudioSource.Play();
                }
                else if (currentDriveVolume <= 0.001f && driveAudioSource.isPlaying)
                {
                    driveAudioSource.Pause();
                }
            }

            // Secondary source volume smoothing
            currentSecondaryVolume = Mathf.MoveTowards(currentSecondaryVolume, targetSecondaryVolume, secondaryFadeSpeed * Time.deltaTime);
            if (secondaryAudioSource != null)
            {
                secondaryAudioSource.volume = currentSecondaryVolume;
                if (currentSecondaryVolume > 0.001f && !secondaryAudioSource.isPlaying && secondaryAudioSource.clip != null)
                {
                    secondaryAudioSource.Play();
                }
                else if (currentSecondaryVolume <= 0.001f && secondaryAudioSource.isPlaying)
                {
                    secondaryAudioSource.Pause();
                }
            }
        }

        private void FadeOutAudioSources()
        {
            targetDriveVolume = 0f;
            targetSecondaryVolume = 0f;
            ApplySmoothedVolumes();
        }

        public void PlayOneShot(AudioClip clip, float volume)
        {
            if (clip != null && fxAudioSource != null)
            {
                fxAudioSource.PlayOneShot(clip, Mathf.Clamp01(volume));
            }
        }

        public void StopAllAudio()
        {
            currentDriveVolume = 0f;
            targetDriveVolume = 0f;
            currentSecondaryVolume = 0f;
            targetSecondaryVolume = 0f;

            if (driveAudioSource != null)
            {
                driveAudioSource.volume = 0f;
                driveAudioSource.Stop();
            }
            if (secondaryAudioSource != null)
            {
                secondaryAudioSource.volume = 0f;
                secondaryAudioSource.Stop();
            }
            if (fxAudioSource != null)
            {
                fxAudioSource.volume = 0f;
                fxAudioSource.Stop();
            }
        }

        private void OnDestroy()
        {
            StopAllAudio();
            if (_activeController == this)
            {
                _activeController = null;
            }
        }
    }
}

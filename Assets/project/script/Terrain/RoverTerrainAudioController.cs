using System;
using UnityEngine;
using ProjectName.Rover;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace ProjectName.Terrain
{
    /// <summary>
    /// Realistic, subtle 3D Terrain Interaction SFX Controller for KALPANA.
    /// Handles physical surface interactions for all three rovers:
    /// - Rover 1: Clearpath Husky A200 (Pneumatic rubber tire surface roll, soft terrain bumps, wheel slip)
    /// - Rover 2: Deep Robotics M20 (Solid hub wheel roll, quadruped leg suspension shock, wheel slip)
    /// - Rover 3: NASA Perseverance M2020 (Cleated aluminum wheel regolith crunch, Rocker-Bogie articulation creak, metal boulder clunks)
    ///
    /// Preserves existing simulation:
    /// - Subscribes to ActiveRoverContext (pure presentation layer, zero physics alteration)
    /// - Truthful detection via RoverTelemetry (wheel grounding raycasts, linear velocity, slip ratios, IMU G-force load)
    /// - Reads active planetary surface material from TerrainMaterialManager / SimulationContext
    /// - Full 3D spatialization with event throttling and smooth cross-fading
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(110)]
    public class RoverTerrainAudioController : MonoBehaviour
    {
        private static RoverTerrainAudioController _activeInstance;
        public static RoverTerrainAudioController ActiveInstance => _activeInstance;

        [Header("Audio Sources (3D Spatialized)")]
        [SerializeField] private AudioSource rollAudioSource;
        [SerializeField] private AudioSource slipAudioSource;
        [SerializeField] private AudioSource impactAudioSource;
        [SerializeField] private AudioSource articulationAudioSource;

        [Header("Audio Clips — Rolling Surface")]
        public AudioClip rollDustRubberClip;
        public AudioClip rollRockyRubberClip;
        public AudioClip rollDustHubClip;
        public AudioClip rollRockyHubClip;
        public AudioClip rollCleatDustClip;
        public AudioClip rollCleatRockClip;

        [Header("Audio Clips — Impacts & Articulation")]
        public AudioClip bumpSoftClip;
        public AudioClip bumpMetalClip;
        public AudioClip legShockClip;
        public AudioClip suspensionCreakClip;
        public AudioClip slipDustClip;

        [Header("Configurable Volume Settings (Subtle & Non-Intrusive)")]
        [Range(0.02f, 0.35f)] public float maxRollVolume = 0.16f;
        [Range(0.02f, 0.35f)] public float maxSlipVolume = 0.15f;
        [Range(0.02f, 0.40f)] public float maxImpactVolume = 0.18f;
        [Range(0.02f, 0.30f)] public float maxArticulationVolume = 0.12f;

        [Header("Throttling & Cooldowns")]
        [Tooltip("Minimum time in seconds between terrain impact/bump sounds to prevent stuttering")]
        public float impactCooldown = 0.38f;
        public float rollFadeSpeed = 4.5f;

        // Context references
        private RoverHandle boundHandle;
        private RoverTelemetry telemetry;
        private RoverProfile profile;
        private string roverId = "";

        // Rocker-Bogie bodies for Perseverance
        private ArticulationBody rockerLeftBody;
        private ArticulationBody rockerRightBody;
        private ArticulationBody bogieLeftBody;
        private ArticulationBody bogieRightBody;

        // State tracking
        private float lastGForce = 1.0f;
        private float lastImpactTime = -10f;
        private float targetRollVolume = 0f;
        private float currentRollVolume = 0f;
        private float targetSlipVolume = 0f;
        private float currentSlipVolume = 0f;
        private float targetArticulationVolume = 0f;
        private float currentArticulationVolume = 0f;

        private bool isInitialized = false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoInitializeLifecycle()
        {
            ActiveRoverContext.OnRoverActivated += HandleRoverActivated;
            ActiveRoverContext.OnRoverDestroyed += HandleRoverDestroyed;

            if (ActiveRoverContext.HasActiveRover)
            {
                HandleRoverActivated(ActiveRoverContext.Current);
            }
        }

        private static void HandleRoverActivated(RoverHandle handle)
        {
            if (handle == null || handle.rootGameObject == null) return;

            var ctrl = handle.rootGameObject.GetComponent<RoverTerrainAudioController>();
            if (ctrl == null)
            {
                ctrl = handle.rootGameObject.AddComponent<RoverTerrainAudioController>();
            }
            ctrl.Initialize(handle);
        }

        private static void HandleRoverDestroyed()
        {
            if (_activeInstance != null)
            {
                _activeInstance.StopAllTerrainAudio();
                _activeInstance = null;
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
            _activeInstance = this;

            if (boundHandle != null)
            {
                telemetry = boundHandle.telemetry;
                profile = boundHandle.profile;
                roverId = boundHandle.roverId.ToLowerInvariant();
            }

            if (telemetry != null)
            {
                lastGForce = telemetry.gForceLoad;
            }

            CachePerseveranceJoints();
            ConfigureSurfaceClips();

            isInitialized = true;
            Debug.Log($"[RoverTerrainAudioController] Initialized terrain audio for: {roverId}");
        }

        private void EnsureAudioSources()
        {
            if (rollAudioSource == null)
            {
                var rollObj = new GameObject("TerrainAudio_Roll");
                rollObj.transform.SetParent(transform, false);
                rollAudioSource = rollObj.AddComponent<AudioSource>();
                Configure3DAudioSource(rollAudioSource, true);
            }

            if (slipAudioSource == null)
            {
                var slipObj = new GameObject("TerrainAudio_Slip");
                slipObj.transform.SetParent(transform, false);
                slipAudioSource = slipObj.AddComponent<AudioSource>();
                Configure3DAudioSource(slipAudioSource, true);
            }

            if (impactAudioSource == null)
            {
                var impactObj = new GameObject("TerrainAudio_Impact");
                impactObj.transform.SetParent(transform, false);
                impactAudioSource = impactObj.AddComponent<AudioSource>();
                Configure3DAudioSource(impactAudioSource, false);
            }

            if (articulationAudioSource == null)
            {
                var artObj = new GameObject("TerrainAudio_Articulation");
                artObj.transform.SetParent(transform, false);
                articulationAudioSource = artObj.AddComponent<AudioSource>();
                Configure3DAudioSource(articulationAudioSource, true);
            }
        }

        private void Configure3DAudioSource(AudioSource src, bool loop)
        {
            src.playOnAwake = false;
            src.loop = loop;
            src.spatialBlend = 1.0f; // 3D spatialized
            src.rolloffMode = AudioRolloffMode.Logarithmic;
            src.minDistance = 1.5f;
            src.maxDistance = 35.0f;
            src.dopplerLevel = 0.0f;
            src.volume = 0f;
        }

        private void LoadAudioClips()
        {
            rollDustRubberClip = LoadClip("terrain_roll_dust_rubber");
            rollRockyRubberClip = LoadClip("terrain_roll_rocky_rubber");
            rollDustHubClip = LoadClip("terrain_roll_dust_hub");
            rollRockyHubClip = LoadClip("terrain_roll_rocky_hub");
            rollCleatDustClip = LoadClip("terrain_roll_cleat_dust");
            rollCleatRockClip = LoadClip("terrain_roll_cleat_rock");

            bumpSoftClip = LoadClip("terrain_bump_soft");
            bumpMetalClip = LoadClip("terrain_bump_metal");
            legShockClip = LoadClip("terrain_leg_shock");
            suspensionCreakClip = LoadClip("terrain_suspension_creak");
            slipDustClip = LoadClip("terrain_slip_dust");
        }

        private AudioClip LoadClip(string clipName)
        {
            var clip = Resources.Load<AudioClip>($"Audio/Terrain/{clipName}");
            if (clip != null) return clip;

#if UNITY_EDITOR
            clip = AssetDatabase.LoadAssetAtPath<AudioClip>($"Assets/project/sfx/terrain/{clipName}.wav");
            if (clip != null) return clip;
#endif
            return null;
        }

        private void CachePerseveranceJoints()
        {
            if (roverId != "m2020" || boundHandle == null || boundHandle.rootGameObject == null) return;

            var allBodies = boundHandle.rootGameObject.GetComponentsInChildren<ArticulationBody>(true);
            foreach (var b in allBodies)
            {
                if (b.name == "Body_RockerLeft") rockerLeftBody = b;
                else if (b.name == "Body_RockerRight") rockerRightBody = b;
                else if (b.name == "Body_BogieLeft") bogieLeftBody = b;
                else if (b.name == "Body_BogieRight") bogieRightBody = b;
            }
        }

        private bool IsRockySurface()
        {
            if (TerrainMaterialManager.Instance != null)
            {
                return TerrainMaterialManager.Instance.activeMaterialType == TerrainMaterialManager.SurfaceMaterialType.VolcanicBasalt;
            }

            string mat = ProjectName.Core.SimulationContext.SelectedMaterial ?? "";
            return mat.ToLowerInvariant().Contains("basalt") || mat.ToLowerInvariant().Contains("rock");
        }

        private void ConfigureSurfaceClips()
        {
            bool isRocky = IsRockySurface();

            if (slipAudioSource != null && slipDustClip != null)
            {
                slipAudioSource.clip = slipDustClip;
            }

            if (articulationAudioSource != null && suspensionCreakClip != null)
            {
                articulationAudioSource.clip = suspensionCreakClip;
            }

            if (rollAudioSource == null) return;

            if (roverId == "husky")
            {
                rollAudioSource.clip = isRocky ? rollRockyRubberClip : rollDustRubberClip;
            }
            else if (roverId == "m20")
            {
                rollAudioSource.clip = isRocky ? rollRockyHubClip : rollDustHubClip;
            }
            else if (roverId == "m2020")
            {
                rollAudioSource.clip = isRocky ? rollCleatRockClip : rollCleatDustClip;
            }
        }

        private void Update()
        {
            if (!isInitialized || boundHandle == null || boundHandle.rootGameObject == null)
            {
                FadeOutAllAudio();
                return;
            }

            if (boundHandle.isFrozen)
            {
                targetRollVolume = 0f;
                targetSlipVolume = 0f;
                targetArticulationVolume = 0f;
                ApplySmoothedVolumes();
                return;
            }

            // Dynamically refresh surface roll clip if user switched material at runtime
            CheckSurfaceMaterialChange();

            // Telemetry readings
            float speed = telemetry != null ? telemetry.linearSpeedMps : 0f;
            float currentG = telemetry != null ? telemetry.gForceLoad : 1.0f;
            float gDelta = Mathf.Abs(currentG - lastGForce);
            lastGForce = currentG;

            // Ground contact & slip analysis across wheels
            int groundedWheelCount = 0;
            int totalWheels = boundHandle.wheelBodies != null ? boundHandle.wheelBodies.Length : 4;
            bool anyWheelSlipping = false;

            if (telemetry != null && telemetry.wheelStates != null)
            {
                for (int i = 0; i < telemetry.wheelStates.Length; i++)
                {
                    if (telemetry.wheelStates[i].isGrounded)
                    {
                        groundedWheelCount++;
                    }
                    if (telemetry.wheelStates[i].status == "SLIP")
                    {
                        anyWheelSlipping = true;
                    }
                }
            }

            bool hasGroundContact = groundedWheelCount > 0;
            float groundRatio = totalWheels > 0 ? (float)groundedWheelCount / totalWheels : 1f;

            // Update individual rover terrain interaction
            if (roverId == "husky")
            {
                UpdateHuskyTerrain(speed, hasGroundContact, groundRatio, anyWheelSlipping, gDelta);
            }
            else if (roverId == "m20")
            {
                UpdateM20Terrain(speed, hasGroundContact, groundRatio, anyWheelSlipping, gDelta);
            }
            else if (roverId == "m2020")
            {
                UpdateM2020Terrain(speed, hasGroundContact, groundRatio, anyWheelSlipping, gDelta);
            }

            ApplySmoothedVolumes();
        }

        private TerrainMaterialManager.SurfaceMaterialType lastSurfaceType = TerrainMaterialManager.SurfaceMaterialType.MartianRust;

        private void CheckSurfaceMaterialChange()
        {
            if (TerrainMaterialManager.Instance != null && TerrainMaterialManager.Instance.activeMaterialType != lastSurfaceType)
            {
                lastSurfaceType = TerrainMaterialManager.Instance.activeMaterialType;
                ConfigureSurfaceClips();
            }
        }

        // =========================================================================
        // ROVER 1: HUSKY A200
        // =========================================================================
        private void UpdateHuskyTerrain(float speed, bool grounded, float groundRatio, bool isSlipping, float gDelta)
        {
            float maxSpd = profile != null ? profile.maxSpeed : 1.0f;
            bool isRolling = grounded && speed > 0.02f;

            if (isRolling)
            {
                float normalizedSpeed = Mathf.Clamp01(speed / maxSpd);
                targetRollVolume = Mathf.Lerp(0.04f, maxRollVolume, normalizedSpeed) * groundRatio;
                float targetPitch = Mathf.Lerp(0.90f, 1.20f, normalizedSpeed);
                rollAudioSource.pitch = Mathf.Lerp(rollAudioSource.pitch, targetPitch, Time.deltaTime * 4f);
            }
            else
            {
                targetRollVolume = 0f;
            }

            // Wheel slip / regolith churn
            targetSlipVolume = (isSlipping && grounded && speed > 0.01f) ? maxSlipVolume : 0f;

            // Rough terrain bump / regolith thud
            if (grounded && gDelta > 0.24f && CanPlayImpact())
            {
                PlayImpact(bumpSoftClip, Mathf.Clamp(gDelta * 0.4f, 0.08f, maxImpactVolume));
            }
        }

        // =========================================================================
        // ROVER 2: DEEP ROBOTICS M20
        // =========================================================================
        private void UpdateM20Terrain(float speed, bool grounded, float groundRatio, bool isSlipping, float gDelta)
        {
            float maxSpd = profile != null ? profile.maxSpeed : 2.5f;
            bool isRolling = grounded && speed > 0.02f;

            if (isRolling)
            {
                float normalizedSpeed = Mathf.Clamp01(speed / maxSpd);
                targetRollVolume = Mathf.Lerp(0.05f, maxRollVolume, normalizedSpeed) * groundRatio;
                float targetPitch = Mathf.Lerp(0.92f, 1.22f, normalizedSpeed);
                rollAudioSource.pitch = Mathf.Lerp(rollAudioSource.pitch, targetPitch, Time.deltaTime * 4f);
            }
            else
            {
                targetRollVolume = 0f;
            }

            // Slip
            targetSlipVolume = (isSlipping && grounded && speed > 0.01f) ? maxSlipVolume : 0f;

            // Leg suspension shock absorption over rough ridges
            if (grounded && gDelta > 0.20f && CanPlayImpact())
            {
                PlayImpact(legShockClip, Mathf.Clamp(gDelta * 0.45f, 0.08f, maxImpactVolume));
            }
        }

        // =========================================================================
        // ROVER 3: NASA PERSEVERANCE M2020
        // =========================================================================
        private void UpdateM2020Terrain(float speed, bool grounded, float groundRatio, bool isSlipping, float gDelta)
        {
            float maxSpd = profile != null ? profile.maxSpeed : 0.15f;
            bool isRolling = grounded && speed > 0.006f;

            if (isRolling)
            {
                float normalizedSpeed = Mathf.Clamp01(speed / maxSpd);
                targetRollVolume = Mathf.Lerp(0.06f, maxRollVolume, normalizedSpeed) * groundRatio;
                float targetPitch = Mathf.Lerp(0.90f, 1.15f, normalizedSpeed);
                rollAudioSource.pitch = Mathf.Lerp(rollAudioSource.pitch, targetPitch, Time.deltaTime * 4f);
            }
            else
            {
                targetRollVolume = 0f;
            }

            // Slip
            targetSlipVolume = (isSlipping && grounded && speed > 0.005f) ? maxSlipVolume : 0f;

            // Rocker-Bogie Linkage Articulation
            float jointActivity = GetPerseveranceJointActivity();
            if (grounded && isRolling && jointActivity > 0.03f)
            {
                targetArticulationVolume = Mathf.Lerp(0.04f, maxArticulationVolume, Mathf.Clamp01(jointActivity * 3.0f));
            }
            else
            {
                targetArticulationVolume = 0f;
            }

            // Metal wheel boulder / rock obstacle clunk
            if (grounded && gDelta > 0.16f && CanPlayImpact())
            {
                PlayImpact(bumpMetalClip, Mathf.Clamp(gDelta * 0.5f, 0.08f, maxImpactVolume));
            }
        }

        private float GetPerseveranceJointActivity()
        {
            float maxJointVel = 0f;
            if (rockerLeftBody != null && rockerLeftBody.jointVelocity.dofCount > 0)
                maxJointVel = Mathf.Max(maxJointVel, Mathf.Abs(rockerLeftBody.jointVelocity[0]));
            if (rockerRightBody != null && rockerRightBody.jointVelocity.dofCount > 0)
                maxJointVel = Mathf.Max(maxJointVel, Mathf.Abs(rockerRightBody.jointVelocity[0]));
            if (bogieLeftBody != null && bogieLeftBody.jointVelocity.dofCount > 0)
                maxJointVel = Mathf.Max(maxJointVel, Mathf.Abs(bogieLeftBody.jointVelocity[0]));
            if (bogieRightBody != null && bogieRightBody.jointVelocity.dofCount > 0)
                maxJointVel = Mathf.Max(maxJointVel, Mathf.Abs(bogieRightBody.jointVelocity[0]));
            return maxJointVel;
        }

        // =========================================================================
        // AUDIO HELPERS & THROTTLING
        // =========================================================================
        private bool CanPlayImpact()
        {
            return (Time.time - lastImpactTime) >= impactCooldown;
        }

        private void PlayImpact(AudioClip clip, float volume)
        {
            if (clip == null || impactAudioSource == null) return;
            lastImpactTime = Time.time;
            impactAudioSource.pitch = UnityEngine.Random.Range(0.95f, 1.05f); // Subtle organic variation
            impactAudioSource.PlayOneShot(clip, Mathf.Clamp01(volume));
        }

        private void ApplySmoothedVolumes()
        {
            // Rolling source
            currentRollVolume = Mathf.MoveTowards(currentRollVolume, targetRollVolume, rollFadeSpeed * Time.deltaTime);
            if (rollAudioSource != null)
            {
                rollAudioSource.volume = currentRollVolume;
                if (currentRollVolume > 0.001f && !rollAudioSource.isPlaying && rollAudioSource.clip != null)
                {
                    rollAudioSource.Play();
                }
                else if (currentRollVolume <= 0.001f && rollAudioSource.isPlaying)
                {
                    rollAudioSource.Pause();
                }
            }

            // Slip source
            currentSlipVolume = Mathf.MoveTowards(currentSlipVolume, targetSlipVolume, rollFadeSpeed * 1.5f * Time.deltaTime);
            if (slipAudioSource != null)
            {
                slipAudioSource.volume = currentSlipVolume;
                if (currentSlipVolume > 0.001f && !slipAudioSource.isPlaying && slipAudioSource.clip != null)
                {
                    slipAudioSource.Play();
                }
                else if (currentSlipVolume <= 0.001f && slipAudioSource.isPlaying)
                {
                    slipAudioSource.Pause();
                }
            }

            // Articulation creak source
            currentArticulationVolume = Mathf.MoveTowards(currentArticulationVolume, targetArticulationVolume, rollFadeSpeed * Time.deltaTime);
            if (articulationAudioSource != null)
            {
                articulationAudioSource.volume = currentArticulationVolume;
                if (currentArticulationVolume > 0.001f && !articulationAudioSource.isPlaying && articulationAudioSource.clip != null)
                {
                    articulationAudioSource.Play();
                }
                else if (currentArticulationVolume <= 0.001f && articulationAudioSource.isPlaying)
                {
                    articulationAudioSource.Pause();
                }
            }
        }

        private void FadeOutAllAudio()
        {
            targetRollVolume = 0f;
            targetSlipVolume = 0f;
            targetArticulationVolume = 0f;
            ApplySmoothedVolumes();
        }

        public void StopAllTerrainAudio()
        {
            currentRollVolume = 0f;
            targetRollVolume = 0f;
            currentSlipVolume = 0f;
            targetSlipVolume = 0f;
            currentArticulationVolume = 0f;
            targetArticulationVolume = 0f;

            if (rollAudioSource != null) { rollAudioSource.volume = 0f; rollAudioSource.Stop(); }
            if (slipAudioSource != null) { slipAudioSource.volume = 0f; slipAudioSource.Stop(); }
            if (impactAudioSource != null) { impactAudioSource.volume = 0f; impactAudioSource.Stop(); }
            if (articulationAudioSource != null) { articulationAudioSource.volume = 0f; articulationAudioSource.Stop(); }
        }

        private void OnDestroy()
        {
            StopAllTerrainAudio();
            if (_activeInstance == this)
            {
                _activeInstance = null;
            }
        }
    }
}

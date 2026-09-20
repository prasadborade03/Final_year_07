using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using ProjectName.Rover;
using ProjectName.Terrain;

namespace ProjectName.Planetary
{
    /// <summary>
    /// Master Single Writer (Rule R3) for all planetary environment parameters.
    /// Controls:
    /// 1. Unity Physics.gravity
    /// 2. TerrainCollider PhysicMaterial (soil friction and restitution)
    /// 3. Active rover damping (as a live-safe multiplier on baseline damping, never an overwrite)
    /// 4. RenderSettings (procedural skybox, horizon-matched fog, ambient trilight, reflection)
    /// 5. Directional Light (solar elevation, azimuth, color, intensity, shadow strength)
    /// 6. WindZone parameters
    /// 7. DynamicGI.UpdateEnvironment()
    /// </summary>
    [DisallowMultipleComponent]
    public class PlanetEnvironmentController : MonoBehaviour
    {
        [Header("Scene References (Auto-acquired)")]
        public TerrainCollider terrainCollider;
        public UnityEngine.Terrain terrain;
        public Light directionalLight;
        public WindZone windZone;

        [Header("Active Environment")]
        public PlanetProfile currentProfile;

        private static PlanetEnvironmentController _instance;
        public static PlanetEnvironmentController Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = UnityEngine.Object.FindAnyObjectByType<PlanetEnvironmentController>();
                }
                return _instance;
            }
            protected set { _instance = value; }
        }

        public event Action<PlanetProfile> OnProfileApplied;

        private PhysicsMaterial planetaryPhysicMaterial;

        // Cache baseline damping for spawned rovers so scaling is purely a multiplier
        private readonly Dictionary<ArticulationBody, float> baselineLinearDamping = new Dictionary<ArticulationBody, float>();
        private readonly Dictionary<ArticulationBody, float> baselineAngularDamping = new Dictionary<ArticulationBody, float>();

        protected virtual void Awake()
        {
            if (Instance == null) Instance = this;
            RefreshSceneReferences();
        }

        protected virtual void OnEnable()
        {
            if (Instance == null) Instance = this;
            RefreshSceneReferences();
        }

        protected virtual void Start()
        {
            if (currentProfile != null)
            {
                ApplyProfile(currentProfile);
            }
        }

        public void RefreshSceneReferences()
        {
            if (terrain == null) terrain = UnityEngine.Terrain.activeTerrain ?? FindAnyObjectByType<UnityEngine.Terrain>();
            if (terrainCollider == null)
            {
                if (terrain != null) terrainCollider = terrain.GetComponent<TerrainCollider>();
                if (terrainCollider == null) terrainCollider = FindAnyObjectByType<TerrainCollider>();
            }

            if (directionalLight == null)
            {
                var lights = FindObjectsByType<Light>();
                foreach (var l in lights)
                {
                    if (l.type == LightType.Directional)
                    {
                        directionalLight = l;
                        break;
                    }
                }
            }

            if (windZone == null)
            {
                windZone = FindAnyObjectByType<WindZone>();
            }
        }

        /// <summary>
        /// Single-Writer Entry Point: Applies all parameters from the PlanetProfile into Unity.
        /// Live-safe: never accidentally re-enables gravity on a frozen rover.
        /// </summary>
        public void ApplyProfile(PlanetProfile profile)
        {
            if (profile == null)
            {
                Debug.LogWarning("[PlanetEnvironmentController] Attempted to apply null PlanetProfile.");
                return;
            }

            currentProfile = profile;
            RefreshSceneReferences();

            // 1. Gravity
            Physics.gravity = new Vector3(0f, -Mathf.Abs(profile.gravityY), 0f);

            // 2. Terrain Soil Friction & Restitution
            ApplyTerrainFriction(profile.soilStaticFriction, profile.soilDynamicFriction, profile.soilRestitution);

            // 3. Rover Damping Multiplier (Live-Safe)
            ApplyRoverDamping(profile.roverDragMultiplier);

            // 4. Directional Sunlight & Shadows
            ApplySunlight(profile);

            // 5. Skybox, Fog & Ambient Lighting
            ApplyAtmosphereAndSky(profile);

            // 6. Wind
            ApplyWind(profile.windMain, profile.windTurbulence, profile.windPulseMagnitude);

            // 7. Global Illumination Refresh
            DynamicGI.UpdateEnvironment();

            // 8. Planetary Surface Terrain Layers & Rocks (Phase 5B)
            ApplySurfaceAndRocks(profile);

            Debug.Log($"[PlanetEnvironmentController] Applied <color=#00E5FF><b>{profile.displayName}</b></color> (g={profile.gravityY:F2} m/s², Press={profile.pressureKPa} kPa, Temp={profile.surfaceTemperature}, Fog={profile.fogEnabled})");

            OnProfileApplied?.Invoke(profile);
        }

        private void ApplySurfaceAndRocks(PlanetProfile profile)
        {
            if (profile == null) return;
            var matManager = TerrainMaterialManager.Instance;
            if (matManager == null) matManager = FindFirstObjectByType<TerrainMaterialManager>();
            if (matManager != null)
            {
                matManager.ApplyPlanetaryProfile(profile);
            }
        }

        /// <summary>
        /// Applies environment parameters and respawns active rover at its last placement pose.
        /// </summary>
        public void ApplyAndRespawnRover(PlanetProfile profile)
        {
            ApplyProfile(profile);

            if (RoverPlacementController.Instance != null && ActiveRoverContext.HasActiveRover)
            {
                RoverPlacementController.Instance.RespawnAtLastPlacementPose();
                Debug.Log($"[PlanetEnvironmentController] Rover respawned at last placement pose in new {profile.displayName} environment.");
            }
            else
            {
                var sfc = FindAnyObjectByType<SimulationFlowController>();
                if (sfc != null) sfc.RespawnActiveRover();
            }
        }

        /// <summary>
        /// Configures Terrain PhysicMaterial. Invalids contact cache to ensure PhysX updates contact response.
        /// </summary>
        public void ApplyTerrainFriction(float staticF, float dynamicF, float bounce)
        {
            if (terrainCollider == null) RefreshSceneReferences();
            if (terrainCollider == null) return;

            if (planetaryPhysicMaterial == null)
            {
                planetaryPhysicMaterial = new PhysicsMaterial("PlanetarySoil_Dynamic")
                {
                    frictionCombine = PhysicsMaterialCombine.Multiply,
                    bounceCombine = PhysicsMaterialCombine.Average
                };
            }

            planetaryPhysicMaterial.staticFriction = Mathf.Clamp(staticF, 0.01f, 2.0f);
            planetaryPhysicMaterial.dynamicFriction = Mathf.Clamp(dynamicF, 0.01f, 2.0f);
            planetaryPhysicMaterial.bounciness = Mathf.Clamp01(bounce);

            // Force PhysX to invalidate contact cache
            terrainCollider.sharedMaterial = null;
            terrainCollider.sharedMaterial = planetaryPhysicMaterial;
            terrainCollider.material = planetaryPhysicMaterial;
        }

        /// <summary>
        /// Live-safe rover damping: scales each articulation body's baseline damping by dragMultiplier.
        /// Preserves frozen state and never overwrites tuned joint damping with 0.
        /// </summary>
        public void ApplyRoverDamping(float dragMultiplier)
        {
            float mult = Mathf.Max(0.01f, dragMultiplier);

            if (ActiveRoverContext.HasActiveRover)
            {
                var handle = ActiveRoverContext.Current;
                if (handle != null && handle.rootGameObject != null)
                {
                    var bodies = handle.rootGameObject.GetComponentsInChildren<ArticulationBody>(true);
                    foreach (var body in bodies)
                    {
                        if (body == null) continue;

                        if (!baselineLinearDamping.ContainsKey(body))
                        {
                            baselineLinearDamping[body] = Mathf.Max(body.linearDamping, 1.0f);
                            baselineAngularDamping[body] = Mathf.Max(body.angularDamping, 1.0f);
                        }

                        body.linearDamping = baselineLinearDamping[body] * mult;
                        body.angularDamping = baselineAngularDamping[body] * mult;

                        // Rule: Live-safe. Never enable gravity or unfreeze if the rover is currently frozen
                        if (handle.isFrozen || (body.isRoot && body.immovable))
                        {
                            body.useGravity = false;
                        }
                    }
                }
            }
        }

        private void ApplySunlight(PlanetProfile profile)
        {
            if (directionalLight == null) RefreshSceneReferences();
            if (directionalLight == null) return;

            directionalLight.color = profile.sunColor;
            directionalLight.intensity = Mathf.Max(0f, profile.sunIntensity);
            directionalLight.shadowStrength = Mathf.Clamp01(profile.shadowStrength);
            directionalLight.shadows = profile.shadowStrength > 0.05f ? LightShadows.Soft : LightShadows.None;

            // Orient sunlight according to planetary look bible default elevation and azimuth
            Quaternion sunRotation = Quaternion.Euler(profile.sunDefaultElevationDeg, profile.sunDefaultAzimuthDeg, 0f);
            directionalLight.transform.rotation = sunRotation;
        }

        private void ApplyAtmosphereAndSky(PlanetProfile profile)
        {
            // 1. Procedural Skybox
            if (profile.skyboxMaterial != null)
            {
                RenderSettings.skybox = profile.skyboxMaterial;
            }

            // 2. Ambient Trilight Lighting
            RenderSettings.ambientMode = profile.ambientMode;
            if (profile.ambientMode == AmbientMode.Trilight)
            {
                RenderSettings.ambientSkyColor = profile.ambientSkyColor * profile.ambientIntensity;
                RenderSettings.ambientEquatorColor = profile.ambientEquatorColor * profile.ambientIntensity;
                RenderSettings.ambientGroundColor = profile.ambientGroundColor * profile.ambientIntensity;
            }
            else
            {
                RenderSettings.ambientLight = profile.ambientSkyColor * profile.ambientIntensity;
            }
            RenderSettings.ambientIntensity = Mathf.Max(0f, profile.ambientIntensity);

            // 3. Reflections
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.reflectionIntensity = Mathf.Clamp01(profile.reflectionIntensity);

            // 4. Fog (Rule: fogColor == sky horizon color; Moon has fogEnabled = false)
            RenderSettings.fog = profile.fogEnabled;
            if (profile.fogEnabled)
            {
                RenderSettings.fogMode = profile.fogMode;
                RenderSettings.fogColor = profile.fogColor;
                RenderSettings.fogDensity = Mathf.Clamp(profile.fogDensity, 0f, 0.2f);
            }
            else
            {
                RenderSettings.fogDensity = 0f;
            }
        }

        private void ApplyWind(float speed, float turbulence, float pulseMagnitude)
        {
            if (windZone == null) windZone = FindAnyObjectByType<WindZone>();
            if (windZone == null) return;

            windZone.windMain = Mathf.Max(0f, speed);
            windZone.windTurbulence = Mathf.Max(0f, turbulence);
            windZone.windPulseMagnitude = Mathf.Max(0f, pulseMagnitude);
        }
    }
}

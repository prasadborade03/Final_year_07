using System;
using UnityEngine;

namespace ProjectName.Planetary
{
    /// <summary>
    /// Writes and applies planetary physical, atmospheric, and environmental parameters
    /// directly into Unity engine systems at runtime.
    /// Manages Physics.gravity, TerrainCollider PhysicMaterial, RenderSettings,
    /// Directional Light, WindZone, and active rover dynamics.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlanetaryParameterWriter : MonoBehaviour
    {
        [Header("Engine Scene References (Auto-acquired if null)")]
        public TerrainCollider terrainCollider;
        public UnityEngine.Terrain terrain;
        public Light directionalLight;
        public WindZone windZone;

        [Header("Active Planetary Profile")]
        public PlanetaryProfile currentProfile;

        public static PlanetaryParameterWriter Instance { get; private set; }

        public event Action<PlanetaryProfile> OnProfileApplied;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            RefreshSceneReferences();
        }

        private void OnEnable()
        {
            if (Instance == null) Instance = this;
            RefreshSceneReferences();
        }

        private void Start()
        {
            if (currentProfile != null)
            {
                ApplyProfile(currentProfile);
            }
        }

        public void RefreshSceneReferences()
        {
            if (terrain == null) terrain = FindAnyObjectByType<UnityEngine.Terrain>();
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
        /// Applies all parameters from the given profile into Unity engine systems.
        /// </summary>
        public void ApplyProfile(PlanetaryProfile profile)
        {
            if (profile == null)
            {
                Debug.LogWarning("[PlanetaryParameterWriter] Attempted to apply a null PlanetaryProfile.");
                return;
            }

            currentProfile = profile;
            RefreshSceneReferences();

            // 1. Unity Physics: Gravity
            ApplyGravity(profile.gravityY);

            // 2. Unity Physics: Terrain Friction & Restitution
            ApplyTerrainFriction(profile.terrainStaticFriction, profile.terrainDynamicFriction, profile.terrainBounciness);

            // 3. Unity Physics: Rover Atmospheric Drag & Damping
            ApplyRoverDrag(profile.roverDrag, profile.roverAngularDrag);

            // 4. Unity Rendering: Fog & Atmosphere
            ApplyAtmosphere(profile.fogDensity, profile.fogColor, profile.fogEnabled);

            // 5. Unity Rendering: Sun & Ambient Lighting
            ApplyLighting(profile.sunColor, profile.sunIntensity, profile.ambientColor, profile.ambientIntensity);

            // 6. Unity WindZone
            ApplyWind(profile.windMain, profile.windTurbulence, profile.windPulseMagnitude);

            // 7. Unity Rendering: Skybox
            if (profile.skyboxMaterial != null)
            {
                ApplySkybox(profile.skyboxMaterial);
            }

            Debug.Log($"[PlanetaryParameterWriter] Applied planetary profile: <color=#00E5FF><b>{profile.planetName}</b></color> (g={profile.gravityY:F2} m/s², drag={profile.roverDrag:F2}, fog={profile.fogDensity:F3})");

            OnProfileApplied?.Invoke(profile);
        }

        /// <summary>
        /// Writes directly to Unity Physics.gravity.
        /// </summary>
        public void ApplyGravity(float g)
        {
            float mag = Mathf.Abs(g);
            Physics.gravity = new Vector3(0f, -mag, 0f);
        }

        /// <summary>
        /// Writes surface friction and bounciness to the Terrain PhysicMaterial.
        /// </summary>
        public void ApplyTerrainFriction(float staticF, float dynamicF, float bounce)
        {
            if (terrainCollider == null)
            {
                if (terrain != null) terrainCollider = terrain.GetComponent<TerrainCollider>();
                if (terrainCollider == null) terrainCollider = FindAnyObjectByType<TerrainCollider>();
            }

            if (terrainCollider != null)
            {
                var mat = terrainCollider.sharedMaterial;
                if (mat == null)
                {
                    mat = new PhysicsMaterial("PlanetaryRegolith_Dynamic");
                    mat.frictionCombine = PhysicsMaterialCombine.Multiply;
                    mat.bounceCombine = PhysicsMaterialCombine.Average;
                    terrainCollider.sharedMaterial = mat;
                }

                mat.staticFriction = Mathf.Clamp(staticF, 0f, 2f);
                mat.dynamicFriction = Mathf.Clamp(dynamicF, 0f, 2f);
                mat.bounciness = Mathf.Clamp01(bounce);
            }
        }

        /// <summary>
        /// Writes atmospheric drag to active rover Rigidbody and ArticulationBody components.
        /// </summary>
        public void ApplyRoverDrag(float drag, float angularDrag)
        {
            float clampedDrag = Mathf.Max(0f, drag);
            float clampedAngularDrag = Mathf.Max(0f, angularDrag);

            // Apply to all active rovers in scene
            var rovers = GameObject.FindGameObjectsWithTag("Player");
            foreach (var r in rovers)
            {
                ApplyDragToGameObject(r, clampedDrag, clampedAngularDrag);
            }

            // Also check root articulation bodies
            var allAb = FindObjectsByType<ArticulationBody>();
            foreach (var ab in allAb)
            {
                ab.linearDamping = clampedDrag;
                ab.angularDamping = clampedAngularDrag;
            }
        }

        public void ApplyDragToGameObject(GameObject rover, float drag, float angularDrag)
        {
            if (rover == null) return;

            var rbs = rover.GetComponentsInChildren<Rigidbody>();
            foreach (var rb in rbs)
            {
                rb.linearDamping = drag;
                rb.angularDamping = angularDrag;
            }

            var abs = rover.GetComponentsInChildren<ArticulationBody>();
            foreach (var ab in abs)
            {
                ab.linearDamping = drag;
                ab.angularDamping = angularDrag;
            }
        }

        /// <summary>
        /// Writes atmospheric fog density and color to Unity RenderSettings.
        /// </summary>
        public void ApplyAtmosphere(float fogDensity, Color fogColor, bool fogEnabled)
        {
            RenderSettings.fog = fogEnabled;
            RenderSettings.fogDensity = Mathf.Clamp(fogDensity, 0f, 0.2f);
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
        }

        /// <summary>
        /// Writes lighting color and intensity to Directional Light and RenderSettings.
        /// </summary>
        public void ApplyLighting(Color sunColor, float sunIntensity, Color ambientColor, float ambientIntensity)
        {
            if (directionalLight != null)
            {
                directionalLight.color = sunColor;
                directionalLight.intensity = Mathf.Max(0f, sunIntensity);
            }

            RenderSettings.ambientLight = ambientColor;
            RenderSettings.ambientIntensity = Mathf.Max(0f, ambientIntensity);
        }

        /// <summary>
        /// Writes wind parameters to Unity WindZone component.
        /// </summary>
        public void ApplyWind(float speed, float turbulence, float pulseMagnitude)
        {
            if (windZone == null) windZone = FindAnyObjectByType<WindZone>();
            if (windZone == null) return;

            windZone.windMain = Mathf.Max(0f, speed);
            windZone.windTurbulence = Mathf.Max(0f, turbulence);
            windZone.windPulseMagnitude = Mathf.Max(0f, pulseMagnitude);
        }

        /// <summary>
        /// Writes skybox material to Unity RenderSettings.
        /// </summary>
        public void ApplySkybox(Material skyboxMat)
        {
            if (skyboxMat == null) return;
            RenderSettings.skybox = skyboxMat;
            DynamicGI.UpdateEnvironment();
        }
    }
}

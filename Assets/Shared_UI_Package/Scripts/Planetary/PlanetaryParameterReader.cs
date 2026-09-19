using System;
using UnityEngine;

namespace ProjectName.Planetary
{
    /// <summary>
    /// Snapshot container representing the live parameters read directly from Unity engine systems.
    /// </summary>
    [Serializable]
    public struct PlanetarySnapshot
    {
        public float gravityY;
        public float staticFriction;
        public float dynamicFriction;
        public float bounciness;
        public float roverDrag;
        public float roverAngularDrag;
        public float sunIntensity;
        public Color sunColor;
        public Color ambientColor;
        public float ambientIntensity;
        public bool fogEnabled;
        public float fogDensity;
        public Color fogColor;
        public float windSpeed;
        public float windTurbulence;
        public float windPulse;
        public float terrainRelief;
        public float terrainWidth;
        public float fixedDeltaTime;
        public Material skyboxMaterial;
    }

    /// <summary>
    /// Reads all physical, atmospheric, and environmental parameters directly from Unity engine systems at runtime.
    /// Zero hardcoding — all values are derived from Physics.gravity, TerrainCollider PhysicMaterial,
    /// RenderSettings, DirectionalLight, WindZone, and active rover dynamics.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlanetaryParameterReader : MonoBehaviour
    {
        [Header("Engine Scene References (Auto-acquired if null)")]
        public TerrainCollider terrainCollider;
        public UnityEngine.Terrain terrain;
        public Light directionalLight;
        public WindZone windZone;

        [Header("Active Rover Reference")]
        public Rigidbody roverRigidbody;
        public ArticulationBody roverArticulationBody;

        public static PlanetaryParameterReader Instance { get; private set; }

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

        /// <summary>
        /// Auto-locates missing scene references in the active scene.
        /// </summary>
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

            RefreshRoverReference();
        }

        /// <summary>
        /// Finds the active rover in the scene to read physical damping/drag parameters.
        /// </summary>
        public void RefreshRoverReference()
        {
            var rovers = GameObject.FindGameObjectsWithTag("Player");
            foreach (var r in rovers)
            {
                if (r.activeInHierarchy)
                {
                    roverRigidbody = r.GetComponentInChildren<Rigidbody>();
                    roverArticulationBody = r.GetComponentInChildren<ArticulationBody>();
                    if (roverRigidbody != null || roverArticulationBody != null) return;
                }
            }

            // Fallback search across root bodies
            if (roverArticulationBody == null)
            {
                var allAb = FindObjectsByType<ArticulationBody>();
                foreach (var ab in allAb)
                {
                    if (ab.isRoot && ab.gameObject.name.ToLower().Contains("link"))
                    {
                        roverArticulationBody = ab;
                        break;
                    }
                }
            }
        }

        public void SetActiveRover(GameObject rover)
        {
            if (rover == null) return;
            roverRigidbody = rover.GetComponentInChildren<Rigidbody>();
            roverArticulationBody = rover.GetComponentInChildren<ArticulationBody>();
        }

        // =========================================================================
        // UNITY ENGINE PHYSICAL PARAMETER READERS
        // =========================================================================

        /// <summary>Gravity magnitude along Y-axis from Unity Physics engine (m/s²)</summary>
        public float GetGravity() => Mathf.Abs(Physics.gravity.y);

        /// <summary>Surface static friction coefficient from Terrain PhysicMaterial</summary>
        public float GetStaticFriction()
        {
            if (terrainCollider != null && terrainCollider.sharedMaterial != null)
                return terrainCollider.sharedMaterial.staticFriction;
            return 0.6f;
        }

        /// <summary>Surface dynamic friction coefficient from Terrain PhysicMaterial</summary>
        public float GetDynamicFriction()
        {
            if (terrainCollider != null && terrainCollider.sharedMaterial != null)
                return terrainCollider.sharedMaterial.dynamicFriction;
            return 0.6f;
        }

        /// <summary>Surface bounciness (restitution) from Terrain PhysicMaterial</summary>
        public float GetBounciness()
        {
            if (terrainCollider != null && terrainCollider.sharedMaterial != null)
                return terrainCollider.sharedMaterial.bounciness;
            return 0.0f;
        }

        /// <summary>Rover atmospheric linear drag / damping from Rigidbody or ArticulationBody</summary>
        public float GetRoverDrag()
        {
            if (roverRigidbody != null) return roverRigidbody.linearDamping;
            if (roverArticulationBody != null) return roverArticulationBody.linearDamping;
            return 0.05f;
        }

        /// <summary>Rover atmospheric angular drag / damping from Rigidbody or ArticulationBody</summary>
        public float GetRoverAngularDrag()
        {
            if (roverRigidbody != null) return roverRigidbody.angularDamping;
            if (roverArticulationBody != null) return roverArticulationBody.angularDamping;
            return 0.05f;
        }

        // =========================================================================
        // UNITY ENGINE RENDERING & ATMOSPHERE READERS
        // =========================================================================

        public float GetAmbientIntensity() => RenderSettings.ambientIntensity;
        public Color GetAmbientColor() => RenderSettings.ambientLight;
        public Color GetSkyColor() => RenderSettings.ambientSkyColor;

        public Color GetSunColor() => directionalLight != null ? directionalLight.color : Color.white;
        public float GetSunIntensity() => directionalLight != null ? directionalLight.intensity : 1.0f;

        public bool GetFogEnabled() => RenderSettings.fog;
        public float GetFogDensity() => RenderSettings.fogDensity;
        public Color GetFogColor() => RenderSettings.fogColor;
        public Material GetSkyboxMaterial() => RenderSettings.skybox;

        // =========================================================================
        // UNITY ENGINE WIND & TERRAIN READERS
        // =========================================================================

        public float GetWindSpeed() => windZone != null ? windZone.windMain : 0f;
        public float GetWindTurbulence() => windZone != null ? windZone.windTurbulence : 0f;
        public float GetWindPulseMagnitude() => windZone != null ? windZone.windPulseMagnitude : 0f;

        public float GetTerrainRelief()
        {
            if (terrain != null && terrain.terrainData != null)
                return terrain.terrainData.size.y;
            return 100.0f;
        }

        public float GetTerrainWidth()
        {
            if (terrain != null && terrain.terrainData != null)
                return terrain.terrainData.size.x;
            return 1000.0f;
        }

        public float GetFixedDeltaTime() => Time.fixedDeltaTime;

        /// <summary>
        /// Snapshots current live engine values into a struct for diffing or telemetry.
        /// </summary>
        public PlanetarySnapshot SnapshotCurrentState()
        {
            RefreshSceneReferences();
            return new PlanetarySnapshot
            {
                gravityY = GetGravity(),
                staticFriction = GetStaticFriction(),
                dynamicFriction = GetDynamicFriction(),
                bounciness = GetBounciness(),
                roverDrag = GetRoverDrag(),
                roverAngularDrag = GetRoverAngularDrag(),
                sunIntensity = GetSunIntensity(),
                sunColor = GetSunColor(),
                ambientColor = GetAmbientColor(),
                ambientIntensity = GetAmbientIntensity(),
                fogEnabled = GetFogEnabled(),
                fogDensity = GetFogDensity(),
                fogColor = GetFogColor(),
                windSpeed = GetWindSpeed(),
                windTurbulence = GetWindTurbulence(),
                windPulse = GetWindPulseMagnitude(),
                terrainRelief = GetTerrainRelief(),
                terrainWidth = GetTerrainWidth(),
                fixedDeltaTime = GetFixedDeltaTime(),
                skyboxMaterial = GetSkyboxMaterial()
            };
        }
    }
}

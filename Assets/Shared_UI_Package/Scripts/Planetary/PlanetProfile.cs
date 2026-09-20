using System;
using UnityEngine;
using UnityEngine.Rendering;
using ProjectName.Terrain;

namespace ProjectName.Planetary
{
    /// <summary>
    /// Authoritative ScriptableObject defining the physical, atmospheric, visual,
    /// and observational parameters for a planetary body (Mars, Earth, Moon, Titan, Venus).
    /// Grounded in NASA NSSDCA planetary fact sheets.
    /// </summary>
    [CreateAssetMenu(fileName = "NewPlanetProfile", menuName = "Planetary Sim/Planet Profile (Realistic)", order = 90)]
    public class PlanetProfile : ScriptableObject
    {
        [Header("Identity & Sources")]
        [Tooltip("Identifier and display name of the celestial body")]
        public string planetName = "Mars";

        [Tooltip("Formatted human-readable title")]
        public string displayName = "Mars";

        [Tooltip("Icon or badge displayed in the planet selection modal and HUD")]
        public Sprite planetIcon;

        [TextArea(2, 4)]
        [Tooltip("Brief physical and atmospheric description")]
        public string description = "The Red Planet: Thin carbon dioxide atmosphere, low gravity, rocky dusty terrain.";

        [Tooltip("If true, this profile is automatically loaded at simulation startup")]
        public bool isDefault = false;

        [TextArea(1, 3)]
        [Tooltip("Source URL and verification reference from NASA NSSDCA / mission data")]
        public string nasaFactSheetUrl = "https://nssdc.gsfc.nasa.gov/planetary/factsheet/marsfact.html";

        [TextArea(1, 3)]
        [Tooltip("Technical notes and citations")]
        public string notes = "";

        [Header("1. Planetary Physics")]
        [Tooltip("Gravitational acceleration magnitude at surface (m/s²). Physics.gravity.y will be set to -gravityY")]
        [Range(0.1f, 30.0f)]
        public float gravityY = 3.72f;

        [Tooltip("Terrain soil static friction coefficient (PhysicMaterial.staticFriction)")]
        [Range(0.01f, 1.5f)]
        public float soilStaticFriction = 0.70f;

        [Tooltip("Terrain soil dynamic friction coefficient (PhysicMaterial.dynamicFriction)")]
        [Range(0.01f, 1.5f)]
        public float soilDynamicFriction = 0.55f;

        [Tooltip("Terrain soil restitution / bounciness (PhysicMaterial.bounciness)")]
        [Range(0.0f, 1.0f)]
        public float soilRestitution = 0.05f;

        [Tooltip("Multiplier applied to each rover's baseline joint/chassis damping (never an overwrite)")]
        [Range(0.01f, 5.0f)]
        public float roverDragMultiplier = 1.0f;

        [Tooltip("Base wind speed on planetary surface (WindZone.windMain)")]
        [Range(0.0f, 50.0f)]
        public float windMain = 4.5f;

        [Tooltip("Wind turbulence factor (WindZone.windTurbulence)")]
        [Range(0.0f, 5.0f)]
        public float windTurbulence = 1.2f;

        [Tooltip("Wind gust pulse magnitude (WindZone.windPulseMagnitude)")]
        [Range(0.0f, 5.0f)]
        public float windPulseMagnitude = 0.8f;

        [Header("2. Display Facts (Truthful HUD & Modal)")]
        [Tooltip("Surface temperature fact string (e.g. '≈ −60 °C' Mars, '+100 °C / −170 °C' Moon)")]
        public string surfaceTemperature = "≈ −60 °C";

        [Tooltip("Ambient surface temperature in Celsius for physics and motor thermal lumped modeling")]
        public float ambientTemperatureCelsius = -60.0f;

        [Tooltip("Atmospheric surface pressure in kilopascals (kPa). 0 = hard vacuum")]
        public float pressureKPa = 0.6f;

        [Tooltip("Solar irradiance at surface as a percentage of Earth (100% = 1361 W/m² top of atmosphere)")]
        [Range(0.0f, 100.0f)]
        public float sunlightPercentOfEarth = 43.0f;

        [Tooltip("Hazard title shown in HUD environment tile (replaces 'Dust Storm')")]
        public string hazardTitle = "DUST STORM";

        [Tooltip("Hazard status value shown in HUD environment tile")]
        public string hazardValue = "No";

        [Header("3. Procedural Skybox Look")]
        [Tooltip("Procedural skybox material utilizing Skybox/Procedural shader")]
        public Material skyboxMaterial;

        [Tooltip("Procedural sky: Sun disk mode (0 = None, 1 = Simple, 2 = HighQuality)")]
        public int sunDiskMode = 1;

        [Tooltip("Procedural sky: Apparent angular diameter of the sun")]
        [Range(0.0f, 0.2f)]
        public float sunSize = 0.035f;

        [Tooltip("Procedural sky: Sun size convergence")]
        [Range(1.0f, 20.0f)]
        public float sunSizeConvergence = 5.0f;

        [Tooltip("Procedural sky: Atmospheric rayleigh thickness")]
        [Range(0.0f, 5.0f)]
        public float atmosphereThickness = 0.70f;

        [Tooltip("Procedural sky: Atmospheric sky color tint")]
        public Color skyTint = new Color(0.77f, 0.48f, 0.28f, 1.0f); // Butterscotch / tan

        [Tooltip("Procedural sky: Lower ground hemisphere color")]
        public Color groundColor = new Color(0.42f, 0.22f, 0.09f, 1.0f);

        [Tooltip("Procedural sky: Overall skybox exposure")]
        [Range(0.1f, 4.0f)]
        public float skyExposure = 0.85f;

        [Header("4. Direct Solar Lighting")]
        [Tooltip("Directional sunlight color")]
        public Color sunColor = new Color(1.0f, 0.90f, 0.78f, 1.0f);

        [Tooltip("Directional sunlight intensity multiplier")]
        [Range(0.0f, 5.0f)]
        public float sunIntensity = 0.95f;

        [Tooltip("Default solar elevation angle above horizon in degrees (e.g. 35° Mars, 25° Moon)")]
        [Range(0.0f, 90.0f)]
        public float sunDefaultElevationDeg = 35.0f;

        [Tooltip("Default solar compass heading (azimuth) in degrees (0° = North, 90° = East)")]
        [Range(0.0f, 360.0f)]
        public float sunDefaultAzimuthDeg = 145.0f;

        [Tooltip("Shadow intensity / strength (1.0 = pitch black shadows, 0.0 = no shadows)")]
        [Range(0.0f, 1.0f)]
        public float shadowStrength = 0.80f;

        [Header("5. Ambient Lighting & Reflections")]
        [Tooltip("Ambient lighting mode (Trilight, Flat, or Skybox)")]
        public AmbientMode ambientMode = AmbientMode.Trilight;

        [Tooltip("Ambient upper sky light color")]
        public Color ambientSkyColor = new Color(0.35f, 0.22f, 0.15f, 1.0f);

        [Tooltip("Ambient horizon / equator light color")]
        public Color ambientEquatorColor = new Color(0.25f, 0.15f, 0.10f, 1.0f);

        [Tooltip("Ambient ground bounce light color")]
        public Color ambientGroundColor = new Color(0.15f, 0.08f, 0.05f, 1.0f);

        [Tooltip("Overall ambient lighting intensity multiplier")]
        [Range(0.0f, 3.0f)]
        public float ambientIntensity = 1.0f;

        [Tooltip("Environment reflection intensity")]
        [Range(0.0f, 1.0f)]
        public float reflectionIntensity = 0.20f;

        [Header("6. Atmosphere & Distance Fog")]
        [Tooltip("Enable atmospheric distance fog. Must be FALSE for vacuum worlds like the Moon!")]
        public bool fogEnabled = true;

        [Tooltip("Fog mode. ExponentialSquared produces natural atmospheric density falloff")]
        public FogMode fogMode = FogMode.ExponentialSquared;

        [Tooltip("Fog color. RULE: fogColor MUST equal sky horizon color so distant terrain blends seamlessly!")]
        public Color fogColor = new Color(0.77f, 0.48f, 0.28f, 1.0f);

        [Tooltip("Atmospheric fog density")]
        [Range(0.0f, 0.1f)]
        public float fogDensity = 0.006f;

        [Header("7. Default Surface Material Preset")]
        [Tooltip("Default PBR planetary surface material preset for Phase 5B terrain")]
        public PlanetaryMaterialType defaultMaterialPreset = PlanetaryMaterialType.MartianDust;

        [Header("8. Surface Rocks & Pebbles")]
        [Tooltip("Scatter density of rocks and pebbles across the terrain (0 = none, 1 = dense field)")]
        [Range(0.0f, 1.0f)]
        public float rockDensity = 0.25f;

        [Tooltip("Color tint applied to rocks to match planetary regolith / basalt tone")]
        public Color rockColorTint = Color.white;

        [Tooltip("Fraction of rocks that are large boulders (>= 0.5m) with convex colliders (0.0 to 0.5)")]
        [Range(0.0f, 0.5f)]
        public float boulderFraction = 0.15f;

        [Tooltip("Safe clearance radius around rover spawn point where no rocks will be spawned (meters)")]
        [Range(5.0f, 50.0f)]
        public float spawnClearanceRadius = 15.0f;

        [Header("Legacy Compatibility Fields")]
        public float terrainStaticFriction { get => soilStaticFriction; set => soilStaticFriction = value; }
        public float terrainDynamicFriction { get => soilDynamicFriction; set => soilDynamicFriction = value; }
        public float terrainBounciness { get => soilRestitution; set => soilRestitution = value; }
        public float roverDrag { get => roverDragMultiplier; set => roverDragMultiplier = value; }
        public float roverAngularDrag = 0.05f;
        public Color ambientColor { get => ambientSkyColor; set => ambientSkyColor = value; }
        public float sunElevationDeg { get => sunDefaultElevationDeg; set => sunDefaultElevationDeg = value; }
        public float sunAzimuthDeg { get => sunDefaultAzimuthDeg; set => sunDefaultAzimuthDeg = value; }
        public Texture2D heightmapPreset;
        public float terrainHeightScale = 120.0f;
    }
}

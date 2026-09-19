using System;
using UnityEngine;

namespace ProjectName.Planetary
{
    /// <summary>
    /// ScriptableObject storing pure Unity-native parameters for a planetary profile.
    /// All values are configured and editable in the Unity Inspector — zero hardcoded physics in scripts.
    /// </summary>
    [CreateAssetMenu(fileName = "NewPlanetProfile", menuName = "Planetary Sim/Planet Profile", order = 100)]
    public class PlanetaryProfile : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Display name of the planet, moon, or celestial body")]
        public string planetName = "Mars";

        [Tooltip("Icon or badge displayed in the planet selection UI")]
        public Sprite planetIcon;

        [TextArea(2, 4)]
        [Tooltip("Brief planetary description (atmospheric composition, terrain characteristics)")]
        public string description = "The Red Planet: Thin carbon dioxide atmosphere, low gravity, rocky dusty terrain.";

        [Tooltip("If true, this profile is automatically applied when the simulation starts")]
        public bool isDefault = false;

        [Header("Unity Physics Settings")]
        [Tooltip("Gravitational acceleration magnitude (m/s²). Unity Physics.gravity.y will be set to -gravityY")]
        [Range(0.1f, 30.0f)]
        public float gravityY = 3.72f;

        [Tooltip("Terrain surface static friction (PhysicMaterial.staticFriction)")]
        [Range(0.0f, 1.5f)]
        public float terrainStaticFriction = 0.70f;

        [Tooltip("Terrain surface dynamic friction (PhysicMaterial.dynamicFriction)")]
        [Range(0.0f, 1.5f)]
        public float terrainDynamicFriction = 0.55f;

        [Tooltip("Terrain surface bounciness / coefficient of restitution (PhysicMaterial.bounciness)")]
        [Range(0.0f, 1.0f)]
        public float terrainBounciness = 0.05f;

        [Tooltip("Atmospheric linear drag applied to rover bodies (Rigidbody.drag / ArticulationBody.linearDamping)")]
        [Range(0.0f, 2.0f)]
        public float roverDrag = 0.02f;

        [Tooltip("Atmospheric angular drag applied to rover bodies (Rigidbody.angularDrag / ArticulationBody.angularDamping)")]
        [Range(0.0f, 2.0f)]
        public float roverAngularDrag = 0.05f;

        [Header("Unity Rendering Settings")]
        [Tooltip("Directional sun light color")]
        public Color sunColor = new Color(1.0f, 0.92f, 0.82f, 1.0f);

        [Tooltip("Directional sun light intensity")]
        [Range(0.0f, 5.0f)]
        public float sunIntensity = 0.85f;

        [Tooltip("Ambient environment light color (RenderSettings.ambientLight)")]
        public Color ambientColor = new Color(0.12f, 0.08f, 0.06f, 1.0f);

        [Tooltip("Ambient environment intensity multiplier (RenderSettings.ambientIntensity)")]
        [Range(0.0f, 2.0f)]
        public float ambientIntensity = 0.80f;

        [Tooltip("Atmospheric haze / dust fog color (RenderSettings.fogColor)")]
        public Color fogColor = new Color(0.72f, 0.45f, 0.30f, 1.0f);

        [Tooltip("Atmospheric haze / dust fog density (RenderSettings.fogDensity)")]
        [Range(0.0f, 0.15f)]
        public float fogDensity = 0.008f;

        [Tooltip("Enable atmospheric fog (RenderSettings.fog)")]
        public bool fogEnabled = true;

        [Tooltip("Skybox material for this celestial body (RenderSettings.skybox)")]
        public Material skyboxMaterial;

        [Header("Unity WindZone Settings")]
        [Tooltip("Base wind speed on planetary surface (WindZone.windMain)")]
        [Range(0.0f, 50.0f)]
        public float windMain = 4.5f;

        [Tooltip("Wind turbulence factor (WindZone.windTurbulence)")]
        [Range(0.0f, 5.0f)]
        public float windTurbulence = 1.2f;

        [Tooltip("Wind gust pulse magnitude (WindZone.windPulseMagnitude)")]
        [Range(0.0f, 5.0f)]
        public float windPulseMagnitude = 0.8f;

        [Header("Terrain Relief & Presets")]
        [Tooltip("Optional pre-configured heightmap image from Assets/project/data/Heightmaps/")]
        public Texture2D heightmapPreset;

        [Tooltip("Vertical height scale / relief (Terrain.terrainData.size.y)")]
        [Range(20f, 500f)]
        public float terrainHeightScale = 120.0f;
    }
}

using UnityEngine;
using UnityEditor;
using ProjectName.Planetary;
using ProjectName.Terrain;

public static class PlanetProfileSetupUtility
{
    [MenuItem("Tools/Planetary/Setup All Planet Profiles")]
    public static string SetupAllProfiles()
    {
        string[] folders = new string[] {
            "Assets/Shared_UI_Package/Planetary_Profiles",
            "Assets/Shared_UI_Package/Resources/Planets"
        };

        foreach (var folder in folders)
        {
            SetupMars(folder + "/Mars.asset");
            SetupEarth(folder + "/Earth.asset");
            SetupMoon(folder + "/Moon.asset");
            SetupTitan(folder + "/Titan.asset");
            SetupVenus(folder + "/Venus.asset");
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("<color=green><b>[PlanetProfileSetupUtility]</b> Successfully configured and saved all 10 planet profile assets.</color>");
        return "SUCCESS";
    }

    private static void SetupMars(string path)
    {
        PlanetaryProfile p = GetOrCreateProfile(path);
        p.planetName = "Mars";
        p.displayName = "Mars";
        p.isDefault = true;
        p.gravityY = 3.72f;
        p.terrainStaticFriction = 0.70f;
        p.terrainDynamicFriction = 0.55f;
        p.terrainBounciness = 0.05f;
        p.roverDrag = 0.02f;
        p.roverDragMultiplier = 1.0f;
        p.roverAngularDrag = 0.05f;

        p.surfaceTemperature = "≈ −60 °C";
        p.pressureKPa = 0.636f;
        p.sunlightPercentOfEarth = 43.0f;
        p.hazardTitle = "DUST STORM";
        p.hazardValue = "No";

        p.skyboxMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Shared_UI_Package/Materials/Skies/Sky_Mars.mat");
        p.sunColor = new Color(1.0f, 0.88f, 0.76f, 1f);
        p.sunIntensity = 0.75f;
        p.sunElevationDeg = 35.0f;
        p.sunAzimuthDeg = 50.0f;
        p.sunSize = 0.35f;
        p.shadowStrength = 0.85f;

        p.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        p.ambientSkyColor = new Color(0.35f, 0.22f, 0.14f, 1f);
        p.ambientEquatorColor = new Color(0.24f, 0.16f, 0.12f, 1f);
        p.ambientGroundColor = new Color(0.14f, 0.10f, 0.08f, 1f);
        p.ambientIntensity = 1.0f;
        p.reflectionIntensity = 0.5f;

        p.fogColor = new Color(0.72f, 0.48f, 0.32f, 1f);
        p.fogDensity = 0.003f;
        p.fogEnabled = true;

        p.defaultMaterialPreset = PlanetaryMaterialType.MartianDust;
        p.description = "The Red Planet: Low gravity, thin CO2 atmosphere, red iron oxide dust, cold desert conditions.";
        p.nasaFactSheetUrl = "https://nssdc.gsfc.nasa.gov/planetary/factsheet/marsfact.html";
        p.notes = "NASA Mars Fact Sheet: Gravity 3.72 m/s2, mean temp -60C, surface pressure 6.36 mb (~0.64 kPa), solar irradiance ~590 W/m2 (~43% Earth).";

        EditorUtility.SetDirty(p);
    }

    private static void SetupEarth(string path)
    {
        PlanetaryProfile p = GetOrCreateProfile(path);
        p.planetName = "Earth";
        p.displayName = "Earth";
        p.isDefault = false;
        p.gravityY = 9.81f;
        p.terrainStaticFriction = 0.80f;
        p.terrainDynamicFriction = 0.60f;
        p.terrainBounciness = 0.10f;
        p.roverDrag = 0.05f;
        p.roverDragMultiplier = 1.5f;
        p.roverAngularDrag = 0.05f;

        p.surfaceTemperature = "≈ +15 °C";
        p.pressureKPa = 101.3f;
        p.sunlightPercentOfEarth = 100.0f;
        p.hazardTitle = "WEATHER";
        p.hazardValue = "Clear";

        p.skyboxMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Shared_UI_Package/Materials/Skies/Sky_Earth.mat");
        p.sunColor = new Color(1.0f, 0.98f, 0.92f, 1f);
        p.sunIntensity = 1.0f;
        p.sunElevationDeg = 55.0f;
        p.sunAzimuthDeg = 45.0f;
        p.sunSize = 0.53f;
        p.shadowStrength = 0.90f;

        p.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        p.ambientSkyColor = new Color(0.25f, 0.35f, 0.50f, 1f);
        p.ambientEquatorColor = new Color(0.30f, 0.35f, 0.38f, 1f);
        p.ambientGroundColor = new Color(0.18f, 0.15f, 0.12f, 1f);
        p.ambientIntensity = 1.0f;
        p.reflectionIntensity = 0.5f;

        p.fogColor = new Color(0.68f, 0.80f, 0.92f, 1f);
        p.fogDensity = 0.0012f;
        p.fogEnabled = true;

        p.defaultMaterialPreset = PlanetaryMaterialType.PolarIce;
        p.description = "Earth: Standard terrestrial gravity, 1 atm nitrogen-oxygen atmosphere, blue Rayleigh sky, temperate climate.";
        p.nasaFactSheetUrl = "https://nssdc.gsfc.nasa.gov/planetary/factsheet/earthfact.html";
        p.notes = "NASA Earth Fact Sheet: Gravity 9.807 m/s2, mean temp 15C, surface pressure 101.3 kPa, solar irradiance 1361 W/m2 (100%).";

        EditorUtility.SetDirty(p);
    }

    private static void SetupMoon(string path)
    {
        PlanetaryProfile p = GetOrCreateProfile(path);
        p.planetName = "Moon";
        p.displayName = "Moon";
        p.isDefault = false;
        p.gravityY = 1.62f;
        p.terrainStaticFriction = 0.90f;
        p.terrainDynamicFriction = 0.75f;
        p.terrainBounciness = 0.05f;
        p.roverDrag = 0.001f;
        p.roverDragMultiplier = 0.1f;
        p.roverAngularDrag = 0.01f;

        p.surfaceTemperature = "+100 °C / −170 °C";
        p.pressureKPa = 0.0f;
        p.sunlightPercentOfEarth = 100.0f;
        p.hazardTitle = "VACUUM";
        p.hazardValue = "No weather";

        p.skyboxMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Shared_UI_Package/Materials/Skies/Sky_Moon.mat");
        p.sunColor = new Color(1.0f, 1.0f, 0.98f, 1f);
        p.sunIntensity = 1.30f;
        p.sunElevationDeg = 25.0f;
        p.sunAzimuthDeg = 60.0f;
        p.sunSize = 0.53f;
        p.shadowStrength = 1.0f;

        p.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        p.ambientSkyColor = new Color(0.04f, 0.04f, 0.05f, 1f);
        p.ambientEquatorColor = new Color(0.02f, 0.02f, 0.03f, 1f);
        p.ambientGroundColor = new Color(0.01f, 0.01f, 0.01f, 1f);
        p.ambientIntensity = 1.0f;
        p.reflectionIntensity = 0.5f;

        p.fogColor = new Color(0.0f, 0.0f, 0.0f, 1f);
        p.fogDensity = 0.0f;
        p.fogEnabled = false;

        p.defaultMaterialPreset = PlanetaryMaterialType.LunarRegolith;
        p.description = "Earth's Moon: Hard vacuum, 1/6th Earth gravity, highly abrasive lunar regolith, high contrast stark shadows.";
        p.nasaFactSheetUrl = "https://nssdc.gsfc.nasa.gov/planetary/factsheet/moonfact.html";
        p.notes = "NASA Moon Fact Sheet: Gravity 1.62 m/s2, hard vacuum (3e-15 bar), surface temp diurnal cycle -170C to +120C, high contrast shadows.";

        EditorUtility.SetDirty(p);
    }

    private static void SetupTitan(string path)
    {
        PlanetaryProfile p = GetOrCreateProfile(path);
        p.planetName = "Titan";
        p.displayName = "Titan";
        p.isDefault = false;
        p.gravityY = 1.35f;
        p.terrainStaticFriction = 0.55f;
        p.terrainDynamicFriction = 0.40f;
        p.terrainBounciness = 0.08f;
        p.roverDrag = 0.15f;
        p.roverDragMultiplier = 3.5f;
        p.roverAngularDrag = 0.12f;

        p.surfaceTemperature = "≈ −179 °C";
        p.pressureKPa = 146.7f;
        p.sunlightPercentOfEarth = 1.0f;
        p.hazardTitle = "HAZE";
        p.hazardValue = "Methane drizzle";

        p.skyboxMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Shared_UI_Package/Materials/Skies/Sky_Titan.mat");
        p.sunColor = new Color(0.95f, 0.65f, 0.25f, 1f);
        p.sunIntensity = 0.20f;
        p.sunElevationDeg = 30.0f;
        p.sunAzimuthDeg = 40.0f;
        p.sunSize = 0.05f;
        p.shadowStrength = 0.35f;

        p.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        p.ambientSkyColor = new Color(0.40f, 0.22f, 0.08f, 1f);
        p.ambientEquatorColor = new Color(0.35f, 0.18f, 0.06f, 1f);
        p.ambientGroundColor = new Color(0.20f, 0.10f, 0.04f, 1f);
        p.ambientIntensity = 1.0f;
        p.reflectionIntensity = 0.5f;

        p.fogColor = new Color(0.68f, 0.42f, 0.16f, 1f);
        p.fogDensity = 0.008f;
        p.fogEnabled = true;

        p.defaultMaterialPreset = PlanetaryMaterialType.VolcanicBasalt;
        p.description = "Titan: Saturn's largest moon with dense nitrogen atmosphere, hydrocarbon lakes, methane cycle, cryogenic temperatures.";
        p.nasaFactSheetUrl = "https://nssdc.gsfc.nasa.gov/planetary/factsheet/saturnfact.html";
        p.notes = "NASA Titan Fact Sheet: Gravity 1.352 m/s2, surface temp 94 K (-179C), pressure 1.45 atm (146.7 kPa), dense nitrogen-methane photochemical smog.";

        EditorUtility.SetDirty(p);
    }

    private static void SetupVenus(string path)
    {
        PlanetaryProfile p = GetOrCreateProfile(path);
        p.planetName = "Venus";
        p.displayName = "Venus";
        p.isDefault = false;
        p.gravityY = 8.87f;
        p.terrainStaticFriction = 0.75f;
        p.terrainDynamicFriction = 0.60f;
        p.terrainBounciness = 0.02f;
        p.roverDrag = 0.35f;
        p.roverDragMultiplier = 6.0f;
        p.roverAngularDrag = 0.25f;

        p.surfaceTemperature = "≈ +465 °C";
        p.pressureKPa = 9200.0f;
        p.sunlightPercentOfEarth = 2.5f;
        p.hazardTitle = "ATMOSPHERE";
        p.hazardValue = "Corrosive / 92 bar";

        p.skyboxMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Shared_UI_Package/Materials/Skies/Sky_Venus.mat");
        p.sunColor = new Color(0.85f, 0.75f, 0.45f, 1f);
        p.sunIntensity = 0.25f;
        p.sunElevationDeg = 40.0f;
        p.sunAzimuthDeg = 45.0f;
        p.sunSize = 0.02f;
        p.shadowStrength = 0.30f;

        p.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        p.ambientSkyColor = new Color(0.42f, 0.38f, 0.20f, 1f);
        p.ambientEquatorColor = new Color(0.35f, 0.30f, 0.15f, 1f);
        p.ambientGroundColor = new Color(0.20f, 0.18f, 0.10f, 1f);
        p.ambientIntensity = 1.0f;
        p.reflectionIntensity = 0.5f;

        p.fogColor = new Color(0.58f, 0.52f, 0.28f, 1f);
        p.fogDensity = 0.009f;
        p.fogEnabled = true;

        p.defaultMaterialPreset = PlanetaryMaterialType.VolcanicBasalt;
        p.description = "Venus: Runaway greenhouse world, crushing 92 bar CO2 atmosphere, lead-melting surface heat, sulfuric acid haze.";
        p.nasaFactSheetUrl = "https://nssdc.gsfc.nasa.gov/planetary/factsheet/venusfact.html";
        p.notes = "NASA Venus Fact Sheet: Gravity 8.87 m/s2, mean temp 465C, pressure 92 bar (9200 kPa), thick sulfuric acid clouds, sunlight diffuse.";

        EditorUtility.SetDirty(p);
    }

    private static PlanetaryProfile GetOrCreateProfile(string path)
    {
        PlanetaryProfile p = AssetDatabase.LoadAssetAtPath<PlanetaryProfile>(path);
        if (p == null)
        {
            p = ScriptableObject.CreateInstance<PlanetaryProfile>();
            AssetDatabase.CreateAsset(p, path);
        }
        return p;
    }
}

# Project Architecture & File Structure

VR Planetary Rover Digital Twin Simulation Studio (`Final_year_07`)

> **Last updated**: September 2026 — updated through Phase 6 (thermal model, minimap, HUD cleanup)  
> **Branch**: `feature/studio-overhaul` · **Engine**: Unity 6.5 LTS · Universal Render Pipeline

---

## 1. System Architecture

```
                                  ┌───────────────────────────────┐
                                  │   UploadHeightmapUIToolkit    │
                                  │   (UI Toolkit Workbench)      │
                                  └───────────────┬───────────────┘
                                                  │ User input & tuning
                                                  ▼
                                  ┌───────────────────────────────┐
                                  │       HeightmapLoader         │
                                  │ (Procedural / Image -> Float) │
                                  └───────────────┬───────────────┘
                                                  │ float[,]
                                                  ▼
                                  ┌───────────────────────────────┐
                                  │       TerrainGenerator        │
                                  │  (Spawns Unity Terrain Mesh)  │
                                  └───────┬───────────────┬───────┘
                                          │               │
                     Live Terrain mesh    │               │ Terrain ready event
                                          ▼               ▼
           ┌──────────────────────────────────┐   ┌───────────────────────────────┐
           │     TerrainMaterialManager       │   │   SimulationFlowController    │
           │ (PBR Layers / Shaders / Textures)│   │  (Terrain -> Rover Placement) │
           └──────────────────────────────────┘   └───────────────┬───────────────┘
                                                                  │
                                                  ┌───────────────┴───────────────┐
                                                  ▼                               ▼
                                  ┌───────────────────────────────┐   ┌───────────────────────────────┐
                                  │        Rover Importers        │   │       Rover Controllers       │
                                  │ (Husky, M20, Perseverance)    │   │ (Tank / Ackermann / Crab / WASD)
                                  └───────────────────────────────┘   └───────────────────────────────┘
```

### Core Subsystems
1. **Terrain Pipeline**:
   - [`HeightmapLoader.cs`](file:///c:/Users/PRASAD%20BORADE/unity/Final_year_shit/Assets/project/script/Terrain/HeightmapLoader.cs): Converts PNG heightmaps or procedural algorithms (Gale Crater, Olympus Mons, Shackleton, Valles Marineris, Fractal) into normalized 2D float arrays (`float[,]`).
   - [`TerrainGenerator.cs`](file:///c:/Users/PRASAD%20BORADE/unity/Final_year_shit/Assets/project/script/Terrain/TerrainGenerator.cs): Builds the 3D Unity Terrain GameObject, applies elevation scale, base offset, dimensions, physics collider, and instanced rendering.
   - [`TerrainMaterialManager.cs`](file:///c:/Users/PRASAD%20BORADE/unity/Final_year_shit/Assets/project/script/Terrain/TerrainMaterialManager.cs): Pipeline-aware manager (Built-in & URP) configuring PBR TerrainLayers, surface presets, CC0 texture loading, and procedural fallbacks.
   - [`UploadHeightmapUIToolkit.cs`](file:///c:/Users/PRASAD%20BORADE/unity/Final_year_shit/Assets/project/script/Terrain/UploadHeightmapUIToolkit.cs): Interactive UI Toolkit workbench with real-time 2D canvas painting, sliders, brush tools, and preset buttons.

2. **Rover Robotics & Digital Twin Pipeline**:
   - **Husky A200**: 4-wheel differential drive rover with tank-steering input.
   - **Deep Robotics M20**: Wheeled quadruped robot with knee/hip articulation and wheel velocity drive.
   - **NASA Perseverance (M2020)**: 6-wheel rocker-bogie suspension with 4-wheel steering supporting 4 distinct drive modes (Ackermann, Point Turn, Crab, Tank).
   - [`M2020VisualMaterialManager.cs`](file:///c:/Users/PRASAD%20BORADE/unity/Final_year_shit/Assets/project/script/Rover/m2020_perseverance/M2020VisualMaterialManager.cs): Cosmetic NASA/JPL material assigner for the Perseverance rover.

3. **Simulation Orchestration**:
   - [`SimulationFlowController.cs`](file:///c:/Users/PRASAD%20BORADE/unity/Final_year_shit/Assets/project/script/Core/SimulationFlowController.cs): Coordinates the end-to-end user loop: Heightmap Tuning → Terrain Generation → Rover Selection → Placement / Drop → Active Driving.

---

## 2. Directory & File Structure

```
Assets/
├── project/                                     ← Primary project source and assets
│   │
│   ├── script/                                  ← C# Source Code
│   │   ├── Core/
│   │   │   └── SimulationFlowController.cs      ← Coordinates state transitions (Terrain -> Select -> Drive)
│   │   │
│   │   ├── Terrain/
│   │   │   ├── HeightmapLoader.cs               ← Image loader & procedural heightmap algorithm generators
│   │   │   ├── HeightmapTuningData.cs           ← Enums (Presets, MaterialTypes, Curves) & TerrainTuningConfig
│   │   │   ├── TerrainGenerator.cs              ← Instantiates Unity Terrain from float[,] height data
│   │   │   ├── TerrainMaterialManager.cs        ← Dual-pipeline PBR layer manager & texture auto-loader
│   │   │   ├── UploadHeightmapUIToolkit.cs      ← UI Toolkit controller for workbench HUD & 2D canvas painter
│   │   │   └── UploadHeightmapUI.cs             ← Legacy uGUI file dialog integration
│   │   │
│   │   ├── Rover/
│   │   │   ├── husky/
│   │   │   │   ├── RoverImporter_husky.cs       ← Spawns Clearpath Husky, sets up physics & wheel drives
│   │   │   │   └── RoverController_Husky.cs     ← WASD differential tank-steering input
│   │   │   │
│   │   │   ├── m20/
│   │   │   │   ├── RoverImporter_m20.cs         ← Spawns Deep Robotics M20 quadruped with leg joints
│   │   │   │   ├── RoverController_m20.cs       ← WASD drive + Q/E leg lift controller
│   │   │   │   └── M20_WheelDebug.cs            ← Diagnostic test script to spin wheel joints
│   │   │   │
│   │   │   └── m2020_perseverance/
│   │   │       ├── RoverImporter_m2020.cs       ← Rocker-bogie 6-wheel + 4-wheel steering articulation
│   │   │       ├── RoverController_m2020.cs     ← 4 drive modes: Ackermann / Point Turn / Crab / Tank
│   │   │       └── M2020VisualMaterialManager.cs← Assigns authentic NASA/JPL materials to M2020 parts
│   │   │
│   │   ├── Editor/                              ← Editor-only utilities
│   │   │   └── TerrainTagHelper.cs              ← Auto-registers 'Terrain' tag on load & auto-configures normal maps
│   │   │
│   │   ├── FreeFlyCamera.cs                     ← Dev camera: RMB-look + WASD fly navigation
│   │   └── OrbitCamera.cs                       ← Dev camera: RMB-orbit around a selected rover/target
│   │
│   ├── materials/                               ← Textures, skyboxes, and planetary materials
│   │   ├── HDR_multi_nebulae_2.hdr              ← Deep-space skybox HDR cubemap
│   │   ├── New Material.mat                     ← Active skybox material assigned to RenderSettings
│   │   └── [Downloaded CC0 Textures]            ← Place MartianRust_*, LunarRegolith_*, etc. here
│   │
│   ├── data/
│   │   ├── Heightmaps/
│   │   │   └── test01.png                       ← Sample heightmap test image
│   │   │
│   │   └── URDF/                                ← Robot URDF models, meshes, materials, and prefabs
│   │       ├── husky_unity/husky.prefab         ← Clearpath Husky A200 prefab & materials
│   │       ├── M20_Unity/M20.prefab             ← Deep Robotics M20 prefab & materials
│   │       └── M2020_Unity/perseverance_m2020.prefab ← NASA Perseverance rover prefab & materials
│   │
│   ├── scenes/
│   │   ├── _Test/
│   │   │   └── Test_TerrainAndRover.unity       ← Master integrated scene (Terrain + Rovers + UI Toolkit)
│   │   ├── Test_Terrain.unity                   ← Terrain-only test scene
│   │   └── Module2.unity                        ← Perseverance M2020 standalone test scene
│   │
│   ├── rendering/                               ← Pipeline configuration
│   │   ├── URP_PipelineAsset.asset              ← Universal Render Pipeline asset (optional)
│   │   └── URP_Renderer.asset                   ← Universal Renderer configuration (optional)
│   │
│   └── package/
│       └── StandaloneFileBrowser/               ← Cross-platform native OS file dialog plugin (SFB)
│
├── ProjectSettings/
│   ├── TagManager.asset                         ← Includes registered 'Terrain', 'robot', 'ground' tags
│   ├── GraphicsSettings.asset                   ← Graphics pipeline settings
│   └── QualitySettings.asset                    ← Quality tiers and shadow configurations
│
└── Packages/manifest.json                       ← Package dependencies (URDF-Importer, URP, InputSystem, XR)
```

---

## 3. Render Pipeline & Materials Policy

1. **Current Pipeline**: Unity Built-in Render Pipeline.
2. **Rovers**: All rover meshes use the Built-in Standard shader (`fileID: 46`). They must remain isolated from pipeline-wide shader conversions to prevent magenta/pink artifacts.
3. **Terrain**: `TerrainMaterialManager.cs` dynamically assigns:
   - `Nature/Terrain/Standard` under Built-in Pipeline.
   - `Universal Render Pipeline/Terrain/Lit` under URP.
4. **CC0 Material Naming Convention**:
   Textures placed into `Assets/project/materials/` should follow:
   - Martian: `MartianRust_Albedo.png`, `MartianRust_Normal.png`, `MartianRust_Roughness.png`
   - Lunar: `LunarRegolith_Albedo.png`, `LunarRegolith_Normal.png`, `LunarRegolith_Roughness.png`
   - Basalt: `VolcanicBasalt_Albedo.png`, `VolcanicBasalt_Normal.png`, `VolcanicBasalt_Roughness.png`
   - Polar Ice: `PolarIce_Albedo.png`, `PolarIce_Normal.png`, `PolarIce_Roughness.png`
   - Sandstone: `Sandstone_Albedo.png`, `Sandstone_Normal.png`, `Sandstone_Roughness.png`

---

## 4. Planetary Environment System (Phase 5A+)

### ScriptableObject Profile Pattern

Each planet is represented by a [`PlanetProfile`](file:///c:/Users/PRASAD%20BORADE/unity/Final_year_shit/Assets/Shared_UI_Package/Scripts/Planetary/PlanetProfile.cs) ScriptableObject at `Assets/Shared_UI_Package/Resources/Planets/<Name>.asset`.

| Asset | Gravity (m/s²) | Temp (°C) | Pressure (kPa) | Drag ×  |
|---|---|---|---|---|
| `Mars.asset` | 3.72 | −60 | 0.636 | 1.0 |
| `Moon.asset` | 1.62 | −20 | ≈ 0 | 0.1 |
| `Venus.asset` | 8.87 | +465 | 9,200 | 6.0 |
| `Titan.asset` | 1.35 | −179 | 146.7 | 3.5 |
| `Earth.asset` | 9.81 | +15 | 101.3 | 1.5 |

**Loaded via**: `Resources.Load<PlanetProfile>("Planets/Mars")`

### Single-Writer Rule

[`PlanetEnvironmentController`](file:///c:/Users/PRASAD%20BORADE/unity/Final_year_shit/Assets/Shared_UI_Package/Scripts/Planetary/PlanetEnvironmentController.cs) is the **only class** that writes to `RenderSettings`, `Physics.gravity`, `WindZone`, or any scene lighting parameters. All other code reads from `PlanetEnvironmentController.Instance.currentProfile`.

`ApplyProfile(PlanetProfile p)` sequence:
1. `Physics.gravity = new Vector3(0, -p.gravityY, 0)`
2. `RenderSettings.skybox = p.skyboxMaterial`
3. Sun `Light`: color, intensity, elevation, azimuth, shadow strength
4. `RenderSettings.fog*`: mode, color, density
5. `RenderSettings.ambientMode`, sky/equator/ground colors
6. `WindZone`: main, turbulence, pulse magnitude
7. `TerrainMaterialManager.SetMaterialPreset(p.defaultMaterialPreset)`
8. `PlanetRockScatterer.Scatter(p)`: replaces all rocks

---

## 5. Rover Telemetry System (Phase 3+)

[`RoverTelemetry`](file:///c:/Users/PRASAD%20BORADE/unity/Final_year_shit/Assets/project/script/Rover/RoverTelemetry.cs) runs on `FixedUpdate()` (0.02 s). Key public fields consumed by the UI:

| Field | Type | Source |
|---|---|---|
| `linearSpeedMps` | float | `Vector3.Dot(rootBody.velocity, forward)` |
| `headingDeg` | float | `Atan2(forward.x, forward.z)` → 0–360° |
| `terrainSlopeDeg` | float | Raycast → `Vector3.Angle(hit.normal, up)` |
| `wheelStates[]` | `WheelState[]` | Per wheel: ω, τ, slipRatio, contact |
| `powerDrawWatts` | float | Avionics + wheel motors + grade + rolling |
| `batteryPercent` | float | `100 × (1 − E_consumed / E_capacity)` |
| `motorTempCelsius` | float | Lumped thermal ODE |
| `ambientTempCelsius` | float | Live from `PlanetProfile` |
| `energyConsumedWh` | float | Cumulative battery drain |

### Lumped Thermal Model

```
dT_motor/dt = (P_loss − (T_motor − T_ambient) / R_th) / C_th

P_loss  = 0.20 × P_wheels + 5 W
R_th    = 0.8 K/W
C_th    = 1500 J/K
T_ambient = PlanetEnvironmentController.Instance.currentProfile.ambientTemperatureCelsius
```

---

## 6. Phase Changelog Summary

| Phase | Title | Key Output |
|---|---|---|
| 0 | Audit & Setup | Branch created; legacy code understood; design decisions locked |
| 1 | UI Toolkit Foundation | UXML/USS studio shell; 3-column layout; tab system |
| 2 | Rover Integration | `ActiveRoverContext`; `RoverHandle`; `RoverPlacementController`; 4 M2020 drive modes |
| 3 | Live Telemetry | `RoverTelemetry`: real speed, heading, slope, slip — no fabricated values |
| 4 | Terrain Lab | Painter, procedural presets, undo/redo, PNG import/export |
| 5A | Planet Realism — Data | `PlanetProfile` + `PlanetEnvironmentController`; per-planet sky/light/fog/facts |
| 5B | Planet Realism — Surface | CC0 PBR textures, slope+height alphamap, rock scatterer |
| 6 | UI Cleanup + Thermal | Compass removed; lumped thermal model; 90% opacity; 24px stats; HUD toggle |

---

## 7. Key Files Quick Reference

| File | Role | Lines |
|---|---|---|
| [`UploadHeightmapUIToolkit.cs`](file:///c:/Users/PRASAD%20BORADE/unity/Final_year_shit/Assets/Shared_UI_Package/Scripts/Controllers/UploadHeightmapUIToolkit.cs) | Main UI controller | ~2382 |
| [`TerrainAndRoverUI.uxml`](file:///c:/Users/PRASAD%20BORADE/unity/Final_year_shit/Assets/Shared_UI_Package/UI_Toolkit/TerrainAndRoverUI.uxml) | Studio layout | ~512 |
| [`TerrainAndRoverUI.uss`](file:///c:/Users/PRASAD%20BORADE/unity/Final_year_shit/Assets/Shared_UI_Package/UI_Toolkit/TerrainAndRoverUI.uss) | All styles | ~1221 |
| [`RoverTelemetry.cs`](file:///c:/Users/PRASAD%20BORADE/unity/Final_year_shit/Assets/project/script/Rover/RoverTelemetry.cs) | Physics telemetry + thermal model | ~473 |
| [`PlanetProfile.cs`](file:///c:/Users/PRASAD%20BORADE/unity/Final_year_shit/Assets/Shared_UI_Package/Scripts/Planetary/PlanetProfile.cs) | ScriptableObject: all planet data | ~210 |
| [`PlanetEnvironmentController.cs`](file:///c:/Users/PRASAD%20BORADE/unity/Final_year_shit/Assets/Shared_UI_Package/Scripts/Planetary/PlanetEnvironmentController.cs) | Single writer for scene environment | ~310 |
| [`HeightmapLoader.cs`](file:///c:/Users/PRASAD%20BORADE/unity/Final_year_shit/Assets/project/script/Terrain/HeightmapLoader.cs) | Procedural algorithms + PNG import | ~660 |
| [`TerrainMaterialManager.cs`](file:///c:/Users/PRASAD%20BORADE/unity/Final_year_shit/Assets/project/script/Terrain/TerrainMaterialManager.cs) | PBR layers + slope/height blend | ~1100 |

> See [`RESEARCH_BLACK_BOOK.md`](file:///c:/Users/PRASAD%20BORADE/unity/Final_year_shit/RESEARCH_BLACK_BOOK.md) for full planetary science data, equations, and phase-by-phase development log.

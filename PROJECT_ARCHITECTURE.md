# Project Architecture & File Structure

VR Planetary Rover Digital Twin Simulation Studio (`Final_year_07`)

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

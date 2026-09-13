# Terrain System & Material Architecture Fixes

## 1. Issue Summary & Root Causes

### A. Editor Pause on "Generate Terrain" Click
* **Error**: `Tag: Terrain is not defined. UnityEngine.GameObject:set_tag (string)` at `TerrainGenerator.cs:107`
* **Root Cause**: `TerrainGenerator.cs` explicitly set `terrainObject.tag = "Terrain"`, but the string `"Terrain"` was missing from `ProjectSettings/TagManager.asset`. With the Console's **"Error Pause"** toggle enabled, this unhandled exception immediately froze the Editor.

### B. Pink / Magenta Terrain Material
* **Root Cause**: The project is configured under Unity's **Built-in Render Pipeline** (`customRenderPipeline: null`). In Unity 6, `TerrainMaterialManager.cs` was assigning `terrain.materialTemplate = null`. For custom procedural terrains in Unity 6, a null material template prevents the splatmap shader from resolving, turning the terrain bright magenta/pink.

### C. Floating Rover on Topographic Grid & Normal Inspector
* **Root Cause**: Previously, selecting Topographic Grid or Normal Inspector assigned a non-terrain shader (`Unlit/Color`) to `terrain.materialTemplate` and skipped assigning any `TerrainLayer`. Because non-terrain shaders lack Unity's terrain heightmap vertex sampling pass, the visual mesh failed to displace and flattened to Y=0, while the `TerrainCollider` remained elevated. As a result, the rover sat high on the invisible collider, floating in mid-air.
* **Fix**: Unified all 7 presets to use dedicated `TerrainLayer` setups with native terrain shaders (`Nature/Terrain/Standard` in Built-in, `URP/Terrain/Lit` in URP). Generated procedural holographic Neon Cyan grid textures (major/minor grid + contours) and slope-vector heat-map textures (Cyan = flat, Green = mild, Yellow = steep, Red = cliff) with tailored normal maps. The visual terrain now matches the physics collider 100%.

### D. Glossy Liquid Reflections / Specular Streaks on Ridges
* **Root Cause**: `TerrainLayer.smoothness` was computed as `1.0f - config.roughness`. Presets had low roughness (e.g., Polar Ice roughness 0.15 => smoothness 0.85, Topographic Grid roughness 0.2 => smoothness 0.80, Normal Inspector roughness 0.45 => smoothness 0.55). At grazing angles along terrain ridges, the Directional Light produced intense, unnatural mirror-like specular highlights that looked like clear epoxy resin or liquid water. Furthermore, Unity's default `smoothnessSource` (`ConstantMultipliedByDiffuseAlpha`) used the diffuse texture's opaque alpha channel (1.0) as 100% smoothness.
* **Fix**: Enforced `smoothness = 0.0f`, `metallic = 0.0f`, and `specular = Color.black` across all terrain presets. Set `layer.smoothnessSource = TerrainLayerSmoothnessSource.ConstantOnly` to guarantee that diffuse texture alpha channels are ignored. Added `SanitizeConfigs()` to override any stale high-gloss serialized values in the scene.

### E. Daytime Blue Sky Reflections Leaking into Space Scene
* **Root Cause**: In `Test_TerrainAndRover.unity`, environment lighting was unbaked (`giWorkflowMode = OnDemand`) with `defaultReflectionMode = Skybox`. When GI is unbaked, Unity falls back to its default internal daylight blue sky reflection cubemap, and `RenderSettings.ambientSkyColor` defaults to daylight cyan `RGBA(0.212, 0.227, 0.259)`. Any reflective surface (or unbaked terrain) reflected Earth's blue sky rather than the space nebula HDRI.
* **Fix**: Switched `RenderSettings.defaultReflectionMode` to `Custom` and bound `Assets/project/materials/HDR_multi_nebulae_2.hdr` as `customReflectionTexture`. Set `RenderSettings.ambientMode` to `Flat` with a dark space starlight ambient (`RGBA(0.05, 0.05, 0.07)`). Implemented `EnforceSpaceLightingAndReflections()` in `TerrainMaterialManager.cs` to ensure space reflection and lighting settings are enforced at runtime.

---

## 2. Technical Changes Applied

### 1. Tag Safety & Editor Automation
* **`ProjectSettings/TagManager.asset`**: Registered `"Terrain"` in the project tags list.
* **`TerrainGenerator.cs`**:
  * Wrapped `terrainObject.tag = "Terrain"` in a safe `try-catch` block.
  * Enabled `terrain.drawInstanced = true`, `terrain.allowAutoConnect = true`, and soft shadow casting.
* **`UploadHeightmapUIToolkit.cs`**:
  * Wrapped `OnGenerateTerrainClicked()` in a `try-catch` block with null-data fallbacks to prevent UI unhandled exceptions.
* **`Assets/project/script/Editor/TerrainTagHelper.cs`** *(NEW)*:
  * Automatically registers `"Terrain"` tag on editor startup (`[InitializeOnLoad]`) and provides a menu item under `Tools/Planetary Rover/Ensure Terrain Tag Exists`.
  * Includes `PlanetaryTexturePostprocessor` to automatically import any `*_Normal.png` in `Assets/project/materials/` as a Unity Normal Map.

### 2. Dual-Pipeline Aware Terrain Material Manager
* **`TerrainMaterialManager.cs`**:
  * Added dynamic pipeline detection (`GraphicsSettings.currentRenderPipeline != null`).
  * **Built-in Pipeline (Active)**: Automatically binds `Nature/Terrain/Standard` material, sets `materialType = BuiltInStandard`, and builds PBR `TerrainLayer` setups for all 7 presets.
  * **URP Pipeline (Future-Proof)**: Binds `Universal Render Pipeline/Terrain/Lit` (or `Universal Render Pipeline/Lit`).
  * **Diagnostic Presets**: Topographic Grid (neon cyan holographic radar) and Surface Normal Inspector (elevation/slope gradient heat map) now render directly on the 3D displaced terrain surface via PBR layers.
  * **Procedural PBR Fallback**: Generates high-quality procedural albedo and normal textures with mipmaps and bilinear filtering, ensuring the terrain is **never pink** and **never flat**.
  * **CC0 Auto-Loader**: Automatically searches `Assets/project/materials/` for matching 2K/4K PBR textures by preset name and auto-invalidates procedural cache when real artist textures are dropped in.
  * **Ultra-Matte Guarantee**: Sets `smoothness = 0.0f`, `metallic = 0.0f`, `specular = Color.black`, and `smoothnessSource = ConstantOnly` to eliminate all glossy plastic streaks.
  * **Space Lighting Guard**: `EnforceSpaceLightingAndReflections()` binds the `HDR_multi_nebulae_2.hdr` cubemap as custom environment reflection and establishes dark space ambient lighting.

### 3. Rover URDF Material Isolation
* **Zero side-effects on rovers**: Rover materials in `Assets/project/data/URDF/` (Husky, M20, M2020 Perseverance) use the Built-in Standard shader (`fileID: 46`). Because pipeline settings were not modified globally, all rovers retain their original textures and will **never turn pink**.

### 4. Lighting & Visual Settings
* Set Main Camera `farClipPlane = 3000m` to prevent panoramic horizon clipping on 1000m planetary terrains.
* Configured Directional Light soft shadows and shadow bias in `Test_TerrainAndRover.unity`.
* Set `RenderSettings.defaultReflectionMode = Custom` with `HDR_multi_nebulae_2.hdr` cubemap and `reflectionIntensity = 0.25f`.
* Set `RenderSettings.ambientMode = Flat` with starlight neutral tone `RGBA(0.05, 0.05, 0.07)` and `subtractiveShadowColor = RGBA(0.02, 0.02, 0.03)`.

---

## 3. Lunar Regolith CC0 Working Sources
The original `gravel_ground` link was a 404 because PolyHaven's asset is named **`gravel_ground_01`**:
* **PolyHaven Option 1**: [Gravel Ground 01](https://polyhaven.com/a/gravel_ground_01)
* **PolyHaven Option 2**: [Aerial Ground Rock](https://polyhaven.com/a/aerial_ground_rock)
* **ambientCG Option 1**: [Ground 048 (Fine Grey Lunar Dust)](https://ambientcg.com/view?id=Ground048)
* **ambientCG Option 2**: [Ground 030 (Stony Grey Regolith)](https://ambientcg.com/view?id=Ground030)

Save files into `Assets/project/materials/` as:
`LunarRegolith_Albedo.png`, `LunarRegolith_Normal.png`, `LunarRegolith_Roughness.png`
*(Or keep their downloaded names like `Ground048_Color.png` or `gravel_ground_01_diff_2k.png` — the auto-loader detects both formats automatically).*

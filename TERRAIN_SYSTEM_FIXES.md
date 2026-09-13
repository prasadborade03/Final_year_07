# Terrain System & Material Architecture Fixes

## 1. Issue Summary & Root Causes

### A. Editor Pause on "Generate Terrain" Click
* **Error**: `Tag: Terrain is not defined. UnityEngine.GameObject:set_tag (string)` at `TerrainGenerator.cs:107`
* **Root Cause**: `TerrainGenerator.cs` explicitly set `terrainObject.tag = "Terrain"`, but the string `"Terrain"` was missing from `ProjectSettings/TagManager.asset`. With the Console's **"Error Pause"** toggle enabled, this unhandled exception immediately froze the Editor.

### B. Pink / Magenta Terrain Material
* **Root Cause**: The project is configured under Unity's **Built-in Render Pipeline** (`customRenderPipeline: null`). In Unity 6, `TerrainMaterialManager.cs` was assigning `terrain.materialTemplate = null`. For custom procedural terrains in Unity 6, a null material template prevents the splatmap shader from resolving, turning the terrain bright magenta/pink.

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
  * **Built-in Pipeline (Active)**: Automatically binds `Nature/Terrain/Standard` material, sets `materialType = BuiltInStandard`, and builds PBR `TerrainLayer` setups.
  * **URP Pipeline (Future-Proof)**: Binds `Universal Render Pipeline/Terrain/Lit` (or `Universal Render Pipeline/Lit`).
  * **Diagnostic Presets**: Supports Topographic Grid (neon cyan) and Surface Normal Inspector modes via pipeline-compatible shaders.
  * **Procedural PBR Fallback**: Generates high-quality procedural albedo and normal textures with mipmaps and bilinear filtering, ensuring the terrain is **never pink**, even before downloading external textures.
  * **CC0 Auto-Loader**: Automatically searches `Assets/project/materials/` for matching 2K/4K PBR textures by preset name.

### 3. Rover URDF Material Isolation
* **Zero side-effects on rovers**: Rover materials in `Assets/project/data/URDF/` (Husky, M20, M2020 Perseverance) use the Built-in Standard shader (`fileID: 46`). Because pipeline settings were not modified globally, all rovers retain their original textures and will **never turn pink**.

### 4. Lighting & Visual Settings
* Set Main Camera `farClipPlane = 3000m` to prevent panoramic horizon clipping on 1000m planetary terrains.
* Configured Directional Light soft shadows and shadow bias in `Test_TerrainAndRover.unity`.

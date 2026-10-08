using UnityEngine;

namespace ProjectName.Terrain
{
    /// <summary>
    /// Production Terrain Generator:
    /// Takes height data (from image, procedural presets, or canvas painter)
    /// and constructs a Unity Terrain GameObject with custom size, elevation amplitude,
    /// base offset, and PBR surface materials.
    /// </summary>
    public class TerrainGenerator : MonoBehaviour
    {
        [Header("Default Terrain Settings")]
        [Tooltip("Fallback terrain heightmap resolution: 33, 65, 129, 257, 513, 1025")]
        public int heightmapResolution = 513;

        [Tooltip("Real-world size of the terrain in meters (width, height range, length)")]
        public Vector3 terrainSize = new Vector3(1000f, 120f, 1000f);

        [Header("Material & Shader Management")]
        public TerrainMaterialManager materialManager;

        // Keeps a reference to the currently-generated terrain so we don't stack terrains
        private GameObject currentTerrainObject;

        private void Awake()
        {
            if (materialManager == null)
            {
                materialManager = GetComponent<TerrainMaterialManager>();
                if (materialManager == null)
                {
                    materialManager = gameObject.AddComponent<TerrainMaterialManager>();
                }
            }
        }

        /// <summary>
        /// Backward-compatible entry point: generate terrain from an image path using default settings.
        /// </summary>
        public void GenerateTerrainFromImage(string imagePath)
        {
            TerrainTuningConfig defaultConfig = new TerrainTuningConfig
            {
                maxHeight = terrainSize.y,
                baseOffset = 0f,
                resolution = heightmapResolution,
                smoothingFactor = 1.0f,
                heightCurve = HeightRemapCurve.Linear,
                materialType = PlanetaryMaterialType.MartianDust,
                terrainWidth = terrainSize.x,
                terrainLength = terrainSize.z
            };

            GenerateTerrainFromImage(imagePath, defaultConfig);
        }

        /// <summary>
        /// Generates a live 3D terrain GameObject from an image file using full pre-import tuning parameters.
        /// </summary>
        public UnityEngine.Terrain GenerateTerrainFromImage(string imagePath, TerrainTuningConfig config)
        {
            float[,] heights = HeightmapLoader.LoadHeightsFromImage(
                imagePath,
                config.resolution,
                config.smoothingFactor,
                config.heightCurve
            );

            if (heights == null)
            {
                Debug.LogError($"[TerrainGenerator] Failed to load heightmap from: {imagePath}");
                return null;
            }

            return GenerateTerrain(heights, config);
        }

        /// <summary>
        /// Thoroughly cleans and destroys any existing terrain GameObject, colliders, and rock scatterers in the scene.
        /// Enforces Section 9 & 29: strictly only one active generated simulation terrain.
        /// </summary>
        public void ClearExistingTerrain()
        {
            if (currentTerrainObject != null)
            {
                if (Application.isPlaying) Destroy(currentTerrainObject);
                else DestroyImmediate(currentTerrainObject);
                currentTerrainObject = null;
            }

            // Find any other terrains in scene to ensure no stale terrains accumulate
            var allTerrains = FindObjectsByType<UnityEngine.Terrain>();
            foreach (var t in allTerrains)
            {
                if (t != null && t.gameObject != null)
                {
                    if (Application.isPlaying) Destroy(t.gameObject);
                    else DestroyImmediate(t.gameObject);
                }
            }

            // Clear scattered rocks
            if (PlanetRockScatterer.Instance != null)
            {
                PlanetRockScatterer.Instance.ClearRocks();
            }
        }

        /// <summary>
        /// Generates a live 3D terrain GameObject directly from a 2D float array (used by presets & canvas painter).
        /// </summary>
        public UnityEngine.Terrain GenerateTerrain(float[,] heights, TerrainTuningConfig config)
        {
            if (heights == null)
            {
                Debug.LogError("[TerrainGenerator] Heights array is null!");
                return null;
            }

            // Enforce single active terrain rule: destroy any existing terrain before generating
            ClearExistingTerrain();

            int res = config.resolution;
            TerrainData terrainData = new TerrainData
            {
                heightmapResolution = res,
                size = new Vector3(config.terrainWidth, config.maxHeight, config.terrainLength)
            };

            terrainData.SetHeights(0, 0, heights);

            GameObject terrainObject = UnityEngine.Terrain.CreateTerrainGameObject(terrainData);
            terrainObject.name = "GeneratedPlanetaryTerrain";

            // Safely assign Tag without breaking execution if tag is missing
            try
            {
                terrainObject.tag = "Terrain";
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[TerrainGenerator] Could not set tag 'Terrain' ({ex.Message}). Ensure 'Terrain' tag is added in Tags and Layers.");
            }

            // Apply base elevation baseline shift via world position
            terrainObject.transform.position = new Vector3(
                -config.terrainWidth * 0.5f,
                config.baseOffset,
                -config.terrainLength * 0.5f
            );

            UnityEngine.Terrain terrain = terrainObject.GetComponent<UnityEngine.Terrain>();
            terrain.drawHeightmap = true;
            terrain.drawTreesAndFoliage = false;
            terrain.allowAutoConnect = true;
            terrain.drawInstanced = true;
            terrain.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            terrain.basemapDistance = 3000f;
            terrain.heightmapPixelError = 5f;

            var cam = Camera.main;
            if (cam != null && cam.farClipPlane < 3500f)
            {
                cam.farClipPlane = 3500f;
            }

            // Ensure TerrainCollider is present and linked to terrainData
            TerrainCollider tCollider = terrainObject.GetComponent<TerrainCollider>();
            if (tCollider == null)
            {
                tCollider = terrainObject.AddComponent<TerrainCollider>();
            }
            tCollider.terrainData = terrainData;

            // Ensure TeleportationArea is present for VR grounded locomotion (§5)
            var teleArea = terrainObject.GetComponent<UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationArea>();
            if (teleArea == null)
            {
                terrainObject.AddComponent<UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationArea>();
            }

            // Apply planetary surface material (PBR layers or shader template)
            if (materialManager != null)
            {
                materialManager.ApplyMaterial(terrain, config.materialType);
            }

            // Scatter planetary surface rocks & boulders (Phase 5B)
            var rockScatterer = terrainObject.GetComponent<PlanetRockScatterer>();
            if (rockScatterer == null) rockScatterer = terrainObject.AddComponent<PlanetRockScatterer>();
            var envController = ProjectName.Planetary.PlanetEnvironmentController.Instance;
            if (envController != null && envController.currentProfile != null)
            {
                rockScatterer.ScatterRocks(terrain, envController.currentProfile);
            }

            currentTerrainObject = terrainObject;
            Debug.Log($"[TerrainGenerator] Successfully built 3D planetary terrain: {config.terrainWidth}x{config.terrainLength}m, MaxHeight: {config.maxHeight}m, Offset: {config.baseOffset}m, Material: {config.materialType}");

            return terrain;
        }

        /// <summary>
        /// Returns the currently active Terrain component, if any.
        /// </summary>
        public UnityEngine.Terrain GetCurrentTerrain()
        {
            return currentTerrainObject != null ? currentTerrainObject.GetComponent<UnityEngine.Terrain>() : null;
        }
    }
}

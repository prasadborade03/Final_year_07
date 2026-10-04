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

            // Auto-detect and register any existing terrain in the scene
            FindAndRegisterExistingTerrain();
        }

        private void Start()
        {
            // Safeguard: Ensure single terrain registered on Start
            FindAndRegisterExistingTerrain();
        }

        /// <summary>
        /// Finds the active terrain in the scene, sets currentTerrainObject, and purges any stray duplicates.
        /// Intelligently prefers configured terrain (with scatterer, teleport area, or children).
        /// </summary>
        public UnityEngine.Terrain FindAndRegisterExistingTerrain()
        {
            var allTerrains = UnityEngine.Object.FindObjectsByType<UnityEngine.Terrain>(UnityEngine.FindObjectsInactive.Include, UnityEngine.FindObjectsSortMode.None);
            if (allTerrains.Length > 0)
            {
                // Select the primary terrain: prefer configured ones
                UnityEngine.Terrain primary = allTerrains[0];
                for (int i = 0; i < allTerrains.Length; i++)
                {
                    var t = allTerrains[i];
                    if (t.GetComponent<PlanetRockScatterer>() != null ||
                        t.GetComponent<UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationArea>() != null ||
                        t.transform.childCount > 0)
                    {
                        primary = t;
                        break;
                    }
                }

                currentTerrainObject = primary.gameObject;
                currentTerrainObject.SetActive(true);

                // Purge any redundant duplicate terrains to prevent overlapping heightmaps
                for (int i = 0; i < allTerrains.Length; i++)
                {
                    var t = allTerrains[i];
                    if (t != primary && t != null && t.gameObject != null)
                    {
                        Debug.LogWarning($"[TerrainGenerator] Purging redundant duplicate terrain '{t.gameObject.name}' to prevent overlapping.");
                        t.gameObject.SetActive(false);
                        if (Application.isPlaying) Destroy(t.gameObject); else DestroyImmediate(t.gameObject);
                    }
                }
                return primary;
            }

            var stray = GameObject.Find("GeneratedPlanetaryTerrain");
            if (stray != null)
            {
                currentTerrainObject = stray;
                return stray.GetComponent<UnityEngine.Terrain>();
            }

            return null;
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
        /// Generates or updates a live 3D terrain directly from a 2D float array (used by presets & canvas painter).
        /// Re-uses the existing Terrain in-place to completely prevent overlapping heightmaps.
        /// </summary>
        public UnityEngine.Terrain GenerateTerrain(float[,] heights, TerrainTuningConfig config)
        {
            if (heights == null)
            {
                Debug.LogError("[TerrainGenerator] Heights array is null!");
                return null;
            }

            // 1. Check for existing terrain to update in-place (ZERO overlap, ZERO stacking!)
            UnityEngine.Terrain existingTerrain = FindAndRegisterExistingTerrain();

            if (existingTerrain != null && existingTerrain.terrainData != null)
            {
                TerrainData td = existingTerrain.terrainData;
                int hRes = heights.GetLength(0);
                if (td.heightmapResolution != hRes)
                {
                    td.heightmapResolution = hRes;
                }
                td.size = new Vector3(config.terrainWidth, config.maxHeight, config.terrainLength);
                td.SetHeights(0, 0, heights);
                td.SyncHeightmap();

                existingTerrain.gameObject.transform.position = new Vector3(
                    -config.terrainWidth * 0.5f,
                    config.baseOffset,
                    -config.terrainLength * 0.5f
                );

                // Ensure collider is synced
                TerrainCollider tCollider = existingTerrain.GetComponent<TerrainCollider>();
                if (tCollider == null) tCollider = existingTerrain.gameObject.AddComponent<TerrainCollider>();
                tCollider.terrainData = null;
                tCollider.terrainData = td;

                // Ensure teleportation area for VR
                var teleArea = existingTerrain.GetComponent<UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationArea>();
                if (teleArea == null) existingTerrain.gameObject.AddComponent<UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationArea>();

                // Re-apply material
                if (materialManager != null)
                {
                    materialManager.ApplyMaterial(existingTerrain, config.materialType);
                }

                // Scatter rocks
                var rockScatterer = existingTerrain.GetComponent<PlanetRockScatterer>();
                if (rockScatterer == null) rockScatterer = existingTerrain.gameObject.AddComponent<PlanetRockScatterer>();
                rockScatterer.ClearRocks();
                var envController = ProjectName.Planetary.PlanetEnvironmentController.Instance;
                if (envController != null && envController.currentProfile != null)
                {
                    rockScatterer.ScatterRocks(existingTerrain, envController.currentProfile);
                }

                currentTerrainObject = existingTerrain.gameObject;
                Debug.Log($"[TerrainGenerator] Successfully updated 3D planetary terrain in-place: {config.terrainWidth}x{config.terrainLength}m, MaxHeight: {config.maxHeight}m, Offset: {config.baseOffset}m, Material: {config.materialType}");
                return existingTerrain;
            }

            // 2. Fallback: Clean up ALL old terrains before creating a new one
            var strayTerrains = UnityEngine.Object.FindObjectsByType<UnityEngine.Terrain>(UnityEngine.FindObjectsInactive.Include, UnityEngine.FindObjectsSortMode.None);
            foreach (var stray in strayTerrains)
            {
                if (stray != null && stray.gameObject != null)
                {
                    stray.gameObject.SetActive(false);
                    if (Application.isPlaying) Destroy(stray.gameObject); else DestroyImmediate(stray.gameObject);
                }
            }
            currentTerrainObject = null;

            int targetRes = heights.GetLength(0);
            TerrainData newTerrainData = new TerrainData
            {
                heightmapResolution = targetRes,
                size = new Vector3(config.terrainWidth, config.maxHeight, config.terrainLength)
            };

            newTerrainData.SetHeights(0, 0, heights);

            GameObject terrainObject = UnityEngine.Terrain.CreateTerrainGameObject(newTerrainData);
            terrainObject.name = "GeneratedPlanetaryTerrain";

            try
            {
                terrainObject.tag = "Terrain";
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[TerrainGenerator] Could not set tag 'Terrain' ({ex.Message}). Ensure 'Terrain' tag is added in Tags and Layers.");
            }

            terrainObject.transform.position = new Vector3(
                -config.terrainWidth * 0.5f,
                config.baseOffset,
                -config.terrainLength * 0.5f
            );

            UnityEngine.Terrain newTerrain = terrainObject.GetComponent<UnityEngine.Terrain>();
            newTerrain.drawHeightmap = true;
            newTerrain.drawTreesAndFoliage = false;
            newTerrain.allowAutoConnect = true;
            newTerrain.drawInstanced = true;
            newTerrain.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            newTerrain.basemapDistance = 3000f;
            newTerrain.heightmapPixelError = 5f;

            var cam = Camera.main;
            if (cam != null && cam.farClipPlane < 3500f)
            {
                cam.farClipPlane = 3500f;
            }

            TerrainCollider newCollider = terrainObject.GetComponent<TerrainCollider>();
            if (newCollider == null)
            {
                newCollider = terrainObject.AddComponent<TerrainCollider>();
            }
            newCollider.terrainData = newTerrainData;

            var newTeleArea = terrainObject.GetComponent<UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationArea>();
            if (newTeleArea == null)
            {
                terrainObject.AddComponent<UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationArea>();
            }

            if (materialManager != null)
            {
                materialManager.ApplyMaterial(newTerrain, config.materialType);
            }

            var newScatterer = terrainObject.GetComponent<PlanetRockScatterer>();
            if (newScatterer == null) newScatterer = terrainObject.AddComponent<PlanetRockScatterer>();
            var env = ProjectName.Planetary.PlanetEnvironmentController.Instance;
            if (env != null && env.currentProfile != null)
            {
                newScatterer.ScatterRocks(newTerrain, env.currentProfile);
            }

            currentTerrainObject = terrainObject;
            Debug.Log($"[TerrainGenerator] Successfully built new 3D planetary terrain: {config.terrainWidth}x{config.terrainLength}m, MaxHeight: {config.maxHeight}m, Offset: {config.baseOffset}m, Material: {config.materialType}");

            return newTerrain;
        }

        /// <summary>
        /// Returns the currently active Terrain component, if any.
        /// </summary>
        public UnityEngine.Terrain GetCurrentTerrain()
        {
            if (currentTerrainObject == null) FindAndRegisterExistingTerrain();
            return currentTerrainObject != null ? currentTerrainObject.GetComponent<UnityEngine.Terrain>() : UnityEngine.Terrain.activeTerrain;
        }
    }
}

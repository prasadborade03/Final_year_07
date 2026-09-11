using UnityEngine;

namespace ProjectName.Terrain
{
    /// <summary>
    /// Job: Take height data and build a real Unity Terrain GameObject
    /// from it. Doesn't know or care where the height data came from
    /// (file upload, test path, procedural generation later, etc).
    /// </summary>
    public class TerrainGenerator : MonoBehaviour
    {
        [Header("Terrain Settings")]
        [Tooltip("Must be a valid Unity terrain resolution: 33, 65, 129, 257, 513, 1025, 2049, 4097")]
        public int heightmapResolution = 1025;

        [Tooltip("Real-world size of the terrain in meters (width, height range, length)")]
        public Vector3 terrainSize = new Vector3(1000, 300, 1000);

        // Keeps a reference to the currently-generated terrain, if any,
        // so we can remove it before building a new one.
        private GameObject currentTerrainObject;

        // NOTE: Start() is intentionally removed. Generation is no longer
        // automatic -- it now only happens when something (the upload UI)
        // explicitly calls GenerateTerrainFromImage(). This keeps this
        // script independent of *how* a file path is obtained.

        /// <summary>
        /// The main entry point: give it an image file path, it builds
        /// and places a Terrain GameObject in the scene.
        /// </summary>
        public void GenerateTerrainFromImage(string imagePath)
        {
            // If a terrain already exists from a previous upload,
            // remove it first so we don't stack terrains on top of
            // each other.
            if (currentTerrainObject != null)
            {
                Destroy(currentTerrainObject);
            }

            // Step 1: Ask the loader for height values from the image.
            float[,] heights = HeightmapLoader.LoadHeightsFromImage(imagePath, heightmapResolution);

            // Step 2: Create a new TerrainData asset in memory.
            // This object holds the actual height/shape information.
            TerrainData terrainData = new TerrainData();
            terrainData.heightmapResolution = heightmapResolution;
            terrainData.size = terrainSize;

            // Step 3: Feed our computed heights into it.
            // SetHeights(xBase, yBase, heights) -- xBase/yBase = 0,0
            // means "start writing from the bottom-left corner."
            terrainData.SetHeights(0, 0, heights);

            // Step 4: Create the actual Terrain GameObject in the scene
            // using this data. This is what makes it visible and collidable.
            GameObject terrainObject = UnityEngine.Terrain.CreateTerrainGameObject(terrainData);
            terrainObject.name = "GeneratedTerrain";

            terrainObject.transform.position = Vector3.zero;

            currentTerrainObject = terrainObject;

            Debug.Log("Terrain generated successfully from: " + imagePath);
        }
    }
}
using System.Collections.Generic;
using UnityEngine;

namespace ProjectName.Terrain
{
    /// <summary>
    /// Manages Planetary Surface Materials and PBR TerrainLayers for Unity Terrain.
    /// Provides procedural texture synthesis fallbacks (Albedo + Normal Maps + Roughness)
    /// so the system works with zero external assets, while supporting custom artist
    /// materials and textures assigned via Inspector.
    /// </summary>
    public class TerrainMaterialManager : MonoBehaviour
    {
        [Header("Custom Artist Materials (Optional)")]
        [Tooltip("Assign custom Material templates if using custom shaders")]
        public Material martianRustMaterial;
        public Material lunarRegolithMaterial;
        public Material volcanicBasaltMaterial;
        public Material polarIceMaterial;
        public Material redCanyonMaterial;
        public Material wireframeMaterial;
        public Material normalInspectorMaterial;

        [Header("Custom Artist Textures (Optional)")]
        public Texture2D customMartianAlbedo;
        public Texture2D customMartianNormal;
        public Texture2D customLunarAlbedo;
        public Texture2D customLunarNormal;
        public Texture2D customBasaltAlbedo;
        public Texture2D customBasaltNormal;
        public Texture2D customIceAlbedo;
        public Texture2D customIceNormal;

        // Cache of procedural textures and TerrainLayers
        private readonly Dictionary<PlanetaryMaterialType, TerrainLayer> generatedLayers = new Dictionary<PlanetaryMaterialType, TerrainLayer>();
        private readonly Dictionary<PlanetaryMaterialType, Material> generatedMaterials = new Dictionary<PlanetaryMaterialType, Material>();

        /// <summary>
        /// Applies the requested planetary surface material to a Unity Terrain instance.
        /// </summary>
        public void ApplyMaterial(UnityEngine.Terrain terrain, PlanetaryMaterialType matType)
        {
            if (terrain == null) return;

            TerrainData tData = terrain.terrainData;
            if (tData == null) return;

            // Handle diagnostic materials that use a custom materialTemplate
            if (matType == PlanetaryMaterialType.TopographicWireframe || matType == PlanetaryMaterialType.NormalInspector)
            {
                Material diagMat = GetOrCreateDiagnosticMaterial(matType);
                if (diagMat != null)
                {
                    terrain.materialTemplate = diagMat;
                    Debug.Log($"[TerrainMaterialManager] Assigned custom materialTemplate '{matType}'.");
                    return;
                }
            }

            // Check if artist provided a custom material override
            Material customMat = GetCustomMaterial(matType);
            if (customMat != null)
            {
                terrain.materialTemplate = customMat;
                Debug.Log($"[TerrainMaterialManager] Assigned custom Material template for '{matType}'.");
                return;
            }

            // Otherwise, reset materialTemplate to null (uses URP default terrain lit shader)
            // and configure PBR TerrainLayer with diffuse, normal map, metallic, and smoothness.
            terrain.materialTemplate = null;

            TerrainLayer layer = GetOrCreateTerrainLayer(matType);
            if (layer != null)
            {
                tData.terrainLayers = new TerrainLayer[] { layer };
                Debug.Log($"[TerrainMaterialManager] Applied TerrainLayer for '{matType}' with PBR albedo & normal mapping.");
            }
        }

        public void ApplyMaterial(UnityEngine.Terrain terrain, string matTypeStr)
        {
            if (System.Enum.TryParse(matTypeStr, true, out PlanetaryMaterialType parsed))
            {
                ApplyMaterial(terrain, parsed);
            }
            else
            {
                ApplyMaterial(terrain, PlanetaryMaterialType.MartianDust);
            }
        }

        private Material GetCustomMaterial(PlanetaryMaterialType matType)
        {
            switch (matType)
            {
                case PlanetaryMaterialType.MartianDust: return martianRustMaterial;
                case PlanetaryMaterialType.LunarRegolith: return lunarRegolithMaterial;
                case PlanetaryMaterialType.VolcanicBasalt: return volcanicBasaltMaterial;
                case PlanetaryMaterialType.PolarIce: return polarIceMaterial;
                case PlanetaryMaterialType.RedCanyon: return redCanyonMaterial;
                case PlanetaryMaterialType.TopographicWireframe: return wireframeMaterial;
                case PlanetaryMaterialType.NormalInspector: return normalInspectorMaterial;
                default: return null;
            }
        }

        private TerrainLayer GetOrCreateTerrainLayer(PlanetaryMaterialType matType)
        {
            if (generatedLayers.TryGetValue(matType, out TerrainLayer cached) && cached != null)
            {
                return cached;
            }

            TerrainLayer layer = new TerrainLayer();
            layer.name = $"Layer_{matType}";

            Texture2D albedo = null;
            Texture2D normal = null;
            float smoothness = 0.2f;
            float metallic = 0.0f;
            Vector2 tileSize = new Vector2(25f, 25f);

            switch (matType)
            {
                case PlanetaryMaterialType.MartianDust:
                    albedo = customMartianAlbedo != null ? customMartianAlbedo : GenerateProceduralMartianAlbedo();
                    normal = customMartianNormal != null ? customMartianNormal : GenerateProceduralNormal(albedo, 2.5f);
                    smoothness = 0.15f;
                    metallic = 0.05f;
                    tileSize = new Vector2(20f, 20f);
                    break;

                case PlanetaryMaterialType.LunarRegolith:
                    albedo = customLunarAlbedo != null ? customLunarAlbedo : GenerateProceduralLunarAlbedo();
                    normal = customLunarNormal != null ? customLunarNormal : GenerateProceduralNormal(albedo, 2.0f);
                    smoothness = 0.35f;
                    metallic = 0.15f;
                    tileSize = new Vector2(18f, 18f);
                    break;

                case PlanetaryMaterialType.VolcanicBasalt:
                    albedo = customBasaltAlbedo != null ? customBasaltAlbedo : GenerateProceduralBasaltAlbedo();
                    normal = customBasaltNormal != null ? customBasaltNormal : GenerateProceduralNormal(albedo, 3.5f);
                    smoothness = 0.08f;
                    metallic = 0.25f;
                    tileSize = new Vector2(15f, 15f);
                    break;

                case PlanetaryMaterialType.PolarIce:
                    albedo = customIceAlbedo != null ? customIceAlbedo : GenerateProceduralIceAlbedo();
                    normal = customIceNormal != null ? customIceNormal : GenerateProceduralNormal(albedo, 1.2f);
                    smoothness = 0.82f;
                    metallic = 0.10f;
                    tileSize = new Vector2(30f, 30f);
                    break;

                case PlanetaryMaterialType.RedCanyon:
                default:
                    albedo = GenerateProceduralCanyonAlbedo();
                    normal = GenerateProceduralNormal(albedo, 3.0f);
                    smoothness = 0.18f;
                    metallic = 0.05f;
                    tileSize = new Vector2(25f, 25f);
                    break;
            }

            layer.diffuseTexture = albedo;
            layer.normalMapTexture = normal;
            layer.smoothness = smoothness;
            layer.metallic = metallic;
            layer.tileSize = tileSize;

            generatedLayers[matType] = layer;
            return layer;
        }

        private Material GetOrCreateDiagnosticMaterial(PlanetaryMaterialType matType)
        {
            if (generatedMaterials.TryGetValue(matType, out Material cached) && cached != null)
                return cached;

            Shader s = null;
            if (matType == PlanetaryMaterialType.TopographicWireframe)
            {
                if (wireframeMaterial != null) return wireframeMaterial;
                s = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
                if (s != null)
                {
                    Material m = new Material(s);
                    m.name = "Mat_TopographicWireframe";
                    m.color = new Color(0.0f, 0.95f, 0.75f, 1.0f);
                    generatedMaterials[matType] = m;
                    return m;
                }
            }
            else if (matType == PlanetaryMaterialType.NormalInspector)
            {
                if (normalInspectorMaterial != null) return normalInspectorMaterial;
                s = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                if (s != null)
                {
                    Material m = new Material(s);
                    m.name = "Mat_NormalInspector";
                    m.color = new Color(0.7f, 0.4f, 0.9f, 1.0f);
                    generatedMaterials[matType] = m;
                    return m;
                }
            }

            return null;
        }

        // =============================================================
        // Procedural PBR Texture Synthesizers
        // =============================================================
        private Texture2D GenerateProceduralMartianAlbedo()
        {
            int size = 256;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGB24, true);
            tex.wrapMode = TextureWrapMode.Repeat;
            Color[] cols = new Color[size * size];

            Color rustBase = new Color(0.78f, 0.32f, 0.16f); // Iron oxide base
            Color rustDark = new Color(0.55f, 0.20f, 0.10f); // Shadow crevices
            Color rustDust = new Color(0.88f, 0.48f, 0.25f); // Fine surface dust

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (float)x / size;
                    float v = (float)y / size;

                    float n1 = Mathf.PerlinNoise(u * 12f, v * 12f);
                    float n2 = Mathf.PerlinNoise(u * 32f, v * 32f) * 0.5f;
                    float n3 = Mathf.PerlinNoise(u * 64f, v * 64f) * 0.25f;
                    float blend = Mathf.Clamp01(n1 + n2 + n3);

                    Color c = Color.Lerp(rustDark, rustBase, blend);
                    if (n2 > 0.35f) c = Color.Lerp(c, rustDust, (n2 - 0.35f) * 1.5f);

                    cols[y * size + x] = c;
                }
            }

            tex.SetPixels(cols);
            tex.Apply();
            return tex;
        }

        private Texture2D GenerateProceduralLunarAlbedo()
        {
            int size = 256;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGB24, true);
            tex.wrapMode = TextureWrapMode.Repeat;
            Color[] cols = new Color[size * size];

            Color anorthositeBase = new Color(0.42f, 0.44f, 0.48f); // Titanium-anorthosite
            Color craterDark = new Color(0.24f, 0.25f, 0.28f);
            Color glassHighlight = new Color(0.65f, 0.67f, 0.72f); // Glass bead spherules

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (float)x / size;
                    float v = (float)y / size;

                    float n1 = Mathf.PerlinNoise(u * 10f, v * 10f);
                    float n2 = Mathf.PerlinNoise(u * 28f, v * 28f);
                    float micro = Mathf.PerlinNoise(u * 70f, v * 70f);

                    Color c = Color.Lerp(craterDark, anorthositeBase, n1);
                    if (micro > 0.65f) c = Color.Lerp(c, glassHighlight, (micro - 0.65f) * 2f);

                    cols[y * size + x] = c;
                }
            }

            tex.SetPixels(cols);
            tex.Apply();
            return tex;
        }

        private Texture2D GenerateProceduralBasaltAlbedo()
        {
            int size = 256;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGB24, true);
            tex.wrapMode = TextureWrapMode.Repeat;
            Color[] cols = new Color[size * size];

            Color basaltDark = new Color(0.12f, 0.13f, 0.16f); // Igneous charcoal
            Color basaltMid = new Color(0.22f, 0.23f, 0.27f);
            Color mineralSpeck = new Color(0.35f, 0.36f, 0.40f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (float)x / size;
                    float v = (float)y / size;

                    float n1 = Mathf.PerlinNoise(u * 16f, v * 16f);
                    float fracture = Mathf.Abs(Mathf.Sin(u * 30f + v * 30f + n1 * 5f));

                    Color c = Color.Lerp(basaltDark, basaltMid, n1);
                    if (fracture < 0.15f) c = Color.Lerp(c, basaltDark * 0.7f, 0.8f);

                    cols[y * size + x] = c;
                }
            }

            tex.SetPixels(cols);
            tex.Apply();
            return tex;
        }

        private Texture2D GenerateProceduralIceAlbedo()
        {
            int size = 256;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGB24, true);
            tex.wrapMode = TextureWrapMode.Repeat;
            Color[] cols = new Color[size * size];

            Color iceBase = new Color(0.68f, 0.85f, 0.95f);
            Color iceDeep = new Color(0.45f, 0.68f, 0.85f);
            Color iceGlaze = new Color(0.92f, 0.97f, 1.0f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (float)x / size;
                    float v = (float)y / size;

                    float n1 = Mathf.PerlinNoise(u * 8f, v * 8f);
                    float sastrugi = Mathf.Sin(u * 25f + v * 8f + n1 * 2f);

                    Color c = Color.Lerp(iceDeep, iceBase, n1);
                    if (sastrugi > 0.4f) c = Color.Lerp(c, iceGlaze, (sastrugi - 0.4f) * 1.5f);

                    cols[y * size + x] = c;
                }
            }

            tex.SetPixels(cols);
            tex.Apply();
            return tex;
        }

        private Texture2D GenerateProceduralCanyonAlbedo()
        {
            int size = 256;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGB24, true);
            tex.wrapMode = TextureWrapMode.Repeat;
            Color[] cols = new Color[size * size];

            Color[] strata = new Color[]
            {
                new Color(0.72f, 0.28f, 0.15f), // Red sandstone
                new Color(0.55f, 0.22f, 0.12f), // Dark mudstone
                new Color(0.85f, 0.45f, 0.22f), // Bright orange sand
                new Color(0.60f, 0.30f, 0.18f), // Siltstone
            };

            for (int y = 0; y < size; y++)
            {
                float v = (float)y / size;
                int stratumIdx = Mathf.FloorToInt(v * 16f) % strata.Length;
                Color baseC = strata[stratumIdx];

                for (int x = 0; x < size; x++)
                {
                    float u = (float)x / size;
                    float noise = Mathf.PerlinNoise(u * 20f, v * 40f) * 0.2f - 0.1f;
                    cols[y * size + x] = new Color(
                        Mathf.Clamp01(baseC.r + noise),
                        Mathf.Clamp01(baseC.g + noise * 0.7f),
                        Mathf.Clamp01(baseC.b + noise * 0.5f)
                    );
                }
            }

            tex.SetPixels(cols);
            tex.Apply();
            return tex;
        }

        // Sobel filter normal map generation from an albedo/grayscale texture
        private Texture2D GenerateProceduralNormal(Texture2D source, float strength = 2.0f)
        {
            int width = source.width;
            int height = source.height;
            Texture2D normalTex = new Texture2D(width, height, TextureFormat.RGBA32, true, true);
            normalTex.wrapMode = TextureWrapMode.Repeat;

            Color[] normals = new Color[width * height];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float left = source.GetPixel((x - 1 + width) % width, y).grayscale;
                    float right = source.GetPixel((x + 1) % width, y).grayscale;
                    float down = source.GetPixel(x, (y - 1 + height) % height).grayscale;
                    float up = source.GetPixel(x, (y + 1) % height).grayscale;

                    float dx = (right - left) * strength;
                    float dy = (up - down) * strength;
                    Vector3 n = new Vector3(-dx, -dy, 1.0f).normalized;

                    // Pack into normal map color [0, 1]
                    normals[y * width + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
                }
            }

            normalTex.SetPixels(normals);
            normalTex.Apply();
            return normalTex;
        }
    }
}

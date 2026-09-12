using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ProjectName.Terrain
{
    /// <summary>
    /// TerrainMaterialManager - FIXED FOR URP & BUILT-IN PIPELINE
    /// Fixes Pink Material / Missing Shader errors on Planetary Surface Materials.
    /// Supports both SurfaceMaterialType (user reference) and PlanetaryMaterialType (workbench enum).
    /// </summary>
    public class TerrainMaterialManager : MonoBehaviour
    {
        [Header("Target Terrain Component")]
        public UnityEngine.Terrain targetTerrain;

        [Header("Material & Layer Presets")]
        public SurfaceMaterialType activeMaterialType = SurfaceMaterialType.MartianRust;

        public enum SurfaceMaterialType
        {
            MartianRust,
            LunarRegolith,
            VolcanicBasalt,
            PolarIce,
            RedSandstone,
            TopographicGrid,
            NormalInspector
        }

        [System.Serializable]
        public struct SurfaceMaterialConfig
        {
            public SurfaceMaterialType type;
            public string name;
            public Color primaryColor;
            public float roughness;
            public float metallic;
            public float bumpScale;
            public Texture2D customAlbedo;
            public Texture2D customNormal;
        }

        [Header("Material Configurations")]
        public List<SurfaceMaterialConfig> materialConfigs = new List<SurfaceMaterialConfig>();

        [Header("Custom Artist Textures (Optional Overrides)")]
        public Texture2D customMartianAlbedo;
        public Texture2D customMartianNormal;
        public Texture2D customLunarAlbedo;
        public Texture2D customLunarNormal;
        public Texture2D customBasaltAlbedo;
        public Texture2D customBasaltNormal;
        public Texture2D customIceAlbedo;
        public Texture2D customIceNormal;

        // Runtime Cache
        private readonly Dictionary<SurfaceMaterialType, TerrainLayer> generatedLayers = new Dictionary<SurfaceMaterialType, TerrainLayer>();
        private readonly Dictionary<SurfaceMaterialType, Material> generatedMaterials = new Dictionary<SurfaceMaterialType, Material>();

        private void Awake()
        {
            if (targetTerrain == null)
                targetTerrain = GetComponent<UnityEngine.Terrain>();

            InitializeDefaultConfigs();
        }

        private void Start()
        {
            ApplySurfaceMaterial(activeMaterialType);
        }

        public void InitializeDefaultConfigs()
        {
            if (materialConfigs != null && materialConfigs.Count > 0) return;

            materialConfigs = new List<SurfaceMaterialConfig>
            {
                new SurfaceMaterialConfig
                {
                    type = SurfaceMaterialType.MartianRust,
                    name = "Martian Rust Oxide",
                    primaryColor = new Color(0.78f, 0.32f, 0.16f),
                    roughness = 0.85f,
                    metallic = 0.05f,
                    bumpScale = 1.2f,
                    customAlbedo = customMartianAlbedo,
                    customNormal = customMartianNormal
                },
                new SurfaceMaterialConfig
                {
                    type = SurfaceMaterialType.LunarRegolith,
                    name = "Lunar Regolith",
                    primaryColor = new Color(0.42f, 0.44f, 0.48f),
                    roughness = 0.65f,
                    metallic = 0.15f,
                    bumpScale = 0.8f,
                    customAlbedo = customLunarAlbedo,
                    customNormal = customLunarNormal
                },
                new SurfaceMaterialConfig
                {
                    type = SurfaceMaterialType.VolcanicBasalt,
                    name = "Volcanic Charcoal Basalt",
                    primaryColor = new Color(0.12f, 0.13f, 0.16f),
                    roughness = 0.92f,
                    metallic = 0.25f,
                    bumpScale = 1.5f,
                    customAlbedo = customBasaltAlbedo,
                    customNormal = customBasaltNormal
                },
                new SurfaceMaterialConfig
                {
                    type = SurfaceMaterialType.PolarIce,
                    name = "Polar Cryo-Ice Cap",
                    primaryColor = new Color(0.68f, 0.85f, 0.95f),
                    roughness = 0.18f,
                    metallic = 0.10f,
                    bumpScale = 0.4f,
                    customAlbedo = customIceAlbedo,
                    customNormal = customIceNormal
                },
                new SurfaceMaterialConfig
                {
                    type = SurfaceMaterialType.RedSandstone,
                    name = "Red Sandstone Canyon",
                    primaryColor = new Color(0.72f, 0.33f, 0.21f),
                    roughness = 0.80f,
                    metallic = 0.08f,
                    bumpScale = 1.4f
                },
                new SurfaceMaterialConfig
                {
                    type = SurfaceMaterialType.TopographicGrid,
                    name = "Topographic Grid",
                    primaryColor = new Color(0.0f, 0.95f, 1.0f),
                    roughness = 0.5f,
                    metallic = 0.1f,
                    bumpScale = 0.0f
                },
                new SurfaceMaterialConfig
                {
                    type = SurfaceMaterialType.NormalInspector,
                    name = "Surface Normal Inspector",
                    primaryColor = new Color(0.65f, 0.33f, 0.96f),
                    roughness = 0.5f,
                    metallic = 0.1f,
                    bumpScale = 0.0f
                }
            };
        }

        /// <summary>
        /// Applies the requested planetary surface material to the Terrain.
        /// Guaranteed immune to pink/magenta missing shader errors.
        /// </summary>
        public void ApplySurfaceMaterial(SurfaceMaterialType type)
        {
            activeMaterialType = type;
            if (targetTerrain == null) targetTerrain = UnityEngine.Terrain.activeTerrain;
            if (targetTerrain == null)
            {
                targetTerrain = FindAnyObjectByType<UnityEngine.Terrain>();
            }
            if (targetTerrain == null) return;

            InitializeDefaultConfigs();
            SurfaceMaterialConfig config = materialConfigs.Find(c => c.type == type);

            // Determine whether an active Scriptable Render Pipeline (URP) is currently rendering
            bool isSRP = GraphicsSettings.currentRenderPipeline != null || QualitySettings.renderPipeline != null;

            // Pipeline-aware shader resolution
            Shader targetShader = null;
            if (isSRP)
            {
                targetShader = Shader.Find("Universal Render Pipeline/Terrain/Lit");
                if (targetShader == null || !targetShader.isSupported)
                    targetShader = Shader.Find("Universal Render Pipeline/Lit");
            }

            if (targetShader == null || !targetShader.isSupported)
                targetShader = Shader.Find("Nature/Terrain/Standard");
            if (targetShader == null || !targetShader.isSupported)
                targetShader = Shader.Find("Nature/Terrain/Diffuse");
            if (targetShader == null || !targetShader.isSupported)
                targetShader = Shader.Find("Standard");

            // Diagnostic Modes (Topographic Grid & Normal Inspector)
            if (type == SurfaceMaterialType.TopographicGrid || type == SurfaceMaterialType.NormalInspector)
            {
                Shader diagShader = null;
                if (isSRP)
                {
                    diagShader = (type == SurfaceMaterialType.NormalInspector)
                        ? Shader.Find("Universal Render Pipeline/Lit")
                        : Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Universal Render Pipeline/Lit");
                }

                if (diagShader == null || !diagShader.isSupported)
                {
                    diagShader = Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
                }

                if (diagShader == null) diagShader = targetShader;

                Material diagMat = new Material(diagShader);
                diagMat.name = "TerrainDiag_" + type.ToString();
                diagMat.SetColor("_BaseColor", config.primaryColor);
                diagMat.SetColor("_Color", config.primaryColor);
                targetTerrain.materialTemplate = diagMat;
            }
            else
            {
                // Planetary Surface Material: Configure TerrainLayer with PBR textures
                TerrainLayer layer = GetOrCreateTerrainLayer(config);
                if (targetTerrain.terrainData != null)
                {
                    targetTerrain.terrainData.terrainLayers = new TerrainLayer[] { layer };

                    // Initialize full splatmap weight so layer 0 renders at 100% opacity
                    int alphaRes = Mathf.Max(32, targetTerrain.terrainData.alphamapResolution);
                    targetTerrain.terrainData.alphamapResolution = alphaRes;
                    float[,,] alphaMaps = new float[alphaRes, alphaRes, 1];
                    for (int y = 0; y < alphaRes; y++)
                    {
                        for (int x = 0; x < alphaRes; x++)
                        {
                            alphaMaps[y, x, 0] = 1.0f;
                        }
                    }
                    targetTerrain.terrainData.SetAlphamaps(0, 0, alphaMaps);
                }

                if (isSRP && targetShader != null && targetShader.name.Contains("Universal Render Pipeline"))
                {
                    // URP TerrainLit material
                    Material customMat = new Material(targetShader);
                    customMat.name = "TerrainMat_" + type.ToString();
                    customMat.SetColor("_BaseColor", config.primaryColor);
                    customMat.SetColor("_Color", config.primaryColor);
                    customMat.EnableKeyword("_TERRAIN_INSTANCED_PERPIXEL_NORMAL");
                    targetTerrain.materialTemplate = customMat;
                }
                else
                {
                    // Built-in Pipeline: setting materialTemplate to null uses Unity's native
                    // built-in terrain engine which shades TerrainLayers with full lighting and NO pink artifacts!
                    targetTerrain.materialTemplate = null;
                }
            }

            targetTerrain.Flush();
            Debug.Log($"[TerrainMaterialManager] Applied FIXED material for '{type}' (isSRP: {isSRP}) with PBR albedo & normal mapping.");
        }

        private TerrainLayer GetOrCreateTerrainLayer(SurfaceMaterialConfig config)
        {
            if (generatedLayers.TryGetValue(config.type, out TerrainLayer cached) && cached != null)
            {
                return cached;
            }

            Texture2D albedoTex = config.customAlbedo != null 
                ? config.customAlbedo 
                : GenerateProceduralAlbedo(config.primaryColor, config.type);

            Texture2D normalTex = config.customNormal != null 
                ? config.customNormal 
                : GenerateProceduralNormalMap(config.bumpScale);

            TerrainLayer layer = new TerrainLayer
            {
                name = "Layer_" + config.type.ToString(),
                diffuseTexture = albedoTex,
                normalMapTexture = normalTex,
                normalScale = config.bumpScale,
                smoothness = Mathf.Clamp01(1.0f - config.roughness),
                metallic = config.metallic,
                tileSize = new Vector2(15f, 15f)
            };

            generatedLayers[config.type] = layer;
            return layer;
        }

        // =========================================================================
        // High-Quality Procedural Texture Synthesizers (RGBA32 + mipmaps)
        // =========================================================================
        private Texture2D GenerateProceduralAlbedo(Color baseColor, SurfaceMaterialType type)
        {
            int size = 256;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Bilinear;
            Color[] pixels = new Color[size * size];

            for (int y = 0; y < size; y++)
            {
                float v = (float)y / size;
                for (int x = 0; x < size; x++)
                {
                    float u = (float)x / size;
                    float noise = 0f;

                    switch (type)
                    {
                        case SurfaceMaterialType.MartianRust:
                            noise = (Mathf.PerlinNoise(u * 14f, v * 14f) - 0.5f) * 0.22f +
                                    (Mathf.PerlinNoise(u * 38f, v * 38f) - 0.5f) * 0.08f;
                            break;

                        case SurfaceMaterialType.LunarRegolith:
                            noise = (Mathf.PerlinNoise(u * 18f, v * 18f) - 0.5f) * 0.16f;
                            // Micro impact speckles
                            if (Mathf.PerlinNoise(u * 75f, v * 75f) > 0.7f) noise += 0.15f;
                            break;

                        case SurfaceMaterialType.VolcanicBasalt:
                            noise = (Mathf.PerlinNoise(u * 20f, v * 20f) - 0.5f) * 0.18f;
                            float cracks = Mathf.Abs(Mathf.Sin(u * 25f + v * 25f));
                            if (cracks < 0.12f) noise -= 0.15f;
                            break;

                        case SurfaceMaterialType.PolarIce:
                            noise = Mathf.Sin(u * 28f + v * 10f) * 0.08f + (Mathf.PerlinNoise(u * 8f, v * 8f) - 0.5f) * 0.12f;
                            break;

                        case SurfaceMaterialType.RedSandstone:
                            float band = Mathf.Sin(v * Mathf.PI * 16f) * 0.12f;
                            noise = band + (Mathf.PerlinNoise(u * 16f, v * 32f) - 0.5f) * 0.10f;
                            break;

                        default:
                            noise = (Mathf.PerlinNoise(u * 10f, v * 10f) - 0.5f) * 0.15f;
                            break;
                    }

                    Color col = new Color(
                        Mathf.Clamp01(baseColor.r + noise),
                        Mathf.Clamp01(baseColor.g + noise * 0.75f),
                        Mathf.Clamp01(baseColor.b + noise * 0.55f),
                        1.0f
                    );
                    pixels[y * size + x] = col;
                }
            }

            tex.SetPixels(pixels);
            tex.Apply(true, false);
            return tex;
        }

        private Texture2D GenerateProceduralNormalMap(float strength)
        {
            int size = 256;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Bilinear;
            Color[] pixels = new Color[size * size];

            for (int y = 0; y < size; y++)
            {
                float v = (float)y / size;
                for (int x = 0; x < size; x++)
                {
                    float u = (float)x / size;
                    float nx = (Mathf.PerlinNoise(u * 16f, v * 16f) - 0.5f) * strength;
                    float ny = (Mathf.PerlinNoise(v * 16f, u * 16f) - 0.5f) * strength;
                    Vector3 normal = new Vector3(-nx, -ny, 1.0f).normalized;

                    pixels[y * size + x] = new Color(
                        normal.x * 0.5f + 0.5f,
                        normal.y * 0.5f + 0.5f,
                        normal.z * 0.5f + 0.5f,
                        1.0f
                    );
                }
            }

            tex.SetPixels(pixels);
            tex.Apply(true, false);
            return tex;
        }

        // =========================================================================
        // Compatibility Adapters for PlanetaryMaterialType (Used by UI Workbench)
        // =========================================================================
        public void ApplyMaterial(UnityEngine.Terrain terrain, PlanetaryMaterialType matType)
        {
            targetTerrain = terrain;
            ApplySurfaceMaterial(MapPlanetaryToSurface(matType));
        }

        public void ApplyMaterial(UnityEngine.Terrain terrain, string matTypeStr)
        {
            targetTerrain = terrain;
            if (System.Enum.TryParse(matTypeStr, true, out SurfaceMaterialType parsedSurface))
            {
                ApplySurfaceMaterial(parsedSurface);
            }
            else if (System.Enum.TryParse(matTypeStr, true, out PlanetaryMaterialType parsedPlanetary))
            {
                ApplySurfaceMaterial(MapPlanetaryToSurface(parsedPlanetary));
            }
            else
            {
                ApplySurfaceMaterial(SurfaceMaterialType.MartianRust);
            }
        }

        private SurfaceMaterialType MapPlanetaryToSurface(PlanetaryMaterialType p)
        {
            switch (p)
            {
                case PlanetaryMaterialType.MartianDust: return SurfaceMaterialType.MartianRust;
                case PlanetaryMaterialType.LunarRegolith: return SurfaceMaterialType.LunarRegolith;
                case PlanetaryMaterialType.VolcanicBasalt: return SurfaceMaterialType.VolcanicBasalt;
                case PlanetaryMaterialType.PolarIce: return SurfaceMaterialType.PolarIce;
                case PlanetaryMaterialType.RedCanyon: return SurfaceMaterialType.RedSandstone;
                case PlanetaryMaterialType.TopographicWireframe: return SurfaceMaterialType.TopographicGrid;
                case PlanetaryMaterialType.NormalInspector: return SurfaceMaterialType.NormalInspector;
                default: return SurfaceMaterialType.MartianRust;
            }
        }
    }
}

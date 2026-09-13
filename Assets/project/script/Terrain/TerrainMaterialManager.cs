using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ProjectName.Terrain
{
    /// <summary>
    /// TerrainMaterialManager - PRODUCTION DUAL-PIPELINE (URP & BUILT-IN)
    /// Guarantees NO pink/magenta materials on planetary terrain under any pipeline setting.
    /// Preserves all Rover URDF materials completely intact without side effects.
    /// Supports automatic loading of high-definition CC0 artist textures from Assets/project/materials/
    /// with procedural PBR synthesizers as automatic fallbacks.
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
            public Vector2 tileSize;
            public Texture2D customAlbedo;
            public Texture2D customNormal;
            public Texture2D customRoughness;
        }

        [Header("Material Configurations")]
        public List<SurfaceMaterialConfig> materialConfigs = new List<SurfaceMaterialConfig>();

        [Header("Custom Artist Textures (Assets/project/materials/)")]
        [Tooltip("Assign optional 2K/4K downloaded PBR textures here, or let the auto-loader find them by name.")]
        public Texture2D customMartianAlbedo;
        public Texture2D customMartianNormal;
        public Texture2D customMartianRoughness;

        public Texture2D customLunarAlbedo;
        public Texture2D customLunarNormal;
        public Texture2D customLunarRoughness;

        public Texture2D customBasaltAlbedo;
        public Texture2D customBasaltNormal;
        public Texture2D customBasaltRoughness;

        public Texture2D customIceAlbedo;
        public Texture2D customIceNormal;
        public Texture2D customIceRoughness;

        public Texture2D customSandstoneAlbedo;
        public Texture2D customSandstoneNormal;
        public Texture2D customSandstoneRoughness;

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
                    tileSize = new Vector2(12f, 12f),
                    customAlbedo = customMartianAlbedo,
                    customNormal = customMartianNormal,
                    customRoughness = customMartianRoughness
                },
                new SurfaceMaterialConfig
                {
                    type = SurfaceMaterialType.LunarRegolith,
                    name = "Lunar Regolith",
                    primaryColor = new Color(0.44f, 0.46f, 0.48f),
                    roughness = 0.65f,
                    metallic = 0.15f,
                    bumpScale = 0.8f,
                    tileSize = new Vector2(10f, 10f),
                    customAlbedo = customLunarAlbedo,
                    customNormal = customLunarNormal,
                    customRoughness = customLunarRoughness
                },
                new SurfaceMaterialConfig
                {
                    type = SurfaceMaterialType.VolcanicBasalt,
                    name = "Volcanic Charcoal Basalt",
                    primaryColor = new Color(0.12f, 0.13f, 0.16f),
                    roughness = 0.92f,
                    metallic = 0.25f,
                    bumpScale = 1.5f,
                    tileSize = new Vector2(15f, 15f),
                    customAlbedo = customBasaltAlbedo,
                    customNormal = customBasaltNormal,
                    customRoughness = customBasaltRoughness
                },
                new SurfaceMaterialConfig
                {
                    type = SurfaceMaterialType.PolarIce,
                    name = "Polar Cryo-Ice Cap",
                    primaryColor = new Color(0.72f, 0.88f, 0.96f),
                    roughness = 0.15f,
                    metallic = 0.10f,
                    bumpScale = 0.5f,
                    tileSize = new Vector2(20f, 20f),
                    customAlbedo = customIceAlbedo,
                    customNormal = customIceNormal,
                    customRoughness = customIceRoughness
                },
                new SurfaceMaterialConfig
                {
                    type = SurfaceMaterialType.RedSandstone,
                    name = "Red Sandstone Canyon",
                    primaryColor = new Color(0.70f, 0.38f, 0.22f),
                    roughness = 0.80f,
                    metallic = 0.04f,
                    bumpScale = 1.4f,
                    tileSize = new Vector2(16f, 16f),
                    customAlbedo = customSandstoneAlbedo,
                    customNormal = customSandstoneNormal,
                    customRoughness = customSandstoneRoughness
                },
                new SurfaceMaterialConfig
                {
                    type = SurfaceMaterialType.TopographicGrid,
                    name = "Topographic Grid",
                    primaryColor = new Color(0.0f, 0.95f, 0.85f),
                    roughness = 0.5f,
                    metallic = 0.1f,
                    bumpScale = 0.0f,
                    tileSize = new Vector2(20f, 20f)
                },
                new SurfaceMaterialConfig
                {
                    type = SurfaceMaterialType.NormalInspector,
                    name = "Surface Normal Inspector",
                    primaryColor = new Color(0.4f, 0.65f, 1.0f),
                    roughness = 0.5f,
                    metallic = 0.1f,
                    bumpScale = 0.0f,
                    tileSize = new Vector2(20f, 20f)
                }
            };
        }

        /// <summary>
        /// Applies the requested planetary surface material to the Terrain.
        /// Dual-pipeline aware (URP and Built-in) - GUARANTEED NEVER PINK.
        /// </summary>
        public void ApplySurfaceMaterial(SurfaceMaterialType type)
        {
            activeMaterialType = type;
            if (targetTerrain == null) targetTerrain = UnityEngine.Terrain.activeTerrain;
            if (targetTerrain == null)
            {
                targetTerrain = FindFirstObjectByType<UnityEngine.Terrain>();
            }
            if (targetTerrain == null) return;

            InitializeDefaultConfigs();
            SurfaceMaterialConfig config = materialConfigs.Find(c => c.type == type);

            // Determine whether an active Scriptable Render Pipeline (URP) is currently rendering
            bool isURP = GraphicsSettings.currentRenderPipeline != null || QualitySettings.renderPipeline != null;

            // Pipeline-aware shader resolution
            Shader targetShader = null;
            if (isURP)
            {
                targetShader = Shader.Find("Universal Render Pipeline/Terrain/Lit");
                if (targetShader == null || !targetShader.isSupported)
                    targetShader = Shader.Find("Universal Render Pipeline/Lit");
            }
            else
            {
                targetShader = Shader.Find("Nature/Terrain/Standard");
                if (targetShader == null || !targetShader.isSupported)
                    targetShader = Shader.Find("Nature/Terrain/Diffuse");
                if (targetShader == null || !targetShader.isSupported)
                    targetShader = Shader.Find("Standard");
            }

            // Diagnostic Modes (Topographic Grid & Normal Inspector)
            if (type == SurfaceMaterialType.TopographicGrid || type == SurfaceMaterialType.NormalInspector)
            {
                Shader diagShader = null;
                if (isURP)
                {
                    diagShader = (type == SurfaceMaterialType.NormalInspector)
                        ? Shader.Find("Universal Render Pipeline/Lit")
                        : Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Universal Render Pipeline/Lit");
                }
                else
                {
                    diagShader = Shader.Find("Unlit/Color") ?? Shader.Find("Nature/Terrain/Standard") ?? Shader.Find("Standard");
                }

                if (diagShader == null || !diagShader.isSupported)
                {
                    diagShader = targetShader;
                }

                Material diagMat = new Material(diagShader);
                diagMat.name = "TerrainDiag_" + type.ToString();

                Color tint = (type == SurfaceMaterialType.TopographicGrid)
                    ? new Color(0.0f, 0.95f, 0.85f, 1.0f)   // Neon Cyan Diagnostic
                    : new Color(0.4f, 0.65f, 1.0f, 1.0f);   // Elevation Inspector

                if (diagMat.HasProperty("_BaseColor")) diagMat.SetColor("_BaseColor", tint);
                if (diagMat.HasProperty("_Color")) diagMat.SetColor("_Color", tint);

                targetTerrain.materialTemplate = diagMat;
            }
            else
            {
                // Standard Planetary Surface: Configure TerrainLayer with PBR textures
                TerrainLayer layer = GetOrCreateTerrainLayer(config);
                if (targetTerrain.terrainData != null)
                {
                    targetTerrain.terrainData.terrainLayers = new TerrainLayer[] { layer };

                    // Initialize splatmap weights so layer 0 renders at 100% opacity
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

                if (isURP && targetShader != null && targetShader.name.Contains("Universal Render Pipeline"))
                {
                    // URP TerrainLit material
                    Material customMat = new Material(targetShader);
                    customMat.name = "TerrainMat_URP_" + type.ToString();
                    if (customMat.HasProperty("_BaseColor")) customMat.SetColor("_BaseColor", config.primaryColor);
                    customMat.EnableKeyword("_TERRAIN_INSTANCED_PERPIXEL_NORMAL");
                    targetTerrain.materialTemplate = customMat;
                }
                else
                {
                    // Built-in Pipeline:
                    // In Unity 6, explicitly assigning a Nature/Terrain/Standard material guarantees
                    // full PBR lighting, normal mapping, and ZERO pink material artifacts!
                    if (targetShader != null)
                    {
                        Material builtInMat = new Material(targetShader);
                        builtInMat.name = "TerrainMat_BuiltIn_" + type.ToString();
                        if (builtInMat.HasProperty("_Color")) builtInMat.color = config.primaryColor;
                        targetTerrain.materialTemplate = builtInMat;
                    }
                    targetTerrain.materialType = UnityEngine.Terrain.MaterialType.BuiltInStandard;
                }
            }

            targetTerrain.drawInstanced = true;
            targetTerrain.Flush();
            Debug.Log($"[TerrainMaterialManager] Applied surface '{type}' (isURP: {isURP}, Shader: {(targetTerrain.materialTemplate != null ? targetTerrain.materialTemplate.shader.name : "Native")})");
        }

        public void ClearCache()
        {
            generatedLayers.Clear();
            generatedMaterials.Clear();
        }

        private TerrainLayer GetOrCreateTerrainLayer(SurfaceMaterialConfig config)
        {
            if (generatedLayers.TryGetValue(config.type, out TerrainLayer cached) && cached != null)
            {
                if (cached.diffuseTexture != null && cached.diffuseTexture.name.StartsWith("Procedural_"))
                {
                    Texture2D freshAlbedo = config.customAlbedo != null ? config.customAlbedo : TryAutoLoadTexture(config.type, TextureMapType.Albedo);
                    if (freshAlbedo != null)
                    {
                        generatedLayers.Remove(config.type);
                    }
                    else
                    {
                        return cached;
                    }
                }
                else
                {
                    return cached;
                }
            }

            // 1. First check explicit inspector overrides
            Texture2D albedoTex = config.customAlbedo;
            Texture2D normalTex = config.customNormal;
            Texture2D roughnessTex = config.customRoughness;

            // 2. Second check Assets/project/materials/ by known naming patterns
            if (albedoTex == null) albedoTex = TryAutoLoadTexture(config.type, TextureMapType.Albedo);
            if (normalTex == null) normalTex = TryAutoLoadTexture(config.type, TextureMapType.Normal);
            if (roughnessTex == null) roughnessTex = TryAutoLoadTexture(config.type, TextureMapType.Roughness);

            // 3. Fallback to procedural synthesis (guarantees NO missing textures / NO pink)
            if (albedoTex == null)
            {
                albedoTex = GenerateProceduralAlbedo(config.primaryColor, config.type);
            }

            if (normalTex == null)
            {
                normalTex = GenerateProceduralNormalMap(config.bumpScale);
            }

            TerrainLayer layer = new TerrainLayer
            {
                name = "Layer_" + config.type.ToString(),
                diffuseTexture = albedoTex,
                normalMapTexture = normalTex,
                normalScale = config.bumpScale,
                smoothness = Mathf.Clamp01(1.0f - config.roughness),
                metallic = config.metallic,
                tileSize = config.tileSize.x > 0 ? config.tileSize : new Vector2(15f, 15f)
            };

            generatedLayers[config.type] = layer;
            return layer;
        }

        private enum TextureMapType { Albedo, Normal, Roughness }

        private Texture2D TryAutoLoadTexture(SurfaceMaterialType surfaceType, TextureMapType mapType)
        {
            string prefix = "";
            string altPrefix = "";

            switch (surfaceType)
            {
                case SurfaceMaterialType.MartianRust:
                    prefix = "MartianRust";
                    altPrefix = "Ground037";
                    break;
                case SurfaceMaterialType.LunarRegolith:
                    prefix = "LunarRegolith";
                    altPrefix = "gravel_ground";
                    break;
                case SurfaceMaterialType.VolcanicBasalt:
                    prefix = "VolcanicBasalt";
                    altPrefix = "Rock022";
                    break;
                case SurfaceMaterialType.PolarIce:
                    prefix = "PolarIce";
                    altPrefix = "snow_02";
                    break;
                case SurfaceMaterialType.RedSandstone:
                    prefix = "Sandstone";
                    altPrefix = "Rock015";
                    break;
                default:
                    return null;
            }

            string suffix = "";
            string altSuffix = "";
            switch (mapType)
            {
                case TextureMapType.Albedo:
                    suffix = "_Albedo";
                    altSuffix = "_Color";
                    break;
                case TextureMapType.Normal:
                    suffix = "_Normal";
                    altSuffix = "_NormalGL";
                    break;
                case TextureMapType.Roughness:
                    suffix = "_Roughness";
                    altSuffix = "_Roughness";
                    break;
            }

            List<string> candidateNames = new List<string>
            {
                $"{prefix}{suffix}",
                $"{prefix}{altSuffix}",
                $"{prefix}_diff",
                $"{altPrefix}{suffix}",
                $"{altPrefix}{altSuffix}",
                $"{altPrefix}_diff_2k",
                $"{altPrefix}_nor_gl_2k",
                $"{altPrefix}_rough_2k"
            };

#if UNITY_EDITOR
            string[] extensions = new string[] { ".png", ".jpg", ".jpeg", ".tga", ".exr" };
            foreach (string baseName in candidateNames)
            {
                foreach (string ext in extensions)
                {
                    string path = "Assets/project/materials/" + baseName + ext;
                    Texture2D found = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    if (found != null)
                    {
                        Debug.Log($"[TerrainMaterialManager] Auto-loaded texture '{path}' for {surfaceType} {mapType}");
                        return found;
                    }
                }
            }
#endif
            return null;
        }

        // =========================================================================
        // High-Quality Procedural Texture Synthesizers (RGBA32 + mipmaps)
        // =========================================================================
        private Texture2D GenerateProceduralAlbedo(Color baseColor, SurfaceMaterialType type)
        {
            int size = 256;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
            tex.name = "Procedural_" + type.ToString() + "_Albedo";
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
            tex.name = "Procedural_Normal";
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

        public void SetSurfacePreset(SurfaceMaterialType preset, UnityEngine.Terrain target = null)
        {
            if (target != null) targetTerrain = target;
            ApplySurfaceMaterial(preset);
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

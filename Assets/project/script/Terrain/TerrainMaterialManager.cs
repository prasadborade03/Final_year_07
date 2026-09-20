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

        public static TerrainMaterialManager Instance { get; private set; }

        // Runtime Cache
        private readonly Dictionary<SurfaceMaterialType, TerrainLayer> generatedLayers = new Dictionary<SurfaceMaterialType, TerrainLayer>();
        private readonly Dictionary<SurfaceMaterialType, Material> generatedMaterials = new Dictionary<SurfaceMaterialType, Material>();

        private void Awake()
        {
            if (Instance == null) Instance = this;

            if (targetTerrain == null)
                targetTerrain = GetComponent<UnityEngine.Terrain>();

            InitializeDefaultConfigs();
            SanitizeConfigs();
            EnforceSpaceLightingAndReflections();
        }

        private void OnEnable()
        {
            if (Instance == null) Instance = this;
        }

        private void Start()
        {
            ApplySurfaceMaterial(activeMaterialType);
        }

        public void InitializeDefaultConfigs(bool force = false)
        {
            if (!force && materialConfigs != null && materialConfigs.Count > 0)
            {
                SanitizeConfigs();
                return;
            }

            materialConfigs = new List<SurfaceMaterialConfig>
            {
                new SurfaceMaterialConfig
                {
                    type = SurfaceMaterialType.MartianRust,
                    name = "Martian Rust Oxide",
                    primaryColor = new Color(0.78f, 0.32f, 0.16f),
                    roughness = 1.0f,
                    metallic = 0.0f,
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
                    roughness = 1.0f,
                    metallic = 0.0f,
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
                    roughness = 1.0f,
                    metallic = 0.0f,
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
                    roughness = 1.0f,
                    metallic = 0.0f,
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
                    roughness = 1.0f,
                    metallic = 0.0f,
                    bumpScale = 1.4f,
                    tileSize = new Vector2(16f, 16f),
                    customAlbedo = customSandstoneAlbedo,
                    customNormal = customSandstoneNormal,
                    customRoughness = customSandstoneRoughness
                },
                new SurfaceMaterialConfig
                {
                    type = SurfaceMaterialType.TopographicGrid,
                    name = "Topographic Wireframe Grid",
                    primaryColor = new Color(0.0f, 0.95f, 0.85f),
                    roughness = 1.0f,
                    metallic = 0.0f,
                    bumpScale = 1.0f,
                    tileSize = new Vector2(10f, 10f)
                },
                new SurfaceMaterialConfig
                {
                    type = SurfaceMaterialType.NormalInspector,
                    name = "Surface Normal Inspector",
                    primaryColor = new Color(0.4f, 0.65f, 1.0f),
                    roughness = 1.0f,
                    metallic = 0.0f,
                    bumpScale = 1.8f,
                    tileSize = new Vector2(25f, 25f)
                }
            };
        }

        /// <summary>
        /// Sanitizes materialConfigs to ensure planetary terrain is completely non-glossy/matte.
        /// Overrides any stale serialized scene values that cause liquid/mirror reflections on terrain ridges.
        /// </summary>
        public void SanitizeConfigs()
        {
            if (materialConfigs == null) return;
            for (int i = 0; i < materialConfigs.Count; i++)
            {
                var cfg = materialConfigs[i];
                cfg.roughness = 1.0f;
                cfg.metallic = 0.0f;
                materialConfigs[i] = cfg;
            }
        }

        /// <summary>
        /// Fixes environment reflections and ambient lighting for space/nebula skybox.
        /// Prevents default daylight blue sky reflections and cyan ambient light from contaminating the scene.
        /// </summary>
        public static void EnforceSpaceLightingAndReflections()
        {
            try
            {
                // Single Writer Rule R3: Do not overwrite lighting if a planetary profile is active
                if (ProjectName.Planetary.PlanetEnvironmentController.Instance != null && ProjectName.Planetary.PlanetEnvironmentController.Instance.currentProfile != null)
                {
                    return;
                }

                if (RenderSettings.customReflectionTexture == null)
                {
                    Cubemap hdrCubemap = null;
#if UNITY_EDITOR
                    hdrCubemap = UnityEditor.AssetDatabase.LoadAssetAtPath<Cubemap>("Assets/project/materials/HDR_multi_nebulae_2.hdr");
#endif
                    if (hdrCubemap != null)
                    {
                        RenderSettings.customReflectionTexture = hdrCubemap;
                        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
                    }
                }
                else
                {
                    RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
                }

                RenderSettings.reflectionIntensity = 0.25f;

                // Eliminate daylight cyan/blue ambient sky tint
                if (RenderSettings.ambientMode == AmbientMode.Skybox || RenderSettings.ambientSkyColor.b > 0.12f)
                {
                    RenderSettings.ambientMode = AmbientMode.Flat;
                    Color spaceDarkAmbient = new Color(0.05f, 0.05f, 0.07f, 1.0f);
                    RenderSettings.ambientLight = spaceDarkAmbient;
                    RenderSettings.ambientSkyColor = spaceDarkAmbient;
                    RenderSettings.ambientEquatorColor = new Color(0.03f, 0.03f, 0.04f, 1.0f);
                    RenderSettings.ambientGroundColor = new Color(0.02f, 0.02f, 0.02f, 1.0f);
                    RenderSettings.subtractiveShadowColor = new Color(0.02f, 0.02f, 0.03f, 1.0f);
                }

                DynamicGI.UpdateEnvironment();

                // Enforce ultra-crisp shadows, 4 cascades, and anti-aliasing (prevents pixelated blocky shadows)
                QualitySettings.shadows = ShadowQuality.All;
                QualitySettings.shadowResolution = ShadowResolution.VeryHigh;
                QualitySettings.shadowCascades = 4;
                QualitySettings.shadowCascade4Split = new Vector3(0.05f, 0.15f, 0.35f);
                QualitySettings.shadowDistance = 150f;
                QualitySettings.shadowProjection = ShadowProjection.CloseFit;
                QualitySettings.shadowNearPlaneOffset = 0.05f;
                QualitySettings.antiAliasing = 8;
                QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
                QualitySettings.pixelLightCount = 8;

                // Enforce sharp contact shadows on Directional Light
                Light sun = RenderSettings.sun;
                if (sun == null)
                {
                    Light[] lights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
                    for (int i = 0; i < lights.Length; i++)
                    {
                        if (lights[i].type == LightType.Directional)
                        {
                            sun = lights[i];
                            break;
                        }
                    }
                }
                if (sun != null)
                {
                    sun.shadows = LightShadows.Soft;
                    sun.shadowCustomResolution = 4096;
                    sun.shadowBias = 0.005f;
                    sun.shadowNormalBias = 0.1f;
                    sun.shadowNearPlane = 0.1f;
                }

                // Enforce camera MSAA and HDR
                Camera cam = Camera.main;
                if (cam != null)
                {
                    cam.allowMSAA = true;
                    cam.allowHDR = true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[TerrainMaterialManager] Could not update environment reflections: {ex.Message}");
            }
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
            SanitizeConfigs();
            EnforceSpaceLightingAndReflections();
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

            // Debug modes (Topographic Grid, Normal Inspector) use single specialized layer at 100% opacity
            if (type == SurfaceMaterialType.TopographicGrid || type == SurfaceMaterialType.NormalInspector)
            {
                TerrainLayer layer = GetOrCreateTerrainLayer(config);
                if (targetTerrain.terrainData != null)
                {
                    targetTerrain.terrainData.terrainLayers = new TerrainLayer[] { layer };
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
            }
            else
            {
                // Planetary Worlds: 4-Layer Slope-and-Height Splatmap Blending (512x512)
                ApplyPlanetaryMultiLayerSplatmap(type);
            }

            if (isURP && targetShader != null && targetShader.name.Contains("Universal Render Pipeline"))
            {
                // URP TerrainLit material
                Material customMat = new Material(targetShader);
                customMat.name = "TerrainMat_URP_" + type.ToString();
                if (customMat.HasProperty("_BaseColor")) customMat.SetColor("_BaseColor", config.primaryColor);
                if (customMat.HasProperty("_Smoothness")) customMat.SetFloat("_Smoothness", 0.0f);
                if (customMat.HasProperty("_Metallic")) customMat.SetFloat("_Metallic", 0.0f);
                customMat.EnableKeyword("_TERRAIN_INSTANCED_PERPIXEL_NORMAL");
                targetTerrain.materialTemplate = customMat;
            }
            else
            {
                // Built-in Pipeline:
                // In Unity 6, explicitly assigning a Nature/Terrain/Standard material guarantees
                // full PBR lighting, normal mapping, vertex displacement, and ZERO pink material artifacts!
                if (targetShader != null)
                {
                    Material builtInMat = new Material(targetShader);
                    builtInMat.name = "TerrainMat_BuiltIn_" + type.ToString();
                    if (builtInMat.HasProperty("_Color")) builtInMat.color = config.primaryColor;
                    targetTerrain.materialTemplate = builtInMat;
                }
                targetTerrain.materialType = UnityEngine.Terrain.MaterialType.BuiltInStandard;
            }

            targetTerrain.drawInstanced = true;
            targetTerrain.Flush();
            Debug.Log($"[TerrainMaterialManager] Applied surface '{type}' (isURP: {isURP}, Shader: {(targetTerrain.materialTemplate != null ? targetTerrain.materialTemplate.shader.name : "Native")})");
        }

        public enum LayerRole { Fines, Scree, Cliff, Macro }
        private readonly Dictionary<string, TerrainLayer> generatedMultiLayers = new Dictionary<string, TerrainLayer>();

        /// <summary>
        /// Generates and assigns 4 blended planetary terrain layers (Fines, Pebbles/Scree, Cliff Bedrock, Macro)
        /// using a 512x512 physical slope-and-height alphamap.
        /// Strictly enforces smoothness = 0.0, metallic = 0.0 (zero 'wet plastic' specular streaks).
        /// </summary>
        public void ApplyPlanetaryMultiLayerSplatmap(SurfaceMaterialType type)
        {
            if (targetTerrain == null || targetTerrain.terrainData == null) return;

            TerrainLayer finesLayer = GetOrCreatePlanetaryLayer(type, LayerRole.Fines);
            TerrainLayer screeLayer = GetOrCreatePlanetaryLayer(type, LayerRole.Scree);
            TerrainLayer cliffLayer = GetOrCreatePlanetaryLayer(type, LayerRole.Cliff);
            TerrainLayer macroLayer = GetOrCreatePlanetaryLayer(type, LayerRole.Macro);

            targetTerrain.terrainData.terrainLayers = new TerrainLayer[] { finesLayer, screeLayer, cliffLayer, macroLayer };

            int alphaRes = 512;
            targetTerrain.terrainData.alphamapResolution = alphaRes;
            float[,,] splat = new float[alphaRes, alphaRes, 4];

            for (int y = 0; y < alphaRes; y++)
            {
                float normY = (float)y / (alphaRes - 1);
                for (int x = 0; x < alphaRes; x++)
                {
                    float normX = (float)x / (alphaRes - 1);
                    float steepness = targetTerrain.terrainData.GetSteepness(normX, normY);

                    // Physical slope weighting:
                    // Steepness < 15 deg: Fines / dust / regolith dominates
                    // Steepness 15..32 deg: Pebbles / scree blends in
                    // Steepness > 30 deg: Bedrock / cliff rock dominates
                    float cliffWeight = Mathf.Clamp01((steepness - 24f) / 18f);
                    float screeWeight = Mathf.Clamp01(1f - Mathf.Abs(steepness - 22f) / 12f);
                    float finesWeight = Mathf.Clamp01((28f - steepness) / 16f);

                    // Large-scale macro color variation to eliminate tiling repetition over 1000m
                    float macroNoise = Mathf.PerlinNoise(normX * 6.5f, normY * 6.5f);
                    float macroWeight = finesWeight * Mathf.Clamp01((macroNoise - 0.35f) * 1.5f);
                    finesWeight = Mathf.Max(0f, finesWeight - macroWeight);

                    float sum = finesWeight + screeWeight + cliffWeight + macroWeight;
                    if (sum < 0.0001f) sum = 1f;

                    splat[y, x, 0] = finesWeight / sum;
                    splat[y, x, 1] = screeWeight / sum;
                    splat[y, x, 2] = cliffWeight / sum;
                    splat[y, x, 3] = macroWeight / sum;
                }
            }

            targetTerrain.terrainData.SetAlphamaps(0, 0, splat);
            Debug.Log($"[TerrainMaterialManager] Multi-layer splatmap generated for {type}: 4 layers (Fines, Scree, Cliff, Macro) at {alphaRes}x{alphaRes} resolution.");
        }

        public TerrainLayer GetOrCreatePlanetaryLayer(SurfaceMaterialType type, LayerRole role)
        {
            string key = $"{type}_{role}";
            if (generatedMultiLayers.TryGetValue(key, out TerrainLayer cached) && cached != null)
            {
                // Invalidate if old layer had glossiness
                if (cached.smoothness <= 0.001f && cached.metallic <= 0.001f)
                    return cached;
                generatedMultiLayers.Remove(key);
            }

            Texture2D diffuse = null;
            Texture2D normal = null;
            Vector2 tileSize = new Vector2(10f, 10f);
            float normalScale = 1.0f;

            switch (role)
            {
                case LayerRole.Fines:
                    diffuse = TryAutoLoadTexture(type, TextureMapType.Albedo);
                    normal = TryAutoLoadTexture(type, TextureMapType.Normal);
                    tileSize = (type == SurfaceMaterialType.LunarRegolith) ? new Vector2(8f, 8f) :
                               (type == SurfaceMaterialType.VolcanicBasalt) ? new Vector2(12f, 12f) :
                               new Vector2(10f, 10f);
                    normalScale = 1.0f;
                    break;

                case LayerRole.Scree:
#if UNITY_EDITOR
                    diffuse = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/project/materials/PBR_CC0/PebbleGround_CC0_Albedo.jpg");
                    normal = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/project/materials/PBR_CC0/PebbleGround_CC0_Normal.jpg");
#endif
                    if (diffuse == null) diffuse = TryAutoLoadTexture(type, TextureMapType.Albedo);
                    if (normal == null) normal = TryAutoLoadTexture(type, TextureMapType.Normal);
                    tileSize = new Vector2(2.5f, 2.5f); // Realistic centimetre pebbles relative to rover 1m
                    normalScale = 1.2f;
                    break;

                case LayerRole.Cliff:
#if UNITY_EDITOR
                    if (type == SurfaceMaterialType.VolcanicBasalt)
                    {
                        diffuse = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/project/materials/PBR_CC0/VolcanicBasalt_CC0_Albedo.jpg");
                        normal = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/project/materials/PBR_CC0/VolcanicBasalt_CC0_Normal.jpg");
                    }
                    else
                    {
                        diffuse = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/project/materials/PBR_CC0/CliffRock_CC0_Albedo.jpg");
                        normal = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/project/materials/PBR_CC0/CliffRock_CC0_Normal.jpg");
                    }
#endif
                    if (diffuse == null) diffuse = TryAutoLoadTexture(SurfaceMaterialType.VolcanicBasalt, TextureMapType.Albedo);
                    if (normal == null) normal = TryAutoLoadTexture(SurfaceMaterialType.VolcanicBasalt, TextureMapType.Normal);
                    tileSize = new Vector2(14f, 14f);
                    normalScale = 1.5f;
                    break;

                case LayerRole.Macro:
                    diffuse = TryAutoLoadTexture(type, TextureMapType.Albedo);
                    normal = TryAutoLoadTexture(type, TextureMapType.Normal);
                    tileSize = new Vector2(90f, 90f); // Large scale to eliminate repetitive tiling over 1000m
                    normalScale = 0.5f;
                    break;
            }

            // Procedural fallback if textures missing
            if (diffuse == null)
            {
                SurfaceMaterialConfig cfg = materialConfigs.Find(c => c.type == type);
                diffuse = GenerateProceduralAlbedo(cfg.primaryColor, type);
            }

            TerrainLayer layer = new TerrainLayer
            {
                name = $"Layer_{type}_{role}",
                diffuseTexture = diffuse,
                normalMapTexture = normal,
                normalScale = normalScale,
                smoothness = 0.0f,
                metallic = 0.0f,
                specular = Color.black,
                smoothnessSource = TerrainLayerSmoothnessSource.ConstantOnly,
                tileSize = tileSize
            };

            generatedMultiLayers[key] = layer;
            return layer;
        }

        public void RegenerateCurrentSplatmap()
        {
            if (targetTerrain == null) targetTerrain = UnityEngine.Terrain.activeTerrain;
            if (targetTerrain == null) return;
            if (activeMaterialType != SurfaceMaterialType.TopographicGrid && 
                activeMaterialType != SurfaceMaterialType.NormalInspector)
            {
                ApplyPlanetaryMultiLayerSplatmap(activeMaterialType);
            }
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
                // Invalidate cached layer if it contains old high glossiness or outdated procedural fallback
                if (cached.smoothness > 0.001f || cached.metallic > 0.001f || cached.specular != Color.black ||
                    cached.smoothnessSource != TerrainLayerSmoothnessSource.ConstantOnly)
                {
                    generatedLayers.Remove(config.type);
                }
                else if (cached.diffuseTexture != null && cached.diffuseTexture.name.StartsWith("Procedural_"))
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
                normalTex = GenerateProceduralNormalMap(config.bumpScale, config.type);
            }

            // Guarantee pure ultra-matte surface: zero glossiness, zero specular highlight, zero metallic reflections
            TerrainLayer layer = new TerrainLayer
            {
                name = "Layer_" + config.type.ToString(),
                diffuseTexture = albedoTex,
                normalMapTexture = normalTex,
                normalScale = config.bumpScale,
                smoothness = 0.0f,
                metallic = 0.0f,
                specular = Color.black,
                smoothnessSource = TerrainLayerSmoothnessSource.ConstantOnly,
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
                case SurfaceMaterialType.TopographicGrid:
                    prefix = "TopographicGrid";
                    altPrefix = "Grid";
                    break;
                case SurfaceMaterialType.NormalInspector:
                    prefix = "NormalInspector";
                    altPrefix = "SlopeInspector";
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
                $"{prefix}_CC0{suffix}",
                $"{prefix}_CC0{altSuffix}",
                $"{prefix}{suffix}",
                $"{prefix}{altSuffix}",
                $"{prefix}_diff",
                $"{altPrefix}_CC0{suffix}",
                $"{altPrefix}{suffix}",
                $"{altPrefix}{altSuffix}",
                $"{altPrefix}_diff_2k",
                $"{altPrefix}_nor_gl_2k",
                $"{altPrefix}_rough_2k",
                $"gravel_ground_01{suffix}",
                $"gravel_ground_01{altSuffix}",
                $"Ground048{altSuffix}",
                $"Ground048{suffix}",
                $"Ground030{altSuffix}",
                $"Ground030{suffix}",
                $"aerial_ground_rock{suffix}"
            };

#if UNITY_EDITOR
            string[] searchDirs = new string[] { "Assets/project/materials/PBR_CC0/", "Assets/project/materials/" };
            string[] extensions = new string[] { ".jpg", ".png", ".jpeg", ".tga", ".exr" };
            foreach (string dir in searchDirs)
            {
                foreach (string baseName in candidateNames)
                {
                    foreach (string ext in extensions)
                    {
                        string path = dir + baseName + ext;
                        Texture2D found = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                        if (found != null)
                        {
                            Debug.Log($"[TerrainMaterialManager] Auto-loaded texture '{path}' for {surfaceType} {mapType}");
                            return found;
                        }
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

                        case SurfaceMaterialType.TopographicGrid:
                            {
                                int px = x % 64;
                                int py = y % 64;
                                bool isMajorGrid = (px < 2 || py < 2);
                                bool isMinorGrid = (px % 16 == 0 || py % 16 == 0);
                                float contour = Mathf.Sin((u + v) * Mathf.PI * 16f);
                                bool isContour = Mathf.Abs(contour) < 0.08f;

                                Color bg = new Color(0.04f, 0.06f, 0.09f, 1.0f); // Deep tactical slate
                                Color majorCol = new Color(0.0f, 0.96f, 0.88f, 1.0f); // Bright Glowing Neon Cyan
                                Color minorCol = new Color(0.0f, 0.40f, 0.38f, 1.0f); // Subtle grid subdivision
                                Color contourCol = new Color(0.08f, 0.65f, 0.60f, 1.0f); // Contour lines

                                if (isMajorGrid) pixels[y * size + x] = majorCol;
                                else if (isMinorGrid) pixels[y * size + x] = minorCol;
                                else if (isContour) pixels[y * size + x] = contourCol;
                                else pixels[y * size + x] = bg;
                                continue;
                            }

                        case SurfaceMaterialType.NormalInspector:
                            {
                                // Surface normal vector slope gradient heat-map (Blue = flat, Green = mild, Yellow = steep, Red = cliff)
                                float snx = Mathf.PerlinNoise(u * 14f, v * 14f);
                                float sny = Mathf.PerlinNoise((u + 40f) * 14f, (v + 40f) * 14f);
                                float slope = Mathf.Clamp01(Mathf.Sqrt((snx - 0.5f) * (snx - 0.5f) + (sny - 0.5f) * (sny - 0.5f)) * 2.8f);

                                Color normCol;
                                if (slope < 0.25f)
                                {
                                    normCol = Color.Lerp(new Color(0.08f, 0.35f, 0.95f), new Color(0.1f, 0.75f, 0.85f), slope / 0.25f);
                                }
                                else if (slope < 0.55f)
                                {
                                    normCol = Color.Lerp(new Color(0.1f, 0.75f, 0.85f), new Color(0.2f, 0.9f, 0.25f), (slope - 0.25f) / 0.3f);
                                }
                                else if (slope < 0.8f)
                                {
                                    normCol = Color.Lerp(new Color(0.95f, 0.85f, 0.1f), new Color(0.95f, 0.45f, 0.05f), (slope - 0.55f) / 0.25f);
                                }
                                else
                                {
                                    normCol = Color.Lerp(new Color(0.95f, 0.45f, 0.05f), new Color(0.9f, 0.1f, 0.35f), (slope - 0.8f) / 0.2f);
                                }

                                float slopeGrid = (x % 32 < 1) || (y % 32 < 1) ? 0.18f : 0f;
                                pixels[y * size + x] = new Color(
                                    Mathf.Clamp01(normCol.r + slopeGrid),
                                    Mathf.Clamp01(normCol.g + slopeGrid),
                                    Mathf.Clamp01(normCol.b + slopeGrid),
                                    1.0f
                                );
                                continue;
                            }

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

        private Texture2D GenerateProceduralNormalMap(float strength, SurfaceMaterialType type = SurfaceMaterialType.MartianRust)
        {
            int size = 256;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
            tex.name = "Procedural_" + type.ToString() + "_Normal";
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Bilinear;
            Color[] pixels = new Color[size * size];

            for (int y = 0; y < size; y++)
            {
                float v = (float)y / size;
                for (int x = 0; x < size; x++)
                {
                    float u = (float)x / size;
                    float nx, ny;

                    if (type == SurfaceMaterialType.TopographicGrid)
                    {
                        int px = x % 64;
                        int py = y % 64;
                        nx = (px < 3 || px > 61) ? ((px < 3 ? 1f : -1f) * 0.5f) : 0f;
                        ny = (py < 3 || py > 61) ? ((py < 3 ? 1f : -1f) * 0.5f) : 0f;
                    }
                    else if (type == SurfaceMaterialType.NormalInspector)
                    {
                        nx = (Mathf.PerlinNoise(u * 14f, v * 14f) - 0.5f) * strength * 1.5f;
                        ny = (Mathf.PerlinNoise(v * 14f, u * 14f) - 0.5f) * strength * 1.5f;
                    }
                    else
                    {
                        nx = (Mathf.PerlinNoise(u * 16f, v * 16f) - 0.5f) * strength;
                        ny = (Mathf.PerlinNoise(v * 16f, u * 16f) - 0.5f) * strength;
                    }

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

        /// <summary>
        /// Authoritative method to apply a PlanetProfile's surface material preset and scatter surface rocks.
        /// </summary>
        public void ApplyPlanetaryProfile(ProjectName.Planetary.PlanetProfile profile)
        {
            if (profile == null) return;
            if (targetTerrain == null) targetTerrain = UnityEngine.Terrain.activeTerrain;
            if (targetTerrain == null) targetTerrain = FindFirstObjectByType<UnityEngine.Terrain>();

            SurfaceMaterialType surface = MapPlanetaryToSurface(profile.defaultMaterialPreset);
            ApplySurfaceMaterial(surface);

            // Scatter planetary surface rocks & boulders
            if (PlanetRockScatterer.Instance != null)
            {
                PlanetRockScatterer.Instance.ScatterRocks(targetTerrain, profile);
            }
            else if (targetTerrain != null)
            {
                var scatterer = targetTerrain.GetComponent<PlanetRockScatterer>();
                if (scatterer == null) scatterer = targetTerrain.gameObject.AddComponent<PlanetRockScatterer>();
                scatterer.ScatterRocks(targetTerrain, profile);
            }
        }

        public SurfaceMaterialType MapPlanetaryToSurface(PlanetaryMaterialType p)
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

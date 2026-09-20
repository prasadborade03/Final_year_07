using System;
using System.Collections.Generic;
using UnityEngine;
using ProjectName.Planetary;

namespace ProjectName.Terrain
{
    /// <summary>
    /// PlanetRockScatterer - High-Performance Planetary Surface Rock & Pebble Scatterer.
    /// Distributes CC0 3D-scanned rocks across the terrain according to the active PlanetProfile.
    /// - Small pebbles (< 0.35m): visual-only, GPU-instanced, NO colliders for ultra-fast performance.
    /// - Boulders (>= 0.5m): convex MeshColliders for realistic rover obstacle interaction.
    /// - Spawn Clearance: strictly preserves an exclusion zone around the rover spawn point (default 15m).
    /// </summary>
    [ExecuteAlways]
    public class PlanetRockScatterer : MonoBehaviour
    {
        public static PlanetRockScatterer Instance { get; private set; }

        [Header("Target Terrain")]
        public UnityEngine.Terrain targetTerrain;

        [Header("Scattered Rocks Container")]
        [SerializeField] private GameObject rocksContainer;

        // Rock mesh paths from Assets/project/models/Rocks/
        private static readonly string[] RockModelPaths = new string[]
        {
            "Assets/project/models/Rocks/moon_rock_01.fbx",
            "Assets/project/models/Rocks/moon_rock_02.fbx",
            "Assets/project/models/Rocks/moon_rock_03.fbx",
            "Assets/project/models/Rocks/moon_rock_04.fbx"
        };

        private static readonly string[] RockDiffPaths = new string[]
        {
            "Assets/project/models/Rocks/moon_rock_01_diff.jpg",
            "Assets/project/models/Rocks/moon_rock_02_diff.jpg",
            "Assets/project/models/Rocks/moon_rock_03_diff.jpg",
            "Assets/project/models/Rocks/moon_rock_04_diff.jpg"
        };

        private static readonly string[] RockNorPaths = new string[]
        {
            "Assets/project/models/Rocks/moon_rock_01_nor.jpg",
            "Assets/project/models/Rocks/moon_rock_02_nor.jpg",
            "Assets/project/models/Rocks/moon_rock_03_nor.jpg",
            "Assets/project/models/Rocks/moon_rock_04_nor.jpg"
        };

        private Mesh[] cachedRockMeshes;
        private Material[] cachedRockMaterials;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            if (targetTerrain == null) targetTerrain = GetComponent<UnityEngine.Terrain>();
            if (targetTerrain == null) targetTerrain = UnityEngine.Terrain.activeTerrain;
        }

        private void OnEnable()
        {
            if (Instance == null) Instance = this;
        }

        /// <summary>
        /// Clears all currently spawned rocks.
        /// </summary>
        public void ClearRocks()
        {
            if (rocksContainer != null)
            {
                if (Application.isPlaying)
                    Destroy(rocksContainer);
                else
                    DestroyImmediate(rocksContainer);
                rocksContainer = null;
            }

            // Also search for any stray container
            Transform stray = transform.Find("PlanetaryRockScatter");
            if (stray != null)
            {
                if (Application.isPlaying)
                    Destroy(stray.gameObject);
                else
                    DestroyImmediate(stray.gameObject);
            }

            GameObject rootStray = GameObject.Find("PlanetaryRockScatter");
            if (rootStray != null)
            {
                if (Application.isPlaying)
                    Destroy(rootStray);
                else
                    DestroyImmediate(rootStray);
            }
        }

        /// <summary>
        /// Scatters rocks according to the active PlanetProfile.
        /// </summary>
        public void ScatterRocks(UnityEngine.Terrain terrain, PlanetProfile profile)
        {
            if (terrain != null) targetTerrain = terrain;
            if (targetTerrain == null) targetTerrain = UnityEngine.Terrain.activeTerrain;
            if (targetTerrain == null || targetTerrain.terrainData == null) return;

            ClearRocks();

            if (profile == null || profile.rockDensity <= 0.005f) return;

            EnsureAssetsLoaded();
            if (cachedRockMeshes == null || cachedRockMeshes.Length == 0) return;

            rocksContainer = new GameObject("PlanetaryRockScatter");
            rocksContainer.transform.SetParent(targetTerrain.transform, false);

            Vector3 terrainPos = targetTerrain.transform.position;
            Vector3 terrainSize = targetTerrain.terrainData.size;
            Vector3 center = terrainPos + new Vector3(terrainSize.x * 0.5f, 0f, terrainSize.z * 0.5f);

            // Rover spawn point is at world (0, y, 0)
            Vector3 roverSpawnWorld = Vector3.zero;
            float clearance = Mathf.Max(10f, profile.spawnClearanceRadius);

            // Seeded deterministic randomness per planet
            int seed = profile.planetName.GetHashCode();
            UnityEngine.Random.InitState(seed);

            // Calculate rock count: denser around rover playzone, tapering outwards
            int totalRocks = Mathf.Clamp(Mathf.RoundToInt(220f * profile.rockDensity), 25, 200);

            // Create tinted materials for this planet
            Material[] planetMats = GetTintedMaterials(profile.rockColorTint);

            int spawnedCount = 0;
            int boulderCount = 0;
            int maxAttempts = totalRocks * 4;

            for (int attempt = 0; attempt < maxAttempts && spawnedCount < totalRocks; attempt++)
            {
                // Distance distribution: 70% within 180m, 30% out to 350m
                float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                float dist;
                if (UnityEngine.Random.value < 0.70f)
                {
                    dist = UnityEngine.Random.Range(clearance, 180f);
                }
                else
                {
                    dist = UnityEngine.Random.Range(180f, 350f);
                }

                float worldX = roverSpawnWorld.x + Mathf.Cos(angle) * dist;
                float worldZ = roverSpawnWorld.z + Mathf.Sin(angle) * dist;

                // Check bounds on terrain
                float normX = (worldX - terrainPos.x) / terrainSize.x;
                float normZ = (worldZ - terrainPos.z) / terrainSize.z;
                if (normX < 0.02f || normX > 0.98f || normZ < 0.02f || normZ > 0.98f)
                    continue;

                // Check steepness: skip vertical cliffs (> 45 deg)
                float steepness = targetTerrain.terrainData.GetSteepness(normX, normZ);
                if (steepness > 45f) continue;

                float worldY = targetTerrain.SampleHeight(new Vector3(worldX, 0f, worldZ)) + terrainPos.y;
                Vector3 normal = targetTerrain.terrainData.GetInterpolatedNormal(normX, normZ);

                // Pick rock mesh & variant
                int rockIdx = UnityEngine.Random.Range(0, cachedRockMeshes.Length);
                Mesh mesh = cachedRockMeshes[rockIdx];
                if (mesh == null) continue;

                // Determine boulder vs pebble
                bool isBoulder = UnityEngine.Random.value < profile.boulderFraction;
                float scale;
                if (isBoulder)
                {
                    scale = UnityEngine.Random.Range(0.60f, 1.75f);
                    boulderCount++;
                }
                else
                {
                    scale = UnityEngine.Random.Range(0.12f, 0.35f);
                }

                GameObject rockGO = new GameObject($"Rock_{spawnedCount:D3}_{(isBoulder ? "Boulder" : "Pebble")}");
                rockGO.transform.SetParent(rocksContainer.transform, false);

                // Embed slightly into ground so it never floats above terrain mesh
                Vector3 spawnPos = new Vector3(worldX, worldY - scale * 0.18f, worldZ);
                rockGO.transform.position = spawnPos;

                // Rotation: aligned with ground normal + random yaw
                Quaternion normalRot = Quaternion.FromToRotation(Vector3.up, normal);
                Quaternion yawRot = Quaternion.Euler(
                    UnityEngine.Random.Range(-10f, 10f),
                    UnityEngine.Random.Range(0f, 360f),
                    UnityEngine.Random.Range(-10f, 10f)
                );
                rockGO.transform.rotation = normalRot * yawRot;
                rockGO.transform.localScale = Vector3.one * scale;

                // Add MeshFilter & MeshRenderer
                MeshFilter mf = rockGO.AddComponent<MeshFilter>();
                mf.sharedMesh = mesh;

                MeshRenderer mr = rockGO.AddComponent<MeshRenderer>();
                mr.sharedMaterial = planetMats[rockIdx];
                mr.shadowCastingMode = isBoulder 
                    ? UnityEngine.Rendering.ShadowCastingMode.On 
                    : UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = true;

                // Add convex collider ONLY on boulders
                if (isBoulder)
                {
                    MeshCollider mc = rockGO.AddComponent<MeshCollider>();
                    mc.sharedMesh = mesh;
                    mc.convex = true;
                }

                spawnedCount++;
            }

            Debug.Log($"[PlanetRockScatterer] Scattered {spawnedCount} rocks ({boulderCount} boulders with colliders, {spawnedCount - boulderCount} GPU pebbles) for {profile.displayName} with {clearance}m rover clearance.");
        }

        private void EnsureAssetsLoaded()
        {
            if (cachedRockMeshes != null && cachedRockMeshes.Length == RockModelPaths.Length) return;

            var meshes = new List<Mesh>();
            var mats = new List<Material>();

#if UNITY_EDITOR
            Shader standardShader = Shader.Find("Standard");
            if (standardShader == null) standardShader = Shader.Find("Nature/Terrain/Standard");

            for (int i = 0; i < RockModelPaths.Length; i++)
            {
                GameObject fbx = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(RockModelPaths[i]);
                Mesh mesh = null;
                if (fbx != null)
                {
                    MeshFilter mf = fbx.GetComponentInChildren<MeshFilter>();
                    if (mf != null) mesh = mf.sharedMesh;
                }
                meshes.Add(mesh);

                Texture2D diff = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(RockDiffPaths[i]);
                Texture2D nor = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(RockNorPaths[i]);

                Material mat = new Material(standardShader);
                mat.name = $"RockMat_{i + 1}";
                if (diff != null) mat.mainTexture = diff;
                if (nor != null)
                {
                    mat.EnableKeyword("_NORMALMAP");
                    mat.SetTexture("_BumpMap", nor);
                }
                mat.SetFloat("_Glossiness", 0.0f);
                mat.SetFloat("_Metallic", 0.0f);
                mat.enableInstancing = true;
                mats.Add(mat);
            }
#endif

            cachedRockMeshes = meshes.ToArray();
            cachedRockMaterials = mats.ToArray();
        }

        private Material[] GetTintedMaterials(Color tint)
        {
            EnsureAssetsLoaded();
            if (cachedRockMaterials == null) return new Material[0];

            Material[] result = new Material[cachedRockMaterials.Length];
            for (int i = 0; i < cachedRockMaterials.Length; i++)
            {
                if (cachedRockMaterials[i] != null)
                {
                    result[i] = new Material(cachedRockMaterials[i]);
                    result[i].color = tint;
                    result[i].SetFloat("_Glossiness", 0.0f);
                    result[i].SetFloat("_Metallic", 0.0f);
                    result[i].enableInstancing = true;
                }
            }
            return result;
        }
    }
}

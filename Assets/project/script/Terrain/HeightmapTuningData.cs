using System;
using UnityEngine;

namespace ProjectName.Terrain
{
    public enum HeightRemapCurve
    {
        Linear,
        Exponential,
        RidgePeak,
        BasinInversion
    }

    public enum HeightmapPreset
    {
        GaleCrater,
        OlympusMons,
        ShackletonCrater,
        VallesMarineris,
        PolarIce,
        ProceduralFractal,
        CustomImage
    }

    public enum PlanetaryMaterialType
    {
        MartianDust,
        LunarRegolith,
        VolcanicBasalt,
        PolarIce,
        RedCanyon,
        TopographicWireframe,
        NormalInspector
    }

    public enum BrushMode
    {
        Raise,
        Lower,
        Smooth,
        Flatten
    }

    [Serializable]
    public class TerrainTuningConfig
    {
        [Header("Elevation Amplitude & Offset")]
        [Tooltip("Max vertical elevation scale in meters (5m to 500m)")]
        [Range(5f, 500f)]
        public float maxHeight = 120f;

        [Tooltip("Elevation floor baseline shift in meters (-50m to 50m)")]
        [Range(-50f, 50f)]
        public float baseOffset = 0f;

        [Header("Grid Resolution")]
        [Tooltip("Unity terrain heightmap resolution (e.g. 33, 65, 129, 257, 513, 1025)")]
        public int resolution = 513;

        [Header("Filter & Remapping")]
        [Tooltip("Gaussian blur kernel strength to prevent staircasing (0.0 sharp to 5.0 smooth)")]
        [Range(0f, 5f)]
        public float smoothingFactor = 1.0f;

        [Tooltip("Height remapping curve profile")]
        public HeightRemapCurve heightCurve = HeightRemapCurve.Linear;

        [Header("Planetary Surface Material")]
        public PlanetaryMaterialType materialType = PlanetaryMaterialType.MartianDust;

        [Header("Terrain Bounds (X/Z Size in meters)")]
        public float terrainWidth = 1000f;
        public float terrainLength = 1000f;

        public TerrainTuningConfig Clone()
        {
            return (TerrainTuningConfig)MemberwiseClone();
        }
    }
}

using System;
using UnityEngine;

namespace ProjectName.Planetary
{
    /// <summary>
    /// Backward-compatible profile class extending the modern PlanetProfile.
    /// Preserves existing serialized assets and script references across the project.
    /// </summary>
    [CreateAssetMenu(fileName = "NewPlanetaryProfile", menuName = "Planetary Sim/Legacy Planetary Profile", order = 100)]
    public class PlanetaryProfile : PlanetProfile
    {
    }
}

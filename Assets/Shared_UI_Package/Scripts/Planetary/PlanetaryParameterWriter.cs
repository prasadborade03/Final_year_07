using System;
using UnityEngine;

namespace ProjectName.Planetary
{
    /// <summary>
    /// Backward-compatible adapter for the modern PlanetEnvironmentController.
    /// Ensures all existing callers compile and function seamlessly while delegating
    /// to the single-writer environment pipeline.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlanetaryParameterWriter : PlanetEnvironmentController
    {
        public new static PlanetaryParameterWriter Instance { get; private set; }

        protected override void Awake()
        {
            if (Instance == null) Instance = this;
            base.Awake();
        }

        protected override void OnEnable()
        {
            if (Instance == null) Instance = this;
            base.OnEnable();
        }
    }
}

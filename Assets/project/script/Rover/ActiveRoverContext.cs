using System;
using UnityEngine;

namespace ProjectName.Rover
{
    /// <summary>
    /// Lightweight handle representing the currently active, deployed rover.
    /// Non-negotiable rule R2: One source of truth for the active rover.
    /// </summary>
    public class RoverHandle
    {
        public GameObject rootGameObject;
        public ArticulationBody rootBody;
        public RoverProfile profile;
        public ArticulationBody[] wheelBodies;
        public WheelDefinition[] wheelDefs;
        public RoverTelemetry telemetry;
        public bool isFrozen;

        public GameObject gameObject => rootGameObject;
        public string roverId => profile != null ? profile.roverId : (rootGameObject != null ? rootGameObject.name : "");

        public bool IsValid => rootGameObject != null && rootBody != null;
    }

    /// <summary>
    /// Global context holding the currently active rover instance.
    /// HUD, camera, telemetry, and drive bars subscribe to this context.
    /// No consumer may call FindObjectOfType, FindWithTag, or pick the first ArticulationBody.
    /// </summary>
    public static class ActiveRoverContext
    {
        private static RoverHandle current;

        public static RoverHandle Current => current;
        public static bool HasActiveRover => current != null && current.IsValid;

        public static event Action<RoverHandle> OnRoverActivated;
        public static event Action OnRoverDestroyed;

        /// <summary>
        /// Registers a newly deployed rover into the studio context.
        /// Importers call this after their post-spawn physics wait.
        /// </summary>
        public static void Register(RoverHandle handle)
        {
            if (handle == null || !handle.IsValid)
            {
                Debug.LogWarning("[ActiveRoverContext] Attempted to register null or invalid RoverHandle.");
                return;
            }

            current = handle;
            Debug.Log($"[ActiveRoverContext] Rover registered: <color=#00E5FF><b>{handle.profile?.displayName ?? handle.rootGameObject.name}</b></color> (Root: {handle.rootBody.name})");
            OnRoverActivated?.Invoke(handle);
        }

        /// <summary>
        /// Unregisters and cleans up the active rover.
        /// </summary>
        public static void Unregister()
        {
            if (current != null)
            {
                Debug.Log($"[ActiveRoverContext] Rover unregistered: {current.profile?.displayName ?? "Unknown"}");
                current = null;
                OnRoverDestroyed?.Invoke();
            }
        }

        /// <summary>
        /// Updates the frozen / parked state of the rover.
        /// Master Brief Rule 4: Frozen state is tracked here so environment gravity updates skip it.
        /// </summary>
        public static void SetFrozen(bool frozen)
        {
            if (current != null && current.rootBody != null)
            {
                current.isFrozen = frozen;
                current.rootBody.immovable = frozen;
                current.rootBody.useGravity = !frozen;
                if (frozen)
                {
                    current.rootBody.linearVelocity = Vector3.zero;
                    current.rootBody.angularVelocity = Vector3.zero;
                }
            }
        }
    }
}

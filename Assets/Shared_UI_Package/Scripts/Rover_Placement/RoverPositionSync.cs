using UnityEngine;

namespace ProjectName.Rover
{
    /// <summary>
    /// Ensures seamless drag-and-drop and positioning of rover in both Edit Mode and Play Mode.
    /// Synchronizes parent GameObject transform with root ArticulationBody (base_link).
    /// Provides one-click snapping to terrain surface and terrain centering.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class RoverPositionSync : MonoBehaviour
    {
        [Header("Placement Configuration")]
        [Tooltip("Height offset above ground surface to rest wheels comfortably")]
        public float surfaceOffset = 0.25f;

        [Header("Desired Placement Coordinates")]
        [Tooltip("Desired (X, Z) coordinates on the active planetary terrain")]
        public Vector2 desiredCoordinates = new Vector2(500f, 500f);
        [Range(0f, 360f)]
        [Tooltip("Desired heading yaw rotation in degrees")]
        public float desiredHeadingYaw = 0f;

        [Header("Debug")]
        public bool showPlacementGizmo = true;

        [Header("Runtime Hotkeys")]
        public bool enableRuntimeHotkeys = true;
        private bool isRelocateModeActive = false;

        private ArticulationBody rootBody;
        private Vector3 lastEditPos;
        private Quaternion lastEditRot;
        private bool isTeleporting = false;

        private void Awake()
        {
            FindRootBody();
        }

        private void OnEnable()
        {
            FindRootBody();
            lastEditPos = transform.position;
            lastEditRot = transform.rotation;
        }

        public void FindRootBody()
        {
            if (rootBody != null && rootBody.isRoot) return;

            var bodies = GetComponentsInChildren<ArticulationBody>(true);
            foreach (var b in bodies)
            {
                if (b.isRoot)
                {
                    rootBody = b;
                    break;
                }
            }
        }

        private void Update()
        {
            FindRootBody();

            if (!Application.isPlaying)
            {
                // In Edit Mode: If user drags or rotates the parent GameObject in Scene View,
                // automatically teleport the ArticulationBody root so physics tree moves with it!
                if (!isTeleporting && rootBody != null && (transform.position != lastEditPos || transform.rotation != lastEditRot))
                {
                    rootBody.transform.position = transform.position;
                    rootBody.transform.rotation = transform.rotation;
                    rootBody.TeleportRoot(transform.position, transform.rotation);
                    lastEditPos = transform.position;
                    lastEditRot = transform.rotation;
                }
            }
            else
            {
                // In Play Mode: Physics engine (PhysX) drives the ArticulationBody root directly in world space.
                // DO NOT set transform.position = rootBody.transform.position here: if rootBody is a child of
                // this transform, modifying the parent transform displaces the child every frame, causing
                // an exponential runaway loop to Infinity/NaN and triggering 'Invalid worldAABB' physics crashes!
                HandlePlayModeInput();
            }
        }

        private void HandlePlayModeInput()
        {
            if (!enableRuntimeHotkeys) return;

            // [R] toggles interactive relocation mode
            if (Input.GetKeyDown(KeyCode.R))
            {
                isRelocateModeActive = !isRelocateModeActive;
                Debug.Log($"[RoverPositionSync] Relocate mode: {(isRelocateModeActive ? "ENABLED (Click on terrain to place, ESC to exit)" : "DISABLED")}");
            }

            if (Input.GetKeyDown(KeyCode.Escape) && isRelocateModeActive)
            {
                isRelocateModeActive = false;
                Debug.Log("[RoverPositionSync] Relocate mode canceled.");
            }

            // [C] centers rover on terrain
            if (Input.GetKeyDown(KeyCode.C) && (isRelocateModeActive || Input.GetKey(KeyCode.LeftShift)))
            {
                CenterOnTerrain();
            }

            // Shift + Left Click OR Relocate Mode Click
            bool isShiftClick = (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) && Input.GetMouseButtonDown(0);
            bool isRelocateClick = isRelocateModeActive && Input.GetMouseButtonDown(0);

            if (isShiftClick || isRelocateClick)
            {
                Camera cam = Camera.main;
                if (cam == null) cam = FindAnyObjectByType<Camera>();
                if (cam != null)
                {
                    Ray ray = cam.ScreenPointToRay(Input.mousePosition);
                    if (Physics.Raycast(ray, out RaycastHit hit, 5000f))
                    {
                        var terrain = hit.collider.GetComponent<UnityEngine.Terrain>() ?? hit.collider.GetComponentInParent<UnityEngine.Terrain>();
                        if (terrain != null || hit.collider is TerrainCollider || hit.collider.CompareTag("Terrain"))
                        {
                            Vector3 targetPos = hit.point + hit.normal * surfaceOffset;
                            Vector3 projectedForward = Vector3.ProjectOnPlane(transform.forward, hit.normal).normalized;
                            if (projectedForward.sqrMagnitude < 0.001f) projectedForward = Vector3.ProjectOnPlane(Vector3.forward, hit.normal).normalized;
                            Quaternion targetRot = Quaternion.LookRotation(projectedForward, hit.normal);

                            TeleportTo(targetPos, targetRot);
                            desiredCoordinates = new Vector2(targetPos.x, targetPos.z);
                            Debug.Log($"[RoverPositionSync] Shift+Click placed rover at {targetPos} on terrain normal {hit.normal}");

                            if (isRelocateClick && !isShiftClick)
                            {
                                isRelocateModeActive = false;
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Places rover at specific (X, Z) coordinates on the active terrain surface.
        /// </summary>
        public void PlaceAtCoordinates(float x, float z, float yawDegrees = 0f)
        {
            var terrain = UnityEngine.Terrain.activeTerrain;
            Vector3 targetPos = new Vector3(x, 0f, z);
            if (terrain != null)
            {
                float groundY = terrain.SampleHeight(targetPos) + terrain.transform.position.y;
                targetPos.y = groundY + surfaceOffset;
            }
            Quaternion rot = Quaternion.Euler(0f, yawDegrees, 0f);
            TeleportTo(targetPos, rot);
            desiredCoordinates = new Vector2(x, z);
            desiredHeadingYaw = yawDegrees;
            Debug.Log($"[RoverPositionSync] Rover placed at desired coordinates ({x:F1}, {z:F1}) -> World: {targetPos}");
        }

        [ContextMenu("Place At Desired Coordinates")]
        public void PlaceAtDesiredCoordinates()
        {
            PlaceAtCoordinates(desiredCoordinates.x, desiredCoordinates.y, desiredHeadingYaw);
        }

        /// <summary>
        /// Finds the highest elevation point on the terrain and places the rover there.
        /// </summary>
        [ContextMenu("Place At Highest Peak")]
        public void PlaceAtHighestPeak()
        {
            var terrain = UnityEngine.Terrain.activeTerrain;
            if (terrain == null || terrain.terrainData == null) return;

            Vector3 tPos = terrain.transform.position;
            Vector3 tSize = terrain.terrainData.size;
            int samples = 32;
            Vector3 peakPos = tPos;
            float maxH = -99999f;

            for (int ix = 1; ix < samples; ix++)
            {
                for (int iz = 1; iz < samples; iz++)
                {
                    Vector3 testPos = new Vector3(tPos.x + (tSize.x * ix / samples), 0f, tPos.z + (tSize.z * iz / samples));
                    float h = terrain.SampleHeight(testPos);
                    if (h > maxH)
                    {
                        maxH = h;
                        peakPos = testPos;
                    }
                }
            }

            peakPos.y = maxH + tPos.y + surfaceOffset;
            TeleportTo(peakPos, transform.rotation);
            desiredCoordinates = new Vector2(peakPos.x, peakPos.z);
            Debug.Log($"[RoverPositionSync] Placed rover on highest peak at {peakPos} (Elevation: {maxH:F1} m)");
        }

        /// <summary>
        /// Finds the lowest crater/basin on the terrain and places the rover there.
        /// </summary>
        [ContextMenu("Place At Lowest Crater")]
        public void PlaceAtLowestCrater()
        {
            var terrain = UnityEngine.Terrain.activeTerrain;
            if (terrain == null || terrain.terrainData == null) return;

            Vector3 tPos = terrain.transform.position;
            Vector3 tSize = terrain.terrainData.size;
            int samples = 32;
            Vector3 basinPos = tPos;
            float minH = 99999f;

            for (int ix = 2; ix < samples - 1; ix++)
            {
                for (int iz = 2; iz < samples - 1; iz++)
                {
                    Vector3 testPos = new Vector3(tPos.x + (tSize.x * ix / samples), 0f, tPos.z + (tSize.z * iz / samples));
                    float h = terrain.SampleHeight(testPos);
                    if (h < minH)
                    {
                        minH = h;
                        basinPos = testPos;
                    }
                }
            }

            basinPos.y = minH + tPos.y + surfaceOffset;
            TeleportTo(basinPos, transform.rotation);
            desiredCoordinates = new Vector2(basinPos.x, basinPos.z);
            Debug.Log($"[RoverPositionSync] Placed rover in lowest crater at {basinPos} (Elevation: {minH:F1} m)");
        }

        /// <summary>
        /// Snaps the rover cleanly to the terrain surface directly beneath its current (X, Z) coordinates.
        /// </summary>
        [ContextMenu("Snap to Terrain Surface")]
        public void SnapToTerrainSurface()
        {
            var terrain = UnityEngine.Terrain.activeTerrain;
            if (terrain == null) return;

            Vector3 currentPos = transform.position;
            float surfaceY = terrain.SampleHeight(currentPos) + terrain.transform.position.y;
            Vector3 targetPos = new Vector3(currentPos.x, surfaceY + surfaceOffset, currentPos.z);

            TeleportTo(targetPos, transform.rotation);
            Debug.Log($"[RoverPositionSync] Snapped '{gameObject.name}' to terrain surface at {targetPos}");
        }

        /// <summary>
        /// Places the rover directly in the center of the active terrain surface.
        /// </summary>
        [ContextMenu("Center on Terrain")]
        public void CenterOnTerrain()
        {
            var terrain = UnityEngine.Terrain.activeTerrain;
            if (terrain == null) return;

            Vector3 tPos = terrain.transform.position;
            Vector3 tSize = terrain.terrainData.size;
            Vector3 centerPos = new Vector3(tPos.x + tSize.x * 0.5f, 0f, tPos.z + tSize.z * 0.5f);
            float surfaceY = terrain.SampleHeight(centerPos) + tPos.y;
            centerPos.y = surfaceY + surfaceOffset;

            TeleportTo(centerPos, Quaternion.identity);
            desiredCoordinates = new Vector2(centerPos.x, centerPos.z);
            Debug.Log($"[RoverPositionSync] Centered '{gameObject.name}' on terrain at {centerPos}");
        }

        public static bool IsValidVector(Vector3 v)
        {
            return !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z) &&
                   !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);
        }

        /// <summary>
        /// Cleanly teleports both the GameObject and all ArticulationBodies without physics explosions.
        /// </summary>
        public void TeleportTo(Vector3 worldPos, Quaternion rotation)
        {
            if (!IsValidVector(worldPos))
            {
                Debug.LogWarning($"[RoverPositionSync] TeleportTo aborted: Invalid position vector {worldPos}");
                return;
            }

            isTeleporting = true;
            FindRootBody();

            if (rootBody != null)
            {
                rootBody.immovable = true;
                rootBody.linearVelocity = Vector3.zero;
                rootBody.angularVelocity = Vector3.zero;

                var allBodies = GetComponentsInChildren<ArticulationBody>(true);
                foreach (var b in allBodies)
                {
                    b.linearVelocity = Vector3.zero;
                    b.angularVelocity = Vector3.zero;
                }

                transform.position = worldPos;
                transform.rotation = rotation;
                lastEditPos = worldPos;
                lastEditRot = rotation;

                rootBody.transform.position = worldPos;
                rootBody.transform.rotation = rotation;
                rootBody.TeleportRoot(worldPos, rotation);

                Physics.SyncTransforms();

                rootBody.immovable = false;
                rootBody.useGravity = true;
            }
            else
            {
                transform.position = worldPos;
                transform.rotation = rotation;
                lastEditPos = worldPos;
                lastEditRot = rotation;
                Physics.SyncTransforms();
            }

            isTeleporting = false;
        }

        private void OnDrawGizmosSelected()
        {
            if (!showPlacementGizmo) return;
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, 0.8f);

            var terrain = UnityEngine.Terrain.activeTerrain;
            if (terrain != null)
            {
                float groundY = terrain.SampleHeight(transform.position) + terrain.transform.position.y;
                Vector3 groundPoint = new Vector3(transform.position.x, groundY, transform.position.z);
                Gizmos.color = Color.green;
                Gizmos.DrawLine(transform.position, groundPoint);
                Gizmos.DrawWireCube(groundPoint, new Vector3(1.2f, 0.05f, 1.2f));
            }
        }
    }
}

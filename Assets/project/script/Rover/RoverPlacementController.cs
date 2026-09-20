using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace ProjectName.Rover
{
    /// <summary>
    /// Production Rover Placement Controller for Desktop Non-VR.
    /// 
    /// Key Responsibilities:
    /// 1. Raycast cursor against terrain collider to determine placement point.
    /// 2. Procedural Ghost Marker (Ring LineRenderer + Forward Heading Arrow + 3D Slope Readout).
    /// 3. Color diagnostics: Green if slope <= profile.maxSafeSlopeDeg, Red otherwise.
    /// 4. Yaw rotation controls: Q/E keys and mouse scroll wheel.
    /// 5. UI Toolkit picking guard: Converts screen coordinates to panel space with Y-flip to prevent clicks on UI.
    /// 6. Proven Spawn Recipe:
    ///    - Destroy any previous rover in scene.
    ///    - Instantiate at targetPoint + up * profile.spawnClearance with upright yaw rotation only.
    ///    - Freeze ArticulationBodies (gravity off, velocities zero, immovable on root).
    ///    - Wait 2 fixed steps for articulation hierarchy to build.
    ///    - Unfreeze (gravity on, immovable false) and register into ActiveRoverContext.
    /// 7. Minimap placement and Quick Spawn (terrain centre) fallback.
    /// 8. 'R' key respawns active rover at the last placement pose.
    /// </summary>
    [DisallowMultipleComponent]
    public class RoverPlacementController : MonoBehaviour
    {
        private static RoverPlacementController _instance;
        public static RoverPlacementController Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = UnityEngine.Object.FindAnyObjectByType<RoverPlacementController>();
                }
                return _instance;
            }
            private set { _instance = value; }
        }

        public enum State
        {
            Idle,
            PlacementActive,
            Spawning
        }

        [Header("State")]
        public State currentState = State.Idle;
        public string pendingRoverId = "";
        public RoverProfile pendingProfile;

        [Header("Ghost Marker Configuration")]
        public float defaultRingRadius = 1.2f;
        public float rotationSpeed = 90f; // degrees per second
        public float scrollRotationStep = 15f; // degrees per scroll tick

        [Header("Last Placement Memory")]
        public bool hasLastPlacementPose = false;
        public Vector3 lastPlacementPoint;
        public float lastPlacementYaw;
        public string lastPlacementRoverId = "";

        // Runtime Ghost Marker GameObjects
        private GameObject ghostRoot;
        private LineRenderer ringRenderer;
        private LineRenderer arrowRenderer;
        private TextMesh slopeTextMesh;
        private MeshRenderer discRenderer;

        // Current placement parameters
        private float currentGhostYaw = 0f;
        private Vector3 currentGhostPosition = Vector3.zero;
        private Vector3 currentHitNormal = Vector3.up;
        private float currentSlopeAngle = 0f;
        private bool isCursorOverTerrain = false;

        // Cached references
        private SimulationFlowController flowController;
        private ProjectName.Terrain.UploadHeightmapUIToolkit uiToolkit;

        public bool IsPlacementActive => currentState == State.PlacementActive;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else if (Instance != this)
            {
                Destroy(this);
                return;
            }

            BuildGhostMarker();
        }

        private void Start()
        {
            flowController = FindAnyObjectByType<SimulationFlowController>();
            uiToolkit = FindAnyObjectByType<ProjectName.Terrain.UploadHeightmapUIToolkit>();
        }

        private void Update()
        {
            // Respawn active rover at last placement pose when 'R' is pressed
            if (currentState == State.Idle && ActiveRoverContext.HasActiveRover && hasLastPlacementPose)
            {
                if (!IsTextInputFocused() && (Input.GetKeyDown(KeyCode.R) || (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)))
                {
                    RespawnAtLastPlacementPose();
                    return;
                }
            }

            if (currentState != State.PlacementActive)
            {
                if (ghostRoot != null && ghostRoot.activeSelf)
                {
                    ghostRoot.SetActive(false);
                }
                return;
            }

            HandlePlacementInput();
            UpdateGhostMarkerPose();
        }

        // =========================================================================
        // 1. PLACEMENT MODE LIFECYCLE
        // =========================================================================

        public void StartPlacement(string roverId)
        {
            var terrain = UnityEngine.Terrain.activeTerrain ?? FindAnyObjectByType<UnityEngine.Terrain>();
            if (terrain == null)
            {
                Debug.LogWarning("[RoverPlacement] Cannot deploy rover: No active planetary terrain generated yet!");
                if (uiToolkit != null)
                {
                    uiToolkit.ShowNotification("Generate or load terrain before deploying rovers.");
                }
                return;
            }

            pendingRoverId = roverId.ToLowerInvariant();
            string profilePath = pendingRoverId == "husky" ? "RoverProfiles/HuskyProfile" :
                                 pendingRoverId == "m20" ? "RoverProfiles/M20Profile" :
                                 "RoverProfiles/M2020Profile";

            pendingProfile = Resources.Load<RoverProfile>(profilePath);
            if (pendingProfile == null)
            {
                Debug.LogError($"[RoverPlacement] Failed to load RoverProfile at '{profilePath}'!");
                return;
            }

            currentState = State.PlacementActive;
            currentGhostYaw = Camera.main != null ? Camera.main.transform.eulerAngles.y : 0f;

            // Configure ghost ring radius based on rover profile
            float r = defaultRingRadius;
            if (pendingRoverId == "m20") r = 1.4f;
            else if (pendingRoverId == "m2020") r = 2.0f;
            UpdateRingRadius(r);

            if (ghostRoot != null) ghostRoot.SetActive(false);

            // Collapse HUD into a slim banner
            if (uiToolkit != null)
            {
                uiToolkit.SetPlacementBannerActive(true, pendingProfile.displayName);
            }

            Debug.Log($"[RoverPlacement] Entered placement mode for {pendingProfile.displayName}. Move cursor over terrain to position.");
        }

        public void CancelPlacement()
        {
            if (currentState != State.PlacementActive) return;

            currentState = State.Idle;
            pendingRoverId = "";
            pendingProfile = null;

            if (ghostRoot != null) ghostRoot.SetActive(false);

            // Restore HUD to previous state
            if (uiToolkit != null)
            {
                uiToolkit.SetPlacementBannerActive(false, "");
            }

            Debug.Log("[RoverPlacement] Placement cancelled by user.");
        }

        public void QuickSpawnTerrainCentre()
        {
            var terrain = UnityEngine.Terrain.activeTerrain ?? FindAnyObjectByType<UnityEngine.Terrain>();
            if (terrain == null)
            {
                Debug.LogWarning("[RoverPlacement] Cannot quick spawn: No active terrain!");
                return;
            }

            string rId = !string.IsNullOrEmpty(pendingRoverId) ? pendingRoverId :
                         (ActiveRoverContext.HasActiveRover ? ActiveRoverContext.Current.roverId : "husky");

            Vector3 tPos = terrain.transform.position;
            Vector3 tSize = terrain.terrainData.size;
            Vector3 center = new Vector3(tPos.x + tSize.x * 0.5f, 0f, tPos.z + tSize.z * 0.5f);
            center.y = terrain.SampleHeight(center) + tPos.y;

            float yaw = Camera.main != null ? Camera.main.transform.eulerAngles.y : 0f;
            ConfirmPlacementAtPoint(center, yaw, rId);
        }

        public void ConfirmPlacementAtPoint(Vector3 worldPoint, float yaw = 0f, string roverIdOverride = null)
        {
            string idToSpawn = !string.IsNullOrEmpty(roverIdOverride) ? roverIdOverride : pendingRoverId;
            if (string.IsNullOrEmpty(idToSpawn)) idToSpawn = "husky";

            RoverProfile profile = pendingProfile;
            if (profile == null || profile.roverId != idToSpawn)
            {
                string profilePath = idToSpawn == "husky" ? "RoverProfiles/HuskyProfile" :
                                     idToSpawn == "m20" ? "RoverProfiles/M20Profile" :
                                     "RoverProfiles/M2020Profile";
                profile = Resources.Load<RoverProfile>(profilePath);
            }

            StartCoroutine(ExecuteSpawnRecipe(worldPoint, yaw, idToSpawn, profile));
        }

        // =========================================================================
        // 2. INPUT HANDLING & GHOST MARKER UPDATES
        // =========================================================================

        private void HandlePlacementInput()
        {
            // Cancel with RMB or Escape
            bool cancelRequested = Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape);
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) cancelRequested = true;
            if (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame) cancelRequested = true;
#endif
            if (cancelRequested)
            {
                CancelPlacement();
                return;
            }

            // Rotate yaw with Q / E keys
            float yawDelta = 0f;
            if (Input.GetKey(KeyCode.Q)) yawDelta -= rotationSpeed * Time.deltaTime;
            if (Input.GetKey(KeyCode.E)) yawDelta += rotationSpeed * Time.deltaTime;

#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null)
            {
                if (Keyboard.current.qKey.isPressed) yawDelta -= rotationSpeed * Time.deltaTime;
                if (Keyboard.current.eKey.isPressed) yawDelta += rotationSpeed * Time.deltaTime;
            }
#endif

            // Rotate yaw with mouse scroll wheel
            float scroll = Input.mouseScrollDelta.y;
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null) scroll = Mouse.current.scroll.ReadValue().y * 0.01f;
#endif
            if (Mathf.Abs(scroll) > 0.01f)
            {
                yawDelta += Mathf.Sign(scroll) * scrollRotationStep;
            }

            currentGhostYaw = (currentGhostYaw + yawDelta) % 360f;
            if (currentGhostYaw < 0f) currentGhostYaw += 360f;

            // Confirm placement on Left Mouse Button
            bool clickRequested = Input.GetMouseButtonDown(0);
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) clickRequested = true;
#endif

            if (clickRequested && isCursorOverTerrain)
            {
                // CRITICAL RULE: Verify click is not over UI Toolkit elements
                Vector2 mousePos = Input.mousePosition;
#if ENABLE_INPUT_SYSTEM
                if (Mouse.current != null) mousePos = Mouse.current.position.ReadValue();
#endif
                if (IsPointerOverUI(mousePos))
                {
                    Debug.Log("[RoverPlacement] Click ignored — pointer is over interactive UI Toolkit element.");
                    return;
                }

                // Execute placement!
                ConfirmPlacementAtPoint(currentGhostPosition, currentGhostYaw);
            }
        }

        private void UpdateGhostMarkerPose()
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                isCursorOverTerrain = false;
                if (ghostRoot != null) ghostRoot.SetActive(false);
                return;
            }

            Vector2 mousePos = Input.mousePosition;
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null) mousePos = Mouse.current.position.ReadValue();
#endif

            Ray ray = cam.ScreenPointToRay(mousePos);

            // Raycast terrain layer
            RaycastHit[] hits = Physics.RaycastAll(ray, 3000f);
            RaycastHit? bestHit = null;
            float closestDist = float.MaxValue;

            foreach (var h in hits)
            {
                bool isTerrain = h.collider is TerrainCollider || h.collider.CompareTag("Terrain") || h.collider.GetComponent<UnityEngine.Terrain>() != null;
                if (isTerrain && h.distance < closestDist)
                {
                    closestDist = h.distance;
                    bestHit = h;
                }
            }

            if (bestHit.HasValue)
            {
                isCursorOverTerrain = true;
                currentGhostPosition = bestHit.Value.point;
                currentHitNormal = bestHit.Value.normal;

                // Calculate surface slope angle relative to vertical up
                currentSlopeAngle = Vector3.Angle(currentHitNormal, Vector3.up);

                if (ghostRoot != null)
                {
                    if (!ghostRoot.activeSelf) ghostRoot.SetActive(true);

                    // Offset slightly above surface to prevent Z-fighting
                    ghostRoot.transform.position = currentGhostPosition + currentHitNormal * 0.03f;

                    // Align ghost ring to surface normal while preserving heading yaw
                    Quaternion surfaceRot = Quaternion.FromToRotation(Vector3.up, currentHitNormal);
                    Quaternion yawRot = Quaternion.Euler(0f, currentGhostYaw, 0f);
                    ghostRoot.transform.rotation = surfaceRot * yawRot;

                    // Evaluate slope safety against active rover profile
                    float maxSlope = pendingProfile != null ? pendingProfile.maxSafeSlopeDeg : 25f;
                    bool isSafe = currentSlopeAngle <= maxSlope;
                    Color markerColor = isSafe ? new Color(0.13f, 0.77f, 0.36f, 0.95f) : new Color(0.94f, 0.27f, 0.27f, 0.95f);

                    UpdateMarkerColor(markerColor);

                    // Update slope readout text
                    if (slopeTextMesh != null)
                    {
                        slopeTextMesh.text = $"{currentSlopeAngle:F1}° ({(isSafe ? "SAFE" : "STEEP")})";
                        slopeTextMesh.color = markerColor;

                        // Billboard text towards camera
                        slopeTextMesh.transform.rotation = Quaternion.LookRotation(slopeTextMesh.transform.position - cam.transform.position);
                    }
                }
            }
            else
            {
                // Cursor over sky or outside terrain bounds
                isCursorOverTerrain = false;
                if (ghostRoot != null && ghostRoot.activeSelf)
                {
                    ghostRoot.SetActive(false);
                }
            }
        }

        // =========================================================================
        // 3. PROVEN SPAWN RECIPE COROUTINE
        // =========================================================================

        private IEnumerator ExecuteSpawnRecipe(Vector3 targetPoint, float yaw, string roverId, RoverProfile profile)
        {
            currentState = State.Spawning;
            if (ghostRoot != null) ghostRoot.SetActive(false);

            Debug.Log($"[RoverPlacement] Executing spawn recipe for '{roverId}' at {targetPoint} (yaw: {yaw:F1}°)...");

            // Step 1: Clean up previous rovers
            DestroyPreviousRovers();

            // Step 2: Spawn via Importer
            GameObject roverObj = null;
            if (flowController == null) flowController = FindAnyObjectByType<SimulationFlowController>();

            if (roverId == "husky")
            {
                var imp = FindAnyObjectByType<RoverImporter_husky>();
                if (imp != null)
                {
                    imp.enabled = true;
                    roverObj = imp.SpawnRover();
                }
            }
            else if (roverId == "m20")
            {
                var imp = FindAnyObjectByType<RoverImporter_m20>();
                if (imp != null)
                {
                    imp.enabled = true;
                    roverObj = imp.SpawnRover();
                }
            }
            else if (roverId == "m2020")
            {
                var imp = FindAnyObjectByType<RoverImporter_m2020>();
                if (imp != null)
                {
                    imp.enabled = true;
                    roverObj = imp.SpawnRover();
                }
            }

            if (roverObj == null)
            {
                Debug.LogError($"[RoverPlacement] Importer failed to spawn rover '{roverId}'!");
                currentState = State.Idle;
                if (uiToolkit != null) uiToolkit.SetPlacementBannerActive(false, "");
                yield break;
            }

            // Step 3: Freeze physics while articulation tree builds
            float clearance = profile != null ? profile.spawnClearance : 0.35f;
            Vector3 spawnPos = targetPoint + Vector3.up * clearance;
            Quaternion spawnRot = Quaternion.Euler(0f, yaw, 0f);

            var allBodies = roverObj.GetComponentsInChildren<ArticulationBody>(true);
            ArticulationBody rootBody = allBodies.FirstOrDefault(b => b.isRoot);

            foreach (var body in allBodies)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.useGravity = false;
                if (body.isRoot)
                {
                    body.immovable = true;
                }
            }

            if (rootBody != null)
            {
                rootBody.TeleportRoot(spawnPos, spawnRot);
            }

            // Step 4: Wait 2 fixed physics steps for solver hierarchy to assemble
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            // Step 5: Unfreeze articulation bodies
            foreach (var body in allBodies)
            {
                if (body != null)
                {
                    body.useGravity = true;
                    if (body.isRoot)
                    {
                        body.immovable = false;
                    }
                }
            }

            // Step 6: Enable appropriate controller
            if (roverId == "husky")
            {
                var ctrl = FindAnyObjectByType<RoverController_husky>();
                if (ctrl != null) ctrl.enabled = true;
            }
            else if (roverId == "m20")
            {
                var ctrl = FindAnyObjectByType<RoverController_m20>();
                if (ctrl != null) ctrl.enabled = true;
            }
            else if (roverId == "m2020")
            {
                var ctrl = FindAnyObjectByType<RoverController_m2020>();
                if (ctrl != null) ctrl.enabled = true;
            }

            // Step 7: Save last placement pose
            lastPlacementPoint = targetPoint;
            lastPlacementYaw = yaw;
            lastPlacementRoverId = roverId;
            hasLastPlacementPose = true;

            // Step 8: Restore HUD & Exit Placement Mode
            currentState = State.Idle;
            if (uiToolkit != null)
            {
                uiToolkit.SetPlacementBannerActive(false, "");
            }

            Debug.Log($"[RoverPlacement] Rover '{roverId}' successfully spawned and placed at {targetPoint} (yaw: {yaw:F1}°).");
        }

        private void DestroyPreviousRovers()
        {
            ActiveRoverContext.Unregister();

            string[] roverNames = new[] { "GeneratedHusky", "GeneratedM20", "GeneratedPerseverance_Fixed" };
            foreach (var n in roverNames)
            {
                var go = GameObject.Find(n);
                if (go != null)
                {
                    Destroy(go);
                }
            }

            if (flowController != null)
            {
                flowController.DisableAllRovers();
            }
        }

        public void RespawnAtLastPlacementPose()
        {
            if (!hasLastPlacementPose)
            {
                Debug.LogWarning("[RoverPlacement] No previous placement pose recorded!");
                return;
            }

            string rId = !string.IsNullOrEmpty(lastPlacementRoverId) ? lastPlacementRoverId :
                         (ActiveRoverContext.HasActiveRover ? ActiveRoverContext.Current.roverId : "husky");

            var terrain = UnityEngine.Terrain.activeTerrain ?? FindAnyObjectByType<UnityEngine.Terrain>();
            if (terrain != null)
            {
                lastPlacementPoint.y = terrain.SampleHeight(lastPlacementPoint) + terrain.transform.position.y;
            }

            ConfirmPlacementAtPoint(lastPlacementPoint, lastPlacementYaw, rId);
        }

        public void TeleportActiveRover(Vector3 targetPoint)
        {
            if (!ActiveRoverContext.HasActiveRover) return;

            var handle = ActiveRoverContext.Current;
            var root = handle.rootBody;
            float clearance = handle.profile != null ? handle.profile.spawnClearance : 0.35f;
            Vector3 dest = targetPoint + Vector3.up * clearance;
            Quaternion rot = handle.rootGameObject.transform.rotation;

            if (root != null)
            {
                root.TeleportRoot(dest, rot);
                root.linearVelocity = Vector3.zero;
                root.angularVelocity = Vector3.zero;
            }
            else
            {
                handle.rootGameObject.transform.position = dest;
            }

            if (handle.telemetry != null)
            {
                handle.telemetry.odometerMeters = 0f;
            }

            Debug.Log($"[RoverPlacement] Relocated active rover '{handle.roverId}' to {dest}");
        }

        // =========================================================================
        // 4. UI TOOLKIT POINTER GUARD
        // =========================================================================

        public bool IsPointerOverUI(Vector2 screenPos)
        {
            if (uiToolkit == null) uiToolkit = FindAnyObjectByType<ProjectName.Terrain.UploadHeightmapUIToolkit>();
            if (uiToolkit != null)
            {
                return uiToolkit.IsPointerOverUI(screenPos);
            }
            return false;
        }

        private bool IsTextInputFocused()
        {
            if (uiToolkit == null) uiToolkit = FindAnyObjectByType<ProjectName.Terrain.UploadHeightmapUIToolkit>();
            return uiToolkit != null && uiToolkit.IsTextInputFocused();
        }

        // =========================================================================
        // 5. PROCEDURAL GHOST MARKER CONSTRUCTION
        // =========================================================================

        private void BuildGhostMarker()
        {
            if (ghostRoot != null) return;

            ghostRoot = new GameObject("RoverPlacementGhost");
            ghostRoot.transform.SetParent(transform, false);

            Shader unlitShader = Shader.Find("Sprites/Default");
            Material lineMat = new Material(unlitShader);
            lineMat.color = Color.green;

            // 1. Circle Ring
            GameObject ringObj = new GameObject("Ring");
            ringObj.transform.SetParent(ghostRoot.transform, false);
            ringRenderer = ringObj.AddComponent<LineRenderer>();
            ringRenderer.useWorldSpace = false;
            ringRenderer.loop = true;
            ringRenderer.positionCount = 48;
            ringRenderer.startWidth = 0.05f;
            ringRenderer.endWidth = 0.05f;
            ringRenderer.material = lineMat;
            ringRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ringRenderer.receiveShadows = false;

            // 2. Heading Forward Arrow
            GameObject arrowObj = new GameObject("HeadingArrow");
            arrowObj.transform.SetParent(ghostRoot.transform, false);
            arrowRenderer = arrowObj.AddComponent<LineRenderer>();
            arrowRenderer.useWorldSpace = false;
            arrowRenderer.positionCount = 5;
            arrowRenderer.startWidth = 0.06f;
            arrowRenderer.endWidth = 0.06f;
            arrowRenderer.material = lineMat;
            arrowRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            arrowRenderer.receiveShadows = false;

            // 3. Slope Text Readout
            GameObject textObj = new GameObject("SlopeText");
            textObj.transform.SetParent(ghostRoot.transform, false);
            textObj.transform.localPosition = new Vector3(0f, 0.45f, 0f);
            slopeTextMesh = textObj.AddComponent<TextMesh>();
            slopeTextMesh.text = "SLOPE: 0.0° (SAFE)";
            slopeTextMesh.characterSize = 0.12f;
            slopeTextMesh.fontSize = 36;
            slopeTextMesh.anchor = TextAnchor.MiddleCenter;
            slopeTextMesh.alignment = TextAlignment.Center;
            slopeTextMesh.color = Color.green;

            UpdateRingRadius(defaultRingRadius);
            ghostRoot.SetActive(false);
        }

        private void UpdateRingRadius(float radius)
        {
            if (ringRenderer == null || arrowRenderer == null) return;

            // Compute circular ring points
            int count = ringRenderer.positionCount;
            Vector3[] ringPts = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                float angle = i * (2f * Mathf.PI / count);
                ringPts[i] = new Vector3(Mathf.Sin(angle) * radius, 0f, Mathf.Cos(angle) * radius);
            }
            ringRenderer.SetPositions(ringPts);

            // Compute forward heading arrow points
            // Arrow stem from center to perimeter, then arrowhead left/right
            float arrowLen = radius * 1.25f;
            float headLen = radius * 0.35f;
            float headWidth = radius * 0.22f;

            Vector3[] arrowPts = new Vector3[5]
            {
                Vector3.zero,
                new Vector3(0f, 0f, arrowLen),
                new Vector3(-headWidth, 0f, arrowLen - headLen),
                new Vector3(0f, 0f, arrowLen),
                new Vector3(headWidth, 0f, arrowLen - headLen)
            };
            arrowRenderer.SetPositions(arrowPts);
        }

        private void UpdateMarkerColor(Color c)
        {
            if (ringRenderer != null && ringRenderer.material != null)
            {
                ringRenderer.material.color = c;
            }
            if (arrowRenderer != null && arrowRenderer.material != null)
            {
                arrowRenderer.material.color = c;
            }
        }
    }
}

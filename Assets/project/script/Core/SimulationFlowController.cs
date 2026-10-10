using System;
using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;
using ProjectName.Rover;
using ProjectName.Terrain;

namespace ProjectName.Core
{
    public enum SimulationState
    {
        RuntimeSelection,   // State 0: Main Menu
        EnvironmentSetup,   // State 1: Select Planet, Terrain Source, Preset/Upload/Painter, Material, Advanced Settings
        TerrainGenerating,  // Generating mesh, colliders, materials
        TerrainReady,       // State 2: 3D terrain visible, summary displayed, [Change Terrain] or [Continue to Rover]
        RoverSelection,     // State 3: Choose Husky / M20 / M2020, [+ Import URDF], [Click to Spawn] / [Centre Spawn]
        RoverSpawning,      // Holographic ghost marker placement mode
        SimulationReady,    // State 4: Confirmation screen showing Planet, Terrain, Rover, Gravity, [Start Mission]
        ActiveSimulation    // State 5: Active Mission (Driving HUD as primary, Full Studio as toggle)
    }

    /// <summary>
    /// Master Simulation Context holding current configuration and facts.
    /// Single source of truth for the active simulation lifecycle.
    /// </summary>
    public static class SimulationContext
    {
        public static string SelectedPlanet = "Mars";
        public static string SelectedTerrainPreset = "Gale Crater";
        public static string SelectedTerrainSource = "Preset"; // Preset, Upload, Painter
        public static string SelectedMaterial = "Martian Rust";
        public static string SelectedRoverId = "husky";
        public static string SelectedRoverDisplayName = "Clearpath Husky A200";
        public static float SelectedGravity = 3.72f;
        public static Vector3 TerrainSize = new Vector3(1000f, 120f, 1000f);
        public static int TerrainResolution = 513;
        public static SimulationState CurrentState = SimulationState.EnvironmentSetup;
    }
}

public class SimulationFlowController : MonoBehaviour
{
    public static SimulationFlowController Instance { get; private set; }

    [Header("Simulation State Machine")]
    [SerializeField] private ProjectName.Core.SimulationState currentSimState = ProjectName.Core.SimulationState.EnvironmentSetup;
    public ProjectName.Core.SimulationState CurrentSimulationState => currentSimState;
    public event Action<ProjectName.Core.SimulationState> OnSimulationStateChanged;

    [Header("Terrain References")]
    public ProjectName.UI.UploadHeightmapUI uploadUI;
    public TerrainGenerator terrainGenerator;
    public GameObject robotSelectPanel;

    [Header("Husky")]
    public RoverImporter_husky huskyImporter;
    public RoverController_husky huskyController;

    [Header("M20")]
    public RoverImporter_m20 m20Importer;
    public RoverController_m20 m20Controller;

    [Header("M2020 (NASA Perseverance)")]
    public RoverImporter_m2020 m2020Importer;
    public RoverController_m2020 m2020Controller;

    [Header("UI Guide Text (Legacy uGUI / TMP Fallback)")]
    [Tooltip("Drag a TextMeshProUGUI element here if using legacy Canvas.")]
    public TextMeshProUGUI guideText;

    [Header("Placement Settings")]
    [Tooltip("How high above the raycast hit point to place the robot, to avoid spawning it inside the terrain.")]
    public float placementYOffset = 0.3f;

    [Header("Debug")]
    [Tooltip("Turn on to see Debug.Log messages and a gizmo at the last raycast hit point.")]
    public bool showDebugLogs = true;

    // Events for UI Toolkit binding
    public Action<string> onGuideTextChanged;
    public Action<string> onStateChanged;

    // Backward-compatible enum
    public enum State { WaitingForTerrain, WaitingForRobotChoice, WaitingForClickToPlace, ActiveDriving }
    public State CurrentState => MapToLegacyState(currentSimState);
    public string SelectedRobot => selectedRobot;

    private string selectedRobot = "husky";
    private GameObject pendingRobot = null;
    private GameObject activeSpawnedRover = null;

    private Vector3 lastHitPoint;      // for debug gizmo
    private bool hasLastHit = false;

    [Header("VR Interaction")]
    public Transform rightControllerTransform;
    public Transform leftControllerTransform;
    private bool wasVRTriggerPressedLastFrame = false;

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

        if (terrainGenerator == null)
        {
            terrainGenerator = FindAnyObjectByType<TerrainGenerator>();
        }
    }

    private void Start()
    {
        if (rightControllerTransform == null)
        {
            var rightGO = GameObject.Find("Right Controller");
            if (rightGO != null) rightControllerTransform = rightGO.transform;
        }
        if (leftControllerTransform == null)
        {
            var leftGO = GameObject.Find("Left Controller");
            if (leftGO != null) leftControllerTransform = leftGO.transform;
        }

        // Section 24: Blank Initial Simulation Environment
        // Ensure no stale terrain or rover remains from any previous session
        CleanSimulationEnvironment();

        SetSimulationState(ProjectName.Core.SimulationState.EnvironmentSetup);
        SetGuideText("Prepare Simulation Environment: Select planet, terrain source, and click Generate Terrain.");
    }

    private void Update()
    {
        if (currentSimState == ProjectName.Core.SimulationState.RoverSpawning)
        {
            bool vrTriggerDown = CheckVRTriggerJustPressed();
            if (Input.GetMouseButtonDown(0) || vrTriggerDown)
            {
                TryPlaceRobot(vrTriggerDown);
            }
        }
    }

    // =========================================================================
    // STATE MACHINE TRANSITIONS
    // =========================================================================

    public void SetSimulationState(ProjectName.Core.SimulationState newState)
    {
        currentSimState = newState;
        ProjectName.Core.SimulationContext.CurrentState = newState;
        Log($"Simulation State changed -> {newState}");

        onStateChanged?.Invoke(newState.ToString());
        OnSimulationStateChanged?.Invoke(newState);
    }

    private State MapToLegacyState(ProjectName.Core.SimulationState s)
    {
        switch (s)
        {
            case ProjectName.Core.SimulationState.EnvironmentSetup:
            case ProjectName.Core.SimulationState.TerrainGenerating:
                return State.WaitingForTerrain;
            case ProjectName.Core.SimulationState.TerrainReady:
            case ProjectName.Core.SimulationState.RoverSelection:
                return State.WaitingForRobotChoice;
            case ProjectName.Core.SimulationState.RoverSpawning:
                return State.WaitingForClickToPlace;
            case ProjectName.Core.SimulationState.SimulationReady:
            case ProjectName.Core.SimulationState.ActiveSimulation:
            default:
                return State.ActiveDriving;
        }
    }

    /// <summary>
    /// Step 1: Return to or start Environment Setup.
    /// </summary>
    public void StartEnvironmentSetup()
    {
        SetSimulationState(ProjectName.Core.SimulationState.EnvironmentSetup);
        SetGuideText("Select planet, terrain source, material, and click Generate Terrain.");
    }

    /// <summary>
    /// Called when terrain generation starts.
    /// </summary>
    public void OnTerrainGenerationStarted()
    {
        SetSimulationState(ProjectName.Core.SimulationState.TerrainGenerating);
        SetGuideText("Generating 3D planetary terrain & configuring colliders...");
    }

    /// <summary>
    /// Step 2: Called when terrain has been successfully generated.
    /// </summary>
    public void OnTerrainReady()
    {
        SetSimulationState(ProjectName.Core.SimulationState.TerrainReady);
        SetGuideText("Terrain Ready! Review environment specifications and continue to rover deployment.");
        Log("Terrain ready -> State.TerrainReady");
    }

    /// <summary>
    /// Step 3: Transition from Terrain Ready to Rover Selection.
    /// </summary>
    public void ContinueToRoverSelection()
    {
        SetSimulationState(ProjectName.Core.SimulationState.RoverSelection);
        SetGuideText("Select a digital twin rover (Husky, M20, M2020) and choose a spawn method.");
        Log("Transitioned to State.RoverSelection");
    }

    /// <summary>
    /// Selects rover model and prepares placement.
    /// </summary>
    public void SelectRover(string roverId)
    {
        selectedRobot = roverId.ToLowerInvariant();
        ProjectName.Core.SimulationContext.SelectedRoverId = selectedRobot;
        if (selectedRobot == "husky") ProjectName.Core.SimulationContext.SelectedRoverDisplayName = "Clearpath Husky A200";
        else if (selectedRobot == "m20") ProjectName.Core.SimulationContext.SelectedRoverDisplayName = "Deep Robotics M20";
        else if (selectedRobot == "m2020") ProjectName.Core.SimulationContext.SelectedRoverDisplayName = "NASA Perseverance M2020";

        Log($"Rover selected: {selectedRobot}");
    }

    /// <summary>
    /// Starts interactive placement mode (Click-to-spawn).
    /// </summary>
    public void StartRoverPlacement(string roverId)
    {
        SelectRover(roverId);
        SetSimulationState(ProjectName.Core.SimulationState.RoverSpawning);
        SetGuideText($"Aim at terrain and click/trigger to place {ProjectName.Core.SimulationContext.SelectedRoverDisplayName}.");

        if (RoverPlacementController.Instance != null)
        {
            RoverPlacementController.Instance.StartPlacement(selectedRobot);
        }
        else
        {
            StartPlacementMode();
        }
    }

    /// <summary>
    /// Spawns rover directly at terrain center.
    /// </summary>
    public void SpawnRoverAtCenter(string roverId)
    {
        SelectRover(roverId);
        if (RoverPlacementController.Instance != null)
        {
            RoverPlacementController.Instance.QuickSpawnTerrainCentre();
        }
        else
        {
            StartPlacementMode();
        }
    }

    /// <summary>
    /// Step 4: Called once a rover has been placed/spawned.
    /// Directly transitions to ActiveSimulation for seamless driving without redundant modal.
    /// </summary>
    public void OnRoverSpawned()
    {
        // Direct transition from placement to active driving mission (No redundant modal)
        ActiveRoverContext.SetFrozen(false);
        EnableActiveRoverController();

        SetSimulationState(ProjectName.Core.SimulationState.ActiveSimulation);
        SetGuideText($"Active Mission! Drive: WASD / Thumbstick. Telemetry streaming live.");
        Log("Rover spawned -> Direct transition to State.ActiveSimulation (Driving)");

        if (ProjectName.Rover.RoverCameraRig.Instance != null)
        {
            ProjectName.Rover.RoverCameraRig.Instance.StartFollowActiveRover();
        }
    }

    /// <summary>
    /// Starts or resumes active driving mission.
    /// </summary>
    public void StartMission()
    {
        ActiveRoverContext.SetFrozen(false);
        EnableActiveRoverController();

        SetSimulationState(ProjectName.Core.SimulationState.ActiveSimulation);
        SetGuideText($"Active Mission! Drive: WASD / Thumbstick. Telemetry streaming live.");
        Log("Active Mission started -> State.ActiveSimulation");

        if (ProjectName.Rover.RoverCameraRig.Instance != null)
        {
            ProjectName.Rover.RoverCameraRig.Instance.StartFollowActiveRover();
        }
    }

    // =========================================================================
    // LIFECYCLE CLEANUP & ENVIRONMENT RESET
    // =========================================================================

    /// <summary>
    /// Destroys active rover and unregisters context.
    /// </summary>
    public void CleanActiveRover()
    {
        DisableAllRovers();
        ActiveRoverContext.DestroyActiveRover();
        activeSpawnedRover = null;
        pendingRobot = null;

        // Clear any leftover spawned rovers by name
        string[] roverNames = { "GeneratedHusky", "GeneratedM20", "GeneratedPerseverance_Fixed" };
        foreach (var name in roverNames)
        {
            var go = GameObject.Find(name);
            if (go != null)
            {
                Destroy(go);
            }
        }

        Log("Active rover cleaned and unregistered.");
    }

    /// <summary>
    /// Destroys generated terrain and clears references.
    /// </summary>
    public void CleanTerrain()
    {
        if (terrainGenerator == null) terrainGenerator = FindAnyObjectByType<TerrainGenerator>();
        if (terrainGenerator != null)
        {
            terrainGenerator.ClearExistingTerrain();
        }
        else
        {
            var allTerrains = FindObjectsByType<UnityEngine.Terrain>();
            foreach (var t in allTerrains)
            {
                if (t != null && t.gameObject != null) Destroy(t.gameObject);
            }
        }

        Log("Terrain cleaned and destroyed.");
    }

    /// <summary>
    /// Full reset: Destroys active rover, destroys terrain, and returns to Environment Setup.
    /// </summary>
    public void CleanSimulationEnvironment()
    {
        CleanActiveRover();
        CleanTerrain();
    }

    /// <summary>
    /// Clean simulation reset returning to Environment Setup.
    /// </summary>
    public void ResetSimulation()
    {
        Log("Resetting simulation...");
        CleanSimulationEnvironment();
        StartEnvironmentSetup();
    }

    // =========================================================================
    // ROVER SELECTION WRAPPERS (for UI Buttons)
    // =========================================================================

    public void OnSelectHusky()
    {
        StartRoverPlacement("husky");
    }

    public void OnSelectM20()
    {
        StartRoverPlacement("m20");
    }

    public void OnSelectM2020()
    {
        StartRoverPlacement("m2020");
    }

    private void StartPlacementMode()
    {
        if (robotSelectPanel != null)
            robotSelectPanel.SetActive(false);

        DisableAllRovers();
        pendingRobot = null;

        if (selectedRobot == "husky" && huskyImporter != null)
        {
            huskyImporter.enabled = true;
            pendingRobot = huskyImporter.SpawnRover();
        }
        else if (selectedRobot == "m20" && m20Importer != null)
        {
            m20Importer.enabled = true;
            pendingRobot = m20Importer.SpawnRover();
        }
        else if (selectedRobot == "m2020" && m2020Importer != null)
        {
            m2020Importer.enabled = true;
            pendingRobot = m2020Importer.SpawnRover();
        }

        if (pendingRobot == null)
        {
            LogWarning($"Spawn failed for '{selectedRobot}'. Check importer reference.");
            SetGuideText("Something went wrong spawning the robot. Check console.");
            return;
        }

        activeSpawnedRover = pendingRobot;

        var terrain = UnityEngine.Terrain.activeTerrain ?? FindAnyObjectByType<UnityEngine.Terrain>();
        if (terrain != null)
        {
            Vector3 tPos = terrain.transform.position;
            Vector3 tSize = terrain.terrainData.size;
            Vector3 spawnPos = new Vector3(tPos.x + tSize.x * 0.5f, 0f, tPos.z + tSize.z * 0.5f);
            spawnPos.y = terrain.SampleHeight(spawnPos) + tPos.y + 0.35f;

            TeleportRover(pendingRobot, spawnPos);
            OnRoverSpawned();
            pendingRobot = null;
        }
        else
        {
            SetRoverFrozen(pendingRobot, true);
            TeleportRover(pendingRobot, new Vector3(0f, 2f, 0f));
            SetSimulationState(ProjectName.Core.SimulationState.RoverSpawning);
            SetGuideText($"Click on the planetary surface to place the {selectedRobot.ToUpper()}.");
        }
    }

    private void TryPlaceRobot(bool fromVR = false)
    {
        if (pendingRobot == null) return;

        bool isShift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        if (!isShift && !fromVR && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
        {
            Log("Click ignored — pointer was over interactive UI element.");
            return;
        }

        Ray ray;
        if (fromVR)
        {
            if (rightControllerTransform != null)
                ray = new Ray(rightControllerTransform.position, rightControllerTransform.forward);
            else if (leftControllerTransform != null)
                ray = new Ray(leftControllerTransform.position, leftControllerTransform.forward);
            else if (Camera.main != null)
                ray = new Ray(Camera.main.transform.position, Camera.main.transform.forward);
            else
                return;
        }
        else
        {
            if (Camera.main != null)
                ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            else
                return;
        }

        if (Physics.Raycast(ray, out RaycastHit hit, 2000f))
        {
            lastHitPoint = hit.point;
            hasLastHit = true;

            if (hit.collider.GetComponent<UnityEngine.Terrain>() != null || hit.collider.CompareTag("Terrain"))
            {
                Vector3 placementPoint = hit.point + Vector3.up * placementYOffset;
                TeleportRover(pendingRobot, placementPoint);

                Log($"Robot '{selectedRobot}' placed at {placementPoint}");
                OnRoverSpawned();
                pendingRobot = null;
            }
            else
            {
                Log($"Raycast hit '{hit.collider.name}' but it is not Terrain.");
                SetGuideText("That's not terrain! Click on the planetary surface to place the robot.");
            }
        }
    }

    private void TeleportRover(GameObject rover, Vector3 position)
    {
        var sync = rover.GetComponent<ProjectName.Rover.RoverPositionSync>();
        if (sync != null)
        {
            sync.TeleportTo(position, rover.transform.rotation);
            return;
        }

        ArticulationBody rootBody = FindRootArticulationBody(rover);
        if (rootBody == null)
        {
            rover.transform.position = position;
            return;
        }

        rootBody.TeleportRoot(position, rover.transform.rotation);
    }

    private ArticulationBody FindRootArticulationBody(GameObject rover)
    {
        var allBodies = rover.GetComponentsInChildren<ArticulationBody>();
        foreach (var body in allBodies)
        {
            if (body.isRoot) return body;
        }
        return null;
    }

    private void SetRoverFrozen(GameObject rover, bool frozen)
    {
        var bodies = rover.GetComponentsInChildren<ArticulationBody>();
        foreach (var body in bodies)
        {
            if (body.isRoot)
            {
                body.immovable = frozen;
            }
            body.useGravity = !frozen;
            if (frozen)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
        }
    }

    private void EnableActiveRoverController()
    {
        if (selectedRobot == "husky" && huskyController != null) huskyController.enabled = true;
        else if (selectedRobot == "m20" && m20Controller != null) m20Controller.enabled = true;
        else if (selectedRobot == "m2020" && m2020Controller != null) m2020Controller.enabled = true;
    }

    private void DisableRoverControllers()
    {
        if (huskyController != null) huskyController.enabled = false;
        if (m20Controller != null) m20Controller.enabled = false;
        if (m2020Controller != null) m2020Controller.enabled = false;
    }

    public void DisableAllRovers()
    {
        if (huskyImporter != null) huskyImporter.enabled = false;
        if (huskyController != null) huskyController.enabled = false;
        if (m20Importer != null) m20Importer.enabled = false;
        if (m20Controller != null) m20Controller.enabled = false;
        if (m2020Importer != null) m2020Importer.enabled = false;
        if (m2020Controller != null) m2020Controller.enabled = false;
    }

    public GameObject ActiveRover => ActiveRoverContext.HasActiveRover ? ActiveRoverContext.Current.gameObject : activeSpawnedRover;

    public void ToggleRelocateMode()
    {
        if (currentSimState == ProjectName.Core.SimulationState.RoverSpawning)
        {
            SetSimulationState(ProjectName.Core.SimulationState.ActiveSimulation);
            SetGuideText("Relocate mode exited.");
        }
        else
        {
            GameObject rover = ActiveRover;
            if (rover != null)
            {
                pendingRobot = rover;
                SetRoverFrozen(pendingRobot, true);
                SetSimulationState(ProjectName.Core.SimulationState.RoverSpawning);
                SetGuideText("Relocate mode active. Click anywhere on terrain to relocate.");
            }
            else
            {
                SetGuideText("No active rover found to relocate. Select and deploy a rover first.");
            }
        }
    }

    public void CenterActiveRoverOnTerrain()
    {
        GameObject rover = ActiveRover;
        if (rover != null)
        {
            var terrain = UnityEngine.Terrain.activeTerrain ?? FindAnyObjectByType<UnityEngine.Terrain>();
            if (terrain != null)
            {
                Vector3 tPos = terrain.transform.position;
                Vector3 tSize = terrain.terrainData.size;
                Vector3 spawnPos = new Vector3(tPos.x + tSize.x * 0.5f, 0f, tPos.z + tSize.z * 0.5f);
                spawnPos.y = terrain.SampleHeight(spawnPos) + tPos.y + 0.35f;
                TeleportRover(rover, spawnPos);
                SetGuideText("Rover centered on terrain.");
            }
        }
        else
        {
            SetGuideText("No active rover found to center. Deploy a rover first.");
        }
    }

    public void RespawnActiveRover()
    {
        if (ActiveRoverContext.HasActiveRover)
        {
            var handle = ActiveRoverContext.Current;
            var terrain = UnityEngine.Terrain.activeTerrain ?? FindAnyObjectByType<UnityEngine.Terrain>();
            Vector3 spawnPos = Vector3.up * 2f;
            if (terrain != null)
            {
                Vector3 tPos = terrain.transform.position;
                Vector3 tSize = terrain.terrainData.size;
                spawnPos = new Vector3(tPos.x + tSize.x * 0.5f, 0f, tPos.z + tSize.z * 0.5f);
                spawnPos.y = terrain.SampleHeight(spawnPos) + tPos.y + (handle.profile != null ? handle.profile.wheelRadius + 0.15f : 0.35f);
            }

            if (handle.rootBody != null)
            {
                handle.rootBody.TeleportRoot(spawnPos, Quaternion.identity);
                handle.rootBody.linearVelocity = Vector3.zero;
                handle.rootBody.angularVelocity = Vector3.zero;
            }
            else if (handle.gameObject != null)
            {
                TeleportRover(handle.gameObject, spawnPos);
            }

            if (handle.telemetry != null)
            {
                handle.telemetry.ResetMissionTimeAndOdometer();
            }
            Log($"Active rover '{handle.roverId}' respawned at {spawnPos}.");
        }
    }

    public void SetGuideText(string message)
    {
        if (guideText != null)
            guideText.text = message;
        onGuideTextChanged?.Invoke(message);
    }

    private bool CheckVRTriggerJustPressed()
    {
        bool isTriggerPressed = false;
        var rightHand = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.RightHand);
        if (rightHand.isValid && rightHand.TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out bool rPressed) && rPressed)
        {
            isTriggerPressed = true;
        }
        else
        {
            var leftHand = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.LeftHand);
            if (leftHand.isValid && leftHand.TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out bool lPressed) && lPressed)
            {
                isTriggerPressed = true;
            }
        }

        bool justPressed = isTriggerPressed && !wasVRTriggerPressedLastFrame;
        wasVRTriggerPressedLastFrame = isTriggerPressed;
        return justPressed;
    }

    private void Log(string message)
    {
        if (showDebugLogs)
            Debug.Log($"[Flow] {message}");
    }

    private void LogWarning(string message)
    {
        if (showDebugLogs)
            Debug.LogWarning($"[Flow] {message}");
    }

    private void OnDrawGizmos()
    {
        if (!showDebugLogs || !hasLastHit) return;
        Gizmos.color = Color.red;
        Gizmos.DrawSphere(lastHitPoint, 0.5f);
    }
}

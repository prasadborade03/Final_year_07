using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;
using ProjectName.Rover;

public class SimulationFlowController : MonoBehaviour
{
    [Header("Terrain")]
    public ProjectName.UI.UploadHeightmapUI uploadUI;
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
    public System.Action<string> onGuideTextChanged;
    public System.Action<string> onStateChanged;

    // Internal state
    public enum State { WaitingForTerrain, WaitingForRobotChoice, WaitingForClickToPlace, ActiveDriving }
    private State currentState = State.WaitingForTerrain;

    public State CurrentState => currentState;
    public string SelectedRobot => selectedRobot;

    private string selectedRobot = "";
    private GameObject pendingRobot = null;
    private GameObject activeSpawnedRover = null;

    private Vector3 lastHitPoint;      // for debug gizmo
    private bool hasLastHit = false;

    [Header("VR Interaction")]
    public Transform rightControllerTransform;
    public Transform leftControllerTransform;
    private bool wasVRTriggerPressedLastFrame = false;

    void Start()
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

        SetState(State.WaitingForTerrain);
        SetGuideText("Configure heightmap tuning & click Generate to begin.");
    }

    void Update()
    {
        if (currentState == State.WaitingForClickToPlace)
        {
            bool vrTriggerDown = CheckVRTriggerJustPressed();
            if (Input.GetMouseButtonDown(0) || vrTriggerDown)
            {
                TryPlaceRobot(vrTriggerDown);
            }
        }
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

    private void SetState(State newState)
    {
        currentState = newState;
        onStateChanged?.Invoke(currentState.ToString());
    }

    // -------------------------------------------------
    // Called by UI after terrain is generated
    // -------------------------------------------------
    public void OnTerrainReady()
    {
        if (robotSelectPanel != null)
            robotSelectPanel.SetActive(true);

        SetState(State.WaitingForRobotChoice);
        SetGuideText("Terrain ready! Select a rover (Husky, M20, or M2020) to deploy.");
        Log("Terrain ready → choose a robot");
    }

    // -------------------------------------------------
    // Called by the UI buttons
    // -------------------------------------------------
    public void OnSelectHusky()
    {
        selectedRobot = "husky";
        if (RoverPlacementController.Instance != null)
        {
            RoverPlacementController.Instance.StartPlacement("husky");
        }
        else
        {
            StartPlacementMode();
        }
    }

    public void OnSelectM20()
    {
        selectedRobot = "m20";
        if (RoverPlacementController.Instance != null)
        {
            RoverPlacementController.Instance.StartPlacement("m20");
        }
        else
        {
            StartPlacementMode();
        }
    }

    public void OnSelectM2020()
    {
        selectedRobot = "m2020";
        if (RoverPlacementController.Instance != null)
        {
            RoverPlacementController.Instance.StartPlacement("m2020");
        }
        else
        {
            StartPlacementMode();
        }
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
            LogWarning($"Spawn failed for '{selectedRobot}'. Check that SpawnRover() returns a GameObject and the importer reference is assigned.");
            SetGuideText("Something went wrong spawning the robot. Check the console.");
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
            SetRoverFrozen(pendingRobot, false);

            if (selectedRobot == "husky" && huskyController != null)
                huskyController.enabled = true;
            else if (selectedRobot == "m20" && m20Controller != null)
                m20Controller.enabled = true;
            else if (selectedRobot == "m2020" && m2020Controller != null)
                m2020Controller.enabled = true;

            SetState(State.ActiveDriving);
            SetGuideText($"{selectedRobot.ToUpper()} active! Drive: WASD / Arrows.");
            Log($"{selectedRobot} spawned and placed on terrain at {spawnPos}. Active driving enabled.");
            pendingRobot = null;
        }
        else
        {
            SetRoverFrozen(pendingRobot, true);
            TeleportRover(pendingRobot, new Vector3(0f, 2f, 0f));
            SetState(State.WaitingForClickToPlace);
            SetGuideText($"Generate terrain and click on the surface to place the {selectedRobot.ToUpper()}.");
            Log($"{selectedRobot} spawned. Waiting for terrain and click to place.");
        }
    }

    private void TryPlaceRobot(bool fromVR = false)
    {
        if (pendingRobot == null) return;

        bool isShift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        if (!isShift && !fromVR && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
        {
            Log("Click ignored — pointer was over an interactive UI element.");
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
                SetRoverFrozen(pendingRobot, false);

                if (selectedRobot == "husky" && huskyController != null)
                    huskyController.enabled = true;
                else if (selectedRobot == "m20" && m20Controller != null)
                    m20Controller.enabled = true;
                else if (selectedRobot == "m2020" && m2020Controller != null)
                    m2020Controller.enabled = true;

                Log($"Robot '{selectedRobot}' placed at {placementPoint}");
                SetGuideText($"{selectedRobot.ToUpper()} active! Drive: WASD. M20: Q/E knee lift. M2020: M (mode), Space (clearance).");

                SetState(State.ActiveDriving);
                pendingRobot = null;
            }
            else
            {
                Log($"Raycast hit '{hit.collider.name}' but it is not Terrain — click ignored.");
                SetGuideText("That's not terrain! Click on the planetary surface to place the robot.");
            }
        }
        else
        {
            Log("Raycast did not hit anything.");
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
            LogWarning($"No root ArticulationBody found anywhere under '{rover.name}' — falling back to transform.position.");
            rover.transform.position = position;
            return;
        }

        rootBody.TeleportRoot(position, rover.transform.rotation);
        Log($"TeleportRoot called on '{rover.name}' (root body: '{rootBody.name}') → {position}");
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

        Log($"SetRoverFrozen({frozen}) applied to {bodies.Length} ArticulationBody components on '{rover.name}'.");
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
        if (currentState == State.WaitingForClickToPlace)
        {
            SetState(State.ActiveDriving);
            SetGuideText("Relocate mode exited.");
        }
        else
        {
            GameObject rover = ActiveRover;
            if (rover != null)
            {
                pendingRobot = rover;
                SetRoverFrozen(pendingRobot, true);
                SetState(State.WaitingForClickToPlace);
                SetGuideText("Relocate mode active. Click anywhere on the terrain to relocate.");
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
            Log($"Active rover '{handle.roverId}' respawned at {spawnPos}. Telemetry reset.");
        }
        else if (!string.IsNullOrEmpty(selectedRobot))
        {
            StartPlacementMode();
        }
    }

    public void SetGuideText(string message)
    {
        if (guideText != null)
            guideText.text = message;
        onGuideTextChanged?.Invoke(message);
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

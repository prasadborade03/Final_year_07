using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;

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

    private Vector3 lastHitPoint;      // for debug gizmo
    private bool hasLastHit = false;

    void Start()
    {
        SetState(State.WaitingForTerrain);
        SetGuideText("Configure heightmap tuning & click Generate to begin.");
    }

    void Update()
    {
        if (currentState == State.WaitingForClickToPlace && Input.GetMouseButtonDown(0))
        {
            TryPlaceRobotAtMouseClick();
        }
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
        StartPlacementMode();
    }

    public void OnSelectM20()
    {
        selectedRobot = "m20";
        StartPlacementMode();
    }

    public void OnSelectM2020()
    {
        selectedRobot = "m2020";
        StartPlacementMode();
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

        // Freeze physics completely while the robot waits off-screen.
        SetRoverFrozen(pendingRobot, true);
        TeleportRover(pendingRobot, new Vector3(0f, 500f, 0f));

        SetState(State.WaitingForClickToPlace);
        SetGuideText($"Click anywhere on the terrain surface to place the {selectedRobot.ToUpper()}.");
        Log($"{selectedRobot} spawned and frozen at holding position. Waiting for click to place.");
    }

    private void TryPlaceRobotAtMouseClick()
    {
        // Ignore clicks that land on UI elements rather than the 3D world.
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
        {
            Log("Click ignored — pointer was over a UI element.");
            return;
        }

        if (pendingRobot == null)
        {
            LogWarning("TryPlaceRobotAtMouseClick called but pendingRobot is null.");
            return;
        }

        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);

        if (Physics.Raycast(ray, out RaycastHit hit, 2000f))
        {
            lastHitPoint = hit.point;
            hasLastHit = true;

            if (hit.collider.GetComponent<Terrain>() != null || hit.collider.CompareTag("Terrain"))
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

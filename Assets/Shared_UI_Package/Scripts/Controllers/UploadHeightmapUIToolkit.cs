using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;
using SFB;
using ProjectName.Rover;
using ProjectName.Planetary;
using ProjectName.UI;
using ProjectName.Core;

namespace ProjectName.Terrain
{
    /// <summary>
    /// Master UI Toolkit Controller for the VR Rover Digital Twin Studio.
    /// Supports both:
    /// 1. DRIVING HUD MODE: Unobstructed, 100% transparent center viewport for driving in 3D,
    ///    with compact telemetry pods on the left/right edges and bottom controls.
    /// 2. FULL STUDIO MODE: Complete 3-column aerospace mission control dashboard
    ///    with topographic radar, presets, deploy cards, and live engine parameters.
    /// Toggle seamlessly anytime via [Tab] or on-screen mode buttons.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class UploadHeightmapUIToolkit : MonoBehaviour
    {
        [Header("System References")]
        public TerrainGenerator terrainGenerator;
        public SimulationFlowController flowController;
        public UIDocument uiDocument;
        public RoverTelemetryProvider activeTelemetryProvider;

        [Header("Tuning Configuration")]
        public TerrainTuningConfig tuningConfig = new TerrainTuningConfig();

        // Working State
        private float[,] currentHeights;
        private Texture2D previewTexture;
        private HeightmapPreset currentPreset = HeightmapPreset.GaleCrater;
        public bool isStudioMinimized = false;
        public bool isDrivingHudMode = false;
        private string activeRoverId = "husky";
        private string currentDriveMode = "CRUISE";

        // UI Element References: Roots & Layouts
        private VisualElement studioRoot;
        private VisualElement studioMainGrid;
        private VisualElement colLeft;
        private VisualElement colCenter;
        private VisualElement colRight;
        private VisualElement cardPosition;
        private VisualElement cardEnvironment;

        private Button btnFloatingDock;
        private Button btnMinimizeStudio;
        private Button btnToggleHUD;

        // Header & View Mode Switch
        private Button btnModeDriving;
        private Button btnModeStudio;
        private Label badgeStatus;
        private Button btnPlanetBadge;
        private Label badgeDriveMode;

        // 7-Step Guided Workflow & Main Menu
        private VisualElement workflowStepBanner;
        private readonly Label[] stepPills = new Label[7];
        private Label lblWorkflowGuide;
        private Button btnResetRoverKeepTerrain;
        private Button btnNewTerrain;
        private Button btnMainMenu;
        public int currentWorkflowStep = 1;

        // Col 1: Kinematics, Attitude, Position
        private Label valLinearSpeed;
        private Label valGroundSpeed;
        private Label valAcceleration;
        private Label valGForce;

        private Label valPitch;
        private Label valRoll;
        private Label valHeading;
        private VisualElement barHeadingLevel;
        private Label iconAttitudeWarning;
        private Label valSlope;

        private Label valCoords;
        private Label valOdometer;
        private Label valMissionTime;

        // Col 2: Topographic Minimap & Deploy
        private VisualElement minimapContainer;
        private Image heightmapPreviewImage;
        private VisualElement radarRoverDot;
        private VisualElement radarRoverHeadingArrow;
        private VisualElement minimapBreadcrumbsContainer;
        private readonly List<VisualElement> breadcrumbDots = new List<VisualElement>();
        private readonly List<Vector3> breadcrumbPositions = new List<Vector3>();
        private const int MAX_BREADCRUMBS = 25;
        private const float BREADCRUMB_MIN_DISTANCE = 1.2f;
        private Label labelRadarRange;
        private Button btnBrowseFile;
        private Button btnPresetGale, btnPresetOlympus, btnPresetShackleton, btnPresetValles, btnPresetFractal;
        private Button btnGenerateTerrain;

        private VisualElement roverEmptyStateBanner;
        private Button btnRoverHusky, btnRoverM20, btnRoverM2020;
        private Label tagHuskyActive, tagM20Active, tagM2020Active;

        // HUD Visibility toggle
        private bool isHudHidden = false;

        // Col 3: Actuators, Power, Environment (Compass card eliminated in Phase 6)

        private VisualElement wheelContainer;
        private class WheelUIItem
        {
            public Label nameLabel;
            public Label tag;
            public VisualElement segsContainer;
            public VisualElement[] segs;
        }
        private readonly List<WheelUIItem> activeWheelUIs = new List<WheelUIItem>();
        private string lastWheelRoverId = "";
        private float uiUpdateTimer = 0f;
        private const float UI_UPDATE_INTERVAL = 0.1f; // 10 Hz throttle

        private VisualElement barBattery, barMotorTemp;
        private Label valBattery, valMotorTemp;

        private Label valEnvGravity, valEnvTemp, valEnvPress, valEnvDust;
        private Label lblEnvHazardTitle;

        // Bottom Bar: Modes & Perspectives
        private VisualElement groupDriveModes;
        private Label lblDriveGroup;
        private Label lblHelperHint;
        private Button btnModeStop, btnModeCruise, btnModeExplore, btnModePrecision;
        private Button btnCamFront, btnCamRear, btnCamLeft, btnCamRight, btnCamTop, btnCamFree;
        private Slider sliderCamSpeed;
        private Label valCamSpeedHud;
        private RoverCameraRig.Perspective currentCamPerspective = RoverCameraRig.Perspective.Free;

        // Placement Mode Banner & Controls
        private VisualElement placementBanner;
        private Label lblPlacementBannerText;
        private Button btnPlacementQuickCentre;
        private Button btnPlacementCancel;
        private Button btnQuickSpawnCentre;
        private VisualElement studioBottomBar;
        public bool isPlacementMode = false;

        // Terrain Lab Elements & State
        private Button btnTabMission;
        private Button btnTabTerrainLab;
        private VisualElement tabMissionView;
        private ScrollView tabTerrainLabView;
        private bool isTerrainLabActive = false;

        // Terrain Lab: Presets, Seed, Files
        private Button btnLabPresetGale, btnLabPresetOlympus, btnLabPresetShackleton, btnLabPresetValles, btnLabPresetPlains, btnLabPresetFractal;
        private IntegerField fieldSeed;
        private Button btnRandomSeed;
        private Button btnImportPNG, btnExportPNG;
        private int currentSeed = 0;

        // Terrain Lab: 2D Canvas Painter & Dirty-Rect Undo/Redo
        private VisualElement painterBrushCursor;
        private Image heightmapPainterImage;
        private Button btnBrushRaise, btnBrushLower, btnBrushSmooth, btnBrushFlatten, btnBrushNoise;
        private Button btnUndo, btnRedo;
        private Label labelHistoryStatus;
        private Slider sliderBrushRadius, sliderBrushStrength, sliderBrushHardness, sliderFlattenHeight;
        private VisualElement rowFlattenHeight;
        private Label labelBrushRadiusVal, labelBrushStrengthVal, labelBrushHardnessVal, labelFlattenHeightVal;
        private BrushMode currentBrushMode = BrushMode.Raise;
        private float brushRadius = 0.08f;
        private float brushStrength = 0.40f;
        private float brushHardness = 0.50f;
        private float targetFlattenHeight = 0.50f;
        private bool isPainting = false;

        public struct HeightStrokeDiff
        {
            public RectInt rect;
            public float[,] oldHeights;
            public float[,] newHeights;
        }
        private readonly Stack<HeightStrokeDiff> undoStack = new Stack<HeightStrokeDiff>();
        private readonly Stack<HeightStrokeDiff> redoStack = new Stack<HeightStrokeDiff>();
        private const int MAX_UNDO_STEPS = 20;
        private readonly Dictionary<int, float> strokeTouchedCells = new Dictionary<int, float>();

        // Terrain Lab: Spatial Tuning
        private Slider sliderMaxHeight, sliderBaseOffset, sliderTerrainSize, sliderSmoothing;
        private Label labelMaxHeightVal, labelBaseOffsetVal, labelTerrainSizeVal, labelSmoothingVal;
        private DropdownField dropdownResolution;
        private Button btnCurveLinear, btnCurveExponential, btnCurveRidge, btnCurveBasin;

        // Terrain Lab: Surface Material & Follow Planet
        private Toggle toggleFollowPlanet;
        private bool followPlanetProfile = true;
        private Button btnMatMartian, btnMatLunar, btnMatBasalt, btnMatIce, btnMatCanyon, btnMatWireframe, btnMatNormal;

        // Terrain Lab: Apply & Live Preview
        private Toggle toggleLivePreview;
        private bool isLivePreviewEnabled = false;
        private Button btnApplyTerrainLab;

        // =============================================================
        // Guided Simulation Flow Elements & State
        // =============================================================
        private VisualElement guidedFlowContainer;
        private VisualElement panelEnvironmentSetup;
        private VisualElement panelTerrainReady;
        private VisualElement panelRoverSelection;
        private VisualElement panelSimulationReady;

        // Flow Planet Selection (State 1)
        private Button btnPlanetMars;
        private Button btnPlanetEarth;
        private Button btnPlanetMoon;
        private Button btnPlanetTitan;
        private Button btnPlanetVenus;

        // Modern UI Toolkit Planet Selection Modal (Runtime & State 5)
        private VisualElement planetSelectionModal;
        private Button btnPlanetModalClose;
        private Button btnModalPlanetMars;
        private Button btnModalPlanetEarth;
        private Button btnModalPlanetMoon;
        private Button btnModalPlanetTitan;
        private Button btnModalPlanetVenus;

        private Label lblModalInspectorName;
        private Label lblModalInspectorBadge;
        private Label lblModalInspectorDesc;
        private Label lblModalGravity;
        private Label lblModalTemp;
        private Label lblModalPress;
        private Label lblModalSkybox;
        private Label lblModalFriction;
        private Label lblModalHazard;

        private Button btnModalCancelPlanet;
        private Button btnModalApplyPlanet;
        private Button btnModalApplyAndRespawn;

        public bool isPlanetModalOpen = false;
        private string pendingModalPlanet = "Mars";

        // Flow Terrain Source
        private Button btnSourcePreset;
        private Button btnSourceUpload;
        private Button btnSourcePainter;
        private VisualElement viewSourcePreset;
        private VisualElement viewSourceUpload;
        private VisualElement viewSourcePainter;

        // Flow Presets
        private Button btnFlowPresetGale, btnFlowPresetOlympus, btnFlowPresetValles, btnFlowPresetShackleton, btnFlowPresetPlains, btnFlowPresetFractal;

        // Flow Upload
        private Button btnFlowUploadImg;
        private Label lblFlowUploadStatus;

        // Flow Materials
        private Button btnFlowMatMartian, btnFlowMatLunar, btnFlowMatBasalt, btnFlowMatIce, btnFlowMatCanyon, btnFlowMatWireframe;

        // Flow Advanced Settings
        private Button btnToggleAdvancedSettings;
        private Label lblAdvancedToggleText;
        private VisualElement viewAdvancedSettings;
        private bool isAdvancedSettingsVisible = false;
        private Slider sliderFlowMaxHeight, sliderFlowBaseOffset, sliderFlowTerrainSize, sliderFlowSmoothing;
        private Label labelFlowMaxHeightVal, labelFlowBaseOffsetVal, labelFlowTerrainSizeVal, labelFlowSmoothingVal;
        private DropdownField dropdownFlowResolution;
        private IntegerField fieldFlowSeed;
        private Button btnFlowRandomSeed;
        private Button btnFlowGenerateTerrain;

        // Flow Terrain Ready
        private Label lblReadyPlanet, lblReadyTerrain, lblReadyMaterial, lblReadySize, lblReadyRes, lblReadyGravity;
        private Button btnReadyChangeTerrain, btnReadyContinue;

        // Flow Rover Selection
        private Button btnFlowRoverHusky, btnFlowRoverM20, btnFlowRoverM2020, btnFlowRoverUrdf;
        private Button btnFlowClickToSpawn, btnFlowCenterSpawn, btnFlowBackToTerrain;
        private string flowSelectedRoverId = "husky";

        // Flow Simulation Ready
        private Label lblSimReadyPlanet, lblSimReadyTerrain, lblSimReadyMaterial, lblSimReadyRover, lblSimReadyGravity;
        private Button btnSimReadyBack, btnSimReadyStart;

        // Confirmation Modal
        private VisualElement modalConfirmation;
        private Label lblModalTitle;
        private Label lblModalMessage;
        private Button btnModalCancel;
        private Button btnModalConfirm;
        private Action pendingModalConfirmAction;

        private void Awake()
        {
            if (uiDocument == null) uiDocument = GetComponent<UIDocument>();
            if (terrainGenerator == null) terrainGenerator = FindAnyObjectByType<TerrainGenerator>();
            if (flowController == null) flowController = FindAnyObjectByType<SimulationFlowController>();

            if (!SceneLoader.IsVREnabled)
            {
                var flatSettings = Resources.Load<PanelSettings>("FlatPanelSettings");
                if (flatSettings != null && uiDocument != null)
                {
                    uiDocument.panelSettings = flatSettings;
                }
                var boxCol = GetComponent<BoxCollider>();
                if (boxCol != null) boxCol.enabled = false;

                var vrPos = GetComponent<VRUIPositioner>();
                if (vrPos != null) vrPos.enabled = false;
            }
            else
            {
                if (GetComponent<VRUIPositioner>() == null)
                {
                    gameObject.AddComponent<VRUIPositioner>();
                }
            }

            currentHeights = HeightmapLoader.GeneratePresetHeights(
                HeightmapPreset.GaleCrater,
                tuningConfig.resolution,
                tuningConfig.smoothingFactor,
                tuningConfig.heightCurve,
                currentSeed
            );
        }

        private void OnEnable()
        {
            ActiveRoverContext.OnRoverActivated += HandleRoverActivated;
            ActiveRoverContext.OnRoverDestroyed += HandleRoverDestroyed;
            RoverCameraRig.OnPerspectiveChanged += HandleRigPerspectiveChanged;
            RoverCameraRig.OnCameraSpeedChanged += HandleCameraSpeedChanged;

            var envCtrl = PlanetEnvironmentController.Instance ?? (PlanetEnvironmentController)FindAnyObjectByType<PlanetEnvironmentController>();
            if (envCtrl != null)
            {
                envCtrl.OnProfileApplied -= HandlePlanetaryProfileApplied;
                envCtrl.OnProfileApplied += HandlePlanetaryProfileApplied;
            }

            if (flowController == null) flowController = FindAnyObjectByType<SimulationFlowController>();
            if (flowController != null)
            {
                flowController.OnSimulationStateChanged -= HandleSimulationStateChanged;
                flowController.OnSimulationStateChanged += HandleSimulationStateChanged;
            }

            BindUIElements();
            RefreshPreview();
            UpdatePresetButtonsUI();
            UpdateRoverSelectionUI();
            UpdateBrushButtonStyles();
            UpdateCurveButtonStyles();
            UpdateMaterialButtonStyles();
            UpdateHistoryUI();

            if (RoverCameraRig.Instance != null)
            {
                currentCamPerspective = RoverCameraRig.Instance.currentPerspective;
            }
            UpdateCameraButtonsUI();

            if (ActiveRoverContext.HasActiveRover)
            {
                HandleRoverActivated(ActiveRoverContext.Current);
            }
            else
            {
                SetTelemetryStandbyState();
                ConfigureDriveBarForStandby();
            }

            if (flowController != null)
            {
                UpdateGuidedFlowUI(flowController.CurrentSimulationState);
            }
            else
            {
                var curTerrain = terrainGenerator != null ? terrainGenerator.GetCurrentTerrain() : UnityEngine.Terrain.activeTerrain;
                UpdateGuidedFlowUI(curTerrain != null ? ProjectName.Core.SimulationState.TerrainReady : ProjectName.Core.SimulationState.EnvironmentSetup);
            }
        }

        private void BindUIElements()
        {
            if (uiDocument == null) uiDocument = GetComponent<UIDocument>();
            if (uiDocument == null) return;

            VisualElement root = uiDocument.rootVisualElement;
            if (root == null) return;

            root.RegisterCallback<GeometryChangedEvent>(OnRootGeometryChanged);

            // Roots & Layout Elements
            studioRoot = root.Q<VisualElement>("StudioRoot");
            studioMainGrid = root.Q<VisualElement>("StudioMainGrid");
            colLeft = root.Q<VisualElement>("ColLeft");
            colCenter = root.Q<VisualElement>("ColCenter");
            colRight = root.Q<VisualElement>("ColRight");
            var scrollLeft = root.Q<ScrollView>("ScrollColLeft");
            if (scrollLeft != null) scrollLeft.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            var scrollRight = root.Q<ScrollView>("ScrollColRight");
            if (scrollRight != null) scrollRight.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            cardPosition = root.Q<VisualElement>("CardPosition");
            cardEnvironment = root.Q<VisualElement>("CardEnvironment");

            btnFloatingDock = root.Q<Button>("BtnFloatingDock");
            btnMinimizeStudio = root.Q<Button>("BtnMinimizeStudio");
            btnToggleHUD = root.Q<Button>("BtnToggleHUD");

            if (btnMinimizeStudio != null) btnMinimizeStudio.clicked += () => SetStudioMinimized(true);
            if (btnFloatingDock != null) btnFloatingDock.clicked += () => SetStudioMinimized(false);
            if (btnToggleHUD != null) btnToggleHUD.clicked += () => SetDrivingHudMode(!isDrivingHudMode);

            // View Mode Switches
            btnModeDriving = root.Q<Button>("BtnModeDriving");
            btnModeStudio = root.Q<Button>("BtnModeStudio");
            if (btnModeDriving != null) btnModeDriving.clicked += () => SetDrivingHudMode(true);
            if (btnModeStudio != null) btnModeStudio.clicked += () => SetDrivingHudMode(false);

            // Header Pills
            badgeStatus = root.Q<Label>("BadgeStatus");
            btnPlanetBadge = root.Q<Button>("BtnPlanetBadge");
            badgeDriveMode = root.Q<Label>("BadgeDriveMode");

            if (btnPlanetBadge != null)
            {
                var lbl = btnPlanetBadge.Q<Label>("LblPlanetName");
                if (lbl != null) btnPlanetBadge.text = "";
                btnPlanetBadge.clicked += () =>
                {
                    TogglePlanetModal();
                };
            }

            btnMainMenu = root.Q<Button>("BtnMainMenu");
            if (btnMainMenu != null)
            {
                btnMainMenu.clicked += () =>
                {
                    ProjectName.Core.SceneLoader.LoadMainMenu();
                };
            }

            // 7-Step Guided Workflow Banner Elements
            workflowStepBanner = root.Q<VisualElement>("WorkflowStepBanner");
            for (int i = 0; i < 7; i++)
            {
                stepPills[i] = root.Q<Label>($"StepPill{i + 1}");
            }
            lblWorkflowGuide = root.Q<Label>("LblWorkflowGuide");
            btnResetRoverKeepTerrain = root.Q<Button>("BtnResetRoverKeepTerrain");
            btnNewTerrain = root.Q<Button>("BtnNewTerrain");

            if (btnResetRoverKeepTerrain != null)
            {
                btnResetRoverKeepTerrain.clicked += OnResetRoverKeepTerrainClicked;
            }

            if (btnNewTerrain != null)
            {
                btnNewTerrain.clicked += OnNewTerrainClicked;
            }

            // Col 1: Kinematics, Attitude, Position
            valLinearSpeed = root.Q<Label>("ValLinearSpeed");
            valGroundSpeed = root.Q<Label>("ValGroundSpeed");
            valAcceleration = root.Q<Label>("ValAcceleration");
            valGForce = root.Q<Label>("ValGForce");

            valPitch = root.Q<Label>("ValPitch");
            valRoll = root.Q<Label>("ValRoll");
            valHeading = root.Q<Label>("ValHeading");
            barHeadingLevel = root.Q<VisualElement>("BarHeadingLevel");
            iconAttitudeWarning = root.Q<Label>("IconAttitudeWarning");
            valSlope = root.Q<Label>("ValSlope");

            valCoords = root.Q<Label>("ValCoords");
            valOdometer = root.Q<Label>("ValOdometer");
            valMissionTime = root.Q<Label>("ValMissionTime");

            // Col 2: Topographic Minimap & Deploy
            minimapContainer = root.Q<VisualElement>("MinimapContainer");
            heightmapPreviewImage = root.Q<Image>("HeightmapPreviewImage");
            radarRoverDot = root.Q<VisualElement>("RadarRoverDot");
            radarRoverHeadingArrow = root.Q<VisualElement>("RadarRoverHeadingArrow");
            minimapBreadcrumbsContainer = root.Q<VisualElement>("MinimapBreadcrumbsContainer");
            labelRadarRange = root.Q<Label>("LabelRadarRange");
            roverEmptyStateBanner = root.Q<VisualElement>("RoverEmptyStateBanner");
            btnBrowseFile = root.Q<Button>("BtnBrowseFile");
            if (btnBrowseFile != null) btnBrowseFile.clicked += OnBrowseFileClicked;

            btnPresetGale = root.Q<Button>("BtnPresetGale");
            btnPresetOlympus = root.Q<Button>("BtnPresetOlympus");
            btnPresetShackleton = root.Q<Button>("BtnPresetShackleton");
            btnPresetValles = root.Q<Button>("BtnPresetValles");
            btnPresetFractal = root.Q<Button>("BtnPresetFractal");

            if (btnPresetGale != null) btnPresetGale.clicked += () => SelectPreset(HeightmapPreset.GaleCrater);
            if (btnPresetOlympus != null) btnPresetOlympus.clicked += () => SelectPreset(HeightmapPreset.OlympusMons);
            if (btnPresetShackleton != null) btnPresetShackleton.clicked += () => SelectPreset(HeightmapPreset.ShackletonCrater);
            if (btnPresetValles != null) btnPresetValles.clicked += () => SelectPreset(HeightmapPreset.VallesMarineris);
            if (btnPresetFractal != null) btnPresetFractal.clicked += () => SelectPreset(HeightmapPreset.ProceduralFractal);

            btnGenerateTerrain = root.Q<Button>("BtnGenerateTerrain");
            if (btnGenerateTerrain != null)
            {
                btnGenerateTerrain.text = "";
                btnGenerateTerrain.clicked += OnGenerateTerrainClicked;
            }

            // Deploy Rover
            btnRoverHusky = root.Q<Button>("BtnRoverHusky");
            btnRoverM20 = root.Q<Button>("BtnRoverM20");
            btnRoverM2020 = root.Q<Button>("BtnRoverM2020");

            tagHuskyActive = root.Q<Label>("TagHuskyActive");
            tagM20Active = root.Q<Label>("TagM20Active");
            tagM2020Active = root.Q<Label>("TagM2020Active");

            if (btnRoverHusky != null) btnRoverHusky.clicked += () => DeployRover("husky");
            if (btnRoverM20 != null) btnRoverM20.clicked += () => DeployRover("m20");
            if (btnRoverM2020 != null) btnRoverM2020.clicked += () => DeployRover("m2020");

            var btnRelocate = root.Q<Button>("BtnRelocateRover");
            if (btnRelocate != null) btnRelocate.clicked += OnRelocateRoverClicked;

            var btnCenter = root.Q<Button>("BtnCenterRover");
            if (btnCenter != null) btnCenter.clicked += OnCenterRoverClicked;

            btnQuickSpawnCentre = root.Q<Button>("BtnQuickSpawnCentre");
            if (btnQuickSpawnCentre != null)
            {
                btnQuickSpawnCentre.clicked += () =>
                {
                    if (RoverPlacementController.Instance != null)
                        RoverPlacementController.Instance.QuickSpawnTerrainCentre();
                };
            }

            // Minimap click placement
            if (heightmapPreviewImage != null)
            {
                heightmapPreviewImage.RegisterCallback<ClickEvent>(OnMinimapImageClicked);
            }

            // Placement Mode Banner Elements
            placementBanner = root.Q<VisualElement>("PlacementBanner");
            lblPlacementBannerText = root.Q<Label>("LblPlacementBannerText");
            btnPlacementQuickCentre = root.Q<Button>("BtnPlacementQuickCentre");
            btnPlacementCancel = root.Q<Button>("BtnPlacementCancel");

            if (btnPlacementQuickCentre != null)
            {
                btnPlacementQuickCentre.clicked += () =>
                {
                    if (RoverPlacementController.Instance != null)
                        RoverPlacementController.Instance.QuickSpawnTerrainCentre();
                };
            }

            if (btnPlacementCancel != null)
            {
                btnPlacementCancel.clicked += () =>
                {
                    if (RoverPlacementController.Instance != null)
                        RoverPlacementController.Instance.CancelPlacement();
                };
            }

            studioBottomBar = root.Q<VisualElement>(className: "studio-bottom-bar");

            // Col 3: Actuators, Power, Environment (Compass card removed in Phase 6)

            wheelContainer = root.Q<VisualElement>("WheelContainer") ?? root.Q<VisualElement>(className: "wheel-grid-2x2");

            barBattery = root.Q<VisualElement>("BarBattery");
            barMotorTemp = root.Q<VisualElement>("BarMotorTemp");
            valBattery = root.Q<Label>("ValBattery");
            valMotorTemp = root.Q<Label>("ValMotorTemp");

            valEnvGravity = root.Q<Label>("ValEnvGravity");
            valEnvTemp = root.Q<Label>("ValEnvTemp");
            valEnvPress = root.Q<Label>("ValEnvPress");
            valEnvDust = root.Q<Label>("ValEnvHazard") ?? root.Q<Label>("ValEnvDust");
            lblEnvHazardTitle = root.Q<Label>("LabelEnvHazardTitle");

            // Bottom Bar: Labels & Groups
            groupDriveModes = root.Q<VisualElement>("GroupDriveModes");
            lblDriveGroup = root.Q<Label>("LblDriveGroup");
            lblHelperHint = root.Q<Label>("LblHelperHint");

            // Bottom Bar: Modes
            btnModeStop = root.Q<Button>("BtnModeStop");
            btnModeCruise = root.Q<Button>("BtnModeCruise");
            btnModeExplore = root.Q<Button>("BtnModeExplore");
            btnModePrecision = root.Q<Button>("BtnModePrecision");

            if (btnModeStop != null) btnModeStop.clicked += () => OnDriveButtonClicked(0);
            if (btnModeCruise != null) btnModeCruise.clicked += () => OnDriveButtonClicked(1);
            if (btnModeExplore != null) btnModeExplore.clicked += () => OnDriveButtonClicked(2);
            if (btnModePrecision != null) btnModePrecision.clicked += () => OnDriveButtonClicked(3);

            // Bottom Bar: Camera Perspectives
            btnCamFront = root.Q<Button>("BtnCamFront");
            btnCamRear = root.Q<Button>("BtnCamRear");
            btnCamLeft = root.Q<Button>("BtnCamLeft");
            btnCamRight = root.Q<Button>("BtnCamRight");
            btnCamTop = root.Q<Button>("BtnCamTop");
            btnCamFree = root.Q<Button>("BtnCamFree");

            if (btnCamFront != null) btnCamFront.clicked += () => SetCameraPerspective(RoverCameraRig.Perspective.Front);
            if (btnCamRear != null) btnCamRear.clicked += () => SetCameraPerspective(RoverCameraRig.Perspective.Rear);
            if (btnCamLeft != null) btnCamLeft.clicked += () => SetCameraPerspective(RoverCameraRig.Perspective.Left);
            if (btnCamRight != null) btnCamRight.clicked += () => SetCameraPerspective(RoverCameraRig.Perspective.Right);
            if (btnCamTop != null) btnCamTop.clicked += () => SetCameraPerspective(RoverCameraRig.Perspective.Top);
            if (btnCamFree != null) btnCamFree.clicked += () => SetCameraPerspective(RoverCameraRig.Perspective.Free);

            // Camera Speed Tuner
            sliderCamSpeed = root.Q<Slider>("SliderCamSpeed");
            valCamSpeedHud = root.Q<Label>("ValCamSpeedHud");
            if (sliderCamSpeed != null)
            {
                float initialSpeed = RoverCameraRig.FreeFlySpeed;
                sliderCamSpeed.value = initialSpeed;
                if (valCamSpeedHud != null) valCamSpeedHud.text = $"{initialSpeed:F1} m/s";
                sliderCamSpeed.RegisterValueChangedCallback(evt =>
                {
                    RoverCameraRig.FreeFlySpeed = evt.newValue;
                    if (valCamSpeedHud != null) valCamSpeedHud.text = $"{evt.newValue:F1} m/s";
                });
            }

            // ---------------------------------------------------------
            // Terrain Lab: Tab Selector & Workbench Bindings
            // ---------------------------------------------------------
            btnTabMission = root.Q<Button>("BtnTabMission");
            btnTabTerrainLab = root.Q<Button>("BtnTabTerrainLab");
            tabMissionView = root.Q<VisualElement>("TabMissionView");
            tabTerrainLabView = root.Q<ScrollView>("TabTerrainLabView");

            if (btnTabMission != null) btnTabMission.clicked += () => SetTerrainLabActive(false);
            if (btnTabTerrainLab != null) btnTabTerrainLab.clicked += () => SetTerrainLabActive(true);

            // Presets & Seed
            btnLabPresetGale = root.Q<Button>("BtnLabPresetGale");
            btnLabPresetOlympus = root.Q<Button>("BtnLabPresetOlympus");
            btnLabPresetShackleton = root.Q<Button>("BtnLabPresetShackleton");
            btnLabPresetValles = root.Q<Button>("BtnLabPresetValles");
            btnLabPresetPlains = root.Q<Button>("BtnLabPresetPlains");
            btnLabPresetFractal = root.Q<Button>("BtnLabPresetFractal");

            if (btnLabPresetGale != null) btnLabPresetGale.clicked += () => SelectPreset(HeightmapPreset.GaleCrater);
            if (btnLabPresetOlympus != null) btnLabPresetOlympus.clicked += () => SelectPreset(HeightmapPreset.OlympusMons);
            if (btnLabPresetShackleton != null) btnLabPresetShackleton.clicked += () => SelectPreset(HeightmapPreset.ShackletonCrater);
            if (btnLabPresetValles != null) btnLabPresetValles.clicked += () => SelectPreset(HeightmapPreset.VallesMarineris);
            if (btnLabPresetPlains != null) btnLabPresetPlains.clicked += () => SelectPreset(HeightmapPreset.Plains);
            if (btnLabPresetFractal != null) btnLabPresetFractal.clicked += () => SelectPreset(HeightmapPreset.ProceduralFractal);

            fieldSeed = root.Q<IntegerField>("FieldSeed");
            btnRandomSeed = root.Q<Button>("BtnRandomSeed");
            if (fieldSeed != null)
            {
                fieldSeed.value = currentSeed;
                fieldSeed.RegisterValueChangedCallback(evt =>
                {
                    currentSeed = evt.newValue;
                    RegeneratePreset();
                });
            }
            if (btnRandomSeed != null)
            {
                btnRandomSeed.clicked += () =>
                {
                    currentSeed = UnityEngine.Random.Range(0, 999999);
                    if (fieldSeed != null) fieldSeed.value = currentSeed;
                    RegeneratePreset();
                };
            }

            btnImportPNG = root.Q<Button>("BtnImportPNG");
            btnExportPNG = root.Q<Button>("BtnExportPNG");
            if (btnImportPNG != null) btnImportPNG.clicked += OnImportPNGClicked;
            if (btnExportPNG != null) btnExportPNG.clicked += OnExportPNGClicked;

            // 2D Canvas Painter & Dynamic Brush Cursor
            painterBrushCursor = root.Q<VisualElement>("PainterBrushCursor");
            if (heightmapPreviewImage != null)
            {
                heightmapPreviewImage.RegisterCallback<PointerDownEvent>(OnPainterPointerDown);
                heightmapPreviewImage.RegisterCallback<PointerMoveEvent>(OnPainterPointerMove);
                heightmapPreviewImage.RegisterCallback<PointerUpEvent>(OnPainterPointerUp);
                heightmapPreviewImage.RegisterCallback<PointerLeaveEvent>(OnPainterPointerLeave);
            }
            if (minimapContainer != null)
            {
                minimapContainer.RegisterCallback<PointerLeaveEvent>(OnPainterPointerLeave);
            }

            heightmapPainterImage = root.Q<Image>("HeightmapPainterImage");
            if (heightmapPainterImage != null)
            {
                heightmapPainterImage.image = previewTexture;
                heightmapPainterImage.RegisterCallback<PointerDownEvent>(OnPainterPointerDown);
                heightmapPainterImage.RegisterCallback<PointerMoveEvent>(OnPainterPointerMove);
                heightmapPainterImage.RegisterCallback<PointerUpEvent>(OnPainterPointerUp);
                heightmapPainterImage.RegisterCallback<PointerLeaveEvent>(OnPainterPointerLeave);
            }

            btnBrushRaise = root.Q<Button>("BtnBrushRaise");
            btnBrushLower = root.Q<Button>("BtnBrushLower");
            btnBrushSmooth = root.Q<Button>("BtnBrushSmooth");
            btnBrushFlatten = root.Q<Button>("BtnBrushFlatten");
            btnBrushNoise = root.Q<Button>("BtnBrushNoise");

            if (btnBrushRaise != null) btnBrushRaise.clicked += () => SetBrushMode(BrushMode.Raise);
            if (btnBrushLower != null) btnBrushLower.clicked += () => SetBrushMode(BrushMode.Lower);
            if (btnBrushSmooth != null) btnBrushSmooth.clicked += () => SetBrushMode(BrushMode.Smooth);
            if (btnBrushFlatten != null) btnBrushFlatten.clicked += () => SetBrushMode(BrushMode.Flatten);
            if (btnBrushNoise != null) btnBrushNoise.clicked += () => SetBrushMode(BrushMode.Noise);

            btnUndo = root.Q<Button>("BtnUndo");
            btnRedo = root.Q<Button>("BtnRedo");
            labelHistoryStatus = root.Q<Label>("LabelHistoryStatus");
            if (btnUndo != null) btnUndo.clicked += UndoLastStroke;
            if (btnRedo != null) btnRedo.clicked += RedoStroke;

            sliderBrushRadius = root.Q<Slider>("SliderBrushRadius");
            labelBrushRadiusVal = root.Q<Label>("LabelBrushRadiusVal");
            if (sliderBrushRadius != null)
            {
                sliderBrushRadius.value = brushRadius;
                sliderBrushRadius.RegisterValueChangedCallback(evt =>
                {
                    brushRadius = evt.newValue;
                    if (labelBrushRadiusVal != null) labelBrushRadiusVal.text = $"{(int)(brushRadius * 100f)}%";
                    if (painterBrushCursor != null && painterBrushCursor.style.display == DisplayStyle.Flex)
                    {
                        var targetImg = heightmapPreviewImage ?? heightmapPainterImage;
                        float canvasW = targetImg != null ? targetImg.resolvedStyle.width : 164f;
                        if (canvasW <= 0f && minimapContainer != null) canvasW = minimapContainer.resolvedStyle.width;
                        if (canvasW <= 0f) canvasW = 164f;
                        float rPx = Mathf.Max(3f, brushRadius * canvasW);
                        painterBrushCursor.style.width = rPx * 2f;
                        painterBrushCursor.style.height = rPx * 2f;
                    }
                });
            }

            sliderBrushStrength = root.Q<Slider>("SliderBrushStrength");
            labelBrushStrengthVal = root.Q<Label>("LabelBrushStrengthVal");
            if (sliderBrushStrength != null)
            {
                sliderBrushStrength.value = brushStrength;
                sliderBrushStrength.RegisterValueChangedCallback(evt =>
                {
                    brushStrength = evt.newValue;
                    if (labelBrushStrengthVal != null) labelBrushStrengthVal.text = $"{(int)(brushStrength * 100f)}%";
                });
            }

            sliderBrushHardness = root.Q<Slider>("SliderBrushHardness");
            labelBrushHardnessVal = root.Q<Label>("LabelBrushHardnessVal");
            if (sliderBrushHardness != null)
            {
                sliderBrushHardness.value = brushHardness;
                sliderBrushHardness.RegisterValueChangedCallback(evt =>
                {
                    brushHardness = evt.newValue;
                    if (labelBrushHardnessVal != null) labelBrushHardnessVal.text = $"{(int)(brushHardness * 100f)}%";
                });
            }

            rowFlattenHeight = root.Q<VisualElement>("RowFlattenHeight");
            sliderFlattenHeight = root.Q<Slider>("SliderFlattenHeight");
            labelFlattenHeightVal = root.Q<Label>("LabelFlattenHeightVal");
            if (sliderFlattenHeight != null)
            {
                sliderFlattenHeight.value = targetFlattenHeight;
                sliderFlattenHeight.RegisterValueChangedCallback(evt =>
                {
                    targetFlattenHeight = evt.newValue;
                    if (labelFlattenHeightVal != null) labelFlattenHeightVal.text = $"{(int)(targetFlattenHeight * 100f)}%";
                });
            }

            // Spatial Tuning
            sliderMaxHeight = root.Q<Slider>("SliderMaxHeight");
            labelMaxHeightVal = root.Q<Label>("LabelMaxHeightVal");
            if (sliderMaxHeight != null)
            {
                sliderMaxHeight.value = tuningConfig.maxHeight;
                sliderMaxHeight.RegisterValueChangedCallback(evt =>
                {
                    tuningConfig.maxHeight = evt.newValue;
                    if (labelMaxHeightVal != null) labelMaxHeightVal.text = $"{evt.newValue:F0} m";
                    if (isLivePreviewEnabled) SyncTerrainDimensionsLive();
                });
            }

            sliderBaseOffset = root.Q<Slider>("SliderBaseOffset");
            labelBaseOffsetVal = root.Q<Label>("LabelBaseOffsetVal");
            if (sliderBaseOffset != null)
            {
                sliderBaseOffset.value = tuningConfig.baseOffset;
                sliderBaseOffset.RegisterValueChangedCallback(evt =>
                {
                    tuningConfig.baseOffset = evt.newValue;
                    if (labelBaseOffsetVal != null) labelBaseOffsetVal.text = $"{evt.newValue:F0} m";
                    if (isLivePreviewEnabled) SyncTerrainDimensionsLive();
                });
            }

            sliderTerrainSize = root.Q<Slider>("SliderTerrainSize");
            labelTerrainSizeVal = root.Q<Label>("LabelTerrainSizeVal");
            if (sliderTerrainSize != null)
            {
                sliderTerrainSize.value = tuningConfig.terrainWidth;
                sliderTerrainSize.RegisterValueChangedCallback(evt =>
                {
                    tuningConfig.terrainWidth = evt.newValue;
                    tuningConfig.terrainLength = evt.newValue;
                    if (labelTerrainSizeVal != null) labelTerrainSizeVal.text = $"{evt.newValue:F0} x {evt.newValue:F0} m";
                    if (isLivePreviewEnabled) SyncTerrainDimensionsLive();
                });
            }

            dropdownResolution = root.Q<DropdownField>("DropdownResolution");
            if (dropdownResolution != null)
            {
                if (tuningConfig.resolution == 257) dropdownResolution.index = 0;
                else if (tuningConfig.resolution == 1025) dropdownResolution.index = 2;
                else if (tuningConfig.resolution == 2049) dropdownResolution.index = 3;
                else dropdownResolution.index = 1;

                dropdownResolution.RegisterValueChangedCallback(OnResolutionChanged);
            }

            sliderSmoothing = root.Q<Slider>("SliderSmoothing");
            labelSmoothingVal = root.Q<Label>("LabelSmoothingVal");
            if (sliderSmoothing != null)
            {
                sliderSmoothing.value = tuningConfig.smoothingFactor;
                sliderSmoothing.RegisterValueChangedCallback(evt =>
                {
                    tuningConfig.smoothingFactor = evt.newValue;
                    if (labelSmoothingVal != null) labelSmoothingVal.text = $"{evt.newValue:F1}";
                });
            }

            btnCurveLinear = root.Q<Button>("BtnCurveLinear");
            btnCurveExponential = root.Q<Button>("BtnCurveExponential");
            btnCurveRidge = root.Q<Button>("BtnCurveRidge");
            btnCurveBasin = root.Q<Button>("BtnCurveBasin");

            if (btnCurveLinear != null) btnCurveLinear.clicked += () => SetHeightCurve(HeightRemapCurve.Linear);
            if (btnCurveExponential != null) btnCurveExponential.clicked += () => SetHeightCurve(HeightRemapCurve.Exponential);
            if (btnCurveRidge != null) btnCurveRidge.clicked += () => SetHeightCurve(HeightRemapCurve.RidgePeak);
            if (btnCurveBasin != null) btnCurveBasin.clicked += () => SetHeightCurve(HeightRemapCurve.BasinInversion);

            // Planetary Surface Material
            toggleFollowPlanet = root.Q<Toggle>("ToggleFollowPlanet");
            if (toggleFollowPlanet != null)
            {
                toggleFollowPlanet.value = followPlanetProfile;
                toggleFollowPlanet.RegisterValueChangedCallback(evt =>
                {
                    followPlanetProfile = evt.newValue;
                    if (followPlanetProfile) SyncMaterialWithCurrentPlanet();
                });
            }

            btnMatMartian = root.Q<Button>("BtnMatMartian");
            btnMatLunar = root.Q<Button>("BtnMatLunar");
            btnMatBasalt = root.Q<Button>("BtnMatBasalt");
            btnMatIce = root.Q<Button>("BtnMatIce");
            btnMatCanyon = root.Q<Button>("BtnMatCanyon");
            btnMatWireframe = root.Q<Button>("BtnMatWireframe");
            btnMatNormal = root.Q<Button>("BtnMatNormal");

            if (btnMatMartian != null) btnMatMartian.clicked += () => SetMaterial(PlanetaryMaterialType.MartianDust);
            if (btnMatLunar != null) btnMatLunar.clicked += () => SetMaterial(PlanetaryMaterialType.LunarRegolith);
            if (btnMatBasalt != null) btnMatBasalt.clicked += () => SetMaterial(PlanetaryMaterialType.VolcanicBasalt);
            if (btnMatIce != null) btnMatIce.clicked += () => SetMaterial(PlanetaryMaterialType.PolarIce);
            if (btnMatCanyon != null) btnMatCanyon.clicked += () => SetMaterial(PlanetaryMaterialType.RedCanyon);
            if (btnMatWireframe != null) btnMatWireframe.clicked += () => SetMaterial(PlanetaryMaterialType.TopographicWireframe);
            if (btnMatNormal != null) btnMatNormal.clicked += () => SetMaterial(PlanetaryMaterialType.NormalInspector);

            // Apply & Live Preview
            toggleLivePreview = root.Q<Toggle>("ToggleLivePreview");
            if (toggleLivePreview != null)
            {
                toggleLivePreview.value = isLivePreviewEnabled;
                toggleLivePreview.RegisterValueChangedCallback(evt => isLivePreviewEnabled = evt.newValue);
            }

            btnApplyTerrainLab = root.Q<Button>("BtnApplyTerrainLab");
            if (btnApplyTerrainLab != null) btnApplyTerrainLab.clicked += OnApplyTerrainLabClicked;

            // Bind Guided Simulation Flow UI elements
            BindGuidedFlowElements(root);
        }

        // -------------------------------------------------------------
        // Guided Simulation Flow Implementation
        // -------------------------------------------------------------
        private void BindGuidedFlowElements(VisualElement root)
        {
            guidedFlowContainer = root.Q<VisualElement>("GuidedFlowContainer");
            panelEnvironmentSetup = root.Q<VisualElement>("PanelEnvironmentSetup");
            panelTerrainReady = root.Q<VisualElement>("PanelTerrainReady");
            panelRoverSelection = root.Q<VisualElement>("PanelRoverSelection");
            panelSimulationReady = root.Q<VisualElement>("PanelSimulationReady");

            // Flow Planet Selection (State 1)
            btnPlanetMars = root.Q<Button>("BtnPlanetMars");
            btnPlanetEarth = root.Q<Button>("BtnPlanetEarth");
            btnPlanetMoon = root.Q<Button>("BtnPlanetMoon");
            btnPlanetTitan = root.Q<Button>("BtnPlanetTitan");
            btnPlanetVenus = root.Q<Button>("BtnPlanetVenus");

            if (btnPlanetMars != null) btnPlanetMars.clicked += () => SelectFlowPlanet("Mars");
            if (btnPlanetEarth != null) btnPlanetEarth.clicked += () => SelectFlowPlanet("Earth");
            if (btnPlanetMoon != null) btnPlanetMoon.clicked += () => SelectFlowPlanet("Moon");
            if (btnPlanetTitan != null) btnPlanetTitan.clicked += () => SelectFlowPlanet("Titan");
            if (btnPlanetVenus != null) btnPlanetVenus.clicked += () => SelectFlowPlanet("Venus");

            // Flow Terrain Source
            btnSourcePreset = root.Q<Button>("BtnSourcePreset");
            btnSourceUpload = root.Q<Button>("BtnSourceUpload");
            btnSourcePainter = root.Q<Button>("BtnSourcePainter");
            viewSourcePreset = root.Q<VisualElement>("ViewSourcePreset");
            viewSourceUpload = root.Q<VisualElement>("ViewSourceUpload");
            viewSourcePainter = root.Q<VisualElement>("ViewSourcePainter");

            if (btnSourcePreset != null) btnSourcePreset.clicked += () => SelectFlowTerrainSource("Preset");
            if (btnSourceUpload != null) btnSourceUpload.clicked += () => SelectFlowTerrainSource("Upload");
            if (btnSourcePainter != null) btnSourcePainter.clicked += () => SelectFlowTerrainSource("Painter");

            // Flow Presets
            btnFlowPresetGale = root.Q<Button>("BtnFlowPresetGale");
            btnFlowPresetOlympus = root.Q<Button>("BtnFlowPresetOlympus");
            btnFlowPresetValles = root.Q<Button>("BtnFlowPresetValles");
            btnFlowPresetShackleton = root.Q<Button>("BtnFlowPresetShackleton");
            btnFlowPresetPlains = root.Q<Button>("BtnFlowPresetPlains");
            btnFlowPresetFractal = root.Q<Button>("BtnFlowPresetFractal");

            if (btnFlowPresetGale != null) btnFlowPresetGale.clicked += () => SelectPreset(HeightmapPreset.GaleCrater);
            if (btnFlowPresetOlympus != null) btnFlowPresetOlympus.clicked += () => SelectPreset(HeightmapPreset.OlympusMons);
            if (btnFlowPresetValles != null) btnFlowPresetValles.clicked += () => SelectPreset(HeightmapPreset.VallesMarineris);
            if (btnFlowPresetShackleton != null) btnFlowPresetShackleton.clicked += () => SelectPreset(HeightmapPreset.ShackletonCrater);
            if (btnFlowPresetPlains != null) btnFlowPresetPlains.clicked += () => SelectPreset(HeightmapPreset.Plains);
            if (btnFlowPresetFractal != null) btnFlowPresetFractal.clicked += () => SelectPreset(HeightmapPreset.ProceduralFractal);

            // Flow Upload
            btnFlowUploadImg = root.Q<Button>("BtnFlowUploadImg");
            lblFlowUploadStatus = root.Q<Label>("LblFlowUploadStatus");
            if (btnFlowUploadImg != null) btnFlowUploadImg.clicked += OnImportPNGClicked;

            // Flow Materials
            btnFlowMatMartian = root.Q<Button>("BtnFlowMatMartian");
            btnFlowMatLunar = root.Q<Button>("BtnFlowMatLunar");
            btnFlowMatBasalt = root.Q<Button>("BtnFlowMatBasalt");
            btnFlowMatIce = root.Q<Button>("BtnFlowMatIce");
            btnFlowMatCanyon = root.Q<Button>("BtnFlowMatCanyon");
            btnFlowMatWireframe = root.Q<Button>("BtnFlowMatWireframe");

            if (btnFlowMatMartian != null) btnFlowMatMartian.clicked += () => SetMaterial(PlanetaryMaterialType.MartianDust);
            if (btnFlowMatLunar != null) btnFlowMatLunar.clicked += () => SetMaterial(PlanetaryMaterialType.LunarRegolith);
            if (btnFlowMatBasalt != null) btnFlowMatBasalt.clicked += () => SetMaterial(PlanetaryMaterialType.VolcanicBasalt);
            if (btnFlowMatIce != null) btnFlowMatIce.clicked += () => SetMaterial(PlanetaryMaterialType.PolarIce);
            if (btnFlowMatCanyon != null) btnFlowMatCanyon.clicked += () => SetMaterial(PlanetaryMaterialType.RedCanyon);
            if (btnFlowMatWireframe != null) btnFlowMatWireframe.clicked += () => SetMaterial(PlanetaryMaterialType.TopographicWireframe);

            // Flow Advanced Settings
            btnToggleAdvancedSettings = root.Q<Button>("BtnToggleAdvancedSettings");
            lblAdvancedToggleText = root.Q<Label>("LblAdvancedToggleText");
            viewAdvancedSettings = root.Q<VisualElement>("ViewAdvancedSettings");
            if (btnToggleAdvancedSettings != null) btnToggleAdvancedSettings.clicked += ToggleAdvancedSettings;

            sliderFlowMaxHeight = root.Q<Slider>("SliderFlowMaxHeight");
            labelFlowMaxHeightVal = root.Q<Label>("LabelFlowMaxHeightVal");
            if (sliderFlowMaxHeight != null)
            {
                sliderFlowMaxHeight.value = tuningConfig.maxHeight;
                sliderFlowMaxHeight.RegisterValueChangedCallback(evt =>
                {
                    tuningConfig.maxHeight = evt.newValue;
                    if (labelFlowMaxHeightVal != null) labelFlowMaxHeightVal.text = $"{evt.newValue:F0} m";
                    if (sliderMaxHeight != null && sliderMaxHeight.value != evt.newValue) sliderMaxHeight.SetValueWithoutNotify(evt.newValue);
                });
            }

            sliderFlowBaseOffset = root.Q<Slider>("SliderFlowBaseOffset");
            labelFlowBaseOffsetVal = root.Q<Label>("LabelFlowBaseOffsetVal");
            if (sliderFlowBaseOffset != null)
            {
                sliderFlowBaseOffset.value = tuningConfig.baseOffset;
                sliderFlowBaseOffset.RegisterValueChangedCallback(evt =>
                {
                    tuningConfig.baseOffset = evt.newValue;
                    if (labelFlowBaseOffsetVal != null) labelFlowBaseOffsetVal.text = $"{evt.newValue:F0} m";
                    if (sliderBaseOffset != null && sliderBaseOffset.value != evt.newValue) sliderBaseOffset.SetValueWithoutNotify(evt.newValue);
                });
            }

            sliderFlowTerrainSize = root.Q<Slider>("SliderFlowTerrainSize");
            labelFlowTerrainSizeVal = root.Q<Label>("LabelFlowTerrainSizeVal");
            if (sliderFlowTerrainSize != null)
            {
                sliderFlowTerrainSize.value = tuningConfig.terrainWidth;
                sliderFlowTerrainSize.RegisterValueChangedCallback(evt =>
                {
                    tuningConfig.terrainWidth = evt.newValue;
                    tuningConfig.terrainLength = evt.newValue;
                    if (labelFlowTerrainSizeVal != null) labelFlowTerrainSizeVal.text = $"{evt.newValue:F0}m";
                    if (sliderTerrainSize != null && sliderTerrainSize.value != evt.newValue) sliderTerrainSize.SetValueWithoutNotify(evt.newValue);
                });
            }

            dropdownFlowResolution = root.Q<DropdownField>("DropdownFlowResolution");
            if (dropdownFlowResolution != null)
            {
                dropdownFlowResolution.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue.Contains("257")) tuningConfig.resolution = 257;
                    else if (evt.newValue.Contains("1025")) tuningConfig.resolution = 1025;
                    else if (evt.newValue.Contains("2049")) tuningConfig.resolution = 2049;
                    else tuningConfig.resolution = 513;

                    if (dropdownResolution != null && dropdownResolution.value != evt.newValue)
                        dropdownResolution.SetValueWithoutNotify(evt.newValue);
                });
            }

            sliderFlowSmoothing = root.Q<Slider>("SliderFlowSmoothing");
            labelFlowSmoothingVal = root.Q<Label>("LabelFlowSmoothingVal");
            if (sliderFlowSmoothing != null)
            {
                sliderFlowSmoothing.value = tuningConfig.smoothingFactor;
                sliderFlowSmoothing.RegisterValueChangedCallback(evt =>
                {
                    tuningConfig.smoothingFactor = evt.newValue;
                    if (labelFlowSmoothingVal != null) labelFlowSmoothingVal.text = $"{evt.newValue:F1}";
                    if (sliderSmoothing != null && sliderSmoothing.value != evt.newValue) sliderSmoothing.SetValueWithoutNotify(evt.newValue);
                });
            }

            fieldFlowSeed = root.Q<IntegerField>("FieldFlowSeed");
            if (fieldFlowSeed != null)
            {
                fieldFlowSeed.value = currentSeed;
                fieldFlowSeed.RegisterValueChangedCallback(evt =>
                {
                    currentSeed = evt.newValue;
                    if (fieldSeed != null && fieldSeed.value != evt.newValue) fieldSeed.SetValueWithoutNotify(evt.newValue);
                });
            }

            btnFlowRandomSeed = root.Q<Button>("BtnFlowRandomSeed");
            if (btnFlowRandomSeed != null)
            {
                btnFlowRandomSeed.clicked += () =>
                {
                    currentSeed = UnityEngine.Random.Range(0, 999999);
                    if (fieldFlowSeed != null) fieldFlowSeed.value = currentSeed;
                    if (fieldSeed != null) fieldSeed.value = currentSeed;
                };
            }

            btnFlowGenerateTerrain = root.Q<Button>("BtnFlowGenerateTerrain");
            if (btnFlowGenerateTerrain != null) btnFlowGenerateTerrain.clicked += OnFlowGenerateTerrainClicked;

            // Flow Terrain Ready
            lblReadyPlanet = root.Q<Label>("LblReadyPlanet");
            lblReadyTerrain = root.Q<Label>("LblReadyTerrain");
            lblReadyMaterial = root.Q<Label>("LblReadyMaterial");
            lblReadySize = root.Q<Label>("LblReadySize");
            lblReadyRes = root.Q<Label>("LblReadyRes");
            lblReadyGravity = root.Q<Label>("LblReadyGravity");

            btnReadyChangeTerrain = root.Q<Button>("BtnReadyChangeTerrain");
            if (btnReadyChangeTerrain != null)
            {
                btnReadyChangeTerrain.clicked += () =>
                {
                    if (flowController != null) flowController.StartEnvironmentSetup();
                };
            }

            btnReadyContinue = root.Q<Button>("BtnReadyContinue");
            if (btnReadyContinue != null)
            {
                btnReadyContinue.clicked += () =>
                {
                    if (flowController != null) flowController.ContinueToRoverSelection();
                };
            }

            // Flow Rover Selection
            btnFlowRoverHusky = root.Q<Button>("BtnFlowRoverHusky");
            btnFlowRoverM20 = root.Q<Button>("BtnFlowRoverM20");
            btnFlowRoverM2020 = root.Q<Button>("BtnFlowRoverM2020");
            btnFlowRoverUrdf = root.Q<Button>("BtnFlowRoverUrdf");

            if (btnFlowRoverHusky != null) btnFlowRoverHusky.clicked += () => SelectFlowRover("husky");
            if (btnFlowRoverM20 != null) btnFlowRoverM20.clicked += () => SelectFlowRover("m20");
            if (btnFlowRoverM2020 != null) btnFlowRoverM2020.clicked += () => SelectFlowRover("m2020");
            if (btnFlowRoverUrdf != null) btnFlowRoverUrdf.clicked += () =>
            {
                ShowNotification("Runtime URDF Import Kit: Select .urdf file via AutonomousRobotKit floating tool.");
            };

            btnFlowClickToSpawn = root.Q<Button>("BtnFlowClickToSpawn");
            if (btnFlowClickToSpawn != null)
            {
                btnFlowClickToSpawn.clicked += () =>
                {
                    if (flowController != null)
                    {
                        flowController.StartRoverPlacement(flowSelectedRoverId);
                    }
                    else if (RoverPlacementController.Instance != null)
                    {
                        RoverPlacementController.Instance.StartPlacement(flowSelectedRoverId);
                    }
                };
            }

            btnFlowCenterSpawn = root.Q<Button>("BtnFlowCenterSpawn");
            if (btnFlowCenterSpawn != null)
            {
                btnFlowCenterSpawn.clicked += () =>
                {
                    if (flowController != null)
                    {
                        flowController.SpawnRoverAtCenter(flowSelectedRoverId);
                    }
                    else if (RoverPlacementController.Instance != null)
                    {
                        RoverPlacementController.Instance.QuickSpawnTerrainCentre();
                    }
                };
            }

            btnFlowBackToTerrain = root.Q<Button>("BtnFlowBackToTerrain");
            if (btnFlowBackToTerrain != null)
            {
                btnFlowBackToTerrain.clicked += () =>
                {
                    if (flowController != null) flowController.SetSimulationState(ProjectName.Core.SimulationState.TerrainReady);
                };
            }

            // Confirmation Modal
            modalConfirmation = root.Q<VisualElement>("ConfirmationModal");
            lblModalTitle = root.Q<Label>("LblModalTitle");
            lblModalMessage = root.Q<Label>("LblModalMessage");
            btnModalCancel = root.Q<Button>("BtnModalCancel");
            btnModalConfirm = root.Q<Button>("BtnModalConfirm");

            if (btnModalCancel != null) btnModalCancel.clicked += HideConfirmationModal;
            if (btnModalConfirm != null)
            {
                btnModalConfirm.clicked += () =>
                {
                    HideConfirmationModal();
                    pendingModalConfirmAction?.Invoke();
                    pendingModalConfirmAction = null;
                };
            }

            // Modern UI Toolkit Planet Selection Modal
            planetSelectionModal = root.Q<VisualElement>("PlanetSelectionModal");
            btnPlanetModalClose = root.Q<Button>("BtnPlanetModalClose");
            btnModalPlanetMars = root.Q<Button>("BtnModalPlanetMars");
            btnModalPlanetEarth = root.Q<Button>("BtnModalPlanetEarth");
            btnModalPlanetMoon = root.Q<Button>("BtnModalPlanetMoon");
            btnModalPlanetTitan = root.Q<Button>("BtnModalPlanetTitan");
            btnModalPlanetVenus = root.Q<Button>("BtnModalPlanetVenus");

            lblModalInspectorName = root.Q<Label>("LblModalInspectorName");
            lblModalInspectorBadge = root.Q<Label>("LblModalInspectorBadge");
            lblModalInspectorDesc = root.Q<Label>("LblModalInspectorDesc");
            lblModalGravity = root.Q<Label>("LblModalGravity");
            lblModalTemp = root.Q<Label>("LblModalTemp");
            lblModalPress = root.Q<Label>("LblModalPress");
            lblModalSkybox = root.Q<Label>("LblModalSkybox");
            lblModalFriction = root.Q<Label>("LblModalFriction");
            lblModalHazard = root.Q<Label>("LblModalHazard");

            btnModalCancelPlanet = root.Q<Button>("BtnModalCancelPlanet");
            btnModalApplyPlanet = root.Q<Button>("BtnModalApplyPlanet");
            btnModalApplyAndRespawn = root.Q<Button>("BtnModalApplyAndRespawn");

            if (btnPlanetModalClose != null) btnPlanetModalClose.clicked += () => SetPlanetModalVisible(false);
            if (btnModalCancelPlanet != null) btnModalCancelPlanet.clicked += () => SetPlanetModalVisible(false);

            if (btnModalPlanetMars != null) btnModalPlanetMars.clicked += () => SelectModalPlanet("Mars");
            if (btnModalPlanetEarth != null) btnModalPlanetEarth.clicked += () => SelectModalPlanet("Earth");
            if (btnModalPlanetMoon != null) btnModalPlanetMoon.clicked += () => SelectModalPlanet("Moon");
            if (btnModalPlanetTitan != null) btnModalPlanetTitan.clicked += () => SelectModalPlanet("Titan");
            if (btnModalPlanetVenus != null) btnModalPlanetVenus.clicked += () => SelectModalPlanet("Venus");

            if (btnModalApplyPlanet != null) btnModalApplyPlanet.clicked += () => ApplyModalPlanet(false);
            if (btnModalApplyAndRespawn != null) btnModalApplyAndRespawn.clicked += () => ApplyModalPlanet(true);
        }

        private void HandleSimulationStateChanged(ProjectName.Core.SimulationState state)
        {
            UpdateGuidedFlowUI(state);
        }

        private void UpdateGuidedFlowUI(ProjectName.Core.SimulationState state)
        {
            bool inFlow = state != ProjectName.Core.SimulationState.ActiveSimulation;
            if (isPlanetModalOpen) SetPlanetModalVisible(false);

            // 1. Guided Flow Container: Visible only during setup states (not placement or active driving)
            if (guidedFlowContainer != null)
                guidedFlowContainer.style.display = (inFlow && state != ProjectName.Core.SimulationState.RoverSpawning) ? DisplayStyle.Flex : DisplayStyle.None;

            // 2. State 1: Environment Setup
            if (panelEnvironmentSetup != null)
                panelEnvironmentSetup.style.display = (state == ProjectName.Core.SimulationState.EnvironmentSetup || state == ProjectName.Core.SimulationState.TerrainGenerating) ? DisplayStyle.Flex : DisplayStyle.None;

            // 3. State 2: Terrain Ready
            if (panelTerrainReady != null)
            {
                panelTerrainReady.style.display = (state == ProjectName.Core.SimulationState.TerrainReady) ? DisplayStyle.Flex : DisplayStyle.None;
                if (state == ProjectName.Core.SimulationState.TerrainReady)
                {
                    if (lblReadyPlanet != null) lblReadyPlanet.text = ProjectName.Core.SimulationContext.SelectedPlanet.ToUpperInvariant();
                    if (lblReadyTerrain != null) lblReadyTerrain.text = currentPreset.ToString().ToUpperInvariant();
                    if (lblReadyMaterial != null) lblReadyMaterial.text = tuningConfig.materialType.ToString().ToUpperInvariant();
                    if (lblReadySize != null) lblReadySize.text = $"{tuningConfig.terrainWidth:F0} × {tuningConfig.terrainLength:F0} m";
                    if (lblReadyRes != null) lblReadyRes.text = $"{tuningConfig.resolution} × {tuningConfig.resolution}";
                    if (lblReadyGravity != null) lblReadyGravity.text = $"{ProjectName.Core.SimulationContext.SelectedGravity:F2} m/s²";
                }
            }

            // 4. State 3: Rover Selection
            if (panelRoverSelection != null)
                panelRoverSelection.style.display = (state == ProjectName.Core.SimulationState.RoverSelection) ? DisplayStyle.Flex : DisplayStyle.None;

            // 5. State 4 (Placement): Placement Banner takes over, all other panels hidden
            if (placementBanner != null)
                placementBanner.style.display = (state == ProjectName.Core.SimulationState.RoverSpawning) ? DisplayStyle.Flex : DisplayStyle.None;

            // 6. Deprecated confirmation modal permanently hidden
            if (panelSimulationReady != null)
                panelSimulationReady.style.display = DisplayStyle.None;

            // 7. Legacy 7-step banner permanently hidden (Eliminate UI stacking)
            if (workflowStepBanner != null)
                workflowStepBanner.style.display = DisplayStyle.None;

            // 8. Side columns & Bottom bars: Strictly hidden during setup, visible only during active driving / studio
            if (studioMainGrid != null)
            {
                studioMainGrid.style.display = inFlow ? DisplayStyle.None : DisplayStyle.Flex;
            }
            if (uiDocument != null && uiDocument.rootVisualElement != null)
            {
                var shellBottomLeft = uiDocument.rootVisualElement.Q<VisualElement>("ShellBottomLeft");
                if (shellBottomLeft != null) shellBottomLeft.style.display = inFlow ? DisplayStyle.None : DisplayStyle.Flex;

                var shellBottomRight = uiDocument.rootVisualElement.Q<VisualElement>("ShellBottomRight");
                if (shellBottomRight != null) shellBottomRight.style.display = inFlow ? DisplayStyle.None : DisplayStyle.Flex;

                // Header controls: Hide mode switches and reset actions during setup, only keep Main Menu
                var modeToggleBar = uiDocument.rootVisualElement.Q<VisualElement>("ModeToggleBar");
                if (modeToggleBar != null) modeToggleBar.style.display = inFlow ? DisplayStyle.None : DisplayStyle.Flex;

                if (btnResetRoverKeepTerrain != null) btnResetRoverKeepTerrain.style.display = inFlow ? DisplayStyle.None : DisplayStyle.Flex;
                if (btnNewTerrain != null) btnNewTerrain.style.display = inFlow ? DisplayStyle.None : DisplayStyle.Flex;
                if (btnMinimizeStudio != null) btnMinimizeStudio.style.display = inFlow ? DisplayStyle.None : DisplayStyle.Flex;
            }

            if (state == ProjectName.Core.SimulationState.ActiveSimulation)
            {
                SetDrivingHudMode(true); // Driving HUD as primary simulation view
            }
        }

        private void SelectFlowPlanet(string planetName)
        {
            ProjectName.Core.SimulationContext.SelectedPlanet = planetName;

            SetBtnClass(btnPlanetMars, "planet-card-active", planetName.Equals("Mars", StringComparison.OrdinalIgnoreCase));
            SetBtnClass(btnPlanetEarth, "planet-card-active", planetName.Equals("Earth", StringComparison.OrdinalIgnoreCase));
            SetBtnClass(btnPlanetMoon, "planet-card-active", planetName.Equals("Moon", StringComparison.OrdinalIgnoreCase));
            SetBtnClass(btnPlanetTitan, "planet-card-active", planetName.Equals("Titan", StringComparison.OrdinalIgnoreCase));
            SetBtnClass(btnPlanetVenus, "planet-card-active", planetName.Equals("Venus", StringComparison.OrdinalIgnoreCase));

            if (btnPlanetBadge != null)
            {
                var lbl = btnPlanetBadge.Q<Label>("LblPlanetName");
                if (lbl != null) lbl.text = planetName.ToUpperInvariant();
                else btnPlanetBadge.text = planetName.ToUpperInvariant();
            }

            // Load matching planet profile
            PlanetProfile profile = Resources.Load<PlanetProfile>($"Planets/{planetName}") ??
                                    Resources.Load<PlanetProfile>(planetName);
            if (profile == null)
            {
                var all = Resources.LoadAll<PlanetaryProfile>("Planets");
                foreach (var p in all)
                {
                    if (p.planetName.Equals(planetName, StringComparison.OrdinalIgnoreCase))
                    {
                        profile = p;
                        break;
                    }
                }
            }

            if (profile != null && PlanetEnvironmentController.Instance != null)
            {
                PlanetEnvironmentController.Instance.ApplyProfile(profile);
            }

            // Sync default material & gravity
            if (planetName.Equals("Mars", StringComparison.OrdinalIgnoreCase))
            {
                SetMaterial(PlanetaryMaterialType.MartianDust);
                ProjectName.Core.SimulationContext.SelectedGravity = 3.72f;
            }
            else if (planetName.Equals("Earth", StringComparison.OrdinalIgnoreCase))
            {
                SetMaterial(PlanetaryMaterialType.PolarIce);
                ProjectName.Core.SimulationContext.SelectedGravity = 9.81f;
            }
            else if (planetName.Equals("Moon", StringComparison.OrdinalIgnoreCase))
            {
                SetMaterial(PlanetaryMaterialType.LunarRegolith);
                ProjectName.Core.SimulationContext.SelectedGravity = 1.62f;
            }
            else if (planetName.Equals("Titan", StringComparison.OrdinalIgnoreCase))
            {
                SetMaterial(PlanetaryMaterialType.VolcanicBasalt);
                ProjectName.Core.SimulationContext.SelectedGravity = 1.35f;
            }
            else if (planetName.Equals("Venus", StringComparison.OrdinalIgnoreCase))
            {
                SetMaterial(PlanetaryMaterialType.VolcanicBasalt);
                ProjectName.Core.SimulationContext.SelectedGravity = 8.87f;
            }
        }

        public void TogglePlanetModal()
        {
            SetPlanetModalVisible(!isPlanetModalOpen);
        }

        public void SetPlanetModalVisible(bool visible)
        {
            isPlanetModalOpen = visible;
            if (planetSelectionModal != null)
            {
                planetSelectionModal.style.display = isPlanetModalOpen ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (isPlanetModalOpen)
            {
                string activePlanet = ProjectName.Core.SimulationContext.SelectedPlanet;
                if (string.IsNullOrEmpty(activePlanet)) activePlanet = "Mars";
                SelectModalPlanet(activePlanet);
            }
        }

        private void SelectModalPlanet(string planetName)
        {
            pendingModalPlanet = planetName;

            SetBtnClass(btnModalPlanetMars, "planet-select-card-active", planetName.Equals("Mars", StringComparison.OrdinalIgnoreCase));
            SetBtnClass(btnModalPlanetEarth, "planet-select-card-active", planetName.Equals("Earth", StringComparison.OrdinalIgnoreCase));
            SetBtnClass(btnModalPlanetMoon, "planet-select-card-active", planetName.Equals("Moon", StringComparison.OrdinalIgnoreCase));
            SetBtnClass(btnModalPlanetTitan, "planet-select-card-active", planetName.Equals("Titan", StringComparison.OrdinalIgnoreCase));
            SetBtnClass(btnModalPlanetVenus, "planet-select-card-active", planetName.Equals("Venus", StringComparison.OrdinalIgnoreCase));

            UpdateModalInspector(planetName);
        }

        private void UpdateModalInspector(string planetName)
        {
            string currentActive = ProjectName.Core.SimulationContext.SelectedPlanet;
            bool isCurrent = planetName.Equals(currentActive, StringComparison.OrdinalIgnoreCase);

            if (lblModalInspectorName != null) lblModalInspectorName.text = planetName.ToUpperInvariant();
            if (lblModalInspectorBadge != null)
            {
                lblModalInspectorBadge.text = isCurrent ? "CURRENT ACTIVE" : "PENDING SELECTION";
                lblModalInspectorBadge.style.display = DisplayStyle.Flex;
            }

            if (planetName.Equals("Mars", StringComparison.OrdinalIgnoreCase))
            {
                if (lblModalInspectorDesc != null) lblModalInspectorDesc.text = "The Red Planet: Low gravity, thin CO2 atmosphere, red iron oxide dust, cold desert conditions with high radiation.";
                if (lblModalGravity != null) lblModalGravity.text = "3.72 m/s² (0.38g)";
                if (lblModalTemp != null) lblModalTemp.text = "≈ −60 °C";
                if (lblModalPress != null) lblModalPress.text = "0.64 kPa (0.006 atm)";
                if (lblModalSkybox != null) { lblModalSkybox.text = "Deep Space Nebulae HDR"; lblModalSkybox.style.color = new Color(0.63f, 0.50f, 1.0f); }
                if (lblModalFriction != null) lblModalFriction.text = "μ = 0.70 / 0.55";
                if (lblModalHazard != null) lblModalHazard.text = "Dust Storm / Arid";
            }
            else if (planetName.Equals("Earth", StringComparison.OrdinalIgnoreCase))
            {
                if (lblModalInspectorDesc != null) lblModalInspectorDesc.text = "Earth: Standard terrestrial gravity, 1 atm nitrogen-oxygen atmosphere, blue Rayleigh sky, temperate baseline climate.";
                if (lblModalGravity != null) lblModalGravity.text = "9.81 m/s² (1.00g)";
                if (lblModalTemp != null) lblModalTemp.text = "≈ +15 °C";
                if (lblModalPress != null) lblModalPress.text = "101.3 kPa (1.00 atm)";
                if (lblModalSkybox != null) { lblModalSkybox.text = "Terrestrial Rayleigh Sky"; lblModalSkybox.style.color = new Color(0.30f, 0.93f, 0.92f); }
                if (lblModalFriction != null) lblModalFriction.text = "μ = 0.80 / 0.60";
                if (lblModalHazard != null) lblModalHazard.text = "Weather / Clear";
            }
            else if (planetName.Equals("Moon", StringComparison.OrdinalIgnoreCase))
            {
                if (lblModalInspectorDesc != null) lblModalInspectorDesc.text = "Earth's Moon: Hard vacuum, 1/6th Earth gravity, highly abrasive lunar regolith, high-contrast pitch black shadows.";
                if (lblModalGravity != null) lblModalGravity.text = "1.62 m/s² (0.17g)";
                if (lblModalTemp != null) lblModalTemp.text = "−170 °C / +100 °C";
                if (lblModalPress != null) lblModalPress.text = "0.00 kPa (Hard Vacuum)";
                if (lblModalSkybox != null) { lblModalSkybox.text = "Deep Space Nebulae HDR"; lblModalSkybox.style.color = new Color(0.63f, 0.50f, 1.0f); }
                if (lblModalFriction != null) lblModalFriction.text = "μ = 0.90 / 0.75";
                if (lblModalHazard != null) lblModalHazard.text = "Vacuum / Micro-meteoroids";
            }
            else if (planetName.Equals("Titan", StringComparison.OrdinalIgnoreCase))
            {
                if (lblModalInspectorDesc != null) lblModalInspectorDesc.text = "Titan: Saturn's largest moon with dense nitrogen atmosphere, hydrocarbon lakes, methane cycle, cryogenic temperatures.";
                if (lblModalGravity != null) lblModalGravity.text = "1.35 m/s² (0.14g)";
                if (lblModalTemp != null) lblModalTemp.text = "≈ −179 °C";
                if (lblModalPress != null) lblModalPress.text = "146.7 kPa (1.45 atm)";
                if (lblModalSkybox != null) { lblModalSkybox.text = "Deep Space Nebulae HDR"; lblModalSkybox.style.color = new Color(0.63f, 0.50f, 1.0f); }
                if (lblModalFriction != null) lblModalFriction.text = "μ = 0.55 / 0.40";
                if (lblModalHazard != null) lblModalHazard.text = "Methane Haze & Drizzle";
            }
            else if (planetName.Equals("Venus", StringComparison.OrdinalIgnoreCase))
            {
                if (lblModalInspectorDesc != null) lblModalInspectorDesc.text = "Venus: Runaway greenhouse world, crushing 92 bar CO2 atmosphere, lead-melting surface heat, sulfuric acid smog.";
                if (lblModalGravity != null) lblModalGravity.text = "8.87 m/s² (0.90g)";
                if (lblModalTemp != null) lblModalTemp.text = "≈ +465 °C";
                if (lblModalPress != null) lblModalPress.text = "9,200 kPa (90.8 atm)";
                if (lblModalSkybox != null) { lblModalSkybox.text = "Deep Space Nebulae HDR"; lblModalSkybox.style.color = new Color(0.63f, 0.50f, 1.0f); }
                if (lblModalFriction != null) lblModalFriction.text = "μ = 0.75 / 0.60";
                if (lblModalHazard != null) lblModalHazard.text = "Corrosive Acid Smog (92 bar)";
            }
        }

        private void ApplyModalPlanet(bool respawnRover)
        {
            string planetName = pendingModalPlanet;
            SelectFlowPlanet(planetName);

            // Also retrieve profile to apply
            PlanetProfile profile = Resources.Load<PlanetProfile>($"Planets/{planetName}") ??
                                    Resources.Load<PlanetProfile>(planetName);
            if (profile == null)
            {
                var all = Resources.LoadAll<PlanetaryProfile>("Planets");
                foreach (var p in all)
                {
                    if (p.planetName.Equals(planetName, StringComparison.OrdinalIgnoreCase))
                    {
                        profile = p;
                        break;
                    }
                }
            }

            if (profile != null && PlanetEnvironmentController.Instance != null)
            {
                if (respawnRover)
                {
                    PlanetEnvironmentController.Instance.ApplyAndRespawnRover(profile);
                    ShowNotification($"{planetName.ToUpper()} applied · Rover respawned at surface.");
                }
                else
                {
                    PlanetEnvironmentController.Instance.ApplyProfile(profile);
                    ShowNotification($"{planetName.ToUpper()} environment applied.");
                }
            }

            SetPlanetModalVisible(false);
        }

        private void SelectFlowTerrainSource(string source)
        {
            ProjectName.Core.SimulationContext.SelectedTerrainSource = source;
            SetBtnClass(btnSourcePreset, "segmented-active", source == "Preset");
            SetBtnClass(btnSourceUpload, "segmented-active", source == "Upload");
            SetBtnClass(btnSourcePainter, "segmented-active", source == "Painter");

            if (viewSourcePreset != null) viewSourcePreset.style.display = source == "Preset" ? DisplayStyle.Flex : DisplayStyle.None;
            if (viewSourceUpload != null) viewSourceUpload.style.display = source == "Upload" ? DisplayStyle.Flex : DisplayStyle.None;
            if (viewSourcePainter != null) viewSourcePainter.style.display = source == "Painter" ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void ToggleAdvancedSettings()
        {
            isAdvancedSettingsVisible = !isAdvancedSettingsVisible;
            if (viewAdvancedSettings != null)
                viewAdvancedSettings.style.display = isAdvancedSettingsVisible ? DisplayStyle.Flex : DisplayStyle.None;
            if (lblAdvancedToggleText != null)
                lblAdvancedToggleText.text = isAdvancedSettingsVisible ? "ADVANCED TERRAIN SETTINGS  ▲" : "ADVANCED TERRAIN SETTINGS  ▼";
        }

        private void SelectFlowRover(string roverId)
        {
            flowSelectedRoverId = roverId.ToLowerInvariant();
            if (flowController != null) flowController.SelectRover(flowSelectedRoverId);

            SetBtnClass(btnFlowRoverHusky, "rover-row-active", flowSelectedRoverId == "husky");
            SetBtnClass(btnFlowRoverM20, "rover-row-active", flowSelectedRoverId == "m20");
            SetBtnClass(btnFlowRoverM2020, "rover-row-active", flowSelectedRoverId == "m2020");
        }

        private void OnFlowGenerateTerrainClicked()
        {
            if (flowController != null) flowController.OnTerrainGenerationStarted();
            OnGenerateTerrainClicked();
        }

        public void ShowConfirmationModal(string title, string message, Action onConfirm)
        {
            pendingModalConfirmAction = onConfirm;
            if (lblModalTitle != null) lblModalTitle.text = title;
            if (lblModalMessage != null) lblModalMessage.text = message;
            if (modalConfirmation != null) modalConfirmation.style.display = DisplayStyle.Flex;
        }

        public void HideConfirmationModal()
        {
            if (modalConfirmation != null) modalConfirmation.style.display = DisplayStyle.None;
            pendingModalConfirmAction = null;
        }

        private void Update()
        {
            // Input blocking guard: If typing in UI, block driving inputs on active rover
            bool isTypingInUI = uiDocument != null && uiDocument.rootVisualElement != null &&
                                uiDocument.rootVisualElement.focusController != null &&
                                uiDocument.rootVisualElement.focusController.focusedElement != null &&
                                (uiDocument.rootVisualElement.focusController.focusedElement.GetType().Name.Contains("Text") ||
                                 uiDocument.rootVisualElement.focusController.focusedElement.GetType().Name.Contains("Input"));

            // Undo / Redo keyboard shortcuts [Ctrl+Z] / [Ctrl+Y]
            if (!isTypingInUI && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)))
            {
                if (Input.GetKeyDown(KeyCode.Z))
                {
                    UndoLastStroke();
                }
                else if (Input.GetKeyDown(KeyCode.Y))
                {
                    RedoStroke();
                }
            }

            if (ActiveRoverContext.HasActiveRover)
            {
                var handle = ActiveRoverContext.Current;
                var h = (handle.rootGameObject != null ? handle.rootGameObject.GetComponent<RoverController_husky>() : null) ?? FindAnyObjectByType<RoverController_husky>();
                if (h != null) h.isInputBlocked = isTypingInUI;

                var m = (handle.rootGameObject != null ? handle.rootGameObject.GetComponent<RoverController_m20>() : null) ?? FindAnyObjectByType<RoverController_m20>();
                if (m != null) m.isInputBlocked = isTypingInUI;

                var m20 = (handle.rootGameObject != null ? handle.rootGameObject.GetComponent<RoverController_m2020>() : null) ?? FindAnyObjectByType<RoverController_m2020>();
                if (m20 != null) m20.isInputBlocked = isTypingInUI;
            }

            // [Tab] switches between Driving HUD and Full Studio view
            if (Input.GetKeyDown(KeyCode.Tab))
            {
                SetDrivingHudMode(!isDrivingHudMode);
            }

            // [U] or [F1] minimizes completely to floating pill dock
            if (Input.GetKeyDown(KeyCode.U) || Input.GetKeyDown(KeyCode.F1))
            {
                SetStudioMinimized(!isStudioMinimized);
            }

            // [H] toggles complete HUD visibility on/off for cinematic rover/terrain view
            if (Input.GetKeyDown(KeyCode.H) && !isTypingInUI)
            {
                ToggleHudVisibility();
            }

            // [P] toggles modern UI Toolkit Planet Selection Modal
            if (Input.GetKeyDown(KeyCode.P) && !isTypingInUI)
            {
                TogglePlanetModal();
            }

            // [Escape] closes planet modal if open
            if (Input.GetKeyDown(KeyCode.Escape) && isPlanetModalOpen)
            {
                SetPlanetModalVisible(false);
            }

            if (isStudioMinimized || isHudHidden) return;

            // Stream live telemetry at <= 10 Hz into Studio elements (Rule R7: Zero garbage, low overhead)
            uiUpdateTimer += Time.unscaledDeltaTime;
            if (uiUpdateTimer >= UI_UPDATE_INTERVAL)
            {
                uiUpdateTimer = 0f;
                UpdateLiveTelemetryUI();
                UpdatePlanetaryEnvironmentUI();
                UpdateDriveModeUI();
            }
        }

        public void SetDrivingHudMode(bool hudMode)
        {
            isDrivingHudMode = hudMode;
            if (painterBrushCursor != null) painterBrushCursor.style.display = DisplayStyle.None;

            if (studioRoot == null && uiDocument != null && uiDocument.rootVisualElement != null)
            {
                BindUIElements();
            }

            if (colCenter != null)
            {
                colCenter.style.display = hudMode ? DisplayStyle.None : DisplayStyle.Flex;
            }

            if (studioRoot != null)
            {
                if (hudMode)
                {
                    studioRoot.AddToClassList("hud-mode-root");
                    studioRoot.AddToClassList("mode-driving");
                    studioRoot.RemoveFromClassList("mode-studio");
                }
                else
                {
                    studioRoot.RemoveFromClassList("hud-mode-root");
                    studioRoot.RemoveFromClassList("mode-driving");
                    studioRoot.AddToClassList("mode-studio");
                }
            }

            if (btnToggleHUD != null)
            {
                btnToggleHUD.text = hudMode ? "STUDIO [Tab]" : "HUD [Tab]";
            }

            SetBtnClass(btnModeDriving, "view-switch-active", hudMode);
            SetBtnClass(btnModeStudio, "view-switch-active", !hudMode);
            SetBtnClass(btnModeDriving, "segmented-active", hudMode);
            SetBtnClass(btnModeStudio, "segmented-active", !hudMode);
        }

        public void SetStudioMinimized(bool minimized)
        {
            isStudioMinimized = minimized;
            if (studioRoot == null && uiDocument != null && uiDocument.rootVisualElement != null)
            {
                BindUIElements();
            }
            if (studioRoot != null)
            {
                studioRoot.style.display = minimized ? DisplayStyle.None : DisplayStyle.Flex;
            }
            if (btnFloatingDock != null)
            {
                btnFloatingDock.style.display = minimized ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        public void ToggleHudVisibility()
        {
            isHudHidden = !isHudHidden;
            if (studioRoot != null)
            {
                studioRoot.style.display = isHudHidden ? DisplayStyle.None : (isStudioMinimized ? DisplayStyle.None : DisplayStyle.Flex);
            }
            if (btnFloatingDock != null)
            {
                btnFloatingDock.style.display = isHudHidden ? DisplayStyle.None : (isStudioMinimized ? DisplayStyle.Flex : DisplayStyle.None);
            }
            Debug.Log($"[Studio HUD] Cinematic HUD view: {(isHudHidden ? "HIDDEN (Press H to restore)" : "RESTORED")}");
        }

        public bool IsTextInputFocused()
        {
            return uiDocument != null && uiDocument.rootVisualElement != null &&
                   uiDocument.rootVisualElement.focusController != null &&
                   uiDocument.rootVisualElement.focusController.focusedElement != null &&
                   (uiDocument.rootVisualElement.focusController.focusedElement.GetType().Name.Contains("Text") ||
                    uiDocument.rootVisualElement.focusController.focusedElement.GetType().Name.Contains("Input"));
        }

        public bool IsPointerOverUI(Vector2 screenPos)
        {
            if (uiDocument == null || uiDocument.rootVisualElement == null) return false;
            var panel = uiDocument.rootVisualElement.panel;
            if (panel == null) return false;

            Vector2 panelPos = new Vector2(screenPos.x, Screen.height - screenPos.y);
            VisualElement picked = panel.Pick(panelPos);
            if (picked == null) return false;
            if (picked == uiDocument.rootVisualElement) return false;

            if (isPlacementMode)
            {
                if (picked == studioRoot || picked == studioMainGrid) return false;
                if (placementBanner != null && (picked == placementBanner || placementBanner.Contains(picked))) return true;
                if (btnFloatingDock != null && (picked == btnFloatingDock || btnFloatingDock.Contains(picked))) return true;
                return false;
            }

            if (isDrivingHudMode)
            {
                if (picked == studioRoot) return false;
                VisualElement cur = picked;
                while (cur != null && cur != studioRoot && cur != uiDocument.rootVisualElement)
                {
                    if (cur.ClassListContains("hud-interactive")) return true;
                    cur = cur.parent;
                }
                return false;
            }

            return true;
        }

        public void SetPlacementBannerActive(bool active, string roverName)
        {
            isPlacementMode = active;

            if (studioRoot == null && uiDocument != null && uiDocument.rootVisualElement != null)
            {
                BindUIElements();
            }

            if (placementBanner != null)
            {
                placementBanner.style.display = active ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (lblPlacementBannerText != null && active)
            {
                lblPlacementBannerText.text = $"Click terrain to place {roverName} · [Q,E] or Scroll rotates · [Esc] cancels";
            }

            if (studioMainGrid != null)
            {
                studioMainGrid.style.display = active ? DisplayStyle.None : DisplayStyle.Flex;
            }

            if (studioBottomBar != null)
            {
                studioBottomBar.style.display = active ? DisplayStyle.None : DisplayStyle.Flex;
            }

            var header = studioRoot != null ? studioRoot.Q<VisualElement>(className: "studio-header") : null;
            if (header != null)
            {
                header.style.display = active ? DisplayStyle.None : DisplayStyle.Flex;
            }

            var shellTop = studioRoot != null ? studioRoot.Q<VisualElement>("ShellTopRow") : null;
            if (shellTop != null)
            {
                shellTop.style.display = active ? DisplayStyle.None : DisplayStyle.Flex;
            }

            var shellBottomLeft = studioRoot != null ? studioRoot.Q<VisualElement>("ShellBottomLeft") : null;
            if (shellBottomLeft != null)
            {
                shellBottomLeft.style.display = active ? DisplayStyle.None : DisplayStyle.Flex;
            }

            var shellBottomRight = studioRoot != null ? studioRoot.Q<VisualElement>("ShellBottomRight") : null;
            if (shellBottomRight != null)
            {
                shellBottomRight.style.display = active ? DisplayStyle.None : DisplayStyle.Flex;
            }

            if (studioRoot != null)
            {
                if (active)
                {
                    studioRoot.AddToClassList("hud-mode-root");
                    studioRoot.pickingMode = PickingMode.Ignore;
                }
                else if (!isDrivingHudMode)
                {
                    studioRoot.RemoveFromClassList("hud-mode-root");
                    studioRoot.pickingMode = PickingMode.Position;
                }
            }

            if (active)
            {
                SetWorkflowStep(4);
            }
            else
            {
                var curTerrain = terrainGenerator != null ? terrainGenerator.GetCurrentTerrain() : UnityEngine.Terrain.activeTerrain;
                SetWorkflowStep(ActiveRoverContext.HasActiveRover ? 6 : (curTerrain != null ? 3 : 1));
            }
        }

        public void ShowNotification(string message)
        {
            if (lblHelperHint != null)
            {
                lblHelperHint.text = message;
            }
            Debug.Log($"[Studio Notification] {message}");
        }

        private void OnMinimapImageClicked(ClickEvent evt)
        {
            // In Studio mode, minimap is used for painting, not rover relocation
            if (!isDrivingHudMode && (RoverPlacementController.Instance == null || !RoverPlacementController.Instance.IsPlacementActive))
            {
                return;
            }

            if (heightmapPreviewImage == null) return;
            Vector2 localPos = evt.localPosition;
            float w = heightmapPreviewImage.resolvedStyle.width;
            float h = heightmapPreviewImage.resolvedStyle.height;
            if (w <= 0f || h <= 0f) return;

            float normX = Mathf.Clamp01(localPos.x / w);
            float normZ = Mathf.Clamp01(1f - (localPos.y / h));

            var terrain = UnityEngine.Terrain.activeTerrain ?? FindAnyObjectByType<UnityEngine.Terrain>();
            if (terrain == null) return;

            Vector3 tPos = terrain.transform.position;
            Vector3 tSize = terrain.terrainData.size;

            float worldX = tPos.x + normX * tSize.x;
            float worldZ = tPos.z + normZ * tSize.z;
            float worldY = terrain.SampleHeight(new Vector3(worldX, 0f, worldZ)) + tPos.y;
            Vector3 targetPoint = new Vector3(worldX, worldY, worldZ);

            Debug.Log($"[Minimap] Clicked at norm ({normX:F2}, {normZ:F2}) -> World {targetPoint}");

            if (RoverPlacementController.Instance != null)
            {
                if (RoverPlacementController.Instance.IsPlacementActive)
                {
                    RoverPlacementController.Instance.ConfirmPlacementAtPoint(targetPoint);
                }
                else if (ActiveRoverContext.HasActiveRover)
                {
                    RoverPlacementController.Instance.TeleportActiveRover(targetPoint);
                }
                else
                {
                    RoverPlacementController.Instance.ConfirmPlacementAtPoint(targetPoint, 0f, "husky");
                }
            }
        }

        private void HandleRoverActivated(RoverHandle handle)
        {
            if (handle == null) return;
            activeRoverId = handle.roverId;
            UpdateRoverSelectionUI();

            if (badgeStatus != null)
            {
                badgeStatus.text = "● ACTIVE";
                badgeStatus.RemoveFromClassList("pill-standby");
                badgeStatus.AddToClassList("pill-active");
            }

            if (roverEmptyStateBanner != null)
            {
                roverEmptyStateBanner.style.display = DisplayStyle.None;
            }

            if (badgeDriveMode != null && handle.profile != null)
            {
                badgeDriveMode.text = handle.profile.steeringLabel.ToUpper();
            }

            RebuildWheelUI(handle);
            ConfigureDriveBarForRover(handle.profile);
            if (flowController != null && flowController.CurrentSimulationState != ProjectName.Core.SimulationState.ActiveSimulation)
            {
                SetWorkflowStep(5);
            }
            else
            {
                SetWorkflowStep(6);
            }
        }

        private void HandleRoverDestroyed()
        {
            activeRoverId = "";
            UpdateRoverSelectionUI();
            ClearBreadcrumbs();
            SetTelemetryStandbyState();
            ConfigureDriveBarForStandby();
            var curTerrain = terrainGenerator != null ? terrainGenerator.GetCurrentTerrain() : UnityEngine.Terrain.activeTerrain;
            SetWorkflowStep(curTerrain != null ? 3 : 1);
        }

        private void SetTelemetryStandbyState()
        {
            if (badgeStatus != null)
            {
                badgeStatus.text = "● STANDBY";
                badgeStatus.RemoveFromClassList("pill-active");
                badgeStatus.AddToClassList("pill-standby");
            }

            if (roverEmptyStateBanner != null)
            {
                roverEmptyStateBanner.style.display = DisplayStyle.Flex;
            }

            if (badgeDriveMode != null) badgeDriveMode.text = "—";

            if (valLinearSpeed != null) valLinearSpeed.text = "—";
            if (valGroundSpeed != null) valGroundSpeed.text = "—";
            if (valAcceleration != null) valAcceleration.text = "—";
            if (valGForce != null) valGForce.text = "—";

            if (valPitch != null) valPitch.text = "—";
            if (valRoll != null) valRoll.text = "—";
            if (valHeading != null) valHeading.text = "—";
            if (valSlope != null) valSlope.text = "—";
            if (barHeadingLevel != null) barHeadingLevel.style.width = Length.Percent(0);
            if (iconAttitudeWarning != null) iconAttitudeWarning.style.display = DisplayStyle.None;

            if (valCoords != null) valCoords.text = "X —   Y —   Z —";
            if (valOdometer != null) valOdometer.text = "—";
            if (valMissionTime != null) valMissionTime.text = "—";

            if (barBattery != null) barBattery.style.width = Length.Percent(0);
            if (valBattery != null) valBattery.text = "—";
            if (barMotorTemp != null) barMotorTemp.style.width = Length.Percent(0);
            if (valMotorTemp != null) valMotorTemp.text = "—";

            if (radarRoverDot != null) radarRoverDot.style.translate = new Translate(0, 0);

            for (int i = 0; i < activeWheelUIs.Count; i++)
            {
                var item = activeWheelUIs[i];
                if (item.tag != null)
                {
                    item.tag.text = "—";
                    item.tag.RemoveFromClassList("tag-good");
                    item.tag.RemoveFromClassList("tag-fair");
                    item.tag.RemoveFromClassList("tag-slip");
                }
                if (item.segs != null)
                {
                    for (int s = 0; s < item.segs.Length; s++)
                    {
                        item.segs[s].RemoveFromClassList("seg-active");
                        item.segs[s].RemoveFromClassList("seg-fair");
                        item.segs[s].RemoveFromClassList("seg-slip");
                        item.segs[s].AddToClassList("seg-inactive");
                    }
                }
            }
        }

        private void RebuildWheelUI(RoverHandle handle)
        {
            if (wheelContainer == null) return;
            if (handle == null || handle.wheelDefs == null || handle.wheelDefs.Length == 0) return;

            wheelContainer.Clear();
            activeWheelUIs.Clear();

            for (int i = 0; i < handle.wheelDefs.Length; i++)
            {
                var def = handle.wheelDefs[i];
                var cell = new VisualElement();
                cell.AddToClassList("wheel-cell");

                var infoCol = new VisualElement();
                infoCol.AddToClassList("wheel-info-col");

                string abbr = !string.IsNullOrEmpty(def.abbreviation) ? def.abbreviation : def.displayName;
                var nameLbl = new Label(abbr);
                nameLbl.AddToClassList("wheel-name");

                var tagLbl = new Label("GOOD");
                tagLbl.AddToClassList("wheel-status");
                tagLbl.AddToClassList("tag-good");

                infoCol.Add(nameLbl);
                infoCol.Add(tagLbl);
                cell.Add(infoCol);

                var segsRow = new VisualElement();
                segsRow.AddToClassList("wheel-segments-row");

                var segList = new VisualElement[5];
                for (int s = 0; s < 5; s++)
                {
                    var seg = new VisualElement();
                    seg.AddToClassList("wheel-seg");
                    seg.AddToClassList("seg-active");
                    segsRow.Add(seg);
                    segList[s] = seg;
                }

                cell.Add(segsRow);
                wheelContainer.Add(cell);

                activeWheelUIs.Add(new WheelUIItem
                {
                    nameLabel = nameLbl,
                    tag = tagLbl,
                    segsContainer = segsRow,
                    segs = segList
                });
            }

            lastWheelRoverId = handle.roverId;
        }

        private void UpdateLiveTelemetryUI()
        {
            if (!ActiveRoverContext.HasActiveRover)
            {
                SetTelemetryStandbyState();
                return;
            }

            var handle = ActiveRoverContext.Current;
            var t = handle.telemetry;
            if (t == null)
            {
                SetTelemetryStandbyState();
                return;
            }

            if (lastWheelRoverId != handle.roverId || (handle.wheelDefs != null && activeWheelUIs.Count != handle.wheelDefs.Length))
            {
                RebuildWheelUI(handle);
            }

            if (badgeStatus != null) badgeStatus.text = "● ACTIVE";
            if (badgeDriveMode != null && handle.profile != null)
            {
                badgeDriveMode.text = handle.profile.steeringLabel.ToUpper();
            }

            // Kinematics (Measured truthful physics data)
            if (valLinearSpeed != null) valLinearSpeed.text = $"{t.linearSpeedMps:F2}";
            if (valGroundSpeed != null) valGroundSpeed.text = $"{t.groundSpeedKmph:F2}";
            if (valAcceleration != null) valAcceleration.text = $"{t.forwardAcceleration:+0.00;-0.00}";
            if (valGForce != null) valGForce.text = $"{t.gForceLoad:F2}";

            // Attitude
            if (valPitch != null) valPitch.text = $"{t.pitchDeg:+0.0;-0.0}°";
            if (valRoll != null) valRoll.text = $"{t.rollDeg:+0.0;-0.0}°";
            if (valHeading != null) valHeading.text = $"{t.headingDeg:000}° {t.headingCardinal}";
            if (valSlope != null) valSlope.text = $"{t.terrainSlopeDeg:F1}°";

            // Attitude Level Bar & Warning
            if (barHeadingLevel != null)
            {
                float normHeading = Mathf.Repeat(t.headingDeg, 360f) / 360f;
                barHeadingLevel.style.width = Length.Percent(normHeading * 100f);
            }
            if (iconAttitudeWarning != null)
            {
                bool isWarning = t.isRolloverWarning || t.isSteepSlopeWarning;
                iconAttitudeWarning.style.display = isWarning ? DisplayStyle.Flex : DisplayStyle.None;
            }

            // Position & Odometry
            if (valCoords != null) valCoords.text = $"X {t.worldPosition.x:F2} m   Y {t.worldPosition.y:F2} m   Z {t.worldPosition.z:F2} m";
            if (valOdometer != null) valOdometer.text = t.odometerMeters >= 1000f ? $"{t.odometerMeters / 1000f:F2} km" : $"{t.odometerMeters:F1} m";

            if (valMissionTime != null)
            {
                int totSec = Mathf.FloorToInt(t.missionTimeSeconds);
                int hrs = totSec / 3600;
                int mins = (totSec % 3600) / 60;
                int secs = totSec % 60;
                valMissionTime.text = $"{hrs:00}:{mins:00}:{secs:00}";
            }

            // Wheel Actuators Matrix (Dynamic count, discrete 5 segments)
            if (t.wheelStates != null && activeWheelUIs.Count > 0)
            {
                int count = Mathf.Min(t.wheelStates.Length, activeWheelUIs.Count);
                for (int i = 0; i < count; i++)
                {
                    UpdateWheelCell(t.wheelStates[i], activeWheelUIs[i]);
                }
            }

            // Power & Thermal (Truthful lumped model & Wh integration)
            if (barBattery != null) barBattery.style.width = Length.Percent(Mathf.Clamp01(t.batteryPercent / 100f) * 100f);
            if (valBattery != null) valBattery.text = $"{t.batteryPercent:F0}%";

            if (barMotorTemp != null) barMotorTemp.style.width = Length.Percent(Mathf.Clamp01(t.motorTempCelsius / 100f) * 100f);
            if (valMotorTemp != null) valMotorTemp.text = $"{t.motorTempCelsius:F0} °C";

            // Topographic Minimap: Rover Position Marker, Heading Arrow, Breadcrumbs, and Grid Range
            if (radarRoverDot != null)
            {
                var tTerrain = UnityEngine.Terrain.activeTerrain;
                if (tTerrain != null && tTerrain.terrainData != null)
                {
                    Vector3 tPos = tTerrain.transform.position;
                    Vector3 tSize = tTerrain.terrainData.size;
                    float normX = Mathf.Clamp01((t.worldPosition.x - tPos.x) / tSize.x);
                    float normZ = Mathf.Clamp01((t.worldPosition.z - tPos.z) / tSize.z);

                    // Minimap is 160x160 px; offset from center is (norm - 0.5) * 156
                    float mapPxX = (normX - 0.5f) * 156f;
                    float mapPxY = (0.5f - normZ) * 156f;
                    radarRoverDot.style.translate = new Translate(mapPxX, mapPxY);

                    // Heading rotation arrow aligned with rover heading
                    if (radarRoverHeadingArrow != null)
                    {
                        radarRoverHeadingArrow.style.rotate = new Rotate(t.headingDeg);
                    }

                    // Update terrain grid range label
                    if (labelRadarRange != null)
                    {
                        labelRadarRange.text = $"GRID {tSize.x:F0}m × {tSize.z:F0}m";
                    }

                    // Dynamic breadcrumb trail
                    UpdateMinimapBreadcrumbs(t.worldPosition, tPos, tSize);
                }
            }
        }

        private void UpdateMinimapBreadcrumbs(Vector3 roverPos, Vector3 tPos, Vector3 tSize)
        {
            if (minimapBreadcrumbsContainer == null) return;

            bool addPoint = false;
            if (breadcrumbPositions.Count == 0)
            {
                addPoint = true;
            }
            else
            {
                float dist = Vector3.Distance(breadcrumbPositions[breadcrumbPositions.Count - 1], roverPos);
                if (dist >= BREADCRUMB_MIN_DISTANCE)
                {
                    addPoint = true;
                }
            }

            if (addPoint)
            {
                breadcrumbPositions.Add(roverPos);
                if (breadcrumbPositions.Count > MAX_BREADCRUMBS)
                {
                    breadcrumbPositions.RemoveAt(0);
                }

                while (breadcrumbDots.Count < breadcrumbPositions.Count)
                {
                    var dot = new VisualElement();
                    dot.AddToClassList("breadcrumb-dot");
                    minimapBreadcrumbsContainer.Add(dot);
                    breadcrumbDots.Add(dot);
                }
                while (breadcrumbDots.Count > breadcrumbPositions.Count)
                {
                    int last = breadcrumbDots.Count - 1;
                    minimapBreadcrumbsContainer.Remove(breadcrumbDots[last]);
                    breadcrumbDots.RemoveAt(last);
                }

                for (int i = 0; i < breadcrumbPositions.Count; i++)
                {
                    Vector3 bPos = breadcrumbPositions[i];
                    float normX = Mathf.Clamp01((bPos.x - tPos.x) / tSize.x);
                    float normZ = Mathf.Clamp01((bPos.z - tPos.z) / tSize.z);
                    float pxX = (normX - 0.5f) * 156f;
                    float pxY = (0.5f - normZ) * 156f;
                    breadcrumbDots[i].style.translate = new Translate(pxX, pxY);

                    float alpha = 0.2f + (0.8f * ((float)(i + 1) / breadcrumbPositions.Count));
                    breadcrumbDots[i].style.opacity = alpha;
                }
            }
        }

        private void ClearBreadcrumbs()
        {
            breadcrumbPositions.Clear();
            if (minimapBreadcrumbsContainer != null)
            {
                minimapBreadcrumbsContainer.Clear();
            }
            breadcrumbDots.Clear();
        }

        private void UpdateWheelCell(WheelTelemetryState w, WheelUIItem item)
        {
            if (item.tag == null || item.segs == null) return;

            string statusText = string.IsNullOrEmpty(w.status) ? "GOOD" : w.status;
            string statusTagClass = "tag-good";
            int activeSegs = 5;
            string segClass = "seg-active";

            if (!w.isGrounded || statusText == "AIR")
            {
                statusText = "AIR";
                statusTagClass = "tag-fair";
                activeSegs = 1;
                segClass = "seg-fair";
            }
            else if (statusText == "SLIP" || w.slipRatio > 0.35f)
            {
                statusText = "SLIP";
                statusTagClass = "tag-slip";
                activeSegs = 2;
                segClass = "seg-slip";
            }
            else if (statusText == "FAIR" || w.slipRatio > 0.15f)
            {
                statusText = "FAIR";
                statusTagClass = "tag-fair";
                activeSegs = 4;
                segClass = "seg-fair";
            }

            item.tag.text = statusText;
            item.tag.RemoveFromClassList("tag-good");
            item.tag.RemoveFromClassList("tag-fair");
            item.tag.RemoveFromClassList("tag-slip");
            item.tag.AddToClassList(statusTagClass);

            for (int i = 0; i < item.segs.Length; i++)
            {
                var seg = item.segs[i];
                seg.RemoveFromClassList("seg-active");
                seg.RemoveFromClassList("seg-fair");
                seg.RemoveFromClassList("seg-slip");
                seg.RemoveFromClassList("seg-inactive");

                if (i < activeSegs)
                {
                    seg.AddToClassList(segClass);
                }
                else
                {
                    seg.AddToClassList("seg-inactive");
                }
            }
        }

        private void UpdatePlanetaryEnvironmentUI()
        {
            var envCtrl = PlanetEnvironmentController.Instance;
            PlanetProfile profile = envCtrl != null ? envCtrl.currentProfile : null;
            if (profile == null)
            {
                var writer = PlanetaryParameterWriter.Instance ?? FindAnyObjectByType<PlanetaryParameterWriter>();
                if (writer != null) profile = writer.currentProfile;
            }

            var reader = PlanetaryParameterReader.Instance ?? FindAnyObjectByType<PlanetaryParameterReader>();

            if (valEnvGravity != null)
            {
                float g = profile != null ? Mathf.Abs(profile.gravityY) : (reader != null ? reader.GetGravity() : Mathf.Abs(Physics.gravity.y));
                valEnvGravity.text = $"{g:F2} m/s²";
            }

            string pName = profile != null ? profile.planetName.ToUpper() : "MARS";
            if (btnPlanetBadge != null)
            {
                var lbl = btnPlanetBadge.Q<Label>("LblPlanetName");
                if (lbl != null)
                {
                    lbl.text = pName;
                    btnPlanetBadge.text = "";
                }
                else
                {
                    btnPlanetBadge.text = pName;
                }
            }

            if (valEnvTemp != null)
            {
                if (profile != null && !string.IsNullOrEmpty(profile.surfaceTemperature))
                {
                    valEnvTemp.text = profile.surfaceTemperature;
                }
                else
                {
                    float tempC = -60f;
                    if (pName.Contains("MOON")) tempC = -130f;
                    else if (pName.Contains("EARTH")) tempC = 15f;
                    else if (pName.Contains("TITAN")) tempC = -179f;
                    else if (pName.Contains("VENUS")) tempC = 465f;
                    else if (pName.Contains("EUROPA")) tempC = -160f;
                    else if (pName.Contains("MERCURY")) tempC = 167f;
                    valEnvTemp.text = $"{tempC:+0;-0}°C";
                }
            }

            if (valEnvPress != null)
            {
                if (profile != null)
                {
                    valEnvPress.text = profile.pressureKPa >= 1000f
                        ? $"{profile.pressureKPa:N0} kPa"
                        : $"{profile.pressureKPa:F1} kPa";
                }
                else
                {
                    float pressKpa = 0.6f;
                    if (pName.Contains("MOON") || pName.Contains("EUROPA") || pName.Contains("MERCURY")) pressKpa = 0.0f;
                    else if (pName.Contains("EARTH")) pressKpa = 101.3f;
                    else if (pName.Contains("TITAN")) pressKpa = 146.7f;
                    else if (pName.Contains("VENUS")) pressKpa = 9200f;
                    valEnvPress.text = pressKpa >= 1000f ? $"{pressKpa:N0} kPa" : $"{pressKpa:F1} kPa";
                }
            }

            if (lblEnvHazardTitle != null && profile != null && !string.IsNullOrEmpty(profile.hazardTitle))
            {
                lblEnvHazardTitle.text = profile.hazardTitle.ToUpper();
            }

            if (valEnvDust != null)
            {
                if (profile != null && !string.IsNullOrEmpty(profile.hazardValue))
                {
                    valEnvDust.text = profile.hazardValue;
                }
                else
                {
                    float wind = reader != null ? reader.GetWindSpeed() : 4.5f;
                    bool hasDust = (pName.Contains("MARS") || pName.Contains("TITAN")) && (wind > 8f || RenderSettings.fogDensity > 0.02f);
                    valEnvDust.text = hasDust ? "Active" : "No";
                }

                valEnvDust.RemoveFromClassList("val-green");
                valEnvDust.RemoveFromClassList("val-gold");
                valEnvDust.RemoveFromClassList("val-red");
                string lowerVal = valEnvDust.text.ToLower();
                if (lowerVal.Contains("no") || lowerVal.Contains("clear") || lowerVal.Contains("none") || lowerVal.Contains("normal"))
                {
                    valEnvDust.AddToClassList("val-green");
                }
                else if (lowerVal.Contains("corrosive") || lowerVal.Contains("extreme") || lowerVal.Contains("active"))
                {
                    valEnvDust.AddToClassList("val-red");
                }
                else
                {
                    valEnvDust.AddToClassList("val-gold");
                }
            }
        }

        // -------------------------------------------------------------
        // Rover-Aware Drive Modes & Camera Controls
        // -------------------------------------------------------------
        private void OnDriveButtonClicked(int index)
        {
            if (!ActiveRoverContext.HasActiveRover) return;

            var handle = ActiveRoverContext.Current;
            if (handle.profile != null && handle.profile.id == "m2020")
            {
                var m2020 = (handle.rootGameObject != null ? handle.rootGameObject.GetComponent<RoverController_m2020>() : null) ?? FindAnyObjectByType<RoverController_m2020>();
                if (m2020 != null)
                {
                    switch (index)
                    {
                        case 0: m2020.SetSteeringMode(RoverController_m2020.DriveMode.Ackermann); break;
                        case 1: m2020.SetSteeringMode(RoverController_m2020.DriveMode.PointTurn); break;
                        case 2: m2020.SetSteeringMode(RoverController_m2020.DriveMode.Crab); break;
                        case 3: m2020.SetSteeringMode(RoverController_m2020.DriveMode.TankDrive); break;
                    }
                }
                UpdateDriveModeUI();
            }
            else
            {
                switch (index)
                {
                    case 0: SetDriveMode("STOP"); break;
                    case 1: SetDriveMode("CRUISE"); break;
                    case 2: SetDriveMode("EXPLORE"); break;
                    case 3: SetDriveMode("PRECISION"); break;
                }
            }
        }

        public void SetDriveMode(string mode)
        {
            currentDriveMode = mode.ToUpperInvariant();

            if (ActiveRoverContext.HasActiveRover)
            {
                var handle = ActiveRoverContext.Current;
                var husky = (handle.rootGameObject != null ? handle.rootGameObject.GetComponent<RoverController_husky>() : null) ?? FindAnyObjectByType<RoverController_husky>();
                if (husky != null) husky.SetDriveMode(currentDriveMode);

                var m20 = (handle.rootGameObject != null ? handle.rootGameObject.GetComponent<RoverController_m20>() : null) ?? FindAnyObjectByType<RoverController_m20>();
                if (m20 != null) m20.SetDriveMode(currentDriveMode);

                var m2020 = (handle.rootGameObject != null ? handle.rootGameObject.GetComponent<RoverController_m2020>() : null) ?? FindAnyObjectByType<RoverController_m2020>();
                if (m2020 != null) m20.SetDriveMode(currentDriveMode);
            }

            UpdateDriveModeUI();
            Debug.Log($"[Studio] Rover drive mode set to: {currentDriveMode}");
        }

        private void ConfigureDriveBarForRover(RoverProfile profile)
        {
            if (profile == null)
            {
                ConfigureDriveBarForStandby();
                return;
            }

            if (profile.id == "m2020")
            {
                if (lblDriveGroup != null) lblDriveGroup.text = "STEERING MODE";
                if (btnModeStop != null) { btnModeStop.text = "ACKERMANN"; btnModeStop.tooltip = "Ackermann 4-wheel curved steering [1]"; }
                if (btnModeCruise != null) { btnModeCruise.text = "POINT TURN"; btnModeCruise.tooltip = "Zero-radius 360-deg spin [2]"; }
                if (btnModeExplore != null) { btnModeExplore.text = "CRAB"; btnModeExplore.tooltip = "Diagonal parallel crab translation [3]"; }
                if (btnModePrecision != null) { btnModePrecision.text = "TANK DRIVE"; btnModePrecision.tooltip = "Differential skid steer [4]"; }
                if (lblHelperHint != null) lblHelperHint.text = "[W,A,S,D] Drive | [M] Steer | [V] Cam | [Tab] HUD | [H] Hide";
            }
            else
            {
                if (lblDriveGroup != null) lblDriveGroup.text = "DRIVE MODE";
                if (btnModeStop != null) { btnModeStop.text = "STOP"; btnModeStop.tooltip = "Full stop / handbrake [1]"; }
                if (btnModeCruise != null) { btnModeCruise.text = "CRUISE"; btnModeCruise.tooltip = "Standard cruise speed [4]"; }
                if (btnModeExplore != null) { btnModeExplore.text = "EXPLORE"; btnModeExplore.tooltip = "Fast exploration speed [3]"; }
                if (btnModePrecision != null) { btnModePrecision.text = "PRECISION"; btnModePrecision.tooltip = "Fine-tuned rock crawling [2]"; }

                if (profile.id == "m20")
                {
                    if (lblHelperHint != null) lblHelperHint.text = "[W,A,S,D] Drive | [Q,E] Knees | [V] Cam | [Tab] HUD | [H] Hide";
                }
                else
                {
                    if (lblHelperHint != null) lblHelperHint.text = "[W,A,S,D] Drive | [V] Cam | [Tab] HUD | [H] Hide";
                }
            }

            UpdateDriveModeUI();
        }

        private void ConfigureDriveBarForStandby()
        {
            if (lblDriveGroup != null) lblDriveGroup.text = "DRIVE MODE";
            if (btnModeStop != null) { btnModeStop.text = "STOP"; btnModeStop.tooltip = "Full stop"; }
            if (btnModeCruise != null) { btnModeCruise.text = "CRUISE"; btnModeCruise.tooltip = "Standard speed"; }
            if (btnModeExplore != null) { btnModeExplore.text = "EXPLORE"; btnModeExplore.tooltip = "Exploration speed"; }
            if (btnModePrecision != null) { btnModePrecision.text = "PRECISION"; btnModePrecision.tooltip = "Precision speed"; }

            SetBtnClass(btnModeStop, "mode-pill-active", false);
            SetBtnClass(btnModeCruise, "mode-pill-active", false);
            SetBtnClass(btnModeExplore, "mode-pill-active", false);
            SetBtnClass(btnModePrecision, "mode-pill-active", false);

            SetBtnClass(btnModeStop, "segmented-active", false);
            SetBtnClass(btnModeCruise, "segmented-active", false);
            SetBtnClass(btnModeExplore, "segmented-active", false);
            SetBtnClass(btnModePrecision, "segmented-active", false);

            if (lblHelperHint != null) lblHelperHint.text = "[Deploy a Rover from the sidebar to drive]";
        }

        private void UpdateDriveModeUI()
        {
            if (!ActiveRoverContext.HasActiveRover)
            {
                ConfigureDriveBarForStandby();
                return;
            }

            var handle = ActiveRoverContext.Current;
            if (handle.profile != null && handle.profile.id == "m2020")
            {
                var m2020 = (handle.rootGameObject != null ? handle.rootGameObject.GetComponent<RoverController_m2020>() : null) ?? FindAnyObjectByType<RoverController_m2020>();
                var mode = m2020 != null ? m2020.driveMode : RoverController_m2020.DriveMode.Ackermann;

                SetBtnClass(btnModeStop, "mode-pill-active", mode == RoverController_m2020.DriveMode.Ackermann);
                SetBtnClass(btnModeCruise, "mode-pill-active", mode == RoverController_m2020.DriveMode.PointTurn);
                SetBtnClass(btnModeExplore, "mode-pill-active", mode == RoverController_m2020.DriveMode.Crab);
                SetBtnClass(btnModePrecision, "mode-pill-active", mode == RoverController_m2020.DriveMode.TankDrive);

                SetBtnClass(btnModeStop, "segmented-active", mode == RoverController_m2020.DriveMode.Ackermann);
                SetBtnClass(btnModeCruise, "segmented-active", mode == RoverController_m2020.DriveMode.PointTurn);
                SetBtnClass(btnModeExplore, "segmented-active", mode == RoverController_m2020.DriveMode.Crab);
                SetBtnClass(btnModePrecision, "segmented-active", mode == RoverController_m2020.DriveMode.TankDrive);
            }
            else
            {
                var h = (handle.rootGameObject != null ? handle.rootGameObject.GetComponent<RoverController_husky>() : null) ?? FindAnyObjectByType<RoverController_husky>();
                var m = (handle.rootGameObject != null ? handle.rootGameObject.GetComponent<RoverController_m20>() : null) ?? FindAnyObjectByType<RoverController_m20>();
                string activeMode = h != null ? h.currentDriveMode : (m != null ? m.currentDriveMode : currentDriveMode);

                SetBtnClass(btnModeStop, "mode-pill-active", activeMode == "STOP");
                SetBtnClass(btnModeCruise, "mode-pill-active", activeMode == "CRUISE");
                SetBtnClass(btnModeExplore, "mode-pill-active", activeMode == "EXPLORE");
                SetBtnClass(btnModePrecision, "mode-pill-active", activeMode == "PRECISION");

                SetBtnClass(btnModeStop, "segmented-active", activeMode == "STOP");
                SetBtnClass(btnModeCruise, "segmented-active", activeMode == "CRUISE");
                SetBtnClass(btnModeExplore, "segmented-active", activeMode == "EXPLORE");
                SetBtnClass(btnModePrecision, "segmented-active", activeMode == "PRECISION");
            }
        }

        private void HandleRigPerspectiveChanged(RoverCameraRig.Perspective p)
        {
            currentCamPerspective = p;
            UpdateCameraButtonsUI();
        }

        public void SetCameraPerspective(RoverCameraRig.Perspective view)
        {
            currentCamPerspective = view;
            UpdateCameraButtonsUI();

            if (RoverCameraRig.Instance != null)
            {
                RoverCameraRig.Instance.SetPerspective(view);
            }
        }

        public void SetCameraPerspective(FreeFlyCamera.CameraPerspective view)
        {
            RoverCameraRig.Perspective p;
            switch (view)
            {
                case FreeFlyCamera.CameraPerspective.Front: p = RoverCameraRig.Perspective.Front; break;
                case FreeFlyCamera.CameraPerspective.Left: p = RoverCameraRig.Perspective.Left; break;
                case FreeFlyCamera.CameraPerspective.Right: p = RoverCameraRig.Perspective.Right; break;
                case FreeFlyCamera.CameraPerspective.Top: p = RoverCameraRig.Perspective.Top; break;
                case FreeFlyCamera.CameraPerspective.Rear:
                default: p = RoverCameraRig.Perspective.Rear; break;
            }
            SetCameraPerspective(p);
        }

        private void UpdateCameraButtonsUI()
        {
            SetBtnClass(btnCamFront, "mode-pill-active", currentCamPerspective == RoverCameraRig.Perspective.Front);
            SetBtnClass(btnCamRear, "mode-pill-active", currentCamPerspective == RoverCameraRig.Perspective.Rear);
            SetBtnClass(btnCamLeft, "mode-pill-active", currentCamPerspective == RoverCameraRig.Perspective.Left);
            SetBtnClass(btnCamRight, "mode-pill-active", currentCamPerspective == RoverCameraRig.Perspective.Right);
            SetBtnClass(btnCamTop, "mode-pill-active", currentCamPerspective == RoverCameraRig.Perspective.Top);
            SetBtnClass(btnCamFree, "mode-pill-active", currentCamPerspective == RoverCameraRig.Perspective.Free);

            SetBtnClass(btnCamFront, "segmented-active", currentCamPerspective == RoverCameraRig.Perspective.Front);
            SetBtnClass(btnCamRear, "segmented-active", currentCamPerspective == RoverCameraRig.Perspective.Rear);
            SetBtnClass(btnCamLeft, "segmented-active", currentCamPerspective == RoverCameraRig.Perspective.Left);
            SetBtnClass(btnCamRight, "segmented-active", currentCamPerspective == RoverCameraRig.Perspective.Right);
            SetBtnClass(btnCamTop, "segmented-active", currentCamPerspective == RoverCameraRig.Perspective.Top);
            SetBtnClass(btnCamFree, "segmented-active", currentCamPerspective == RoverCameraRig.Perspective.Free);

            if (btnCamRear != null) btnCamRear.text = "Back";
            if (btnCamFront != null) btnCamFront.text = "Front";
            if (btnCamLeft != null) btnCamLeft.text = "Left";
            if (btnCamRight != null) btnCamRight.text = "Right";
            if (btnCamTop != null) btnCamTop.text = "Up";
            if (btnCamFree != null) btnCamFree.text = "Free";
        }

        private void HandleCameraSpeedChanged(float speed)
        {
            if (sliderCamSpeed != null)
            {
                sliderCamSpeed.SetValueWithoutNotify(speed);
            }
            if (valCamSpeedHud != null)
            {
                valCamSpeedHud.text = $"{speed:F1} m/s";
            }
        }

        // -------------------------------------------------------------
        // Terrain Lab Workbench & Presets
        // -------------------------------------------------------------
        public void SetTerrainLabActive(bool labActive)
        {
            isTerrainLabActive = labActive;
            if (tabMissionView != null) tabMissionView.style.display = labActive ? DisplayStyle.None : DisplayStyle.Flex;
            if (tabTerrainLabView != null) tabTerrainLabView.style.display = labActive ? DisplayStyle.Flex : DisplayStyle.None;

            SetBtnClass(btnTabMission, "studio-tab-btn-active", !labActive);
            SetBtnClass(btnTabTerrainLab, "studio-tab-btn-active", labActive);
        }

        private void SelectPreset(HeightmapPreset preset)
        {
            currentPreset = preset;
            ProjectName.Core.SimulationContext.SelectedTerrainPreset = preset.ToString();
            RegeneratePreset();
            UpdatePresetButtonsUI();
        }

        private void RegeneratePreset()
        {
            currentHeights = HeightmapLoader.GeneratePresetHeights(
                currentPreset,
                tuningConfig.resolution,
                tuningConfig.smoothingFactor,
                tuningConfig.heightCurve,
                currentSeed
            );

            undoStack.Clear();
            redoStack.Clear();
            UpdateHistoryUI();
            RefreshPreview();

            if (isLivePreviewEnabled)
            {
                var curTerrain = terrainGenerator != null ? terrainGenerator.GetCurrentTerrain() : UnityEngine.Terrain.activeTerrain;
                if (curTerrain != null && curTerrain.terrainData != null)
                {
                    curTerrain.terrainData.SetHeights(0, 0, currentHeights);
                }
            }
        }

        private void UpdatePresetButtonsUI()
        {
            // Radar overview pills
            SetBtnClass(btnPresetGale, "preset-active", currentPreset == HeightmapPreset.GaleCrater);
            SetBtnClass(btnPresetOlympus, "preset-active", currentPreset == HeightmapPreset.OlympusMons);
            SetBtnClass(btnPresetShackleton, "preset-active", currentPreset == HeightmapPreset.ShackletonCrater);
            SetBtnClass(btnPresetValles, "preset-active", currentPreset == HeightmapPreset.VallesMarineris);
            SetBtnClass(btnPresetFractal, "preset-active", currentPreset == HeightmapPreset.ProceduralFractal || currentPreset == HeightmapPreset.Plains);

            // Terrain Lab pills
            SetBtnClass(btnLabPresetGale, "preset-btn-lab-active", currentPreset == HeightmapPreset.GaleCrater);
            SetBtnClass(btnLabPresetOlympus, "preset-btn-lab-active", currentPreset == HeightmapPreset.OlympusMons);
            SetBtnClass(btnLabPresetShackleton, "preset-btn-lab-active", currentPreset == HeightmapPreset.ShackletonCrater);
            SetBtnClass(btnLabPresetValles, "preset-btn-lab-active", currentPreset == HeightmapPreset.VallesMarineris);
            SetBtnClass(btnLabPresetPlains, "preset-btn-lab-active", currentPreset == HeightmapPreset.Plains);
            SetBtnClass(btnLabPresetFractal, "preset-btn-lab-active", currentPreset == HeightmapPreset.ProceduralFractal);

            // Flow Setup Presets
            SetBtnClass(btnFlowPresetGale, "preset-btn-lab-active", currentPreset == HeightmapPreset.GaleCrater);
            SetBtnClass(btnFlowPresetOlympus, "preset-btn-lab-active", currentPreset == HeightmapPreset.OlympusMons);
            SetBtnClass(btnFlowPresetValles, "preset-btn-lab-active", currentPreset == HeightmapPreset.VallesMarineris);
            SetBtnClass(btnFlowPresetShackleton, "preset-btn-lab-active", currentPreset == HeightmapPreset.ShackletonCrater);
            SetBtnClass(btnFlowPresetPlains, "preset-btn-lab-active", currentPreset == HeightmapPreset.Plains);
            SetBtnClass(btnFlowPresetFractal, "preset-btn-lab-active", currentPreset == HeightmapPreset.ProceduralFractal);
        }

        // -------------------------------------------------------------
        // Interactive 2D Heightmap Canvas Painter & Dirty-Rect Undo/Redo
        // -------------------------------------------------------------
        private void SetBrushMode(BrushMode mode)
        {
            currentBrushMode = mode;
            UpdateBrushButtonStyles();
            if (rowFlattenHeight != null)
            {
                rowFlattenHeight.style.display = (mode == BrushMode.Flatten) ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private void UpdateBrushButtonStyles()
        {
            SetBtnClass(btnBrushRaise, "brush-btn-active", currentBrushMode == BrushMode.Raise);
            SetBtnClass(btnBrushLower, "brush-btn-active", currentBrushMode == BrushMode.Lower);
            SetBtnClass(btnBrushSmooth, "brush-btn-active", currentBrushMode == BrushMode.Smooth);
            SetBtnClass(btnBrushFlatten, "brush-btn-active", currentBrushMode == BrushMode.Flatten);
            SetBtnClass(btnBrushNoise, "brush-btn-active", currentBrushMode == BrushMode.Noise);
        }

        private void OnPainterPointerDown(PointerDownEvent evt)
        {
            if (isDrivingHudMode || currentHeights == null) return;
            var targetImg = evt.currentTarget as VisualElement;
            if (targetImg != null)
            {
                targetImg.CapturePointer(evt.pointerId);
            }
            evt.StopPropagation();
            isPainting = true;
            strokeTouchedCells.Clear();
            PaintAtPointerPosition(evt.localPosition);
        }

        private void OnPainterPointerMove(PointerMoveEvent evt)
        {
            if (isDrivingHudMode)
            {
                if (painterBrushCursor != null) painterBrushCursor.style.display = DisplayStyle.None;
                return;
            }

            UpdateBrushCursor(evt.localPosition);

            if (!isPainting) return;
            evt.StopPropagation();
            PaintAtPointerPosition(evt.localPosition);
        }

        private void UpdateBrushCursor(Vector2 localPos)
        {
            if (painterBrushCursor == null) return;

            var targetImg = heightmapPreviewImage ?? heightmapPainterImage;
            float canvasW = targetImg != null ? targetImg.resolvedStyle.width : 164f;
            if (canvasW <= 0f && minimapContainer != null) canvasW = minimapContainer.resolvedStyle.width;
            if (canvasW <= 0f) canvasW = 164f;

            float radiusPx = Mathf.Max(3f, brushRadius * canvasW);
            painterBrushCursor.style.left = localPos.x - radiusPx;
            painterBrushCursor.style.top = localPos.y - radiusPx;
            painterBrushCursor.style.width = radiusPx * 2f;
            painterBrushCursor.style.height = radiusPx * 2f;
            painterBrushCursor.style.display = DisplayStyle.Flex;
        }

        private void OnPainterPointerUp(PointerUpEvent evt)
        {
            var targetImg = evt.currentTarget as VisualElement;
            if (targetImg != null && targetImg.HasPointerCapture(evt.pointerId))
            {
                targetImg.ReleasePointer(evt.pointerId);
            }
            evt.StopPropagation();
            EndStroke();
        }

        private void OnPainterPointerLeave(PointerLeaveEvent evt)
        {
            if (painterBrushCursor != null)
            {
                painterBrushCursor.style.display = DisplayStyle.None;
            }
            if (!isPainting) return;
            var targetImg = evt.currentTarget as VisualElement;
            if (targetImg != null && targetImg.HasPointerCapture(evt.pointerId))
            {
                targetImg.ReleasePointer(evt.pointerId);
            }
            EndStroke();
        }

        private void PaintAtPointerPosition(Vector2 localPos)
        {
            if (currentHeights == null) return;

            var targetImg = heightmapPreviewImage ?? heightmapPainterImage;
            float w = targetImg != null ? targetImg.resolvedStyle.width : 0f;
            float h = targetImg != null ? targetImg.resolvedStyle.height : 0f;

            if ((w <= 0f || h <= 0f) && minimapContainer != null)
            {
                w = minimapContainer.resolvedStyle.width;
                h = minimapContainer.resolvedStyle.height;
            }
            if (w <= 0f) w = 164f;
            if (h <= 0f) h = 164f;

            float u = Mathf.Clamp01(localPos.x / w);
            float v = Mathf.Clamp01(1.0f - (localPos.y / h)); // Invert Y

            int res = tuningConfig.resolution;
            int centerX = Mathf.RoundToInt(u * (res - 1));
            int centerY = Mathf.RoundToInt(v * (res - 1));
            int radiusPixels = Mathf.Max(1, Mathf.RoundToInt(brushRadius * res));

            int minX = Mathf.Max(0, centerX - radiusPixels);
            int maxX = Mathf.Min(res - 1, centerX + radiusPixels);
            int minY = Mathf.Max(0, centerY - radiusPixels);
            int maxY = Mathf.Min(res - 1, centerY + radiusPixels);

            for (int py = minY; py <= maxY; py++)
            {
                for (int px = minX; px <= maxX; px++)
                {
                    int key = py * res + px;
                    if (!strokeTouchedCells.ContainsKey(key))
                    {
                        strokeTouchedCells[key] = currentHeights[py, px];
                    }
                }
            }

            RectInt dirtyRect = HeightmapLoader.ApplyBrush(
                currentHeights,
                res,
                new Vector2(u, v),
                currentBrushMode,
                brushRadius,
                brushStrength,
                brushHardness,
                targetFlattenHeight,
                currentSeed
            );

            // Update only dirty rect on preview texture
            HeightmapLoader.UpdatePreviewTextureRect(
                previewTexture,
                currentHeights,
                dirtyRect,
                res,
                tuningConfig.materialType
            );

            if (isLivePreviewEnabled)
            {
                var curTerrain = terrainGenerator != null ? terrainGenerator.GetCurrentTerrain() : UnityEngine.Terrain.activeTerrain;
                if (curTerrain != null && curTerrain.terrainData != null)
                {
                    int subW = dirtyRect.width;
                    int subH = dirtyRect.height;
                    float[,] subHeights = new float[subH, subW];
                    for (int sy = 0; sy < subH; sy++)
                    {
                        for (int sx = 0; sx < subW; sx++)
                        {
                            subHeights[sy, sx] = currentHeights[dirtyRect.y + sy, dirtyRect.x + sx];
                        }
                    }
                    curTerrain.terrainData.SetHeightsDelayLOD(dirtyRect.x, dirtyRect.y, subHeights);
                }
            }
        }

        private void EndStroke()
        {
            if (!isPainting) return;
            isPainting = false;

            if (strokeTouchedCells.Count > 0)
            {
                int res = tuningConfig.resolution;
                int minX = int.MaxValue, maxX = int.MinValue;
                int minY = int.MaxValue, maxY = int.MinValue;

                foreach (var key in strokeTouchedCells.Keys)
                {
                    int py = key / res;
                    int px = key % res;
                    if (px < minX) minX = px;
                    if (px > maxX) maxX = px;
                    if (py < minY) minY = py;
                    if (py > maxY) maxY = py;
                }

                minX = Mathf.Clamp(minX, 0, res - 1);
                maxX = Mathf.Clamp(maxX, 0, res - 1);
                minY = Mathf.Clamp(minY, 0, res - 1);
                maxY = Mathf.Clamp(maxY, 0, res - 1);

                int rw = maxX - minX + 1;
                int rh = maxY - minY + 1;

                if (rw > 0 && rh > 0)
                {
                    float[,] oldSub = new float[rh, rw];
                    float[,] newSub = new float[rh, rw];

                    for (int y = 0; y < rh; y++)
                    {
                        int py = minY + y;
                        for (int x = 0; x < rw; x++)
                        {
                            int px = minX + x;
                            int key = py * res + px;
                            oldSub[y, x] = strokeTouchedCells.TryGetValue(key, out float oldVal) ? oldVal : currentHeights[py, px];
                            newSub[y, x] = currentHeights[py, px];
                        }
                    }

                    if (undoStack.Count >= MAX_UNDO_STEPS)
                    {
                        var temp = new List<HeightStrokeDiff>(undoStack);
                        temp.RemoveAt(temp.Count - 1);
                        undoStack.Clear();
                        for (int i = temp.Count - 1; i >= 0; i--)
                        {
                            undoStack.Push(temp[i]);
                        }
                    }

                    undoStack.Push(new HeightStrokeDiff
                    {
                        rect = new RectInt(minX, minY, rw, rh),
                        oldHeights = oldSub,
                        newHeights = newSub
                    });

                    redoStack.Clear();
                    UpdateHistoryUI();
                }

                strokeTouchedCells.Clear();

                if (isLivePreviewEnabled)
                {
                    var curTerrain = terrainGenerator != null ? terrainGenerator.GetCurrentTerrain() : UnityEngine.Terrain.activeTerrain;
                    if (curTerrain != null && curTerrain.terrainData != null)
                    {
                        curTerrain.terrainData.SyncHeightmap();
                    }
                }
            }
        }

        public void UndoLastStroke()
        {
            if (undoStack.Count == 0) return;

            var diff = undoStack.Pop();
            redoStack.Push(diff);

            int res = tuningConfig.resolution;
            int rw = diff.rect.width;
            int rh = diff.rect.height;

            for (int y = 0; y < rh; y++)
            {
                int py = diff.rect.y + y;
                for (int x = 0; x < rw; x++)
                {
                    int px = diff.rect.x + x;
                    if (py >= 0 && py < res && px >= 0 && px < res)
                    {
                        currentHeights[py, px] = diff.oldHeights[y, x];
                    }
                }
            }

            HeightmapLoader.UpdatePreviewTextureRect(previewTexture, currentHeights, diff.rect, res, tuningConfig.materialType);

            if (isLivePreviewEnabled)
            {
                var curTerrain = terrainGenerator != null ? terrainGenerator.GetCurrentTerrain() : UnityEngine.Terrain.activeTerrain;
                if (curTerrain != null && curTerrain.terrainData != null)
                {
                    curTerrain.terrainData.SetHeightsDelayLOD(diff.rect.x, diff.rect.y, diff.oldHeights);
                    curTerrain.terrainData.SyncHeightmap();
                }
            }

            UpdateHistoryUI();
            ShowNotification("Undo stroke.");
        }

        public void RedoStroke()
        {
            if (redoStack.Count == 0) return;

            var diff = redoStack.Pop();
            undoStack.Push(diff);

            int res = tuningConfig.resolution;
            int rw = diff.rect.width;
            int rh = diff.rect.height;

            for (int y = 0; y < rh; y++)
            {
                int py = diff.rect.y + y;
                for (int x = 0; x < rw; x++)
                {
                    int px = diff.rect.x + x;
                    if (py >= 0 && py < res && px >= 0 && px < res)
                    {
                        currentHeights[py, px] = diff.newHeights[y, x];
                    }
                }
            }

            HeightmapLoader.UpdatePreviewTextureRect(previewTexture, currentHeights, diff.rect, res, tuningConfig.materialType);

            if (isLivePreviewEnabled)
            {
                var curTerrain = terrainGenerator != null ? terrainGenerator.GetCurrentTerrain() : UnityEngine.Terrain.activeTerrain;
                if (curTerrain != null && curTerrain.terrainData != null)
                {
                    curTerrain.terrainData.SetHeightsDelayLOD(diff.rect.x, diff.rect.y, diff.newHeights);
                    curTerrain.terrainData.SyncHeightmap();
                }
            }

            UpdateHistoryUI();
            ShowNotification("Redo stroke.");
        }

        private void UpdateHistoryUI()
        {
            if (labelHistoryStatus != null)
            {
                labelHistoryStatus.text = $"History: {undoStack.Count}/{MAX_UNDO_STEPS}";
            }
            if (btnUndo != null) btnUndo.SetEnabled(undoStack.Count > 0);
            if (btnRedo != null) btnRedo.SetEnabled(redoStack.Count > 0);
        }

        // -------------------------------------------------------------
        // Height Remap Curves & Surface Materials
        // -------------------------------------------------------------
        private void SetHeightCurve(HeightRemapCurve curve)
        {
            tuningConfig.heightCurve = curve;
            UpdateCurveButtonStyles();
            RegeneratePreset();
        }

        private void UpdateCurveButtonStyles()
        {
            SetBtnClass(btnCurveLinear, "curve-btn-active", tuningConfig.heightCurve == HeightRemapCurve.Linear);
            SetBtnClass(btnCurveExponential, "curve-btn-active", tuningConfig.heightCurve == HeightRemapCurve.Exponential);
            SetBtnClass(btnCurveRidge, "curve-btn-active", tuningConfig.heightCurve == HeightRemapCurve.RidgePeak);
            SetBtnClass(btnCurveBasin, "curve-btn-active", tuningConfig.heightCurve == HeightRemapCurve.BasinInversion);
        }

        public void SetMaterial(PlanetaryMaterialType mat)
        {
            SetMaterialInternal(mat, isManualOverride: true);
        }

        private void SetMaterialInternal(PlanetaryMaterialType mat, bool isManualOverride)
        {
            if (isManualOverride)
            {
                followPlanetProfile = false;
                if (toggleFollowPlanet != null) toggleFollowPlanet.value = false;
            }

            tuningConfig.materialType = mat;
            ProjectName.Core.SimulationContext.SelectedMaterial = mat.ToString();
            UpdateMaterialButtonStyles();
            RefreshPreview();

            if (terrainGenerator == null) terrainGenerator = FindAnyObjectByType<TerrainGenerator>();
            if (terrainGenerator != null)
            {
                var curTerrain = terrainGenerator.GetCurrentTerrain() ?? UnityEngine.Terrain.activeTerrain;
                if (curTerrain != null && terrainGenerator.materialManager != null)
                {
                    terrainGenerator.materialManager.ApplyMaterial(curTerrain, mat);
                }
            }
        }

        private void HandlePlanetaryProfileApplied(PlanetProfile profile)
        {
            UpdatePlanetaryEnvironmentUI();
            if (!followPlanetProfile || profile == null) return;
            SyncMaterialWithProfile(profile);
        }

        private void SyncMaterialWithCurrentPlanet()
        {
            var envCtrl = PlanetEnvironmentController.Instance;
            PlanetProfile profile = envCtrl != null ? envCtrl.currentProfile : null;
            if (profile == null)
            {
                var writer = PlanetaryParameterWriter.Instance;
                if (writer != null) profile = writer.currentProfile;
            }

            if (profile != null)
            {
                SyncMaterialWithProfile(profile);
            }
            else
            {
                SetMaterialInternal(PlanetaryMaterialType.MartianDust, isManualOverride: false);
            }
        }

        private void SyncMaterialWithProfile(PlanetProfile profile)
        {
            PlanetaryMaterialType targetMat = profile.defaultMaterialPreset;
            SetMaterialInternal(targetMat, isManualOverride: false);
        }

        private void UpdateMaterialButtonStyles()
        {
            SetBtnClass(btnMatMartian, "material-btn-active", tuningConfig.materialType == PlanetaryMaterialType.MartianDust);
            SetBtnClass(btnMatLunar, "material-btn-active", tuningConfig.materialType == PlanetaryMaterialType.LunarRegolith);
            SetBtnClass(btnMatBasalt, "material-btn-active", tuningConfig.materialType == PlanetaryMaterialType.VolcanicBasalt);
            SetBtnClass(btnMatIce, "material-btn-active", tuningConfig.materialType == PlanetaryMaterialType.PolarIce);
            SetBtnClass(btnMatCanyon, "material-btn-active", tuningConfig.materialType == PlanetaryMaterialType.RedCanyon);
            SetBtnClass(btnMatWireframe, "material-btn-active", tuningConfig.materialType == PlanetaryMaterialType.TopographicWireframe);
            SetBtnClass(btnMatNormal, "material-btn-active", tuningConfig.materialType == PlanetaryMaterialType.NormalInspector);

            // Flow Setup Materials
            SetBtnClass(btnFlowMatMartian, "material-btn-active", tuningConfig.materialType == PlanetaryMaterialType.MartianDust);
            SetBtnClass(btnFlowMatLunar, "material-btn-active", tuningConfig.materialType == PlanetaryMaterialType.LunarRegolith);
            SetBtnClass(btnFlowMatBasalt, "material-btn-active", tuningConfig.materialType == PlanetaryMaterialType.VolcanicBasalt);
            SetBtnClass(btnFlowMatIce, "material-btn-active", tuningConfig.materialType == PlanetaryMaterialType.PolarIce);
            SetBtnClass(btnFlowMatCanyon, "material-btn-active", tuningConfig.materialType == PlanetaryMaterialType.RedCanyon);
            SetBtnClass(btnFlowMatWireframe, "material-btn-active", tuningConfig.materialType == PlanetaryMaterialType.TopographicWireframe);
        }

        private void SyncTerrainDimensionsLive()
        {
            var curTerrain = terrainGenerator != null ? terrainGenerator.GetCurrentTerrain() : UnityEngine.Terrain.activeTerrain;
            if (curTerrain != null && curTerrain.terrainData != null)
            {
                curTerrain.terrainData.size = new Vector3(tuningConfig.terrainWidth, tuningConfig.maxHeight, tuningConfig.terrainLength);
                curTerrain.transform.position = new Vector3(-tuningConfig.terrainWidth * 0.5f, tuningConfig.baseOffset, -tuningConfig.terrainLength * 0.5f);
                var col = curTerrain.GetComponent<TerrainCollider>();
                if (col != null)
                {
                    col.terrainData = null;
                    col.terrainData = curTerrain.terrainData;
                }
            }
        }

        private void OnResolutionChanged(ChangeEvent<string> evt)
        {
            int newRes = 513;
            if (evt.newValue.Contains("257")) newRes = 257;
            else if (evt.newValue.Contains("513")) newRes = 513;
            else if (evt.newValue.Contains("1025")) newRes = 1025;
            else if (evt.newValue.Contains("2049")) newRes = 2049;

            if (newRes != tuningConfig.resolution)
            {
                currentHeights = HeightmapLoader.ResampleHeights(currentHeights, newRes);
                tuningConfig.resolution = newRes;
                undoStack.Clear();
                redoStack.Clear();
                UpdateHistoryUI();
                RefreshPreview();
                ShowNotification($"Heightmap resampled to {newRes}x{newRes}.");
            }
        }

        // -------------------------------------------------------------
        // File Operations: PNG Import & Export
        // -------------------------------------------------------------
        private void OnExportPNGClicked()
        {
            if (currentHeights == null) return;
            int res = tuningConfig.resolution;

            string path = StandaloneFileBrowser.SaveFilePanel("Export Heightmap PNG", "", "PlanetaryHeightmap.png", "png");
            if (string.IsNullOrEmpty(path)) return;

            Texture2D exportTex = new Texture2D(res, res, TextureFormat.RGB24, false);
            Color[] colors = new Color[res * res];
            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    float h = currentHeights[y, x];
                    colors[y * res + x] = new Color(h, h, h, 1f);
                }
            }
            exportTex.SetPixels(colors);
            exportTex.Apply(false);

            byte[] bytes = exportTex.EncodeToPNG();
            File.WriteAllBytes(path, bytes);
            DestroyImmediate(exportTex);

            ShowNotification($"Exported heightmap PNG to {Path.GetFileName(path)}");
            Debug.Log($"[TerrainLab] Exported heightmap PNG to {path}");
        }

        private void OnImportPNGClicked()
        {
            var extensions = new[] { new ExtensionFilter("Grayscale Heightmap", "png", "jpg", "jpeg") };
            string[] paths = StandaloneFileBrowser.OpenFilePanel("Import Heightmap Image", "", extensions, false);

            if (paths.Length > 0 && !string.IsNullOrEmpty(paths[0]))
            {
                string path = paths[0];
                float[,] loaded = HeightmapLoader.LoadHeightsFromImage(
                    path,
                    tuningConfig.resolution,
                    tuningConfig.smoothingFactor,
                    tuningConfig.heightCurve
                );

                if (loaded != null)
                {
                    currentHeights = loaded;
                    currentPreset = HeightmapPreset.CustomImage;
                    undoStack.Clear();
                    redoStack.Clear();
                    UpdateHistoryUI();
                    UpdatePresetButtonsUI();
                    RefreshPreview();
                    ShowNotification($"Imported heightmap from {Path.GetFileName(path)}");
                }
            }
        }

        private void OnBrowseFileClicked()
        {
            OnImportPNGClicked();
        }

        private void OnApplyTerrainLabClicked()
        {
            OnGenerateTerrainClicked();
        }

        // -------------------------------------------------------------
        // Primary Terrain Generation & Collider Sync
        // -------------------------------------------------------------
        private void OnGenerateTerrainClicked()
        {
            if (btnGenerateTerrain != null)
            {
                btnGenerateTerrain.SetEnabled(false);
                var btnLbl = btnGenerateTerrain.Q<Label>(className: "btn-text");
                if (btnLbl != null) btnLbl.text = "Generating...";
                else btnGenerateTerrain.text = "Generating...";
            }

            try
            {
                SetWorkflowStep(2);
                ClearBreadcrumbs();

                if (currentHeights == null)
                {
                    currentHeights = HeightmapLoader.GeneratePresetHeights(
                        currentPreset,
                        tuningConfig.resolution,
                        tuningConfig.smoothingFactor,
                        tuningConfig.heightCurve,
                        currentSeed
                    );
                }

                if (terrainGenerator == null) terrainGenerator = FindAnyObjectByType<TerrainGenerator>();
                if (terrainGenerator == null)
                {
                    Debug.LogError("[Studio] TerrainGenerator component not found!");
                    return;
                }

                // Generates new terrain and automatically destroys ALL old terrains and hazards!
                UnityEngine.Terrain terrain = terrainGenerator.GenerateTerrain(currentHeights, tuningConfig);
                if (terrain != null)
                {
                    // Force TerrainCollider to bind to terrainData to prevent rover floating/sinking bugs
                    var col = terrain.GetComponent<TerrainCollider>();
                    if (col != null)
                    {
                        col.terrainData = null;
                        col.terrainData = terrain.terrainData;
                    }

                    if (flowController != null)
                    {
                        flowController.OnTerrainReady();
                    }

                    // Auto-respawn active rover at last placement pose
                    if (RoverPlacementController.Instance != null && ActiveRoverContext.HasActiveRover)
                    {
                        RoverPlacementController.Instance.RespawnAtLastPlacementPose();
                        Debug.Log("[Studio] Auto-respawned active rover at last placement pose after terrain rebuild.");
                    }
                }

                RefreshPreview();
                ShowNotification("Terrain generated & collider synced.");
                SetWorkflowStep(ActiveRoverContext.HasActiveRover ? 6 : 3);
                Debug.Log("[Studio] New planetary terrain generated successfully. Old terrain removed.");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[Studio] Terrain generation exception: {ex.Message}\n{ex.StackTrace}");
            }
            finally
            {
                if (btnGenerateTerrain != null)
                {
                    btnGenerateTerrain.SetEnabled(true);
                    var btnLbl = btnGenerateTerrain.Q<Label>(className: "btn-text");
                    if (btnLbl != null) btnLbl.text = "Generate Terrain";
                    else btnGenerateTerrain.text = "Generate Terrain";
                }
            }
        }

        private void RefreshPreview()
        {
            if (currentHeights == null) return;

            previewTexture = HeightmapLoader.CreatePreviewTexture(
                currentHeights,
                tuningConfig.resolution,
                tuningConfig.materialType,
                previewTexture
            );

            if (heightmapPreviewImage != null)
            {
                heightmapPreviewImage.image = previewTexture;
            }

            if (heightmapPainterImage != null)
            {
                heightmapPainterImage.image = previewTexture;
            }
        }

        // -------------------------------------------------------------
        // Rover Deployment
        // -------------------------------------------------------------
        private void DeployRover(string roverId)
        {
            activeRoverId = roverId;
            UpdateRoverSelectionUI();

            var terrain = UnityEngine.Terrain.activeTerrain ?? FindAnyObjectByType<UnityEngine.Terrain>();
            if (terrain == null)
            {
                ShowNotification("Generate planetary terrain first before deploying rovers.");
                SetWorkflowStep(1);
                return;
            }

            SetWorkflowStep(4);

            if (RoverPlacementController.Instance != null)
            {
                RoverPlacementController.Instance.StartPlacement(roverId);
            }
            else if (flowController != null)
            {
                if (roverId == "husky") flowController.OnSelectHusky();
                else if (roverId == "m20") flowController.OnSelectM20();
                else if (roverId == "m2020") flowController.OnSelectM2020();
            }
        }

        private void UpdateRoverSelectionUI()
        {
            bool hasRover = ActiveRoverContext.HasActiveRover;
            string curId = hasRover ? ActiveRoverContext.Current.roverId : "";

            SetBtnClass(btnRoverHusky, "rover-active-row", hasRover && curId == "husky");
            SetBtnClass(btnRoverM20, "rover-active-row", hasRover && curId == "m20");
            SetBtnClass(btnRoverM2020, "rover-active-row", hasRover && curId == "m2020");

            UpdateTagClass(tagHuskyActive, hasRover && curId == "husky");
            UpdateTagClass(tagM20Active, hasRover && curId == "m20");
            UpdateTagClass(tagM2020Active, hasRover && curId == "m2020");
        }

        private void UpdateTagClass(Label tag, bool isActive)
        {
            if (tag == null) return;
            tag.text = isActive ? "ACTIVE" : "DEPLOY";
            tag.RemoveFromClassList("rover-active-tag");
            tag.RemoveFromClassList("rover-idle-tag");
            tag.AddToClassList(isActive ? "rover-active-tag" : "rover-idle-tag");
        }

        private void SetBtnClass(Button btn, string className, bool active)
        {
            if (btn == null) return;
            if (active) btn.AddToClassList(className);
            else btn.RemoveFromClassList(className);
        }

        private void OnRelocateRoverClicked()
        {
            SetDrivingHudMode(true);
            if (flowController == null) flowController = FindAnyObjectByType<SimulationFlowController>();
            if (flowController != null) flowController.ToggleRelocateMode();
        }

        private void OnCenterRoverClicked()
        {
            if (flowController == null) flowController = FindAnyObjectByType<SimulationFlowController>();
            if (flowController != null) flowController.CenterActiveRoverOnTerrain();
        }

        // -------------------------------------------------------------
        // 7-Step Guided Workflow Methods & Actions
        // -------------------------------------------------------------
        public void SetWorkflowStep(int step, string customGuide = null)
        {
            currentWorkflowStep = Mathf.Clamp(step, 1, 7);

            for (int i = 0; i < 7; i++)
            {
                if (stepPills[i] == null) continue;
                int pillStep = i + 1;
                stepPills[i].RemoveFromClassList("step-pill-active");
                stepPills[i].RemoveFromClassList("step-pill-done");

                if (pillStep < currentWorkflowStep)
                {
                    stepPills[i].AddToClassList("step-pill-done");
                }
                else if (pillStep == currentWorkflowStep)
                {
                    stepPills[i].AddToClassList("step-pill-active");
                }
            }

            if (lblWorkflowGuide != null)
            {
                if (!string.IsNullOrEmpty(customGuide))
                {
                    lblWorkflowGuide.text = customGuide;
                }
                else
                {
                    bool isVR = ProjectName.Core.SceneLoader.IsVREnabled;
                    switch (currentWorkflowStep)
                    {
                        case 1:
                            lblWorkflowGuide.text = "STEP 1: Select a heightmap preset or upload PNG, then click Generate Terrain.";
                            break;
                        case 2:
                            lblWorkflowGuide.text = "STEP 2: Generating planetary mesh & syncing physics colliders...";
                            break;
                        case 3:
                            lblWorkflowGuide.text = "STEP 3: Terrain ready! Select a digital twin rover (Husky, M20, or Perseverance) to deploy.";
                            break;
                        case 4:
                            lblWorkflowGuide.text = isVR
                                ? "STEP 4: Aim laser pointer at terrain & pull Trigger to place. Thumbstick to rotate heading."
                                : "STEP 4: Move mouse over terrain & Left-Click to place. [Q,E] or Scroll to rotate heading.";
                            break;
                        case 5:
                            lblWorkflowGuide.text = "STEP 5: Configure planetary body & gravity (Click planet badge at top), or begin driving.";
                            break;
                        case 6:
                            lblWorkflowGuide.text = isVR
                                ? "STEP 6: Driving active! [Left Stick] to drive • [A/X] for speeds • [B] / Recall to toggle HUD."
                                : "STEP 6: Driving active! [W,A,S,D] to drive • [1,2,3,4] for speeds • [Tab] for Driving HUD.";
                            break;
                        case 7:
                            lblWorkflowGuide.text = "STEP 7: Reset options active. Click 'RESET ROVER' to respawn, or 'NEW TERRAIN' to start over.";
                            break;
                    }
                }
            }
        }

        private void OnResetRoverKeepTerrainClicked()
        {
            if (RoverPlacementController.Instance != null && ActiveRoverContext.HasActiveRover)
            {
                RoverPlacementController.Instance.RespawnAtLastPlacementPose();
            }
            else if (flowController != null)
            {
                flowController.RespawnActiveRover();
            }
            SetWorkflowStep(6, "Rover respawned on terrain. Telemetry and mission odometer reset. Ready to drive!");
            ShowNotification("Rover respawned! Terrain preserved.");
        }

        private void OnNewTerrainClicked()
        {
            if (ActiveRoverContext.HasActiveRover || (flowController != null && flowController.CurrentSimulationState == ProjectName.Core.SimulationState.ActiveSimulation))
            {
                ShowConfirmationModal(
                    "CHANGE SIMULATION TERRAIN?",
                    "Changing the terrain will reset the current simulation environment. The current rover placement and terrain will be cleared.",
                    () =>
                    {
                        if (flowController != null)
                        {
                            flowController.ResetSimulation();
                        }
                        else
                        {
                            ActiveRoverContext.DestroyActiveRover();
                            if (terrainGenerator != null) terrainGenerator.ClearExistingTerrain();
                        }
                        ShowNotification("Environment reset. Ready for new terrain.");
                    }
                );
            }
            else
            {
                if (flowController != null)
                {
                    flowController.ResetSimulation();
                }
                else
                {
                    ActiveRoverContext.DestroyActiveRover();
                    if (terrainGenerator != null) terrainGenerator.ClearExistingTerrain();
                }
                ShowNotification("Ready for new terrain. Select preset and generate.");
            }
        }

        private void OnRootGeometryChanged(GeometryChangedEvent evt)
        {
            if (studioRoot == null) return;

            if (!ProjectName.Core.SceneLoader.IsVREnabled)
            {
                if (!studioRoot.ClassListContains("mode-flat"))
                    studioRoot.AddToClassList("mode-flat");
            }

            bool isCompact = evt.newRect.height < 820f || evt.newRect.width < 1400f;
            if (isCompact)
            {
                if (!studioRoot.ClassListContains("compact-view"))
                    studioRoot.AddToClassList("compact-view");
            }
            else
            {
                if (studioRoot.ClassListContains("compact-view"))
                    studioRoot.RemoveFromClassList("compact-view");
            }
        }

        private void OnDisable()
        {
            ActiveRoverContext.OnRoverActivated -= HandleRoverActivated;
            ActiveRoverContext.OnRoverDestroyed -= HandleRoverDestroyed;
            RoverCameraRig.OnPerspectiveChanged -= HandleRigPerspectiveChanged;
            RoverCameraRig.OnCameraSpeedChanged -= HandleCameraSpeedChanged;
            if (flowController != null)
            {
                flowController.OnSimulationStateChanged -= HandleSimulationStateChanged;
            }

            if (PlanetEnvironmentController.Instance != null)
            {
                PlanetEnvironmentController.Instance.OnProfileApplied -= HandlePlanetaryProfileApplied;
            }
            else if (PlanetaryParameterWriter.Instance != null)
            {
                PlanetaryParameterWriter.Instance.OnProfileApplied -= HandlePlanetaryProfileApplied;
            }

            if (previewTexture != null)
            {
                DestroyImmediate(previewTexture);
                previewTexture = null;
            }
        }
    }
}

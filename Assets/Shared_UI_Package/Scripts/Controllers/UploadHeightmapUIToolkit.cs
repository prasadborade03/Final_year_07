using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;
using SFB;
using ProjectName.Rover;
using ProjectName.Planetary;
using ProjectName.UI;

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
        private bool isStudioMinimized = false;
        private bool isDrivingHudMode = false;
        private string activeRoverId = "husky";
        private string currentDriveMode = "CRUISE";
        private FreeFlyCamera.CameraPerspective currentCamView = FreeFlyCamera.CameraPerspective.Front;

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

        // Col 2: Center Radar & Deploy
        private Image heightmapPreviewImage;
        private VisualElement radarRoverDot;
        private Label labelRadarRange;
        private Button btnBrowseFile;
        private Button btnPresetGale, btnPresetOlympus, btnPresetShackleton, btnPresetValles, btnPresetFractal;
        private Button btnGenerateTerrain;

        private Button btnRoverHusky, btnRoverM20, btnRoverM2020;
        private Label tagHuskyActive, tagM20Active, tagM2020Active;

        // Col 3: Compass, Actuators, Power, Environment
        private VisualElement compassNeedle;
        private Label valCompassBig;

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

        // Bottom Bar: Modes & Perspectives
        private VisualElement groupDriveModes;
        private Label lblDriveGroup;
        private Label lblHelperHint;
        private Button btnModeStop, btnModeCruise, btnModeExplore, btnModePrecision;
        private Button btnCamFront, btnCamRear, btnCamLeft, btnCamRight, btnCamTop;
        private RoverCameraRig.Perspective currentCamPerspective = RoverCameraRig.Perspective.Rear;

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

        private void Awake()
        {
            if (uiDocument == null) uiDocument = GetComponent<UIDocument>();
            if (terrainGenerator == null) terrainGenerator = FindAnyObjectByType<TerrainGenerator>();
            if (flowController == null) flowController = FindAnyObjectByType<SimulationFlowController>();

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
            if (PlanetaryParameterWriter.Instance != null)
            {
                PlanetaryParameterWriter.Instance.OnProfileApplied += HandlePlanetaryProfileApplied;
            }

            BindUIElements();
            RefreshPreview();
            UpdatePresetButtonsUI();
            UpdateRoverSelectionUI();
            UpdateBrushButtonStyles();
            UpdateCurveButtonStyles();
            UpdateMaterialButtonStyles();
            UpdateHistoryUI();
            SetDrivingHudMode(false); // Default to Studio view on open, user can switch to Driving HUD with [Tab]

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
        }

        private void BindUIElements()
        {
            if (uiDocument == null) uiDocument = GetComponent<UIDocument>();
            if (uiDocument == null) return;

            VisualElement root = uiDocument.rootVisualElement;
            if (root == null) return;

            // Roots & Layout Elements
            studioRoot = root.Q<VisualElement>("StudioRoot");
            studioMainGrid = root.Q<VisualElement>("StudioMainGrid");
            colLeft = root.Q<VisualElement>("ColLeft");
            colCenter = root.Q<VisualElement>("ColCenter");
            colRight = root.Q<VisualElement>("ColRight");
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
                btnPlanetBadge.clicked += () =>
                {
                    if (PlanetSelectionUI.Instance != null)
                    {
                        PlanetSelectionUI.Instance.ToggleModal();
                    }
                };
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

            // Col 2: Center Radar
            heightmapPreviewImage = root.Q<Image>("HeightmapPreviewImage");
            radarRoverDot = root.Q<VisualElement>("RadarRoverDot");
            labelRadarRange = root.Q<Label>("LabelRadarRange");
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
            if (btnGenerateTerrain != null) btnGenerateTerrain.clicked += OnGenerateTerrainClicked;

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

            // Col 3: Compass, Actuators, Power, Environment
            compassNeedle = root.Q<VisualElement>("CompassNeedle");
            valCompassBig = root.Q<Label>("ValCompassBig");

            wheelContainer = root.Q<VisualElement>("WheelContainer") ?? root.Q<VisualElement>(className: "wheel-grid-2x2");

            barBattery = root.Q<VisualElement>("BarBattery");
            barMotorTemp = root.Q<VisualElement>("BarMotorTemp");
            valBattery = root.Q<Label>("ValBattery");
            valMotorTemp = root.Q<Label>("ValMotorTemp");

            valEnvGravity = root.Q<Label>("ValEnvGravity");
            valEnvTemp = root.Q<Label>("ValEnvTemp");
            valEnvPress = root.Q<Label>("ValEnvPress");
            valEnvDust = root.Q<Label>("ValEnvDust");

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

            if (btnCamFront != null) btnCamFront.clicked += () => SetCameraPerspective(RoverCameraRig.Perspective.Front);
            if (btnCamRear != null) btnCamRear.clicked += () => SetCameraPerspective(RoverCameraRig.Perspective.Rear);
            if (btnCamLeft != null) btnCamLeft.clicked += () => SetCameraPerspective(RoverCameraRig.Perspective.Left);
            if (btnCamRight != null) btnCamRight.clicked += () => SetCameraPerspective(RoverCameraRig.Perspective.Right);
            if (btnCamTop != null) btnCamTop.clicked += () => SetCameraPerspective(RoverCameraRig.Perspective.Top);

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

            // 2D Canvas Painter
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

            if (isStudioMinimized) return;

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
                }
                else
                {
                    studioRoot.RemoveFromClassList("hud-mode-root");
                }
            }

            if (btnToggleHUD != null)
            {
                btnToggleHUD.text = hudMode ? "STUDIO [Tab]" : "HUD [Tab]";
            }

            SetBtnClass(btnModeDriving, "view-switch-active", hudMode);
            SetBtnClass(btnModeStudio, "view-switch-active", !hudMode);
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

            if (badgeStatus != null) badgeStatus.text = "● ACTIVE";
            if (badgeDriveMode != null && handle.profile != null)
            {
                badgeDriveMode.text = handle.profile.steeringLabel.ToUpper();
            }

            RebuildWheelUI(handle);
            ConfigureDriveBarForRover(handle.profile);
            UpdateLiveTelemetryUI();
        }

        private void HandleRoverDestroyed()
        {
            activeRoverId = "";
            UpdateRoverSelectionUI();
            SetTelemetryStandbyState();
            ConfigureDriveBarForStandby();
        }

        private void SetTelemetryStandbyState()
        {
            if (badgeStatus != null) badgeStatus.text = "● STANDBY";
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
            if (valCompassBig != null) valCompassBig.text = "—";

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

            // Compass Needle & Big Deg
            if (valCompassBig != null) valCompassBig.text = $"{t.headingDeg:000}° {t.headingCardinal}";
            if (compassNeedle != null)
            {
                compassNeedle.style.rotate = new Rotate(t.headingDeg);
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

            // Power & Thermal
            if (barBattery != null) barBattery.style.width = Length.Percent(Mathf.Clamp01(t.batteryPercent / 100f) * 100f);
            if (valBattery != null) valBattery.text = $"{t.batteryPercent:F0}%";

            if (barMotorTemp != null) barMotorTemp.style.width = Length.Percent(Mathf.Clamp01(t.motorTempCelsius / 100f) * 100f);
            if (valMotorTemp != null) valMotorTemp.text = $"{t.motorTempCelsius:F0} °C";

            // Radar Dot Position
            if (radarRoverDot != null)
            {
                var tTerrain = UnityEngine.Terrain.activeTerrain;
                if (tTerrain != null)
                {
                    Vector3 tPos = tTerrain.transform.position;
                    Vector3 tSize = tTerrain.terrainData.size;
                    float normX = Mathf.Clamp01((t.worldPosition.x - tPos.x) / tSize.x);
                    float normZ = Mathf.Clamp01((t.worldPosition.z - tPos.z) / tSize.z);

                    // Radar is 154x154 px
                    float radarPxX = (normX - 0.5f) * 130f;
                    float radarPxY = (0.5f - normZ) * 130f;
                    radarRoverDot.style.translate = new Translate(radarPxX, radarPxY);
                }
            }
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
            var reader = PlanetaryParameterReader.Instance;
            if (reader == null) reader = FindAnyObjectByType<PlanetaryParameterReader>();

            if (valEnvGravity != null)
            {
                float g = reader != null ? reader.GetGravity() : Mathf.Abs(Physics.gravity.y);
                valEnvGravity.text = $"{g:F2} m/s²";
            }

            string pName = "MARS";
            var writer = PlanetaryParameterWriter.Instance ?? FindAnyObjectByType<PlanetaryParameterWriter>();
            if (writer != null && writer.currentProfile != null)
            {
                pName = writer.currentProfile.planetName.ToUpper();
            }

            if (btnPlanetBadge != null)
            {
                btnPlanetBadge.text = pName;
            }

            if (valEnvTemp != null)
            {
                float tempC = -60f;
                if (pName.Contains("MOON")) tempC = -130f;
                else if (pName.Contains("EARTH")) tempC = 15f;
                else if (pName.Contains("TITAN")) tempC = -179f;
                else if (pName.Contains("EUROPA")) tempC = -160f;
                else if (pName.Contains("MERCURY")) tempC = 167f;
                valEnvTemp.text = $"{tempC:+0;-0}°C";
            }

            if (valEnvPress != null)
            {
                float pressKpa = 0.6f;
                if (pName.Contains("MOON") || pName.Contains("EUROPA") || pName.Contains("MERCURY")) pressKpa = 0.0f;
                else if (pName.Contains("EARTH")) pressKpa = 101.3f;
                else if (pName.Contains("TITAN")) pressKpa = 146.7f;
                valEnvPress.text = $"{pressKpa:F1} kPa";
            }

            if (valEnvDust != null)
            {
                float wind = reader != null ? reader.GetWindSpeed() : 4.5f;
                bool hasDust = (pName.Contains("MARS") || pName.Contains("TITAN")) && (wind > 8f || RenderSettings.fogDensity > 0.02f);
                valEnvDust.text = hasDust ? "Active" : "No";
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
                if (lblHelperHint != null) lblHelperHint.text = "[W,A,S,D] Drive | [M] Steer Mode | [Space] Clearance | [V] Cam | [Tab] HUD";
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
                    if (lblHelperHint != null) lblHelperHint.text = "[W,A,S,D] Drive | [Q,E] Knees | [1..4] Speed | [V] Cam | [Tab] HUD";
                }
                else
                {
                    if (lblHelperHint != null) lblHelperHint.text = "[W,A,S,D] Drive | [1..4] Speed | [V] Cam Presets | [Tab] HUD Toggle";
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

            if (btnCamFront != null) btnCamFront.text = (currentCamPerspective == RoverCameraRig.Perspective.Front) ? "● FRONT" : "FRONT";
            if (btnCamRear != null) btnCamRear.text = (currentCamPerspective == RoverCameraRig.Perspective.Rear) ? "● REAR" : "REAR";
            if (btnCamLeft != null) btnCamLeft.text = (currentCamPerspective == RoverCameraRig.Perspective.Left) ? "● LEFT" : "LEFT";
            if (btnCamRight != null) btnCamRight.text = (currentCamPerspective == RoverCameraRig.Perspective.Right) ? "● RIGHT" : "RIGHT";
            if (btnCamTop != null) btnCamTop.text = (currentCamPerspective == RoverCameraRig.Perspective.Top) ? "● TOP" : "TOP";
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
            if (heightmapPainterImage == null || currentHeights == null) return;
            isPainting = true;
            strokeTouchedCells.Clear();
            PaintAtPointerPosition(evt.localPosition);
        }

        private void OnPainterPointerMove(PointerMoveEvent evt)
        {
            if (!isPainting) return;
            PaintAtPointerPosition(evt.localPosition);
        }

        private void OnPainterPointerUp(PointerUpEvent evt)
        {
            EndStroke();
        }

        private void OnPainterPointerLeave(PointerLeaveEvent evt)
        {
            EndStroke();
        }

        private void PaintAtPointerPosition(Vector2 localPos)
        {
            if (heightmapPainterImage == null || currentHeights == null) return;

            float w = heightmapPainterImage.resolvedStyle.width;
            float h = heightmapPainterImage.resolvedStyle.height;
            if (w <= 0f || h <= 0f) return;

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

        private void HandlePlanetaryProfileApplied(PlanetaryProfile profile)
        {
            if (!followPlanetProfile || profile == null) return;
            SyncMaterialWithProfile(profile);
        }

        private void SyncMaterialWithCurrentPlanet()
        {
            var writer = PlanetaryParameterWriter.Instance;
            if (writer != null && writer.currentProfile != null)
            {
                SyncMaterialWithProfile(writer.currentProfile);
            }
            else
            {
                SetMaterialInternal(PlanetaryMaterialType.MartianDust, isManualOverride: false);
            }
        }

        private void SyncMaterialWithProfile(PlanetaryProfile profile)
        {
            string pName = profile.planetName.ToLowerInvariant();
            PlanetaryMaterialType targetMat;
            if (pName.Contains("moon")) targetMat = PlanetaryMaterialType.LunarRegolith;
            else if (pName.Contains("titan") || pName.Contains("venus")) targetMat = PlanetaryMaterialType.VolcanicBasalt;
            else if (pName.Contains("earth") || pName.Contains("polar")) targetMat = PlanetaryMaterialType.PolarIce;
            else targetMat = PlanetaryMaterialType.MartianDust;

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
            try
            {
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

                ShowNotification("Terrain generated & collider synced.");
                Debug.Log("[Studio] New planetary terrain generated successfully. Old terrain removed.");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[Studio] Terrain generation exception: {ex.Message}\n{ex.StackTrace}");
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
                return;
            }

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

        private void OnDisable()
        {
            ActiveRoverContext.OnRoverActivated -= HandleRoverActivated;
            ActiveRoverContext.OnRoverDestroyed -= HandleRoverDestroyed;
            RoverCameraRig.OnPerspectiveChanged -= HandleRigPerspectiveChanged;
            if (PlanetaryParameterWriter.Instance != null)
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

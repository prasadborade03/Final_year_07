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
        private Button btnModeStop, btnModeCruise, btnModeExplore, btnModePrecision;
        private Button btnCamFront, btnCamRear, btnCamLeft, btnCamRight, btnCamTop;

        private void Awake()
        {
            if (uiDocument == null) uiDocument = GetComponent<UIDocument>();
            if (terrainGenerator == null) terrainGenerator = FindAnyObjectByType<TerrainGenerator>();
            if (flowController == null) flowController = FindAnyObjectByType<SimulationFlowController>();

            currentHeights = HeightmapLoader.GeneratePresetHeights(
                HeightmapPreset.GaleCrater,
                tuningConfig.resolution,
                tuningConfig.smoothingFactor,
                tuningConfig.heightCurve
            );
        }

        private void OnEnable()
        {
            ActiveRoverContext.OnRoverActivated += HandleRoverActivated;
            ActiveRoverContext.OnRoverDestroyed += HandleRoverDestroyed;

            BindUIElements();
            RefreshPreview();
            UpdatePresetButtonsUI();
            UpdateRoverSelectionUI();
            UpdateDriveModeUI();
            UpdateCameraButtonsUI();
            SetDrivingHudMode(false); // Default to Studio view on open, user can switch to Driving HUD with [Tab]

            if (ActiveRoverContext.HasActiveRover)
            {
                HandleRoverActivated(ActiveRoverContext.Current);
            }
            else
            {
                SetTelemetryStandbyState();
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

            // Bottom Bar: Modes
            btnModeStop = root.Q<Button>("BtnModeStop");
            btnModeCruise = root.Q<Button>("BtnModeCruise");
            btnModeExplore = root.Q<Button>("BtnModeExplore");
            btnModePrecision = root.Q<Button>("BtnModePrecision");

            if (btnModeStop != null) btnModeStop.clicked += () => SetDriveMode("STOP");
            if (btnModeCruise != null) btnModeCruise.clicked += () => SetDriveMode("CRUISE");
            if (btnModeExplore != null) btnModeExplore.clicked += () => SetDriveMode("EXPLORE");
            if (btnModePrecision != null) btnModePrecision.clicked += () => SetDriveMode("PRECISION");

            // Bottom Bar: Camera Perspectives
            btnCamFront = root.Q<Button>("BtnCamFront");
            btnCamRear = root.Q<Button>("BtnCamRear");
            btnCamLeft = root.Q<Button>("BtnCamLeft");
            btnCamRight = root.Q<Button>("BtnCamRight");
            btnCamTop = root.Q<Button>("BtnCamTop");

            if (btnCamFront != null) btnCamFront.clicked += () => SetCameraPerspective(FreeFlyCamera.CameraPerspective.Front);
            if (btnCamRear != null) btnCamRear.clicked += () => SetCameraPerspective(FreeFlyCamera.CameraPerspective.Rear);
            if (btnCamLeft != null) btnCamLeft.clicked += () => SetCameraPerspective(FreeFlyCamera.CameraPerspective.Left);
            if (btnCamRight != null) btnCamRight.clicked += () => SetCameraPerspective(FreeFlyCamera.CameraPerspective.Right);
            if (btnCamTop != null) btnCamTop.clicked += () => SetCameraPerspective(FreeFlyCamera.CameraPerspective.Top);
        }

        private void Update()
        {
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
            UpdateLiveTelemetryUI();
        }

        private void HandleRoverDestroyed()
        {
            activeRoverId = "";
            UpdateRoverSelectionUI();
            SetTelemetryStandbyState();
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
        // Drive Modes & Camera Controls
        // -------------------------------------------------------------
        public void SetDriveMode(string mode)
        {
            currentDriveMode = mode.ToUpperInvariant();
            UpdateDriveModeUI();

            var husky = FindAnyObjectByType<HuskyController>();
            if (husky != null)
            {
                switch (currentDriveMode)
                {
                    case "STOP": husky.maxWheelSpeed = 0f; break;
                    case "CRUISE": husky.maxWheelSpeed = 400f; break;
                    case "EXPLORE": husky.maxWheelSpeed = 650f; break;
                    case "PRECISION": husky.maxWheelSpeed = 160f; break;
                }
            }

            var huskyOld = FindAnyObjectByType<RoverController_husky>();
            if (huskyOld != null)
            {
                switch (currentDriveMode)
                {
                    case "STOP": huskyOld.maxWheelSpeed = 0f; break;
                    case "CRUISE": huskyOld.maxWheelSpeed = 400f; break;
                    case "EXPLORE": huskyOld.maxWheelSpeed = 650f; break;
                    case "PRECISION": huskyOld.maxWheelSpeed = 160f; break;
                }
            }

            var m20 = FindAnyObjectByType<RoverController_m20>();
            if (m20 != null)
            {
                switch (currentDriveMode)
                {
                    case "STOP": m20.maxWheelSpeed = 0f; break;
                    case "CRUISE": m20.maxWheelSpeed = 25f; break;
                    case "EXPLORE": m20.maxWheelSpeed = 45f; break;
                    case "PRECISION": m20.maxWheelSpeed = 10f; break;
                }
            }

            var m2020 = FindAnyObjectByType<RoverController_m2020>();
            if (m2020 != null)
            {
                switch (currentDriveMode)
                {
                    case "STOP": m2020.maxSpeedDegPerSec = 0f; break;
                    case "CRUISE": m2020.maxSpeedDegPerSec = 240f; break;
                    case "EXPLORE": m2020.maxSpeedDegPerSec = 400f; break;
                    case "PRECISION": m2020.maxSpeedDegPerSec = 100f; break;
                }
            }

            Debug.Log($"[Studio] Rover drive mode set to: {currentDriveMode}");
        }

        private void UpdateDriveModeUI()
        {
            SetBtnClass(btnModeStop, "mode-pill-active", currentDriveMode == "STOP");
            SetBtnClass(btnModeCruise, "mode-pill-active", currentDriveMode == "CRUISE");
            SetBtnClass(btnModeExplore, "mode-pill-active", currentDriveMode == "EXPLORE");
            SetBtnClass(btnModePrecision, "mode-pill-active", currentDriveMode == "PRECISION");
        }

        public void SetCameraPerspective(FreeFlyCamera.CameraPerspective view)
        {
            currentCamView = view;
            UpdateCameraButtonsUI();

            if (FreeFlyCamera.Instance != null)
            {
                FreeFlyCamera.Instance.SetCameraPerspective(view);
            }
        }

        private void UpdateCameraButtonsUI()
        {
            SetBtnClass(btnCamFront, "mode-pill-active", currentCamView == FreeFlyCamera.CameraPerspective.Front);
            SetBtnClass(btnCamRear, "mode-pill-active", currentCamView == FreeFlyCamera.CameraPerspective.Rear);
            SetBtnClass(btnCamLeft, "mode-pill-active", currentCamView == FreeFlyCamera.CameraPerspective.Left);
            SetBtnClass(btnCamRight, "mode-pill-active", currentCamView == FreeFlyCamera.CameraPerspective.Right);
            SetBtnClass(btnCamTop, "mode-pill-active", currentCamView == FreeFlyCamera.CameraPerspective.Top);

            if (btnCamFront != null) btnCamFront.text = (currentCamView == FreeFlyCamera.CameraPerspective.Front) ? "● FRONT" : "FRONT";
            if (btnCamRear != null) btnCamRear.text = (currentCamView == FreeFlyCamera.CameraPerspective.Rear) ? "● REAR" : "REAR";
            if (btnCamLeft != null) btnCamLeft.text = (currentCamView == FreeFlyCamera.CameraPerspective.Left) ? "● LEFT" : "LEFT";
            if (btnCamRight != null) btnCamRight.text = (currentCamView == FreeFlyCamera.CameraPerspective.Right) ? "● RIGHT" : "RIGHT";
            if (btnCamTop != null) btnCamTop.text = (currentCamView == FreeFlyCamera.CameraPerspective.Top) ? "● TOP" : "TOP";
        }

        // -------------------------------------------------------------
        // Presets & Terrain Generation
        // -------------------------------------------------------------
        private void SelectPreset(HeightmapPreset preset)
        {
            currentPreset = preset;
            currentHeights = HeightmapLoader.GeneratePresetHeights(
                preset,
                tuningConfig.resolution,
                tuningConfig.smoothingFactor,
                tuningConfig.heightCurve
            );

            UpdatePresetButtonsUI();
            RefreshPreview();
        }

        private void UpdatePresetButtonsUI()
        {
            SetBtnClass(btnPresetGale, "preset-active", currentPreset == HeightmapPreset.GaleCrater);
            SetBtnClass(btnPresetOlympus, "preset-active", currentPreset == HeightmapPreset.OlympusMons);
            SetBtnClass(btnPresetShackleton, "preset-active", currentPreset == HeightmapPreset.ShackletonCrater);
            SetBtnClass(btnPresetValles, "preset-active", currentPreset == HeightmapPreset.VallesMarineris);
            SetBtnClass(btnPresetFractal, "preset-active", currentPreset == HeightmapPreset.ProceduralFractal);
        }

        private void OnBrowseFileClicked()
        {
            var extensions = new[] { new ExtensionFilter("Heightmap Images", "png", "jpg", "jpeg") };
            string[] paths = StandaloneFileBrowser.OpenFilePanel("Select Planetary Heightmap", "", extensions, false);

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
                    UpdatePresetButtonsUI();
                    RefreshPreview();
                }
            }
        }

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
                        tuningConfig.heightCurve
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
                if (terrain != null && flowController != null)
                {
                    flowController.OnTerrainReady();
                }

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
        }

        // -------------------------------------------------------------
        // Rover Deployment
        // -------------------------------------------------------------
        private void DeployRover(string roverId)
        {
            activeRoverId = roverId;
            UpdateRoverSelectionUI();

            if (flowController == null) flowController = FindAnyObjectByType<SimulationFlowController>();
            if (flowController == null) return;

            if (roverId == "husky") flowController.OnSelectHusky();
            else if (roverId == "m20") flowController.OnSelectM20();
            else if (roverId == "m2020") flowController.OnSelectM2020();

            // Auto-switch to HUD mode so 3D viewport is clear and UI doesn't block clicks/driving
            SetDrivingHudMode(true);
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

            if (previewTexture != null)
            {
                DestroyImmediate(previewTexture);
                previewTexture = null;
            }
        }
    }
}

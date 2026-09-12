using UnityEngine;
using UnityEngine.UIElements;
using SFB;

namespace ProjectName.Terrain
{
    /// <summary>
    /// Modern Unity UI Toolkit Controller for VR Rover Digital Twin Studio & Workbench.
    /// Manages real-time pre-import heightmap tuning, interactive 2D canvas painting,
    /// planetary surface material selection, rover deployment, and camera speed controls.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class UploadHeightmapUIToolkit : MonoBehaviour
    {
        [Header("System References")]
        public TerrainGenerator terrainGenerator;
        public SimulationFlowController flowController;
        public UIDocument uiDocument;

        [Header("Tuning Configuration")]
        public TerrainTuningConfig tuningConfig = new TerrainTuningConfig();

        // Internal Working State
        private float[,] currentHeights;
        private Texture2D previewTexture;
        private BrushMode currentBrushMode = BrushMode.Raise;
        private float brushRadius = 0.08f;
        private float brushStrength = 0.4f;
        private bool isPainting = false;
        private HeightmapPreset currentPreset = HeightmapPreset.GaleCrater;

        // UI Element References
        private Label stateBadge;
        private Label labelGuide;
        private Image previewImage;

        private Slider sliderMaxHeight;
        private Label labelMaxHeight;
        private Slider sliderBaseOffset;
        private Label labelBaseOffset;
        private Slider sliderSmoothing;
        private Label labelSmoothing;
        private DropdownField dropdownResolution;

        private Button btnGenerateTerrain;
        private Button btnBrowseFile;

        // Brush Buttons
        private Button btnBrushRaise, btnBrushLower, btnBrushSmooth, btnBrushFlatten;
        private Slider sliderBrushRadius, sliderBrushStrength;

        // Preset Buttons
        private Button btnPresetGale, btnPresetOlympus, btnPresetShackleton, btnPresetValles, btnPresetPolar, btnPresetFractal;

        // Curve Buttons
        private Button btnCurveLinear, btnCurveExponential, btnCurveRidge, btnCurveBasin;

        // Material Buttons
        private Button btnMatMartian, btnMatLunar, btnMatBasalt, btnMatIce, btnMatCanyon, btnMatWireframe, btnMatNormal;

        // Rover Buttons
        private Button btnRoverHusky, btnRoverM20, btnRoverM2020;

        // Camera Speed Controller
        private Label labelSpeedometer;
        private Slider sliderCameraSpeed;
        private Label labelCameraSpeedVal;
        private Button btnSpeedPrecision, btnSpeedStandard, btnSpeedFast, btnSpeedWarp;

        private void Awake()
        {
            if (uiDocument == null) uiDocument = GetComponent<UIDocument>();
            if (terrainGenerator == null) terrainGenerator = FindAnyObjectByType<TerrainGenerator>();
            if (flowController == null) flowController = FindAnyObjectByType<SimulationFlowController>();

            // Initialize default heights with Gale Crater preset
            currentHeights = HeightmapLoader.GeneratePresetHeights(
                HeightmapPreset.GaleCrater,
                tuningConfig.resolution,
                tuningConfig.smoothingFactor,
                tuningConfig.heightCurve
            );
        }

        private void OnEnable()
        {
            if (uiDocument == null) uiDocument = GetComponent<UIDocument>();
            if (uiDocument == null) return;

            VisualElement root = uiDocument.rootVisualElement;
            if (root == null) return;

            // Header & Status
            stateBadge = root.Q<Label>("StateBadge");
            labelGuide = root.Q<Label>("LabelGuide");

            // 2D Preview Canvas
            previewImage = root.Q<Image>("HeightmapPreviewImage");
            if (previewImage != null)
            {
                previewImage.RegisterCallback<PointerDownEvent>(OnCanvasPointerDown);
                previewImage.RegisterCallback<PointerMoveEvent>(OnCanvasPointerMove);
                previewImage.RegisterCallback<PointerUpEvent>(OnCanvasPointerUp);
                previewImage.RegisterCallback<PointerLeaveEvent>(OnCanvasPointerLeave);
            }

            // Brush Tools
            btnBrushRaise = root.Q<Button>("BtnBrushRaise");
            btnBrushLower = root.Q<Button>("BtnBrushLower");
            btnBrushSmooth = root.Q<Button>("BtnBrushSmooth");
            btnBrushFlatten = root.Q<Button>("BtnBrushFlatten");

            if (btnBrushRaise != null) btnBrushRaise.clicked += () => SetBrushMode(BrushMode.Raise);
            if (btnBrushLower != null) btnBrushLower.clicked += () => SetBrushMode(BrushMode.Lower);
            if (btnBrushSmooth != null) btnBrushSmooth.clicked += () => SetBrushMode(BrushMode.Smooth);
            if (btnBrushFlatten != null) btnBrushFlatten.clicked += () => SetBrushMode(BrushMode.Flatten);

            sliderBrushRadius = root.Q<Slider>("SliderBrushRadius");
            if (sliderBrushRadius != null)
            {
                sliderBrushRadius.value = brushRadius;
                sliderBrushRadius.RegisterValueChangedCallback(evt => brushRadius = evt.newValue);
            }

            sliderBrushStrength = root.Q<Slider>("SliderBrushStrength");
            if (sliderBrushStrength != null)
            {
                sliderBrushStrength.value = brushStrength;
                sliderBrushStrength.RegisterValueChangedCallback(evt => brushStrength = evt.newValue);
            }

            // Tuning Sliders
            sliderMaxHeight = root.Q<Slider>("SliderMaxHeight");
            labelMaxHeight = root.Q<Label>("LabelMaxHeight");
            if (sliderMaxHeight != null)
            {
                sliderMaxHeight.value = tuningConfig.maxHeight;
                sliderMaxHeight.RegisterValueChangedCallback(evt =>
                {
                    tuningConfig.maxHeight = evt.newValue;
                    if (labelMaxHeight != null) labelMaxHeight.text = $"{evt.newValue:F1} m";
                });
            }

            sliderBaseOffset = root.Q<Slider>("SliderBaseOffset");
            labelBaseOffset = root.Q<Label>("LabelBaseOffset");
            if (sliderBaseOffset != null)
            {
                sliderBaseOffset.value = tuningConfig.baseOffset;
                sliderBaseOffset.RegisterValueChangedCallback(evt =>
                {
                    tuningConfig.baseOffset = evt.newValue;
                    if (labelBaseOffset != null) labelBaseOffset.text = $"{evt.newValue:F1} m";
                });
            }

            sliderSmoothing = root.Q<Slider>("SliderSmoothing");
            labelSmoothing = root.Q<Label>("LabelSmoothing");
            if (sliderSmoothing != null)
            {
                sliderSmoothing.value = tuningConfig.smoothingFactor;
                sliderSmoothing.RegisterValueChangedCallback(evt =>
                {
                    tuningConfig.smoothingFactor = evt.newValue;
                    if (labelSmoothing != null) labelSmoothing.text = $"{evt.newValue:F1}";
                    RegenerateCurrentHeights();
                });
            }

            dropdownResolution = root.Q<DropdownField>("DropdownResolution");
            if (dropdownResolution != null)
            {
                dropdownResolution.RegisterValueChangedCallback(evt =>
                {
                    int parsedRes = 513;
                    if (evt.newValue.Contains("33")) parsedRes = 33;
                    else if (evt.newValue.Contains("65")) parsedRes = 65;
                    else if (evt.newValue.Contains("129")) parsedRes = 129;
                    else if (evt.newValue.Contains("257")) parsedRes = 257;
                    else if (evt.newValue.Contains("513")) parsedRes = 513;
                    else if (evt.newValue.Contains("1025")) parsedRes = 1025;

                    tuningConfig.resolution = parsedRes;
                    RegenerateCurrentHeights();
                });
            }

            // Presets
            btnPresetGale = root.Q<Button>("BtnPresetGale");
            btnPresetOlympus = root.Q<Button>("BtnPresetOlympus");
            btnPresetShackleton = root.Q<Button>("BtnPresetShackleton");
            btnPresetValles = root.Q<Button>("BtnPresetValles");
            btnPresetPolar = root.Q<Button>("BtnPresetPolar");
            btnPresetFractal = root.Q<Button>("BtnPresetFractal");

            if (btnPresetGale != null) btnPresetGale.clicked += () => SelectPreset(HeightmapPreset.GaleCrater);
            if (btnPresetOlympus != null) btnPresetOlympus.clicked += () => SelectPreset(HeightmapPreset.OlympusMons);
            if (btnPresetShackleton != null) btnPresetShackleton.clicked += () => SelectPreset(HeightmapPreset.ShackletonCrater);
            if (btnPresetValles != null) btnPresetValles.clicked += () => SelectPreset(HeightmapPreset.VallesMarineris);
            if (btnPresetPolar != null) btnPresetPolar.clicked += () => SelectPreset(HeightmapPreset.PolarIce);
            if (btnPresetFractal != null) btnPresetFractal.clicked += () => SelectPreset(HeightmapPreset.ProceduralFractal);

            btnBrowseFile = root.Q<Button>("BtnBrowseFile");
            if (btnBrowseFile != null) btnBrowseFile.clicked += OnBrowseFileClicked;

            // Height Remapping Curves
            btnCurveLinear = root.Q<Button>("BtnCurveLinear");
            btnCurveExponential = root.Q<Button>("BtnCurveExponential");
            btnCurveRidge = root.Q<Button>("BtnCurveRidge");
            btnCurveBasin = root.Q<Button>("BtnCurveBasin");

            if (btnCurveLinear != null) btnCurveLinear.clicked += () => SetHeightCurve(HeightRemapCurve.Linear);
            if (btnCurveExponential != null) btnCurveExponential.clicked += () => SetHeightCurve(HeightRemapCurve.Exponential);
            if (btnCurveRidge != null) btnCurveRidge.clicked += () => SetHeightCurve(HeightRemapCurve.RidgePeak);
            if (btnCurveBasin != null) btnCurveBasin.clicked += () => SetHeightCurve(HeightRemapCurve.BasinInversion);

            // Surface Materials
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

            // Generate Button
            btnGenerateTerrain = root.Q<Button>("BtnGenerateTerrain");
            if (btnGenerateTerrain != null) btnGenerateTerrain.clicked += OnGenerateTerrainClicked;

            // Rover Buttons
            btnRoverHusky = root.Q<Button>("BtnRoverHusky");
            btnRoverM20 = root.Q<Button>("BtnRoverM20");
            btnRoverM2020 = root.Q<Button>("BtnRoverM2020");

            if (btnRoverHusky != null) btnRoverHusky.clicked += () => { if (flowController != null) flowController.OnSelectHusky(); };
            if (btnRoverM20 != null) btnRoverM20.clicked += () => { if (flowController != null) flowController.OnSelectM20(); };
            if (btnRoverM2020 != null) btnRoverM2020.clicked += () => { if (flowController != null) flowController.OnSelectM2020(); };

            // Camera Speed Controller
            labelSpeedometer = root.Q<Label>("LabelSpeedometer");
            sliderCameraSpeed = root.Q<Slider>("SliderCameraSpeed");
            labelCameraSpeedVal = root.Q<Label>("LabelCameraSpeedVal");

            btnSpeedPrecision = root.Q<Button>("BtnSpeedPrecision");
            btnSpeedStandard = root.Q<Button>("BtnSpeedStandard");
            btnSpeedFast = root.Q<Button>("BtnSpeedFast");
            btnSpeedWarp = root.Q<Button>("BtnSpeedWarp");

            if (sliderCameraSpeed != null)
            {
                float initialSpeed = FreeFlyCamera.Instance != null ? FreeFlyCamera.Instance.movementSpeed : 10.0f;
                sliderCameraSpeed.value = initialSpeed;
                if (labelCameraSpeedVal != null) labelCameraSpeedVal.text = $"{initialSpeed:F1} m/s";

                sliderCameraSpeed.RegisterValueChangedCallback(evt =>
                {
                    if (labelCameraSpeedVal != null) labelCameraSpeedVal.text = $"{evt.newValue:F1} m/s";
                    if (FreeFlyCamera.Instance != null) FreeFlyCamera.Instance.SetMovementSpeed(evt.newValue);
                    UpdateSpeedPresetStyles(evt.newValue);
                });
            }

            if (btnSpeedPrecision != null) btnSpeedPrecision.clicked += () => { if (FreeFlyCamera.Instance != null) FreeFlyCamera.Instance.SetPrecisionPreset(); };
            if (btnSpeedStandard != null) btnSpeedStandard.clicked += () => { if (FreeFlyCamera.Instance != null) FreeFlyCamera.Instance.SetStandardPreset(); };
            if (btnSpeedFast != null) btnSpeedFast.clicked += () => { if (FreeFlyCamera.Instance != null) FreeFlyCamera.Instance.SetFastPreset(); };
            if (btnSpeedWarp != null) btnSpeedWarp.clicked += () => { if (FreeFlyCamera.Instance != null) FreeFlyCamera.Instance.SetWarpPreset(); };

            FreeFlyCamera.OnCameraSpeedChanged += OnCameraSpeedUpdated;

            // Flow Controller Events
            if (flowController != null)
            {
                flowController.onGuideTextChanged += text => { if (labelGuide != null) labelGuide.text = text; };
                flowController.onStateChanged += state =>
                {
                    if (stateBadge != null)
                    {
                        stateBadge.text = state.ToUpper();
                    }
                };
            }

            RefreshPreview();
        }

        private void OnDisable()
        {
            FreeFlyCamera.OnCameraSpeedChanged -= OnCameraSpeedUpdated;

            if (previewTexture != null)
            {
                DestroyImmediate(previewTexture);
                previewTexture = null;
            }
        }

        private void OnCameraSpeedUpdated(float baseSpeed, float activeSpeed)
        {
            if (labelSpeedometer != null)
            {
                labelSpeedometer.text = $"{baseSpeed:F1} m/s";
            }

            if (sliderCameraSpeed != null && Mathf.Abs(sliderCameraSpeed.value - baseSpeed) > 0.1f)
            {
                sliderCameraSpeed.SetValueWithoutNotify(baseSpeed);
                if (labelCameraSpeedVal != null) labelCameraSpeedVal.text = $"{baseSpeed:F1} m/s";
                UpdateSpeedPresetStyles(baseSpeed);
            }
        }

        private void UpdateSpeedPresetStyles(float currentSpeed)
        {
            SetBtnClass(btnSpeedPrecision, "speed-preset-active", Mathf.Abs(currentSpeed - 2f) < 0.8f);
            SetBtnClass(btnSpeedStandard, "speed-preset-active", Mathf.Abs(currentSpeed - 10f) < 1.5f || Mathf.Abs(currentSpeed - 15f) < 1.5f);
            SetBtnClass(btnSpeedFast, "speed-preset-active", Mathf.Abs(currentSpeed - 50f) < 3.0f);
            SetBtnClass(btnSpeedWarp, "speed-preset-active", Mathf.Abs(currentSpeed - 120f) < 5.0f);
        }

        // -------------------------------------------------------------
        // Presets & File Upload
        // -------------------------------------------------------------
        private void SelectPreset(HeightmapPreset preset)
        {
            currentPreset = preset;
            RegenerateCurrentHeights();
            UpdatePresetButtonStyles();
        }

        private void RegenerateCurrentHeights()
        {
            if (currentPreset == HeightmapPreset.CustomImage) return;

            currentHeights = HeightmapLoader.GeneratePresetHeights(
                currentPreset,
                tuningConfig.resolution,
                tuningConfig.smoothingFactor,
                tuningConfig.heightCurve
            );

            RefreshPreview();
        }

        private void OnBrowseFileClicked()
        {
            var extensions = new[] { new ExtensionFilter("Heightmap Images", "png", "jpg", "jpeg") };
            string[] paths = StandaloneFileBrowser.OpenFilePanel("Select Heightmap Image", "", extensions, false);

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
                    UpdatePresetButtonStyles();
                    RefreshPreview();
                    if (labelGuide != null) labelGuide.text = $"Loaded heightmap: {System.IO.Path.GetFileName(path)}";
                }
            }
        }

        // -------------------------------------------------------------
        // Interactive 2D Canvas Painter
        // -------------------------------------------------------------
        private void SetBrushMode(BrushMode mode)
        {
            currentBrushMode = mode;
            UpdateBrushButtonStyles();
        }

        private void OnCanvasPointerDown(PointerDownEvent evt)
        {
            if (previewImage == null || currentHeights == null) return;
            isPainting = true;
            PaintAtPointerPosition(evt.localPosition);
        }

        private void OnCanvasPointerMove(PointerMoveEvent evt)
        {
            if (!isPainting) return;
            PaintAtPointerPosition(evt.localPosition);
        }

        private void OnCanvasPointerUp(PointerUpEvent evt)
        {
            isPainting = false;
        }

        private void OnCanvasPointerLeave(PointerLeaveEvent evt)
        {
            isPainting = false;
        }

        private void PaintAtPointerPosition(Vector2 localPos)
        {
            if (previewImage == null || currentHeights == null) return;

            float w = previewImage.resolvedStyle.width;
            float h = previewImage.resolvedStyle.height;
            if (w <= 0 || h <= 0) return;

            float u = Mathf.Clamp01(localPos.x / w);
            // Invert Y because texture UV (0,0) is bottom-left, but UI origin is top-left
            float v = Mathf.Clamp01(1.0f - (localPos.y / h));

            HeightmapLoader.ApplyBrush(
                currentHeights,
                tuningConfig.resolution,
                new Vector2(u, v),
                currentBrushMode,
                brushRadius,
                brushStrength
            );

            RefreshPreview();
        }

        // -------------------------------------------------------------
        // Height Remap Curves & Surface Material
        // -------------------------------------------------------------
        private void SetHeightCurve(HeightRemapCurve curve)
        {
            tuningConfig.heightCurve = curve;
            RegenerateCurrentHeights();
            UpdateCurveButtonStyles();
        }

        private void SetMaterial(PlanetaryMaterialType mat)
        {
            tuningConfig.materialType = mat;
            UpdateMaterialButtonStyles();
            RefreshPreview();

            // If terrain is already generated, apply material live
            if (terrainGenerator != null)
            {
                var currentTerrain = terrainGenerator.GetCurrentTerrain();
                if (currentTerrain != null && terrainGenerator.materialManager != null)
                {
                    terrainGenerator.materialManager.ApplyMaterial(currentTerrain, mat);
                }
            }
        }

        // -------------------------------------------------------------
        // Generate Terrain Action
        // -------------------------------------------------------------
        private void OnGenerateTerrainClicked()
        {
            if (currentHeights == null) return;

            UnityEngine.Terrain terrain = terrainGenerator.GenerateTerrain(currentHeights, tuningConfig);
            if (terrain != null && flowController != null)
            {
                flowController.OnTerrainReady();
            }
        }

        // -------------------------------------------------------------
        // Preview Refresh
        // -------------------------------------------------------------
        private void RefreshPreview()
        {
            if (currentHeights == null || previewImage == null) return;

            previewTexture = HeightmapLoader.CreatePreviewTexture(
                currentHeights,
                tuningConfig.resolution,
                tuningConfig.materialType,
                previewTexture
            );

            previewImage.image = previewTexture;
        }

        // -------------------------------------------------------------
        // CSS Style Helpers
        // -------------------------------------------------------------
        private void UpdateBrushButtonStyles()
        {
            SetBtnClass(btnBrushRaise, "brush-btn-active", currentBrushMode == BrushMode.Raise);
            SetBtnClass(btnBrushLower, "brush-btn-active", currentBrushMode == BrushMode.Lower);
            SetBtnClass(btnBrushSmooth, "brush-btn-active", currentBrushMode == BrushMode.Smooth);
            SetBtnClass(btnBrushFlatten, "brush-btn-active", currentBrushMode == BrushMode.Flatten);
        }

        private void UpdatePresetButtonStyles()
        {
            SetBtnClass(btnPresetGale, "preset-btn-active", currentPreset == HeightmapPreset.GaleCrater);
            SetBtnClass(btnPresetOlympus, "preset-btn-active", currentPreset == HeightmapPreset.OlympusMons);
            SetBtnClass(btnPresetShackleton, "preset-btn-active", currentPreset == HeightmapPreset.ShackletonCrater);
            SetBtnClass(btnPresetValles, "preset-btn-active", currentPreset == HeightmapPreset.VallesMarineris);
            SetBtnClass(btnPresetPolar, "preset-btn-active", currentPreset == HeightmapPreset.PolarIce);
            SetBtnClass(btnPresetFractal, "preset-btn-active", currentPreset == HeightmapPreset.ProceduralFractal);
        }

        private void UpdateCurveButtonStyles()
        {
            SetBtnClass(btnCurveLinear, "curve-btn-active", tuningConfig.heightCurve == HeightRemapCurve.Linear);
            SetBtnClass(btnCurveExponential, "curve-btn-active", tuningConfig.heightCurve == HeightRemapCurve.Exponential);
            SetBtnClass(btnCurveRidge, "curve-btn-active", tuningConfig.heightCurve == HeightRemapCurve.RidgePeak);
            SetBtnClass(btnCurveBasin, "curve-btn-active", tuningConfig.heightCurve == HeightRemapCurve.BasinInversion);
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

        private void SetBtnClass(Button btn, string className, bool active)
        {
            if (btn == null) return;
            if (active) btn.AddToClassList(className);
            else btn.RemoveFromClassList(className);
        }
    }
}

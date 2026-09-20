using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ProjectName.Planetary;

namespace ProjectName.UI
{
    /// <summary>
    /// Interactive Planet Selection Modal HUD.
    /// Toggleable via the [P] key or UI buttons.
    /// Dynamically discovers all PlanetaryProfile ScriptableObjects from Resources,
    /// shows a live parameter diff against current Unity engine values,
    /// and writes the selected planetary profile directly into Unity engine systems.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlanetSelectionUI : MonoBehaviour
    {
        [Header("Controls")]
        public KeyCode toggleKey = KeyCode.P;
        public KeyCode alternateCloseKey = KeyCode.Escape;

        [Header("UI Root References")]
        public GameObject modalRoot;
        public Transform planetListContainer;
        public TextMeshProUGUI diffInspectorText;
        public TextMeshProUGUI selectedPlanetTitleText;
        public TextMeshProUGUI selectedPlanetDescText;

        [Header("Action Buttons")]
        public Button btnApplyEnvironment;
        public Button btnApplyAndRespawn;
        public Button btnClose;

        [Header("Active Selection")]
        public PlanetaryProfile selectedProfile;
        public List<PlanetaryProfile> availableProfiles = new List<PlanetaryProfile>();

        public static PlanetSelectionUI Instance { get; private set; }

        private bool isOpen = false;
        private readonly StringBuilder sb = new StringBuilder(1024);

        private void Awake()
        {
            if (Instance == null) Instance = this;
        }

        private void Start()
        {
            LoadProfiles();
            if (modalRoot == null)
            {
                BuildProceduralModal();
            }

            SetModalVisible(false);

            // Select default profile if available
            if (availableProfiles.Count > 0)
            {
                var def = availableProfiles.Find(p => p.isDefault);
                SelectProfile(def != null ? def : availableProfiles[0]);
            }
        }

        private void Update()
        {
            if (Input.GetKeyDown(toggleKey))
            {
                ToggleModal();
            }
            else if (isOpen && Input.GetKeyDown(alternateCloseKey))
            {
                SetModalVisible(false);
            }

            // Live diff refresh if modal is open
            if (isOpen && selectedProfile != null)
            {
                RefreshDiffInspector();
            }
        }

        public void ToggleModal()
        {
            SetModalVisible(!isOpen);
        }

        public void SetModalVisible(bool visible)
        {
            isOpen = visible;
            if (modalRoot != null)
            {
                modalRoot.SetActive(isOpen);
            }

            if (isOpen)
            {
                LoadProfiles();
                PopulatePlanetList();
                RefreshDiffInspector();
            }
        }

        public void LoadProfiles()
        {
            availableProfiles.Clear();

            // 1. Try loading from Resources/Planets
            var loaded = Resources.LoadAll<PlanetaryProfile>("Planets");
            if (loaded != null && loaded.Length > 0)
            {
                availableProfiles.AddRange(loaded);
            }

            // 2. Fallback: load all PlanetaryProfiles in Resources
            if (availableProfiles.Count == 0)
            {
                var fallback = Resources.LoadAll<PlanetaryProfile>("");
                if (fallback != null && fallback.Length > 0)
                {
                    availableProfiles.AddRange(fallback);
                }
            }

            // Sort: Default first, then alphabetical
            availableProfiles.Sort((a, b) =>
            {
                if (a.isDefault && !b.isDefault) return -1;
                if (!a.isDefault && b.isDefault) return 1;
                return string.Compare(a.planetName, b.planetName, StringComparison.OrdinalIgnoreCase);
            });
        }

        public void SelectProfile(PlanetaryProfile profile)
        {
            selectedProfile = profile;
            if (selectedProfile == null) return;

            if (selectedPlanetTitleText != null)
            {
                selectedPlanetTitleText.text = $"<color=#00E5FF><b>{selectedProfile.planetName.ToUpper()}</b></color> PROFILE";
            }

            if (selectedPlanetDescText != null)
            {
                selectedPlanetDescText.text = selectedProfile.description;
            }

            RefreshDiffInspector();
        }

        public void ApplySelectedProfile(bool respawnRover)
        {
            if (selectedProfile == null) return;

            if (PlanetEnvironmentController.Instance != null)
            {
                PlanetEnvironmentController.Instance.ApplyProfile(selectedProfile);
            }
            else if (PlanetaryParameterWriter.Instance != null)
            {
                PlanetaryParameterWriter.Instance.ApplyProfile(selectedProfile);
            }

            if (respawnRover)
            {
                var sfc = FindAnyObjectByType<SimulationFlowController>();
                if (sfc != null)
                {
                    sfc.RespawnActiveRover();
                }
            }

            RefreshDiffInspector();
        }

        private void RefreshDiffInspector()
        {
            if (diffInspectorText == null || selectedProfile == null) return;

            var reader = PlanetaryParameterReader.Instance;
            if (reader == null) reader = FindAnyObjectByType<PlanetaryParameterReader>();

            PlanetarySnapshot cur = reader != null ? reader.SnapshotCurrentState() : new PlanetarySnapshot();

            sb.Clear();
            sb.AppendLine("<b><color=#80D8FF>LIVE ENGINE PARAMETER DIFF</color></b> (Current Unity Engine → Target Profile)");
            sb.AppendLine("<size=80%><color=#78909C>Values read directly from Unity Physics, RenderSettings, Light & WindZone</color></size>\n");

            // 1. Gravity
            FormatDiffRow("Gravity (Physics.gravity.y)", $"{cur.gravityY:F2} m/s²", $"{selectedProfile.gravityY:F2} m/s²", Mathf.Abs(cur.gravityY - selectedProfile.gravityY) > 0.05f);

            // 2. Soil Static Friction
            FormatDiffRow("Soil Static Friction (μs)", $"{cur.staticFriction:F2}", $"{selectedProfile.terrainStaticFriction:F2}", Mathf.Abs(cur.staticFriction - selectedProfile.terrainStaticFriction) > 0.03f);

            // 3. Soil Dynamic Friction
            FormatDiffRow("Soil Dynamic Friction (μk)", $"{cur.dynamicFriction:F2}", $"{selectedProfile.terrainDynamicFriction:F2}", Mathf.Abs(cur.dynamicFriction - selectedProfile.terrainDynamicFriction) > 0.03f);

            // 4. Soil Bounciness / Restitution
            FormatDiffRow("Surface Restitution (e)", $"{cur.bounciness:F2}", $"{selectedProfile.terrainBounciness:F2}", Mathf.Abs(cur.bounciness - selectedProfile.terrainBounciness) > 0.03f);

            // 5. Rover Atmospheric Drag
            FormatDiffRow("Rover Drag (Damping)", $"{cur.roverDrag:F3}", $"{selectedProfile.roverDrag:F3}", Mathf.Abs(cur.roverDrag - selectedProfile.roverDrag) > 0.01f);

            // 6. Atmospheric Fog Density
            float curFog = cur.fogEnabled ? cur.fogDensity : 0f;
            float targetFog = selectedProfile.fogEnabled ? selectedProfile.fogDensity : 0f;
            FormatDiffRow("Atmosphere Fog Density", $"{curFog:F3}", $"{targetFog:F3}", Mathf.Abs(curFog - targetFog) > 0.002f);

            // 7. Sun Intensity
            FormatDiffRow("Solar Irradiance (Intensity)", $"{cur.sunIntensity:F2}", $"{selectedProfile.sunIntensity:F2}", Mathf.Abs(cur.sunIntensity - selectedProfile.sunIntensity) > 0.05f);

            // 8. Surface Wind Speed
            FormatDiffRow("Wind Velocity (windMain)", $"{cur.windSpeed:F1} m/s", $"{selectedProfile.windMain:F1} m/s", Mathf.Abs(cur.windSpeed - selectedProfile.windMain) > 0.2f);

            // Truthful facts section
            sb.AppendLine("\n<b><color=#FFD54F>VERIFIED ATMOSPHERIC & DISPLAY FACTS</color></b>");
            sb.AppendLine($"> Surface Temperature : <color=#00E5FF>{selectedProfile.surfaceTemperature}</color>");
            sb.AppendLine($"> Surface Pressure    : <color=#00E5FF>{selectedProfile.pressureKPa:F1} kPa</color>");
            sb.AppendLine($"> Solar Irradiance    : <color=#00E5FF>{selectedProfile.sunlightPercentOfEarth:F1}% of Earth</color>");
            sb.AppendLine($"> Hazard Classification: <color=#00E5FF>[{selectedProfile.hazardTitle}] {selectedProfile.hazardValue}</color>");
            if (!string.IsNullOrEmpty(selectedProfile.nasaFactSheetUrl))
            {
                sb.AppendLine($"> NASA Fact Sheet Source : <size=85%><color=#80D8FF>{selectedProfile.nasaFactSheetUrl}</color></size>");
            }

            diffInspectorText.text = sb.ToString();
        }

        private void FormatDiffRow(string label, string currentVal, string targetVal, bool hasChanged)
        {
            if (hasChanged)
            {
                sb.AppendLine($"> <b>{label,-26}</b>: <color=#90A4AE>{currentVal}</color>  <color=#FFD54F>-></color>  <color=#00E676><b>{targetVal}</b></color>");
            }
            else
            {
                sb.AppendLine($"> <b>{label,-26}</b>: <color=#00E5FF>{targetVal}</color> <size=75%><color=#81C784>(Matches Engine)</color></size>");
            }
        }

        public void PopulatePlanetList()
        {
            if (planetListContainer == null) return;

            // Clear old buttons
            foreach (Transform child in planetListContainer)
            {
                Destroy(child.gameObject);
            }

            // Create button for each profile
            foreach (var profile in availableProfiles)
            {
                var p = profile;
                GameObject btnObj = new GameObject($"Btn_{p.planetName}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
                btnObj.transform.SetParent(planetListContainer, false);

                RectTransform rt = btnObj.GetComponent<RectTransform>();
                rt.sizeDelta = new Vector2(0f, 52f);

                Image img = btnObj.GetComponent<Image>();
                img.color = (selectedProfile == p) ? new Color(0.0f, 0.45f, 0.65f, 0.85f) : new Color(0.08f, 0.14f, 0.22f, 0.85f);

                Button btn = btnObj.GetComponent<Button>();
                btn.onClick.AddListener(() =>
                {
                    SelectProfile(p);
                    PopulatePlanetList();
                });

                // Button Text
                GameObject txtObj = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
                txtObj.transform.SetParent(btnObj.transform, false);
                RectTransform txtRt = txtObj.GetComponent<RectTransform>();
                txtRt.anchorMin = Vector2.zero;
                txtRt.anchorMax = Vector2.one;
                txtRt.offsetMin = new Vector2(14f, 4f);
                txtRt.offsetMax = new Vector2(-14f, -4f);

                TextMeshProUGUI tmp = txtObj.GetComponent<TextMeshProUGUI>();
                tmp.fontSize = 13;
                tmp.alignment = TextAlignmentOptions.MidlineLeft;
                string defTag = p.isDefault ? " <color=#FFD54F>[DEFAULT]</color>" : "";
                tmp.text = $"<b>{p.planetName.ToUpper()}</b>{defTag}\n<size=75%><color=#B0BEC5>g={p.gravityY:F2} m/s² | Drag={p.roverDrag:F2} | Fog={p.fogDensity:F3}</color></size>";
            }
        }

        /// <summary>
        /// Procedurally constructs a sleek aerospace planet selection modal directly on the Canvas.
        /// </summary>
        public void BuildProceduralModal()
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas == null) canvas = FindAnyObjectByType<Canvas>();
            if (canvas == null)
            {
                var canvasGO = new GameObject("PlanetSelectionCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                canvas = canvasGO.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 50;
                var scaler = canvasGO.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
            }

            // Modal Root Container
            modalRoot = new GameObject("PlanetSelectionModal", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            modalRoot.transform.SetParent(canvas.transform, false);
            RectTransform modalRt = modalRoot.GetComponent<RectTransform>();
            modalRt.anchorMin = new Vector2(0.5f, 0.5f);
            modalRt.anchorMax = new Vector2(0.5f, 0.5f);
            modalRt.pivot = new Vector2(0.5f, 0.5f);
            modalRt.sizeDelta = new Vector2(760f, 520f);

            Image modalBg = modalRoot.GetComponent<Image>();
            modalBg.color = new Color(0.03f, 0.07f, 0.12f, 0.96f);

            Color cardBg = new Color(0.06f, 0.12f, 0.18f, 0.90f);
            Color cyan = new Color(0.0f, 0.90f, 1.0f);

            // 1. Header Bar
            GameObject header = CreatePanel(modalRoot.transform, "Header",
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -6f), new Vector2(-16f, 48f), cardBg);

            CreateTextMesh(header.transform, "Title",
                Vector2.zero, Vector2.one, new Vector2(16f, 0f), new Vector2(-80f, 0f),
                16, TextAlignmentOptions.MidlineLeft, cyan,
                "<b>PLANETARY ENVIRONMENT SELECTION STUDIO</b>  <size=75%><color=#B0BEC5>(Unity Built-in Systems)</color></size>");

            // Close 'X' Button
            GameObject closeObj = CreateButton(header.transform, "BtnClose",
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-8f, 0f), new Vector2(36f, 32f),
                new Color(0.4f, 0.1f, 0.1f, 0.8f), "X", 14);
            btnClose = closeObj.GetComponent<Button>();
            btnClose.onClick.AddListener(() => SetModalVisible(false));

            // 2. Left Column - Planet List Container
            GameObject listPanel = CreatePanel(modalRoot.transform, "PlanetListPanel",
                new Vector2(0f, 0f), new Vector2(0.36f, 1f), new Vector2(0f, 0.5f),
                new Vector2(12f, -30f), new Vector2(-18f, -74f), cardBg);

            // Vertical Layout for Buttons
            VerticalLayoutGroup vlg = listPanel.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 8f;
            vlg.padding = new RectOffset(8, 8, 10, 10);
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            planetListContainer = listPanel.transform;

            // 3. Right Column - Live Diff & Detail Inspector
            GameObject rightPanel = CreatePanel(modalRoot.transform, "DiffInspectorPanel",
                new Vector2(0.38f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f),
                new Vector2(-12f, -30f), new Vector2(-18f, -74f), cardBg);

            selectedPlanetTitleText = CreateTextMesh(rightPanel.transform, "SelectedTitle",
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(14f, -30f), new Vector2(-14f, -4f),
                16, TextAlignmentOptions.TopLeft, cyan, "PLANET PROFILE");

            selectedPlanetDescText = CreateTextMesh(rightPanel.transform, "SelectedDesc",
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(14f, -66f), new Vector2(-14f, -32f),
                11, TextAlignmentOptions.TopLeft, new Color(0.8f, 0.85f, 0.9f));

            diffInspectorText = CreateTextMesh(rightPanel.transform, "DiffText",
                new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(14f, 62f), new Vector2(-14f, -72f),
                12, TextAlignmentOptions.TopLeft, Color.white);

            // Action Buttons at bottom-right
            GameObject btnApplyEnvObj = CreateButton(rightPanel.transform, "BtnApplyEnv",
                new Vector2(0f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(8f, 12f), new Vector2(-12f, 40f),
                new Color(0.12f, 0.45f, 0.35f, 0.9f), "Apply Environment", 12);
            btnApplyEnvironment = btnApplyEnvObj.GetComponent<Button>();
            btnApplyEnvironment.onClick.AddListener(() => ApplySelectedProfile(false));

            GameObject btnApplyRespawnObj = CreateButton(rightPanel.transform, "BtnApplyRespawn",
                new Vector2(0.5f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                new Vector2(8f, 12f), new Vector2(-12f, 40f),
                new Color(0.15f, 0.55f, 0.75f, 0.95f), "Apply & Respawn Rover", 12);
            btnApplyAndRespawn = btnApplyRespawnObj.GetComponent<Button>();
            btnApplyAndRespawn.onClick.AddListener(() => ApplySelectedProfile(true));
        }

        private GameObject CreatePanel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPos, Vector2 sizeDelta, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = sizeDelta;

            Image img = go.GetComponent<Image>();
            img.color = color;
            return go;
        }

        private GameObject CreateButton(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPos, Vector2 sizeDelta, Color color, string label, float fontSize)
        {
            GameObject go = CreatePanel(parent, name, anchorMin, anchorMax, pivot, anchoredPos, sizeDelta, color);
            Button btn = go.AddComponent<Button>();

            GameObject txtObj = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            txtObj.transform.SetParent(go.transform, false);
            RectTransform txtRt = txtObj.GetComponent<RectTransform>();
            txtRt.anchorMin = Vector2.zero;
            txtRt.anchorMax = Vector2.one;
            txtRt.offsetMin = Vector2.zero;
            txtRt.offsetMax = Vector2.zero;

            TextMeshProUGUI tmp = txtObj.GetComponent<TextMeshProUGUI>();
            tmp.fontSize = fontSize;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            tmp.text = $"<b>{label}</b>";

            return go;
        }

        private TextMeshProUGUI CreateTextMesh(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, float fontSize, TextAlignmentOptions alignment, Color color, string initialText = "")
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;

            TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.fontSize = fontSize;
            tmp.alignment = alignment;
            tmp.color = color;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            if (!string.IsNullOrEmpty(initialText)) tmp.text = initialText;
            return tmp;
        }
    }
}

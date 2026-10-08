using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

namespace AutonomousRobotKit
{
    /// <summary>
    /// Runtime UI controller providing:
    /// 1. Floating '+' icon button for instant robot import (.zip and .urdf).
    /// 2. Interactive Robot Controller HUD:
    ///    - Displays active robot architecture and status.
    ///    - Interactive Stance buttons for quadrupeds ([1] Stand, [2] Crouch, [3] Sit, [0] Zero).
    ///    - Live driving telemetry for wheeled rovers (WASD skid-steer).
    ///    - Emergency Reset / Recenter button & Camera Focus button.
    /// 3. Real-time import progress & status toasts.
    /// </summary>
    [DisallowMultipleComponent]
    public class RuntimeRoverImportUI : MonoBehaviour
    {
        [Header("UI Positioning")]
        [Tooltip("Anchor position for the floating '+' button on screen.")]
        public Vector2 buttonAnchorPosition = new Vector2(45f, -45f); // Top-left default
        public Vector2 buttonSize = new Vector2(58f, 58f);

        [Header("Colors & Styling")]
        public Color buttonBgNormal = new Color(0.08f, 0.10f, 0.14f, 0.88f);
        public Color buttonBgHover = new Color(0.12f, 0.16f, 0.22f, 0.95f);
        public Color accentCyan = new Color(0.0f, 0.82f, 1.0f, 1.0f);
        public Color successGreen = new Color(0.15f, 0.85f, 0.40f, 1.0f);
        public Color errorRed = new Color(1.0f, 0.28f, 0.28f, 1.0f);
        public Color panelBgDark = new Color(0.06f, 0.08f, 0.12f, 0.92f);

        // Internal UI references
        private Canvas _rootCanvas;
        private RectTransform _buttonRect;
        private Button _importButton;
        private Image _buttonBg;
        private GameObject _toastPanel;
        private TextMeshProUGUI _toastTitle;
        private TextMeshProUGUI _toastMessage;
        private Image _toastProgressBar;
        private Coroutine _hideToastRoutine;

        // Controller HUD UI references
        private GameObject _controllerHud;
        private TextMeshProUGUI _hudRobotTitle;
        private TextMeshProUGUI _hudInstructionText;
        private GameObject _stanceButtonGroup;
        private Button _btnStand;
        private Button _btnCrouch;
        private Button _btnSit;
        private Button _btnZero;
        private Button _btnRecenter;
        private Button _btnCamFocus;
        private Button _btnToggleHud;
        private GameObject _hudBody;
        private bool _isHudExpanded = true;

        // Active robot controller references
        private GameObject _activeRobot;
        private ArticulatedRobotController _activeArticulatedCtrl;
        private SimpleDifferentialDrive _activeDiffDrive;
        private float _lastScanTime = 0f;

        private void Awake()
        {
            InitializeInterface();
        }

        private void Start()
        {
            InitializeInterface();
            ScanAndBindActiveRobot();
        }

        private void Update()
        {
            // Periodically check for active robot in scene if not bound
            if (_activeRobot == null && Time.time - _lastScanTime > 2.0f)
            {
                _lastScanTime = Time.time;
                ScanAndBindActiveRobot();
            }

            UpdateHudState();
        }

        public void InitializeInterface()
        {
            EnsureCanvasAndEventSystem();
            BuildInterface();
        }

        private void EnsureCanvasAndEventSystem()
        {
            if (_rootCanvas != null) return;

            _rootCanvas = GetComponentInParent<Canvas>();
            if (_rootCanvas == null)
            {
                _rootCanvas = FindAnyObjectByType<Canvas>();
            }

            if (_rootCanvas == null)
            {
                var canvasGo = new GameObject("RuntimeImportCanvas");
                _rootCanvas = canvasGo.AddComponent<Canvas>();
                _rootCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                _rootCanvas.sortingOrder = 999;

                var scaler = canvasGo.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                scaler.matchWidthOrHeight = 0.5f;

                canvasGo.AddComponent<GraphicRaycaster>();
            }

            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var eventSystemGo = new GameObject("EventSystem");
                eventSystemGo.AddComponent<EventSystem>();

                var inputModuleType = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
                if (inputModuleType != null)
                {
                    eventSystemGo.AddComponent(inputModuleType);
                }
                else
                {
                    eventSystemGo.AddComponent<StandaloneInputModule>();
                }
            }
        }

        private void BuildInterface()
        {
            if (_buttonRect != null && _toastPanel != null && _controllerHud != null) return;

            var existingContainer = _rootCanvas.transform.Find("RoverImportUI_Container");
            if (existingContainer != null)
            {
                DestroyImmediate(existingContainer.gameObject);
            }

            var container = new GameObject("RoverImportUI_Container", typeof(RectTransform));
            container.transform.SetParent(_rootCanvas.transform, false);
            var contRect = container.GetComponent<RectTransform>();
            contRect.anchorMin = Vector2.zero;
            contRect.anchorMax = Vector2.one;
            contRect.offsetMin = Vector2.zero;
            contRect.offsetMax = Vector2.zero;

            // ----------------------------------------------------
            // 1. The Floating '+' Action Button (Clean Single Button)
            // ----------------------------------------------------
            var btnGo = new GameObject("Btn_AddRover", typeof(RectTransform), typeof(Image), typeof(Button));
            btnGo.transform.SetParent(container.transform, false);

            _buttonRect = btnGo.GetComponent<RectTransform>();
            _buttonRect.anchorMin = new Vector2(0f, 1f);
            _buttonRect.anchorMax = new Vector2(0f, 1f);
            _buttonRect.pivot = new Vector2(0f, 1f);
            _buttonRect.anchoredPosition = buttonAnchorPosition;
            _buttonRect.sizeDelta = buttonSize;

            _buttonBg = btnGo.GetComponent<Image>();
            _buttonBg.color = buttonBgNormal;
            _buttonBg.sprite = CreateRoundedSprite(64, 64, 16);

            var outline = btnGo.AddComponent<Outline>();
            outline.effectColor = new Color(accentCyan.r, accentCyan.g, accentCyan.b, 0.75f);
            outline.effectDistance = new Vector2(2f, -2f);

            _importButton = btnGo.GetComponent<Button>();
            _importButton.targetGraphic = _buttonBg;
            _importButton.onClick.AddListener(OnPlusButtonClicked);

            var trigger = btnGo.AddComponent<EventTrigger>();
            AddTriggerEntry(trigger, EventTriggerType.PointerEnter, (e) =>
            {
                _buttonBg.color = buttonBgHover;
                _buttonRect.localScale = new Vector3(1.08f, 1.08f, 1f);
            });
            AddTriggerEntry(trigger, EventTriggerType.PointerExit, (e) =>
            {
                _buttonBg.color = buttonBgNormal;
                _buttonRect.localScale = Vector3.one;
            });

            var iconGo = new GameObject("Icon_Plus", typeof(RectTransform), typeof(TextMeshProUGUI));
            iconGo.transform.SetParent(btnGo.transform, false);
            var iconRect = iconGo.GetComponent<RectTransform>();
            iconRect.anchorMin = Vector2.zero;
            iconRect.anchorMax = Vector2.one;
            iconRect.offsetMin = Vector2.zero;
            iconRect.offsetMax = new Vector2(0f, 4f);

            var iconTmp = iconGo.GetComponent<TextMeshProUGUI>();
            iconTmp.text = "+";
            iconTmp.fontSize = 38f;
            iconTmp.alignment = TextAlignmentOptions.Center;
            iconTmp.color = accentCyan;
            iconTmp.fontStyle = FontStyles.Bold;
            iconTmp.raycastTarget = false;

            var labelGo = new GameObject("Label_Import", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelGo.transform.SetParent(btnGo.transform, false);
            var labelRect = labelGo.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(1f, 0f);
            labelRect.pivot = new Vector2(0.5f, 0f);
            labelRect.anchoredPosition = new Vector2(0f, 4f);
            labelRect.sizeDelta = new Vector2(0f, 14f);

            var labelTmp = labelGo.GetComponent<TextMeshProUGUI>();
            labelTmp.text = "URDF";
            labelTmp.fontSize = 10f;
            labelTmp.alignment = TextAlignmentOptions.Center;
            labelTmp.color = new Color(0.85f, 0.9f, 1f, 0.95f);
            labelTmp.fontStyle = FontStyles.Bold;
            labelTmp.raycastTarget = false;

            // ----------------------------------------------------
            // 2. Status / Progress Toast Overlay
            // ----------------------------------------------------
            _toastPanel = new GameObject("Toast_ProgressOverlay", typeof(RectTransform), typeof(Image));
            _toastPanel.transform.SetParent(container.transform, false);

            var toastRect = _toastPanel.GetComponent<RectTransform>();
            toastRect.anchorMin = new Vector2(0.5f, 1f);
            toastRect.anchorMax = new Vector2(0.5f, 1f);
            toastRect.pivot = new Vector2(0.5f, 1f);
            toastRect.anchoredPosition = new Vector2(0f, -40f);
            toastRect.sizeDelta = new Vector2(440f, 85f);

            var toastImg = _toastPanel.GetComponent<Image>();
            toastImg.color = panelBgDark;
            toastImg.sprite = CreateRoundedSprite(64, 64, 12);

            var toastOutline = _toastPanel.AddComponent<Outline>();
            toastOutline.effectColor = new Color(accentCyan.r, accentCyan.g, accentCyan.b, 0.5f);
            toastOutline.effectDistance = new Vector2(1.5f, -1.5f);

            var titleGo = new GameObject("Toast_Title", typeof(RectTransform), typeof(TextMeshProUGUI));
            titleGo.transform.SetParent(_toastPanel.transform, false);
            var tRect = titleGo.GetComponent<RectTransform>();
            tRect.anchorMin = new Vector2(0f, 1f);
            tRect.anchorMax = new Vector2(1f, 1f);
            tRect.pivot = new Vector2(0.5f, 1f);
            tRect.anchoredPosition = new Vector2(0f, -12f);
            tRect.sizeDelta = new Vector2(-30f, 24f);

            _toastTitle = titleGo.GetComponent<TextMeshProUGUI>();
            _toastTitle.text = "ROBOT IMPORTER";
            _toastTitle.fontSize = 14f;
            _toastTitle.alignment = TextAlignmentOptions.Left;
            _toastTitle.color = accentCyan;
            _toastTitle.fontStyle = FontStyles.Bold;

            var msgGo = new GameObject("Toast_Message", typeof(RectTransform), typeof(TextMeshProUGUI));
            msgGo.transform.SetParent(_toastPanel.transform, false);
            var mRect = msgGo.GetComponent<RectTransform>();
            mRect.anchorMin = new Vector2(0f, 1f);
            mRect.anchorMax = new Vector2(1f, 1f);
            mRect.pivot = new Vector2(0.5f, 1f);
            mRect.anchoredPosition = new Vector2(0f, -36f);
            mRect.sizeDelta = new Vector2(-30f, 22f);

            _toastMessage = msgGo.GetComponent<TextMeshProUGUI>();
            _toastMessage.text = "Initializing...";
            _toastMessage.fontSize = 12f;
            _toastMessage.alignment = TextAlignmentOptions.Left;
            _toastMessage.color = new Color(0.85f, 0.9f, 0.95f, 1f);

            var barBgGo = new GameObject("Progress_Bg", typeof(RectTransform), typeof(Image));
            barBgGo.transform.SetParent(_toastPanel.transform, false);
            var bBgRect = barBgGo.GetComponent<RectTransform>();
            bBgRect.anchorMin = new Vector2(0f, 0f);
            bBgRect.anchorMax = new Vector2(1f, 0f);
            bBgRect.pivot = new Vector2(0.5f, 0f);
            bBgRect.anchoredPosition = new Vector2(0f, 10f);
            bBgRect.sizeDelta = new Vector2(-30f, 6f);

            var bBgImg = barBgGo.GetComponent<Image>();
            bBgImg.color = new Color(0.18f, 0.22f, 0.28f, 0.8f);

            var barFillGo = new GameObject("Progress_Fill", typeof(RectTransform), typeof(Image));
            barFillGo.transform.SetParent(barBgGo.transform, false);
            var bFillRect = barFillGo.GetComponent<RectTransform>();
            bFillRect.anchorMin = new Vector2(0f, 0f);
            bFillRect.anchorMax = new Vector2(0f, 1f);
            bFillRect.pivot = new Vector2(0f, 0.5f);
            bFillRect.offsetMin = Vector2.zero;
            bFillRect.offsetMax = Vector2.zero;

            _toastProgressBar = barFillGo.GetComponent<Image>();
            _toastProgressBar.color = accentCyan;

            _toastPanel.SetActive(false);

            // ----------------------------------------------------
            // 3. Interactive Robot Controller HUD Panel
            // ----------------------------------------------------
            BuildControllerHud(container.transform);

            // Wire any extra buttons in Canvas
            if (_rootCanvas != null)
            {
                var allButtons = _rootCanvas.GetComponentsInChildren<Button>(true);
                foreach (var b in allButtons)
                {
                    if (b != _importButton && b != _btnStand && b != _btnCrouch && b != _btnSit && b != _btnZero && b != _btnRecenter && b != _btnCamFocus && b != _btnToggleHud)
                    {
                        var text = b.GetComponentInChildren<Text>();
                        var tmp = b.GetComponentInChildren<TextMeshProUGUI>();
                        string tStr = ((text != null ? text.text : "") + " " + (tmp != null ? tmp.text : "") + " " + b.name).ToLowerInvariant();
                        if (tStr.Contains("+") || tStr.Contains("import"))
                        {
                            b.onClick.RemoveListener(OnPlusButtonClicked);
                            b.onClick.AddListener(OnPlusButtonClicked);
                        }
                    }
                }
            }
        }

        private void BuildControllerHud(Transform parentContainer)
        {
            _controllerHud = new GameObject("RobotController_HUD", typeof(RectTransform), typeof(Image));
            _controllerHud.transform.SetParent(parentContainer, false);

            var hudRect = _controllerHud.GetComponent<RectTransform>();
            hudRect.anchorMin = new Vector2(0.5f, 0f); // Bottom-center anchor
            hudRect.anchorMax = new Vector2(0.5f, 0f);
            hudRect.pivot = new Vector2(0.5f, 0f);
            hudRect.anchoredPosition = new Vector2(0f, 25f);
            hudRect.sizeDelta = new Vector2(560f, 125f);

            var hudImg = _controllerHud.GetComponent<Image>();
            hudImg.color = panelBgDark;
            hudImg.sprite = CreateRoundedSprite(64, 64, 14);

            var hudOutline = _controllerHud.AddComponent<Outline>();
            hudOutline.effectColor = new Color(accentCyan.r, accentCyan.g, accentCyan.b, 0.6f);
            hudOutline.effectDistance = new Vector2(1.5f, -1.5f);

            // Header Bar
            var headerGo = new GameObject("HUD_Header", typeof(RectTransform));
            headerGo.transform.SetParent(_controllerHud.transform, false);
            var hRect = headerGo.GetComponent<RectTransform>();
            hRect.anchorMin = new Vector2(0f, 1f);
            hRect.anchorMax = new Vector2(1f, 1f);
            hRect.pivot = new Vector2(0.5f, 1f);
            hRect.anchoredPosition = new Vector2(0f, -8f);
            hRect.sizeDelta = new Vector2(-24f, 26f);

            var titleGo = new GameObject("Title_Robot", typeof(RectTransform), typeof(TextMeshProUGUI));
            titleGo.transform.SetParent(headerGo.transform, false);
            var tr = titleGo.GetComponent<RectTransform>();
            tr.anchorMin = new Vector2(0f, 0f);
            tr.anchorMax = new Vector2(0.85f, 1f);
            tr.offsetMin = Vector2.zero;
            tr.offsetMax = Vector2.zero;

            _hudRobotTitle = titleGo.GetComponent<TextMeshProUGUI>();
            _hudRobotTitle.text = "ROBOT CONTROLLER";
            _hudRobotTitle.fontSize = 13f;
            _hudRobotTitle.alignment = TextAlignmentOptions.Left;
            _hudRobotTitle.color = accentCyan;
            _hudRobotTitle.fontStyle = FontStyles.Bold;

            // Minimize / Collapse button
            var minBtnGo = new GameObject("Btn_ToggleHUD", typeof(RectTransform), typeof(Image), typeof(Button));
            minBtnGo.transform.SetParent(headerGo.transform, false);
            var minRect = minBtnGo.GetComponent<RectTransform>();
            minRect.anchorMin = new Vector2(1f, 0.5f);
            minRect.anchorMax = new Vector2(1f, 0.5f);
            minRect.pivot = new Vector2(1f, 0.5f);
            minRect.sizeDelta = new Vector2(26f, 22f);

            var minImg = minBtnGo.GetComponent<Image>();
            minImg.color = new Color(0.2f, 0.25f, 0.35f, 0.8f);
            minImg.sprite = CreateRoundedSprite(32, 32, 6);

            _btnToggleHud = minBtnGo.GetComponent<Button>();
            _btnToggleHud.onClick.AddListener(ToggleHudExpanded);

            var minTxtGo = new GameObject("Txt", typeof(RectTransform), typeof(TextMeshProUGUI));
            minTxtGo.transform.SetParent(minBtnGo.transform, false);
            var mtr = minTxtGo.GetComponent<RectTransform>();
            mtr.anchorMin = Vector2.zero;
            mtr.anchorMax = Vector2.one;
            mtr.offsetMin = Vector2.zero;
            mtr.offsetMax = Vector2.zero;
            var minTmp = minTxtGo.GetComponent<TextMeshProUGUI>();
            minTmp.text = "-";
            minTmp.fontSize = 16f;
            minTmp.alignment = TextAlignmentOptions.Center;
            minTmp.color = Color.white;
            minTmp.raycastTarget = false;

            // HUD Body (Collapsible)
            _hudBody = new GameObject("HUD_Body", typeof(RectTransform));
            _hudBody.transform.SetParent(_controllerHud.transform, false);
            var bodyRect = _hudBody.GetComponent<RectTransform>();
            bodyRect.anchorMin = Vector2.zero;
            bodyRect.anchorMax = new Vector2(1f, 1f);
            bodyRect.offsetMin = new Vector2(12f, 8f);
            bodyRect.offsetMax = new Vector2(-12f, -36f);

            // Row 1: Stance Buttons (Stand, Crouch, Sit, Zero) & Quick Utility Buttons
            _stanceButtonGroup = new GameObject("StanceButtons", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            _stanceButtonGroup.transform.SetParent(_hudBody.transform, false);
            var sbgRect = _stanceButtonGroup.GetComponent<RectTransform>();
            sbgRect.anchorMin = new Vector2(0f, 1f);
            sbgRect.anchorMax = new Vector2(1f, 1f);
            sbgRect.pivot = new Vector2(0.5f, 1f);
            sbgRect.anchoredPosition = new Vector2(0f, 0f);
            sbgRect.sizeDelta = new Vector2(0f, 36f);

            var hlg = _stanceButtonGroup.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing = 8f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;

            _btnStand = CreateHudButton(_stanceButtonGroup.transform, "[1] STAND", new Color(0.12f, 0.45f, 0.65f, 0.9f), () => OnStanceButtonClicked(ArticulatedRobotController.StancePreset.Stand));
            _btnCrouch = CreateHudButton(_stanceButtonGroup.transform, "[2] CROUCH", new Color(0.18f, 0.24f, 0.35f, 0.9f), () => OnStanceButtonClicked(ArticulatedRobotController.StancePreset.Crouch));
            _btnSit = CreateHudButton(_stanceButtonGroup.transform, "[3] SIT", new Color(0.18f, 0.24f, 0.35f, 0.9f), () => OnStanceButtonClicked(ArticulatedRobotController.StancePreset.Sit));
            _btnZero = CreateHudButton(_stanceButtonGroup.transform, "[0] ZERO", new Color(0.25f, 0.22f, 0.28f, 0.9f), () => OnStanceButtonClicked(ArticulatedRobotController.StancePreset.Zero));

            _btnRecenter = CreateHudButton(_stanceButtonGroup.transform, "RECENTER", new Color(0.15f, 0.50f, 0.35f, 0.9f), OnRecenterButtonClicked);
            _btnCamFocus = CreateHudButton(_stanceButtonGroup.transform, "CAMERA", new Color(0.35f, 0.25f, 0.55f, 0.9f), OnCameraFocusButtonClicked);

            // Row 2: Live instruction and drive telemetry text
            var instrGo = new GameObject("Text_Instructions", typeof(RectTransform), typeof(TextMeshProUGUI));
            instrGo.transform.SetParent(_hudBody.transform, false);
            var iRect = instrGo.GetComponent<RectTransform>();
            iRect.anchorMin = new Vector2(0f, 0f);
            iRect.anchorMax = new Vector2(1f, 0f);
            iRect.pivot = new Vector2(0.5f, 0f);
            iRect.anchoredPosition = new Vector2(0f, 4f);
            iRect.sizeDelta = new Vector2(0f, 32f);

            _hudInstructionText = instrGo.GetComponent<TextMeshProUGUI>();
            _hudInstructionText.text = "Drive: W/A/S/D to Trot & Steer | Stances: [1] Stand, [2] Crouch, [3] Sit, [0] Zero";
            _hudInstructionText.fontSize = 11f;
            _hudInstructionText.alignment = TextAlignmentOptions.Center;
            _hudInstructionText.color = new Color(0.85f, 0.9f, 0.95f, 0.95f);

            _controllerHud.SetActive(false);
        }

        private Button CreateHudButton(Transform parent, string label, Color bgColor, UnityEngine.Events.UnityAction onClick)
        {
            var btnGo = new GameObject($"Btn_{label.Replace(" ", "").Replace("[", "").Replace("]", "")}", typeof(RectTransform), typeof(Image), typeof(Button));
            btnGo.transform.SetParent(parent, false);

            var img = btnGo.GetComponent<Image>();
            img.color = bgColor;
            img.sprite = CreateRoundedSprite(48, 48, 8);

            var btn = btnGo.GetComponent<Button>();
            btn.targetGraphic = img;
            if (onClick != null) btn.onClick.AddListener(onClick);

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            textGo.transform.SetParent(btnGo.transform, false);
            var tr = textGo.GetComponent<RectTransform>();
            tr.anchorMin = Vector2.zero;
            tr.anchorMax = Vector2.one;
            tr.offsetMin = Vector2.zero;
            tr.offsetMax = Vector2.zero;

            var tmp = textGo.GetComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = 10f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            tmp.raycastTarget = false;

            return btn;
        }

        private void ToggleHudExpanded()
        {
            _isHudExpanded = !_isHudExpanded;
            if (_hudBody != null) _hudBody.SetActive(_isHudExpanded);

            var hudRect = _controllerHud.GetComponent<RectTransform>();
            hudRect.sizeDelta = _isHudExpanded ? new Vector2(560f, 125f) : new Vector2(320f, 40f);

            var minTmp = _btnToggleHud.GetComponentInChildren<TextMeshProUGUI>();
            if (minTmp != null) minTmp.text = _isHudExpanded ? "-" : "+";
        }

        public void BindActiveRobot(GameObject robot)
        {
            if (robot == null) return;
            _activeRobot = robot;

            _activeArticulatedCtrl = robot.GetComponent<ArticulatedRobotController>();
            _activeDiffDrive = robot.GetComponent<SimpleDifferentialDrive>();

            if (_activeArticulatedCtrl == null && _activeDiffDrive == null)
            {
                _activeArticulatedCtrl = robot.GetComponentInChildren<ArticulatedRobotController>();
                _activeDiffDrive = robot.GetComponentInChildren<SimpleDifferentialDrive>();
            }

            if (_controllerHud != null)
            {
                _controllerHud.SetActive(true);
            }

            string rName = robot.name.Replace("(Clone)", "").Replace("_description", "").Trim();
            if (_activeDiffDrive != null && _activeArticulatedCtrl != null)
            {
                // Wheeled Quadruped (e.g. Deep Robotics M20, Unitree Go2-W)
                if (_hudRobotTitle != null) _hudRobotTitle.text = $"ROBOT: {rName.ToUpper()} [WHEELED QUADRUPED]";
                if (_btnStand != null) _btnStand.gameObject.SetActive(true);
                if (_btnCrouch != null) _btnCrouch.gameObject.SetActive(true);
                if (_btnSit != null) _btnSit.gameObject.SetActive(true);
                if (_btnZero != null) _btnZero.gameObject.SetActive(true);
                if (_hudInstructionText != null)
                {
                    _hudInstructionText.text = "Drive: W/A/S/D to Drive Wheels | Posture: [1] Stand, [2] Crouch, [3] Sit, [0] Zero";
                }
            }
            else if (_activeDiffDrive != null)
            {
                // Pure Wheeled Rover (e.g. Husky, Perseverance, Pickerbot)
                if (_hudRobotTitle != null) _hudRobotTitle.text = $"ROVER: {rName.ToUpper()} [WHEELED MOBILE ROVER]";
                if (_btnStand != null) _btnStand.gameObject.SetActive(false);
                if (_btnCrouch != null) _btnCrouch.gameObject.SetActive(false);
                if (_btnSit != null) _btnSit.gameObject.SetActive(false);
                if (_btnZero != null) _btnZero.gameObject.SetActive(false);
                if (_hudInstructionText != null)
                {
                    _hudInstructionText.text = "Drive: W/A/S/D or Arrow keys to Drive & Differential Skid-Steer";
                }
            }
            else if (_activeArticulatedCtrl != null)
            {
                // Pure Legged Quadruped (e.g. Go2, Go1, ANYmal D, Spot)
                if (_hudRobotTitle != null) _hudRobotTitle.text = $"ROBOT: {rName.ToUpper()} [ARTICULATED QUADRUPED]";
                if (_btnStand != null) _btnStand.gameObject.SetActive(true);
                if (_btnCrouch != null) _btnCrouch.gameObject.SetActive(true);
                if (_btnSit != null) _btnSit.gameObject.SetActive(true);
                if (_btnZero != null) _btnZero.gameObject.SetActive(true);
                if (_hudInstructionText != null)
                {
                    _hudInstructionText.text = "Drive: W/A/S/D to Trot & Steer | Stances: [1] Stand, [2] Crouch, [3] Sit, [0] Zero";
                }
            }
            else
            {
                if (_hudRobotTitle != null) _hudRobotTitle.text = $"ROBOT: {rName.ToUpper()}";
                if (_btnStand != null) _btnStand.gameObject.SetActive(false);
                if (_btnCrouch != null) _btnCrouch.gameObject.SetActive(false);
                if (_btnSit != null) _btnSit.gameObject.SetActive(false);
                if (_btnZero != null) _btnZero.gameObject.SetActive(false);
                if (_hudInstructionText != null)
                {
                    _hudInstructionText.text = "Robot active on terrain. FreeFly camera enabled.";
                }
            }
        }

        private void ScanAndBindActiveRobot()
        {
            var artCtrl = FindAnyObjectByType<ArticulatedRobotController>();
            if (artCtrl != null)
            {
                BindActiveRobot(artCtrl.gameObject);
                return;
            }

            var diff = FindAnyObjectByType<SimpleDifferentialDrive>();
            if (diff != null)
            {
                BindActiveRobot(diff.gameObject);
                return;
            }

            var allMono = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            foreach (var m in allMono)
            {
                if (m != null && m.GetType().Name == "SimulationFlowController")
                {
                    var activeRoverProp = m.GetType().GetProperty("ActiveRover") ?? (object)m.GetType().GetField("ActiveRover");
                    GameObject roverObj = null;
                    if (activeRoverProp is System.Reflection.PropertyInfo pi)
                        roverObj = pi.GetValue(m) as GameObject;
                    else if (activeRoverProp is System.Reflection.FieldInfo fi)
                        roverObj = fi.GetValue(m) as GameObject;

                    if (roverObj != null)
                    {
                        BindActiveRobot(roverObj);
                        return;
                    }
                }
            }
        }

        private void UpdateHudState()
        {
            if (_activeArticulatedCtrl != null && _hudInstructionText != null && _isHudExpanded)
            {
                string stance = _activeArticulatedCtrl.currentPreset.ToString().ToUpper();
                bool moving = _activeArticulatedCtrl.IsMoving;
                string locomotionStatus = moving ? "<color=#00FF88>TROTTING (WASD Active)</color>" : "POSTURE HOLDING (Stable)";
                _hudInstructionText.text = $"Stance: <b>{stance}</b> | Locomotion: <b>{locomotionStatus}</b>\nKeys: [W/A/S/D] Trot | [1] Stand [2] Crouch [3] Sit [0] Zero";
            }
            else if (_activeDiffDrive != null && _hudInstructionText != null && _isHudExpanded)
            {
                float speed = _activeDiffDrive.SmoothedForwardVelocity;
                string moveStatus = Mathf.Abs(speed) > 5f ? $"<color=#00FF88>DRIVING: {speed:F0} deg/s</color>" : "STOPPED / BRAKING";
                _hudInstructionText.text = $"Drivetrain: <b>{moveStatus}</b> ({_activeDiffDrive.wheels.Count} Wheels Active)\nDrive with W/A/S/D or Arrow keys";
            }
        }

        private void OnStanceButtonClicked(ArticulatedRobotController.StancePreset preset)
        {
            if (_activeArticulatedCtrl == null && _activeRobot != null)
            {
                _activeArticulatedCtrl = _activeRobot.GetComponent<ArticulatedRobotController>();
            }

            if (_activeArticulatedCtrl != null)
            {
                _activeArticulatedCtrl.currentPreset = preset;
                switch (preset)
                {
                    case ArticulatedRobotController.StancePreset.Stand:
                        _activeArticulatedCtrl.ApplyStandStance();
                        break;
                    case ArticulatedRobotController.StancePreset.Crouch:
                        _activeArticulatedCtrl.ApplyCrouchStance();
                        break;
                    case ArticulatedRobotController.StancePreset.Sit:
                        _activeArticulatedCtrl.ApplySitStance();
                        break;
                    case ArticulatedRobotController.StancePreset.Zero:
                        _activeArticulatedCtrl.ApplyZeroStance();
                        break;
                }
                Debug.Log($"[RuntimeRoverImportUI] Applied stance {preset} to '{_activeRobot.name}'.");
            }
        }

        private void OnRecenterButtonClicked()
        {
            if (_activeRobot == null) return;

            Vector3 targetPos = Vector3.zero;
            var terrain = UnityEngine.Terrain.activeTerrain;
            if (terrain != null)
            {
                targetPos = terrain.transform.position + terrain.terrainData.size * 0.5f;
                targetPos.y = terrain.SampleHeight(targetPos) + terrain.transform.position.y + 0.35f;
            }
            else if (Camera.main != null)
            {
                targetPos = Camera.main.transform.position + Camera.main.transform.forward * 4f;
                targetPos.y = 0.35f;
            }

            var allBodies = _activeRobot.GetComponentsInChildren<ArticulationBody>(true);
            var rootBody = allBodies != null ? allBodies.FirstOrDefault(b => b.isRoot) : null;

            _activeRobot.transform.position = targetPos;
            _activeRobot.transform.rotation = Quaternion.identity;

            if (rootBody != null)
            {
                rootBody.transform.position = targetPos;
                rootBody.transform.rotation = Quaternion.identity;
                rootBody.TeleportRoot(targetPos, Quaternion.identity);
            }

            if (allBodies != null)
            {
                foreach (var b in allBodies)
                {
                    if (b == null) continue;
                    b.linearVelocity = Vector3.zero;
                    b.angularVelocity = Vector3.zero;
                }
            }

            Physics.SyncTransforms();
            Debug.Log($"[RuntimeRoverImportUI] Recentered '{_activeRobot.name}' at {targetPos}.");
        }

        public void FocusCameraOnActiveRobot() => OnCameraFocusButtonClicked();

        private void OnCameraFocusButtonClicked()
        {
            if (_activeRobot == null) return;

            var allBodies = _activeRobot.GetComponentsInChildren<ArticulationBody>(true);
            var rootBody = allBodies != null ? allBodies.FirstOrDefault(b => b.isRoot) : null;
            Transform targetT = rootBody != null ? rootBody.transform : _activeRobot.transform;

            var mainCam = Camera.main;
            if (mainCam != null)
            {
                var camMono = mainCam.GetComponent("FreeFlyCamera");
                if (camMono != null)
                {
                    camMono.SendMessage("SetTargetRover", targetT, SendMessageOptions.DontRequireReceiver);
                    var method = camMono.GetType().GetMethod("SetCameraPerspective");
                    if (method != null)
                    {
                        var enumType = camMono.GetType().GetNestedType("CameraPerspective");
                        if (enumType != null)
                        {
                            try
                            {
                                object targetVal = null;
                                if (Enum.IsDefined(enumType, "Chase"))
                                    targetVal = Enum.Parse(enumType, "Chase");
                                else if (Enum.IsDefined(enumType, "Rear"))
                                    targetVal = Enum.Parse(enumType, "Rear");

                                if (targetVal != null)
                                    method.Invoke(camMono, new object[] { targetVal });
                            }
                            catch { }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Invoked when the user clicks the '+' button.
        /// Opens native file explorer strictly filtering .zip and .urdf files.
        /// </summary>
        public void OnPlusButtonClicked()
        {
            string selectedFile = null;

#if UNITY_EDITOR
            selectedFile = UnityEditor.EditorUtility.OpenFilePanel(
                "Select Robot Package (.zip or .urdf)",
                "",
                "zip,urdf"
            );
#else
            try
            {
                Type sfbType = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    sfbType = asm.GetType("SFB.StandaloneFileBrowser");
                    if (sfbType != null) break;
                }

                if (sfbType != null)
                {
                    var method = sfbType.GetMethod("OpenFilePanel", new[] { typeof(string), typeof(string), typeof(string), typeof(bool) });
                    if (method != null)
                    {
                        var result = method.Invoke(null, new object[] { "Select Robot Package (.zip or .urdf)", "", "zip,urdf", false }) as string[];
                        if (result != null && result.Length > 0 && !string.IsNullOrEmpty(result[0]))
                        {
                            selectedFile = result[0];
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RuntimeRoverImportUI] Native file dialog reflection error: {ex.Message}");
            }
#endif

            if (string.IsNullOrEmpty(selectedFile))
            {
                Debug.Log("[RuntimeRoverImportUI] User cancelled file selection.");
                return;
            }

            Debug.Log("[RuntimeRoverImportUI] Selected file: " + selectedFile);
            StartImport(selectedFile);
        }

        public void StartImport(string filePath)
        {
            SetButtonInteractable(false);
            ShowToast("IMPORTING ROBOT", "Reading " + Path.GetFileName(filePath) + "...", 0.05f, accentCyan);

            RuntimeUrdfImporter.ImportRobot(
                filePath,
                onProgress: (prog, status) =>
                {
                    UpdateToastProgress(status, prog);
                },
                onSuccess: (robotObj) =>
                {
                    SetButtonInteractable(true);
                    string name = robotObj != null ? robotObj.name : "Robot";
                    ShowToast("IMPORT COMPLETE", $"'{name}' successfully imported and ready to drive!", 1.0f, successGreen);
                    AutoDismissToast(4.0f);

                    BindActiveRobot(robotObj);
                },
                onError: (errorMsg) =>
                {
                    SetButtonInteractable(true);
                    ShowToast("IMPORT FAILED", errorMsg, 1.0f, errorRed);
                    AutoDismissToast(6.0f);
                }
            );
        }

        private void ShowToast(string title, string message, float progress, Color titleColor)
        {
            if (_hideToastRoutine != null)
            {
                StopCoroutine(_hideToastRoutine);
                _hideToastRoutine = null;
            }

            _toastTitle.text = title;
            _toastTitle.color = titleColor;
            _toastMessage.text = message;

            SetProgressFill(progress);
            _toastPanel.SetActive(true);
        }

        private void UpdateToastProgress(string status, float progress)
        {
            _toastMessage.text = status;
            SetProgressFill(progress);
        }

        private void SetProgressFill(float progress)
        {
            if (_toastProgressBar != null)
            {
                var rt = _toastProgressBar.rectTransform;
                rt.anchorMax = new Vector2(Mathf.Clamp01(progress), 1f);
            }
        }

        private void AutoDismissToast(float delaySeconds)
        {
            if (_hideToastRoutine != null) StopCoroutine(_hideToastRoutine);
            _hideToastRoutine = StartCoroutine(DismissRoutine(delaySeconds));
        }

        private IEnumerator DismissRoutine(float delay)
        {
            yield return new WaitForSeconds(delay);
            if (_toastPanel != null) _toastPanel.SetActive(false);
            _hideToastRoutine = null;
        }

        private void SetButtonInteractable(bool interactable)
        {
            if (_importButton != null) _importButton.interactable = interactable;
            if (_buttonBg != null)
            {
                _buttonBg.color = interactable ? buttonBgNormal : new Color(0.2f, 0.2f, 0.25f, 0.5f);
            }
        }

        private static void AddTriggerEntry(EventTrigger trigger, EventTriggerType type, UnityEngine.Events.UnityAction<BaseEventData> action)
        {
            var entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(action);
            trigger.triggers.Add(entry);
        }

        private static Sprite CreateRoundedSprite(int width, int height, int cornerRadius)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;

            Color[] colors = new Color[width * height];
            float r = cornerRadius;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float dx = 0;
                    float dy = 0;

                    if (x < r) dx = r - x;
                    else if (x > width - r - 1) dx = x - (width - r - 1);

                    if (y < r) dy = r - y;
                    else if (y > height - r - 1) dy = y - (height - r - 1);

                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float alpha = dist > r ? 0f : (dist > r - 1f ? (r - dist) : 1f);

                    colors[y * width + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            tex.SetPixels(colors);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f);
        }

        private void CreateDockButton(Transform parent, string goName, string title, string sub, Color accent, Vector2 pos, UnityEngine.Events.UnityAction onClick)
        {
            var btnGo = new GameObject(goName, typeof(RectTransform), typeof(Image), typeof(Button));
            btnGo.transform.SetParent(parent, false);

            var rt = btnGo.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(54f, 52f);

            var img = btnGo.GetComponent<Image>();
            img.color = buttonBgNormal;
            img.sprite = CreateRoundedSprite(64, 64, 10);

            var outl = btnGo.AddComponent<Outline>();
            outl.effectColor = new Color(accent.r, accent.g, accent.b, 0.65f);
            outl.effectDistance = new Vector2(1.2f, -1.2f);

            var btn = btnGo.GetComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(onClick);

            var trigger = btnGo.AddComponent<EventTrigger>();
            AddTriggerEntry(trigger, EventTriggerType.PointerEnter, (e) =>
            {
                img.color = buttonBgHover;
                rt.localScale = new Vector3(1.05f, 1.05f, 1f);
            });
            AddTriggerEntry(trigger, EventTriggerType.PointerExit, (e) =>
            {
                img.color = buttonBgNormal;
                rt.localScale = Vector3.one;
            });

            var tGo = new GameObject("Title", typeof(RectTransform), typeof(TextMeshProUGUI));
            tGo.transform.SetParent(btnGo.transform, false);
            var tRt = tGo.GetComponent<RectTransform>();
            tRt.anchorMin = new Vector2(0f, 0.45f);
            tRt.anchorMax = new Vector2(1f, 1f);
            tRt.offsetMin = Vector2.zero;
            tRt.offsetMax = new Vector2(0f, -4f);

            var tTmp = tGo.GetComponent<TextMeshProUGUI>();
            tTmp.text = title;
            tTmp.fontSize = 11f;
            tTmp.alignment = TextAlignmentOptions.Center;
            tTmp.color = accent;
            tTmp.fontStyle = FontStyles.Bold;
            tTmp.raycastTarget = false;

            var sGo = new GameObject("Sub", typeof(RectTransform), typeof(TextMeshProUGUI));
            sGo.transform.SetParent(btnGo.transform, false);
            var sRt = sGo.GetComponent<RectTransform>();
            sRt.anchorMin = new Vector2(0f, 0f);
            sRt.anchorMax = new Vector2(1f, 0.45f);
            sRt.offsetMin = new Vector2(0f, 4f);
            sRt.offsetMax = Vector2.zero;

            var sTmp = sGo.GetComponent<TextMeshProUGUI>();
            sTmp.text = sub;
            sTmp.fontSize = 9f;
            sTmp.alignment = TextAlignmentOptions.Center;
            sTmp.color = new Color(0.8f, 0.85f, 0.9f, 0.9f);
            sTmp.raycastTarget = false;
        }

        public void SpawnOrSelectHusky()
        {
            var existing = FindObjectsByType<ArticulationBody>(FindObjectsSortMode.None)
                .Where(b => b.isRoot && b.name.ToLowerInvariant().Contains("husky"))
                .Select(b => b.gameObject)
                .FirstOrDefault();

            if (existing != null)
            {
                ShowToast("ACTIVE ROBOT", "Switched focus to Clearpath Husky", 1.0f, successGreen);
                AutoDismissToast(2.5f);
                SelectActiveRobot(existing);
                return;
            }

            string path = Path.Combine(Application.dataPath, "project/data/URDF/husky_unity/husky.urdf");
            if (!File.Exists(path)) path = Path.Combine(Application.dataPath, "husky_unity_urdf/husky.urdf");

            if (File.Exists(path))
            {
                StartImport(path);
            }
            else
            {
                ShowToast("NOT FOUND", "husky.urdf not found in project paths.", 1.0f, errorRed);
                AutoDismissToast(3.0f);
            }
        }

        public void SpawnOrSelectPerseverance()
        {
            var existing = FindObjectsByType<ArticulationBody>(FindObjectsSortMode.None)
                .Where(b => b.isRoot && (b.name.ToLowerInvariant().Contains("perseverance") || b.name.ToLowerInvariant().Contains("m2020")))
                .Select(b => b.gameObject)
                .FirstOrDefault();

            if (existing != null)
            {
                ShowToast("ACTIVE ROBOT", "Switched focus to NASA Perseverance", 1.0f, successGreen);
                AutoDismissToast(2.5f);
                SelectActiveRobot(existing);
                return;
            }

            string path = Path.Combine(Application.dataPath, "project/m2020-urdf-models-main/rover/m2020_sanitized.urdf");
            if (!File.Exists(path)) path = Path.Combine(Application.dataPath, "project/data/URDF/M2020_Unity/perseverance_m2020.urdf");

            if (File.Exists(path))
            {
                StartImport(path);
            }
            else
            {
                ShowToast("NOT FOUND", "m2020_sanitized.urdf not found in project paths.", 1.0f, errorRed);
                AutoDismissToast(3.0f);
            }
        }

        public void SpawnOrSelectGo2()
        {
            var existing = FindObjectsByType<ArticulationBody>(FindObjectsSortMode.None)
                .Where(b => b.isRoot && (b.name.ToLowerInvariant().Contains("go2") || b.name.ToLowerInvariant().Contains("go1")))
                .Select(b => b.gameObject)
                .FirstOrDefault();

            if (existing != null)
            {
                ShowToast("ACTIVE ROBOT", "Switched focus to Unitree Go2 Quadruped", 1.0f, successGreen);
                AutoDismissToast(2.5f);
                SelectActiveRobot(existing);
                return;
            }

            string path = Path.Combine(Application.dataPath, "ImportedRovers/Unitree Go2/go2_description/urdf/go2_description_sanitized.urdf");
            if (!File.Exists(path)) path = Path.Combine(Application.dataPath, "project/prefabs/Rover/go1.prefab");

            if (File.Exists(path))
            {
                StartImport(path);
            }
            else
            {
                ShowToast("NOT FOUND", "go2_description_sanitized.urdf not found in project paths.", 1.0f, errorRed);
                AutoDismissToast(3.0f);
            }
        }

        public void SelectActiveRobot(GameObject targetRobot)
        {
            if (targetRobot == null) return;

            // Pause / disable driving on all other robots in the scene so only active robot moves
            var allDiffs = FindObjectsByType<SimpleDifferentialDrive>(FindObjectsSortMode.None);
            foreach (var d in allDiffs)
            {
                if (d != null && d.gameObject != targetRobot)
                {
                    d.enabled = false;
                }
            }

            var allArtCtrls = FindObjectsByType<ArticulatedRobotController>(FindObjectsSortMode.None);
            foreach (var a in allArtCtrls)
            {
                if (a != null && a.gameObject != targetRobot)
                {
                    a.enableInteractiveDrive = false;
                }
            }

            // Enable driving on target robot
            var targetDiff = targetRobot.GetComponent<SimpleDifferentialDrive>() ?? targetRobot.GetComponentInChildren<SimpleDifferentialDrive>();
            if (targetDiff != null) targetDiff.enabled = true;

            var targetArt = targetRobot.GetComponent<ArticulatedRobotController>() ?? targetRobot.GetComponentInChildren<ArticulatedRobotController>();
            if (targetArt != null && targetDiff == null) targetArt.enableInteractiveDrive = true;

            BindActiveRobot(targetRobot);
            FocusCameraOnActiveRobot();
        }
    }
}

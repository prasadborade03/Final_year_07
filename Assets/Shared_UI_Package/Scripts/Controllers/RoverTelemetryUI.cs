using System;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ProjectName.Rover;
using ProjectName.Planetary;

namespace ProjectName.UI
{
    /// <summary>
    /// Aerospace-grade Mission Control Telemetry HUD for planetary rovers.
    /// Neatly docked to the right viewport, completely non-overlapping with workbench controls.
    /// Displays physically accurate ground-truth telemetry: real speed, pitch/roll attitude,
    /// heading, wheel RPM and traction slip matrix, world coordinates, elevation, odometer, battery, and thermals.
    /// Supports Full Sidebar, Compact Top-Right Bar, and Hidden modes via [Tab] / [H].
    /// </summary>
    [DisallowMultipleComponent]
    public class RoverTelemetryUI : MonoBehaviour
    {
        public enum HudDisplayMode
        {
            Full = 0,
            Compact = 1,
            Hidden = 2
        }

        [Header("Telemetry Source")]
        [Tooltip("Active rover telemetry provider. If null, auto-detects in scene.")]
        public RoverTelemetryProvider telemetryProvider;

        [Header("HUD Panels & Controls")]
        public GameObject hudRootPanel;
        public GameObject telemetrySidebar;
        public GameObject standbyBadge;
        public GameObject miniHudPill;
        public KeyCode toggleKey = KeyCode.H;
        public KeyCode alternateToggleKey = KeyCode.None;
        public HudDisplayMode displayMode = HudDisplayMode.Hidden;

        [Header("Text Displays")]
        public TextMeshProUGUI headerText;
        public TextMeshProUGUI speedText;
        public TextMeshProUGUI dynamicsText;
        public TextMeshProUGUI attitudeText;
        public TextMeshProUGUI compassSlopeText;
        public TextMeshProUGUI navCoordsText;
        public TextMeshProUGUI odoAltText;
        public TextMeshProUGUI wheelTelemetryText;
        public TextMeshProUGUI powerHealthText;
        public TextMeshProUGUI environmentText;
        public TextMeshProUGUI warningBannerText;
        public TextMeshProUGUI miniHudText;

        [Header("Gauges & Bars")]
        public Image speedProgressBar;
        public Image batteryProgressBar;

        [Header("Display Settings")]
        public bool showKmph = false;
        public float maxSpeedGauge = 6.0f; // m/s for full gauge bar

        private readonly StringBuilder sb = new StringBuilder(512);

        private void Start()
        {
            BuildOrUpdateHUD();
            FindActiveRover();
            UpdateVisibilityState();
        }

        private void Update()
        {
            // Cycle HUD display mode: Full -> Compact -> Hidden
            if (Input.GetKeyDown(toggleKey) || Input.GetKeyDown(alternateToggleKey))
            {
                displayMode = (HudDisplayMode)(((int)displayMode + 1) % 3);
                UpdateVisibilityState();
            }

            if (displayMode == HudDisplayMode.Hidden) return;

            // Re-acquire provider if needed
            if (telemetryProvider == null || !telemetryProvider.gameObject.activeInHierarchy)
            {
                FindActiveRover();
            }

            if (telemetryProvider != null && telemetryProvider.telemetry != null)
            {
                UpdateVisibilityState();
                UpdateHUD(telemetryProvider.telemetry);
            }
            else
            {
                UpdateVisibilityState();
            }
        }

        public void BindRover(RoverTelemetryProvider provider)
        {
            telemetryProvider = provider;
            displayMode = HudDisplayMode.Hidden;
            UpdateVisibilityState();
        }

        private void UpdateVisibilityState()
        {
            if (hudRootPanel != null)
            {
                hudRootPanel.SetActive(displayMode != HudDisplayMode.Hidden);
            }

            bool roverActive = telemetryProvider != null && telemetryProvider.telemetry != null;

            if (telemetrySidebar != null)
            {
                telemetrySidebar.SetActive(roverActive && displayMode == HudDisplayMode.Full);
            }

            if (miniHudPill != null)
            {
                miniHudPill.SetActive(roverActive && displayMode == HudDisplayMode.Compact);
            }

            if (standbyBadge != null)
            {
                standbyBadge.SetActive(!roverActive && displayMode != HudDisplayMode.Hidden);
            }
        }

        private void FindActiveRover()
        {
            telemetryProvider = FindAnyObjectByType<RoverTelemetryProvider>();
            if (telemetryProvider == null)
            {
                var rovers = GameObject.FindGameObjectsWithTag("Player");
                foreach (var r in rovers)
                {
                    var prov = r.GetComponentInChildren<RoverTelemetryProvider>();
                    if (prov != null)
                    {
                        telemetryProvider = prov;
                        break;
                    }
                }
            }
        }

        private void UpdateHUD(RoverTelemetryData data)
        {
            if (data == null) return;

            int minutes = Mathf.FloorToInt(data.missionTimeSeconds / 60f);
            int seconds = Mathf.FloorToInt(data.missionTimeSeconds % 60f);
            string rName = string.IsNullOrEmpty(data.roverName) ? "PLANETARY ROVER" : data.roverName.ToUpper();
            string pName = string.IsNullOrEmpty(data.planetName) ? "MARS" : data.planetName.ToUpper();

            // 0. Compact Mini HUD Pill
            if (miniHudText != null)
            {
                miniHudText.text = $"<color=#00E5FF><b>{rName}</b></color> | <b>{data.speedMps:F1} m/s</b> | BATT: <color=#00E676><b>{data.batteryPercent:F0}%</b></color> | <color=#FFD54F>{pName}</color> | <size=80%><color=#90A4AE>[Tab] Full</color></size>";
            }

            // 1. Header (Rover name, mode, MET)
            if (headerText != null)
            {
                headerText.text = $"<color=#00E5FF><b>{rName}</b></color>\n<size=75%><color=#00E676>ACTIVE</color> | MODE: <color=#FFD54F>{data.driveMode}</color> | MET: <color=#80D8FF>{minutes:00}:{seconds:00}</color></size>";
            }

            // 2. Speedometer & Acceleration
            if (speedText != null)
            {
                if (showKmph)
                    speedText.text = $"<b>{data.speedKmph:F2}</b> <size=60%>KM/H</size>";
                else
                    speedText.text = $"<b>{data.speedMps:F2}</b> <size=60%>M/S</size>";
            }

            if (dynamicsText != null)
            {
                dynamicsText.text = $"<size=80%>{data.speedKmph:F1} km/h | Accel: {data.forwardAcceleration:+0.00;-0.00} m/s² | G: {data.gForce:F2}</size>";
            }

            if (speedProgressBar != null)
            {
                speedProgressBar.fillAmount = Mathf.Clamp01(data.speedMps / maxSpeedGauge);
            }

            // 3. Attitude & Inclinometer
            if (attitudeText != null)
            {
                string pitchColor = Mathf.Abs(data.pitchDeg) > 20f ? (Mathf.Abs(data.pitchDeg) > 30f ? "#FF1744" : "#FFD600") : "#00E676";
                string rollColor = Mathf.Abs(data.rollDeg) > 15f ? (Mathf.Abs(data.rollDeg) > 25f ? "#FF1744" : "#FFD600") : "#00E676";
                attitudeText.text = $"PITCH: <color={pitchColor}><b>{data.pitchDeg:+0.0;-0.0}°</b></color>   ROLL: <color={rollColor}><b>{data.rollDeg:+0.0;-0.0}°</b></color>";
            }

            if (compassSlopeText != null)
            {
                string slopeColor = data.terrainSlopeDeg > 20f ? "#FF9100" : "#E0E0E0";
                compassSlopeText.text = $"HDG: <color=#00E5FF><b>{data.headingDeg:000}° {data.headingCardinal}</b></color>   SLOPE: <color={slopeColor}><b>{data.terrainSlopeDeg:F1}°</b></color>";
            }

            // 4. World Coordinates & Navigation
            if (navCoordsText != null)
            {
                navCoordsText.text = $"XYZ: <color=#80D8FF>{data.worldPosition.x:F1}, {data.worldPosition.y:F1}, {data.worldPosition.z:F1}</color>";
            }

            if (odoAltText != null)
            {
                odoAltText.text = $"ALT: <color=#80D8FF>{data.altitudeMeters:F1}m</color>   ODO: <color=#FFD54F><b>{data.odometerMeters:F2}m</b></color>";
            }

            // 5. Wheel Actuators Matrix (2 Columns)
            if (wheelTelemetryText != null)
            {
                sb.Clear();
                if (data.wheels != null && data.wheels.Length > 0)
                {
                    for (int i = 0; i < data.wheels.Length; i += 2)
                    {
                        var w1 = data.wheels[i];
                        string s1 = FormatWheelItem(w1);

                        if (i + 1 < data.wheels.Length)
                        {
                            var w2 = data.wheels[i + 1];
                            string s2 = FormatWheelItem(w2);
                            sb.AppendLine($"<size=82%>{s1,-20} | {s2}</size>");
                        }
                        else
                        {
                            sb.AppendLine($"<size=82%>{s1}</size>");
                        }
                    }
                }
                else
                {
                    sb.AppendLine("<size=80%>Awaiting wheel telemetry...</size>");
                }
                wheelTelemetryText.text = sb.ToString();
            }

            // 6. Subsystem Power & Thermals
            if (powerHealthText != null)
            {
                string batteryColor = data.batteryPercent < 20f ? "#FF1744" : (data.batteryPercent < 45f ? "#FFD600" : "#00E676");
                string tempColor = data.thermalWarning ? "#FF1744" : "#80D8FF";
                powerHealthText.text = $"BATT: <color={batteryColor}><b>{data.batteryPercent:F1}%</b></color>  LOAD: <color=#80D8FF>{data.powerDrawWatts:F0}W</color>  TEMP: <color={tempColor}><b>{data.motorTempCelsius:F1}°C</b></color>";
            }

            if (batteryProgressBar != null)
            {
                batteryProgressBar.fillAmount = Mathf.Clamp01(data.batteryPercent / 100f);
            }

            // 7. Planetary Environment (Live from Engine)
            if (environmentText != null)
            {
                float g = PlanetaryParameterReader.Instance != null ? PlanetaryParameterReader.Instance.GetGravity() : data.liveGravityMagnitude;
                float drag = PlanetaryParameterReader.Instance != null ? PlanetaryParameterReader.Instance.GetRoverDrag() : data.roverDrag;
                float wind = PlanetaryParameterReader.Instance != null ? PlanetaryParameterReader.Instance.GetWindSpeed() : data.liveWindSpeed;
                float sFric = PlanetaryParameterReader.Instance != null ? PlanetaryParameterReader.Instance.GetStaticFriction() : data.terrainStaticFriction;

                environmentText.text = $"PLANET: <color=#00E5FF><b>{pName}</b></color>   GRAV: <color=#FFD54F><b>{g:F2} m/s²</b></color>\n" +
                                       $"SOIL μs: <color=#80D8FF>{sFric:F2}</color>  DRAG: <color=#80D8FF>{drag:F2}</color>  WIND: <color=#80D8FF>{wind:F1} m/s</color>";
            }

            // 8. Rollover Hazard Warning Banner
            if (warningBannerText != null)
            {
                if (data.rolloverWarning)
                {
                    warningBannerText.gameObject.SetActive(true);
                    warningBannerText.text = "<color=#FF1744><b>CAUTION: ROLLOVER HAZARD EXCEEDED</b></color>";
                }
                else if (data.thermalWarning)
                {
                    warningBannerText.gameObject.SetActive(true);
                    warningBannerText.text = "<color=#FF9100><b>MOTOR OVERTEMPERATURE WARNING</b></color>";
                }
                else
                {
                    warningBannerText.gameObject.SetActive(false);
                }
            }
        }

        private string FormatWheelItem(WheelTelemetry w)
        {
            string status;
            string color;
            if (!w.isGrounded) { status = "AIR"; color = "#90A4AE"; }
            else if (w.slipRatio < 0.15f) { status = "GRIP"; color = "#00E676"; }
            else if (w.slipRatio < 0.40f) { status = "SLIP"; color = "#FFD600"; }
            else { status = "SPIN"; color = "#FF1744"; }

            string sn = ShortenWheelName(w.wheelName);
            return $"<b>{sn}</b> {w.rpm,4:F0} <color={color}>[{status}]</color>";
        }

        private string ShortenWheelName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "WHL";
            string lower = name.ToLower();
            if (lower.Contains("front") && lower.Contains("left")) return "FL";
            if (lower.Contains("front") && lower.Contains("right")) return "FR";
            if (lower.Contains("rear") && lower.Contains("left")) return "RL";
            if (lower.Contains("rear") && lower.Contains("right")) return "RR";
            if (lower.Contains("mid") && lower.Contains("left")) return "ML";
            if (lower.Contains("mid") && lower.Contains("right")) return "MR";
            if (lower.Contains("fl")) return "FL";
            if (lower.Contains("fr")) return "FR";
            if (lower.Contains("rl")) return "RL";
            if (lower.Contains("rr")) return "RR";
            return name.Length > 4 ? name.Substring(0, 4) : name;
        }

        /// <summary>
        /// Builds or reconfigures a sleek, right-docked aerospace telemetry HUD.
        /// Replaces any existing layout with a modern, non-overlapping glassmorphic sidebar.
        /// </summary>
        public void BuildOrUpdateHUD()
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas == null) canvas = FindAnyObjectByType<Canvas>();
            if (canvas == null)
            {
                Debug.LogWarning("[RoverTelemetryUI] No Canvas found in scene.");
                return;
            }

            // Destroy existing RoverTelemetryHUD if present to rebuild cleanly
            Transform oldHud = canvas.transform.Find("RoverTelemetryHUD");
            if (oldHud != null)
            {
                DestroyImmediate(oldHud.gameObject);
            }

            // Root HUD Container
            GameObject root = new GameObject("RoverTelemetryHUD", typeof(RectTransform));
            root.transform.SetParent(canvas.transform, false);
            RectTransform rootRt = root.GetComponent<RectTransform>();
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = Vector2.zero;
            rootRt.offsetMax = Vector2.zero;
            hudRootPanel = root;

            // Palette
            Color panelBgColor = new Color(0.04f, 0.08f, 0.14f, 0.94f);
            Color cardBgColor = new Color(0.08f, 0.14f, 0.22f, 0.88f);
            Color headerCyan = new Color(0.0f, 0.90f, 1.0f);
            Color textDim = new Color(0.70f, 0.80f, 0.90f);

            // =========================================================================
            // 1. STANDBY BADGE (Top-Right, shows only when waiting for rover deployment)
            // =========================================================================
            standbyBadge = CreatePanel(root.transform, "StandbyBadge",
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-16f, -16f), new Vector2(290f, 32f), panelBgColor);
            CreateTextMesh(standbyBadge.transform, "StandbyText",
                Vector2.zero, Vector2.one, new Vector2(10f, 0f), new Vector2(-10f, 0f),
                11, TextAlignmentOptions.Center, textDim,
                "MISSION CONTROL: STANDBY | [P] Planet Select");

            // =========================================================================
            // 2. MINI COMPACT HUD PILL (Top-Right, shows when in Compact Mode)
            // =========================================================================
            miniHudPill = CreatePanel(root.transform, "MiniHudPill",
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-16f, -16f), new Vector2(380f, 32f), panelBgColor);
            miniHudText = CreateTextMesh(miniHudPill.transform, "MiniHudText",
                Vector2.zero, Vector2.one, new Vector2(10f, 0f), new Vector2(-10f, 0f),
                11, TextAlignmentOptions.Center, Color.white,
                "ROVER TELEMETRY | [Tab] Full HUD");
            miniHudPill.SetActive(false);

            // =========================================================================
            // 3. RIGHT TELEMETRY SIDEBAR (Docked strictly to right viewport, 330px wide)
            // =========================================================================
            telemetrySidebar = CreatePanel(root.transform, "TelemetrySidebar",
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-12f, 0f), new Vector2(330f, 480f), panelBgColor);

            // Warning Banner at top of sidebar
            GameObject warnObj = CreatePanel(telemetrySidebar.transform, "WarningBanner",
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -4f), new Vector2(-16f, 26f), new Color(0.5f, 0.05f, 0.05f, 0.95f));
            warningBannerText = CreateTextMesh(warnObj.transform, "WarningText",
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                11, TextAlignmentOptions.Center, new Color(1f, 0.3f, 0.3f));
            warnObj.SetActive(false);

            float currentY = -10f;

            // --- HEADER CARD with [P] PLANET Button ---
            GameObject headerCard = CreatePanel(telemetrySidebar.transform, "HeaderCard",
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, currentY), new Vector2(-16f, 50f), cardBgColor);

            headerText = CreateTextMesh(headerCard.transform, "HeaderText",
                Vector2.zero, Vector2.one, new Vector2(10f, 2f), new Vector2(-80f, -2f),
                13, TextAlignmentOptions.MidlineLeft, Color.white);

            GameObject planetBtnObj = CreateButton(headerCard.transform, "BtnHeaderPlanet",
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-8f, 0f), new Vector2(68f, 28f),
                new Color(0f, 0.5f, 0.7f, 0.9f), "[P] PLANET", 10);
            Button pBtn = planetBtnObj.GetComponent<Button>();
            pBtn.onClick.AddListener(() => {
                if (PlanetSelectionUI.Instance != null) PlanetSelectionUI.Instance.ToggleModal();
            });

            currentY -= 56f;

            // --- CARD 1: SPEED & DYNAMICS ---
            GameObject speedCard = CreatePanel(telemetrySidebar.transform, "SpeedCard",
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, currentY), new Vector2(-16f, 74f), cardBgColor);

            CreateTextMesh(speedCard.transform, "SpeedTitle",
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(10f, -16f), new Vector2(-10f, 0f),
                10, TextAlignmentOptions.TopLeft, headerCyan, "KINEMATICS / SPEED");

            speedText = CreateTextMesh(speedCard.transform, "SpeedVal",
                new Vector2(0f, 0.32f), new Vector2(1f, 1f), new Vector2(10f, 0f), new Vector2(-10f, -10f),
                20, TextAlignmentOptions.MidlineLeft, Color.white);

            // Speed gauge
            GameObject speedGaugeBg = CreatePanel(speedCard.transform, "GaugeBg",
                new Vector2(0f, 0.26f), new Vector2(1f, 0.34f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 0f), new Vector2(-20f, 0f), new Color(0.15f, 0.22f, 0.30f, 1f));
            GameObject speedGaugeFill = CreatePanel(speedGaugeBg.transform, "GaugeFill",
                Vector2.zero, Vector2.one, new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero, headerCyan);
            speedProgressBar = speedGaugeFill.GetComponent<Image>();
            speedProgressBar.type = Image.Type.Filled;
            speedProgressBar.fillMethod = Image.FillMethod.Horizontal;

            dynamicsText = CreateTextMesh(speedCard.transform, "DynamicsVal",
                new Vector2(0f, 0f), new Vector2(1f, 0.24f), new Vector2(10f, 2f), new Vector2(-10f, 0f),
                10, TextAlignmentOptions.MidlineLeft, textDim);

            currentY -= 80f;

            // --- CARD 2: ATTITUDE & ORIENTATION ---
            GameObject attitudeCard = CreatePanel(telemetrySidebar.transform, "AttitudeCard",
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, currentY), new Vector2(-16f, 56f), cardBgColor);

            CreateTextMesh(attitudeCard.transform, "AttitudeTitle",
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(10f, -14f), new Vector2(-10f, 0f),
                10, TextAlignmentOptions.TopLeft, headerCyan, "ATTITUDE & ORIENTATION");

            attitudeText = CreateTextMesh(attitudeCard.transform, "AttitudeVal",
                new Vector2(0f, 0.40f), new Vector2(1f, 0.85f), new Vector2(10f, 0f), new Vector2(-10f, 0f),
                11, TextAlignmentOptions.MidlineLeft, Color.white);

            compassSlopeText = CreateTextMesh(attitudeCard.transform, "CompassVal",
                new Vector2(0f, 0f), new Vector2(1f, 0.42f), new Vector2(10f, 2f), new Vector2(-10f, 0f),
                11, TextAlignmentOptions.MidlineLeft, Color.white);

            currentY -= 62f;

            // --- CARD 3: NAVIGATION & ODOMETER ---
            GameObject navCard = CreatePanel(telemetrySidebar.transform, "NavCard",
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, currentY), new Vector2(-16f, 56f), cardBgColor);

            CreateTextMesh(navCard.transform, "NavTitle",
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(10f, -14f), new Vector2(-10f, 0f),
                10, TextAlignmentOptions.TopLeft, headerCyan, "POSITION & ODOMETRY");

            navCoordsText = CreateTextMesh(navCard.transform, "NavCoordsVal",
                new Vector2(0f, 0.42f), new Vector2(1f, 0.85f), new Vector2(10f, 0f), new Vector2(-10f, 0f),
                11, TextAlignmentOptions.MidlineLeft, Color.white);

            odoAltText = CreateTextMesh(navCard.transform, "OdoAltVal",
                new Vector2(0f, 0f), new Vector2(1f, 0.42f), new Vector2(10f, 2f), new Vector2(-10f, 0f),
                11, TextAlignmentOptions.MidlineLeft, Color.white);

            currentY -= 62f;

            // --- CARD 4: WHEEL ACTUATORS MATRIX ---
            GameObject wheelCard = CreatePanel(telemetrySidebar.transform, "WheelCard",
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, currentY), new Vector2(-16f, 70f), cardBgColor);

            CreateTextMesh(wheelCard.transform, "WheelTitle",
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(10f, -14f), new Vector2(-10f, 0f),
                10, TextAlignmentOptions.TopLeft, headerCyan, "ACTUATORS / WHEEL TRACTION MATRIX");

            wheelTelemetryText = CreateTextMesh(wheelCard.transform, "WheelVal",
                new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(10f, 4f), new Vector2(-10f, -16f),
                10, TextAlignmentOptions.TopLeft, Color.white);

            currentY -= 76f;

            // --- CARD 5: SUBSYSTEM HEALTH & POWER ---
            GameObject powerCard = CreatePanel(telemetrySidebar.transform, "PowerCard",
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, currentY), new Vector2(-16f, 56f), cardBgColor);

            CreateTextMesh(powerCard.transform, "PowerTitle",
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(10f, -14f), new Vector2(-10f, 0f),
                10, TextAlignmentOptions.TopLeft, headerCyan, "POWER & THERMAL SUBSYSTEMS");

            powerHealthText = CreateTextMesh(powerCard.transform, "PowerVal",
                new Vector2(0f, 0.30f), new Vector2(1f, 0.85f), new Vector2(10f, 0f), new Vector2(-10f, 0f),
                11, TextAlignmentOptions.MidlineLeft, Color.white);

            // Battery bar
            GameObject battGaugeBg = CreatePanel(powerCard.transform, "BattGaugeBg",
                new Vector2(0f, 0.10f), new Vector2(1f, 0.20f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 0f), new Vector2(-20f, 0f), new Color(0.15f, 0.22f, 0.30f, 1f));
            GameObject battGaugeFill = CreatePanel(battGaugeBg.transform, "BattGaugeFill",
                Vector2.zero, Vector2.one, new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero, new Color(0f, 0.9f, 0.45f));
            batteryProgressBar = battGaugeFill.GetComponent<Image>();
            batteryProgressBar.type = Image.Type.Filled;
            batteryProgressBar.fillMethod = Image.FillMethod.Horizontal;

            currentY -= 62f;

            // --- CARD 6: PLANETARY ENVIRONMENT ---
            GameObject envCard = CreatePanel(telemetrySidebar.transform, "EnvironmentCard",
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, currentY), new Vector2(-16f, 56f), cardBgColor);

            CreateTextMesh(envCard.transform, "EnvTitle",
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(10f, -14f), new Vector2(-10f, 0f),
                10, TextAlignmentOptions.TopLeft, headerCyan, "PLANETARY ENVIRONMENT (LIVE ENGINE)");

            environmentText = CreateTextMesh(envCard.transform, "EnvVal",
                new Vector2(0f, 0f), new Vector2(1f, 0.85f), new Vector2(10f, 0f), new Vector2(-10f, 0f),
                10, TextAlignmentOptions.MidlineLeft, Color.white);

            currentY -= 60f;

            // Footer hint
            CreateTextMesh(telemetrySidebar.transform, "FooterHint",
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(10f, 6f), new Vector2(-10f, 22f),
                9, TextAlignmentOptions.Center, textDim,
                "[Tab / H] Cycle Mode | [P] Planet Select | [V] Camera Chase");

            telemetrySidebar.SetActive(false);
            standbyBadge.SetActive(true);
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

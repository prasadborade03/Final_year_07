using UnityEngine;

namespace ProjectName.Rover
{
    /// <summary>
    /// Aerospace Runtime Debug Overlay toggled via [F3].
    /// Displays unfiltered ground-truth physics metrics, bound object identity,
    /// ArticulationBody state, and per-wheel actuator kinematics directly on screen.
    /// Non-negotiable rule R1: Zero fabricated data. Everything read directly from physics.
    /// </summary>
    [DisallowMultipleComponent]
    public class F3DebugOverlay : MonoBehaviour
    {
        [Header("Settings")]
        public KeyCode toggleKey = KeyCode.F3;
        public bool showOverlay = false;

        private GUIStyle boxStyle;
        private GUIStyle headerStyle;
        private GUIStyle textStyle;
        private GUIStyle alertStyle;
        private GUIStyle greenStyle;

        private float fpsSmoothed = 60f;
        private Vector2 scrollPos;

        private void Update()
        {
            if (Input.GetKeyDown(toggleKey))
            {
                showOverlay = !showOverlay;
            }

            float currentFps = 1f / Mathf.Max(0.0001f, Time.unscaledDeltaTime);
            fpsSmoothed = Mathf.Lerp(fpsSmoothed, currentFps, Time.unscaledDeltaTime * 4f);
        }

        private void InitStyles()
        {
            if (boxStyle != null) return;

            var bgTex = new Texture2D(1, 1);
            bgTex.SetPixel(0, 0, new Color(0.04f, 0.08f, 0.14f, 0.88f));
            bgTex.Apply();

            boxStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = bgTex },
                padding = new RectOffset(10, 10, 8, 8)
            };

            headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.2f, 0.8f, 1f) }
            };

            textStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                normal = { textColor = new Color(0.85f, 0.9f, 0.95f) }
            };

            alertStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(1f, 0.4f, 0.4f) }
            };

            greenStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.2f, 0.95f, 0.5f) }
            };
        }

        private void OnGUI()
        {
            if (!showOverlay) return;

            InitStyles();

            float w = 380f;
            float h = Mathf.Min(Screen.height - 40f, 560f);
            Rect panelRect = new Rect(15f, 20f, w, h);

            GUILayout.BeginArea(panelRect, boxStyle);
            scrollPos = GUILayout.BeginScrollView(scrollPos, GUILayout.Width(w - 20f), GUILayout.Height(h - 16f));

            GUILayout.Label("[F3] RUNTIME DIAGNOSTICS & TELEMETRY", headerStyle);
            GUILayout.Space(4);

            // Engine / Frame Metrics
            float dtMs = Time.unscaledDeltaTime * 1000f;
            float fixedDtMs = Time.fixedDeltaTime * 1000f;
            float fixedHz = 1f / Mathf.Max(0.0001f, Time.fixedDeltaTime);
            GUILayout.Label($"FPS: {fpsSmoothed:F1} ({dtMs:F1} ms) | Phys: {fixedDtMs:F1} ms ({fixedHz:F0} Hz) | Scale: {Time.timeScale:F1}", textStyle);
            GUILayout.Space(4);

            // Rover Identity & Source of Truth
            if (ActiveRoverContext.HasActiveRover)
            {
                var hnd = ActiveRoverContext.Current;
                string roverIdStr = !string.IsNullOrEmpty(hnd.roverId) ? hnd.roverId.ToUpper() : "UNKNOWN";
                string dName = (hnd.profile != null && !string.IsNullOrEmpty(hnd.profile.displayName)) ? hnd.profile.displayName : "Unknown";
                string goName = hnd.gameObject != null ? hnd.gameObject.name : "Null";
                int goHash = hnd.gameObject != null ? hnd.gameObject.GetHashCode() : 0;
                string sLabel = (hnd.profile != null && !string.IsNullOrEmpty(hnd.profile.steeringLabel)) ? hnd.profile.steeringLabel : "—";

                GUILayout.Label(string.Format("BOUND ROVER: {0} ({1})", roverIdStr, dName), greenStyle);
                GUILayout.Label(string.Format("GameObject: {0} [Hash: {1}]", goName, goHash), textStyle);
                GUILayout.Label(string.Format("Steering Mode: {0}", sLabel), textStyle);

                if (hnd.rootBody != null)
                {
                    GUILayout.Label($"Root ArticulationBody: {hnd.rootBody.name} | Immovable: {hnd.rootBody.immovable} | Gravity: {hnd.rootBody.useGravity}", textStyle);
                    GUILayout.Label($"Root LinVel: {hnd.rootBody.linearVelocity} | AngVel: {hnd.rootBody.angularVelocity}", textStyle);
                }
                else
                {
                    GUILayout.Label("Root ArticulationBody: NONE (Warning)", alertStyle);
                }

                GUILayout.Space(6);

                // Telemetry Data
                var t = hnd.telemetry;
                if (t != null)
                {
                    GUILayout.Label("--- TRUTHFUL KINEMATICS ---", headerStyle);
                    GUILayout.Label($"Linear Speed: {t.linearSpeedMps:F3} m/s ({t.groundSpeedKmph:F2} km/h)", textStyle);
                    GUILayout.Label($"Forward Accel: {t.forwardAcceleration:+0.00;-0.00} m/s² | G-Force: {t.gForceLoad:F2} g", textStyle);
                    GUILayout.Label($"Attitude: Pitch {t.pitchDeg:+0.0;-0.0}° | Roll {t.rollDeg:+0.0;-0.0}°", textStyle);
                    GUILayout.Label($"Heading: {t.headingDeg:000.0}° ({t.headingCardinal}) | Terrain Slope: {t.terrainSlopeDeg:F1}°", textStyle);

                    if (t.isRolloverWarning || t.isSteepSlopeWarning)
                    {
                        GUILayout.Label("! ATTITUDE SAFETY WARNING ACTIVE !", alertStyle);
                    }

                    GUILayout.Space(6);
                    GUILayout.Label("--- POSITION & NAVIGATION ---", headerStyle);
                    GUILayout.Label($"World: ({t.worldPosition.x:F2}, {t.worldPosition.y:F2}, {t.worldPosition.z:F2})", textStyle);
                    GUILayout.Label($"Local: East {t.localEastMeters:+0.00;-0.00} m | North {t.localNorthMeters:+0.00;-0.00} m", textStyle);
                    GUILayout.Label($"Odometer: {t.odometerMeters:F2} m | Mission Time: {t.missionTimeSeconds:F1} s", textStyle);

                    GUILayout.Space(6);
                    GUILayout.Label("--- POWER & THERMALS ---", headerStyle);
                    GUILayout.Label($"Power Draw: {t.powerDrawWatts:F0} W | Battery: {t.batteryPercent:F1}% | Motor Temp: {t.motorTempCelsius:F1} °C", textStyle);

                    GUILayout.Space(6);
                    GUILayout.Label($"--- ACTUATORS & TRACTION ({t.wheelStates.Length} WHEELS) ---", headerStyle);
                    for (int i = 0; i < t.wheelStates.Length; i++)
                    {
                        var ws = t.wheelStates[i];
                        string gText = ws.isGrounded ? "GND" : "AIR";
                        GUIStyle wStyle = ws.status == "GOOD" ? greenStyle : (ws.status == "SLIP" ? alertStyle : textStyle);
                        GUILayout.Label($"[{ws.wheelName}] {ws.status} ({gText}) | {ws.rpm:F1} RPM | Slip: {ws.slipRatio * 100f:F0}% | {ws.tangentialSpeedMps:F2} m/s", wStyle);
                    }
                }
                else
                {
                    GUILayout.Label("RoverTelemetry: Component missing on active rover!", alertStyle);
                }
            }
            else
            {
                GUILayout.Label("NO ROVER ACTIVE (ActiveRoverContext.Current == null)", alertStyle);
                GUILayout.Label("HUD State: STANDBY", textStyle);
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
    }
}

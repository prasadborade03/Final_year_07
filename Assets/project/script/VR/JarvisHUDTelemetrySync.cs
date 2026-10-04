using System;
using UnityEngine;
using UnityEngine.UIElements;
using ProjectName.VR;

namespace ProjectName.VR
{
    /// <summary>
    /// Synchronizes the Jarvis Iron Man HUD with real-time flight telemetry,
    /// compass heading, rover tracking range, and perspective controls.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class JarvisHUDTelemetrySync : MonoBehaviour
    {
        private UIDocument uiDocument;
        private VisualElement root;

        private Label lblBannerSubtext;
        private Label lblPerspectiveBadge;
        private Label lblAltitude;
        private Label lblSpeed;
        private Label lblHeading;
        private Label lblTargetRange;
        private Label lblVRStatus;

        private Button btnCamFront;
        private Button btnCamRear;
        private Button btnCamLeft;
        private Button btnCamRight;
        private Button btnCamTop;
        private Button btnCamFree;

        private Button btnDriverFront;
        private Button btnDriverRear;
        private Button btnDriverLeft;
        private Button btnDriverRight;
        private Button btnDriverTop;
        private Button btnDriverFree;

        private Transform mainCameraTransform;
        private Vector3 lastCamPos;
        private float measuredSpeed = 0f;
        private float lastPerspectiveCheck = -1f;

        private void OnEnable()
        {
            uiDocument = GetComponent<UIDocument>();
            if (uiDocument == null) return;

            root = uiDocument.rootVisualElement;
            if (root != null)
            {
                BindElements(root);
            }
            else
            {
                // In case root is built next frame
                Invoke(nameof(DeferredBind), 0.1f);
            }
        }

        private void DeferredBind()
        {
            if (uiDocument != null && uiDocument.rootVisualElement != null)
            {
                BindElements(uiDocument.rootVisualElement);
            }
        }

        private void BindElements(VisualElement r)
        {
            root = r;

            lblBannerSubtext = root.Q<Label>("LblJarvisBannerSubtext");
            lblPerspectiveBadge = root.Q<Label>("LblJarvisPerspectiveBadge");
            lblAltitude = root.Q<Label>("LblJarvisAltitude");
            lblSpeed = root.Q<Label>("LblJarvisSpeed");
            lblHeading = root.Q<Label>("LblJarvisHeading");
            lblTargetRange = root.Q<Label>("LblJarvisTargetRange");
            lblVRStatus = root.Q<Label>("LblJarvisVRStatus");

            btnCamFront = root.Q<Button>("BtnCamFront");
            btnCamRear = root.Q<Button>("BtnCamRear");
            btnCamLeft = root.Q<Button>("BtnCamLeft");
            btnCamRight = root.Q<Button>("BtnCamRight");
            btnCamTop = root.Q<Button>("BtnCamTop");
            btnCamFree = root.Q<Button>("BtnCamFree");

            btnDriverFront = root.Q<Button>("BtnDriverCamFront");
            btnDriverRear = root.Q<Button>("BtnDriverCamRear");
            btnDriverLeft = root.Q<Button>("BtnDriverCamLeft");
            btnDriverRight = root.Q<Button>("BtnDriverCamRight");
            btnDriverTop = root.Q<Button>("BtnDriverCamTop");
            btnDriverFree = root.Q<Button>("BtnDriverCamFree");

            if (btnCamFront != null) btnCamFront.clicked += () => SwitchPerspective(JarvisCameraFlightController.PerspectiveMode.Front);
            if (btnCamRear != null) btnCamRear.clicked += () => SwitchPerspective(JarvisCameraFlightController.PerspectiveMode.Rear);
            if (btnCamLeft != null) btnCamLeft.clicked += () => SwitchPerspective(JarvisCameraFlightController.PerspectiveMode.Left);
            if (btnCamRight != null) btnCamRight.clicked += () => SwitchPerspective(JarvisCameraFlightController.PerspectiveMode.Right);
            if (btnCamTop != null) btnCamTop.clicked += () => SwitchPerspective(JarvisCameraFlightController.PerspectiveMode.Top);
            if (btnCamFree != null) btnCamFree.clicked += () => SwitchPerspective(JarvisCameraFlightController.PerspectiveMode.FreeFly);

            if (btnDriverFront != null) btnDriverFront.clicked += () => SwitchPerspective(JarvisCameraFlightController.PerspectiveMode.Front);
            if (btnDriverRear != null) btnDriverRear.clicked += () => SwitchPerspective(JarvisCameraFlightController.PerspectiveMode.Rear);
            if (btnDriverLeft != null) btnDriverLeft.clicked += () => SwitchPerspective(JarvisCameraFlightController.PerspectiveMode.Left);
            if (btnDriverRight != null) btnDriverRight.clicked += () => SwitchPerspective(JarvisCameraFlightController.PerspectiveMode.Right);
            if (btnDriverTop != null) btnDriverTop.clicked += () => SwitchPerspective(JarvisCameraFlightController.PerspectiveMode.Top);
            if (btnDriverFree != null) btnDriverFree.clicked += () => SwitchPerspective(JarvisCameraFlightController.PerspectiveMode.FreeFly);
        }

        private void SwitchPerspective(JarvisCameraFlightController.PerspectiveMode mode)
        {
            var ctrl = JarvisCameraFlightController.Instance;
            if (ctrl != null)
            {
                ctrl.SetPerspective(mode);
            }

            var audio = JarvisAudioFeedback.Instance;
            if (audio != null)
            {
                audio.PlayPerspectiveSwitch();
            }
        }

        private void Update()
        {
            if (mainCameraTransform == null)
            {
                var cam = Camera.main;
                if (cam != null) mainCameraTransform = cam.transform;
            }

            if (mainCameraTransform == null) return;

            // Measure speed
            float dt = Time.unscaledDeltaTime;
            if (dt > 0.0001f)
            {
                float dist = Vector3.Distance(mainCameraTransform.position, lastCamPos);
                measuredSpeed = Mathf.Lerp(measuredSpeed, dist / dt, dt * 10f);
                lastCamPos = mainCameraTransform.position;
            }

            // Update Altitude
            if (lblAltitude != null)
            {
                lblAltitude.text = $"{mainCameraTransform.position.y:0.0} m";
            }

            // Update Airspeed
            if (lblSpeed != null)
            {
                lblSpeed.text = $"{measuredSpeed:0.0} m/s";
            }

            // Update Heading
            if (lblHeading != null)
            {
                float yaw = (mainCameraTransform.eulerAngles.y % 360f + 360f) % 360f;
                string cardinal = GetCardinalDirection(yaw);
                lblHeading.text = $"{yaw:000}° {cardinal}";
            }

            // Update Input Focus & Banner Subtext
            var ctrl = JarvisCameraFlightController.Instance;
            if (ctrl != null)
            {
                if (lblBannerSubtext != null)
                {
                    if (ctrl.activeInputFocus == JarvisCameraFlightController.InputFocus.Rover)
                    {
                        lblBannerSubtext.text = JarvisCameraFlightController.IsCameraFlyingActive
                            ? "RMB CAMERA FLIGHT (ROVER MUTED)"
                            : "INPUT: ROVER [C TO FLY CAM]";
                    }
                    else
                    {
                        lblBannerSubtext.text = "INPUT: FREE CAMERA [C TO DRIVE ROVER]";
                    }
                }

                var rover = ctrl.GetActiveRoverTransform();
                if (rover != null)
                {
                    float range = Vector3.Distance(mainCameraTransform.position, rover.position);
                    if (lblTargetRange != null) lblTargetRange.text = $"{range:0.0} m";
                }
                else
                {
                    if (lblTargetRange != null) lblTargetRange.text = "NO TGT";
                }

                // Update perspective badge
                if (lblPerspectiveBadge != null)
                {
                    lblPerspectiveBadge.text = $"[{ctrl.activePerspective.ToString().ToUpper()}]";
                }

                // Highlight active button
                HighlightActiveButton(ctrl.activePerspective);
            }

            // Update VR status
            if (lblVRStatus != null)
            {
                bool vr = JarvisCameraFlightController.IsVRHeadsetActive();
                lblVRStatus.text = vr ? "VR: ACTIVE (6DOF)" : "DESKTOP SIM";
            }
        }

        private void HighlightActiveButton(JarvisCameraFlightController.PerspectiveMode mode)
        {
            SetBtnActive(btnCamFront, mode == JarvisCameraFlightController.PerspectiveMode.Front);
            SetBtnActive(btnCamRear, mode == JarvisCameraFlightController.PerspectiveMode.Rear);
            SetBtnActive(btnCamLeft, mode == JarvisCameraFlightController.PerspectiveMode.Left);
            SetBtnActive(btnCamRight, mode == JarvisCameraFlightController.PerspectiveMode.Right);
            SetBtnActive(btnCamTop, mode == JarvisCameraFlightController.PerspectiveMode.Top);
            SetBtnActive(btnCamFree, mode == JarvisCameraFlightController.PerspectiveMode.FreeFly);

            SetBtnActive(btnDriverFront, mode == JarvisCameraFlightController.PerspectiveMode.Front);
            SetBtnActive(btnDriverRear, mode == JarvisCameraFlightController.PerspectiveMode.Rear);
            SetBtnActive(btnDriverLeft, mode == JarvisCameraFlightController.PerspectiveMode.Left);
            SetBtnActive(btnDriverRight, mode == JarvisCameraFlightController.PerspectiveMode.Right);
            SetBtnActive(btnDriverTop, mode == JarvisCameraFlightController.PerspectiveMode.Top);
            SetBtnActive(btnDriverFree, mode == JarvisCameraFlightController.PerspectiveMode.FreeFly);
        }

        private void SetBtnActive(Button btn, bool active)
        {
            if (btn == null) return;
            if (active)
            {
                if (!btn.ClassListContains("segmented-active")) btn.AddToClassList("segmented-active");
                if (!btn.ClassListContains("mode-pill-active")) btn.AddToClassList("mode-pill-active");
            }
            else
            {
                btn.RemoveFromClassList("segmented-active");
                btn.RemoveFromClassList("mode-pill-active");
            }
        }

        private static string GetCardinalDirection(float yaw)
        {
            if (yaw >= 337.5f || yaw < 22.5f) return "N";
            if (yaw >= 22.5f && yaw < 67.5f) return "NE";
            if (yaw >= 67.5f && yaw < 112.5f) return "E";
            if (yaw >= 112.5f && yaw < 157.5f) return "SE";
            if (yaw >= 157.5f && yaw < 202.5f) return "S";
            if (yaw >= 202.5f && yaw < 247.5f) return "SW";
            if (yaw >= 247.5f && yaw < 292.5f) return "W";
            return "NW";
        }
    }
}

using System;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using UnityEngine.XR;

namespace ProjectName.VR
{
    /// <summary>
    /// FloatingUIRecallController implements the B-button Studio UI Toggle & Recall-in-front pattern
    /// specified in §4.2 of the VR Control Scheme specification.
    ///
    /// Architectural Guarantees:
    /// 1. Toggle via B button (Right Controller Secondary Button):
    ///    - If visible -> SetActive(false) to hide without destroying data bindings or generating GC.
    ///    - If hidden -> recalls the UI to a comfortable world-space position in front of current gaze,
    ///      facing the user, then SetActive(true).
    /// 2. Gaze Calculation (§4.2):
    ///    - Ignores head pitch (flat forward vector on XZ plane) so looking down does not plant the panel on the floor.
    ///    - Position = camera.position + flatForward * recallDistance (~1.2 - 1.5m, default 1.35m).
    ///    - Height = camera.position.y - 0.1m (comfortably below eye line for readability).
    ///    - Rotation = LookRotation facing the player.
    /// 3. Non-sticky / World Anchored:
    ///    - The UI remains stationary in 3D world space once recalled. It is NOT head-locked or sticky.
    /// 4. Robust Fallback Inputs:
    ///    - Input System action reference or binding path.
    ///    - OpenXR InputDevices fallback for Meta Quest 2.
    ///    - Keyboard fallback ('B' and 'U') for XR Device Simulator & desktop play.
    /// </summary>
    [DisallowMultipleComponent]
    public class FloatingUIRecallController : MonoBehaviour
    {
        [Header("Target UI & Camera")]
        [Tooltip("The root GameObject of the Studio UI panel to toggle and recall.")]
        public GameObject studioUIRoot;

        [Tooltip("The XR tracking camera. If null, automatically resolves Camera.main.")]
        public Camera xrCamera;

        [Header("Spatial Recall Parameters (§4.2)")]
        [Tooltip("Distance in meters from the headset where the UI reappears (recommended 1.2 - 1.5m).")]
        [Range(0.8f, 3.0f)]
        public float recallDistance = 1.35f;

        [Tooltip("Vertical drop below eye level in meters for comfortable reading.")]
        public float verticalOffset = -0.1f;

        [Header("Input Bindings")]
#if ENABLE_INPUT_SYSTEM
        [Tooltip("Input Action for B button / Secondary Button on Right Controller.")]
        public InputActionProperty toggleAction;
#endif

        [Tooltip("Keyboard shortcut for simulator / desktop testing.")]
        public KeyCode keyboardShortcut = KeyCode.B;

        [Tooltip("Secondary keyboard shortcut for parity with HUD key.")]
        public KeyCode secondaryShortcut = KeyCode.U;

        [Header("State")]
        [SerializeField]
        private bool isUIVisible = true;

        private bool wasSecondaryButtonPressed = false;

        public bool IsUIVisible => isUIVisible;

        private void Awake()
        {
            if (xrCamera == null)
            {
                ResolveXRCamera();
            }

            if (studioUIRoot == null)
            {
                // Auto-detect common UI roots if not manually assigned
                var uiDoc = FindAnyObjectByType<UnityEngine.UIElements.UIDocument>();
                if (uiDoc != null)
                {
                    studioUIRoot = uiDoc.gameObject;
                }
                else
                {
                    var canvas = FindAnyObjectByType<Canvas>();
                    if (canvas != null)
                    {
                        studioUIRoot = canvas.gameObject;
                    }
                }
            }

            // Deactivate sticky mode on legacy positioner if present
            DeactivateLegacyStickyMode();
        }

        private void Start()
        {
            if (xrCamera == null)
            {
                ResolveXRCamera();
            }

            DeactivateLegacyStickyMode();

            if (studioUIRoot != null)
            {
                isUIVisible = studioUIRoot.activeSelf;
            }
        }

        private void OnEnable()
        {
#if ENABLE_INPUT_SYSTEM
            if (toggleAction.action != null)
            {
                toggleAction.action.Enable();
            }
#endif
        }

        private void OnDisable()
        {
#if ENABLE_INPUT_SYSTEM
            if (toggleAction.action != null)
            {
                toggleAction.action.Disable();
            }
#endif
        }

        private void Update()
        {
            if (CheckToggleInput())
            {
                ToggleStudioUI();
            }
        }

        /// <summary>
        /// Checks all input pathways: Input System action, OpenXR RightHand device, and Keyboard.
        /// </summary>
        private bool CheckToggleInput()
        {
            bool triggered = false;

            // 1. Input System Action
#if ENABLE_INPUT_SYSTEM
            if (toggleAction.action != null && toggleAction.action.WasPressedThisFrame())
            {
                triggered = true;
            }

            if (Keyboard.current != null)
            {
                if (Keyboard.current.bKey.wasPressedThisFrame || Keyboard.current.uKey.wasPressedThisFrame)
                {
                    triggered = true;
                }
            }
#endif

            // 2. OpenXR InputDevices fallback for Quest 2 Right Controller Secondary Button (B)
            var rightHand = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            if (rightHand.isValid)
            {
                if (rightHand.TryGetFeatureValue(UnityEngine.XR.CommonUsages.secondaryButton, out bool secondaryPressed))
                {
                    if (secondaryPressed && !wasSecondaryButtonPressed)
                    {
                        triggered = true;
                    }
                    wasSecondaryButtonPressed = secondaryPressed;
                }
            }

            // 3. Legacy input fallback
            if (!triggered)
            {
                if (Input.GetKeyDown(keyboardShortcut) || Input.GetKeyDown(secondaryShortcut))
                {
                    triggered = true;
                }
            }

            return triggered;
        }

        /// <summary>
        /// Toggles the Studio UI. Hides if visible, or recalls in front of player gaze if hidden.
        /// </summary>
        public void ToggleStudioUI()
        {
            if (studioUIRoot == null) return;

            if (isUIVisible)
            {
                HideUI();
            }
            else
            {
                RecallAndShowUI();
            }
        }

        /// <summary>
        /// Hides the UI panel via SetActive(false).
        /// </summary>
        public void HideUI()
        {
            if (studioUIRoot == null) return;

            studioUIRoot.SetActive(false);
            isUIVisible = false;
            Debug.Log("[FloatingUIRecallController] Studio UI hidden (SetActive=false).");
        }

        /// <summary>
        /// Recalls the UI panel directly in front of the player's gaze and activates it.
        /// </summary>
        public void RecallAndShowUI()
        {
            if (studioUIRoot == null) return;

            if (xrCamera == null)
            {
                ResolveXRCamera();
                if (xrCamera == null) return;
            }

            DeactivateLegacyStickyMode();

            // Spec §4.2 Math:
            // headForward = camera.transform.forward
            // flatForward = new Vector3(headForward.x, 0, headForward.z).normalized (ignore up/down tilt)
            Vector3 headForward = xrCamera.transform.forward;
            Vector3 flatForward = new Vector3(headForward.x, 0f, headForward.z).normalized;
            if (flatForward.sqrMagnitude < 0.001f)
            {
                flatForward = xrCamera.transform.forward;
            }

            // panelPosition = camera.transform.position + flatForward * recallDistance
            // panelPosition.y = camera.transform.position.y - 0.1f (slightly below eye line)
            Vector3 panelPosition = xrCamera.transform.position + flatForward * recallDistance;
            panelPosition.y = xrCamera.transform.position.y + verticalOffset;

            // panelRotation = Quaternion.LookRotation(panelPosition - camera.transform.position)
            Vector3 lookDirection = panelPosition - xrCamera.transform.position;
            Quaternion panelRotation = lookDirection.sqrMagnitude > 0.001f
                ? Quaternion.LookRotation(lookDirection, Vector3.up)
                : Quaternion.identity;

            studioUIRoot.SetActive(true);
            studioUIRoot.transform.SetPositionAndRotation(panelPosition, panelRotation);
            isUIVisible = true;

            Debug.Log($"[FloatingUIRecallController] Studio UI recalled in front of user at {panelPosition}. Fixed in world space.");
        }

        /// <summary>
        /// If a VRUIPositioner component exists on the target UI, switches it away from HeadLocked
        /// to WorldAnchor so it does not counteract the world-fixed recall behavior.
        /// </summary>
        private void DeactivateLegacyStickyMode()
        {
            if (studioUIRoot == null) return;

            var legacyPositioner = studioUIRoot.GetComponent<ProjectName.UI.VRUIPositioner>();
            if (legacyPositioner != null)
            {
                legacyPositioner.stickyMode = ProjectName.UI.VRUIPositioner.StickyMode.WorldAnchor;
                // Detach from camera if it was previously parented
                if (legacyPositioner.transform.parent != null && legacyPositioner.transform.parent.GetComponent<Camera>() != null)
                {
                    legacyPositioner.transform.SetParent(null, true);
                }
            }
        }

        private void ResolveXRCamera()
        {
            xrCamera = Camera.main;
            if (xrCamera == null)
            {
                xrCamera = FindAnyObjectByType<Camera>();
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (studioUIRoot != null && Application.isPlaying)
            {
                DeactivateLegacyStickyMode();
            }
        }
#endif
    }
}

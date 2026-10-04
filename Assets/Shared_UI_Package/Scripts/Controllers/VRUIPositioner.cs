using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using UnityEngine.XR;

namespace ProjectName.UI
{
    /// <summary>
    /// Iron Man Sticky VR HUD Controller.
    /// Positions and binds UI Toolkit UIDocuments and Canvases directly to the player's view in VR.
    /// Features:
    /// - HeadLocked: Strictly locked to headset view, always visible like an Iron Man helmet visor.
    /// - SmoothFollow: Holographic floating HUD with slight cinematic inertia.
    /// - WorldAnchor: Stationary in world space.
    /// - Smooth Hide/Show toggle via controller Secondary Button (B/Y) or Keyboard [U].
    /// - Distance controls (+/-) and Recenter ([R]).
    /// </summary>
    [DisallowMultipleComponent]
    public class VRUIPositioner : MonoBehaviour
    {
        public static VRUIPositioner Instance { get; private set; }

        public enum StickyMode
        {
            [Tooltip("Iron Man Helmet Visor: Strictly locked to headset view, always visible.")]
            HeadLocked,
            [Tooltip("Iron Man Hologram: Smoothly glides to follow head movement with inertia.")]
            SmoothFollow,
            [Tooltip("Stationary in world space.")]
            WorldAnchor
        }

        [Header("Iron Man HUD Configuration")]
        [Tooltip("Sticky mode: WorldAnchor = Stationary in world space (non-sticky).")]
        public StickyMode stickyMode = StickyMode.WorldAnchor;

        [Tooltip("Forward distance in meters from camera to UI panel. Optical sweet spot for Quest 2: 1.10m.")]
        public float forwardDistance = 1.10f;

        [Tooltip("Vertical offset relative to camera eye height (negative = slightly below eye level for comfortable reading).")]
        public float heightOffset = -0.08f;

        [Tooltip("Follow responsiveness when in SmoothFollow mode.")]
        public float smoothFollowSpeed = 6.0f;

        [Header("HUD Visibility & Fade")]
        [Tooltip("Is the HUD currently visible.")]
        public bool isHUDVisible = true;

        [Tooltip("Fade duration in seconds when hiding/showing the HUD.")]
        public float fadeDuration = 0.2f;

        [Header("Controls")]
        [Tooltip("Keyboard key to re-align or toggle modes.")]
        public KeyCode recenterKey = KeyCode.R;
        public KeyCode toggleModeKey = KeyCode.H;
        public KeyCode toggleHUDKey = KeyCode.U;

        private Camera targetCamera;
        private Transform originalParent;
        private bool isParented = false;

        private UIDocument uiDocument;
        private BoxCollider cachedCollider;
        private float currentOpacity = 1f;
        private float targetOpacity = 1f;
        private bool wasSecondaryButtonPressed = false;

        private const string PrefKey_HUDVisible = "VR_HUD_Visible";

        private void Awake()
        {
            Instance = this;
            originalParent = transform.parent;
            uiDocument = GetComponent<UIDocument>();
            cachedCollider = GetComponent<BoxCollider>();

            // Studio UI must always be visible and active on scene startup
            isHUDVisible = true;
            currentOpacity = 1f;
            targetOpacity = 1f;

            if (PlayerPrefs.HasKey(PrefKey_HUDVisible))
            {
                PlayerPrefs.DeleteKey(PrefKey_HUDVisible);
                PlayerPrefs.Save();
            }
        }

        private void Start()
        {
            EnsureWorldSpaceSettings();
            FindActiveCamera();
            ApplyStickyMode();
            ApplyHUDVisibility(instant: true);
        }

        private void OnEnable()
        {
            EnsureWorldSpaceSettings();
            FindActiveCamera();
            ApplyStickyMode();
        }

        private void OnDisable()
        {
            DetachFromCamera();
        }

        private void Update()
        {
            HandleInput();
            UpdateFade();
        }

        private void LateUpdate()
        {
            if (targetCamera == null || !targetCamera.gameObject.activeInHierarchy)
            {
                FindActiveCamera();
                if (targetCamera == null) return;
            }

            switch (stickyMode)
            {
                case StickyMode.HeadLocked:
                    UpdateHeadLocked();
                    break;

                case StickyMode.SmoothFollow:
                    UpdateSmoothFollow();
                    break;

                case StickyMode.WorldAnchor:
                    // Stays stationary at current world position
                    break;
            }
        }

        private void HandleInput()
        {
            bool triggerRecenter = false;
            bool triggerModeToggle = false;
            bool triggerHUDToggle = false;

            // Check if FloatingUIRecallController is active in the scene.
            // If present, let it exclusively handle the B-button / Secondary button to prevent dual-trigger conflicts.
            bool hasRecallController = ProjectName.VR.FloatingUIRecallController.Instance != null ||
                                       FindFirstObjectByType<ProjectName.VR.FloatingUIRecallController>() != null;

            if (!hasRecallController)
            {
                // 1. Controller Secondary Button (B on Right controller, Y on Left controller)
                bool secondaryDown = false;
                var leftHand = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
                if (leftHand.isValid && leftHand.TryGetFeatureValue(UnityEngine.XR.CommonUsages.secondaryButton, out bool lSec) && lSec)
                {
                    secondaryDown = true;
                }
                var rightHand = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
                if (rightHand.isValid && rightHand.TryGetFeatureValue(UnityEngine.XR.CommonUsages.secondaryButton, out bool rSec) && rSec)
                {
                    secondaryDown = true;
                }

                if (secondaryDown && !wasSecondaryButtonPressed)
                {
                    triggerHUDToggle = true;
                }
                wasSecondaryButtonPressed = secondaryDown;
            }

            // 2. New Input System Keyboard
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null)
            {
                if (Keyboard.current.rKey.wasPressedThisFrame) triggerRecenter = true;
                if (Keyboard.current.hKey.wasPressedThisFrame) triggerModeToggle = true;
                if (!hasRecallController && Keyboard.current.uKey.wasPressedThisFrame) triggerHUDToggle = true;

                // Adjust distance with +/- or [/]
                if (Keyboard.current.equalsKey.wasPressedThisFrame || Keyboard.current.numpadPlusKey.wasPressedThisFrame)
                {
                    AdjustDistance(0.15f);
                }
                if (Keyboard.current.minusKey.wasPressedThisFrame || Keyboard.current.numpadMinusKey.wasPressedThisFrame)
                {
                    AdjustDistance(-0.15f);
                }
            }
#endif

            // 3. Legacy Input Fallback
            if (!triggerRecenter && Input.GetKeyDown(recenterKey)) triggerRecenter = true;
            if (!triggerModeToggle && Input.GetKeyDown(toggleModeKey)) triggerModeToggle = true;
            if (!hasRecallController && !triggerHUDToggle && Input.GetKeyDown(toggleHUDKey)) triggerHUDToggle = true;

            if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadPlus)) AdjustDistance(0.15f);
            if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus)) AdjustDistance(-0.15f);

            if (triggerRecenter) Recenter();
            if (triggerModeToggle) CycleStickyMode();
            if (triggerHUDToggle) ToggleHUD();
        }

        public void ForceShow()
        {
            isHUDVisible = true;
            targetOpacity = 1f;
            currentOpacity = 1f;
            ApplyHUDVisibility(instant: true);
        }

        public void AdjustDistance(float delta)
        {
            forwardDistance = Mathf.Clamp(forwardDistance + delta, 0.6f, 3.5f);
            Debug.Log($"[VRUIPositioner] HUD distance: {forwardDistance:F2}m");
            if (stickyMode == StickyMode.HeadLocked && isParented)
            {
                transform.localPosition = new Vector3(0f, heightOffset, forwardDistance);
            }
        }

        public void ToggleHUD()
        {
            SetHUDVisible(!isHUDVisible);
        }

        public void ShowHUD()
        {
            SetHUDVisible(true);
        }

        public void HideHUD()
        {
            SetHUDVisible(false);
        }

        public void SetHUDVisible(bool visible, bool instant = false)
        {
            isHUDVisible = visible;
            targetOpacity = visible ? 1f : 0f;
            PlayerPrefs.SetInt(PrefKey_HUDVisible, visible ? 1 : 0);
            PlayerPrefs.Save();

            if (instant)
            {
                currentOpacity = targetOpacity;
                ApplyHUDVisibility(instant: true);
            }

            Debug.Log($"[VRUIPositioner] HUD visibility toggled: <b>{(visible ? "<color=#00E5FF>VISIBLE</color>" : "<color=#FF5252>HIDDEN</color>")}</b>");
        }

        private void UpdateFade()
        {
            if (Mathf.Abs(currentOpacity - targetOpacity) > 0.001f)
            {
                float step = Time.unscaledDeltaTime / Mathf.Max(0.01f, fadeDuration);
                currentOpacity = Mathf.MoveTowards(currentOpacity, targetOpacity, step);
                ApplyHUDVisibility(instant: false);
            }
        }

        private void ApplyHUDVisibility(bool instant)
        {
            if (uiDocument == null) uiDocument = GetComponent<UIDocument>();
            if (cachedCollider == null) cachedCollider = GetComponent<BoxCollider>();

            if (uiDocument != null && uiDocument.rootVisualElement != null)
            {
                var root = uiDocument.rootVisualElement;
                root.style.opacity = new StyleFloat(currentOpacity);

                if (currentOpacity <= 0.01f)
                {
                    root.style.display = DisplayStyle.None;
                    if (cachedCollider != null) cachedCollider.enabled = false;
                }
                else
                {
                    root.style.display = DisplayStyle.Flex;
                    if (cachedCollider != null) cachedCollider.enabled = true;
                }
            }
        }

        public void CycleStickyMode()
        {
            if (stickyMode == StickyMode.HeadLocked)
            {
                SetStickyMode(StickyMode.SmoothFollow);
            }
            else if (stickyMode == StickyMode.SmoothFollow)
            {
                SetStickyMode(StickyMode.WorldAnchor);
            }
            else
            {
                SetStickyMode(StickyMode.HeadLocked);
            }
        }

        public void SetStickyMode(StickyMode mode)
        {
            stickyMode = mode;
            ApplyStickyMode();
            Debug.Log($"[VRUIPositioner] Sticky Mode set to: <color=#00E5FF><b>{stickyMode}</b></color>");
        }

        private void ApplyStickyMode()
        {
            if (targetCamera == null) FindActiveCamera();
            if (targetCamera == null) return;

            if (stickyMode == StickyMode.HeadLocked)
            {
                if (transform.parent != targetCamera.transform)
                {
                    transform.SetParent(targetCamera.transform, false);
                    isParented = true;
                }
                transform.localPosition = new Vector3(0f, heightOffset, forwardDistance);
                transform.localRotation = Quaternion.identity;
                transform.localScale = Vector3.one;
            }
            else if (stickyMode == StickyMode.SmoothFollow)
            {
                DetachFromCamera();
                Recenter();
            }
            else // WorldAnchor
            {
                DetachFromCamera();
                // In WorldAnchor mode, keep the current world position (do NOT overwrite with Recenter)
            }
        }

        private void DetachFromCamera()
        {
            if (isParented)
            {
                transform.SetParent(originalParent, true);
                isParented = false;
            }
        }

        private void UpdateHeadLocked()
        {
            if (isParented) DetachFromCamera();

            Vector3 camPos = targetCamera.transform.position;
            Vector3 camForward = targetCamera.transform.forward;
            Vector3 camUp = targetCamera.transform.up;

            transform.position = camPos + (camForward * forwardDistance) + (camUp * heightOffset);
            transform.rotation = Quaternion.LookRotation(camForward, camUp);
        }

        private void UpdateSmoothFollow()
        {
            if (isParented) DetachFromCamera();

            Vector3 camPos = targetCamera.transform.position;
            Vector3 camForward = targetCamera.transform.forward;
            Vector3 camUp = targetCamera.transform.up;

            Vector3 desiredPos = camPos + (camForward * forwardDistance) + (camUp * heightOffset);
            Quaternion desiredRot = Quaternion.LookRotation(camForward, camUp);

            float t = 1f - Mathf.Exp(-smoothFollowSpeed * Time.unscaledDeltaTime);
            transform.position = Vector3.Lerp(transform.position, desiredPos, t);
            transform.rotation = Quaternion.Slerp(transform.rotation, desiredRot, t);
        }

        public void Recenter()
        {
            FindActiveCamera();
            if (targetCamera == null) return;

            if (stickyMode == StickyMode.HeadLocked)
            {
                UpdateHeadLocked();
            }
            else
            {
                Vector3 camPos = targetCamera.transform.position;
                Vector3 camForward = targetCamera.transform.forward;
                Vector3 camUp = targetCamera.transform.up;

                transform.position = camPos + (camForward * forwardDistance) + (camUp * heightOffset);
                transform.rotation = Quaternion.LookRotation(camForward, camUp);
            }

            Debug.Log($"[VRUIPositioner] Iron Man HUD aligned with camera '{targetCamera.name}'. Mode: {stickyMode}");
        }

        public void FindActiveCamera()
        {
            targetCamera = Camera.main;

            if (targetCamera == null || !targetCamera.gameObject.activeInHierarchy)
            {
                var cams = FindObjectsByType<Camera>(FindObjectsSortMode.None);
                foreach (var c in cams)
                {
                    if (c.enabled && c.gameObject.activeInHierarchy)
                    {
                        targetCamera = c;
                        break;
                    }
                }
            }
        }

        public void EnsureWorldSpaceSettings()
        {
            uiDocument = GetComponent<UIDocument>();
            if (uiDocument != null && uiDocument.panelSettings != null)
            {
                SetPanelSettingsWorldSpace(uiDocument.panelSettings);
            }

            cachedCollider = GetComponent<BoxCollider>();
            if (cachedCollider == null)
            {
                cachedCollider = gameObject.AddComponent<BoxCollider>();
                cachedCollider.isTrigger = true;
                cachedCollider.center = Vector3.zero;
                cachedCollider.size = new Vector3(1.92f, 1.08f, 0.05f);
            }
        }

        public static void SetPanelSettingsWorldSpace(PanelSettings panelSettings)
        {
            if (panelSettings == null) return;
            var prop = typeof(PanelSettings).GetProperty("renderMode");
            if (prop != null && prop.CanWrite)
            {
                var enumType = prop.PropertyType;
                object targetVal = null;
                try
                {
                    targetVal = Enum.Parse(enumType, "WorldSpace");
                }
                catch
                {
                    try
                    {
                        targetVal = Enum.Parse(enumType, "World");
                    }
                    catch
                    {
                        targetVal = Enum.ToObject(enumType, 1);
                    }
                }

                if (targetVal != null)
                {
                    try
                    {
                        prop.SetValue(panelSettings, targetVal);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning("[VRUIPositioner] Failed to set renderMode on PanelSettings: " + ex.Message);
                    }
                }
            }
        }
    }
}

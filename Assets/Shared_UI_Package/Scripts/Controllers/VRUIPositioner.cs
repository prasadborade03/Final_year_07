using System;
using UnityEngine;
using UnityEngine.UIElements;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace ProjectName.UI
{
    /// <summary>
    /// Iron Man Sticky VR HUD Controller.
    /// Positions and binds UI Toolkit UIDocuments and Canvases directly to the player's view in VR.
    /// Supports:
    /// - HeadLocked: 100% sticky helmet visor (Iron Man HUD) that stays in view all the time.
    /// - SmoothFollow: Holographic floating HUD with slight cinematic inertia.
    /// - WorldAnchor: Fixed in world space.
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
        [Tooltip("Sticky mode: HeadLocked = Always in view like Iron Man helmet visor.")]
        public StickyMode stickyMode = StickyMode.HeadLocked;

        [Tooltip("Forward distance in meters from camera to UI panel.")]
        public float forwardDistance = 1.65f;

        [Tooltip("Vertical offset relative to camera eye height (negative = slightly below eye level for comfortable reading).")]
        public float heightOffset = -0.08f;

        [Tooltip("Follow responsiveness when in SmoothFollow mode.")]
        public float smoothFollowSpeed = 6.0f;

        [Header("Controls")]
        [Tooltip("Keyboard key to re-align or toggle modes.")]
        public KeyCode recenterKey = KeyCode.R;
        public KeyCode toggleModeKey = KeyCode.H;

        private Camera targetCamera;
        private Transform originalParent;
        private bool isParented = false;

        private void Awake()
        {
            Instance = this;
            originalParent = transform.parent;
        }

        private void Start()
        {
            EnsureWorldSpaceSettings();
            FindActiveCamera();
            ApplyStickyMode();
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

#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null)
            {
                if (Keyboard.current.rKey.wasPressedThisFrame) triggerRecenter = true;
                if (Keyboard.current.hKey.wasPressedThisFrame) triggerModeToggle = true;

                // Adjust distance with +/- or [/]
                if (Keyboard.current.equalsKey.wasPressedThisFrame || Keyboard.current.numpadPlusKey.wasPressedThisFrame)
                {
                    forwardDistance = Mathf.Clamp(forwardDistance + 0.15f, 0.8f, 3.5f);
                    Debug.Log($"[VRUIPositioner] HUD distance: {forwardDistance:F2}m");
                }
                if (Keyboard.current.minusKey.wasPressedThisFrame || Keyboard.current.numpadMinusKey.wasPressedThisFrame)
                {
                    forwardDistance = Mathf.Clamp(forwardDistance - 0.15f, 0.8f, 3.5f);
                    Debug.Log($"[VRUIPositioner] HUD distance: {forwardDistance:F2}m");
                }
            }
#endif
            if (!triggerRecenter && Input.GetKeyDown(recenterKey)) triggerRecenter = true;
            if (!triggerModeToggle && Input.GetKeyDown(toggleModeKey)) triggerModeToggle = true;

            if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadPlus))
            {
                forwardDistance = Mathf.Clamp(forwardDistance + 0.15f, 0.8f, 3.5f);
                Debug.Log($"[VRUIPositioner] HUD distance: {forwardDistance:F2}m");
            }
            if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus))
            {
                forwardDistance = Mathf.Clamp(forwardDistance - 0.15f, 0.8f, 3.5f);
                Debug.Log($"[VRUIPositioner] HUD distance: {forwardDistance:F2}m");
            }

            if (triggerRecenter)
            {
                Recenter();
            }

            if (triggerModeToggle)
            {
                CycleStickyMode();
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
            else
            {
                DetachFromCamera();
                Recenter();
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
            if (transform.parent != targetCamera.transform)
            {
                transform.SetParent(targetCamera.transform, false);
                isParented = true;
            }

            // Keep local position locked in front of camera
            transform.localPosition = new Vector3(0f, heightOffset, forwardDistance);
            transform.localRotation = Quaternion.identity;
        }

        private void UpdateSmoothFollow()
        {
            if (isParented) DetachFromCamera();

            Vector3 camPos = targetCamera.transform.position;
            Vector3 camForward = targetCamera.transform.forward;
            Vector3 camUp = targetCamera.transform.up;

            Vector3 desiredPos = camPos + (camForward * forwardDistance) + (camUp * heightOffset);
            Quaternion desiredRot = Quaternion.LookRotation(camForward, camUp);

            transform.position = Vector3.Lerp(transform.position, desiredPos, Time.deltaTime * smoothFollowSpeed);
            transform.rotation = Quaternion.Slerp(transform.rotation, desiredRot, Time.deltaTime * smoothFollowSpeed);
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
            var uiDoc = GetComponent<UIDocument>();
            if (uiDoc != null && uiDoc.panelSettings != null)
            {
                SetPanelSettingsWorldSpace(uiDoc.panelSettings);
            }

            var boxCol = GetComponent<BoxCollider>();
            if (boxCol == null)
            {
                boxCol = gameObject.AddComponent<BoxCollider>();
                boxCol.isTrigger = true;
                boxCol.center = Vector3.zero;
                boxCol.size = new Vector3(1.92f, 1.08f, 0.05f);
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

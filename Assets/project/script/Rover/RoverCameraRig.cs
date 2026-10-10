using System;
using System.Collections;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using ProjectName.VR;

namespace ProjectName.Rover
{
    /// <summary>
    /// Production Jitter-Free Follow & Perspective Camera Rig for simulated planetary rovers.
    /// Supports both Flat/Desktop and VR (OpenXR / Meta Quest 2).
    /// 
    /// Key architectural guarantees:
    /// 1. LateUpdate synchronization: updates after ArticulationBody physics ticks, eliminating micro-stutter.
    /// 2. Horizontal heading-relative frame: projects target.forward onto the horizontal XZ plane
    ///    so chassis pitch and roll over rocky terrain do not induce camera horizon wobble.
    /// 3. Terrain collision clearance: samples terrain height and clamps elevation >= 0.5m above ground.
    /// 4. 5 Rover-relative Presets (Rear, Left, Front, Right, Top) + Free 6DOF Flight mode.
    /// 5. Smooth cinematic blending (~0.5s) between perspectives with zero teleport snaps.
    /// 6. Seamless Free-Fly handoff: entering Free preserves the exact current camera pose.
    /// 7. VR safety: in VR, rig translates with rover while user's head rotation is controlled purely by HMD tracking.
    /// 8. Camera speed tuner integration: shared speed setting across Flat and VR free camera.
    /// 9. 'V' key cycles camera perspectives in sequence: Rear -> Left -> Front -> Right -> Top -> Free.
    /// 10. 'F' key focuses on the active rover (smooth blend to Rear Follow).
    /// </summary>
    public class RoverCameraRig : MonoBehaviour
    {
        private static RoverCameraRig _instance;
        public static RoverCameraRig Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = UnityEngine.Object.FindAnyObjectByType<RoverCameraRig>();
                    if (_instance == null)
                    {
                        var origin = GameObject.Find("XR Origin");
                        if (origin != null)
                        {
                            _instance = origin.AddComponent<RoverCameraRig>();
                        }
                        else if (Camera.main != null)
                        {
                            _instance = Camera.main.gameObject.AddComponent<RoverCameraRig>();
                        }
                    }
                }
                return _instance;
            }
            private set => _instance = value;
        }

        public enum Perspective
        {
            Rear,
            Left,
            Front,
            Right,
            Top,
            Free
        }

        [Header("State & Perspective")]
        public Perspective currentPerspective = Perspective.Free;

        [Header("Cinematic Transitions")]
        [Tooltip("Smooth blending transition duration in seconds (Flat/Desktop mode)")]
        [Range(0.2f, 1.5f)]
        public float transitionDuration = 0.35f;

        [Header("VR Comfort Transitions")]
        [Tooltip("Duration of comfort fade-out to black in seconds")]
        public float vrFadeOutDuration = 0.10f;
        [Tooltip("Duration of comfort fade-in from black in seconds")]
        public float vrFadeInDuration = 0.15f;

        [Header("Follow Smoothing")]
        [Tooltip("Position damping time in seconds (SmoothDamp)")]
        public float smoothTime = 0.14f;
        [Tooltip("Rotation angular damping speed (Slerp factor)")]
        public float rotationDamping = 8f;

        [Header("Controls Sensitivity (Follow Modes)")]
        public float orbitSensitivity = 3f;
        public float zoomSensitivity = 0.5f;
        public float minZoomScale = 0.4f;
        public float maxZoomScale = 2.5f;

        [Header("Clearance & Collision")]
        public float minTerrainClearance = 0.5f;

        // Events
        public static event Action<Perspective> OnPerspectiveChanged;
        public static event Action<float> OnCameraSpeedChanged;

        // Shared Free Fly Speed setting
        private static float _freeFlySpeed = 12f;
        public static float FreeFlySpeed
        {
            get => _freeFlySpeed;
            set
            {
                _freeFlySpeed = Mathf.Clamp(value, 2f, 50f);
                PlayerPrefs.SetFloat("CamFreeSpeed", _freeFlySpeed);
                OnCameraSpeedChanged?.Invoke(_freeFlySpeed);
            }
        }

        // Target tracking
        private Transform targetTransform;
        private RoverProfile currentProfile;
        private Vector3 currentVelocity;
        private bool isTeleporting = false;
        private bool isFollowingActive = false;

        // Transition Blend State (Flat/Desktop mode)
        private bool isBlending = false;
        private float blendTimer = 0f;
        private Vector3 blendStartPos;
        private Quaternion blendStartRot;

        // VR Comfort Screen Fade State
        private GameObject vrFadeQuad;
        private Material vrFadeMaterial;
        private MeshRenderer vrFadeRenderer;
        private Coroutine vrFadeCoroutine;

        // Input edge detection & debounce
        private bool wasVrStickClicked = false;
        private float lastPerspectiveCycleTime = 0f;

        // Orbit & Zoom offsets
        private float orbitYaw = 0f;
        private float orbitPitch = 0f;
        private float zoomScale = 1.0f;

        // Camera hierarchy references
        private Camera targetCamera;
        private Vector3 childCameraOffset = Vector3.zero;

        public bool IsFollowingActive => isFollowingActive;
        public Transform TargetTransform => targetTransform;

        private void Awake()
        {
            if (_instance == null)
            {
                _instance = this;
            }

            _freeFlySpeed = PlayerPrefs.GetFloat("CamFreeSpeed", 12f);

            ResolveTargetCamera();
        }

        private void ResolveTargetCamera()
        {
            targetCamera = GetComponentInChildren<Camera>();
            if (targetCamera == null)
            {
                targetCamera = Camera.main;
            }

            if (targetCamera != null)
            {
                targetCamera.nearClipPlane = 0.1f;
                targetCamera.farClipPlane = 3000f;

                if (targetCamera.transform != transform)
                {
                    childCameraOffset = transform.InverseTransformPoint(targetCamera.transform.position);
                }
                else
                {
                    childCameraOffset = Vector3.zero;
                }
            }
        }

        private void OnEnable()
        {
            ActiveRoverContext.OnRoverActivated += HandleRoverActivated;
            ActiveRoverContext.OnRoverDestroyed += HandleRoverDestroyed;

            if (ActiveRoverContext.HasActiveRover)
            {
                HandleRoverActivated(ActiveRoverContext.Current);
            }
        }

        private void OnDisable()
        {
            ActiveRoverContext.OnRoverActivated -= HandleRoverActivated;
            ActiveRoverContext.OnRoverDestroyed -= HandleRoverDestroyed;
            ResetVRFadeOverlay();
        }

        private void Start()
        {
            if (targetCamera == null)
            {
                ResolveTargetCamera();
            }

            if (targetTransform == null && ActiveRoverContext.HasActiveRover)
            {
                HandleRoverActivated(ActiveRoverContext.Current);
            }
        }

        private void Update()
        {
            // Perspective cycling: 'V' key on desktop, or Right Thumbstick Click in VR
            bool cyclePressed = false;
            bool focusPressed = false;

#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null)
            {
                if (Keyboard.current.vKey.wasPressedThisFrame) cyclePressed = true;
                if (Keyboard.current.fKey.wasPressedThisFrame) focusPressed = true;
            }
#endif
            if (!cyclePressed && Input.GetKeyDown(KeyCode.V)) cyclePressed = true;
            if (!focusPressed && Input.GetKeyDown(KeyCode.F)) focusPressed = true;

            // VR Right Thumbstick click (edge-detected)
            if (!cyclePressed && CheckVRRightStickClick())
            {
                cyclePressed = true;
            }

            if (cyclePressed)
            {
                CyclePerspective();
            }
            else if (focusPressed)
            {
                FocusActiveRover();
            }

            // Orbit & Zoom user input handling (only in follow modes, not in Free fly)
            if (currentPerspective != Perspective.Free && targetTransform != null)
            {
                HandleManualOrbitAndZoom();
            }
        }

        private void LateUpdate()
        {
            // If desktop free-fly camera grab is currently active, do not overwrite transform
            if (DesktopFreeFlyCamera.IsFreeFlyGrabActive)
            {
                return;
            }

            // In Free mode, flight controllers own the transform
            if (currentPerspective == Perspective.Free)
            {
                return;
            }

            // During initial setup steps, do not pull camera away from setup framing
            if (!isFollowingActive)
            {
                return;
            }

            if (targetTransform == null)
            {
                if (ActiveRoverContext.HasActiveRover)
                {
                    HandleRoverActivated(ActiveRoverContext.Current);
                }
                else
                {
                    return;
                }
            }

            ComputeDesiredCameraPose(out Vector3 desiredCamPos, out Quaternion desiredCamRot);

            // Compute rig root position compensating for child camera offset
            Vector3 desiredRigPos = desiredCamPos - (desiredCamRot * childCameraOffset);

            bool vrMode = IsVRHeadsetActive();

            if (isTeleporting)
            {
                transform.position = desiredRigPos;
                if (!vrMode)
                {
                    transform.rotation = desiredCamRot;
                }
                currentVelocity = Vector3.zero;
                isTeleporting = false;
                isBlending = false;
            }
            else if (isBlending)
            {
                blendTimer += Time.deltaTime;
                float progress = Mathf.Clamp01(blendTimer / Mathf.Max(0.01f, transitionDuration));
                float smoothT = Mathf.SmoothStep(0f, 1f, progress);

                transform.position = Vector3.Lerp(blendStartPos, desiredRigPos, smoothT);
                if (!vrMode)
                {
                    transform.rotation = Quaternion.Slerp(blendStartRot, desiredCamRot, smoothT);
                }

                if (progress >= 1f)
                {
                    isBlending = false;
                    currentVelocity = Vector3.zero;
                }
            }
            else
            {
                // Smooth follow damping
                transform.position = Vector3.SmoothDamp(transform.position, desiredRigPos, ref currentVelocity, smoothTime);
                if (!vrMode)
                {
                    transform.rotation = Quaternion.Slerp(transform.rotation, desiredCamRot, Time.deltaTime * rotationDamping);
                }
            }
        }

        /// <summary>
        /// Computes the exact camera target position and orientation in world space.
        /// Uses horizontal yaw heading to eliminate terrain roll/pitch wobble.
        /// </summary>
        public void ComputeDesiredCameraPose(out Vector3 desiredPos, out Quaternion desiredRot)
        {
            if (targetTransform == null)
            {
                desiredPos = targetCamera != null ? targetCamera.transform.position : transform.position;
                desiredRot = targetCamera != null ? targetCamera.transform.rotation : transform.rotation;
                return;
            }

            // 1. Calculate look target center (slightly above root chassis)
            float lookHeight = 0.4f;
            if (currentProfile != null)
            {
                lookHeight = Mathf.Max(0.35f, currentProfile.spawnClearance * 0.9f);
            }
            Vector3 targetLookPos = targetTransform.position + Vector3.up * lookHeight;

            // 2. Horizontal heading vectors (decoupled from chassis slope pitch/roll)
            Vector3 forward = targetTransform.forward;
            Vector3 flatForward = Vector3.ProjectOnPlane(forward, Vector3.up).normalized;
            if (flatForward.sqrMagnitude < 0.001f) flatForward = Vector3.forward;
            Vector3 flatRight = Vector3.Cross(Vector3.up, flatForward).normalized;

            // 3. Base perspective distance and height scaled to rover profile
            float baseDist = 4.0f;
            float baseHeight = 1.8f;
            if (currentProfile != null)
            {
                if (currentProfile.id == "m2020")
                {
                    baseDist = 6.8f;
                    baseHeight = 2.8f;
                }
                else if (currentProfile.id == "husky")
                {
                    baseDist = 3.6f;
                    baseHeight = 1.6f;
                }
                else if (currentProfile.id == "m20")
                {
                    baseDist = 4.2f;
                    baseHeight = 1.8f;
                }
            }

            float effectiveDist = baseDist * zoomScale;
            float effectiveHeight = baseHeight * zoomScale;

            Vector3 baseOffset;
            Quaternion baseLookRot;

            switch (currentPerspective)
            {
                case Perspective.Front:
                    baseOffset = flatForward * effectiveDist + Vector3.up * (effectiveHeight * 0.7f);
                    baseLookRot = Quaternion.LookRotation((targetLookPos - (targetLookPos + baseOffset)).normalized, Vector3.up);
                    break;

                case Perspective.Left:
                    baseOffset = -flatRight * effectiveDist + Vector3.up * effectiveHeight;
                    baseLookRot = Quaternion.LookRotation((targetLookPos - (targetLookPos + baseOffset)).normalized, Vector3.up);
                    break;

                case Perspective.Right:
                    baseOffset = flatRight * effectiveDist + Vector3.up * effectiveHeight;
                    baseLookRot = Quaternion.LookRotation((targetLookPos - (targetLookPos + baseOffset)).normalized, Vector3.up);
                    break;

                case Perspective.Top:
                    baseOffset = Vector3.up * (effectiveDist * 1.6f);
                    baseLookRot = Quaternion.LookRotation(Vector3.down, flatForward);
                    break;

                case Perspective.Rear:
                default:
                    baseOffset = -flatForward * effectiveDist + Vector3.up * effectiveHeight;
                    baseLookRot = Quaternion.LookRotation((targetLookPos - (targetLookPos + baseOffset)).normalized, Vector3.up);
                    break;
            }

            // 4. Apply manual orbit rotation (if user has RMB-dragged in follow mode)
            if (Mathf.Abs(orbitYaw) > 0.01f || Mathf.Abs(orbitPitch) > 0.01f)
            {
                Quaternion orbitRot = Quaternion.Euler(orbitPitch, orbitYaw, 0f);
                baseOffset = orbitRot * baseOffset;
                baseLookRot = Quaternion.LookRotation((targetLookPos - (targetLookPos + baseOffset)).normalized, Vector3.up);
            }

            desiredPos = targetLookPos + baseOffset;
            desiredRot = baseLookRot;

            // 5. Terrain clearance clamp
            if (UnityEngine.Terrain.activeTerrain != null)
            {
                float terrainY = UnityEngine.Terrain.activeTerrain.SampleHeight(desiredPos) + UnityEngine.Terrain.activeTerrain.transform.position.y;
                if (desiredPos.y < terrainY + minTerrainClearance)
                {
                    desiredPos.y = terrainY + minTerrainClearance;
                    desiredRot = Quaternion.LookRotation((targetLookPos - desiredPos).normalized, Vector3.up);
                }
            }
        }

        private void HandleManualOrbitAndZoom()
        {
            if (IsPointerOverUI()) return;
            if (DesktopFreeFlyCamera.IsFreeFlyGrabActive) return;

            // RMB Orbit
            if (Input.GetMouseButton(1))
            {
                orbitYaw += Input.GetAxis("Mouse X") * orbitSensitivity;
                orbitPitch -= Input.GetAxis("Mouse Y") * orbitSensitivity;
                orbitPitch = Mathf.Clamp(orbitPitch, -20f, 75f);
            }

            // Mouse Scroll Zoom
            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                zoomScale = Mathf.Clamp(zoomScale - scroll * zoomSensitivity * 0.2f, minZoomScale, maxZoomScale);
            }
        }

        private bool IsPointerOverUI()
        {
            if (UnityEngine.EventSystems.EventSystem.current != null)
            {
                return UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();
            }
            return false;
        }

        /// <summary>
        /// Transitions to the specified camera perspective.
        /// In VR: Uses a brief comfort fade (~0.10s out, pose swap, ~0.15s in) to eliminate motion sickness.
        /// In Flat/Desktop: Uses a short game-like eased blend (~0.35s).
        /// Selecting Free preserves current camera pose and hands off to Free-Fly controls.
        /// </summary>
        public void SetPerspective(Perspective perspective, bool snapImmediate = false)
        {
            bool vrMode = IsVRHeadsetActive();

            if (vrMode && !snapImmediate)
            {
                if (vrFadeCoroutine != null) StopCoroutine(vrFadeCoroutine);
                vrFadeCoroutine = StartCoroutine(DoVRComfortPerspectiveSwitch(perspective));
                return;
            }

            ExecutePerspectiveSwitch(perspective, snapImmediate);
        }

        private void ExecutePerspectiveSwitch(Perspective perspective, bool snapImmediate)
        {
            currentPerspective = perspective;
            orbitYaw = 0f;
            orbitPitch = 0f;
            zoomScale = 1.0f;

            Vector3 currentPos = targetCamera != null ? targetCamera.transform.position : transform.position;
            Quaternion currentRot = targetCamera != null ? targetCamera.transform.rotation : transform.rotation;

            if (perspective == Perspective.Free)
            {
                // Free camera: preserves current instantaneous camera pose!
                isFollowingActive = false;
                isBlending = false;

                if (DesktopFreeFlyCamera.Instance != null)
                {
                    DesktopFreeFlyCamera.Instance.SyncPoseFromCurrent(currentPos, currentRot);
                }

                if (FreeFlyRigController.Instance != null)
                {
                    FreeFlyRigController.Instance.enabled = true;
                }

                Debug.Log($"[RoverCameraRig] Handed off to Free-Fly mode preserving pose: {currentPos}");
            }
            else
            {
                // Returning to a rover follow perspective
                isFollowingActive = true;

                if (DesktopFreeFlyCamera.Instance != null && DesktopFreeFlyCamera.Instance.isGrabActive)
                {
                    DesktopFreeFlyCamera.Instance.EndGrabMode();
                }

                ComputeDesiredCameraPose(out Vector3 desiredCamPos, out Quaternion desiredCamRot);
                Vector3 desiredRigPos = desiredCamPos - (desiredCamRot * childCameraOffset);

                if (snapImmediate)
                {
                    transform.position = desiredRigPos;
                    if (!IsVRHeadsetActive())
                    {
                        transform.rotation = desiredCamRot;
                    }
                    currentVelocity = Vector3.zero;
                    isTeleporting = false;
                    isBlending = false;
                }
                else
                {
                    // Start smooth cinematic blend from current instantaneous pose (Flat/Desktop mode)
                    blendStartPos = transform.position;
                    blendStartRot = currentRot;
                    blendTimer = 0f;
                    isBlending = true;
                }
            }

            OnPerspectiveChanged?.Invoke(currentPerspective);
            Debug.Log($"[RoverCameraRig] Perspective set to: {currentPerspective} (snap: {snapImmediate})");
        }

        /// <summary>
        /// New default state on rover placement / mission start:
        /// Default camera mode is FREE FLY, positioned at the rear vantage point framing the active rover,
        /// but NOT automatically locked to follow the rover once active.
        /// </summary>
        public void InitializeFreeFlyAtRoverRear()
        {
            if (ActiveRoverContext.HasActiveRover)
            {
                HandleRoverActivated(ActiveRoverContext.Current);
            }

            // Calculate ideal rear vantage point framing the active rover
            ComputePerspectiveCameraPose(Perspective.Rear, out Vector3 rearPos, out Quaternion rearRot);

            Vector3 desiredRigPos = rearPos - (rearRot * childCameraOffset);
            transform.position = desiredRigPos;

            bool vrMode = IsVRHeadsetActive();
            if (!vrMode)
            {
                transform.rotation = rearRot;
            }

            currentVelocity = Vector3.zero;
            isTeleporting = false;
            isBlending = false;

            // Default camera mode is FREE FLY (NOT automatically locked to follow rover)
            currentPerspective = Perspective.Free;
            isFollowingActive = false;
            orbitYaw = 0f;
            orbitPitch = 0f;
            zoomScale = 1.0f;

            if (DesktopFreeFlyCamera.Instance != null)
            {
                DesktopFreeFlyCamera.Instance.SyncPoseFromCurrent(rearPos, rearRot);
            }

            if (FreeFlyRigController.Instance != null)
            {
                FreeFlyRigController.Instance.enabled = true;
            }

            OnPerspectiveChanged?.Invoke(Perspective.Free);
            Debug.Log($"[RoverCameraRig] InitializeFreeFlyAtRoverRear complete -> Mode: FREE FLY at rear pose: {rearPos}");
        }

        public void ComputePerspectiveCameraPose(Perspective perspective, out Vector3 desiredPos, out Quaternion desiredRot)
        {
            Perspective saved = currentPerspective;
            currentPerspective = perspective;
            ComputeDesiredCameraPose(out desiredPos, out desiredRot);
            currentPerspective = saved;
        }

        /// <summary>
        /// Called when rover placement is finished and driving starts.
        /// Directs to default Free Fly initialized at rear rover pose.
        /// </summary>
        public void StartFollowActiveRover()
        {
            InitializeFreeFlyAtRoverRear();
        }

        /// <summary>
        /// Focuses on active rover (smooth blend to Rear Follow).
        /// </summary>
        public void FocusActiveRover()
        {
            if (ActiveRoverContext.HasActiveRover)
            {
                isFollowingActive = true;
                SetPerspective(Perspective.Rear, snapImmediate: false);
            }
        }

        /// <summary>
        /// Cycles perspective presets in sequence:
        /// FREE FLY -> REAR -> FRONT -> LEFT -> RIGHT -> UP (TOP) -> FREE FLY.
        /// Debounced to avoid rapid multi-stepping from a single physical click.
        /// </summary>
        public void CyclePerspective()
        {
            if (Time.unscaledTime - lastPerspectiveCycleTime < 0.22f) return;
            lastPerspectiveCycleTime = Time.unscaledTime;

            Perspective next;
            switch (currentPerspective)
            {
                case Perspective.Free:
                    next = Perspective.Rear;
                    break;
                case Perspective.Rear:
                    next = Perspective.Front;
                    break;
                case Perspective.Front:
                    next = Perspective.Left;
                    break;
                case Perspective.Left:
                    next = Perspective.Right;
                    break;
                case Perspective.Right:
                    next = Perspective.Top;
                    break;
                case Perspective.Top:
                default:
                    next = Perspective.Free;
                    break;
            }

            SetPerspective(next, false);
        }

        private void EnsureVRFadeOverlay()
        {
            if (vrFadeQuad != null && vrFadeRenderer != null && vrFadeMaterial != null) return;

            if (targetCamera == null)
            {
                ResolveTargetCamera();
                if (targetCamera == null) return;
            }

            Transform existing = targetCamera.transform.Find("VRComfortFadeQuad");
            if (existing != null)
            {
                vrFadeQuad = existing.gameObject;
                vrFadeRenderer = vrFadeQuad.GetComponent<MeshRenderer>();
                if (vrFadeRenderer != null)
                {
                    vrFadeMaterial = vrFadeRenderer.material;
                    return;
                }
            }

            vrFadeQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            vrFadeQuad.name = "VRComfortFadeQuad";

            var col = vrFadeQuad.GetComponent<Collider>();
            if (col != null) Destroy(col);

            vrFadeQuad.transform.SetParent(targetCamera.transform, false);
            float nearClip = targetCamera != null ? targetCamera.nearClipPlane : 0.1f;
            vrFadeQuad.transform.localPosition = new Vector3(0f, 0f, Mathf.Max(0.12f, nearClip + 0.04f));
            vrFadeQuad.transform.localRotation = Quaternion.identity;
            vrFadeQuad.transform.localScale = new Vector3(8f, 8f, 1f);

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("UI/Default");
            if (shader == null) shader = Shader.Find("Hidden/Internal-Colored");

            vrFadeMaterial = new Material(shader);
            vrFadeMaterial.name = "VRComfortFadeMat";
            vrFadeMaterial.color = new Color(0f, 0f, 0f, 0f);

            if (vrFadeMaterial.HasProperty("_Surface")) vrFadeMaterial.SetFloat("_Surface", 1f); // Transparent
            if (vrFadeMaterial.HasProperty("_Blend")) vrFadeMaterial.SetFloat("_Blend", 0f); // Alpha
            if (vrFadeMaterial.HasProperty("_BaseColor")) vrFadeMaterial.SetColor("_BaseColor", new Color(0f, 0f, 0f, 0f));
            vrFadeMaterial.renderQueue = 5000;

            vrFadeRenderer = vrFadeQuad.GetComponent<MeshRenderer>();
            vrFadeRenderer.material = vrFadeMaterial;
            vrFadeRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            vrFadeRenderer.receiveShadows = false;
            vrFadeRenderer.enabled = false;
        }

        private void SetFadeAlpha(float alpha)
        {
            if (vrFadeMaterial == null) return;
            Color c = new Color(0f, 0f, 0f, Mathf.Clamp01(alpha));
            vrFadeMaterial.color = c;
            if (vrFadeMaterial.HasProperty("_BaseColor"))
            {
                vrFadeMaterial.SetColor("_BaseColor", c);
            }
        }

        private void ResetVRFadeOverlay()
        {
            if (vrFadeCoroutine != null)
            {
                StopCoroutine(vrFadeCoroutine);
                vrFadeCoroutine = null;
            }
            SetFadeAlpha(0f);
            if (vrFadeRenderer != null)
            {
                vrFadeRenderer.enabled = false;
            }
        }

        private IEnumerator DoVRComfortPerspectiveSwitch(Perspective nextPerspective)
        {
            EnsureVRFadeOverlay();
            if (vrFadeRenderer != null)
            {
                vrFadeRenderer.enabled = true;
            }

            // 1. Brief comfort fade-out (~0.10s)
            float elapsed = 0f;
            float outDuration = Mathf.Max(0.02f, vrFadeOutDuration);
            while (elapsed < outDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                SetFadeAlpha(Mathf.Clamp01(elapsed / outDuration));
                yield return null;
            }
            SetFadeAlpha(1f);

            // 2. Discrete pose swap at peak black
            ExecutePerspectiveSwitch(nextPerspective, snapImmediate: true);

            // Small 1-frame hold at black
            yield return null;

            // 3. Comfort fade-in (~0.15s)
            elapsed = 0f;
            float inDuration = Mathf.Max(0.02f, vrFadeInDuration);
            while (elapsed < inDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                SetFadeAlpha(1f - Mathf.Clamp01(elapsed / inDuration));
                yield return null;
            }
            SetFadeAlpha(0f);

            if (vrFadeRenderer != null)
            {
                vrFadeRenderer.enabled = false;
            }
            vrFadeCoroutine = null;
        }

        private bool CheckVRRightStickClick()
        {
            bool isDown = false;
            var rightHand = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.RightHand);
            if (rightHand.isValid)
            {
                if (rightHand.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxisClick, out bool clickVal) && clickVal)
                {
                    isDown = true;
                }
            }

            bool pressedThisFrame = isDown && !wasVrStickClicked;
            wasVrStickClicked = isDown;
            return pressedThisFrame;
        }

        private void HandleRoverActivated(RoverHandle handle)
        {
            if (handle != null && handle.IsValid)
            {
                targetTransform = handle.rootBody != null ? handle.rootBody.transform : handle.rootGameObject.transform;
                currentProfile = handle.profile;
                Debug.Log($"[RoverCameraRig] Bound target to active rover: {handle.profile?.displayName ?? handle.rootGameObject.name}");
            }
        }

        private void HandleRoverDestroyed()
        {
            targetTransform = null;
            currentProfile = null;
            isBlending = false;
            Debug.Log("[RoverCameraRig] Active rover destroyed; camera holding position safely.");
        }

        public static bool IsVRHeadsetActive()
        {
            return DesktopFreeFlyCamera.IsVRHeadsetActive();
        }
    }
}

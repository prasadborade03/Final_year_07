using System;
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
        public Perspective currentPerspective = Perspective.Rear;

        [Header("Cinematic Transitions")]
        [Tooltip("Smooth blending transition duration in seconds")]
        [Range(0.2f, 1.5f)]
        public float transitionDuration = 0.5f;

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

        // Transition Blend State
        private bool isBlending = false;
        private float blendTimer = 0f;
        private Vector3 blendStartPos;
        private Quaternion blendStartRot;

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
            // Keyboard shortcut 'V' cycles perspectives in sequence
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
        /// Selecting Free preserves the current camera pose without snap/teleport.
        /// Selecting a rover perspective starts a smooth cinematic blend toward the calculated pose.
        /// </summary>
        public void SetPerspective(Perspective perspective, bool snapImmediate = false)
        {
            currentPerspective = perspective;
            orbitYaw = 0f;
            orbitPitch = 0f;
            zoomScale = 1.0f;

            Vector3 currentPos = targetCamera != null ? targetCamera.transform.position : transform.position;
            Quaternion currentRot = targetCamera != null ? targetCamera.transform.rotation : transform.rotation;

            if (perspective == Perspective.Free)
            {
                // CRITICAL REQUIREMENT: Free camera preserves current camera pose!
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
                if (DesktopFreeFlyCamera.Instance != null && DesktopFreeFlyCamera.Instance.isGrabActive)
                {
                    DesktopFreeFlyCamera.Instance.EndGrabMode();
                }

                if (snapImmediate)
                {
                    isTeleporting = true;
                    isBlending = false;
                }
                else
                {
                    // Start smooth cinematic blend from current instantaneous pose
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
        /// Called when rover placement is finished and driving starts.
        /// Transitions cleanly into default Rear Follow mode.
        /// </summary>
        public void StartFollowActiveRover()
        {
            isFollowingActive = true;
            if (ActiveRoverContext.HasActiveRover)
            {
                HandleRoverActivated(ActiveRoverContext.Current);
            }

            SetPerspective(Perspective.Rear, snapImmediate: false);
            Debug.Log("[RoverCameraRig] StartFollowActiveRover engaged -> Default Rear Follow activated.");
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
        /// Cycles perspective presets in sequence: Rear -> Left -> Front -> Right -> Top -> Free -> Rear.
        /// </summary>
        public void CyclePerspective()
        {
            Perspective next;
            switch (currentPerspective)
            {
                case Perspective.Rear:
                    next = Perspective.Left;
                    break;
                case Perspective.Left:
                    next = Perspective.Front;
                    break;
                case Perspective.Front:
                    next = Perspective.Right;
                    break;
                case Perspective.Right:
                    next = Perspective.Top;
                    break;
                case Perspective.Top:
                    next = Perspective.Free;
                    break;
                case Perspective.Free:
                default:
                    next = Perspective.Rear;
                    break;
            }

            SetPerspective(next, false);
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

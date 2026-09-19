using System;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace ProjectName.Rover
{
    /// <summary>
    /// Production Jitter-Free Follow Camera for simulated planetary rovers.
    /// 
    /// Key architectural guarantees:
    /// 1. LateUpdate synchronization: updates after ArticulationBody physics ticks, eliminating micro-stutter.
    /// 2. Horizontal heading-relative frame: projects target.forward onto the horizontal XZ plane
    ///    so chassis pitch and roll over rocky terrain do not induce camera horizon wobble.
    /// 3. Terrain collision clearance: samples terrain height and clamps elevation >= 0.5m above ground.
    /// 4. 5 Presets (Rear, Left, Front, Right, Top) + Free flight toggle.
    /// 5. Smooth orbit (RMB drag) and zoom (mouse scroll), with UI input guard.
    /// 6. 'V' key cycles camera perspectives in sequence: Rear -> Left -> Front -> Right -> Top.
    /// 7. Automatic compensation for child camera local offsets (e.g. XR Origin -> Camera Offset -> Main Camera).
    /// </summary>
    public class RoverCameraRig : MonoBehaviour
    {
        public static RoverCameraRig Instance { get; private set; }

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

        [Header("Smoothing")]
        [Tooltip("Position damping time in seconds (SmoothDamp)")]
        public float smoothTime = 0.12f;
        [Tooltip("Rotation angular damping speed (Slerp factor)")]
        public float rotationDamping = 10f;

        [Header("Controls Sensitivity")]
        public float orbitSensitivity = 3f;
        public float zoomSensitivity = 0.5f;
        public float minZoomScale = 0.4f;
        public float maxZoomScale = 2.5f;

        [Header("Clearance & Collision")]
        public float minTerrainClearance = 0.5f;

        // Events
        public static event Action<Perspective> OnPerspectiveChanged;

        // Target tracking
        private Transform targetTransform;
        private RoverProfile currentProfile;
        private Vector3 currentVelocity;
        private bool isTeleporting = false;

        // Orbit & Zoom offsets
        private float orbitYaw = 0f;
        private float orbitPitch = 0f;
        private float zoomScale = 1.0f;

        // FreeFlyCamera & Camera hierarchy references
        private FreeFlyCamera freeFlyCamera;
        private Camera targetCamera;
        private Vector3 childCameraOffset = Vector3.zero;

        private void Awake()
        {
            Instance = this;
            freeFlyCamera = GetComponent<FreeFlyCamera>();
            if (freeFlyCamera == null)
            {
                freeFlyCamera = GetComponentInChildren<FreeFlyCamera>();
            }

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
            if (targetTransform == null && ActiveRoverContext.HasActiveRover)
            {
                HandleRoverActivated(ActiveRoverContext.Current);
            }
        }

        private void Update()
        {
            // 'V' key cycles presets: Rear -> Left -> Front -> Right -> Top
            bool cyclePressed = false;
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.vKey.wasPressedThisFrame)
            {
                cyclePressed = true;
            }
#endif
            if (!cyclePressed && Input.GetKeyDown(KeyCode.V))
            {
                cyclePressed = true;
            }

            if (cyclePressed)
            {
                CyclePerspective();
            }

            // Orbit & Zoom user input handling (only in follow modes, not in Free fly)
            if (currentPerspective != Perspective.Free && targetTransform != null)
            {
                HandleManualOrbitAndZoom();
            }
        }

        private void LateUpdate()
        {
            if (currentPerspective == Perspective.Free)
            {
                if (freeFlyCamera != null && !freeFlyCamera.enabled)
                {
                    freeFlyCamera.enabled = true;
                }
                return;
            }

            // Ensure FreeFlyCamera is disabled during locked follow
            if (freeFlyCamera != null && freeFlyCamera.enabled)
            {
                freeFlyCamera.enabled = false;
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

            // Instant snap if teleporting/spawning, otherwise smooth damp
            if (isTeleporting)
            {
                transform.position = desiredRigPos;
                transform.rotation = desiredCamRot;
                currentVelocity = Vector3.zero;
                isTeleporting = false;
            }
            else
            {
                transform.position = Vector3.SmoothDamp(transform.position, desiredRigPos, ref currentVelocity, smoothTime);
                transform.rotation = Quaternion.Slerp(transform.rotation, desiredCamRot, Time.deltaTime * rotationDamping);
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

            // 4. Apply manual orbit rotation (if user has RMB-dragged)
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
            // UI input guard: do not orbit/zoom when mouse is hovering over interactive UI
            if (IsPointerOverUI()) return;

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

        public void SetPerspective(Perspective perspective, bool snapImmediate = false)
        {
            currentPerspective = perspective;
            orbitYaw = 0f;
            orbitPitch = 0f;
            zoomScale = 1.0f;

            if (perspective == Perspective.Free)
            {
                if (freeFlyCamera != null)
                {
                    freeFlyCamera.enabled = true;
                }
            }
            else
            {
                if (freeFlyCamera != null)
                {
                    freeFlyCamera.enabled = false;
                }

                if (snapImmediate)
                {
                    isTeleporting = true;
                }
            }

            OnPerspectiveChanged?.Invoke(currentPerspective);
            Debug.Log($"[RoverCameraRig] Perspective set to: {currentPerspective}");
        }

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
                isTeleporting = true; // Snap immediately on rover spawn / swap
                Debug.Log($"[RoverCameraRig] Bound target to active rover: {handle.profile.displayName}");
            }
        }

        private void HandleRoverDestroyed()
        {
            targetTransform = null;
            currentProfile = null;
            Debug.Log("[RoverCameraRig] Active rover destroyed; camera holding position.");
        }
    }
}

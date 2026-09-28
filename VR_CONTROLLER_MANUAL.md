# 🥽 VR Controller Manual & Architecture Guide
**Scene:** `Assets/project/scenes/VR_Module.unity`  
**Interaction Framework:** Unity XR Interaction Toolkit (XRI) 3.5.1 + OpenXR  
**Target Hardware:** Meta Quest 2 / Quest 3 / Quest Pro / PCVR (via Quest Link or AirLink)

---

## 1. Physical Controller Diagram & Button Mapping

```text
========================================================================================
                          META QUEST TOUCH CONTROLLERS
========================================================================================

           LEFT CONTROLLER                                 RIGHT CONTROLLER
         ┌─────────────────┐                             ┌─────────────────┐
         │                 │                             │   [B] BUTTON    │
         │   (Y) [Free]    │                             │  Studio UI Menu │
         │                 │                             │  Toggle / Recall│
         │   (X) [Free]    │                             │                 │
         │                 │                             │   (A) [Free]    │
         │  [L THUMBSTICK] │                             │                 │
         │   (Cleaned up   │                             │  [R THUMBSTICK] │
         │   no conflicts) │                             │   (Cleaned up   │
         │                 │                             │   no conflicts) │
         │ ┌─────────────┐ │                             │ ┌─────────────┐ │
         │ │  L TRIGGER  │ │                             │ │  R TRIGGER  │ │
         │ │ Click / UI  │ │                             │ │ Click / UI  │ │
         │ └─────────────┘ │                             │ └─────────────┘ │
         │ ┌─────────────┐ │                             │ ┌─────────────┐ │
         │ │   L GRIP    │ │                             │ │   R GRIP    │ │
         │ │  [Reserved] │ │                             │ │  [Reserved] │ │
         │ └─────────────┘ │                             │ └─────────────┘ │
         └─────────────────┘                             └─────────────────┘
```

---

## 2. Active Interaction Controls

| Control | Input Binding | Hand | Active Functionality in `VR_Module.unity` |
|---|---|---|---|
| **Secondary Button (<kbd>B</kbd>)** | `<XRController>{RightHand}/secondaryButton` | **Right Hand** | **Studio UI Menu Toggle & Recall**: Press once to hide the Studio UI. Press again to recall the entire UI directly in front of your current gaze ($1.35$m forward, at eye height) stationary in world space. |
| **Index Trigger** | `<XRController>{Left/RightHand}/activate` & `select` | **Both Hands** | **Click / Select UI**: Interacts with UI buttons, dropdowns, sliders, and canvas elements. Confirms terrain rover placement. |
| **Curved Ray Laser** | `NearFarInteractor` (`CurveInteractionCaster`) | **Both Hands** | **Curved Bezier Pointer**: Projects a natural curved laser from the controller tip with collision detection and hover feedback on UI Toolkit elements. |
| **HMD 6DOF Tracking** | `TrackedPoseDriver` (`XRI Head`) | **Headset** | **1:1 Natural Head Tracking**: Full 6DOF positional and rotational tracking in `Floor` origin mode. Zero camera fighting, zero sticky visor dragging. |
| **Controller Pose Tracking** | `TrackedPoseDriver` (`XRI Left/RightHand`) | **Both Hands** | **1:1 Hand Tracking**: Positioned at ergonomic natural resting pose (`±0.25m` X, `1.10m` Y, `0.35m` Z). Visualized using official Meta Quest 2 `UniversalController` models with animated buttons. |
| **Keyboard Test Fallback** | <kbd>B</kbd> or <kbd>U</kbd> Key | **Desktop / In-Editor** | Executes the exact same Studio UI toggle/recall behavior during Editor play mode testing. |

---

## 3. XR Origin Rig Hierarchy (`VR_Module.unity`)

```text
VR_Module.unity
├── XR Interaction Manager                      [Global interaction coordinator]
├── EventSystem                                 [XRUIInputModule + InputSystemUIInputModule]
├── XR Origin (XR Rig)                          [Floor tracking mode @ (0.00, 76.55, -3.50)]
│   ├── FloatingUIRecallController              [Manages B-button toggle & gaze positioning]
│   ├── Camera Offset                           [XRI Floor Offset @ local (0, 0, 0)]
│   │   └── Main Camera                         [SOLE active camera; TrackedPoseDriver: Head]
│   ├── Left Controller                         [TrackedPoseDriver: LeftHand @ (-0.25, 1.10, 0.35)]
│   │   ├── Near-Far Interactor                 [Active; Curve/Sphere Caster, SimpleHapticFeedback]
│   │   │   └── LineVisual                      [CurveVisualController, LineRenderer]
│   │   └── Left Controller Visual              [ControllerAnimator, UniversalController 3D Model]
│   │       └── UniversalController             [Bumper, Home, Base, TouchPad, Trigger, Buttons]
│   └── Right Controller                        [TrackedPoseDriver: RightHand @ (0.25, 1.10, 0.35)]
│       ├── Near-Far Interactor                 [Active; Curve/Sphere Caster, SimpleHapticFeedback]
│       │   └── LineVisual                      [CurveVisualController, LineRenderer]
│       └── Right Controller Visual             [ControllerAnimator, UniversalController 3D Model]
│           └── UniversalController             [Bumper, Home, Base, TouchPad, Trigger, Buttons]
└── UIManager                                   [UIDocument @ world (0.00, 77.30, -1.80)]
```

---

## 4. Key Architectural Improvements Over Previous Setup

### 1. Eliminated "Sticky Visor" Effect
- **Previous Issue**: The Studio UI was parented to the camera in `StickyMode.HeadLocked`, which forced the giant multi-column UI to drag across the viewport every time the user rotated their head.
- **Solution in `VR_Module`**: The UI defaults to stationary `WorldAnchor` mode. It stands as an extraterrestrial holographic console in 3D space.
- **Dynamic Recall**: If the user turns around and wants the console in front of them, pressing the **B Button** repositions the UI directly in front of their current gaze at a comfortable distance ($1.35$m) and orientation, then locks it in place.

### 2. Single Active Camera Architecture
- **Previous Issue**: Multiple cameras (`Main Camera` with `FreeFlyCamera`, plus `XR Origin/Main Camera`) were active concurrently, fighting for viewport control and creating orientation jitter.
- **Solution in `VR_Module`**: Exactly **one** camera exists: `XR Origin/Camera Offset/Main Camera`, driven exclusively by OpenXR `TrackedPoseDriver`.

### 3. Official XRI 3.5.1 Starter Assets Controller Pipeline
- Clean `NearFarInteractor` implementation without dormant teleportation listeners deactivating the rays.
- Ergonomic default resting coordinates (`±0.25m` X, `1.10m` Y, `0.35m` Z) preventing controllers from spawning inside the player's skull or floor.
- Direct `XRInteractionGroup` registration ensuring immediate ray visual rendering upon entering Play Mode.

---

## 5. How to Test in the Unity Editor

### Testing with a Connected Meta Quest Headset:
1. Connect headset via Quest Link cable or AirLink.
2. Ensure Oculus / Meta Quest Link app has OpenXR set as active runtime.
3. Open `Assets/project/scenes/VR_Module.unity`.
4. Press **Play** in Unity Editor.
5. Put on the headset. Aim the curved laser at UI buttons and pull the **Right Index Trigger** to click.
6. Press the **B Button** on the right controller to hide/show the Studio console.

### Testing without Headset (XR Device Simulator):
1. Open `Assets/project/scenes/VR_Module.unity`.
2. Press **Play** in Unity Editor.
3. Use the keyboard fallbacks:
   - Press <kbd>B</kbd> or <kbd>U</kbd> to toggle/recall the Studio UI.
   - Hold <kbd>RMB</kbd> to look around or aim the simulator controller.
   - Press <kbd>Tab</kbd> to cycle between simulated Left and Right hands.
   - Press <kbd>LMB</kbd> to trigger-click UI elements.

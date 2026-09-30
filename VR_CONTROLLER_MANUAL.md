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
         │  [X] CYCLE SPEED│                             │ Cancel Placement│
         │      REGIME     │                             │                 │
         │                 │                             │  [A] CYCLE SPEED│
         │  [L THUMBSTICK] │                             │      REGIME     │
         │  Drive (WASD):  │                             │                 │
         │  Y = Throttle   │                             │  (R Thumbstick) │
         │  X = Steer      │                             │   [Clean/Free]  │
         │  (Yaw in Place) │                             │                 │
         │ ┌─────────────┐ │                             │ ┌─────────────┐ │
         │ │  L TRIGGER  │ │                             │ │  R TRIGGER  │ │
         │ │ Click UI /  │ │                             │ │ Click UI /  │ │
         │ │ Deploy Rover│ │                             │ │ Deploy Rover│ │
         │ └─────────────┘ │                             │ └─────────────┘ │
         │ ┌─────────────┐ │                             │ ┌─────────────┐ │
         │ │   L GRIP    │ │                             │ │   R GRIP    │ │
         │ │  [Reserved] │ │                             │ │  [Reserved] │ │
         │ └─────────────┘ │                             │ └─────────────┘ │
         └─────────────────┘                             └─────────────────┘
```

---

## 2. Active Interaction Controls

### VR Rover Drive Controls (Phase 3 – Active Driving)

| Input | Hand | Action |
|---|---|---|
| **Left Thumbstick Y** | Left Hand | **Throttle**: Forward ($+1$) and Reverse ($-1$) driving throttle with continuous analog smoothing. |
| **Left Thumbstick X** | Left Hand | **Steer**: Turn Left ($-1$) and Turn Right ($+1$) with continuous analog steering. |
| **Button <kbd>A</kbd> or <kbd>X</kbd>** | Right (<kbd>A</kbd>) / Left (<kbd>X</kbd>) | **Change Speed Regime**: Cycles STOP (0%) ➔ PRECISION (25%) ➔ EXPLORE (60%) ➔ CRUISE (100%) ➔ STOP. |
| **Index Trigger** | Both Hands | **UI + Placement Only**: Confirm rover placement on terrain; click UI buttons and sliders. *Does not control throttle.* |
| **Secondary Button (<kbd>B</kbd>)** | Right Hand | **Studio UI Toggle / Recall**: Toggles dashboard or recalls in front of player gaze. Cancels placement mode. |

### Complete Binding Reference:

| Control | Input Binding | Hand | Active Functionality in `VR_Module.unity` |
|---|---|---|---|
| **Left Thumbstick** | `<XRController>{LeftHand}/thumbstick` & `CommonUsages.primary2DAxis` | **Left Hand** | **Primary Drive Stick**: Y = Throttle, X = Steer during Active Driving (Phase 3). Rotates ghost marker heading during Placement (Phase 2). Deadzone $0.15$ with smooth remapping. |
| **Face Button <kbd>A</kbd> or <kbd>X</kbd>** | `<XRController>/primaryButton` & `CommonUsages.primaryButton` | **Right / Left Hand** | **Cycle Speed Regime**: Cycles STOP ➔ PRECISION ➔ EXPLORE ➔ CRUISE across all 3 rover models. |
| **Secondary Button (<kbd>B</kbd>)** | `<XRController>{RightHand}/secondaryButton` | **Right Hand** | **Studio UI Menu Toggle & Recall**: Press once to hide Studio UI. Press again to recall UI directly in front of current gaze ($1.35$m forward) stationary in world space. In Placement Mode, cancels placement. |
| **Index Trigger** | `<XRController>{Left/RightHand}/activate` & `select` | **Both Hands** | **Click / Select UI & Confirm Rover Placement**: Interacts with UI buttons, dropdowns, and sliders. In Placement Mode, pulling trigger while aiming at terrain deploys the active rover. *Does not control throttle.* |
| **Curved Ray Laser** | `NearFarInteractor` (`CurveInteractionCaster`) | **Both Hands** | **Curved Bezier Pointer & Terrain Raycast**: Projects a natural curved laser from controller tip with collision detection and hover feedback on UI Toolkit elements, casting up to $100$m onto terrain. |
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
│   │   └── Main Camera                         [SOLE active camera; TrackedPoseDriver: Head; DesktopFreeFlyCamera (auto-gated)]
│   ├── Left Controller                         [TrackedPoseDriver: LeftHand @ (-0.25, 1.10, 0.35)]
│   │   ├── Near-Far Interactor                 [Active; Curve/Sphere Caster, SimpleHapticFeedback, 100m cast]
│   │   │   └── LineVisual                      [CurveVisualController, LineRenderer]
│   │   └── Left Controller Visual              [ControllerAnimator, UniversalController 3D Model]
│   │       └── UniversalController             [Bumper, Home, Base, TouchPad, Trigger, Buttons]
│   └── Right Controller                        [TrackedPoseDriver: RightHand @ (0.25, 1.10, 0.35)]
│       ├── Near-Far Interactor                 [Active; Curve/Sphere Caster, SimpleHapticFeedback, 100m cast]
│       │   └── LineVisual                      [CurveVisualController, LineRenderer]
│       └── Right Controller Visual             [ControllerAnimator, UniversalController 3D Model]
│           └── UniversalController             [Bumper, Home, Base, TouchPad, Trigger, Buttons]
└── UIManager                                   [UIDocument @ world (0.00, 77.30, -1.80)]
```

---

## 4. Key Architectural Improvements & System Integrations

### 1. Dual-Mode VR Rover Placement System
- **Curved Ray Terrain Sampling**: In VR, `RoverPlacementController` connects to the `NearFarInteractor` instances on both controllers. It samples the live Bezier curve (`CurveInteractionCaster.samplePoints`) to pinpoint exact terrain impact coordinates up to $100$m away.
- **Visual Ghost Marker & Slope Readout**: Renders a circular footprint ring, directional heading arrow, and a billboarded slope safety text readout (🟢 Green for safe $\le 25^\circ$, 🔴 Red for steep hazard).
- **Thumbstick Yaw Heading**: Nudging the Left or Right thumbstick horizontally spins the placement ghost marker heading in real time.
- **Trigger Deployment**: Squeezing the Index Trigger on either controller confirms placement and deploys the rover with ArticulationBody physics initialized.
- **Smart UI Shielding**: If the ray intersects a UI button (e.g. Cancel or Centre Spawn), the trigger click is absorbed by UI Toolkit and will **never** accidentally drop a rover behind the interface.

### 2. Desktop Free-Fly Camera vs. VR Headset Safety Gate
- `DesktopFreeFlyCamera` provides desktop users with pure world/horizontal camera translation (<kbd>Left Alt</kbd> / <kbd>RMB</kbd> + <kbd>W</kbd><kbd>A</kbd><kbd>S</kbd><kbd>D</kbd>/<kbd>Q</kbd><kbd>E</kbd>) with rotation strictly locked.
- **Automatic VR Gating**: The script checks `XRSettings.isDeviceActive` and active XR subsystems. When a VR headset is detected, `DesktopFreeFlyCamera` automatically deactivates itself, completely preventing any camera fighting, jitter, or conflict with `TrackedPoseDriver`.

### 3. Continuous Analog Joystick Rover Driving
- All three rovers (Husky A200, Deep Robotics M20, NASA Perseverance M2020) read driving input via an analog virtual joystick (`Vector2 virtualJoystick`) with smooth acceleration ramps (`joystickRampSpeed = 6.0f`).
- Input is seamlessly shared between desktop WASD / arrow keys, Gamepad Left Stick, and programmatic VR commands.
- Speed regimes (<kbd>1</kbd>=Stop 0%, <kbd>2</kbd>=Precision 25%, <kbd>3</kbd>=Explore 60%, <kbd>4</kbd>=Cruise 100%) scale wheel velocity limits on the fly.
- When desktop camera grab is active, driving inputs are paused to prevent accidental rover movement while translating the camera.

### 4. Eliminated "Sticky Visor" Effect
- The Studio UI defaults to stationary `WorldAnchor` mode in 3D space (`0.00, 77.30, -1.80`).
- Pressing the **B Button** on the Right Controller repositions the UI directly in front of the user's current gaze ($1.35$m forward, at eye height) and locks it stationary in world space.

### 5. Single Active Camera Architecture
- Exactly **one** camera exists: `XR Origin/Camera Offset/Main Camera`.
- All legacy secondary cameras and unparented rigs have been removed.

---

## 5. How to Test in the Unity Editor

### Testing with a Connected Meta Quest Headset:
1. Connect headset via Quest Link cable or AirLink.
2. Ensure Oculus / Meta Quest Link app has OpenXR set as active runtime.
3. Open `Assets/project/scenes/VR_Module.unity`.
4. Press **Play** in Unity Editor.
5. Put on the headset. Aim the curved laser at UI buttons and pull the **Right Index Trigger** to click.
6. Press the **B Button** on the right controller to hide/show the Studio console.
7. Select a rover (Husky, M20, or M2020) from the Deploy panel:
   - Aim the curved laser at the terrain surface. The holographic ghost ring and slope readout will appear.
   - Push the **Thumbstick** left or right to orient the rover's heading.
   - Pull the **Index Trigger** to confirm placement and drop the rover.
8. Drive the rover using desktop WASD, arrow keys, or gamepad analog stick.

### Testing without Headset (XR Device Simulator):
1. Open `Assets/project/scenes/VR_Module.unity`.
2. Press **Play** in Unity Editor.
3. Use the simulator and keyboard fallbacks:
   - Press <kbd>B</kbd> or <kbd>U</kbd> to toggle/recall the Studio UI.
   - Press <kbd>Tab</kbd> to cycle between simulated Left and Right hands.
   - Hold <kbd>RMB</kbd> to look around or aim the simulator controller.
   - Press <kbd>LMB</kbd> to trigger-click UI elements or deploy rover on terrain.
   - Hold <kbd>Left Alt</kbd> + <kbd>W</kbd>/<kbd>A</kbd>/<kbd>S</kbd>/<kbd>D</kbd>/<kbd>Q</kbd>/<kbd>E</kbd> to test Desktop Free-Fly camera translation (rotation remains locked).


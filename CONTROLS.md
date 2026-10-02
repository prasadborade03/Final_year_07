# Comprehensive Controls & Interaction Guide

**VR Planetary Rover Digital Twin Simulation Studio (`Final_year_07`)**  
*Supports both Desktop (Keyboard & Mouse) and Virtual Reality (Meta Quest 2 / OpenXR).*

---

## 1. Quick Reference Cheat Sheet

### 🖥️ Desktop (Non-VR) Controls

| Key / Input | Action | Mode / Context |
|---|---|---|
| <kbd>Tab</kbd> | **Switch View Mode**: Toggle between **Full Studio View** (3-column mission control) and **Driving HUD Mode** (clear center viewport for driving). | Global / UI |
| <kbd>W</kbd> / <kbd>↑</kbd> | **Drive Forward** (Analog throttle ramp $0 \rightarrow +1$) | Active Driving (Virtual Joystick) |
| <kbd>S</kbd> / <kbd>↓</kbd> | **Drive Reverse** (Analog throttle ramp $0 \rightarrow -1$) | Active Driving (Virtual Joystick) |
| <kbd>A</kbd> / <kbd>←</kbd> | **Steer Left** (Analog steer ramp $0 \rightarrow -1$) | Active Driving (Virtual Joystick) |
| <kbd>D</kbd> / <kbd>→</kbd> | **Steer Right** (Analog steer ramp $0 \rightarrow +1$) | Active Driving (Virtual Joystick) |
| <kbd>Hold Left Alt</kbd> (or <kbd>RMB</kbd>) + <kbd>W</kbd>/<kbd>A</kbd>/<kbd>S</kbd>/<kbd>D</kbd> | **Free-Fly Translate Horizontal**: Camera moves in camera-relative X/Z plane. **Rotation is 100% locked.** | Desktop Free-Fly Camera |
| <kbd>Hold Left Alt</kbd> (or <kbd>RMB</kbd>) + <kbd>Q</kbd> / <kbd>E</kbd> | **Free-Fly Translate Vertical**: Camera moves down (-Y) / up (+Y) in world space. **Rotation is 100% locked.** | Desktop Free-Fly Camera |
| <kbd>Release Grab Key</kbd> | **Restore Camera**: Camera smoothly returns to previous mode (OrbitCamera / FollowRig). | Desktop Free-Fly Camera |
| <kbd>1</kbd> | **STOP Mode** (Zero speed limit / emergency brake) | Active Driving |
| <kbd>2</kbd> | **PRECISION Mode** (25% speed limit — rock crawling) | Active Driving |
| <kbd>3</kbd> | **EXPLORE Mode** (60% speed limit — traverse & survey) | Active Driving |
| <kbd>4</kbd> | **CRUISE Mode** (100% full speed limit) | Active Driving |
| <kbd>Q</kbd> / <kbd>E</kbd> | **Rotate Rover Heading (Yaw)** (in placement) / **Raise or Lower Knees** (in M20) | Placement / M20 Driving |
| <kbd>M</kbd> | **Cycle Steering Mode**: Ackermann ➔ Point Turn ➔ Crab ➔ Tank | M2020 Perseverance |
| <kbd>Space</kbd> | **Toggle Chassis Ground Clearance**: Raises/lowers rocker-bogie by 8° | M2020 Perseverance |
| <kbd>V</kbd> | **Cycle Camera View**: Rear ➔ Left ➔ Front ➔ Right ➔ Top | Follow Camera Rig |
| <kbd>RMB</kbd> + Drag | **Orbit Camera** around rover (clamped -20° to 75°) | Camera Rig / Orbit Mode |
| <kbd>Mouse Scroll</kbd> | **Camera Zoom** (0.4× to 2.5×) / Rotate Ghost Marker (in placement) | Camera / Placement |
| <kbd>LMB</kbd> (Left Click) | **Click UI Buttons**, paint on 2D canvas, confirm rover placement | Global / Placement / Lab |
| <kbd>R</kbd> | **Respawn Rover** at last placed position and reset mission odometer | Active Driving |
| <kbd>Shift</kbd> + <kbd>LMB</kbd> | **Instant Relocate**: Click anywhere on terrain to teleport rover directly | Global / Debug |
| <kbd>C</kbd> | **Center Rover**: Teleports active rover to the geometric center of terrain | Global / Placement |
| <kbd>P</kbd> | **Open Planet Selection Modal**: Mars, Moon, Earth, Titan, Venus | Global / Planetary |
| <kbd>Esc</kbd> | **Cancel / Close**: Cancels placement mode or closes open popups | Global / Placement / Modals |
| <kbd>U</kbd> or <kbd>F1</kbd> | **Minimize Studio**: Collapses dashboard into a compact floating pill dock | Global / UI |
| <kbd>H</kbd> | **Cinematic Toggle**: Hides or restores all on-screen UI overlays | Global / Cinematic |
| <kbd>F3</kbd> | **Aerospace Physics Debug Overlay**: True FPS, physics Hz, wheel kinematics | Global / Diagnostics |
| <kbd>Ctrl</kbd> + <kbd>Z</kbd> | **Undo** last heightmap brush stroke (up to 20 levels) | Terrain Lab |
| <kbd>Ctrl</kbd> + <kbd>Y</kbd> | **Redo** undone heightmap brush stroke | Terrain Lab |

---

### 🥽 Virtual Reality (Meta Quest 2 / XR) Controls (`VR_Module.unity`)

| Controller Action | Hand | Functionality | Current Status |
|---|---|---|---|
| **Left Thumbstick (Y-Axis)** | Left Hand | **Throttle**: Forward ($+1$) and Reverse ($-1$) driving throttle with continuous analog smoothing and deadzone ($0.15$). | **Active** (Phase 3 Driving) |
| **Left Thumbstick (X-Axis)** | Left Hand | **Steer**: Turn Left ($-1$) and Turn Right ($+1$) steering input. In Phase 2 (Placement), rotates ghost marker yaw. | **Active** (Phase 3 Driving / Phase 2 Placement) |
| **Button <kbd>A</kbd> or <kbd>X</kbd>** | Right (<kbd>A</kbd>) / Left (<kbd>X</kbd>) | **Cycle Speed Regime**: Cycles STOP (0%) ➔ PRECISION (25%) ➔ EXPLORE (60%) ➔ CRUISE (100%) ➔ STOP. | **Active** (`RoverInputProvider`) |
| **Near-Far Curved Ray** | Left & Right Hand | Curved bezier pointer with line visual for pointing at UI Toolkit elements and 3D terrain. Live ghost marker projects to ray hit point on terrain. | **Active** (XRI 3.5.1 Near-Far Interactor, $100$m range) |
| **Index Trigger (Click / Select)** | Right & Left Hand | **UI + Placement Only**: Confirm rover placement on terrain; click UI buttons and sliders. *Does NOT control throttle.* | **Active** (`XRI Left/Right Interaction/Select`) |
| **Secondary Button (<kbd>B</kbd>)** | Right Controller | **Toggle Studio UI**: Hides UI or recalls it directly in front of current gaze ($1.35$m forward, stationary world-anchored). Cancels placement mode if active. | **Active** (`FloatingUIRecallController`) |
| **Keyboard Fallbacks (<kbd>B</kbd> / <kbd>U</kbd>)** | Desktop / Simulator | Triggers the same B-button Studio UI toggle/recall during editor testing. | **Active** |
| **Head Tracking (6DOF)** | Headset (HMD) | Natural 1:1 head rotation and position in Floor tracking space (zero stickiness, no camera fighting). | **Active** (`TrackedPoseDriver: Head`) |
| **Controller Pose Tracking** | Left & Right Hand | Ergonomic resting pose in front of user (`±0.25m` X, `1.10m` Y, `0.35m` Z). Tracks 1:1 with real hands. | **Active** (`TrackedPoseDriver: Left/Right`) |
| **Locomotion (Teleport / Snap Turn)** | Left & Right Sticks | Intentionally omitted for this baseline scene to guarantee clean tracking and no input conflict. | *Deferred for future step-by-step addition* |

---

## 2. Interactive Workflow (Phase-by-Phase)

The simulation follows a 3-phase lifecycle. Here is how controls work during each phase:

```
┌─────────────────────────┐      ┌─────────────────────────┐      ┌─────────────────────────┐
│         PHASE 1         │      │         PHASE 2         │      │         PHASE 3         │
│  Planetary Environment  │ ───► │     Rover Selection     │ ───► │     Active Driving &    │
│    & Terrain Studio     │      │   & Surface Placement   │      │    Scientific Survey    │
└─────────────────────────┘      └─────────────────────────┘      └─────────────────────────┘
```

---

### Phase 1: Planetary Environment & Terrain Setup

**Objective**: Choose your celestial world, configure the terrain geometry, and generate the 3D surface mesh.

1. **Select Celestial Planet / Environment**:
   - Press <kbd>P</kbd> on your keyboard (or click the **Planet Badge** button in the top header).
   - The **Planetary Selection Modal** appears.
   - Click to choose from:
     - 🔴 **Mars** (0.38g gravity, 610 Pa atmosphere, reddish Martian dust)
     - ⚪ **Moon** (0.16g gravity, 0 Pa vacuum, high contrast lunar lighting)
     - 🔵 **Earth** (1.00g gravity, 101.3 kPa atmosphere)
     - 🟠 **Titan** (0.14g gravity, dense 146.7 kPa atmosphere, haze)
     - 🟡 **Venus** (0.90g gravity, extreme 9.2 MPa atmosphere, caustic heat)
   - Click **Apply Environment** or press <kbd>Esc</kbd> to close the modal.

2. **Load or Sculpt Terrain**:
   - **Quick Presets**: In Column 2 (Center) of the Studio, click any preset button:
     - **Gale Crater** (Central peak and alluvial basin)
     - **Olympus Mons** (Massive volcanic caldera shield)
     - **Shackleton Crater** (Deep lunar impact crater)
     - **Valles Marineris** (Gigantic rift canyon system)
     - **Fractal** (Procedurally randomized multi-octave Perlin noise)
   - **Custom Heightmap Image**: Click **Browse File** to select any 16-bit or 8-bit grayscale PNG heightmap from your PC.
   - **Terrain Lab (Advanced Sculpting)**: Click the **Terrain Lab** tab to open the 2D canvas painter:
     - Select a brush: **Raise**, **Lower**, **Smooth**, **Flatten**, or **Noise**.
     - Adjust sliders: **Radius**, **Strength**, **Hardness**, or **Flatten Height**.
     - Click and drag on the 2D heightmap canvas to paint topological features.
     - Press <kbd>Ctrl</kbd> + <kbd>Z</kbd> to Undo or <kbd>Ctrl</kbd> + <kbd>Y</kbd> to Redo.
     - Choose surface material presets (**Martian**, **Lunar**, **Basalt**, **Wireframe**, etc.).

3. **Generate Terrain**:
   - Click the green **Generate Terrain** button.
   - The 3D terrain mesh instantly spawns in the scene with real physics colliders, elevation scaling, and PBR textures.
   - The system automatically transitions to **Phase 2 (Rover Selection)**.

---

### Phase 2: Rover Selection & Precision Placement

**Objective**: Pick your exploration robot and deploy it onto a safe surface location.

1. **Select a Rover**:
   - In the **Deploy Rover** panel (or popup cards), click one of the three rovers:
     - 🟡 **Clearpath Husky A200** (Rugged 4-wheel skid-steer field robot)
     - 🐕 **Deep Robotics M20** (Wheeled quadruped with active knee articulation)
     - 🚀 **NASA Perseverance (M2020)** (6-wheel rocker-bogie rover with 4-wheel steering)

2. **Aim & Inspect Placement Ghost Marker**:
   - **Desktop**: Move the mouse across the viewport to cast a screen ray onto the planetary terrain surface.
   - **VR**: Aim the **Near-Far Curved Ray** (Right or Left hand) at any valid terrain surface. The ray smoothly casts up to $100$m across the planetary topography.
   - Both methods dynamically project the **3D Holographic Ghost Marker** directly onto the terrain hit point:
     - **Ring Outline**: Matches the physical footprint radius of the selected robot.
     - **Forward Heading Arrow**: Shows which direction the rover will face upon landing.
     - **Live Slope Angle Text Readout**: Continuously calculates surface inclination:
       - 🟢 **Green ("SAFE")**: Slope is within the rover's safe grade limit (e.g. $\le 25^\circ$).
       - 🔴 **Red ("STEEP")**: Slope exceeds safe limit; risking tipping or wheel slippage.

3. **Orient & Deploy**:
   - **Rotate Heading (Yaw)**:
     - **Desktop**: Press <kbd>Q</kbd> to turn counter-clockwise or <kbd>E</kbd> to turn clockwise, or scroll the **Mouse Wheel** for fast step rotation.
     - **VR**: Push the **Left or Right Thumbstick** horizontally to smoothly rotate the ghost marker heading. Driving inputs are automatically locked out while placement is active.
   - **Confirm Placement**:
     - **Desktop**: **Left Click** on the terrain surface.
     - **VR**: Pull the **Index Trigger** (Right or Left hand) while aiming at the terrain. If pointing at an interactive UI button (e.g. Cancel or Centre Spawn), the trigger clicks the UI button instead of placing the robot.
   - **Center Spawn Shortcut**: Click the **Centre Spawn** button in the placement banner to immediately drop the rover at the exact geometric center of the terrain.
   - **Cancel Placement**:
     - **Desktop**: Press <kbd>Esc</kbd> or **Right Click** (when not holding the camera grab key).
     - **VR**: Press the **Secondary Button (<kbd>B</kbd>)** on the Right Controller.

4. **Physics Initialization**:
   - The robot is dropped with ArticulationBody physics stabilized, gravity engaged, and automatically registered into the live telemetry system.
   - The system transitions to **Phase 3 (Active Driving)**. Thumbsticks automatically switch from ghost marker rotation to primary driving control!

---

### Phase 3: Active Driving, Exploration & Telemetry

**Objective**: Drive across extraterrestrial landscapes, execute maneuvers, and monitor telemetry.

1. **Optimize Your Viewport (HUD Switching)**:
   - Press <kbd>Tab</kbd> to switch from **Full Studio Mode** to **Driving HUD Mode**.
   - **Full Studio Mode**: Best for analyzing systems, topography, radar minimap, and motor temperatures.
   - **Driving HUD Mode**: Leaves the middle 100% transparent for clear driving visibility while keeping critical speedometer, attitude, and wheel cards on the edges.
   - In VR, press <kbd>B</kbd> on the Right Controller to toggle the Studio UI or recall it comfortably in front of your gaze ($1.35$m).

2. **Drive the Rover (Unified Desktop & VR)**:
   - **Virtual Reality (Meta Quest / OpenXR)**:
     - **Left Thumbstick Y**: Continuous forward throttle (push forward) and reverse throttle (pull back).
     - **Left Thumbstick X**: Continuous steering left and right.
     - **Deadzone**: Hardware calibrated at $\approx 0.15$ with smooth remapping to prevent stick drift while preserving subtle rock-crawling control.
     - **Cycle Speed Regimes**: Press Button <kbd>A</kbd> (Right Controller) or Button <kbd>X</kbd> (Left Controller) to cycle:
       $$\text{STOP (0\%)} \longrightarrow \text{PRECISION (25\%)} \longrightarrow \text{EXPLORE (60\%)} \longrightarrow \text{CRUISE (100\%)} \longrightarrow \text{STOP}$$
   - **Desktop (Keyboard & Mouse)**:
     - Use <kbd>W</kbd> / <kbd>A</kbd> / <kbd>S</kbd> / <kbd>D</kbd> or the **Arrow Keys** (analog virtual joystick ramp).
     - Set speed regimes directly on the fly with <kbd>1</kbd> (Stop), <kbd>2</kbd> (Precision), <kbd>3</kbd> (Explore), or <kbd>4</kbd> (Cruise).

3. **Rover-Specific Controls**:
   - **Clearpath Husky A200**:
     - Simple skid-steer differential drive. Turns like a tank by spinning left and right wheels in opposite directions.
   - **Deep Robotics M20 (Wheeled Quadruped)**:
     - Hold <kbd>Q</kbd>: **Raise knees** (increases ground clearance to roll over tall rocks).
     - Hold <kbd>E</kbd>: **Lower knees** (crouches chassis for higher stability and lower center of gravity).
   - **NASA Perseverance (M2020)**:
     - Press <kbd>M</kbd> to cycle steering modes (or press <kbd>1</kbd>–<kbd>4</kbd>):
       - **Ackermann**: Standard car-like front/rear steering differential.
       - **Point Turn**: Wheels pivot in a circle; rover spins 360° on the spot without moving forward.
       - **Crab**: All steerable wheels angle diagonally together for sideways traversal.
       - **Tank Drive**: Differential skid-steering.
     - Press <kbd>Space</kbd>: **Lift Rocker-Bogie Chassis** by $8^\circ$ for extra obstacle clearance.

4. **Camera Rig Perspectives**:
   - Press <kbd>V</kbd> to cycle through 5 stabilized viewpoints:
     - **Rear** (Default chase camera)
     - **Left** (Side survey view)
     - **Front** (Forward obstacle inspection)
     - **Right** (Right wheel observation)
     - **Top** (Direct overhead bird's-eye view)
   - Hold <kbd>RMB</kbd> (Right Mouse Button) and drag to orbit around the rover at any custom angle.
   - Scroll the **Mouse Wheel** to zoom closer or farther.
   - Click **Free** in the bottom bar to disengage the follow camera and fly freely with the Free-Fly camera.

5. **Telemetry & Recovery**:
   - Monitor the live instruments:
     - **Kinematics Pod**: Speed (m/s), acceleration, and G-force.
     - **Artificial Horizon**: Chassis pitch, roll, heading degrees, and rollover hazard alerts.
     - **Wheel Actuator Grid**: Individual wheel angular velocity (rad/s), motor torque, and slip.
     - **Thermal & Battery Bar**: Watt-hour consumption and motor coil temperatures.
     - **Radar Minimap**: Shows real-time rover coordinate dot, heading pointer, and traversal breadcrumbs.
   - Press <kbd>F3</kbd> to open the **Aerospace Diagnostics Overlay** (shows raw physics tick rate, ArticulationBody joint forces, and frame delta ms).
   - If you flip over or get wedged between rocks, simply press <kbd>R</kbd> to **Respawn** at your last safe placement point.

---

## 3. Desktop (Non-VR) Controls Reference

### 🎮 Rover Movement & Speed Regimes

```
       [W / ↑] Forward
          ▲
[A / ←] ◄   ► [D / →]
  Left    ▼   Right
       [S / ↓] Reverse
```

| Key | Action | Details |
|---|---|---|
| <kbd>W</kbd> / <kbd>↑</kbd> | **Forward Throttle** | Accelerates wheels forward. |
| <kbd>S</kbd> / <kbd>↓</kbd> | **Reverse Throttle** | Drives wheels in reverse. |
| <kbd>A</kbd> / <kbd>←</kbd> | **Steer Left** | Steers or spins left wheels backward / right wheels forward. |
| <kbd>D</kbd> / <kbd>→</kbd> | **Steer Right** | Steers or spins right wheels backward / left wheels forward. |
| <kbd>1</kbd> | **STOP** | 0% speed limit (Emergency stop / parking brake). |
| <kbd>2</kbd> | **PRECISION** | 25% speed limit (For navigating steep boulders and precision rock testing). |
| <kbd>3</kbd> | **EXPLORE** | 60% speed limit (Standard exploratory cruising with power efficiency). |
| <kbd>4</kbd> | **CRUISE** | 100% full speed limit (Max rated wheel angular velocity). |
| <kbd>R</kbd> | **Respawn** | Resets the active rover to its last placement pose, zeroing velocity and resetting the mission odometer. |
| <kbd>Shift</kbd> + <kbd>LMB</kbd> | **Teleport / Relocate** | Instantly raycasts and drops the rover anywhere you click on the terrain surface. |
| <kbd>C</kbd> | **Center on Terrain** | Centers the active rover at the mid-point coordinates of the current planetary map. |

---

### 🤖 Rover-Specific Key Bindings

#### Deep Robotics M20 (Wheeled Quadruped)
- <kbd>Q</kbd> (Hold): **Knee Lift (+)** — Raises the knee joints at 1.5 rad/s up to max +0.5 rad.
- <kbd>E</kbd> (Hold): **Knee Lower (-)** — Lowers the knee joints down to -2.5 rad.

#### NASA Perseverance M2020
- <kbd>M</kbd>: **Toggle Steering Mode** — Rotates between:
  1. **Ackermann** (Smooth curved steering with differential wheel RPMs).
  2. **Point Turn** (Zero-radius pivot in place).
  3. **Crab Steering** (Diagonal parallel wheel translation).
  4. **Tank Drive** (Skid-steer mode).
- Keys <kbd>1</kbd>, <kbd>2</kbd>, <kbd>3</kbd>, <kbd>4</kbd> on M2020 directly select these 4 steering modes.
- <kbd>Space</kbd>: **Chassis Clearance Lift** — Toggles the rocker-bogie suspension lift angle ($0^\circ \leftrightarrow 8^\circ$).

---

### 🎥 Camera Modes & Controls

The camera system automatically synchronizes in `LateUpdate` after ArticulationBody physics ticks, eliminating micro-stutter and stabilizing horizontal pitch and roll.

#### 1. Follow Camera Rig (Default)
- <kbd>V</kbd>: **Cycle View Presets** in order:
  $$\text{Rear} \longrightarrow \text{Left} \longrightarrow \text{Front} \longrightarrow \text{Right} \longrightarrow \text{Top}$$
- <kbd>RMB</kbd> (Hold & Drag): **360° Manual Orbit** — Freely rotate around the rover (pitch clamped to $-20^\circ \dots 75^\circ$).
- <kbd>Mouse Scroll Wheel</kbd>: **Zoom In / Out** — Scales distance between $0.4\times$ and $2.5\times$.

#### 2. Desktop Free-Fly Camera (Pure Translation + Rotation Lock)

While holding the **Grab Key** (<kbd>Left Alt</kbd>, <kbd>Left Ctrl</kbd>, or holding <kbd>RMB</kbd> while pressing movement keys), the camera translates smoothly across 3D space with **camera rotation 100% frozen** (no mouse-look, pitch, yaw, or roll drift). When the grab key is held, rover driving inputs are automatically paused so keys only control camera translation.

| Input Action | Result | Constraints / Behavior |
|---|---|---|
| **Hold Left Alt (or RMB) + <kbd>W</kbd>/<kbd>A</kbd>/<kbd>S</kbd>/<kbd>D</kbd>** | **Translate camera in world X/Z** | Moves forward/backward/left/right relative to camera's horizontal heading. |
| **Hold Left Alt (or RMB) + <kbd>Q</kbd> / <kbd>E</kbd>** | **Translate camera in world Y** | Pure vertical translation: <kbd>Q</kbd> = Down, <kbd>E</kbd> = Up. |
| **Hold <kbd>Shift</kbd>** | **Speed Boost** | Smoothly accelerates translation velocity ($2.5\times$). |
| **Release Grab Key** | **Return to previous camera mode** | Returns immediately to previous mode (`RoverCameraRig` follow / `OrbitCamera`). |
| **Camera Rotation** | **Completely locked** | Orientation is strictly frozen while grab key is held. Zero mouse-look. |
| **VR Headset Safety Gate** | **Strictly bypassed in VR** | Automatically disables when an active VR headset is detected (`!IsVRHeadsetActive()`), eliminating any conflict with `TrackedPoseDriver` or `XR Origin`. |

#### 3. Legacy Keypad Free-Fly Flight (Optional Numpad Navigation)
- <kbd>RMB</kbd> (Hold & Drag): **Look Around** (Pitch and Yaw when grab translation is not active).
- <kbd>8</kbd> (Keypad or Alpha 8): **Fly Forward** relative to camera view.
- <kbd>5</kbd> (Keypad or Alpha 5): **Fly Backward**.
- <kbd>4</kbd> (Keypad or Alpha 4): **Strafe Left**.
- <kbd>6</kbd> (Keypad or Alpha 6): **Strafe Right**.
- <kbd>9</kbd> (Keypad or Alpha 9): **Ascend (Climb Up)** vertically.
- <kbd>7</kbd> (Keypad or Alpha 7): **Descend (Fly Down)** vertically.
- <kbd>Shift</kbd> (Hold): **Speed Boost** ($3\times$ faster).
- UI Preset Buttons: **Precision** (2 m/s), **Standard** (10 m/s), **Fast** (50 m/s), **Warp** (120 m/s).

---

### 🖥️ UI Navigation & Hotkeys

- <kbd>Tab</kbd>: **Toggle Studio Mode / Driving HUD Mode**:
  - *Studio Mode*: Full 3-column dashboard with topographic radar, terrain lab, file upload, and telemetry cards.
  - *Driving HUD Mode*: Minimizes middle cards, opening a 100% transparent center viewport for clear forward driving vision.
- <kbd>U</kbd> or <kbd>F1</kbd>: **Minimize Studio**: Collapses the entire dashboard into a small floating pill dock at the bottom/side. Click or press again to restore.
- <kbd>H</kbd>: **Cinematic Mode**: Completely hides all HUD elements for clean cinematic footage and screenshots.
- <kbd>P</kbd>: **Planet Selection Modal**: Opens the celestial environment picker.
- <kbd>Esc</kbd>: **Cancel / Exit**: Cancels active rover placement or closes open modal windows.
- <kbd>F3</kbd>: **Diagnostics Overlay**: Displays real-time framerate, physics delta-time, fixed update Hz, and per-actuator telemetry.
- <kbd>+</kbd> / <kbd>-</kbd> (or Keypad <kbd>+</kbd> / <kbd>-</kbd>): Adjusts VR HUD distance forward or backward.

---

## 4. Virtual Reality (VR) Controls Reference (`VR_Module.unity`)

The `VR_Module.unity` scene features an official **Unity XR Interaction Toolkit 3.5.1 + URP** architecture, matching the official Unity VR Template standards. All previous experimental locomotion, sticky head-locked visors, and conflicting camera scripts have been cleanly stripped out, providing a rock-solid, tracking-verified baseline.

---

### 🕹️ Meta Quest Touch Controller Physical Layout & Mappings

```text
               LEFT CONTROLLER                                 RIGHT CONTROLLER
             ┌─────────────────┐                             ┌─────────────────┐
             │  (Y) [Free]     │                             │  [B] STUDIO UI  │
             │                 │                             │  TOGGLE / RECALL│
             │  [X] CYCLE SPEED│                             │                 │
             │      REGIME     │                             │  [A] CYCLE SPEED│
             │                 │                             │      REGIME     │
             │  [L THUMBSTICK] │                             │                 │
             │  Drive: Y=Throt │                             │  (R Thumbstick) │
             │         X=Steer │                             │   [Clean/Free]  │
             │ ┌─────────────┐ │                             │ ┌─────────────┐ │
             │ │  L TRIGGER  │ │                             │ │  R TRIGGER  │ │
             │ │ UI / Place  │ │                             │ │ UI / Place  │ │
             │ └─────────────┘ │                             │ └─────────────┘ │
             │ ┌─────────────┐ │                             │ ┌─────────────┐ │
             │ │   L GRIP    │ │                             │ │   R GRIP    │ │
             │ │  [Reserved] │ │                             │ │  [Reserved] │ │
             │ └─────────────┘ │                             │ └─────────────┘ │
             └─────────────────┘                             └─────────────────┘
```

### VR Rover Drive Controls (Phase 3 – Active Driving)

| Input                    | Hand   | Action                          |
|--------------------------|--------|---------------------------------|
| Left Thumbstick Y        | Left   | Throttle (forward / reverse)    |
| Left Thumbstick X        | Left   | Steer left / right              |
| A / X (or cycle)         | —      | Change speed regime             |
| Index Trigger            | Both   | UI + Placement only             |
| B                        | Right  | Studio UI toggle / recall       |

#### Detailed Input Action Bindings:

| Physical Control | Hand | Component / Binding | Behavior in `VR_Module.unity` |
|---|---|---|---|
| **Left Thumbstick** | **Left Hand** | `RoverInputProvider` (`<XRController>{LeftHand}/thumbstick` + `CommonUsages.primary2DAxis`) | **Primary Rover Drive Stick**: Y-axis controls throttle ($-1$ reverse to $+1$ forward); X-axis controls steering ($-1$ left to $+1$ right). Automatically gated: acts as ghost marker yaw rotation during Phase 2, and drive stick during Phase 3. Configured with $0.15$ deadzone. |
| **Face Button <kbd>A</kbd> or <kbd>X</kbd>** | **Right (<kbd>A</kbd>) / Left (<kbd>X</kbd>)** | `RoverInputProvider` (`<XRController>/primaryButton` + `CommonUsages.primaryButton`) | **Cycle Speed Regime**: Cycles STOP (0%) ➔ PRECISION (25%) ➔ EXPLORE (60%) ➔ CRUISE (100%) ➔ STOP. Synchronizes across all three rover types. |
| **Secondary Button (<kbd>B</kbd>)** | **Right Hand** | `FloatingUIRecallController` (`<XRController>{RightHand}/secondaryButton`) | **Toggles Studio UI**: If visible, hides the panel (`SetActive(false)`). If hidden, recalls the panel to eye level $1.35$m directly in front of the player's current gaze and locks it in world space. In Placement Mode, cancels placement. |
| **Index Trigger** | **Both Hands** | `NearFarInteractor` (`XRI Left/Right Interaction/Select`) | **UI + Placement Only**: Interacts with UI buttons, sliders, dropdowns, and confirms rover placement on terrain. *Does not control throttle.* |
| **Curved Ray Laser** | **Both Hands** | `CurveInteractionCaster` + `LineVisual` | Emits a graceful curved bezier ray with real-time collision detection. Highlights hovered UI elements and casts $100$m onto planetary terrain. |
| **Headset 6DOF Tracking** | **HMD** | `TrackedPoseDriver` (`XRI Head`) | Natural 1:1 orientation and translation in **Floor** tracking mode. No sticky HUD, no mouse override, no camera fighting. |
| **Controller Tracking** | **Both Hands** | `TrackedPoseDriver` (`XRI Left/Right`) | Tracks official Quest 2 `UniversalController` 3D models with animated triggers and thumbsticks. Controllers rest naturally at hand height in front of the body. |
| **Keyboard Desktop Fallback** | **Keyboard** | <kbd>B</kbd> or <kbd>U</kbd> Key | Triggers the same B-button UI Toggle & Recall behavior during in-editor testing. |

---

### 🏛️ XR Origin Architecture & Hierarchy

The active VR hierarchy under `VR_Module.unity` follows the official XRI 3.5.1 gold standard:

```text
XR Origin                                       [XROrigin in Floor Mode @ (0, 76.55, -3.50)]
├── Camera Offset                               [Floor Offset Object @ local (0, 0, 0)]
│   └── Main Camera                             [SOLE active camera; TrackedPoseDriver: Head]
├── Left Controller                             [TrackedPoseDriver: LeftHand @ local (-0.25, 1.10, 0.35)]
│   ├── Near-Far Interactor                     [Active; Curve/Sphere Caster, SimpleHapticFeedback]
│   │   └── LineVisual                          [CurveVisualController, LineRenderer]
│   └── Left Controller Visual                  [ControllerAnimator, UniversalController 3D Model]
│       └── UniversalController                 [Bumper, Home, Base, TouchPad, Trigger, Buttons]
└── Right Controller                            [TrackedPoseDriver: RightHand @ local (0.25, 1.10, 0.35)]
    ├── Near-Far Interactor                     [Active; Curve/Sphere Caster, SimpleHapticFeedback]
    │   └── LineVisual                          [CurveVisualController, LineRenderer]
    └── Right Controller Visual                 [ControllerAnimator, UniversalController 3D Model]
        └── UniversalController                 [Bumper, Home, Base, TouchPad, Trigger, Buttons]
```

---

### 🌐 World-Anchored UI vs. Legacy "Sticky" Visor

In earlier iterations, the UI was parented to the headset camera in `HeadLocked` mode ("Iron Man Visor"), which caused the entire dashboard to drag across the viewport whenever the player turned their head, creating severe visual stickiness and motion discomfort.

In `VR_Module.unity`:
1. **World-Anchored by Default**: The `UIManager` (UI Toolkit UIDocument) is situated at `(0.00, 77.30, -1.80)`, standing comfortably in 3D world space like a physical mission console.
2. **Zero Camera Fighting**: No scripts parent the UI to the camera or modify the camera's local rotation.
3. **Dynamic Gaze Recall**: When the player presses the **B Button** to bring back the UI, `FloatingUIRecallController` calculates the user's flat horizontal forward vector (ignoring pitch tilt so the panel does not plant on the ground) and places the UI $1.35$m in front of them, facing them. Once placed, it stays completely stationary in world space.

---

### 🚧 Locomotion Baseline Notice

To guarantee clean, glitch-free tracking and zero input conflicts:
- Free-fly flight, teleportation, continuous movement, and snap-turning are **intentionally omitted** from this initial baseline.
- The scene is fully pre-configured with `XR Interaction Manager` and `EventSystem (XRUIInputModule)`, ready for clean, modular additions (such as teleport locomotion or snap turning) in future steps.

---

### 💻 XR Device Simulator (Desktop VR Testing)

When running `VR_Module.unity` inside the Unity Editor without a physical headset attached:

- **B Button UI Toggle**: Press <kbd>B</kbd> or <kbd>U</kbd> on your keyboard.
- **Cycle Active Simulator Hand**: Press <kbd>Tab</kbd> to cycle between **Left Hand**, **Right Hand**, or **Both Hands**.
- **Rotate Controller**: Hold <kbd>RMB</kbd> and move your mouse to aim the curved controller ray.
- **Pull Trigger (UI Click)**: Press <kbd>LMB</kbd> to select or click buttons.
- **Move Simulated Rig**: Use <kbd>W</kbd>, <kbd>A</kbd>, <kbd>S</kbd>, <kbd>D</kbd> while controlling the simulator device.

## 5. How Everything is Clicked & Input Safety Guards

### 🛡️ Smart UI Picking Shield (Click-Through Protection)
To prevent accidental actions, the system implements strict picking guards:
- **Never click the ground by accident**: If you click on any UI element (buttons, sliders, tabs, or minimap controls), the click is strictly absorbed by UI Toolkit. The 3D terrain placement raycast will **never** trigger a rover drop behind a button.
- **Text Input Driving Guard**: When typing numbers or text into UI fields (such as entering a seed or typing terrain dimensions), all rover driving inputs (<kbd>W</kbd>, <kbd>A</kbd>, <kbd>S</kbd>, <kbd>D</kbd>) are automatically muted so the rover does not drive away while you type.

### 🎯 Interactive Minimap Clicking
- The topographic radar minimap in Column 2 is fully interactive.
- You can **Left Click** directly on any point of the 2D minimap to place or relocate your rover to those coordinates on the planetary map.
- The minimap displays your active rover as a glowing teal dot with a real-time heading arrow, leaving behind breadcrumbs of your traversal history.

### 🎨 2D Canvas Painter Clicking
- Inside the **Terrain Lab** tab:
  - **Click & Hold LMB**: Continuously paints height modifications onto the canvas under your brush.
  - **Brush cursor circle**: Expands and contracts in real time to match your selected brush radius.
  - **Dirty-Rect Caching**: Saves and updates only the touched pixels, keeping frame rates at a silky 60+ FPS even with large $2048 \times 2048$ heightmaps.

---

## 6. Summary Table of Keybinds

| Key | Primary Function | Secondary / Alternative |
|---|---|---|
| <kbd>Tab</kbd> | Toggle Studio Mode $\longleftrightarrow$ Driving HUD Mode | — |
| <kbd>W</kbd>, <kbd>A</kbd>, <kbd>S</kbd>, <kbd>D</kbd> | Rover Drive: Forward, Left, Reverse, Right | Arrow Keys |
| <kbd>1</kbd>, <kbd>2</kbd>, <kbd>3</kbd>, <kbd>4</kbd> | Drive Speed: Stop (1), Precision (2), Explore (3), Cruise (4) | M2020 Steering Presets |
| <kbd>Q</kbd> | Placement: Rotate Left / M20: Raise Knees | — |
| <kbd>E</kbd> | Placement: Rotate Right / M20: Lower Knees | — |
| <kbd>M</kbd> | M2020: Cycle Steering Mode (Ackermann, Point Turn, Crab, Tank) | — |
| <kbd>Space</kbd> | M2020: Toggle Rocker-Bogie Ground Clearance Lift | — |
| <kbd>V</kbd> | Cycle Camera Perspectives (Rear, Left, Front, Right, Top) | Bottom Bar UI Buttons |
| <kbd>R</kbd> | Respawn Rover at Last Placement Location | VR Recenter Key |
| <kbd>Shift</kbd> + <kbd>LMB</kbd> | Instant Raycast Relocation on Terrain | — |
| <kbd>C</kbd> | Center Rover on Terrain Coordinates | UI Center Button |
| <kbd>P</kbd> | Open Planet Selection Modal (Mars, Moon, Earth, Titan, Venus) | Planet Header Badge |
| <kbd>Esc</kbd> | Cancel Rover Placement / Close Modals | — |
| <kbd>U</kbd> / <kbd>F1</kbd> | Minimize / Restore Studio to Floating Dock | Minimize Button |
| <kbd>H</kbd> | Cinematic Toggle (Hide / Restore All HUD Overlays) | — |
| <kbd>F3</kbd> | Toggle Aerospace Physics & Kinematics Debug Overlay | — |
| <kbd>Ctrl</kbd> + <kbd>Z</kbd> | Undo Heightmap Brush Stroke | UI Undo Button |
| <kbd>Ctrl</kbd> + <kbd>Y</kbd> | Redo Heightmap Brush Stroke | UI Redo Button |
| <kbd>+</kbd> / <kbd>-</kbd> | Adjust VR HUD Distance Forward / Backward | — |
| <kbd>LMB</kbd> (Left Click) | Click UI / Confirm Placement / Paint Canvas / Click Minimap | VR Index Trigger |
| <kbd>RMB</kbd> (Right Click) | Hold & Drag: Orbit Camera / Look in Free Fly / Cancel Placement | — |
| <kbd>Mouse Scroll</kbd> | Camera Zoom / Step-Rotate Placement Ghost Marker | — |

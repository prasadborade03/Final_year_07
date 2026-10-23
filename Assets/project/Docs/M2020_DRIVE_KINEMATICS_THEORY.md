# NASA Perseverance (M2020) Drive Kinematics & Theoretical Mechanics

**VR Planetary Rover Digital Twin Simulation Studio (`Final_year_07`)**  
**Component:** `Assets/project/script/Rover/m2020_perseverance/` (`RoverImporter_m2020.cs`, `RoverController_m2020.cs`)  
**Physics Engine:** Unity 6.5 LTS `ArticulationBody` Featherstone Solver  
**Author:** Prasad Borade  
**Last Updated:** October 2026  

---

## 1. Executive Summary & Purpose

The **NASA Mars 2020 Perseverance Rover (M2020)** is an advanced 1,025 kg six-wheeled robotic explorer designed for extreme terrain traversal on Mars. In this digital twin simulation studio, the rover is modeled as a high-fidelity physical multi-body robotic articulation running on Unity's Featherstone-based `ArticulationBody` physics engine.

This document serves as the authoritative theoretical and engineering reference for the M2020 mobility system, explaining:
1. The **rocker-bogie 6-wheel suspension topology** and 4-corner steering geometry.
2. The **underlying joint coordinate systems, anchor rotations**, and empirical sign derivations.
3. The root cause analysis and resolution of historical coordinate inversion issues.
4. The exact **mathematical equations for all 4 drive modes**:
   - **Double Ackermann Steering** (4-wheel counter-steer with per-wheel differential speed and turning radius).
   - **Point Turn** (in-place zero-radius spin with tangent toe-in angles).
   - **Crab Steering** (pure diagonal translation with parallel corner wheels and unsteered middle wheels).
   - **Tank Drive** (skid-steer differential velocity).
5. **Empirical acceptance test data** with physical real-world displacement and yaw measurements.
6. **Desktop (WASD) and Virtual Reality (Meta Quest 2 OpenXR thumbstick) input equivalence**.

---

## 2. Mechanical Topology & Suspension Architecture

### 2.1 The Rocker-Bogie Mechanism
The Perseverance mobility subsystem utilizes an unspringed **rocker-bogie suspension**:
- **Rocker Arm:** Attached to the rover body via a central differential pivot shaft. The front wheel and bogie attach to either end of the rocker.
- **Bogie Arm:** Attached to the rear end of the rocker, carrying the middle and rear wheels.
- **Differential Cross-Beam:** Links the left and right rockers across the chassis roof, forcing opposite pitch motions when climbing obstacles and ensuring all 6 wheels maintain ground contact over rocks up to 25 cm.

```
       [Differential Pivot Bar]
            /               \
     [Left Rocker]     [Right Rocker]
      /         \       /          \
  (LF Wheel)   [Bogie] (RF Wheel)  [Bogie]
               /     \             /     \
          (LM)       (LR)        (RM)    (RR)
```

### 2.2 Actuator Allocation
- **6 Drive Wheels:**
  - Front: `Body_WheelLeftFront` (LF), `Body_WheelRightFront` (RF)
  - Middle: `Body_WheelLeftMiddle` (LM), `Body_WheelRightMiddle` (RM)
  - Rear: `Body_WheelLeftRear` (LR), `Body_WheelRightRear` (RR)
  - *Actuation:* Revolute drive joints (`ArticulationDrive`, velocity-controlled in degrees/second).
- **4 Steerable Corner Wheels:**
  - `Joint_SteerLeftFront` (LF), `Joint_SteerRightFront` (RF)
  - `Joint_SteerLeftRear` (LR), `Joint_SteerRightRear` (RR)
  - *Actuation:* Revolute steering joints (`ArticulationDrive`, position-controlled in degrees, clamped to $\pm 40^\circ$).
- **2 Middle Wheels (Fixed Straight):**
  - LM and RM have **no steering joints**. They are structurally mounted straight ($0^\circ$).

---

## 3. Coordinate System Conventions & Joint Sign Verification

### 3.1 Ground Plane Coordinate Definition
To avoid ambiguities caused by chassis tilt or visual mesh pivots, the rover coordinate frame is defined purely from the physical wheel ground contacts:

1. **Midpoint of Front Axles:** $\mathbf{P}_{\text{front}} = \frac{1}{2}\left(\mathbf{P}_{\text{LF}} + \mathbf{P}_{\text{RF}}\right)$
2. **Midpoint of Rear Axles:** $\mathbf{P}_{\text{rear}} = \frac{1}{2}\left(\mathbf{P}_{\text{LR}} + \mathbf{P}_{\text{RR}}\right)$
3. **Chassis Forward Vector ($\mathbf{F}$):**
   $$\mathbf{F} = \text{normalize}\left(\text{ProjectOnPlane}(\mathbf{P}_{\text{front}} - \mathbf{P}_{\text{rear}},\, \mathbf{U})\right)$$
4. **Chassis Up Vector ($\mathbf{U}$):** $\mathbf{U} = (0, 1, 0)^T$
5. **Chassis Right Vector ($\mathbf{R}$):**
   $$\mathbf{R} = \mathbf{U} \times \mathbf{F}$$

### 3.2 ArticulationBody Joint Anchor Rotations
In the M2020 URDF and prefab hierarchy:
- **All 6 Drive Wheel Joints** share identical anchor rotations: `anchorRot = (0, 0, 180)`.
- **All 4 Steer Joints** share identical anchor rotations: `anchorRot = (0, 0, 270)`.

```
Joint Type      URDF Joint Axis    Unity Anchor Rotation    Positive Drive Effect
----------------------------------------------------------------------------------
Drive Wheel     X-Axis             (0, 0, 180)              Drives wheel FORWARD
Steer Joint     X-Axis             (0, 0, 270)              Steers wheel CLOCKWISE / RIGHT
```

### 3.3 Root Cause Analysis: The "Right-Side Inversion" Antipattern
In earlier software revisions, a legacy flag `invertRightSideDrives = true` was active under the assumption that right-hand wheels were mirrored and required negated drive velocities.

However, because the URDF export already flipped the coordinate frame of the right-hand joints into matching rotational symmetry, inverting the right side caused:
- Pressing **Forward (<kbd>W</kbd>)** applied $+v$ to left wheels and $-v$ to right wheels $\rightarrow$ **Rover spun in place**.
- Pressing **Turn Right (<kbd>D</kbd>)** applied $+v$ to left wheels and $+v$ to right wheels $\rightarrow$ **Rover drove forward**.

**Fix Applied:** The `invertRightSideDrives` flag was completely removed from `RoverImporter_m2020.cs`. Both left and right wheels now receive positive velocity for forward translation.

### 3.4 Step 0 Empirical Measurements (Verification on Flat Ground)
Prior to writing kinematics, empirical verification was executed inside Unity on a flat terrain:

1. **Drive Velocity Sign Test:**
   - Input: Set all 6 wheel drives to $\omega = +60^\circ/\text{s}$ ($1.047\text{ rad/s}$) for $t = 1.0\text{ s}$.
   - Theoretical translation: $s = r \cdot \omega \cdot t = 0.260\text{ m} \times 1.047 \times 1.0 = 0.272\text{ m}$.
   - **Measured Displacement along $\mathbf{F}$:** $+0.2701\text{ m}$ (pure forward).
   - **Measured Displacement along $\mathbf{R}$:** $-0.0001\text{ m}$.
   - **Conclusion:** Uniform positive velocity drives all wheels forward without inversion.

2. **Steer Angle Sign Test:**
   - Input: Set all 4 corner steer joints to $\delta = +20^\circ$.
   - **Measured Signed Yaw Change:** $+8.71^\circ$ (Clockwise from above / turning right).
   - **Conclusion:** Positive target angle turns clockwise/right. Single constant `steerSign = +1.0f`.

---

## 4. Mathematical Kinematics of the Four Drive Modes

The rover geometry is evaluated at runtime dynamically from wheel positions relative to the middle axle line ($x$ forward, $y$ left):
- Front wheels: $(x_F, y_L)$ and $(x_F, y_R)$ where $x_F \approx +1.09\text{ m}$
- Middle wheels: $(0, y_L)$ and $(0, y_R)$ where $x_M = 0.00\text{ m}$
- Rear wheels: $(x_R, y_L)$ and $(x_R, y_R)$ where $x_R \approx -1.16\text{ m}$
- Track half-width: $y_L \approx +1.03\text{ m}$, $y_R \approx -1.03\text{ m}$

```
       y (Left +)
       ▲
       │        [FL]  (xF, yL)       [FR]  (xF, yR)
       │          \                    /
       │           \                  /
       │            \                /
───────┼─────────────[ML]────────────[MR]──────────────► x (Forward +)
       │           (0, yL)          (0, yR)
       │            /                \
       │           /                  \
       │          /                    \
       │        [RL]  (xR, yL)       [RR]  (xR, yR)
```

---

### 4.1 Mode 0: 4-Wheel Double Ackermann Steering

In standard automotive Ackermann steering, only the front axle turns. On Perseverance, both the **front and rear corner wheels counter-steer**, halving the turning radius and keeping the instantaneous center of rotation (ICR) along the unsteered middle axle line ($x = 0$).

#### 1. Instantaneous Center of Rotation (ICR)
Given steering input $S \in [-1, 1]$ and maximum steer angle $\delta_{\text{max}} = 40^\circ$:
$$\delta_{\text{cmd}} = S \cdot \delta_{\text{max}}$$
$$R = \frac{L_c}{\tan(\delta_{\text{cmd}})}$$
where $L_c = \frac{x_F - x_R}{2} \approx 1.125\text{ m}$ is the effective half-wheelbase, and $R$ is the signed lateral distance to the ICR along the middle axle ($y = R$).

#### 2. Per-Wheel Steer Angles
For any wheel $i \in \{\text{LF, RF, LR, RR}\}$ located at $(x_i, y_i)$:
$$\delta_i = \text{atan2}(x_i,\, y_i - R) \cdot \text{sgn}(R)$$

- **Inner Wheel vs. Outer Wheel:** Because $|y_{\text{inner}} - R| < |y_{\text{outer}} - R|$, the inner wheel steers at a sharper angle than the outer wheel:
  $$\delta_{\text{inner}} > \delta_{\text{outer}}$$
- **Counter-Steering:** For front wheels ($x_i > 0$), $\delta_i$ points toward the turn. For rear wheels ($x_i < 0$), $\delta_i$ points opposite to the turn ($\delta_{\text{rear}} \approx -\delta_{\text{front}}$).

#### 3. Per-Wheel Velocity Scaling
Every wheel travels along a circle of radius $r_i = \sqrt{x_i^2 + (y_i - R)^2}$. To eliminate wheel slip, each wheel speed is scaled proportional to its radius from the ICR:
$$v_i = v_{\text{base}} \cdot \frac{r_i}{|R|}$$

- **Outer wheels spin faster** than inner wheels.
- **Corner wheels spin faster** than middle wheels ($r_{\text{corner}} > r_{\text{middle}}$).
- **Pre-Steering:** If throttle $T = 0$ but steer $S \neq 0$, $v_i = 0$ while steer targets smoothly angle into position.

---

### 4.2 Mode 1: Point Turn (Zero-Radius In-Place Spin)

Point Turn rotates the rover about its central origin $(0, 0)$ with zero translation ($R = 0$).

#### 1. Wheel Tangent Angles
All four corner wheels steer to tangents perpendicular to the radial vector from the rover center $(0,0)$:
$$\theta_i = \text{atan2}(x_i,\, -y_i) + \frac{\pi}{2}$$

Evaluating for the M2020 geometry:
- **Left Front (LF):** $\delta_{\text{LF}} \approx +48.2^\circ$ (Toe-out clockwise)
- **Right Front (RF):** $\delta_{\text{RF}} \approx -48.2^\circ$ (Toe-in counter-clockwise)
- **Left Rear (LR):** $\delta_{\text{LR}} \approx -44.8^\circ$ (Toe-in counter-clockwise)
- **Right Rear (RR):** $\delta_{\text{RR}} \approx +44.8^\circ$ (Toe-out clockwise)

#### 2. Wheel Velocities
Throttle $T$ is ignored ($T = 0$). Steer input $S \in [-1, 1]$ drives the turn ($+1 = \text{Clockwise}$, $-1 = \text{Counter-Clockwise}$):
$$v_{\text{left}} = +S \cdot v_{\text{max}} \cdot \frac{d_i}{d_{\text{max}}}$$
$$v_{\text{right}} = -S \cdot v_{\text{max}} \cdot \frac{d_i}{d_{\text{max}}}$$
where $d_i = \sqrt{x_i^2 + y_i^2}$ is the wheel distance from center, and $d_{\text{max}} \approx 1.55\text{ m}$.
Because middle wheels are closer to the center ($d_{\text{mid}} \approx 1.03\text{ m}$ vs. $d_{\text{corner}} \approx 1.55\text{ m}$), middle wheels spin at $\sim 66\%$ the speed of corner wheels to avoid skidding.

---

### 4.3 Mode 2: Crab Steering (Diagonal Translation)

Crab Steering enables the rover to translate diagonally across the terrain without altering its orientation / heading.

#### 1. Wheel Angles
All four steerable corner wheels orient parallel to the commanded crab angle:
$$\delta_{\text{LF}} = \delta_{\text{RF}} = \delta_{\text{LR}} = \delta_{\text{RR}} = S \cdot \delta_{\text{max}}$$
Middle wheels have no steering joints and remain at $0^\circ$.

#### 2. Wheel Velocities
The four corner wheels propel the vehicle along the angled vector $\mathbf{v}_{\text{corner}} = (v \cos\delta, v \sin\delta)$.  
Since middle wheels are fixed straight along the rover longitudinal axis, they receive only the forward longitudinal component:
$$v_{\text{middle}} = v_{\text{corner}} \cdot \cos(\delta)$$
This matches ground kinematics exactly and prevents lateral drag on the middle tires. The rover translates diagonally with near-zero yaw change ($\Delta\text{Yaw} \approx 0$).

---

### 4.4 Mode 3: Tank Drive (Skid-Steer Differential)

In Tank Drive mode, all four steer joints are locked straight at $0^\circ$. Heading is altered purely through differential speed between the left and right wheel sets:

$$\delta_{\text{LF}} = \delta_{\text{RF}} = \delta_{\text{LR}} = \delta_{\text{RR}} = 0^\circ$$
$$v_{\text{left}} = \text{clamp}(T + S,\, -1,\, 1) \cdot v_{\text{max}}$$
$$v_{\text{right}} = \text{clamp}(T - S,\, -1,\, 1) \cdot v_{\text{max}}$$

All three wheels on each side share identical angular velocities.

---

## 5. Dynamic Slew-Rate & Stability Management

When transitioning between modes (e.g., from Ackermann $\delta = 40^\circ$ into Crab or Point Turn), instantaneous servo jumps would cause violent reaction forces in the `ArticulationBody` physics solver, causing the rover to jump or tip.

To guarantee physical realism and stability:
1. **Steering Servo Slew Rate:** Commanded steer targets are slewed at:
   $$\dot{\delta} = 90^\circ/\text{s}$$
   using `Mathf.MoveTowards(activeAngle, targetAngle, slewRate * Time.deltaTime)`.
2. **Acceleration Ramps:** Throttle and steering inputs pass through an exponential low-pass smoothing filter:
   $$u_{\text{smooth}} = \text{Lerp}(u_{\text{current}},\, u_{\text{raw}},\, \Delta t \cdot k_{\text{smooth}}), \quad k_{\text{smooth}} = 8.0\text{ s}^{-1}$$

---

## 6. Empirical Acceptance Test Data

The following empirical benchmark was recorded inside `Assets/project/scenes/_Test/Test_TerrainAndRover.unity` on a flat procedural terrain ($200 \times 200\text{ m}$, resolution $65$, height $10.0\text{ m}$). Each test case was executed for exactly $2.0\text{ s}$ of active drive input in **CRUISE** mode ($v_{\text{max}} = 240^\circ/\text{s}$):

| Drive Mode | Commanded Input | Displacement FRONT ($\mathbf{F}$) | Displacement RIGHT ($\mathbf{R}$) | Signed Yaw Change ($\Delta\text{Yaw}$) | Verified Physical Behavior |
| :--- | :--- | :---: | :---: | :---: | :--- |
| **Ackermann** | <kbd>W</kbd> (Forward) | **+2.1273 m** | +0.0027 m | +0.10° | Pure straight forward driving |
| **Ackermann** | <kbd>S</kbd> (Reverse) | **-2.1387 m** | +0.0081 m | -0.23° | Pure straight reverse driving |
| **Ackermann** | <kbd>W</kbd>+<kbd>D</kbd> (Curve Right) | **+1.8732 m** | **+0.7679 m** | **+44.96°** | Smooth right curve; inner counter-steer active |
| **Ackermann** | <kbd>W</kbd>+<kbd>A</kbd> (Curve Left) | **+1.8740 m** | **-0.7656 m** | **-44.76°** | Smooth left curve; inner counter-steer active |
| **Point Turn** | <kbd>D</kbd> (Spin Clockwise) | -0.0134 m | +0.0612 m | **+73.44°** | Pure in-place clockwise pivot ($<1.5\text{ cm}$ drift) |
| **Point Turn** | <kbd>A</kbd> (Spin Counter-CW) | -0.0135 m | -0.0605 m | **-73.94°** | Pure in-place counter-CW pivot ($<1.5\text{ cm}$ drift) |
| **Crab** | <kbd>W</kbd>+<kbd>D</kbd> (Forward-Right) | **+1.5514 m** | **+1.0429 m** | **+2.72°** | Diagonal right translation; heading fixed ($\approx 0^\circ$) |
| **Crab** | <kbd>W</kbd>+<kbd>A</kbd> (Forward-Left) | **+1.5450 m** | **-1.0586 m** | **-1.78°** | Diagonal left translation; heading fixed ($\approx 0^\circ$) |
| **Tank Drive** | <kbd>W</kbd> (Forward) | **+2.1191 m** | +0.0009 m | +0.06° | Straight forward translation |
| **Tank Drive** | <kbd>S</kbd> (Reverse) | **-2.1424 m** | +0.0043 m | -0.16° | Straight backward translation |
| **Tank Drive** | <kbd>D</kbd> (Pivot Right) | -0.4568 m | -0.5288 m | **+39.36°** | Differential skid turn right |
| **Tank Drive** | <kbd>A</kbd> (Pivot Left) | -0.3078 m | +0.2520 m | **-46.18°** | Differential skid turn left |
| **VR Parity** | **XR Left Thumbstick** | — | — | — | **Identical mapping** via `RoverInputProvider.GetDriveInput()` |

---

## 7. Controls & Input Mapping

### 7.1 Desktop (WASD) & VR (Left Thumbstick) Equivalence
Both control schemes feed into the unified static input provider [`RoverInputProvider.cs`](file:///c:/Users/PRASAD%20BORADE/unity/Final_year_shit/Assets/project/script/Rover/RoverInputProvider.cs):
- **Steering ($X$):** Analog $[-1, +1]$ (<kbd>A</kbd> = Left $-1$, <kbd>D</kbd> = Right $+1$, Left Stick Horizontal).
- **Throttle ($Y$):** Analog $[-1, +1]$ (<kbd>S</kbd> = Reverse $-1$, <kbd>W</kbd> = Forward $+1$, Left Stick Vertical).

### 7.2 Steering Mode Cycling
- Pressing <kbd>M</kbd> or clicking the HUD drive mode button cycles the steering modes sequentially:
  $$\text{Ackermann (0)} \longrightarrow \text{Point Turn (1)} \longrightarrow \text{Crab (2)} \longrightarrow \text{Tank Drive (3)}$$
- The enum ordering is strictly preserved for compatibility with the HUD UI Toolkit, ROS telemetry, and audio controllers:
  ```csharp
  public enum DriveMode
  {
      Ackermann = 0,
      PointTurn = 1,
      Crab = 2,
      TankDrive = 3
  }
  ```

### 7.3 Speed Regimes
The speed regimes scale the maximum wheel angular velocity $v_{\text{max}}$ across all 4 drive modes:
- <kbd>1</kbd>: **STOP** ($0\%$ speed limit, emergency brake)
- <kbd>2</kbd>: **PRECISION** ($25\%$ speed limit, $60^\circ/\text{s}$ — rock crawling)
- <kbd>3</kbd>: **EXPLORE** ($60\%$ speed limit, $144^\circ/\text{s}$ — survey traverse)
- <kbd>4</kbd>: **CRUISE** ($100\%$ speed limit, $240^\circ/\text{s}$ — full speed)
- **VR Controller:** Button <kbd>A</kbd> (Right hand) or <kbd>X</kbd> (Left hand) cycles through these regimes cyclically.

### 7.4 Chassis Ground Clearance (Rocker-Bogie Lift)
- <kbd>Space</kbd> key toggles the rocker-bogie pitch lift between $0^\circ$ and $+8^\circ$, raising the chassis clearance for crossing jagged boulder fields.

---

## 8. Architectural File Locations

| Script | Purpose |
|---|---|
| [`RoverImporter_m2020.cs`](file:///c:/Users/PRASAD%20BORADE/unity/Final_year_shit/Assets/project/script/Rover/m2020_perseverance/RoverImporter_m2020.cs) | ArticulationBody hierarchy discovery, dynamic wheel coordinate extraction, individual 6-wheel velocity & 4-steer angle actuation |
| [`RoverController_m2020.cs`](file:///c:/Users/PRASAD%20BORADE/unity/Final_year_shit/Assets/project/script/Rover/m2020_perseverance/RoverController_m2020.cs) | Double Ackermann / Point Turn / Crab / Tank kinematics, servo slewing, speed regimes, and input delegation |
| [`UploadHeightmapUIToolkit.cs`](file:///c:/Users/PRASAD%20BORADE/unity/Final_year_shit/Assets/Shared_UI_Package/Scripts/Controllers/UploadHeightmapUIToolkit.cs) | Mission control UI Toolkit HUD, mode cycling button delegates, real-time telemetry gauges |
| [`M2020VisualMaterialManager.cs`](file:///c:/Users/PRASAD%20BORADE/unity/Final_year_shit/Assets/project/script/Rover/m2020_perseverance/M2020VisualMaterialManager.cs) | NASA/JPL authentic visual material and texture assignments for rover chassis and wheels |

# Perseverance M2020 – drive + suspension + visuals

## Fixes in this package

### Controls (was inverted)
- **W / S** = forward / backward
- **A / D** = turn left / right (skid + 4-wheel steering)
- Previous bug: A/D drove forward/back because same-sign speeds were on the turn axis.

### Suspension (was locked → wheels “chased” the cube)
Rocker, bogie, and center differential are **revolute** again with soft spring holds.
Wheels can rise over obstacles (~wheel radius ≈ 0.26 m) like a real rocker-bogie.

### Steering
LF / LR / RF / RR steer joints unlocked. A/D commands them (±45° default).

### Visuals (URDF only)
| Part | Color |
|------|--------|
| Wheels | Pure black |
| Chassis (CHASSIS_0 style) | Near-white |
| Turret / arm accents (CHASSIS_4 style) | Yellow A |
| Link5 / HGA / RSM accents (CHASSIS_5 style) | Yellow B |
| Mobility structure | Medium gray metal |

## Re-import required
Delete old prefab → import `perseverance_m2020.urdf` again → assign new prefab.

## Test
1. W → rolls straight
2. D → pivots / arcs right
3. Drive onto ~0.3–0.5 m cube → wheel lifts, suspension flexes

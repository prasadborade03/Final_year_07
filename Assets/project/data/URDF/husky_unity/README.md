# Husky A200 – Unity-compatible URDF (fixed joints)

Plain URDF from Clearpath `husky-noetic-devel` for the **Unity Robotics URDF Importer**.

## Critical Unity setup (prevents body falling off wheels)

After importing `husky.urdf`:

1. Select the **root** GameObject (the one with `ArticulationBody`).
2. In the Inspector set **Immovable = true** (temporary, while you check the hierarchy).
3. Expand the four wheel links and, on each continuous joint / ArticulationBody drive:
   - **Stiffness** ≈ `100000`
   - **Damping** ≈ `10000`
   - **Force Limit** ≈ `1000000`
4. Make sure the four wheel ArticulationBodies are children of `base_link` (they should be after a clean import).
5. Uncheck **Immovable** and press Play. The chassis should stay attached to the wheels.

If the wheels still separate:
- Delete the previous prefab and re-import the whole `husky_unity` folder.
- Prefer **STL** meshes (already used for body & wheels in this package).
- Disable gravity briefly to verify joint hierarchy, then re-enable.

## Contents

```
husky_unity/
├── husky.urdf
├── meshes/
│   ├── base_link.stl / .dae
│   ├── top_chassis.stl / .dae
│   ├── bumper.dae
│   ├── top_plate.stl / .dae
│   ├── user_rail.stl / .dae
│   └── wheel.stl / .dae
└── README.md
```

## What was fixed vs the previous package

| Problem                         | Fix                                              |
|---------------------------------|--------------------------------------------------|
| Body falls off / joints break   | Mass moved onto `base_link` (required by ArticulationBody) |
| DAE orientation drift           | Main visuals switched to STL                     |
| Weak continuous joints          | Documented high stiffness / damping / force limit|
| Missing collision stability     | Box + cylinder primitives for body & wheels      |

## Default robot

- Chassis + top chassis, front/rear bumpers, top plate, user rail
- 4 continuous wheels (front_left/right, rear_left/right)
- IMU link
- No optional sensors / PACS

Source: https://github.com/husky/husky (BSD)

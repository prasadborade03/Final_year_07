# Arduinobot – Fixed for Unity URDF Importer

## Folder structure (exactly this)

```
arduinobot/
├── arduinobot.urdf          ← put this at the ROOT of the folder
├── meshes/
│   ├── basement.STL
│   ├── base_plate.STL
│   ├── ... (all 13 STLs)
└── README.md
```

## How to import (clean steps)

1. **Delete the old broken folder**  
   Delete `Assets/project/data/URDF/arduinobot` completely (including any generated `*_1.asset`, `*_2.asset` … files).

2. **Copy the new package**  
   Unzip this archive so that you have:
   ```
   Assets/project/data/URDF/arduinobot/
       arduinobot.urdf
       meshes/
           *.STL
   ```

3. **Import**
   - Right-click `arduinobot.urdf`
   - **Import Robot from Selected URDF file**
   - Choose **Articulation Body**
   - **Important**: In the import options, leave “Generate Colliders” ON (they are now simple boxes, so VHACD will not run).
   - Click Import.

4. **Done.** You should see the full robot with all meshes and no path or VHACD errors.

## Why the previous import failed

- Paths starting with `../` are rewritten by the importer into broken `package://` paths.
- Full mesh collisions on complex STLs trigger VHACD, which was crashing with NullReference and creating 90+ temporary mesh assets.

This version uses:
- Relative paths `meshes/xxx.STL` (no `../`)
- Simple box/cylinder collisions → no VHACD

## Optional controller

A simple keyboard controller is available in the previous package if you still need it.  
You can also drive the joints with the built-in ArticulationBody drives or write your own script matching the joint names (`joint_1` … `joint_9`, `left_finger_joint`, `right_finger_joint`).

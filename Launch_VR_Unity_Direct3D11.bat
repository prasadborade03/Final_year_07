@echo off
echo =======================================================
echo Launching Unity 6 Editor in Direct3D 11 mode for VR...
echo Prevents the OpenXR DirectX 12 GPU device crash.
echo =======================================================
start "" "C:\Program Files\Unity\Hub\Editor\6000.5.0f1\Editor\Unity.exe" -projectpath "C:\Users\PRASAD BORADE\unity\Final_year_shit" -force-d3d11

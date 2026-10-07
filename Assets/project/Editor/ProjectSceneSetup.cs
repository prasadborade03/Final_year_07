using System;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using ProjectName.UI;
using ProjectName.Core;
using ProjectName.VR;

namespace ProjectName.Editor
{
    [InitializeOnLoad]
    public static class ProjectSceneSetup
    {
        private const string MainMenuScenePath = "Assets/project/scenes/MainMenu.unity";
        private const string VRModuleScenePath = "Assets/project/scenes/VR_Module.unity";
        private const string SimVRScenePath = "Assets/project/scenes/Simulation_VR.unity";
        private const string SimFlatScenePath = "Assets/project/scenes/Simulation_Flat.unity";

        private const string MainMenuUxmlPath = "Assets/project/UI/MainMenu.uxml";
        private const string FlatPanelSettingsPath = "Assets/project/UI/FlatPanelSettings.asset";
        private const string MainMenuPanelSettingsPath = "Assets/project/UI/MainMenuPanelSettings.asset";

        private const string SetupCompleteSessionKey = "DualSceneArchitecture_Setup_v1";

        static ProjectSceneSetup()
        {
            EditorApplication.delayCall += AutoSetupOnce;
        }

        private static void AutoSetupOnce()
        {
            if (SessionState.GetBool(SetupCompleteSessionKey, false)) return;
            SessionState.SetBool(SetupCompleteSessionKey, true);
            ExecuteFullProjectSceneSetup();
        }

        [MenuItem("VR Digital Twin/Setup Dual Scene Flow (MainMenu + VR + Flat)", false, 50)]
        public static void ExecuteFullProjectSceneSetup()
        {
            Debug.Log("<color=#00E5FF><b>[ProjectSceneSetup] Building Dual-Scene Architecture & 7-Step User Flow...</b></color>");

            try
            {
                // 1. Create Flat & MainMenu PanelSettings assets
                var flatPanelSettings = GetOrCreateScreenSpacePanelSettings(FlatPanelSettingsPath);
                var mainMenuPanelSettings = GetOrCreateScreenSpacePanelSettings(MainMenuPanelSettingsPath);

                // 2. Duplicate VR_Module.unity into Simulation_VR.unity (if not already existing)
                if (!File.Exists(SimVRScenePath))
                {
                    AssetDatabase.CopyAsset(VRModuleScenePath, SimVRScenePath);
                    Debug.Log($"[ProjectSceneSetup] Created '{SimVRScenePath}' as duplicate of '{VRModuleScenePath}'.");
                }

                // 3. Duplicate VR_Module.unity into Simulation_Flat.unity
                if (!File.Exists(SimFlatScenePath))
                {
                    AssetDatabase.CopyAsset(VRModuleScenePath, SimFlatScenePath);
                    Debug.Log($"[ProjectSceneSetup] Created '{SimFlatScenePath}' as duplicate of '{VRModuleScenePath}'.");
                }

                AssetDatabase.Refresh();

                // 4. Configure Simulation_Flat.unity for desktop interaction
                ConfigureSimulationFlatScene(SimFlatScenePath, flatPanelSettings);

                // 5. Create MainMenu.unity scene
                CreateMainMenuScene(MainMenuScenePath, mainMenuPanelSettings);

                // 6. Register Scenes in EditorBuildSettings
                UpdateEditorBuildSettings();

                Debug.Log("<color=#00FF88><b>[ProjectSceneSetup] Dual Scene Flow successfully generated & verified!</b></color>");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ProjectSceneSetup] Setup error: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private static PanelSettings GetOrCreateScreenSpacePanelSettings(string assetPath)
        {
            var ps = AssetDatabase.LoadAssetAtPath<PanelSettings>(assetPath);
            if (ps == null)
            {
                ps = ScriptableObject.CreateInstance<PanelSettings>();
                ps.renderMode = PanelRenderMode.ScreenSpaceOverlay;
                ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                ps.referenceResolution = new Vector2Int(1920, 1080);
                ps.match = 0.5f;
                ps.clearDepthStencil = false;

                // Serialized object configuration
                SerializedObject so = new SerializedObject(ps);
                var scaleProp = so.FindProperty("m_Scale");
                if (scaleProp != null) scaleProp.floatValue = 1f;
                so.ApplyModifiedProperties();

                var atlas = ps.dynamicAtlasSettings;
                atlas.minAtlasSize = 1024;
                atlas.maxAtlasSize = 4096;
                ps.dynamicAtlasSettings = atlas;

                AssetDatabase.CreateAsset(ps, assetPath);
                AssetDatabase.SaveAssets();
                Debug.Log($"[ProjectSceneSetup] Created ScreenSpace PanelSettings at '{assetPath}'.");
            }
            else
            {
                ps.renderMode = PanelRenderMode.ScreenSpaceOverlay;
                ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                ps.referenceResolution = new Vector2Int(1920, 1080);
                ps.match = 0.5f;
                EditorUtility.SetDirty(ps);
            }
            return ps;
        }

        private static void ConfigureSimulationFlatScene(string scenePath, PanelSettings flatPanelSettings)
        {
            var currentScene = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            // Configure UIManager for Screen-Space Desktop Overlay
            var uiDoc = UnityEngine.Object.FindAnyObjectByType<UIDocument>();
            if (uiDoc != null)
            {
                if (flatPanelSettings != null)
                {
                    uiDoc.panelSettings = flatPanelSettings;
                    EditorUtility.SetDirty(uiDoc);
                }

                // In Desktop Flat mode, disable VR world positioner & recall controllers so UI overlays directly on screen
                var vrPos = uiDoc.GetComponent<VRUIPositioner>();
                if (vrPos != null) vrPos.enabled = false;

                var recall = uiDoc.GetComponent<FloatingUIRecallController>();
                if (recall != null) recall.enabled = false;

                var boxCol = uiDoc.GetComponent<BoxCollider>();
                if (boxCol != null) boxCol.enabled = false;

                EditorUtility.SetDirty(uiDoc.gameObject);
                Debug.Log($"[ProjectSceneSetup] Configured UIManager in '{scenePath}' for ScreenSpaceOverlay.");
            }

            // In Desktop Flat mode, disable VR controllers so raycasters don't conflict with mouse cursor
            var leftCtrl = GameObject.Find("Left Controller");
            if (leftCtrl != null) leftCtrl.SetActive(false);

            var rightCtrl = GameObject.Find("Right Controller");
            if (rightCtrl != null) rightCtrl.SetActive(false);

            // Ensure Main Camera exists and has DesktopFreeFlyCamera / OrbitCamera enabled
            var mainCam = Camera.main;
            if (mainCam != null)
            {
                if (mainCam.GetComponent<DesktopFreeFlyCamera>() == null)
                {
                    mainCam.gameObject.AddComponent<DesktopFreeFlyCamera>();
                }
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[ProjectSceneSetup] '{scenePath}' saved successfully.");
        }

        private static void CreateMainMenuScene(string scenePath, PanelSettings mainMenuPanelSettings)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 1. Main Camera
            var camGo = new GameObject("Main Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.03f, 0.05f, 0.08f, 1f);
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 1000f;
            camGo.tag = "MainCamera";
            camGo.transform.position = new Vector3(0f, 1f, -10f);

            // 2. Directional Light
            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(0.8f, 0.9f, 1f);
            light.intensity = 1.0f;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            // 3. EventSystem with InputSystemUIInputModule
            var esGo = new GameObject("EventSystem");
            esGo.AddComponent<EventSystem>();
            esGo.AddComponent<InputSystemUIInputModule>();

            // 4. Main Menu UI Manager
            var menuMgrGo = new GameObject("MainMenuManager");
            var uiDoc = menuMgrGo.AddComponent<UIDocument>();
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(MainMenuUxmlPath);
            if (uxml != null)
            {
                uiDoc.visualTreeAsset = uxml;
            }
            if (mainMenuPanelSettings != null)
            {
                uiDoc.panelSettings = mainMenuPanelSettings;
            }

            menuMgrGo.AddComponent<MainMenuController>();
            menuMgrGo.AddComponent<SceneLoader>();

            EditorSceneManager.SaveScene(scene, scenePath);
            Debug.Log($"[ProjectSceneSetup] '{scenePath}' created and saved successfully.");
        }

        private static void UpdateEditorBuildSettings()
        {
            var scenes = new EditorBuildSettingsScene[]
            {
                new EditorBuildSettingsScene(MainMenuScenePath, true),
                new EditorBuildSettingsScene(SimVRScenePath, true),
                new EditorBuildSettingsScene(SimFlatScenePath, true),
                new EditorBuildSettingsScene(VRModuleScenePath, true)
            };

            EditorBuildSettings.scenes = scenes;
            Debug.Log($"[ProjectSceneSetup] Updated EditorBuildSettings with {scenes.Length} scenes (MainMenu=0, Simulation_VR=1, Simulation_Flat=2, VR_Module=3).");
        }
    }
}

using System;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.UIElements;
using ProjectName.UI;
using ProjectName.VR;

namespace ProjectName.Editor
{
    [InitializeOnLoad]
    public static class UIDiagnosticFixer
    {
        static UIDiagnosticFixer()
        {
            EditorApplication.delayCall += RunDiagnosticAndFix;
        }

        [MenuItem("VR Digital Twin/Diagnose and Fix UI", false, 100)]
        public static void RunDiagnosticAndFix()
        {
            Debug.Log("<color=#00E5FF><b>[UIDiagnosticFixer] Starting comprehensive UI diagnosis & repair...</b></color>");

            // 1. Clear any poisoned PlayerPrefs that hide the VR HUD
            PlayerPrefs.DeleteKey("VR_HUD_Visible");
            PlayerPrefs.Save();
            Debug.Log("[UIDiagnosticFixer] Cleared PlayerPrefs 'VR_HUD_Visible'.");

            // 2. Fix PanelSettings assets
            FixPanelSettingsAsset("Assets/project/UI/WorkbenchPanelSettings.asset");
            FixPanelSettingsAsset("Assets/Shared_UI_Package/UI_Toolkit/WorkbenchPanelSettings.asset");

            // 3. Inspect Active Scene
            var currentScene = EditorSceneManager.GetActiveScene();
            Debug.Log($"[UIDiagnosticFixer] Active scene: {currentScene.name} (path: {currentScene.path})");
            if (currentScene.name == "MainMenu")
            {
                Debug.Log("[UIDiagnosticFixer] Active scene is MainMenu (ScreenSpaceOverlay). Skipping VR HUD positioner.");
                return;
            }

            var uiManager = GameObject.Find("UIManager");
            if (uiManager == null)
            {
                var allUiDocs = UnityEngine.Object.FindObjectsByType<UIDocument>(FindObjectsInactive.Include);
                foreach (var doc in allUiDocs)
                {
                    if (doc.gameObject.name.Contains("UI") || doc.name.Contains("UI"))
                    {
                        uiManager = doc.gameObject;
                        break;
                    }
                }
            }

            if (uiManager != null)
            {
                Debug.Log($"[UIDiagnosticFixer] Found UIManager GameObject '{uiManager.name}', activeSelf={uiManager.activeSelf}, worldPos={uiManager.transform.position}");
                
                // Ensure active
                if (!uiManager.activeSelf)
                {
                    uiManager.SetActive(true);
                    EditorUtility.SetDirty(uiManager);
                    Debug.Log("[UIDiagnosticFixer] Activated UIManager GameObject.");
                }

                // Check UIDocument
                var uiDoc = uiManager.GetComponent<UIDocument>();
                if (uiDoc != null)
                {
                    Debug.Log($"[UIDiagnosticFixer] UIDocument: sourceAsset={uiDoc.visualTreeAsset?.name}, panelSettings={uiDoc.panelSettings?.name}");
                    if (uiDoc.panelSettings != null)
                    {
                        FixPanelSettings(uiDoc.panelSettings);
                        EditorUtility.SetDirty(uiDoc.panelSettings);
                    }
                }

                // Position UI at user-preferred comfortable viewing depth (Z=-1.84, which is 1.66m from headset at Z=-3.50)
                uiManager.transform.position = new Vector3(0f, 77.35f, -1.84f);
                uiManager.transform.rotation = Quaternion.identity;
                EditorUtility.SetDirty(uiManager.transform);

                // Check VRUIPositioner
                var positioner = uiManager.GetComponent<VRUIPositioner>();
                if (positioner != null)
                {
                    positioner.isHUDVisible = true;
                    positioner.stickyMode = VRUIPositioner.StickyMode.WorldAnchor;
                    positioner.forwardDistance = 1.66f;
                    positioner.heightOffset = -0.05f;
                    EditorUtility.SetDirty(positioner);
                    Debug.Log("[UIDiagnosticFixer] VRUIPositioner reset: isHUDVisible=true, stickyMode=WorldAnchor, forwardDistance=1.66m (Z=-1.84).");
                }

                // Ensure BoxCollider for VR raycasting matches enlarged 4.42m x 2.49m panel size
                var col = uiManager.GetComponent<BoxCollider>();
                if (col == null)
                {
                    col = uiManager.AddComponent<BoxCollider>();
                }
                col.isTrigger = true;
                col.center = Vector3.zero;
                col.size = new Vector3(4.42f, 2.49f, 0.05f);
                EditorUtility.SetDirty(col);

                // Ensure FloatingUIRecallController references UIManager with 1.66m recall distance (Z=-1.84)
                var recallController = UnityEngine.Object.FindAnyObjectByType<FloatingUIRecallController>();
                if (recallController != null)
                {
                    recallController.recallDistance = 1.66f;
                    recallController.verticalOffset = -0.05f;
                    if (recallController.studioUIRoot == null)
                    {
                        recallController.studioUIRoot = uiManager;
                    }
                    EditorUtility.SetDirty(recallController);
                    Debug.Log("[UIDiagnosticFixer] Configured FloatingUIRecallController: recallDistance=1.66m (Z=-1.84).");
                }

                // Save scene if dirty
                if (currentScene.isDirty)
                {
                    EditorSceneManager.SaveScene(currentScene);
                    Debug.Log("[UIDiagnosticFixer] Saved modified scene.");
                }
            }
            else
            {
                Debug.LogWarning("[UIDiagnosticFixer] UIManager GameObject not found in active scene!");
            }

            AssetDatabase.SaveAssets();
            Debug.Log("<color=#00FF88><b>[UIDiagnosticFixer] Diagnosis & Repair complete!</b></color>");
        }

        private static void FixPanelSettingsAsset(string assetPath)
        {
            var panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(assetPath);
            if (panelSettings != null)
            {
                FixPanelSettings(panelSettings);
                EditorUtility.SetDirty(panelSettings);
                Debug.Log($"[UIDiagnosticFixer] Repaired PanelSettings asset at '{assetPath}'.");
            }
        }

        private static void FixPanelSettings(PanelSettings ps)
        {
            if (ps == null) return;

            // 1. Remove textSettings if it causes FontAsset null material crash
            ps.textSettings = null;

            // 2. Configure WorldSpace
            ps.renderMode = PanelRenderMode.WorldSpace;
            ps.scaleMode = PanelScaleMode.ConstantPixelSize;
            ps.referenceResolution = new Vector2Int(1920, 1080);
            ps.clearDepthStencil = true;

            // 3. SerializedObject properties for internal fields
            SerializedObject so = new SerializedObject(ps);
            var ppuProp = so.FindProperty("m_PixelsPerUnit");
            if (ppuProp != null) ppuProp.floatValue = 434f;
            var scaleProp = so.FindProperty("m_Scale");
            if (scaleProp != null) scaleProp.floatValue = 1.25f;
            var gammaProp = so.FindProperty("forceGammaRendering");
            if (gammaProp != null) gammaProp.boolValue = false;
            so.ApplyModifiedProperties();

            // 4. Dynamic atlas settings for crystal clear text and icon rasterization in VR
            var atlas = ps.dynamicAtlasSettings;
            atlas.minAtlasSize = 1024;
            atlas.maxAtlasSize = 4096;
            atlas.maxSubTextureSize = 2048;
            ps.dynamicAtlasSettings = atlas;
        }
    }
}

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

                // Check VRUIPositioner
                var positioner = uiManager.GetComponent<VRUIPositioner>();
                if (positioner != null)
                {
                    positioner.isHUDVisible = true;
                    if (currentScene.name.IndexOf("jarvis", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        positioner.stickyMode = VRUIPositioner.StickyMode.WorldAnchor;
                    }
                    EditorUtility.SetDirty(positioner);
                    Debug.Log($"[UIDiagnosticFixer] VRUIPositioner checked: isHUDVisible=true, stickyMode={positioner.stickyMode}.");
                }

                // Ensure BoxCollider for VR raycasting
                var col = uiManager.GetComponent<BoxCollider>();
                if (col == null)
                {
                    col = uiManager.AddComponent<BoxCollider>();
                }
                col.isTrigger = true;
                col.center = Vector3.zero;
                col.size = new Vector3(1.92f, 1.08f, 0.05f);
                EditorUtility.SetDirty(col);

                // Ensure FloatingUIRecallController references UIManager
                var recallController = UnityEngine.Object.FindAnyObjectByType<FloatingUIRecallController>();
                if (recallController != null)
                {
                    if (recallController.studioUIRoot == null)
                    {
                        recallController.studioUIRoot = uiManager;
                        EditorUtility.SetDirty(recallController);
                        Debug.Log("[UIDiagnosticFixer] Assigned studioUIRoot on FloatingUIRecallController to UIManager.");
                    }
                }

                // Ensure XR Device Simulator does not spawn huge world-space UI ribbon blocking the sky
                var sim = UnityEngine.Object.FindAnyObjectByType<UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation.XRDeviceSimulator>();
                if (sim != null)
                {
                    var simSo = new SerializedObject(sim);
                    var uiProp = simSo.FindProperty("m_DeviceSimulatorUI");
                    if (uiProp != null && uiProp.objectReferenceValue != null)
                    {
                        uiProp.objectReferenceValue = null;
                        simSo.ApplyModifiedProperties();
                        EditorUtility.SetDirty(sim);
                        Debug.Log("[UIDiagnosticFixer] Cleared m_DeviceSimulatorUI on XRDeviceSimulator.");
                    }
                    var simUIChild = sim.transform.Find("XR Device Simulator UI(Clone)");
                    if (simUIChild != null)
                    {
                        UnityEngine.Object.DestroyImmediate(simUIChild.gameObject);
                        Debug.Log("[UIDiagnosticFixer] Destroyed XR Device Simulator UI(Clone).");
                    }
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
            if (ppuProp != null) ppuProp.floatValue = 1000f;
            var gammaProp = so.FindProperty("forceGammaRendering");
            if (gammaProp != null) gammaProp.boolValue = false;
            so.ApplyModifiedProperties();

            // 4. Dynamic atlas settings
            var atlas = ps.dynamicAtlasSettings;
            atlas.minAtlasSize = 512;
            atlas.maxAtlasSize = 4096;
            atlas.maxSubTextureSize = 1024;
            ps.dynamicAtlasSettings = atlas;
        }
    }
}

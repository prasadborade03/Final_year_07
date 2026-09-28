using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;
using UnityEngine.XR.Interaction.Toolkit.Feedback;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;
using ProjectName.VR;
using ProjectName.UI;

namespace ProjectName.EditorScripts
{
    /// <summary>
    /// Automated setup script to configure the full VR Control Scheme in Test_TerrainAndRover.unity
    /// strictly according to the "VR Control Scheme — Rover Digital Twin Studio" specification:
    ///
    /// 1. Dual Controller Rays / Near-Far Interactors with curved line visual (§4.1).
    /// 2. Trigger = Select / Grab / Click (§4.1) for both UI and props.
    /// 3. B Button Studio UI Toggle & Recall-in-front via FloatingUIRecallController (§4.2).
    /// 4. Free-Fly Camera via FreeFlyRigController (Right Grip held + Right Thumbstick) (§4.3).
    /// 5. Grounded Locomotion via Teleportation Area on terrain and Snap Turn on XR Origin (§5).
    /// 6. World Space UI with Tracked Device Graphic Raycaster and XRUIInputModule.
    /// 7. XR Device Simulator enabled for instant editor testing.
    /// </summary>
    [InitializeOnLoad]
    public static class VRControlSchemeSetup
    {
        private const string ScenePath = "Assets/project/scenes/_Test/Test_TerrainAndRover.unity";
        private const string ActionsAssetPath = "Assets/Samples/XR Interaction Toolkit/3.5.1/Starter Assets/XRI Default Input Actions.inputactions";
        private const string LeftNearFarPrefabPath = "Assets/Samples/XR Interaction Toolkit/3.5.1/Starter Assets/Prefabs/Interactors/Left_NearFarInteractor.prefab";
        private const string RightNearFarPrefabPath = "Assets/Samples/XR Interaction Toolkit/3.5.1/Starter Assets/Prefabs/Interactors/Right_NearFarInteractor.prefab";
        private const string TeleportInteractorPrefabPath = "Assets/Samples/XR Interaction Toolkit/3.5.1/Starter Assets/Prefabs/Interactors/Teleport Interactor.prefab";
        private const string ReticleFbxPath = "Assets/Samples/XR Interaction Toolkit/3.5.1/Starter Assets/Models/Reticle_Torus.fbx";

        private const string SessionKey_Applied = "VRControlScheme_Applied_v1";

        static VRControlSchemeSetup()
        {
            // Auto-apply disabled to prevent accidental scene overrides on compile
            // EditorApplication.delayCall += AutoApplyOnce;
        }

        private static void AutoApplyOnce()
        {
            if (SessionState.GetBool(SessionKey_Applied, false))
            {
                return;
            }

            SessionState.SetBool(SessionKey_Applied, true);
            Debug.Log("[VRControlSchemeSetup] Running initial automated setup on compile...");
            ApplyToTargetScene(ScenePath);
        }

        [MenuItem("Tools/VR Project/Apply VR Control Scheme", false, 0)]
        public static void MenuApplyVRControlScheme()
        {
            ApplyToTargetScene(ScenePath);
        }

        public static void ApplyToTargetScene(string scenePath)
        {
            var currentScene = SceneManager.GetActiveScene();
            bool needToOpen = currentScene.path != scenePath;

            Scene sceneToConfig = currentScene;
            if (needToOpen)
            {
                if (currentScene.isDirty)
                {
                    EditorSceneManager.SaveScene(currentScene);
                }
                sceneToConfig = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            }

            Debug.Log($"<color=#00E5FF><b>[VRControlSchemeSetup]</b> Configuring VR Control Scheme for scene: {sceneToConfig.path}</color>");

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Apply Full VR Control Scheme");
            int undoGroup = Undo.GetCurrentGroup();

            try
            {
                var inputActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(ActionsAssetPath);
                if (inputActions == null)
                {
                    Debug.LogError($"[VRControlSchemeSetup] Input actions asset not found at {ActionsAssetPath}!");
                    return;
                }

                // 1. Locate XR Origin
                var xrOrigin = GameObject.Find("XR Origin (VR)");
                if (xrOrigin == null) xrOrigin = GameObject.Find("XR Origin");
                if (xrOrigin == null)
                {
                    var xo = UnityEngine.Object.FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>();
                    if (xo != null) xrOrigin = xo.gameObject;
                }

                if (xrOrigin == null)
                {
                    Debug.LogError("[VRControlSchemeSetup] XR Origin not found in scene!");
                    return;
                }

                var cameraOffset = xrOrigin.transform.Find("Camera Offset");
                if (cameraOffset == null)
                {
                    Debug.LogError("[VRControlSchemeSetup] 'Camera Offset' not found under XR Origin!");
                    return;
                }

                Camera mainCamera = cameraOffset.GetComponentInChildren<Camera>(true);
                if (mainCamera == null) mainCamera = Camera.main;

                // 2. Setup EventSystem with XRUIInputModule
                SetupEventSystem(inputActions, mainCamera);

                // 3. Setup Controllers (Near-Far with Curve Visual, Teleport Interactor on Left)
                SetupControllers(xrOrigin, cameraOffset, inputActions);

                // 4. Setup Grounded Locomotion (Snap Turn Provider + Teleportation Provider)
                var snapTurn = SetupLocomotion(xrOrigin, inputActions);

                // 5. Setup FreeFlyRigController (Right Grip held + Right Thumbstick)
                SetupFreeFlyRig(xrOrigin, mainCamera, snapTurn, inputActions);

                // 6. Setup Studio UI (FloatingUIRecallController on UIManager, non-sticky)
                SetupStudioUI(xrOrigin, mainCamera, inputActions);

                // 7. Setup Terrain Teleportation Area
                SetupTerrainTeleportation();

                // 8. Setup World Space Canvas (if present)
                SetupWorldSpaceCanvas(mainCamera);

                // 9. Enable XR Device Simulator for in-editor play
                SetupDeviceSimulator();

                // 10. Verify / Setup draggable test cube
                SetupTestCube();

                // Mark scene dirty and save
                EditorSceneManager.MarkSceneDirty(sceneToConfig);
                EditorSceneManager.SaveScene(sceneToConfig);
                AssetDatabase.SaveAssets();

                Debug.Log("<color=#00FF66><b>[VRControlSchemeSetup] SUCCESS: Full VR Control Scheme applied and scene saved!</b></color>");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[VRControlSchemeSetup] Exception during setup: {ex}");
            }
            finally
            {
                Undo.CollapseUndoOperations(undoGroup);
            }
        }

        private static void SetupEventSystem(InputActionAsset inputActions, Camera uiCamera)
        {
            var eventSystem = UnityEngine.Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>();
            GameObject esGO;
            if (eventSystem == null)
            {
                esGO = new GameObject("EventSystem");
                eventSystem = Undo.AddComponent<UnityEngine.EventSystems.EventSystem>(esGO);
            }
            else
            {
                esGO = eventSystem.gameObject;
            }

            var standalone = esGO.GetComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            if (standalone != null) Undo.DestroyObjectImmediate(standalone);

            var xrInputModule = esGO.GetComponent<XRUIInputModule>();
            if (xrInputModule == null) xrInputModule = Undo.AddComponent<XRUIInputModule>(esGO);

            xrInputModule.enableXRInput = true;
            xrInputModule.enableMouseInput = true;
            xrInputModule.bypassUIToolkitEvents = false;
            xrInputModule.enableBuiltinActionsAsFallback = true;
            if (uiCamera != null) xrInputModule.uiCamera = uiCamera;

            var so = new SerializedObject(xrInputModule);
            SetActionRef(so, "m_PointAction", FindActionReference(inputActions, "XRI UI", "Point"));
            SetActionRef(so, "m_LeftClickAction", FindActionReference(inputActions, "XRI UI", "Click"));
            SetActionRef(so, "m_MiddleClickAction", FindActionReference(inputActions, "XRI UI", "MiddleClick"));
            SetActionRef(so, "m_RightClickAction", FindActionReference(inputActions, "XRI UI", "RightClick"));
            SetActionRef(so, "m_ScrollWheelAction", FindActionReference(inputActions, "XRI UI", "ScrollWheel"));
            SetActionRef(so, "m_NavigateAction", FindActionReference(inputActions, "XRI UI", "Navigate"));
            SetActionRef(so, "m_SubmitAction", FindActionReference(inputActions, "XRI UI", "Submit"));
            SetActionRef(so, "m_CancelAction", FindActionReference(inputActions, "XRI UI", "Cancel"));
            so.ApplyModifiedProperties();

            Debug.Log("[VRControlSchemeSetup] Configured EventSystem with XRUIInputModule.");
        }

        private static void SetupControllers(GameObject xrOrigin, Transform cameraOffset, InputActionAsset inputActions)
        {
            var leftCtrl = cameraOffset.Find("Left Controller");
            var rightCtrl = cameraOffset.Find("Right Controller");

            var leftPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(LeftNearFarPrefabPath);
            var rightPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RightNearFarPrefabPath);
            var teleportPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(TeleportInteractorPrefabPath);

            if (leftCtrl != null && leftPrefab != null)
            {
                ConfigureControllerHand(leftCtrl.gameObject, isLeft: true, leftPrefab, teleportPrefab, inputActions);
            }

            if (rightCtrl != null && rightPrefab != null)
            {
                ConfigureControllerHand(rightCtrl.gameObject, isLeft: false, rightPrefab, null, inputActions);
            }
        }

        private static void ConfigureControllerHand(
            GameObject controllerGO,
            bool isLeft,
            GameObject nearFarPrefab,
            GameObject teleportPrefab,
            InputActionAsset inputActions)
        {
            string handPrefix = isLeft ? "Left" : "Right";
            string interactionMap = isLeft ? "XRI Left Interaction" : "XRI Right Interaction";
            string locomotionMap = isLeft ? "XRI Left Locomotion" : "XRI Right Locomotion";

            // 1. Remove old straight "Ray Interactor" if present
            var oldRay = controllerGO.transform.Find("Ray Interactor");
            if (oldRay != null)
            {
                Undo.DestroyObjectImmediate(oldRay.gameObject);
                Debug.Log($"[VRControlSchemeSetup] Removed old straight Ray Interactor from {handPrefix} Controller.");
            }

            // 2. Setup or preserve Near-Far Interactor
            NearFarInteractor nearFarComp = null;
            var existingNF = controllerGO.transform.Find("Near-Far Interactor");
            if (existingNF == null) existingNF = controllerGO.transform.Find(handPrefix + "_NearFarInteractor");

            if (existingNF == null && nearFarPrefab != null)
            {
                var instantiated = (GameObject)PrefabUtility.InstantiatePrefab(nearFarPrefab, controllerGO.transform);
                instantiated.name = "Near-Far Interactor";
                instantiated.transform.localPosition = Vector3.zero;
                instantiated.transform.localRotation = Quaternion.identity;
                instantiated.transform.localScale = Vector3.one;
                Undo.RegisterCreatedObjectUndo(instantiated, $"Instantiate {handPrefix} Near-Far Interactor");
                existingNF = instantiated.transform;
            }

            if (existingNF != null)
            {
                nearFarComp = existingNF.GetComponent<NearFarInteractor>();
                if (nearFarComp != null)
                {
                    // Map Select and UI Press to Trigger (§4.1: pull trigger to select/grab/click)
                    var nfSO = new SerializedObject(nearFarComp);
                    var triggerAction = FindActionReference(inputActions, interactionMap, "Activate");
                    var uiPressAction = FindActionReference(inputActions, interactionMap, "UI Press");

                    if (triggerAction != null)
                    {
                        SetActionRefPerformed(nfSO, "m_SelectInput", triggerAction);
                    }
                    if (uiPressAction != null)
                    {
                        SetActionRefPerformed(nfSO, "m_UIPressInput", uiPressAction);
                    }
                    nfSO.ApplyModifiedProperties();
                }
            }

            // 3. Setup Teleport Interactor on Left Controller
            XRRayInteractor teleportRayComp = null;
            if (isLeft && teleportPrefab != null)
            {
                var existingTeleport = controllerGO.transform.Find("Teleport Interactor");
                if (existingTeleport == null)
                {
                    var instantiatedTeleport = (GameObject)PrefabUtility.InstantiatePrefab(teleportPrefab, controllerGO.transform);
                    instantiatedTeleport.name = "Teleport Interactor";
                    instantiatedTeleport.transform.localPosition = Vector3.zero;
                    instantiatedTeleport.transform.localRotation = Quaternion.identity;
                    instantiatedTeleport.transform.localScale = Vector3.one;
                    instantiatedTeleport.SetActive(false); // Inactive until teleport mode is engaged
                    Undo.RegisterCreatedObjectUndo(instantiatedTeleport, "Instantiate Left Teleport Interactor");
                    existingTeleport = instantiatedTeleport.transform;
                }

                if (existingTeleport != null)
                {
                    teleportRayComp = existingTeleport.GetComponent<XRRayInteractor>();
                }
            }

            // 4. Configure ControllerInputActionManager
            var ciam = controllerGO.GetComponent<ControllerInputActionManager>();
            if (ciam == null) ciam = Undo.AddComponent<ControllerInputActionManager>(controllerGO);

            var ciamSO = new SerializedObject(ciam);
            var nfProp = ciamSO.FindProperty("m_NearFarInteractor");
            if (nfProp != null && nearFarComp != null) nfProp.objectReferenceValue = nearFarComp;

            var teleProp = ciamSO.FindProperty("m_TeleportInteractor");
            if (teleProp != null) teleProp.objectReferenceValue = teleportRayComp;

            SetActionRef(ciamSO, "m_TeleportMode", FindActionReference(inputActions, locomotionMap, "Teleport Mode"));
            SetActionRef(ciamSO, "m_TeleportModeCancel", FindActionReference(inputActions, locomotionMap, "Teleport Mode Cancel"));
            SetActionRef(ciamSO, "m_Move", FindActionReference(inputActions, locomotionMap, "Move"));
            SetActionRef(ciamSO, "m_SnapTurn", FindActionReference(inputActions, locomotionMap, "Snap Turn"));

            var smoothMotionProp = ciamSO.FindProperty("m_SmoothMotionEnabled");
            if (smoothMotionProp != null) smoothMotionProp.boolValue = false; // Teleport mode

            var smoothTurnProp = ciamSO.FindProperty("m_SmoothTurnEnabled");
            if (smoothTurnProp != null) smoothTurnProp.boolValue = false; // Snap turn mode

            ciamSO.ApplyModifiedProperties();

            // 5. Setup XRInteractionGroup
            var group = controllerGO.GetComponent<XRInteractionGroup>();
            if (group == null) group = Undo.AddComponent<XRInteractionGroup>(controllerGO);

            Debug.Log($"[VRControlSchemeSetup] Successfully configured {handPrefix} Controller with Near-Far Curved Interactor and CIAM.");
        }

        private static SnapTurnProvider SetupLocomotion(GameObject xrOrigin, InputActionAsset inputActions)
        {
            // TeleportationProvider
            var teleProvider = xrOrigin.GetComponent<TeleportationProvider>();
            if (teleProvider == null) teleProvider = Undo.AddComponent<TeleportationProvider>(xrOrigin);

            // SnapTurnProvider
            var snapTurn = xrOrigin.GetComponent<SnapTurnProvider>();
            if (snapTurn == null) snapTurn = Undo.AddComponent<SnapTurnProvider>(xrOrigin);

            snapTurn.turnAmount = 45f;
            snapTurn.debounceTime = 0.5f;

            var snapSO = new SerializedObject(snapTurn);
            var snapTurnAction = FindActionReference(inputActions, "XRI Right Locomotion", "Snap Turn");

            var rightTurn = snapSO.FindProperty("m_RightHandTurnInput");
            if (rightTurn != null)
            {
                var mode = rightTurn.FindPropertyRelative("m_InputSourceMode");
                if (mode != null) mode.enumValueIndex = 2; // InputActionReference
                var iar = rightTurn.FindPropertyRelative("m_InputActionReference");
                if (iar != null) iar.objectReferenceValue = snapTurnAction;
            }

            snapSO.ApplyModifiedProperties();

            Debug.Log("[VRControlSchemeSetup] Configured SnapTurnProvider (45 deg) and TeleportationProvider on XR Origin.");
            return snapTurn;
        }

        private static void SetupFreeFlyRig(GameObject xrOrigin, Camera mainCamera, SnapTurnProvider snapTurn, InputActionAsset inputActions)
        {
            var freeFly = xrOrigin.GetComponent<FreeFlyRigController>();
            if (freeFly == null) freeFly = Undo.AddComponent<FreeFlyRigController>(xrOrigin);

            freeFly.xrOriginTransform = xrOrigin.transform;
            freeFly.xrCamera = mainCamera;
            freeFly.snapTurnProvider = snapTurn;
            freeFly.flySpeed = 6.0f;
            freeFly.fastFlySpeed = 14.0f;
            freeFly.acceleration = 8.0f;
            freeFly.deceleration = 10.0f;
            freeFly.gripThreshold = 0.5f;

            var ffSO = new SerializedObject(freeFly);

            // Right Grip action (hold modifier §4.3)
            var gripRef = FindActionReference(inputActions, "XRI Right Interaction", "Select")
                       ?? FindActionReference(inputActions, "XRI Right", "Grip Position");
            SetActionRef(ffSO, "rightGripAction", gripRef);

            // Right Thumbstick action (flight direction §4.3)
            var stickRef = FindActionReference(inputActions, "XRI Right", "Thumbstick")
                        ?? FindActionReference(inputActions, "XRI Right Locomotion", "Turn");
            SetActionRef(ffSO, "rightThumbstickAction", stickRef);

            ffSO.ApplyModifiedProperties();

            Debug.Log("[VRControlSchemeSetup] Attached and configured FreeFlyRigController (Right Grip held + Right Thumbstick) on XR Origin.");
        }

        private static void SetupStudioUI(GameObject xrOrigin, Camera mainCamera, InputActionAsset inputActions)
        {
            var uiManager = GameObject.Find("UIManager");
            if (uiManager == null)
            {
                var doc = UnityEngine.Object.FindAnyObjectByType<UIDocument>();
                if (doc != null) uiManager = doc.gameObject;
            }

            if (uiManager == null)
            {
                Debug.LogWarning("[VRControlSchemeSetup] UIManager not found in scene.");
                return;
            }

            // Ensure VRUIPositioner is in WorldAnchor mode (NOT HeadLocked / sticky)
            var positioner = uiManager.GetComponent<VRUIPositioner>();
            if (positioner != null)
            {
                positioner.stickyMode = VRUIPositioner.StickyMode.WorldAnchor;
                if (uiManager.transform.parent != null && uiManager.transform.parent.GetComponent<Camera>() != null)
                {
                    uiManager.transform.SetParent(null, true);
                }
            }

            // Ensure BoxCollider is present
            var boxCol = uiManager.GetComponent<BoxCollider>();
            if (boxCol == null) boxCol = Undo.AddComponent<BoxCollider>(uiManager);
            boxCol.isTrigger = true;
            boxCol.center = Vector3.zero;
            boxCol.size = new Vector3(1.92f, 1.08f, 0.05f);

            // Attach FloatingUIRecallController
            var recallCtrl = xrOrigin.GetComponent<FloatingUIRecallController>();
            if (recallCtrl == null) recallCtrl = Undo.AddComponent<FloatingUIRecallController>(xrOrigin);

            recallCtrl.studioUIRoot = uiManager;
            recallCtrl.xrCamera = mainCamera;
            recallCtrl.recallDistance = 1.35f;
            recallCtrl.verticalOffset = -0.1f;
            recallCtrl.keyboardShortcut = KeyCode.B;
            recallCtrl.secondaryShortcut = KeyCode.U;

            var recallSO = new SerializedObject(recallCtrl);
            var toggleProp = recallSO.FindProperty("toggleAction");
            if (toggleProp != null)
            {
                var use = toggleProp.FindPropertyRelative("m_UseReference");
                if (use != null) use.boolValue = false;
                var action = toggleProp.FindPropertyRelative("m_Action");
                if (action != null)
                {
                    var nameProp = action.FindPropertyRelative("m_Name");
                    if (nameProp != null) nameProp.stringValue = "Toggle Studio UI";
                    var typeProp = action.FindPropertyRelative("m_Type");
                    if (typeProp != null) typeProp.intValue = 1; // Button
                    var bindings = action.FindPropertyRelative("m_SingletonActionBindings");
                    if (bindings != null && bindings.arraySize == 0)
                    {
                        bindings.InsertArrayElementAtIndex(0);
                        var b = bindings.GetArrayElementAtIndex(0);
                        var path = b.FindPropertyRelative("m_Path");
                        if (path != null) path.stringValue = "<XRController>{RightHand}/secondaryButton";
                    }
                }
            }
            recallSO.ApplyModifiedProperties();

            Debug.Log("[VRControlSchemeSetup] Configured FloatingUIRecallController (B button recall-to-gaze, WorldAnchor non-sticky).");
        }

        private static void SetupTerrainTeleportation()
        {
            var terrainGO = GameObject.Find("GeneratedPlanetaryTerrain");
            if (terrainGO == null)
            {
                var terrain = UnityEngine.Object.FindAnyObjectByType<UnityEngine.Terrain>();
                if (terrain != null) terrainGO = terrain.gameObject;
            }

            if (terrainGO != null)
            {
                var col = terrainGO.GetComponent<TerrainCollider>();
                if (col == null) col = Undo.AddComponent<TerrainCollider>(terrainGO);
                var tComp = terrainGO.GetComponent<UnityEngine.Terrain>();
                if (tComp != null) col.terrainData = tComp.terrainData;

                var teleArea = terrainGO.GetComponent<TeleportationArea>();
                if (teleArea == null) teleArea = Undo.AddComponent<TeleportationArea>(terrainGO);

                Debug.Log("[VRControlSchemeSetup] Attached TeleportationArea and TerrainCollider to GeneratedPlanetaryTerrain.");
            }
            else
            {
                Debug.LogWarning("[VRControlSchemeSetup] GeneratedPlanetaryTerrain not found in scene.");
            }
        }

        private static void SetupWorldSpaceCanvas(Camera mainCamera)
        {
            var canvases = Resources.FindObjectsOfTypeAll<Canvas>();
            foreach (var canvas in canvases)
            {
                if (canvas.gameObject.scene.name == SceneManager.GetActiveScene().name)
                {
                    canvas.renderMode = RenderMode.WorldSpace;
                    if (mainCamera != null) canvas.worldCamera = mainCamera;

                    var oldRaycaster = canvas.GetComponent<UnityEngine.UI.GraphicRaycaster>();
                    if (oldRaycaster != null)
                    {
                        Undo.DestroyObjectImmediate(oldRaycaster);
                    }

                    var trackedRaycaster = canvas.GetComponent<TrackedDeviceGraphicRaycaster>();
                    if (trackedRaycaster == null)
                    {
                        Undo.AddComponent<TrackedDeviceGraphicRaycaster>(canvas.gameObject);
                    }

                    Debug.Log($"[VRControlSchemeSetup] Configured Canvas '{canvas.name}' as WorldSpace with TrackedDeviceGraphicRaycaster.");
                }
            }
        }

        private static void SetupDeviceSimulator()
        {
            var sim = Resources.FindObjectsOfTypeAll<UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation.XRDeviceSimulator>();
            if (sim.Length > 0)
            {
                sim[0].gameObject.SetActive(true);
                Debug.Log("[VRControlSchemeSetup] Activated XR Device Simulator in scene.");
            }
        }

        private static void SetupTestCube()
        {
            var cube = GameObject.Find("Cube");
            if (cube != null)
            {
                var grab = cube.GetComponent<XRGrabInteractable>();
                if (grab == null) grab = Undo.AddComponent<XRGrabInteractable>(cube);

                var rb = cube.GetComponent<Rigidbody>();
                if (rb == null) rb = Undo.AddComponent<Rigidbody>(cube);

                var col = cube.GetComponent<BoxCollider>();
                if (col == null) col = Undo.AddComponent<BoxCollider>(cube);

                Debug.Log("[VRControlSchemeSetup] Verified throwaway test Cube with XRGrabInteractable for drag-and-drop verification.");
            }
        }

        private static void SetActionRef(SerializedObject so, string propertyName, InputActionReference actionRef)
        {
            if (actionRef == null) return;
            var prop = so.FindProperty(propertyName);
            if (prop != null)
            {
                if (prop.propertyType == SerializedPropertyType.ObjectReference)
                {
                    prop.objectReferenceValue = actionRef;
                }
                else
                {
                    var refProp = prop.FindPropertyRelative("m_Reference") ?? prop.FindPropertyRelative("m_InputActionReference");
                    if (refProp != null)
                    {
                        var useProp = prop.FindPropertyRelative("m_UseReference");
                        if (useProp != null) useProp.boolValue = true;
                        refProp.objectReferenceValue = actionRef;
                    }
                }
            }
        }

        private static void SetActionRefPerformed(SerializedObject so, string propertyName, InputActionReference actionRef)
        {
            if (actionRef == null) return;
            var prop = so.FindProperty(propertyName);
            if (prop != null)
            {
                var perfProp = prop.FindPropertyRelative("m_InputActionReferencePerformed");
                if (perfProp != null)
                {
                    perfProp.objectReferenceValue = actionRef;
                }
                else
                {
                    SetActionRef(so, propertyName, actionRef);
                }
            }
        }

        private static InputActionReference FindActionReference(InputActionAsset asset, string mapName, string actionName)
        {
            string fullPath = AssetDatabase.GetAssetPath(asset);
            var subAssets = AssetDatabase.LoadAllAssetsAtPath(fullPath);
            foreach (var a in subAssets)
            {
                if (a is InputActionReference iar && iar.action != null)
                {
                    if (iar.action.actionMap != null && iar.action.actionMap.name == mapName && iar.action.name == actionName)
                    {
                        return iar;
                    }
                    if (iar.name == (mapName + "/" + actionName))
                    {
                        return iar;
                    }
                }
            }
            return null;
        }
    }
}

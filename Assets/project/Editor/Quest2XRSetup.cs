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
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;
using ProjectName.UI;

namespace ProjectName.EditorScripts
{
    /// <summary>
    /// Automated setup script to configure Meta Quest 2 Touch Controllers,
    /// dual straight-line laser pointers with hit reticles, UI Toolkit world space XRI support,
    /// EventSystem XRUIInputModule, and sticky Iron-Man HUD.
    /// </summary>
    public static class Quest2XRSetup
    {
        private const string ActionsAssetPath = "Assets/Samples/XR Interaction Toolkit/3.5.1/Starter Assets/XRI Default Input Actions.inputactions";
        private const string LeftControllerPrefabPath = "Assets/Samples/XR Interaction Toolkit/3.5.1/Starter Assets/Prefabs/Controllers/XR Controller Left.prefab";
        private const string RightControllerPrefabPath = "Assets/Samples/XR Interaction Toolkit/3.5.1/Starter Assets/Prefabs/Controllers/XR Controller Right.prefab";
        private const string ReticleFbxPath = "Assets/Samples/XR Interaction Toolkit/3.5.1/Starter Assets/Models/Reticle_Torus.fbx";

        [MenuItem("Tools/VR Project/Configure Quest 2 Controllers and Sticky HUD", false, 1)]
        public static void ConfigureActiveScene()
        {
            var activeScene = SceneManager.GetActiveScene();
            Debug.Log($"<color=#00E5FF><b>[Quest2XRSetup]</b> Starting configuration for active scene: {activeScene.name}</color>");

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Configure Quest 2 Controllers and HUD");
            int group = Undo.GetCurrentGroup();

            try
            {
                var inputActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(ActionsAssetPath);
                if (inputActions == null)
                {
                    Debug.LogError($"[Quest2XRSetup] Could not find input actions at {ActionsAssetPath}!");
                    return;
                }

                var leftControllerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(LeftControllerPrefabPath);
                var rightControllerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RightControllerPrefabPath);
                var reticlePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ReticleFbxPath);

                // 1. Setup XR UI Toolkit Manager & PanelInputConfiguration
                SetupXRUIToolkitInfrastructure();

                // 2. Setup EventSystem with XRUIInputModule
                SetupEventSystem(inputActions);

                // 3. Setup Controllers and Laser Pointers on XR Origin
                SetupXROriginControllers(inputActions, leftControllerPrefab, rightControllerPrefab, reticlePrefab);

                // 4. Setup UIManager (UIDocument, VRUIPositioner, Collider)
                SetupUIManager();

                // Mark scene dirty and save
                EditorSceneManager.MarkSceneDirty(activeScene);
                EditorSceneManager.SaveScene(activeScene);

                Debug.Log("<color=#00FF66><b>[Quest2XRSetup] Successfully configured Quest 2 Controllers and Sticky HUD! Scene saved.</b></color>");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Quest2XRSetup] Exception during setup: {ex}");
            }
            finally
            {
                Undo.CollapseUndoOperations(group);
            }
        }

        private static void SetupXRUIToolkitInfrastructure()
        {
            var existingManager = UnityEngine.Object.FindAnyObjectByType<XRUIToolkitManager>();
            GameObject managerGO;
            if (existingManager != null)
            {
                managerGO = existingManager.gameObject;
            }
            else
            {
                managerGO = GameObject.Find("XR UI Toolkit Manager");
                if (managerGO == null)
                {
                    managerGO = new GameObject("XR UI Toolkit Manager");
                    Undo.RegisterCreatedObjectUndo(managerGO, "Create XR UI Toolkit Manager");
                }
                Undo.AddComponent<XRUIToolkitManager>(managerGO);
            }

            // PanelInputConfiguration
            var pic = managerGO.GetComponent<PanelInputConfiguration>();
            if (pic == null)
            {
                pic = Undo.AddComponent<PanelInputConfiguration>(managerGO);
            }

            pic.panelInputRedirection = PanelInputConfiguration.PanelInputRedirection.Never;
            pic.processWorldSpaceInput = true;
            pic.defaultEventCameraIsMainCamera = true;
            pic.maxInteractionDistance = 15f;
            Debug.Log("[Quest2XRSetup] Configured XRUIToolkitManager and PanelInputConfiguration.");
        }

        private static void SetupEventSystem(InputActionAsset inputActions)
        {
            var eventSystem = UnityEngine.Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>();
            if (eventSystem == null)
            {
                var esGO = new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem));
                Undo.RegisterCreatedObjectUndo(esGO, "Create EventSystem");
                eventSystem = esGO.GetComponent<UnityEngine.EventSystems.EventSystem>();
            }

            // Remove InputSystemUIInputModule if present to avoid conflicting with XRUIInputModule
            var standardModule = eventSystem.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            if (standardModule != null)
            {
                Undo.DestroyObjectImmediate(standardModule);
                Debug.Log("[Quest2XRSetup] Removed conflicting InputSystemUIInputModule from EventSystem.");
            }

            // Ensure XRUIInputModule is present
            var xrInputModule = eventSystem.GetComponent<XRUIInputModule>();
            if (xrInputModule == null)
            {
                xrInputModule = Undo.AddComponent<XRUIInputModule>(eventSystem.gameObject);
            }

            xrInputModule.bypassUIToolkitEvents = false;
            xrInputModule.enableBuiltinActionsAsFallback = true;

            var mainCam = Camera.main;
            if (mainCam != null) xrInputModule.uiCamera = mainCam;

            // Wire XRI UI actions
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

            Debug.Log("[Quest2XRSetup] Configured XRUIInputModule on EventSystem.");
        }

        private static void SetupXROriginControllers(
            InputActionAsset inputActions,
            GameObject leftControllerPrefab,
            GameObject rightControllerPrefab,
            GameObject reticlePrefab)
        {
            var xrOrigin = GameObject.Find("XR Origin (VR)");
            if (xrOrigin == null) xrOrigin = GameObject.Find("XR Origin");
            if (xrOrigin == null)
            {
                var xo = UnityEngine.Object.FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>();
                if (xo != null) xrOrigin = xo.gameObject;
            }

            if (xrOrigin == null)
            {
                Debug.LogError("[Quest2XRSetup] XR Origin GameObject not found in scene!");
                return;
            }

            var cameraOffset = xrOrigin.transform.Find("Camera Offset");
            if (cameraOffset == null)
            {
                Debug.LogError("[Quest2XRSetup] 'Camera Offset' child not found under XR Origin!");
                return;
            }

            // Left Controller
            var leftController = cameraOffset.Find("Left Controller");
            if (leftController != null)
            {
                ConfigureSingleController(
                    leftController.gameObject,
                    isLeft: true,
                    inputActions,
                    leftControllerPrefab,
                    reticlePrefab);
            }
            else
            {
                Debug.LogWarning("[Quest2XRSetup] Left Controller not found under Camera Offset!");
            }

            // Right Controller
            var rightController = cameraOffset.Find("Right Controller");
            if (rightController != null)
            {
                ConfigureSingleController(
                    rightController.gameObject,
                    isLeft: false,
                    inputActions,
                    rightControllerPrefab,
                    reticlePrefab);
            }
            else
            {
                Debug.LogWarning("[Quest2XRSetup] Right Controller not found under Camera Offset!");
            }
        }

        private static void ConfigureSingleController(
            GameObject controllerGO,
            bool isLeft,
            InputActionAsset inputActions,
            GameObject controllerModelPrefab,
            GameObject reticlePrefab)
        {
            string handPrefix = isLeft ? "Left" : "Right";
            string interactionMap = isLeft ? "XRI Left Interaction" : "XRI Right Interaction";
            string controllerMap = isLeft ? "XRI Left" : "XRI Right";

            // 1. Remove old robotic hands if present
            string oldHandName = isLeft ? "LeftHandPrefab" : "RightHandPrefab";
            var oldHand = controllerGO.transform.Find(oldHandName);
            if (oldHand != null)
            {
                Undo.DestroyObjectImmediate(oldHand.gameObject);
                Debug.Log($"[Quest2XRSetup] Removed old robotic hand '{oldHandName}' from {handPrefix} Controller.");
            }

            // 2. Remove any bare NearFarInteractor attached directly to controller root
            var bareNF = controllerGO.GetComponent<NearFarInteractor>();
            if (bareNF != null)
            {
                Undo.DestroyObjectImmediate(bareNF);
                Debug.Log($"[Quest2XRSetup] Removed bare NearFarInteractor from {handPrefix} Controller root.");
            }

            // 3. Attach Touch Controller Model
            string visualName = isLeft ? "Left Controller Visual" : "Right Controller Visual";
            var visualChild = controllerGO.transform.Find(visualName);
            if (visualChild == null && controllerModelPrefab != null)
            {
                visualChild = ((GameObject)PrefabUtility.InstantiatePrefab(controllerModelPrefab, controllerGO.transform)).transform;
                visualChild.name = visualName;
                visualChild.localPosition = new Vector3(0f, 0f, -0.05f);
                visualChild.localRotation = Quaternion.Euler(0f, 180f, 0f);
                visualChild.localScale = Vector3.one;
                Undo.RegisterCreatedObjectUndo(visualChild.gameObject, $"Instantiate {visualName}");
                Debug.Log($"[Quest2XRSetup] Attached Oculus Touch {visualName} model.");
            }

            // 4. Attach / Configure Ray Interactor
            var rayChild = controllerGO.transform.Find("Ray Interactor");
            if (rayChild == null)
            {
                var rayGO = new GameObject("Ray Interactor");
                rayGO.transform.SetParent(controllerGO.transform, false);
                rayGO.transform.localPosition = Vector3.zero;
                rayGO.transform.localRotation = Quaternion.identity;
                rayGO.transform.localScale = Vector3.one;
                Undo.RegisterCreatedObjectUndo(rayGO, $"Create {handPrefix} Ray Interactor");
                rayChild = rayGO.transform;
            }

            // XRRayInteractor
            var ray = rayChild.GetComponent<XRRayInteractor>();
            if (ray == null) ray = Undo.AddComponent<XRRayInteractor>(rayChild.gameObject);

            ray.lineType = XRRayInteractor.LineType.StraightLine;
            ray.maxRaycastDistance = 15f;
            ray.enableUIInteraction = true;
            ray.raycastUIDocumentTriggerInteraction = QueryUIDocumentInteraction.Collide;
            ray.hitDetectionType = XRRayInteractor.HitDetectionType.Raycast;

            // Wire input actions on XRRayInteractor
            var raySO = new SerializedObject(ray);
            SetActionRefPerformed(raySO, "m_SelectInput", FindActionReference(inputActions, interactionMap, "Select"));
            SetActionRefPerformed(raySO, "m_ActivateInput", FindActionReference(inputActions, interactionMap, "Activate"));
            SetActionRefPerformed(raySO, "m_UIPressInput", FindActionReference(inputActions, interactionMap, "UI Press"));
            SetActionRef(raySO, "m_UIScrollInput", FindActionReference(inputActions, interactionMap, "UI Scroll"));
            raySO.ApplyModifiedProperties();

            // LineRenderer
            var lr = rayChild.GetComponent<LineRenderer>();
            if (lr == null) lr = Undo.AddComponent<LineRenderer>(rayChild.gameObject);
            lr.startWidth = 0.006f;
            lr.endWidth = 0.006f;
            lr.useWorldSpace = true;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;

            var defaultLineMat = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Line.mat");
            if (defaultLineMat != null) lr.sharedMaterial = defaultLineMat;

            // XRInteractorLineVisual
            var lineVisual = rayChild.GetComponent<XRInteractorLineVisual>();
            if (lineVisual == null) lineVisual = Undo.AddComponent<XRInteractorLineVisual>(rayChild.gameObject);

            lineVisual.lineLength = 15f;
            lineVisual.stopLineAtFirstRaycastHit = true;

            // Gradients:
            // Valid (UI Hover): Vibrant Cyan
            lineVisual.validColorGradient = CreateGradient(
                new Color(0.0f, 0.90f, 1.0f, 1.0f),
                new Color(0.0f, 0.90f, 1.0f, 0.85f),
                1.0f, 0.85f);

            // Invalid (Idle): Soft Cyan fading to transparent at the end
            lineVisual.invalidColorGradient = CreateGradient(
                new Color(0.5f, 0.85f, 1.0f, 0.45f),
                new Color(0.0f, 0.70f, 1.0f, 0.0f),
                0.45f, 0.0f);

            // Reticle
            if (reticlePrefab != null)
            {
                lineVisual.reticle = reticlePrefab;
            }

            // Haptics: HapticImpulsePlayer + SimpleHapticFeedback
            var hapticPlayer = controllerGO.GetComponent<HapticImpulsePlayer>();
            if (hapticPlayer == null) hapticPlayer = Undo.AddComponent<HapticImpulsePlayer>(controllerGO);

            var hpSO = new SerializedObject(hapticPlayer);
            var hapticAction = FindActionReference(inputActions, controllerMap, "Haptic Device");
            var hapticOutputProp = hpSO.FindProperty("m_HapticOutput");
            if (hapticOutputProp != null)
            {
                var iarProp = hapticOutputProp.FindPropertyRelative("m_InputActionReference");
                if (iarProp != null) iarProp.objectReferenceValue = hapticAction;
            }
            hpSO.ApplyModifiedProperties();

            var simpleHaptic = rayChild.GetComponent<SimpleHapticFeedback>();
            if (simpleHaptic == null) simpleHaptic = Undo.AddComponent<SimpleHapticFeedback>(rayChild.gameObject);

            var shSO = new SerializedObject(simpleHaptic);
            var impulsePlayerProp = shSO.FindProperty("m_HapticImpulsePlayer");
            if (impulsePlayerProp != null) impulsePlayerProp.objectReferenceValue = hapticPlayer;

            var playHoverProp = shSO.FindProperty("m_PlayHoverEntered");
            if (playHoverProp != null) playHoverProp.boolValue = true;
            var hoverDataProp = shSO.FindProperty("m_HoverEnteredData");
            if (hoverDataProp != null)
            {
                var ampProp = hoverDataProp.FindPropertyRelative("m_Amplitude");
                var durProp = hoverDataProp.FindPropertyRelative("m_Duration");
                if (ampProp != null) ampProp.floatValue = 0.15f;
                if (durProp != null) durProp.floatValue = 0.04f;
            }

            var playSelectProp = shSO.FindProperty("m_PlaySelectEntered");
            if (playSelectProp != null) playSelectProp.boolValue = true;
            var selectDataProp = shSO.FindProperty("m_SelectEnteredData");
            if (selectDataProp != null)
            {
                var ampProp = selectDataProp.FindPropertyRelative("m_Amplitude");
                var durProp = selectDataProp.FindPropertyRelative("m_Duration");
                if (ampProp != null) ampProp.floatValue = 0.45f;
                if (durProp != null) durProp.floatValue = 0.08f;
            }
            shSO.ApplyModifiedProperties();

            Debug.Log($"[Quest2XRSetup] Successfully configured {handPrefix} Controller with Touch model, Straight Laser Ray, Reticle, and Haptics.");
        }

        private static void SetupUIManager()
        {
            var uiManager = GameObject.Find("UIManager");
            if (uiManager == null)
            {
                var doc = UnityEngine.Object.FindAnyObjectByType<UIDocument>();
                if (doc != null) uiManager = doc.gameObject;
            }

            if (uiManager != null)
            {
                var positioner = uiManager.GetComponent<VRUIPositioner>();
                if (positioner == null) positioner = Undo.AddComponent<VRUIPositioner>(uiManager);

                positioner.forwardDistance = 1.15f;
                positioner.heightOffset = -0.10f;
                positioner.stickyMode = VRUIPositioner.StickyMode.HeadLocked;

                var boxCol = uiManager.GetComponent<BoxCollider>();
                if (boxCol == null) boxCol = Undo.AddComponent<BoxCollider>(uiManager);
                boxCol.isTrigger = true;
                boxCol.center = Vector3.zero;
                boxCol.size = new Vector3(1.92f, 1.08f, 0.05f);

                Debug.Log("[Quest2XRSetup] Configured UIManager with VRUIPositioner (1.15m forward) and BoxCollider.");
            }
            else
            {
                Debug.LogWarning("[Quest2XRSetup] UIManager / UIDocument not found in active scene!");
            }
        }

        private static Gradient CreateGradient(Color startCol, Color endCol, float startAlpha, float endAlpha)
        {
            var grad = new Gradient();
            grad.SetKeys(
                new GradientColorKey[] { new GradientColorKey(startCol, 0f), new GradientColorKey(endCol, 1f) },
                new GradientAlphaKey[] { new GradientAlphaKey(startAlpha, 0f), new GradientAlphaKey(endAlpha, 1f) }
            );
            return grad;
        }

        private static void SetActionRef(SerializedObject so, string propertyName, InputActionReference actionRef)
        {
            var prop = so.FindProperty(propertyName);
            if (prop != null)
            {
                var refProp = prop.FindPropertyRelative("m_InputActionReference");
                if (refProp != null)
                {
                    refProp.objectReferenceValue = actionRef;
                }
                else
                {
                    prop.objectReferenceValue = actionRef;
                }
            }
        }

        private static void SetActionRefPerformed(SerializedObject so, string propertyName, InputActionReference actionRef)
        {
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

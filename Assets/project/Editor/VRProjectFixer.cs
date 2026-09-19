using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public static class VRProjectFixer
{
    [MenuItem("Tools/VR Project/Run Full VR Fixer")]
    public static void FixAllVRProjectIssues()
    {
        Debug.Log("[VRProjectFixer] Starting full VR project fix procedure...");

        FixHandMaterials();
        FixHandPrefabs();
        FixSceneLocomotionAndInteractables();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[VRProjectFixer] Full VR project fix completed successfully!");
    }

    public static void FixHandMaterials()
    {
        string matPath = "Assets/Materials/VR Hands/HandMaterial.mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat != null)
        {
            Shader targetShader = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null ? Shader.Find("Universal Render Pipeline/Lit") : Shader.Find("Standard");
            if (targetShader != null)
            {
                mat.shader = targetShader;
                EditorUtility.SetDirty(mat);
                Debug.Log("[VRProjectFixer] Successfully converted HandMaterial.mat shader to: " + targetShader.name);
            }
            else
            {
                Debug.LogError("[VRProjectFixer] Could not find appropriate target shader for HandMaterial.");
            }
        }
        else
        {
            Debug.LogWarning("[VRProjectFixer] HandMaterial.mat not found at path: " + matPath);
        }
    }

    public static void FixHandPrefabs()
    {
        string inputActionsPath = "Assets/Samples/XR Interaction Toolkit/3.5.1/Starter Assets/XRI Default Input Actions.inputactions";
        Object[] allSubAssets = AssetDatabase.LoadAllAssetsAtPath(inputActionsPath);

        InputActionReference leftGrip = FindActionRef(allSubAssets, "XRI Left Interaction", "Select Value") ?? FindActionRef(allSubAssets, "XRI Left", "Select Value");
        InputActionReference leftTrigger = FindActionRef(allSubAssets, "XRI Left Interaction", "Activate Value") ?? FindActionRef(allSubAssets, "XRI Left", "Activate Value");
        
        InputActionReference rightGrip = FindActionRef(allSubAssets, "XRI Right Interaction", "Select Value") ?? FindActionRef(allSubAssets, "XRI Right", "Select Value");
        InputActionReference rightTrigger = FindActionRef(allSubAssets, "XRI Right Interaction", "Activate Value") ?? FindActionRef(allSubAssets, "XRI Right", "Activate Value");

        FixSingleHandPrefab("Assets/Prefabs/VR Hands/LeftHandPrefab.prefab", "Assets/Animation/Hand Animation/Anim controllers/LeftHandAnimController.controller", leftGrip, leftTrigger);
        FixSingleHandPrefab("Assets/Prefabs/VR Hands/RightHandPrefab.prefab", "Assets/Animation/Hand Animation/Anim controllers/RightHandAnimController 1.controller", rightGrip, rightTrigger);
    }

    private static InputActionReference FindActionRef(Object[] subAssets, string mapName, string actionName)
    {
        foreach (var sub in subAssets)
        {
            if (sub is InputActionReference inputRef && inputRef.action != null)
            {
                if (inputRef.action.name == actionName && inputRef.action.actionMap != null && inputRef.action.actionMap.name == mapName)
                {
                    return inputRef;
                }
            }
        }
        return null;
    }

    private static void FixSingleHandPrefab(string prefabPath, string controllerPath, InputActionReference gripRef, InputActionReference triggerRef)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        if (root == null)
        {
            Debug.LogError("[VRProjectFixer] Failed to load prefab contents: " + prefabPath);
            return;
        }

        try
        {
            Animator animator = root.GetComponent<Animator>();
            if (animator == null)
            {
                animator = root.AddComponent<Animator>();
            }

            RuntimeAnimatorController controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(controllerPath);
            if (controller != null)
            {
                animator.runtimeAnimatorController = controller;
            }
            else
            {
                Debug.LogWarning("[VRProjectFixer] Could not load AnimatorController at: " + controllerPath);
            }

            HandAnimation handAnim = root.GetComponent<HandAnimation>();
            if (handAnim == null)
            {
                handAnim = root.AddComponent<HandAnimation>();
            }

            SerializedObject serializedHandAnim = new SerializedObject(handAnim);
            SerializedProperty gripProp = serializedHandAnim.FindProperty("gripActionRefrerence");
            SerializedProperty triggerProp = serializedHandAnim.FindProperty("triggerActionRefrerence");

            if (gripProp != null && gripRef != null)
            {
                gripProp.objectReferenceValue = gripRef;
            }

            if (triggerProp != null && triggerRef != null)
            {
                triggerProp.objectReferenceValue = triggerRef;
            }

            serializedHandAnim.ApplyModifiedProperties();

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Debug.Log("[VRProjectFixer] Successfully configured hand prefab: " + prefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    public static void FixSceneLocomotionAndInteractables()
    {
        string scenePath = "Assets/project/scenes/_Test/Test_TerrainAndRover.unity";
        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        if (!scene.IsValid())
        {
            Debug.LogError("[VRProjectFixer] Invalid scene: " + scenePath);
            return;
        }

        // 1. Locomotion Mediator on XR Origin (VR)
        GameObject xrOriginGO = GameObject.Find("XR Origin (VR)");
        if (xrOriginGO != null)
        {
            LocomotionMediator mediator = xrOriginGO.GetComponent<LocomotionMediator>();
            if (mediator == null)
            {
                mediator = xrOriginGO.AddComponent<LocomotionMediator>();
                Debug.Log("[VRProjectFixer] Added LocomotionMediator component to 'XR Origin (VR)'.");
            }

            TeleportationProvider teleportProvider = xrOriginGO.GetComponent<TeleportationProvider>();
            if (teleportProvider != null)
            {
                teleportProvider.mediator = mediator;
                EditorUtility.SetDirty(teleportProvider);
                Debug.Log("[VRProjectFixer] Linked TeleportationProvider.mediator to LocomotionMediator.");
            }
        }
        else
        {
            Debug.LogWarning("[VRProjectFixer] 'XR Origin (VR)' GameObject not found in scene.");
        }

        // 2. Fix BoxCollider negative scale warning & enable XRGrabInteractable on Cube
        GameObject cubeGO = GameObject.Find("Cube");
        if (cubeGO != null)
        {
            Vector3 ls = cubeGO.transform.localScale;
            if (ls.y < 0)
            {
                cubeGO.transform.localScale = new Vector3(ls.x, Mathf.Abs(ls.y), ls.z);
                Debug.Log($"[VRProjectFixer] Fixed negative localScale on 'Cube': old scale={ls}, new scale={cubeGO.transform.localScale}");
            }

            if (cubeGO.GetComponent<Collider>() == null)
            {
                cubeGO.AddComponent<BoxCollider>();
            }

            Rigidbody rb = cubeGO.GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = cubeGO.AddComponent<Rigidbody>();
            }
            rb.isKinematic = true;

            XRGrabInteractable grabInteractable = cubeGO.GetComponent<XRGrabInteractable>();
            if (grabInteractable == null)
            {
                grabInteractable = cubeGO.AddComponent<XRGrabInteractable>();
                Debug.Log("[VRProjectFixer] Added XRGrabInteractable component to 'Cube'.");
            }
            grabInteractable.movementType = XRBaseInteractable.MovementType.Kinematic;
            EditorUtility.SetDirty(cubeGO);
        }
        else
        {
            Debug.LogWarning("[VRProjectFixer] 'Cube' GameObject not found in scene.");
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[VRProjectFixer] Scene saved with updated Locomotion and Interactable configurations.");
    }
}

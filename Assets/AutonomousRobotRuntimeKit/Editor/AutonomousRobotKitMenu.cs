#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace AutonomousRobotKit.Editor
{
    public static class AutonomousRobotKitMenu
    {
        [MenuItem("Tools/Autonomous Robot Kit/Add Runtime '+' Import UI to Active Scene", false, 10)]
        public static void AddImportUIToActiveScene()
        {
            var existing = Object.FindAnyObjectByType<RuntimeRoverImportUI>();
            if (existing != null)
            {
                Debug.Log($"[AutonomousRobotKit] RuntimeRoverImportUI is already present in scene: '{existing.gameObject.name}'.");
                Selection.activeGameObject = existing.gameObject;
                return;
            }

            var go = new GameObject("RuntimeRoverImportUI");
            go.AddComponent<RuntimeRoverImportUI>();
            Undo.RegisterCreatedObjectUndo(go, "Create Runtime Rover Import UI");
            Selection.activeGameObject = go;

            Debug.Log("<color=#00D4FF>[AutonomousRobotKit] SUCCESS:</color> Added Runtime '+' Import UI to active scene.");
        }
    }
}
#endif

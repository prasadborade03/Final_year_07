using UnityEngine;
using UnityEditor;
using ProjectName.Rover;

namespace ProjectName.Editor
{
    [CustomEditor(typeof(RoverPositionSync))]
    public class RoverPositionSyncEditor : UnityEditor.Editor
    {
        private static bool isScenePickMode = false;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            RoverPositionSync sync = (RoverPositionSync)target;

            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField("🎯 Click-to-Place Rover on Terrain", EditorStyles.boldLabel);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField($"Active World Pos: {sync.transform.position:F1}");

            GUI.backgroundColor = isScenePickMode ? new Color(1f, 0.4f, 0.2f) : new Color(0.1f, 0.85f, 0.45f);
            string btnText = isScenePickMode ? "👉 CLICK ANYWHERE ON TERRAIN NOW (ESC to cancel)" : "📍 CLICK ON TERRAIN TO PLACE ROVER";
            if (GUILayout.Button(btnText, GUILayout.Height(36)))
            {
                isScenePickMode = !isScenePickMode;
                SceneView.RepaintAll();
            }

            GUI.backgroundColor = Color.white;
            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Snap to Surface"))
            {
                Undo.RecordObject(sync.transform, "Snap Rover to Terrain");
                sync.SnapToTerrainSurface();
                EditorUtility.SetDirty(sync.gameObject);
            }
            if (GUILayout.Button("Center on Terrain"))
            {
                Undo.RecordObject(sync.transform, "Center on Terrain");
                sync.CenterOnTerrain();
                EditorUtility.SetDirty(sync.gameObject);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Highest Mountain"))
            {
                Undo.RecordObject(sync.transform, "Place at Peak");
                sync.PlaceAtHighestPeak();
                EditorUtility.SetDirty(sync.gameObject);
            }
            if (GUILayout.Button("Lowest Crater"))
            {
                Undo.RecordObject(sync.transform, "Place at Crater");
                sync.PlaceAtLowestCrater();
                EditorUtility.SetDirty(sync.gameObject);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();

            EditorGUILayout.HelpBox("Scene View Shortcut:\nWith this rover selected, simply hold [Shift] and Click anywhere on the terrain to instantly spawn/place it there!", MessageType.Info);
        }

        private void OnSceneGUI()
        {
            RoverPositionSync sync = (RoverPositionSync)target;
            Event e = Event.current;

            if (isScenePickMode)
            {
                HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
                if (e != null && e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
                {
                    isScenePickMode = false;
                    e.Use();
                    Repaint();
                    SceneView.RepaintAll();
                    return;
                }
            }

            // Either pick mode is active OR holding Shift + Left Click
            if (e != null && e.isMouse && e.type == EventType.MouseDown && e.button == 0 && (isScenePickMode || e.shift))
            {
                Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                if (Physics.Raycast(ray, out RaycastHit hit, 5000f))
                {
                    var terrain = hit.collider.GetComponent<UnityEngine.Terrain>() ?? hit.collider.GetComponentInParent<UnityEngine.Terrain>();
                    if (terrain != null || hit.collider is TerrainCollider)
                    {
                        Undo.RecordObject(sync.transform, "Click to Place Rover");
                        Vector3 targetPos = hit.point + Vector3.up * sync.surfaceOffset;
                        sync.TeleportTo(targetPos, sync.transform.rotation);
                        sync.desiredCoordinates = new Vector2(targetPos.x, targetPos.z);
                        EditorUtility.SetDirty(sync.gameObject);
                        isScenePickMode = false;
                        e.Use();
                        Debug.Log($"[RoverPositionSync] Rover placed at clicked terrain point: {targetPos}");
                    }
                }
            }
        }

        [MenuItem("Tools/Planetary Simulation/Place Rover at Desired Position")]
        public static void MenuPlaceRoverAtDesiredPosition()
        {
            var sync = FindAnyObjectByType<RoverPositionSync>();
            if (sync != null)
            {
                Undo.RecordObject(sync.transform, "Place at Desired Coordinates");
                sync.PlaceAtDesiredCoordinates();
                EditorUtility.SetDirty(sync.gameObject);
            }
            else
            {
                Debug.LogWarning("[RoverPositionSyncEditor] No Rover with RoverPositionSync found in the scene.");
            }
        }

        [MenuItem("Tools/Planetary Simulation/Snap Rover to Terrain Surface")]
        public static void MenuSnapRoverToTerrain()
        {
            var sync = FindAnyObjectByType<RoverPositionSync>();
            if (sync != null)
            {
                Undo.RecordObject(sync.transform, "Snap Rover to Terrain");
                sync.SnapToTerrainSurface();
                EditorUtility.SetDirty(sync.gameObject);
            }
            else
            {
                Debug.LogWarning("[RoverPositionSyncEditor] No Rover with RoverPositionSync found in the scene.");
            }
        }

        [MenuItem("Tools/Planetary Simulation/Center Rover on Terrain")]
        public static void MenuCenterRoverOnTerrain()
        {
            var sync = FindAnyObjectByType<RoverPositionSync>();
            if (sync != null)
            {
                Undo.RecordObject(sync.transform, "Center Rover on Terrain");
                sync.CenterOnTerrain();
                EditorUtility.SetDirty(sync.gameObject);
            }
            else
            {
                Debug.LogWarning("[RoverPositionSyncEditor] No Rover with RoverPositionSync found in the scene.");
            }
        }
    }
}

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace ProjectName.EditorScripts
{
    /// <summary>
    /// Editor utility that automatically validates project tags and asset settings.
    /// Ensures 'Terrain' tag is always registered to prevent runtime exceptions and editor pauses.
    /// </summary>
    [InitializeOnLoad]
    public static class TerrainTagHelper
    {
        static TerrainTagHelper()
        {
            EnsureTerrainTagExists();
        }

        [MenuItem("Tools/Planetary Rover/Ensure Terrain Tag Exists")]
        public static void EnsureTerrainTagExists()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0) return;

            SerializedObject tagManager = new SerializedObject(assets[0]);
            SerializedProperty tagsProp = tagManager.FindProperty("tags");
            if (tagsProp == null) return;

            bool found = false;
            for (int i = 0; i < tagsProp.arraySize; i++)
            {
                SerializedProperty t = tagsProp.GetArrayElementAtIndex(i);
                if (t.stringValue.Equals("Terrain"))
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                tagsProp.InsertArrayElementAtIndex(tagsProp.arraySize);
                SerializedProperty n = tagsProp.GetArrayElementAtIndex(tagsProp.arraySize - 1);
                n.stringValue = "Terrain";
                tagManager.ApplyModifiedProperties();
                AssetDatabase.SaveAssets();
                Debug.Log("[TerrainTagHelper] Automatically registered missing 'Terrain' tag in ProjectSettings!");
            }
        }
    }

    /// <summary>
    /// Asset postprocessor to automatically mark *_Normal textures in project/materials as Normal maps.
    /// </summary>
    public class PlanetaryTexturePostprocessor : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (assetPath.Contains("project/materials") && (assetPath.Contains("_Normal") || assetPath.Contains("_normal") || assetPath.Contains("_norm")))
            {
                TextureImporter textureImporter = (TextureImporter)assetImporter;
                if (textureImporter.textureType != TextureImporterType.NormalMap)
                {
                    textureImporter.textureType = TextureImporterType.NormalMap;
                    textureImporter.wrapMode = TextureWrapMode.Repeat;
                    Debug.Log($"[PlanetaryTexturePostprocessor] Automatically configured '{assetPath}' as Normal Map.");
                }
            }
        }
    }
}
#endif

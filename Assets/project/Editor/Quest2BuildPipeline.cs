using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ProjectName.EditorScripts
{
    public static class Quest2BuildPipeline
    {
        public static readonly string[] RequiredScenes = new[]
        {
            "Assets/project/scenes/MainMenu.unity",
            "Assets/project/scenes/Simulation_VR.unity",
            "Assets/project/scenes/Simulation_Flat.unity",
            "Assets/project/scenes/vr_module.unity"
        };

        public const string OutputDirectory = "Builds";
        public const string OutputApkName = "PlanetaryRover_Quest2.apk";

        [MenuItem("Build/Build Meta Quest 2 APK", false, 10)]
        public static void BuildQuest2Apk()
        {
            Debug.Log("<color=#00E5FF><b>[Quest2BuildPipeline]</b> Starting automated Meta Quest 2 APK build...</color>");

            // 1. Validate scenes
            foreach (var scenePath in RequiredScenes)
            {
                if (!File.Exists(scenePath))
                {
                    Debug.LogError($"[Quest2BuildPipeline] Required scene missing: {scenePath}");
                    if (Application.isBatchMode) EditorApplication.Exit(1);
                    return;
                }
            }

            // 2. Configure Android Player Settings
            Debug.Log("[Quest2BuildPipeline] Configuring PlayerSettings for Meta Quest 2 (ARM64, IL2CPP)...");
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            
            if ((int)PlayerSettings.Android.minSdkVersion < 26)
            {
                PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            }

            EditorUserBuildSettings.buildAppBundle = false; // Generate APK, not AAB

            // 3. Ensure destination directory exists
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string outputDir = Path.Combine(projectRoot, OutputDirectory);
            if (!Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            string apkPath = Path.Combine(outputDir, OutputApkName);

            // 4. Build Player Options
            BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions
            {
                scenes = RequiredScenes,
                locationPathName = apkPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None
            };

            Debug.Log($"[Quest2BuildPipeline] Building APK to: {apkPath}");
            DateTime startTime = DateTime.UtcNow;

            BuildReport report = BuildPipeline.BuildPlayer(buildPlayerOptions);
            BuildSummary summary = report.summary;

            TimeSpan duration = DateTime.UtcNow - startTime;

            if (summary.result == BuildResult.Succeeded)
            {
                FileInfo apkInfo = new FileInfo(apkPath);
                long sizeMb = apkInfo.Exists ? (apkInfo.Length / (1024 * 1024)) : 0;

                Debug.Log($"<color=#00FF66><b>[Quest2BuildPipeline] BUILD SUCCEEDED!</b></color>");
                Debug.Log($"[Quest2BuildPipeline] Output: {apkPath} ({sizeMb} MB)");
                Debug.Log($"[Quest2BuildPipeline] Total build time: {duration.TotalMinutes:F1} min ({summary.totalTime.TotalSeconds:F0}s)");

                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            else
            {
                Debug.LogError($"<color=#FF3333><b>[Quest2BuildPipeline] BUILD FAILED!</b></color> Result: {summary.result}, Errors: {summary.totalErrors}");
                
                foreach (var step in report.steps)
                {
                    foreach (var msg in step.messages)
                    {
                        if (msg.type == LogType.Error || msg.type == LogType.Exception)
                        {
                            Debug.LogError($"[BuildError] {msg.content}");
                        }
                    }
                }

                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }
    }

    public class Quest2ShaderStripper : UnityEditor.Build.IPreprocessShaders
    {
        public int callbackOrder => 0;

        public void OnProcessShader(
            UnityEngine.Shader shader,
            UnityEditor.Rendering.ShaderSnippetData snippet,
            System.Collections.Generic.IList<UnityEditor.Rendering.ShaderCompilerData> data)
        {
            if (shader == null || string.IsNullOrEmpty(shader.name)) return;

            if (shader.name.StartsWith("TextMeshPro/SRP/TMP_SDF-URP"))
            {
                data.Clear();
                return;
            }

            if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android)
            {
                for (int i = data.Count - 1; i >= 0; i--)
                {
                    if (data[i].shaderKeywordSet.IsEnabled(new UnityEngine.Rendering.ShaderKeyword("DEBUG_DISPLAY")))
                    {
                        data.RemoveAt(i);
                    }
                }
            }
        }
    }
}

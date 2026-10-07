using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectName.Core
{
    /// <summary>
    /// Master Scene Loader and VR Mode Controller.
    /// Manages the application lifecycle:
    /// - Start Screen (MainMenu) -> Reads VR toggle -> Launches Simulation_VR or Simulation_Flat.
    /// - In-Simulation -> "Back to Menu" -> Returns to MainMenu.
    /// </summary>
    public class SceneLoader : MonoBehaviour
    {
        public const string PREF_VR_ENABLED = "VR_Enabled";
        public const string SCENE_MAIN_MENU = "MainMenu";
        public const string SCENE_SIMULATION_VR = "Simulation_VR";
        public const string SCENE_VR_MODULE_FALLBACK = "VR_Module";
        public const string SCENE_SIMULATION_FLAT = "Simulation_Flat";

        public static bool IsVREnabled
        {
            get
            {
                string activeScene = SceneManager.GetActiveScene().name;
                if (activeScene == SCENE_SIMULATION_FLAT) return false;
                if (activeScene == SCENE_SIMULATION_VR || activeScene == SCENE_VR_MODULE_FALLBACK) return true;
                return PlayerPrefs.GetInt(PREF_VR_ENABLED, 1) == 1;
            }
            set
            {
                PlayerPrefs.SetInt(PREF_VR_ENABLED, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        /// <summary>
        /// Reads the VR toggle and loads the appropriate simulation scene.
        /// </summary>
        public static void LoadSimulation()
        {
            bool vrOn = IsVREnabled;
            Debug.Log($"[SceneLoader] Launching simulation. Mode: {(vrOn ? "VR Headset (Simulation_VR)" : "Desktop Flat (Simulation_Flat)")}");

            if (vrOn)
            {
                if (Application.CanStreamedLevelBeLoaded(SCENE_SIMULATION_VR))
                {
                    SceneManager.LoadScene(SCENE_SIMULATION_VR);
                }
                else if (Application.CanStreamedLevelBeLoaded(SCENE_VR_MODULE_FALLBACK))
                {
                    Debug.LogWarning($"[SceneLoader] '{SCENE_SIMULATION_VR}' not in build settings; falling back to '{SCENE_VR_MODULE_FALLBACK}'.");
                    SceneManager.LoadScene(SCENE_VR_MODULE_FALLBACK);
                }
                else
                {
                    Debug.LogError($"[SceneLoader] Neither '{SCENE_SIMULATION_VR}' nor '{SCENE_VR_MODULE_FALLBACK}' could be loaded!");
                }
            }
            else
            {
                if (Application.CanStreamedLevelBeLoaded(SCENE_SIMULATION_FLAT))
                {
                    SceneManager.LoadScene(SCENE_SIMULATION_FLAT);
                }
                else
                {
                    Debug.LogWarning($"[SceneLoader] '{SCENE_SIMULATION_FLAT}' not in build settings; falling back to '{SCENE_VR_MODULE_FALLBACK}'.");
                    SceneManager.LoadScene(SCENE_VR_MODULE_FALLBACK);
                }
            }
        }

        /// <summary>
        /// Returns to the MainMenu scene from any simulation scene.
        /// </summary>
        public static void LoadMainMenu()
        {
            Debug.Log("[SceneLoader] Returning to Main Menu...");
            if (Application.CanStreamedLevelBeLoaded(SCENE_MAIN_MENU))
            {
                SceneManager.LoadScene(SCENE_MAIN_MENU);
            }
            else
            {
                Debug.LogError($"[SceneLoader] Cannot load '{SCENE_MAIN_MENU}'. Ensure it is added to Build Settings.");
            }
        }

        // Instance methods for UI Events / Inspector bindings
        public void SetVREnabled(bool enabled)
        {
            IsVREnabled = enabled;
            Debug.Log($"[SceneLoader] VR Enabled set to: {enabled}");
        }

        public void StartSimulation()
        {
            LoadSimulation();
        }

        public void ReturnToMenu()
        {
            LoadMainMenu();
        }

        public void QuitApplication()
        {
            Debug.Log("[SceneLoader] Quitting application.");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}

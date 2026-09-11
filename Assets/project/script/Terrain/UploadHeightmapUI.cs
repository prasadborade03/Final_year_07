using UnityEngine;
using SFB; // Standalone File Browser namespace

namespace ProjectName.UI
{
    /// <summary>
    /// Job: Show an "Upload Heightmap" button. When clicked, open the
    /// native OS file picker, and if the user selects a valid image,
    /// pass its path to the TerrainGenerator.
    ///
    /// This script knows about UI and file dialogs -- it does NOT know
    /// HOW terrain is built. It just hands off a file path.
    /// </summary>
    /// 
    public class UploadHeightmapUI : MonoBehaviour
    {
        [Tooltip("Drag the GameObject holding your TerrainGenerator component here.")]
        public ProjectName.Terrain.TerrainGenerator terrainGenerator;

        [Tooltip("The button (or its parent panel) to hide once upload succeeds. " +
                 "Drag the UploadButton GameObject here.")]
        public GameObject uploadPanel;

        /// <summary>
        /// Call this from a UI Button's OnClick() event in the Inspector.
        /// </summary>
        /// 
        [Tooltip("Drag the _SimulationFlow object here")]
        public SimulationFlowController flowController;


        public void OnUploadButtonClicked()
        {
            // Define what file types the dialog should allow.
            // "Image Files" is just a label shown in the dialog window.
            var extensions = new[]
            {
                new ExtensionFilter("Image Files", "png", "jpg", "jpeg")
            };

            // Opens the native OS file picker.
            // Parameters: title, starting directory ("" = last used), 
            // filters, allowMultiple (false = single file only).
            string[] paths = StandaloneFileBrowser.OpenFilePanel(
                "Select Heightmap Image", "", extensions, false);

            // paths.Length == 0 means the user closed the dialog
            // without picking anything -- not an error, just do nothing.
            if (paths.Length == 0)
            {
                Debug.Log("Upload cancelled by user.");
                return;
            }

            string selectedPath = paths[0];
            Debug.Log("Selected heightmap: " + selectedPath);

            // Hand off to the terrain module. This script's job ends here.
            terrainGenerator.GenerateTerrainFromImage(selectedPath);

            // Upload succeeded -- hide the button so it doesn't sit on
            // screen during the simulation. Using SetActive(false) instead
            // of Destroy() keeps the option open to bring it back later
            // (e.g. a future "Load Different Heightmap" feature).
            // If you want it gone permanently instead, swap this line for:
            //     Destroy(uploadPanel);
            if (uploadPanel != null)
            {
                uploadPanel.SetActive(false);
            }

            if (flowController != null)
            flowController.OnTerrainReady();

        }
    }
}
using UnityEngine;
using UnityEngine.UIElements;
using ProjectName.Core;

namespace ProjectName.UI
{
    [RequireComponent(typeof(UIDocument))]
    public class MainMenuController : MonoBehaviour
    {
        private UIDocument uiDocument;
        private VisualElement root;

        private VisualElement cardVR;
        private VisualElement cardFlat;
        private Toggle toggleVR;
        private Label lblActiveModeStatus;
        private Label lblBtnLaunchText;
        private Button btnStartSimulation;
        private Button btnQuit;

        private bool isVREnabled = true;

        private void Awake()
        {
            EnsureUIDocumentSetup();
        }

        private void OnEnable()
        {
            EnsureUIDocumentSetup();
            if (uiDocument == null) return;

            root = uiDocument.rootVisualElement;
            if (root == null)
            {
                Debug.LogWarning("[MainMenuController] rootVisualElement is null!");
                return;
            }

            // Ensure root fills full screen
            root.style.width = Length.Percent(100);
            root.style.height = Length.Percent(100);
            root.style.flexGrow = 1;

            // Load saved preference
            isVREnabled = SceneLoader.IsVREnabled;

            cardVR = root.Q<VisualElement>("CardVR");
            cardFlat = root.Q<VisualElement>("CardFlat");
            toggleVR = root.Q<Toggle>("ToggleVR");
            lblActiveModeStatus = root.Q<Label>("LblActiveModeStatus");
            lblBtnLaunchText = root.Q<Label>("LblBtnLaunchText");
            btnStartSimulation = root.Q<Button>("BtnStartSimulation");
            btnQuit = root.Q<Button>("BtnQuit");

            if (toggleVR != null)
            {
                toggleVR.value = isVREnabled;
                toggleVR.RegisterValueChangedCallback(OnToggleVRChanged);
            }

            if (cardVR != null)
            {
                cardVR.RegisterCallback<ClickEvent>(evt => SetVRMode(true));
            }

            if (cardFlat != null)
            {
                cardFlat.RegisterCallback<ClickEvent>(evt => SetVRMode(false));
            }

            if (btnStartSimulation != null)
            {
                btnStartSimulation.clicked += OnStartSimulationClicked;
            }

            if (btnQuit != null)
            {
                btnQuit.clicked += OnQuitClicked;
            }

            UpdateUIState();
        }

        private void OnToggleVRChanged(ChangeEvent<bool> evt)
        {
            SetVRMode(evt.newValue);
        }

        private void SetVRMode(bool enabled)
        {
            isVREnabled = enabled;
            SceneLoader.IsVREnabled = enabled;
            if (toggleVR != null && toggleVR.value != enabled)
            {
                toggleVR.SetValueWithoutNotify(enabled);
            }
            UpdateUIState();
        }

        private void UpdateUIState()
        {
            if (cardVR != null)
            {
                if (isVREnabled) cardVR.AddToClassList("mode-card-active");
                else cardVR.RemoveFromClassList("mode-card-active");
            }

            if (cardFlat != null)
            {
                if (!isVREnabled) cardFlat.AddToClassList("mode-card-active");
                else cardFlat.RemoveFromClassList("mode-card-active");
            }

            if (lblActiveModeStatus != null)
            {
                lblActiveModeStatus.text = isVREnabled
                    ? "VR HEADSET (Simulation_VR)"
                    : "DESKTOP FLAT (Simulation_Flat)";
                lblActiveModeStatus.style.color = isVREnabled
                    ? new StyleColor(new Color(0f, 0.83f, 1f)) // Precision cyan
                    : new StyleColor(new Color(0.79f, 0.84f, 0.90f)); // Crisp light slate
            }

            if (lblBtnLaunchText != null)
            {
                lblBtnLaunchText.text = "START SIMULATION";
            }
        }

        private void OnStartSimulationClicked()
        {
            Debug.Log($"[MainMenu] Launch button clicked. VR Enabled: {isVREnabled}");
            SceneLoader.LoadSimulation();
        }

        private void OnQuitClicked()
        {
            Debug.Log("[MainMenu] Quit button clicked.");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void EnsureUIDocumentSetup()
        {
            if (uiDocument == null) uiDocument = GetComponent<UIDocument>();
            if (uiDocument == null) uiDocument = gameObject.AddComponent<UIDocument>();

            if (uiDocument.panelSettings == null)
            {
                var ps = Resources.Load<PanelSettings>("MainMenuPanelSettings") 
                         ?? Resources.Load<PanelSettings>("FlatPanelSettings");
                if (ps != null)
                {
                    uiDocument.panelSettings = ps;
                    Debug.Log($"[MainMenuController] Auto-assigned PanelSettings: {ps.name} (RenderMode: {ps.renderMode})");
                }
                else
                {
                    Debug.LogWarning("[MainMenuController] PanelSettings could not be found in Resources.");
                }
            }

            if (uiDocument.visualTreeAsset == null)
            {
                var vta = Resources.Load<VisualTreeAsset>("MainMenu");
                if (vta != null)
                {
                    uiDocument.visualTreeAsset = vta;
                    Debug.Log($"[MainMenuController] Auto-assigned VisualTreeAsset: {vta.name}");
                }
                else
                {
                    Debug.LogWarning("[MainMenuController] MainMenu.uxml could not be found in Resources.");
                }
            }
        }
    }
}

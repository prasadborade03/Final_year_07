using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.UIElements;
using TMPro;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace ProjectName.UI
{
    /// <summary>
    /// Centralized Global UI Click SFX manager for KALPANA.
    /// Provides subtle, non-intrusive tactile audio feedback for UI buttons, cards, toggles,
    /// dropdowns, and VR XR interactors across all scenes in the simulation.
    /// Uses the exact audio asset: Assets/project/sfx/mixkit-modern-technology-select-3124.wav.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class UIClickAudio : MonoBehaviour
    {
        private static UIClickAudio instance;

        public static UIClickAudio Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = FindAnyObjectByType<UIClickAudio>();
                    if (instance == null)
                    {
                        GameObject go = new GameObject("[UIClickAudio]");
                        instance = go.AddComponent<UIClickAudio>();
                    }
                }
                return instance;
            }
        }

        [Header("Audio Settings")]
        [Tooltip("Exact audio clip played on UI clicks (mixkit-modern-technology-select-3124.wav).")]
        [SerializeField] private AudioClip clickClip;

        [Range(0f, 1f)]
        [Tooltip("Playback volume for UI clicks (default 0.20 for subtle, non-irritating feedback).")]
        [SerializeField] private float volume = 0.20f;

        [Tooltip("Minimum delay in seconds between sounds to prevent double-clicks or flutter.")]
        [SerializeField] private float debounceTime = 0.04f;

        private AudioSource audioSource;
        private static float lastPlayTime = -1f;

        // Tracking sets to prevent duplicate listeners
        private readonly HashSet<VisualElement> registeredRoots = new HashSet<VisualElement>();
        private readonly HashSet<Selectable> registeredSelectables = new HashSet<Selectable>();

        private float nextPeriodicScanTime = 0f;
        private const float SCAN_INTERVAL = 0.5f;

        public float Volume
        {
            get => volume;
            set => volume = Mathf.Clamp01(value);
        }

        public AudioClip ClickClip
        {
            get => clickClip;
            set => clickClip = value;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoInitialize()
        {
            _ = Instance;
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);

            InitializeAudioSource();
            SceneManager.sceneLoaded += OnSceneLoaded;

            ScanAllUI();
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                SceneManager.sceneLoaded -= OnSceneLoaded;
            }
        }

        private void Update()
        {
            // Lightweight periodic scan to discover dynamically spawned panels, dialogs, and modals
            if (Time.unscaledTime >= nextPeriodicScanTime)
            {
                nextPeriodicScanTime = Time.unscaledTime + SCAN_INTERVAL;
                ScanAllUI();
            }
        }

        private void InitializeAudioSource()
        {
            if (audioSource == null)
            {
                audioSource = GetComponent<AudioSource>();
                if (audioSource == null)
                {
                    audioSource = gameObject.AddComponent<AudioSource>();
                }
            }

            audioSource.playOnAwake = false;
            audioSource.loop = false;
            audioSource.spatialBlend = 0f; // Pure 2D non-spatial audio for crisp, clear UI
            audioSource.bypassEffects = true;
            audioSource.bypassListenerEffects = true;
            audioSource.ignoreListenerPause = true;

            ResolveAudioClip();
            EnsureAudioListener();
        }

        private void ResolveAudioClip()
        {
            if (clickClip != null) return;

            // 1. Try loading from Resources
            clickClip = Resources.Load<AudioClip>("Audio/mixkit-modern-technology-select-3124")
                     ?? Resources.Load<AudioClip>("Audio/ui_click_subtle");

#if UNITY_EDITOR
            // 2. Direct lookup from Assets/project/sfx/ in Editor
            if (clickClip == null)
            {
                clickClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/project/sfx/mixkit-modern-technology-select-3124.wav");
            }
#endif
        }

        private void EnsureAudioListener()
        {
            var listeners = FindObjectsByType<AudioListener>();
            if (listeners == null || listeners.Length == 0)
            {
                if (GetComponent<AudioListener>() == null)
                {
                    gameObject.AddComponent<AudioListener>();
                }
            }
            else if (listeners.Length > 1)
            {
                // If scene already has an AudioListener (e.g. on Camera), remove our fallback listener
                var myListener = GetComponent<AudioListener>();
                if (myListener != null)
                {
                    Destroy(myListener);
                }
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            EnsureAudioListener();
            ScanAllUI();
        }

        /// <summary>
        /// Scans active and inactive UI systems in the current scene and binds click SFX.
        /// </summary>
        public void ScanAllUI()
        {
            ScanUIDocuments();
            ScanSelectables();
        }

        /// <summary>
        /// Finds all UI Toolkit UIDocument instances and registers click listeners on their root elements.
        /// </summary>
        public void ScanUIDocuments()
        {
            var docs = FindObjectsByType<UIDocument>(FindObjectsInactive.Include);
            foreach (var doc in docs)
            {
                if (doc != null && doc.rootVisualElement != null)
                {
                    RegisterVisualElement(doc.rootVisualElement);
                }
            }
        }

        /// <summary>
        /// Registers click sound callbacks on a UI Toolkit visual tree root.
        /// </summary>
        public static void RegisterVisualElement(VisualElement root)
        {
            if (root == null || Instance == null) return;
            if (Instance.registeredRoots.Contains(root)) return;

            Instance.registeredRoots.Add(root);
            root.RegisterCallback<ClickEvent>(Instance.OnUIToolkitClick);
            root.RegisterCallback<NavigationSubmitEvent>(Instance.OnUIToolkitSubmit);
        }

        /// <summary>
        /// Convenience helper to register a specific UIDocument.
        /// </summary>
        public static void RegisterUIDocument(UIDocument uiDoc)
        {
            if (uiDoc != null && uiDoc.rootVisualElement != null)
            {
                RegisterVisualElement(uiDoc.rootVisualElement);
            }
        }

        private void OnUIToolkitClick(ClickEvent evt)
        {
            if (evt.target is VisualElement el && IsClickableVisualElement(el))
            {
                PlayClick();
            }
        }

        private void OnUIToolkitSubmit(NavigationSubmitEvent evt)
        {
            if (evt.target is VisualElement el && IsClickableVisualElement(el))
            {
                PlayClick();
            }
        }

        /// <summary>
        /// Determines if a UI Toolkit VisualElement represents an interactive button, toggle, or card.
        /// Explicitly excludes continuous sliders and painter drawing canvases.
        /// </summary>
        private static bool IsClickableVisualElement(VisualElement el)
        {
            if (el == null) return false;

            // Exclude sliders to prevent continuous clicking while dragging
            if (el is UnityEngine.UIElements.Slider || el is UnityEngine.UIElements.SliderInt ||
                el.GetFirstAncestorOfType<UnityEngine.UIElements.Slider>() != null ||
                el.GetFirstAncestorOfType<UnityEngine.UIElements.SliderInt>() != null)
            {
                return false;
            }

            // Exclude terrain painter drawing canvas
            if (el.name == "HeightmapPreviewImage" || el.name == "HeightmapPainterImage" || el.ClassListContains("painter-canvas"))
            {
                return false;
            }

            // Direct interactive types
            if (el is UnityEngine.UIElements.Button || el is UnityEngine.UIElements.Toggle || el is UnityEngine.UIElements.RadioButton || el is DropdownField)
            {
                return true;
            }

            // Descendants of buttons or toggles (e.g. clicked on label/icon inside a button)
            if (el.GetFirstAncestorOfType<UnityEngine.UIElements.Button>() != null ||
                el.GetFirstAncestorOfType<UnityEngine.UIElements.Toggle>() != null ||
                el.GetFirstAncestorOfType<DropdownField>() != null)
            {
                return true;
            }

            // Check class hierarchy for card, button, tab, and mode selectors
            VisualElement curr = el;
            while (curr != null && curr != curr.panel?.visualTree)
            {
                foreach (string cls in curr.GetClasses())
                {
                    string lower = cls.ToLowerInvariant();
                    if (lower.Contains("btn") || lower.Contains("button") || lower.Contains("card") || lower.Contains("tab") || lower.Contains("toggle"))
                    {
                        return true;
                    }
                }
                curr = curr.parent;
            }

            return false;
        }

        /// <summary>
        /// Finds all uGUI Selectable components (Button, Toggle, Dropdown) and attaches click SFX.
        /// </summary>
        public void ScanSelectables()
        {
            var selectables = FindObjectsByType<Selectable>(FindObjectsInactive.Include);
            foreach (var s in selectables)
            {
                if (s == null || registeredSelectables.Contains(s)) continue;
                registeredSelectables.Add(s);

                if (s is UnityEngine.UI.Button btn)
                {
                    btn.onClick.AddListener(() => PlayClick());
                }
                else if (s is UnityEngine.UI.Toggle tgl)
                {
                    tgl.onValueChanged.AddListener(_ => PlayClick());
                }
                else if (s is UnityEngine.UI.Dropdown dd)
                {
                    dd.onValueChanged.AddListener(_ => PlayClick());
                }
                else if (s is TMP_Dropdown tmpDd)
                {
                    tmpDd.onValueChanged.AddListener(_ => PlayClick());
                }
            }
        }

        /// <summary>
        /// Plays the subtle UI click sound once.
        /// Enforces debounce window to prevent flutter or duplicate firing.
        /// </summary>
        /// <param name="customVolume">Optional volume override (-1 uses configured volume).</param>
        public static void PlayClick(float customVolume = -1f)
        {
            float now = Time.unscaledTime;
            float debounce = (Instance != null) ? Instance.debounceTime : 0.04f;
            if (lastPlayTime > 0f && (now - lastPlayTime) < debounce)
            {
                return;
            }
            lastPlayTime = now;

            if (Instance == null) return;
            Instance.PlayInternal(customVolume);
        }

        private void PlayInternal(float customVolume)
        {
            if (clickClip == null)
            {
                ResolveAudioClip();
                if (clickClip == null)
                {
                    return;
                }
            }

            if (audioSource == null)
            {
                InitializeAudioSource();
            }

            float finalVolume = customVolume >= 0f ? Mathf.Clamp01(customVolume) : volume;
            audioSource.PlayOneShot(clickClip, finalVolume);
        }

        /// <summary>
        /// Helper to bind a UI Toolkit Button explicitly.
        /// </summary>
        public static void BindButton(UnityEngine.UIElements.Button button)
        {
            if (button == null) return;
            button.clicked += () => PlayClick();
        }

        /// <summary>
        /// Helper to bind a UI Toolkit Toggle explicitly.
        /// </summary>
        public static void BindToggle(UnityEngine.UIElements.Toggle toggle)
        {
            if (toggle == null) return;
            toggle.RegisterValueChangedCallback(_ => PlayClick());
        }

        /// <summary>
        /// Helper to bind a uGUI Button explicitly.
        /// </summary>
        public static void BindButton(UnityEngine.UI.Button button)
        {
            if (button == null) return;
            button.onClick.AddListener(() => PlayClick());
        }

        /// <summary>
        /// Helper to bind a uGUI Toggle explicitly.
        /// </summary>
        public static void BindToggle(UnityEngine.UI.Toggle toggle)
        {
            if (toggle == null) return;
            toggle.onValueChanged.AddListener(_ => PlayClick());
        }
    }
}

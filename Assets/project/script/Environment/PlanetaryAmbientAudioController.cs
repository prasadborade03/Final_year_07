using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace ProjectName.Environment
{
    /// <summary>
    /// Subtle, Continuous Planetary Ambient SFX Controller for KALPANA.
    /// Manages background extraterrestrial environmental ambience:
    /// - Plays one subtle, continuous, seamless-looping planetary ambience sound (space drone/atmospheric texture).
    /// - Pure 2D non-directional playback for comfortable, non-fatiguing VR headset immersion.
    /// - Smooth 2-4s fade-in upon entering planetary simulation scenes (Simulation_VR, Simulation_Flat).
    /// - Smooth 1-3s fade-out upon returning to MainMenu or leaving simulation.
    /// - Respects existing audio: zero interference with UI clicks, rovers, terrain, or VR interaction audio.
    /// - Does not create additional AudioListeners.
    /// - Prevents duplicate playback across scene loads via a persistent singleton pattern.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-95)]
    public class PlanetaryAmbientAudioController : MonoBehaviour
    {
        private static PlanetaryAmbientAudioController _instance;
        public static PlanetaryAmbientAudioController Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<PlanetaryAmbientAudioController>();
                    if (_instance == null)
                    {
                        var go = new GameObject("[PlanetaryAmbientAudioController]");
                        _instance = go.AddComponent<PlanetaryAmbientAudioController>();
                    }
                }
                return _instance;
            }
        }

        [Header("Audio Clip")]
        [Tooltip("The planetary ambient sound loop (extraterrestrial atmospheric drone).")]
        [SerializeField] private AudioClip ambientClip;

        [Header("Volume Configuration")]
        [Range(0.01f, 0.30f)]
        [Tooltip("Configurable target volume during active simulation (recommended: 0.05 - 0.15).")]
        [SerializeField] private float targetVolume = 0.08f;

        [Header("Fade Transitions")]
        [Range(0.5f, 6.0f)]
        [Tooltip("Smooth fade-in duration in seconds when entering simulation (default: 3.0s).")]
        [SerializeField] private float fadeInDuration = 3.0f;

        [Range(0.5f, 6.0f)]
        [Tooltip("Smooth fade-out duration in seconds when exiting simulation (default: 2.0s).")]
        [SerializeField] private float fadeOutDuration = 2.0f;

        [Header("Lifecycle Automation")]
        [Tooltip("Automatically manage fade-in/fade-out based on scene loads (MainMenu vs Simulation).")]
        [SerializeField] private bool autoHandleScenes = true;

        private AudioSource audioSource;
        private float currentDesiredVolume = 0f;
        private float currentFadeRate = 1f;
        private bool isFadingOut = false;

        public float TargetVolume
        {
            get => targetVolume;
            set => targetVolume = Mathf.Clamp(value, 0f, 1f);
        }

        public bool IsPlaying => audioSource != null && audioSource.isPlaying;
        public float CurrentVolume => audioSource != null ? audioSource.volume : 0f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoInitialize()
        {
            _ = Instance;
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);

            InitializeAudioSource();
            ResolveAudioClip();

            SceneManager.sceneLoaded += OnSceneLoaded;

            // Check current active scene state at startup
            string initialScene = SceneManager.GetActiveScene().name;
            if (IsSimulationScene(initialScene))
            {
                FadeInAmbience(fadeInDuration);
            }
            else
            {
                currentDesiredVolume = 0f;
                if (audioSource != null)
                {
                    audioSource.volume = 0f;
                }
            }
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                SceneManager.sceneLoaded -= OnSceneLoaded;
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
            audioSource.loop = true;
            // 2D non-directional playback: comfortable for VR headsets without head-rotation panning
            audioSource.spatialBlend = 0f;
            audioSource.bypassEffects = true;
            audioSource.bypassListenerEffects = true;
            audioSource.ignoreListenerPause = false; // Pauses cleanly if game/listener pauses
            audioSource.volume = 0f;
        }

        private void ResolveAudioClip()
        {
            if (ambientClip != null) return;

            // 1. Try loading from Resources runtime folder
            ambientClip = Resources.Load<AudioClip>("Audio/Ambient/planetary_ambience_loop");

#if UNITY_EDITOR
            // 2. Direct lookup from Assets/project/sfx/ambient/ in Unity Editor
            if (ambientClip == null)
            {
                ambientClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/project/sfx/ambient/planetary_ambience_loop.wav");
            }
#endif

            if (ambientClip != null && audioSource != null)
            {
                audioSource.clip = ambientClip;
            }
            else
            {
                Debug.LogWarning("[PlanetaryAmbientAudioController] Planetary ambient audio clip not yet assigned or resolved.");
            }
        }

        private void Update()
        {
            if (audioSource == null) return;

            // Ensure clip is assigned if it was resolved late
            if (audioSource.clip == null && ambientClip != null)
            {
                audioSource.clip = ambientClip;
            }

            // Smooth volume ramping using unscaled time
            if (Mathf.Abs(audioSource.volume - currentDesiredVolume) > 0.0005f)
            {
                float step = currentFadeRate * Time.unscaledDeltaTime;
                audioSource.volume = Mathf.MoveTowards(audioSource.volume, currentDesiredVolume, step);

                if (isFadingOut && audioSource.volume <= 0.001f)
                {
                    audioSource.volume = 0f;
                    audioSource.Stop();
                    isFadingOut = false;
                }
            }
            else if (isFadingOut && audioSource.volume <= 0.001f)
            {
                audioSource.volume = 0f;
                audioSource.Stop();
                isFadingOut = false;
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!autoHandleScenes) return;

            // Resolve clip if missing
            if (ambientClip == null || (audioSource != null && audioSource.clip == null))
            {
                ResolveAudioClip();
            }

            if (IsSimulationScene(scene.name))
            {
                // Enter simulation: start loop and smoothly fade in over 2-4 seconds
                FadeInAmbience(fadeInDuration);
            }
            else
            {
                // Exited simulation (e.g. MainMenu): fade out over 1-3 seconds and stop
                FadeOutAmbience(fadeOutDuration);
            }
        }

        private bool IsSimulationScene(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return false;

            // MainMenu is the startup UI screen (non-simulation)
            if (sceneName.Equals("MainMenu", System.StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Any simulation or testing scene
            return true;
        }

        /// <summary>
        /// Starts playback and smoothly fades volume in to targetVolume.
        /// </summary>
        public void FadeInAmbience(float duration = -1f)
        {
            if (audioSource == null) return;
            if (ambientClip == null)
            {
                ResolveAudioClip();
                if (ambientClip == null) return;
            }

            if (audioSource.clip != ambientClip)
            {
                audioSource.clip = ambientClip;
            }

            float fadeTime = duration > 0f ? duration : fadeInDuration;
            fadeTime = Mathf.Max(fadeTime, 0.1f);

            currentDesiredVolume = targetVolume;
            currentFadeRate = targetVolume / fadeTime;
            isFadingOut = false;

            if (!audioSource.isPlaying)
            {
                audioSource.Play();
            }
        }

        /// <summary>
        /// Smoothly fades volume out to 0 and stops playback.
        /// </summary>
        public void FadeOutAmbience(float duration = -1f)
        {
            if (audioSource == null || !audioSource.isPlaying) return;

            float fadeTime = duration > 0f ? duration : fadeOutDuration;
            fadeTime = Mathf.Max(fadeTime, 0.1f);

            currentDesiredVolume = 0f;
            currentFadeRate = Mathf.Max(audioSource.volume, 0.01f) / fadeTime;
            isFadingOut = true;
        }

        /// <summary>
        /// Instantly stops playback and resets volume.
        /// </summary>
        public void StopImmediate()
        {
            if (audioSource == null) return;
            audioSource.Stop();
            audioSource.volume = 0f;
            currentDesiredVolume = 0f;
            isFadingOut = false;
        }

        /// <summary>
        /// Adjust the active target volume at runtime.
        /// </summary>
        public void SetTargetVolume(float volume)
        {
            targetVolume = Mathf.Clamp(volume, 0f, 1f);
            if (!isFadingOut && audioSource != null && audioSource.isPlaying)
            {
                currentDesiredVolume = targetVolume;
                currentFadeRate = 0.1f; // Gentle transition to new setting
            }
        }
    }
}

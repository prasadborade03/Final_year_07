using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace ProjectName.VR
{
    /// <summary>
    /// Centralized VR Interaction Audio Controller for KALPANA.
    /// Provides subtle, professional audio feedback for XR interactions:
    /// - Object Grabbing (XRGrabInteractable selectEntered)
    /// - Object Releasing (XRGrabInteractable selectExited)
    /// - Object Hover Focus (XRBaseInteractable firstHoverEntered)
    /// - Control Activation (XRBaseInteractable activated)
    /// - Spatial Studio UI Recall & Toggle (FloatingUIRecallController B-button)
    /// - Teleportation Locomotion (TeleportationProvider)
    ///
    /// Preserves existing systems:
    /// - VR UI clicks are handled by UIClickAudio to prevent duplicate click sounds
    /// - Pure presentation layer; does not modify XR Origin, controllers, locomotion, physics, or rovers
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-90)]
    public class VRAudioFeedbackController : MonoBehaviour
    {
        private static VRAudioFeedbackController _instance;
        public static VRAudioFeedbackController Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<VRAudioFeedbackController>();
                    if (_instance == null)
                    {
                        var go = new GameObject("[VRAudioFeedbackController]");
                        _instance = go.AddComponent<VRAudioFeedbackController>();
                    }
                }
                return _instance;
            }
        }

        [Header("Audio Clips — VR Interactions")]
        public AudioClip grabClip;
        public AudioClip releaseClip;
        public AudioClip hoverClip;
        public AudioClip activateClip;
        public AudioClip recallToggleClip;
        public AudioClip teleportClip;

        [Header("Configurable Volume Settings (Subtle & Professional)")]
        [Range(0.02f, 0.40f)] public float grabVolume = 0.18f;
        [Range(0.02f, 0.40f)] public float releaseVolume = 0.16f;
        [Range(0.01f, 0.25f)] public float hoverVolume = 0.08f;
        [Range(0.02f, 0.40f)] public float activateVolume = 0.18f;
        [Range(0.02f, 0.35f)] public float recallVolume = 0.14f;
        [Range(0.02f, 0.35f)] public float teleportVolume = 0.12f;

        [Header("Hover Debouncing")]
        [Tooltip("Minimum time between hover sounds in seconds to prevent chatter")]
        public float hoverDebounceTime = 0.12f;

        private AudioSource audioSource2D;
        private AudioSource audioSource3D;
        private float lastHoverPlayTime = -1f;
        private bool lastUIVisibleState = true;

        // Tracking sets to prevent duplicate event subscriptions
        private readonly HashSet<IXRInteractable> registeredInteractables = new HashSet<IXRInteractable>();
        private XRInteractionManager cachedManager;
        private TeleportationProvider cachedTeleportProvider;

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

            InitializeAudioSources();
            LoadAudioClips();

            SceneManager.sceneLoaded += OnSceneLoaded;
            BindSceneInteractions();
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                SceneManager.sceneLoaded -= OnSceneLoaded;
                UnbindAllInteractions();
            }
        }

        private void InitializeAudioSources()
        {
            if (audioSource2D == null)
            {
                audioSource2D = gameObject.AddComponent<AudioSource>();
                audioSource2D.playOnAwake = false;
                audioSource2D.spatialBlend = 0f; // 2D non-spatial for player UI / HUD recall
                audioSource2D.bypassEffects = true;
                audioSource2D.ignoreListenerPause = true;
            }

            if (audioSource3D == null)
            {
                var childObj = new GameObject("VRAudio_3D");
                childObj.transform.SetParent(transform, false);
                audioSource3D = childObj.AddComponent<AudioSource>();
                audioSource3D.playOnAwake = false;
                audioSource3D.spatialBlend = 1.0f; // 3D spatialized for props and world objects
                audioSource3D.rolloffMode = AudioRolloffMode.Logarithmic;
                audioSource3D.minDistance = 0.3f;
                audioSource3D.maxDistance = 15.0f;
                audioSource3D.dopplerLevel = 0f;
            }
        }

        private void LoadAudioClips()
        {
            grabClip = LoadClip("vr_grab");
            releaseClip = LoadClip("vr_release");
            hoverClip = LoadClip("vr_hover");
            activateClip = LoadClip("vr_activate");
            recallToggleClip = LoadClip("vr_recall_toggle");
            teleportClip = LoadClip("vr_teleport");
        }

        private AudioClip LoadClip(string clipName)
        {
            var clip = Resources.Load<AudioClip>($"Audio/VR/{clipName}");
            if (clip != null) return clip;

#if UNITY_EDITOR
            clip = AssetDatabase.LoadAssetAtPath<AudioClip>($"Assets/project/sfx/vr/{clipName}.wav");
            if (clip != null) return clip;
#endif
            return null;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            BindSceneInteractions();
        }

        public void BindSceneInteractions()
        {
            UnbindAllInteractions();

            // 1. Hook into XRInteractionManager
            cachedManager = FindAnyObjectByType<XRInteractionManager>();
            if (cachedManager != null)
            {
                cachedManager.interactableRegistered += OnInteractableRegistered;
                cachedManager.interactableUnregistered += OnInteractableUnregistered;
            }

            // 2. Scan and bind existing interactables in the active scene
            var allInteractables = FindObjectsByType<XRBaseInteractable>(FindObjectsInactive.Include);
            foreach (var interactable in allInteractables)
            {
                RegisterInteractable(interactable);
            }

            // 3. Hook into TeleportationProvider
            cachedTeleportProvider = FindAnyObjectByType<TeleportationProvider>();
            if (cachedTeleportProvider != null)
            {
                cachedTeleportProvider.beginLocomotion += OnTeleportBegin;
            }

            // 4. Cache FloatingUIRecallController visibility state
            if (FloatingUIRecallController.Instance != null)
            {
                lastUIVisibleState = FloatingUIRecallController.Instance.IsUIVisible;
            }
        }

        private void UnbindAllInteractions()
        {
            if (cachedManager != null)
            {
                cachedManager.interactableRegistered -= OnInteractableRegistered;
                cachedManager.interactableUnregistered -= OnInteractableUnregistered;
                cachedManager = null;
            }

            if (cachedTeleportProvider != null)
            {
                cachedTeleportProvider.beginLocomotion -= OnTeleportBegin;
                cachedTeleportProvider = null;
            }

            foreach (var item in registeredInteractables)
            {
                if (item is XRBaseInteractable baseInteractable && baseInteractable != null)
                {
                    baseInteractable.selectEntered.RemoveListener(OnSelectEntered);
                    baseInteractable.selectExited.RemoveListener(OnSelectExited);
                    baseInteractable.firstHoverEntered.RemoveListener(OnFirstHoverEntered);
                    baseInteractable.activated.RemoveListener(OnActivated);
                }
            }
            registeredInteractables.Clear();
        }

        private void OnInteractableRegistered(InteractableRegisteredEventArgs args)
        {
            if (args.interactableObject != null)
            {
                RegisterInteractable(args.interactableObject);
            }
        }

        private void OnInteractableUnregistered(InteractableUnregisteredEventArgs args)
        {
            if (args.interactableObject != null)
            {
                UnregisterInteractable(args.interactableObject);
            }
        }

        private void RegisterInteractable(IXRInteractable interactable)
        {
            if (interactable == null || registeredInteractables.Contains(interactable)) return;

            if (interactable is XRBaseInteractable baseInteractable)
            {
                registeredInteractables.Add(interactable);
                baseInteractable.selectEntered.AddListener(OnSelectEntered);
                baseInteractable.selectExited.AddListener(OnSelectExited);
                baseInteractable.firstHoverEntered.AddListener(OnFirstHoverEntered);
                baseInteractable.activated.AddListener(OnActivated);
            }
        }

        private void UnregisterInteractable(IXRInteractable interactable)
        {
            if (interactable == null || !registeredInteractables.Contains(interactable)) return;

            if (interactable is XRBaseInteractable baseInteractable)
            {
                baseInteractable.selectEntered.RemoveListener(OnSelectEntered);
                baseInteractable.selectExited.RemoveListener(OnSelectExited);
                baseInteractable.firstHoverEntered.RemoveListener(OnFirstHoverEntered);
                baseInteractable.activated.RemoveListener(OnActivated);
            }
            registeredInteractables.Remove(interactable);
        }

        // =========================================================================
        // XR EVENT HANDLERS
        // =========================================================================

        private void OnSelectEntered(SelectEnterEventArgs args)
        {
            // Triggered upon successful grab
            Vector3 pos = args.interactableObject != null ? args.interactableObject.transform.position : transform.position;
            Play3DSound(grabClip, grabVolume, pos);
        }

        private void OnSelectExited(SelectExitEventArgs args)
        {
            // Triggered upon release
            Vector3 pos = args.interactableObject != null ? args.interactableObject.transform.position : transform.position;
            Play3DSound(releaseClip, releaseVolume, pos);
        }

        private void OnFirstHoverEntered(HoverEnterEventArgs args)
        {
            // Quiet initial focus cue with debouncing
            float now = Time.unscaledTime;
            if (lastHoverPlayTime > 0f && (now - lastHoverPlayTime) < hoverDebounceTime)
            {
                return;
            }
            lastHoverPlayTime = now;

            Vector3 pos = args.interactableObject != null ? args.interactableObject.transform.position : transform.position;
            Play3DSound(hoverClip, hoverVolume, pos);
        }

        private void OnActivated(ActivateEventArgs args)
        {
            // Trigger squeezed while holding interactable
            Vector3 pos = args.interactableObject != null ? args.interactableObject.transform.position : transform.position;
            Play3DSound(activateClip, activateVolume, pos);
        }

        private void OnTeleportBegin(LocomotionSystem system)
        {
            Play2DSound(teleportClip, teleportVolume);
        }

        private void Update()
        {
            // Monitor FloatingUIRecallController toggle state (B button on Right Controller)
            if (FloatingUIRecallController.Instance != null)
            {
                bool currentVisibility = FloatingUIRecallController.Instance.IsUIVisible;
                if (currentVisibility != lastUIVisibleState)
                {
                    lastUIVisibleState = currentVisibility;
                    Play2DSound(recallToggleClip, recallVolume);
                }
            }
        }

        // =========================================================================
        // PLAYBACK HELPERS
        // =========================================================================

        public void Play3DSound(AudioClip clip, float volume, Vector3 position)
        {
            if (clip == null) return;

            if (audioSource3D != null)
            {
                audioSource3D.transform.position = position;
                audioSource3D.pitch = UnityEngine.Random.Range(0.96f, 1.04f); // Subtle organic variation
                audioSource3D.PlayOneShot(clip, Mathf.Clamp01(volume));
            }
            else
            {
                AudioSource.PlayClipAtPoint(clip, position, Mathf.Clamp01(volume));
            }
        }

        public void Play2DSound(AudioClip clip, float volume)
        {
            if (clip == null || audioSource2D == null) return;
            audioSource2D.pitch = 1.0f;
            audioSource2D.PlayOneShot(clip, Mathf.Clamp01(volume));
        }
    }
}

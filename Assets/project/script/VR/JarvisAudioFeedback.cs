using System;
using UnityEngine;

namespace ProjectName.VR
{
    /// <summary>
    /// Jarvis Audio Feedback Synthesizer.
    /// Generates high-tech procedural audio cues (suit boot, perspective switch, rover focus, boost)
    /// entirely in code using AudioClip.Create without requiring external WAV/MP3 files.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class JarvisAudioFeedback : MonoBehaviour
    {
        public static JarvisAudioFeedback Instance { get; private set; }

        private AudioSource audioSource;
        private AudioClip bootClip;
        private AudioClip switchClip;
        private AudioClip focusClip;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;

            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
            }

            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f; // 2D Head-mounted suit sound
            audioSource.volume = 0.45f;

            // Generate procedural audio clips
            bootClip = CreateToneSequence(new float[] { 523.25f, 659.25f, 783.99f, 1046.50f }, 0.08f, 0.4f); // C5, E5, G5, C6 (boot chord)
            switchClip = CreateToneSequence(new float[] { 880f, 1318.5f }, 0.045f, 0.3f); // A5, E6 (quick high-tech chirp)
            focusClip = CreateToneSequence(new float[] { 1200f, 1600f, 2000f }, 0.04f, 0.35f); // Ping
        }

        private void Start()
        {
            // Play initial boot chime after small delay
            Invoke(nameof(PlayBoot), 0.5f);
        }

        public void PlayBoot()
        {
            if (audioSource != null && bootClip != null)
            {
                audioSource.PlayOneShot(bootClip, 0.5f);
            }
        }

        public void PlayPerspectiveSwitch()
        {
            if (audioSource != null && switchClip != null)
            {
                audioSource.PlayOneShot(switchClip, 0.4f);
            }
        }

        public void PlayRoverFocus()
        {
            if (audioSource != null && focusClip != null)
            {
                audioSource.PlayOneShot(focusClip, 0.45f);
            }
        }

        private static AudioClip CreateToneSequence(float[] freqs, float durationPerTone, float maxAmp)
        {
            int sampleRate = 44100;
            int totalSamples = (int)(sampleRate * durationPerTone * freqs.Length);
            float[] samples = new float[totalSamples];

            int samplesPerTone = (int)(sampleRate * durationPerTone);

            for (int t = 0; t < freqs.Length; t++)
            {
                float freq = freqs[t];
                int offset = t * samplesPerTone;

                for (int i = 0; i < samplesPerTone; i++)
                {
                    float time = (float)i / sampleRate;
                    // Envelope: fast attack, exponential decay
                    float env = Mathf.Exp(-time * 18f);
                    float sin = Mathf.Sin(2f * Mathf.PI * freq * time);
                    samples[offset + i] = sin * env * maxAmp;
                }
            }

            AudioClip clip = AudioClip.Create("JarvisSynth_" + freqs[0], totalSamples, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}

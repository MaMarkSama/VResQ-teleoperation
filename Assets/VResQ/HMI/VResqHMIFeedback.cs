using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR;

namespace VResQ.HMI
{
    /// <summary>
    /// Human-Machine Interface (HMI) tactile feedback hub for V-ResQ VR & Desktop Cockpit.
    /// Provides procedural tactical audio chimes, OpenXR controller haptic impulses,
    /// Gamepad rumble pulses, animated diegetic command confirmation toasts,
    /// and automatic tactile hover/press FX attachment on all interactive HUD buttons.
    /// </summary>
    public class VResqHMIFeedback : MonoBehaviour
    {
        public static VResqHMIFeedback Instance { get; private set; }

        [Header("Feedback Settings")]
        public bool enableSound = true;
        public bool enableHaptics = true;
        [Range(0f, 1f)] public float masterVolume = 0.42f;

        [Header("Diegetic HMI Toast Banner (Optional)")]
        public GameObject toastRoot;
        public Image toastBackground;
        public Image toastAccentBar;
        public TextMeshProUGUI toastTitleText;
        public TextMeshProUGUI toastDetailText;
        public CanvasGroup toastCanvasGroup;

        private AudioSource _audioSource;
        private AudioClip _clipHover;
        private AudioClip _clipConfirm;
        private AudioClip _clipArm;
        private AudioClip _clipDisarm;
        private AudioClip _clipGesture;
        private AudioClip _clipWarning;

        private float _toastHideAt = -1f;
        private float _toastAlpha = 0f;
        private float _gamepadRumbleStopAt = -1f;
        private readonly List<UnityEngine.XR.InputDevice> _xrDevices = new List<UnityEngine.XR.InputDevice>();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;

            if (Application.isPlaying)
            {
                SetupProceduralAudio();
            }

            if (toastCanvasGroup != null)
            {
                toastCanvasGroup.alpha = 0f;
            }
            if (toastRoot != null)
            {
                toastRoot.SetActive(false);
            }
        }

        private void Start()
        {
            AttachButtonEffectsUnder(transform);
        }

        private void OnDisable()
        {
            StopGamepadRumble();
        }

        private void OnDestroy()
        {
            StopGamepadRumble();
            if (Instance == this) Instance = null;
        }

        public void AttachButtonEffectsUnder(Transform root)
        {
            if (root == null) return;
            var buttons = root.GetComponentsInChildren<Button>(true);
            for (int i = 0; i < buttons.Length; i++)
            {
                var btn = buttons[i];
                if (btn == null) continue;
                if (btn.GetComponent<VResqHMIButtonFX>() == null)
                {
                    btn.gameObject.AddComponent<VResqHMIButtonFX>();
                }
            }
        }

        private void Update()
        {
            if (_gamepadRumbleStopAt > 0f && Time.unscaledTime >= _gamepadRumbleStopAt)
            {
                StopGamepadRumble();
            }

            if (toastRoot != null && toastCanvasGroup != null)
            {
                bool shouldShow = Time.unscaledTime < _toastHideAt;
                float targetAlpha = shouldShow ? 1f : 0f;
                float speed = shouldShow ? 12f : 5.5f;
                _toastAlpha = Mathf.MoveTowards(_toastAlpha, targetAlpha, Time.unscaledDeltaTime * speed);
                toastCanvasGroup.alpha = _toastAlpha;
                if (_toastAlpha <= 0.01f && toastRoot.activeSelf && !shouldShow)
                {
                    toastRoot.SetActive(false);
                }
            }
        }

        public void ShowToast(string title, string detail, Color accent, float duration = 2.6f)
        {
            if (toastRoot == null) return;
            toastRoot.SetActive(true);
            if (toastTitleText != null)
            {
                toastTitleText.text = title;
                toastTitleText.color = accent;
            }
            if (toastDetailText != null)
            {
                toastDetailText.text = detail ?? "";
            }
            if (toastAccentBar != null)
            {
                toastAccentBar.color = accent;
            }
            if (toastBackground != null)
            {
                toastBackground.color = new Color(
                    Mathf.Lerp(0.015f, accent.r, 0.14f),
                    Mathf.Lerp(0.045f, accent.g, 0.14f),
                    Mathf.Lerp(0.085f, accent.b, 0.14f),
                    0.94f);
            }
            _toastHideAt = Time.unscaledTime + Mathf.Max(0.8f, duration);
        }

        public void PlayHoverTick()
        {
            PlayClip(_clipHover, 0.30f);
            PulseHaptics(0.06f, 0.018f, left: true, right: true, includeGamepad: false);
        }

        public void PlayClickConfirm()
        {
            PlayClip(_clipConfirm, 0.65f);
            PulseHaptics(0.24f, 0.045f, left: true, right: true, includeGamepad: true);
        }

        public void PlayArmChime()
        {
            PlayClip(_clipArm, 0.90f);
            PulseHaptics(0.55f, 0.14f, left: true, right: true, includeGamepad: true);
        }

        public void PlayDisarmChime()
        {
            PlayClip(_clipDisarm, 0.75f);
            PulseHaptics(0.28f, 0.08f, left: true, right: true, includeGamepad: true);
        }

        public void PlayGestureLock()
        {
            PlayClip(_clipGesture, 0.85f);
            PulseHaptics(0.45f, 0.10f, left: true, right: true, includeGamepad: true);
        }

        public void PlayWarningPulse()
        {
            PlayClip(_clipWarning, 1.0f);
            PulseHaptics(0.85f, 0.22f, left: true, right: true, includeGamepad: true);
        }

        public void PulseHaptics(float intensity, float duration, bool left = true, bool right = true, bool includeGamepad = true)
        {
            if (!enableHaptics || !Application.isPlaying) return;
            float amp = Mathf.Clamp01(intensity);
            float dur = Mathf.Clamp(duration, 0.01f, 0.5f);

            // 1. OpenXR / Meta Quest Hand Controllers
            try
            {
                _xrDevices.Clear();
                InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Controller, _xrDevices);
                for (int i = 0; i < _xrDevices.Count; i++)
                {
                    var dev = _xrDevices[i];
                    if (!dev.isValid) continue;
                    bool isLeft = (dev.characteristics & InputDeviceCharacteristics.Left) != 0;
                    bool isRight = (dev.characteristics & InputDeviceCharacteristics.Right) != 0;
                    if ((isLeft && !left) || (isRight && !right)) continue;
                    if (dev.TryGetHapticCapabilities(out HapticCapabilities caps) && caps.supportsImpulse)
                    {
                        dev.SendHapticImpulse(0u, amp, dur);
                    }
                }
            }
            catch { }

            // 2. Standard Gamepad Rumble (Xbox / PlayStation / Switch / XInput)
            if (includeGamepad && Gamepad.current != null)
            {
                try
                {
                    Gamepad.current.SetMotorSpeeds(amp * 0.55f, amp * 0.85f);
                    _gamepadRumbleStopAt = Time.unscaledTime + dur;
                }
                catch { }
            }
        }

        private void StopGamepadRumble()
        {
            _gamepadRumbleStopAt = -1f;
            if (Gamepad.current != null)
            {
                try { Gamepad.current.SetMotorSpeeds(0f, 0f); } catch { }
            }
        }

        private void PlayClip(AudioClip clip, float relativeVolume)
        {
            if (!enableSound || !Application.isPlaying || clip == null || _audioSource == null) return;
            _audioSource.PlayOneShot(clip, Mathf.Clamp01(masterVolume * relativeVolume));
        }

        private void SetupProceduralAudio()
        {
            _audioSource = GetComponent<AudioSource>();
            if (_audioSource == null) _audioSource = gameObject.AddComponent<AudioSource>();
            _audioSource.playOnAwake = false;
            _audioSource.loop = false;
            _audioSource.spatialBlend = 0f;

            _clipHover = BuildSweepClip("HMI_Hover", 1380f, 1560f, 0.018f, 0.25f);
            _clipConfirm = BuildSweepClip("HMI_Confirm", 880f, 1320f, 0.068f, 0.40f);
            _clipArm = BuildNotesClip("HMI_Arm", new[] { 440f, 659.25f, 880f }, 0.065f, 0.45f);
            _clipDisarm = BuildNotesClip("HMI_Disarm", new[] { 659.25f, 392f }, 0.075f, 0.42f);
            _clipGesture = BuildNotesClip("HMI_Gesture", new[] { 1046.5f, 1567.98f }, 0.060f, 0.42f);
            _clipWarning = BuildNotesClip("HMI_Warning", new[] { 920f, 0f, 920f }, 0.085f, 0.52f);
        }

        private static AudioClip BuildSweepClip(string clipName, float f0, float f1, float duration, float amplitude)
        {
            const int sampleRate = 22050;
            int count = Mathf.Max(64, Mathf.RoundToInt(sampleRate * duration));
            float[] samples = new float[count];
            float phase = 0f;
            for (int i = 0; i < count; i++)
            {
                float t = (float)i / count;
                float freq = Mathf.Lerp(f0, f1, t);
                float env = Mathf.Sin(t * Mathf.PI);
                phase += (2f * Mathf.PI * freq) / sampleRate;
                samples[i] = Mathf.Sin(phase) * env * amplitude;
            }
            var clip = AudioClip.Create(clipName, count, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private static AudioClip BuildNotesClip(string clipName, float[] freqs, float noteDuration, float amplitude)
        {
            const int sampleRate = 22050;
            int perNote = Mathf.Max(64, Mathf.RoundToInt(sampleRate * noteDuration));
            float[] samples = new float[perNote * freqs.Length];
            for (int n = 0; n < freqs.Length; n++)
            {
                float freq = freqs[n];
                float phase = 0f;
                int offset = n * perNote;
                for (int i = 0; i < perNote; i++)
                {
                    if (freq <= 1f) { samples[offset + i] = 0f; continue; }
                    float t = (float)i / perNote;
                    float env = Mathf.Sin(Mathf.Min(1f, t * 6f) * Mathf.PI * 0.5f) * (1f - t * 0.65f);
                    phase += (2f * Mathf.PI * freq) / sampleRate;
                    samples[offset + i] = (Mathf.Sin(phase) + 0.18f * Mathf.Sin(phase * 2f)) * env * amplitude;
                }
            }
            var clip = AudioClip.Create(clipName, samples.Length, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}

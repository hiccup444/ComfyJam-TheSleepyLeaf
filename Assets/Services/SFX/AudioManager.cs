using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using JamesKJamKit.Services.Audio;

namespace JamesKJamKit.Services
{
    /// <summary>
    /// Handles playing sound effects, UI clicks and lightweight looping ambience.
    /// </summary>
    [DefaultExecutionOrder(-9995)]
    public sealed class AudioManager : MonoBehaviour
    {
        private const string MasterParam = "MASTER_VOL";
        private const string SfxParam    = "SFX_VOL";

        [Header("Mixer Routing")]
        [SerializeField] private AudioMixer mixer;
        [SerializeField] private AudioMixerGroup sfxGroup;

        [Header("UI Click (fallbacks)")]
        [SerializeField] private SFXEvent uiClickEvent; // Preferred
        [SerializeField] private AudioClip uiClickClip; // Fallback if event not assigned

        [Header("Pool")]
        [SerializeField, Min(1)] private int initialPoolSize = 8;

        public static AudioManager Instance { get; private set; }

        private readonly List<AudioSource> _sources = new();
        private bool _missingMasterParamLogged;
        private bool _missingSfxParamLogged;

        // cache AudioListener to avoid FindAnyObjectByType on every SFX play
        private AudioListener _cachedListener;
        private Camera _cachedMainCamera;
        private float _lastListenerCacheTime;
        private const float ListenerCacheTimeout = 0.5f; // refresh cache every 0.5 seconds

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            CreatePool(initialPoolSize);
            ApplyVolumes();
        }

        private void OnEnable()
        {
            if (SaveSettings.Instance != null)
            {
                SaveSettings.Instance.OnSettingsChanged += HandleSettingsChanged;
            }
        }

        private void OnDisable()
        {
            if (SaveSettings.Instance != null)
            {
                SaveSettings.Instance.OnSettingsChanged -= HandleSettingsChanged;
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // --------------------------
        // Public API (clips)
        // --------------------------

        /// <summary>Plays a one-shot 2D clip (UI style). Use SFXEvent API for spatial or randomized.</summary>
        public void PlayOneShot(AudioClip clip, float volume = 1f)
        {
            if (clip == null) return;

            var source = GetFreeSource();
            ResetSource(source);

            source.loop = false;
            source.spatialBlend = 0f; // UI/2D
            source.PlayOneShot(clip, Mathf.Clamp01(volume));
        }

        /// <summary>Starts a lightweight loop; set spatial=true to make it 3D.</summary>
        public AudioSource PlayLoop(AudioClip clip, bool spatial = false)
        {
            if (clip == null) return null;

            var source = GetFreeSource();
            ResetSource(source);

            source.loop = true;
            source.clip = clip;
            source.spatialBlend = spatial ? 1f : 0f;
            source.dopplerLevel = 0f;
            source.Play();
            return source;
        }

        /// <summary>Stops and clears a loop created by PlayLoop.</summary>
        public void StopLoop(AudioSource loopSource)
        {
            if (loopSource == null) return;
            loopSource.Stop();
            loopSource.clip = null;
            loopSource.loop = false;
        }

        /// <summary>Convenience for UI click using SFXEvent if available, else AudioClip.</summary>
        public void PlayUiClick()
        {
            if (uiClickEvent != null)
            {
                PlaySFX(uiClickEvent);
            }
            else
            {
                PlayOneShot(uiClickClip);
            }
        }

        // --------------------------
        // Public API (SFXEvent)
        // --------------------------

        /// <summary>Plays an SFX event at the active listener (2D-feel if spatialBlend = 0).</summary>
        public void PlaySFX(SFXEvent evt)
        {
            PlaySFX(evt, (Vector3?)null);
        }

        /// <summary>Plays an SFX event anchored at a Transform's position (use for diegetic sounds).</summary>
        public void PlaySFX(SFXEvent evt, Transform at)
        {
            if (at == null) { PlaySFX(evt); return; }
            PlaySFX(evt, (Vector3?)at.position);
        }

        /// <summary>Plays an SFX event at a world position (null = listener position).</summary>
        public void PlaySFX(SFXEvent evt, Vector3? worldPos)
        {
            if (evt == null) return;

            var clip = evt.GetRandomClip();
            if (clip == null) return;

            var src = GetFreeSource();
            ResetSource(src);

            // Route & base settings
            src.outputAudioMixerGroup = sfxGroup;
            src.pitch        = Random.Range(evt.pitchRange.x, evt.pitchRange.y);
            src.spatialBlend = Mathf.Clamp01(evt.spatialBlend);
            src.dopplerLevel = Mathf.Max(0f, evt.dopplerLevel);

            // 3D params (harmless even if spatialBlend = 0)
            src.rolloffMode = evt.rolloff;
            src.minDistance = Mathf.Max(0.01f, evt.minDistance);
            src.maxDistance = Mathf.Max(src.minDistance + 0.01f, evt.maxDistance);

            // Position near listener or at provided world pos
            src.transform.position = worldPos ?? GetListenerPosition();

            if (evt.loop)
            {
                src.clip = clip;
                src.loop = true;
                src.volume = Mathf.Clamp01(evt.volume);
                src.Play();
            }
            else
            {
                src.loop = false;
                src.PlayOneShot(clip, Mathf.Clamp01(evt.volume));
            }
        }

        // --------------------------
        // Settings
        // --------------------------

        private void HandleSettingsChanged(SettingKind kind)
        {
            if (kind is SettingKind.MasterVolume or SettingKind.SfxVolume)
            {
                ApplyVolumes();
            }
        }

        private void ApplyVolumes()
        {
            if (mixer == null || SaveSettings.Instance == null) return;

            var data = SaveSettings.Instance.Data;
            if (!string.IsNullOrEmpty(MasterParam))
            {
                if (mixer.SetFloat(MasterParam, data.masterDb))
                {
                    _missingMasterParamLogged = false;
                }
                else if (!_missingMasterParamLogged)
                {
                    #if UNITY_EDITOR
                    Debug.LogWarning($"[AudioManager] Mixer missing exposed parameter '{MasterParam}'. Volume will use default value.", mixer);
                    #endif
                    _missingMasterParamLogged = true;
                }
            }

            if (!string.IsNullOrEmpty(SfxParam))
            {
                if (mixer.SetFloat(SfxParam, data.sfxDb))
                {
                    _missingSfxParamLogged = false;
                }
                else if (!_missingSfxParamLogged)
                {
                    #if UNITY_EDITOR
                    Debug.LogWarning($"[AudioManager] Mixer missing exposed parameter '{SfxParam}'. Volume will use default value.", mixer);
                    #endif
                    _missingSfxParamLogged = true;
                }
            }
        }

        // --------------------------
        // Pooling
        // --------------------------

        private void CreatePool(int size)
        {
            for (int i = 0; i < size; i++)
            {
                var source = CreateSource();
                _sources.Add(source);
            }
        }

        private AudioSource GetFreeSource()
        {
            for (int i = 0; i < _sources.Count; i++)
            {
                if (!_sources[i].isPlaying) return _sources[i];
            }

            var newSource = CreateSource();
            _sources.Add(newSource);
            return newSource;
        }

        private AudioSource CreateSource()
        {
            var go = new GameObject("SFX Source");
            go.transform.SetParent(transform, false);

            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.outputAudioMixerGroup = sfxGroup;
            source.dopplerLevel = 0f; // indoor/UI default
            return source;
        }

        private void ResetSource(AudioSource s)
        {
            s.Stop();
            s.clip = null;
            s.loop = false;
            s.pitch = 1f;
            s.volume = 1f;
            s.spatialBlend = 0f;
            s.rolloffMode = AudioRolloffMode.Logarithmic;
            s.minDistance = 1f;
            s.maxDistance = 10f;
            s.dopplerLevel = 0f;
        }

        // --------------------------
        // Listener helpers
        // --------------------------

        private Vector3 GetListenerPosition()
        {
            // Optimization: Cache listener to avoid expensive FindAnyObjectByType on every SFX play
            float currentTime = Time.unscaledTime;

            // Refresh cache periodically or if null
            if (_cachedListener == null || currentTime - _lastListenerCacheTime > ListenerCacheTimeout)
            {
                _cachedListener = FindAnyObjectByType<AudioListener>();
                _cachedMainCamera = Camera.main;
                _lastListenerCacheTime = currentTime;
            }

            // Try cached AudioListener first
            if (_cachedListener != null && _cachedListener.isActiveAndEnabled)
                return _cachedListener.transform.position;

            // Fallback to cached main camera if it exists
            if (_cachedMainCamera != null)
                return _cachedMainCamera.transform.position;

            // Last resort
            return Vector3.zero;
        }
    }
}

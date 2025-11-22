using UnityEngine;
using JamesKJamKit.Services;
using JamesKJamKit.Services.Audio;

public interface IDispenserAudio
{
    void HandleButtonPressed(WaterTemp temp);
    void HandlePourStarted();
    void HandlePourCompleted();
    void HandlePourCancelled();
    void HandlePourStopped();
}

/// <summary>
/// Optional audio bridge for the water dispenser. All fields are optional and safe for reuse.
/// </summary>
[DisallowMultipleComponent]
public sealed class DispenserAudio : MonoBehaviour, IDispenserAudio
{
    [Header("Clips")]
    [SerializeField] private AudioClip hotButtonClip;
    [SerializeField] private AudioClip coldButtonClip;
    [Header("Pour Events")]
    [SerializeField] private PourEvents coldPourEvents;
    [SerializeField] private PourEvents hotPourEvents;

    [Header("Settings")]
    [SerializeField] private bool useAudioManager = true;
    [Header("Spatial Blend")]
    [SerializeField, Range(0f, 1f)] private float pourSpatialBlend = 1f;
    [SerializeField, Range(0f, 1f)] private float buttonSpatialBlend = 0f;
    [SerializeField, Min(0f)] private float buttonDebounceSeconds = 0.08f;

    [Header("References")]
    [SerializeField] private DispenserController controller;
    [SerializeField] private AudioSource loopSource;
    [SerializeField] private AudioSource loopLayerSource;

    private float _lastHotPressedTime = -10f;
    private float _lastColdPressedTime = -10f;
    private bool _loopActive;
    private AudioSource _fallback2DSource;
    private WaterTemp _currentTemp = WaterTemp.Cold;
    private SFXEvent _activeLoopEvent;
    private SFXEvent _activeLayerLoopEvent;

    [System.Serializable]
    private struct PourEvents
    {
        public SFXEvent start;
        public SFXEvent loop;
        public SFXEvent end;
        [Tooltip("Optional event that loops alongside the main loop for extra layers (e.g. steam).")]
        public SFXEvent loopLayer;
    }

    void Reset()
    {
        if (!controller) controller = GetComponent<DispenserController>();
        if (!loopSource) loopSource = GetComponent<AudioSource>();
        ConfigureLoopSource();
        ConfigureLoopLayerSource();
    }

    void Awake()
    {
        ConfigureLoopSource();
        ConfigureLoopLayerSource();
    }

    void OnEnable()
    {
        if (controller != null)
        {
            controller.OnFillStarted.AddListener(HandlePourStarted);
            controller.OnFillCompleted.AddListener(HandlePourCompleted);
            controller.OnFillCancelled.AddListener(HandlePourCancelled);
        }
    }

    void OnDisable()
    {
        if (controller != null)
        {
            controller.OnFillStarted.RemoveListener(HandlePourStarted);
            controller.OnFillCompleted.RemoveListener(HandlePourCompleted);
            controller.OnFillCancelled.RemoveListener(HandlePourCancelled);
        }

        StopLoopImmediate();
        _loopActive = false;
    }

    public void HandleButtonPressed(WaterTemp temp)
    {
        if (!isActiveAndEnabled) return;

        var now = Time.unscaledTime;
        switch (temp)
        {
            case WaterTemp.Hot:
                if (now - _lastHotPressedTime < buttonDebounceSeconds) return;
                _lastHotPressedTime = now;
                PlayButtonOneShot(hotButtonClip);
                break;
            case WaterTemp.Cold:
                if (now - _lastColdPressedTime < buttonDebounceSeconds) return;
                _lastColdPressedTime = now;
                PlayButtonOneShot(coldButtonClip);
                break;
        }
    }

    public void HandlePourStarted()
    {
        if (!isActiveAndEnabled) return;
        if (_loopActive) return;

        _currentTemp = controller != null ? controller.LastFillTemp : WaterTemp.Cold;
        var events = GetPourEvents(_currentTemp);
        _loopActive = true;
        PlayPourEvent(events.start);
        StartLoop(events);
    }

    public void HandlePourCompleted()
    {
        HandlePourStoppedInternal(playEndTick: true);
    }

    public void HandlePourCancelled()
    {
        HandlePourStoppedInternal(playEndTick: true);
    }

    public void HandlePourStopped()
    {
        HandlePourStoppedInternal(playEndTick: false);
    }

    private void HandlePourStoppedInternal(bool playEndTick)
    {
        if (!_loopActive && !IsAnyLoopPlaying())
        {
            if (playEndTick)
            {
                var events = GetPourEvents(_currentTemp);
                PlayPourEvent(events.end);
            }
            return;
        }

        _loopActive = false;
        StopLoopImmediate();

        if (playEndTick)
        {
            var events = GetPourEvents(_currentTemp);
            PlayPourEvent(events.end);
        }
    }

    private void StartLoop(PourEvents events)
    {
        StartPrimaryLoop(events.loop);
        StartLayerLoop(events.loopLayer);
    }

    private void StartPrimaryLoop(SFXEvent evt)
    {
        if (loopSource == null)
        {
            _activeLoopEvent = null;
            return;
        }

        if (evt == null)
        {
            StopPrimaryLoop();
            return;
        }

        if (_activeLoopEvent == evt && loopSource.isPlaying) return;

        var clip = evt.GetRandomClip();
        if (clip == null)
        {
            StopPrimaryLoop();
            return;
        }

        loopSource.Stop();
        ConfigureSourceForEvent(loopSource, evt, asLoop: true);
        loopSource.clip = clip;
        loopSource.Play();
        _activeLoopEvent = evt;
    }

    private void StartLayerLoop(SFXEvent evt)
    {
        if (evt == null)
        {
            StopLayerLoopImmediate();
            return;
        }

        var src = EnsureLoopLayerSource();
        if (src == null) return;

        if (_activeLayerLoopEvent == evt && src.isPlaying) return;

        var clip = evt.GetRandomClip();
        if (clip == null)
        {
            StopLayerLoopImmediate();
            return;
        }

        src.Stop();
        ConfigureSourceForEvent(src, evt, asLoop: true);
        src.clip = clip;
        src.Play();
        _activeLayerLoopEvent = evt;
    }

    private void StopLoopImmediate()
    {
        StopPrimaryLoop();
        StopLayerLoopImmediate();
    }

    private void StopPrimaryLoop()
    {
        if (loopSource == null)
        {
            _activeLoopEvent = null;
            return;
        }
        loopSource.Stop();
        loopSource.loop = true;
        loopSource.clip = null;
        _activeLoopEvent = null;
    }

    private void StopLayerLoopImmediate()
    {
        if (loopLayerSource == null)
        {
            _activeLayerLoopEvent = null;
            return;
        }
        loopLayerSource.Stop();
        loopLayerSource.loop = true;
        loopLayerSource.clip = null;
        _activeLayerLoopEvent = null;
    }

    private void PlayButtonOneShot(AudioClip clip)
    {
        if (clip == null) return;

        if (useAudioManager && AudioManager.Instance != null)
        {
            AudioManager.Instance.PlayOneShot(clip);
            return;
        }

        var src = Get2DOrFallbackSource();
        if (src == null) return;
        src.spatialBlend = buttonSpatialBlend;
        src.PlayOneShot(clip);
    }

    private void PlayPourEvent(SFXEvent evt)
    {
        if (evt == null) return;

        if (useAudioManager && AudioManager.Instance != null)
        {
            AudioManager.Instance.PlaySFX(evt, transform);
            return;
        }

        var clip = evt.GetRandomClip();
        if (clip == null) return;

        var src = loopSource != null ? loopSource : Get2DOrFallbackSource();
        if (src == null) return;

        ConfigureSourceForEvent(src, evt, asLoop: false);
        src.PlayOneShot(clip);
    }

    private AudioSource Get2DOrFallbackSource()
    {
        if (_fallback2DSource == null)
        {
            _fallback2DSource = gameObject.AddComponent<AudioSource>();
            _fallback2DSource.playOnAwake = false;
            _fallback2DSource.loop = false;
            _fallback2DSource.spatialBlend = buttonSpatialBlend;

            if (loopSource != null)
            {
                _fallback2DSource.outputAudioMixerGroup = loopSource.outputAudioMixerGroup;
            }
        }

        return _fallback2DSource;
    }

    private void ConfigureSourceForEvent(AudioSource source, SFXEvent evt, bool asLoop)
    {
        if (source == null) return;

        source.playOnAwake = false;
        source.loop = asLoop && (evt == null || evt.loop);

        var targetSpatial = evt != null ? evt.spatialBlend : pourSpatialBlend;
        var targetVolume = evt != null ? Mathf.Clamp01(evt.volume) : 1f;
        var minPitch = evt != null ? Mathf.Min(evt.pitchRange.x, evt.pitchRange.y) : 1f;
        var maxPitch = evt != null ? Mathf.Max(evt.pitchRange.x, evt.pitchRange.y) : 1f;
        var rolloff = evt != null ? evt.rolloff : AudioRolloffMode.Logarithmic;
        var minDistance = evt != null ? evt.minDistance : 1f;
        var maxDistance = evt != null ? evt.maxDistance : 10f;
        var doppler = evt != null ? evt.dopplerLevel : 0f;

        source.pitch = Random.Range(minPitch, maxPitch);
        source.volume = targetVolume;
        source.spatialBlend = targetSpatial;
        source.rolloffMode = rolloff;
        source.minDistance = minDistance;
        source.maxDistance = maxDistance;
        source.dopplerLevel = doppler;
    }

    private void ConfigureLoopSource()
    {
        if (loopSource == null) return;
        loopSource.playOnAwake = false;
        loopSource.loop = true;
        loopSource.spatialBlend = pourSpatialBlend;
        loopSource.dopplerLevel = 0f;
    }

    private void ConfigureLoopLayerSource()
    {
        if (loopLayerSource == null) return;
        loopLayerSource.playOnAwake = false;
        loopLayerSource.loop = true;
        loopLayerSource.spatialBlend = pourSpatialBlend;
        loopLayerSource.dopplerLevel = 0f;

        if (loopSource != null && loopLayerSource.outputAudioMixerGroup == null)
        {
            loopLayerSource.outputAudioMixerGroup = loopSource.outputAudioMixerGroup;
        }
    }

    private AudioSource EnsureLoopLayerSource()
    {
        if (loopLayerSource == null)
        {
            loopLayerSource = gameObject.AddComponent<AudioSource>();
        }

        if (loopSource != null && loopLayerSource.outputAudioMixerGroup == null)
        {
            loopLayerSource.outputAudioMixerGroup = loopSource.outputAudioMixerGroup;
        }

        ConfigureLoopLayerSource();
        return loopLayerSource;
    }

    private bool IsAnyLoopPlaying()
    {
        var primaryPlaying = loopSource != null && loopSource.isPlaying;
        var layerPlaying = loopLayerSource != null && loopLayerSource.isPlaying;
        return primaryPlaying || layerPlaying;
    }

    private PourEvents GetPourEvents(WaterTemp temp) => temp == WaterTemp.Hot ? hotPourEvents : coldPourEvents;
}

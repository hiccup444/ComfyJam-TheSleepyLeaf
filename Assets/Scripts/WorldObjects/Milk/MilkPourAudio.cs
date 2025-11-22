using UnityEngine;
using JamesKJamKit.Services.Audio;

/// <summary>
/// Plays an SFXEvent-driven loop while milk is pouring.
/// Subscribes to LiquidStream.OnPourStart/OnPourStop to control playback.
/// Attach to the same GameObject that has LiquidStream/MilkTiltVisual.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(AudioSource))]
public sealed class MilkPourAudio : MonoBehaviour
{
    [Header("SFX Event (Loop)")]
    [Tooltip("Clip(s) to loop while pouring.")]
    [SerializeField] private SFXEvent sfxPourLoop;

    [Header("Variation")]
    [SerializeField] private Vector2 pitchJitter = new Vector2(0.98f, 1.02f);
    [SerializeField] private Vector2 volumeJitter = new Vector2(0.95f, 1.00f);

    [Header("Output")]
    [Tooltip("0 = fully 2D, 1 = fully 3D. Applied when starting loop.")]
    [Range(0f, 1f)]
    [SerializeField] private float spatialBlend = 0f;

    [Header("Fade")]
    [Tooltip("Seconds to fade out when pour stops (0 = hard stop)")]
    [SerializeField] private float stopFadeSeconds = 0.08f;

    private AudioSource _source;
    private LiquidStream _stream;
    private float _originalPitch;
    private float _targetVolume = 1f;
    private Coroutine _fadeRoutine;

    private void Awake()
    {
        _source = GetComponent<AudioSource>();
        if (_source == null) _source = gameObject.AddComponent<AudioSource>();
        _stream = GetComponent<LiquidStream>();

        _source.playOnAwake = false;
        _source.loop = false;
        _source.spatialBlend = spatialBlend;
        _originalPitch = _source.pitch;
    }

    private void OnEnable()
    {
        if (_stream == null) _stream = GetComponent<LiquidStream>();
        if (_stream != null)
        {
            _stream.OnPourStart += HandlePourStart;
            _stream.OnPourStop  += HandlePourStop;
        }
    }

    private void OnDisable()
    {
        if (_stream != null)
        {
            _stream.OnPourStart -= HandlePourStart;
            _stream.OnPourStop  -= HandlePourStop;
        }
        StopLoopImmediate();
    }

    private void HandlePourStart()
    {
        if (sfxPourLoop == null || _source == null) return;

        var clip = sfxPourLoop.GetRandomClip();
        if (clip == null) return;

        // Configure audio source based on SFXEvent + jitter
        float evtPitch = Random.Range(Mathf.Min(sfxPourLoop.pitchRange.x, sfxPourLoop.pitchRange.y), Mathf.Max(sfxPourLoop.pitchRange.x, sfxPourLoop.pitchRange.y));
        float jitPitch = Random.Range(Mathf.Min(pitchJitter.x, pitchJitter.y), Mathf.Max(pitchJitter.x, pitchJitter.y));
        _source.pitch = evtPitch * jitPitch;

        float evtVol = Mathf.Clamp01(sfxPourLoop.volume);
        float jitVol = Random.Range(Mathf.Min(volumeJitter.x, volumeJitter.y), Mathf.Max(volumeJitter.x, volumeJitter.y));
        _targetVolume = Mathf.Clamp01(evtVol * Mathf.Clamp01(jitVol));

        _source.spatialBlend = Mathf.Clamp01(sfxPourLoop.spatialBlend);
        _source.rolloffMode  = sfxPourLoop.rolloff;
        _source.minDistance  = Mathf.Max(0.01f, sfxPourLoop.minDistance);
        _source.maxDistance  = Mathf.Max(_source.minDistance + 0.01f, sfxPourLoop.maxDistance);
        _source.dopplerLevel = Mathf.Max(0f, sfxPourLoop.dopplerLevel);

        _source.clip = clip;
        _source.loop = true;
        _source.volume = _targetVolume;
        _source.Play();
    }

    private void HandlePourStop()
    {
        if (_source == null) return;
        if (stopFadeSeconds <= 0f)
        {
            StopLoopImmediate();
            return;
        }

        if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
        _fadeRoutine = StartCoroutine(FadeOutAndStop(stopFadeSeconds));
    }

    private System.Collections.IEnumerator FadeOutAndStop(float seconds)
    {
        float startVol = _source.volume;
        float t = 0f;
        while (t < seconds)
        {
            t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(t / seconds);
            _source.volume = Mathf.Lerp(startVol, 0f, u);
            yield return null;
        }
        StopLoopImmediate();
        _fadeRoutine = null;
    }

    private void StopLoopImmediate()
    {
        if (_source == null) return;
        _source.Stop();
        _source.loop = false;
        _source.clip = null;
        _source.volume = _targetVolume;
        _source.pitch = _originalPitch;
    }
}


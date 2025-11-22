using UnityEngine;
using JamesKJamKit.Services.Audio;

/// <summary>
/// Handles all ice-scoop related sounds:
/// - Scoop from bucket (one-shot)
/// - Ice cubes dropping into a mug (one-shot)
/// Attach this to the root "ice_scoop" GameObject (same object as IceScooperController).
///
/// Uses SFXEvent assets so volumes/pitch/spatial settings can be tweaked in data.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(AudioSource))]
public sealed class IceScoopAudio : MonoBehaviour
{
    [Header("SFX Events (Scriptable Objects)")]
    [Tooltip("One-shot: scooping ice from the bucket")]
    [SerializeField] private SFXEvent sfxScoop;

    [Tooltip("One-shot: cubes landing in the mug/cup")]
    [SerializeField] private SFXEvent sfxIceDrop;

    [Header("Jitter (natural variation)")]
    [SerializeField] private Vector2 pitchJitter = new Vector2(0.96f, 1.04f);
    [SerializeField] private Vector2 volumeJitter = new Vector2(0.92f, 1.00f);

    [Header("Output")]
    [Tooltip("0 = fully 2D (UI), 1 = fully 3D (positional).")]
    [Range(0f, 1f)]
    [SerializeField] private float spatialBlend = 0f;
    [Header("Cooldowns")]
    [Tooltip("Minimum seconds between scoop SFX. Set to 0 to disable throttling.")]
    [SerializeField, Min(0f)] private float scoopCooldownSeconds = 0.08f;

    private AudioSource _src;
    private float _lastScoopPlayedAt = -10f;

    private void Awake()
    {
        _src = GetComponent<AudioSource>();
        if (_src == null) _src = gameObject.AddComponent<AudioSource>();
        _src.playOnAwake = false;
        _src.loop = false;
        _src.spatialBlend = spatialBlend;
    }

    /// <summary>Play the "scoop from bucket" one-shot.</summary>
    public void PlayScoop()
    {
        var now = Time.unscaledTime;
        if (scoopCooldownSeconds > 0f && now - _lastScoopPlayedAt < scoopCooldownSeconds) return;

        if (PlayWithJitter(sfxScoop))
        {
            _lastScoopPlayedAt = now;
        }
    }

    /// <summary>Play the "ice drops into cup" one-shot.</summary>
    public void PlayDrop()
    {
        PlayWithJitter(sfxIceDrop);
    }

    private bool PlayWithJitter(SFXEvent ev)
    {
        if (ev == null || _src == null) return false;

        var originalPitch = _src.pitch;

        var evtMinPitch = Mathf.Min(ev.pitchRange.x, ev.pitchRange.y);
        var evtMaxPitch = Mathf.Max(ev.pitchRange.x, ev.pitchRange.y);
        var jitterMinPitch = Mathf.Min(pitchJitter.x, pitchJitter.y);
        var jitterMaxPitch = Mathf.Max(pitchJitter.x, pitchJitter.y);

        var evtPitch = Random.Range(evtMinPitch, evtMaxPitch);
        var jitterPitch = Random.Range(jitterMinPitch, jitterMaxPitch);
        _src.pitch = evtPitch * jitterPitch;

        float vol = Random.Range(volumeJitter.x, volumeJitter.y);
        bool played = TryPlay(ev, vol);

        _src.pitch = originalPitch;
        return played;
    }

    // Applies the SFXEvent mix settings and fires a one-shot with extra volume jitter.
    private bool TryPlay(SFXEvent ev, float volumeJitter)
    {
        if (ev == null || _src == null) return false;

        var clip = ev.GetRandomClip();
        if (clip == null) return false;

        var evtVolume = Mathf.Clamp01(ev.volume);
        var finalVolume = Mathf.Clamp01(evtVolume * Mathf.Clamp01(volumeJitter));

        _src.spatialBlend = Mathf.Clamp01(ev.spatialBlend);
        _src.rolloffMode = ev.rolloff;
        _src.minDistance = Mathf.Max(0.01f, ev.minDistance);
        _src.maxDistance = Mathf.Max(_src.minDistance + 0.01f, ev.maxDistance);
        _src.dopplerLevel = Mathf.Max(0f, ev.dopplerLevel);

        _src.PlayOneShot(clip, finalVolume);
        return true;
    }
}

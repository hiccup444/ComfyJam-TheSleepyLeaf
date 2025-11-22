using UnityEngine;
using UnityEngine.Audio;
using JamesKJamKit.Services.Audio;

[DisallowMultipleComponent]
[RequireComponent(typeof(AudioSource))]
public sealed class FridgeDoorAudio : MonoBehaviour
{
    [Header("SFX Events")]
    [SerializeField] private SFXEvent sfxOpen;
    [SerializeField] private SFXEvent sfxClose;

    [Header("Output (optional)")]
    [SerializeField] private AudioMixerGroup outputGroup;
    [Tooltip("0 = fully 2D, 1 = fully 3D")]
    [Range(0f, 1f)]
    [SerializeField] private float spatialBlend = 0f;

    [Header("Jitter (variation)")]
    [SerializeField] private Vector2 pitchJitter = new Vector2(0.99f, 1.01f);
    [SerializeField] private Vector2 volumeJitter = new Vector2(0.97f, 1.00f);

    private AudioSource _source;

    void Reset()
    {
        spatialBlend = 0f;
        pitchJitter = new Vector2(0.99f, 1.01f);
        volumeJitter = new Vector2(0.97f, 1.00f);
    }

    void Awake()
    {
        _source = GetComponent<AudioSource>();
        if (_source == null) _source = gameObject.AddComponent<AudioSource>();

        _source.playOnAwake = false;
        _source.loop = false;
        _source.spatialBlend = spatialBlend;
        if (outputGroup) _source.outputAudioMixerGroup = outputGroup;
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (_source == null) _source = GetComponent<AudioSource>();
        if (_source != null)
        {
            _source.playOnAwake = false;
            _source.loop = false;
            _source.spatialBlend = spatialBlend;
            if (outputGroup) _source.outputAudioMixerGroup = outputGroup;
        }
    }
#endif

    public void PlayOpen()  => PlayEvent(sfxOpen);
    public void PlayClose() => PlayEvent(sfxClose);

    private void PlayEvent(SFXEvent evt)
    {
        if (_source == null || evt == null) return;

        var clip = evt.GetRandomClip();
        if (clip == null) return;

        // Cache & apply per-event settings
        float originalPitch = _source.pitch;

        // Event ranges
        float evtPitchMin = Mathf.Min(evt.pitchRange.x, evt.pitchRange.y);
        float evtPitchMax = Mathf.Max(evt.pitchRange.x, evt.pitchRange.y);
        float evtPitch     = Random.Range(evtPitchMin, evtPitchMax);

        float jitPitchMin  = Mathf.Min(pitchJitter.x, pitchJitter.y);
        float jitPitchMax  = Mathf.Max(pitchJitter.x, pitchJitter.y);
        float jitPitch     = Random.Range(jitPitchMin, jitPitchMax);

        _source.pitch = evtPitch * jitPitch;

        float evtVol      = Mathf.Clamp01(evt.volume);
        float jitVolMin   = Mathf.Min(volumeJitter.x, volumeJitter.y);
        float jitVolMax   = Mathf.Max(volumeJitter.x, volumeJitter.y);
        float jitVol      = Random.Range(jitVolMin, jitVolMax);
        float finalVolume = Mathf.Clamp01(evtVol * Mathf.Clamp01(jitVol));

        _source.spatialBlend = Mathf.Clamp01(evt.spatialBlend);
        _source.rolloffMode  = evt.rolloff;
        _source.minDistance  = Mathf.Max(0.01f, evt.minDistance);
        _source.maxDistance  = Mathf.Max(_source.minDistance + 0.01f, evt.maxDistance);
        _source.dopplerLevel = Mathf.Max(0f, evt.dopplerLevel);

        if (outputGroup) _source.outputAudioMixerGroup = outputGroup;

        _source.PlayOneShot(clip, finalVolume);

        // Restore pitch for next play
        _source.pitch = originalPitch;
    }
}

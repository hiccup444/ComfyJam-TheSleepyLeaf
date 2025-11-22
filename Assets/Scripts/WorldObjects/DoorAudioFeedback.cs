using UnityEngine;
using JamesKJamKit.Services.Audio;

/// <summary>
/// Plays paired door + chime SFXEvents when the door opens or closes.
/// Call PlayOpen()/PlayClose() from the door logic (e.g., when toggling meshes).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(AudioSource))]
public sealed class DoorAudioFeedback : MonoBehaviour
{
    [Header("Open SFX")]
    [SerializeField] private SFXEvent sfxDoorOpen;
    [SerializeField] private SFXEvent sfxChimeOpen;

    [Header("Close SFX")]
    [SerializeField] private SFXEvent sfxDoorClose;
    [SerializeField] private SFXEvent sfxChimeClose;

    [Header("Jitter (variation)")]
    [SerializeField] private Vector2 pitchJitter = new Vector2(0.98f, 1.02f);
    [SerializeField] private Vector2 volumeJitter = new Vector2(0.95f, 1.00f);

    [Header("Output")]
    [Tooltip("0 = fully 2D, 1 = fully 3D. Applied on Awake.")]
    [Range(0f, 1f)]
    [SerializeField] private float spatialBlend = 0f;

    private AudioSource _source;

    private void Awake()
    {
        _source = GetComponent<AudioSource>();
        if (_source == null)
            _source = gameObject.AddComponent<AudioSource>();

        _source.playOnAwake = false;
        _source.loop = false;
        _source.spatialBlend = spatialBlend;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (_source == null)
            _source = GetComponent<AudioSource>();

        if (_source != null)
        {
            _source.playOnAwake = false;
            _source.loop = false;
            _source.spatialBlend = spatialBlend;
        }
    }
#endif

    /// <summary>Plays the paired door + chime open SFX events.</summary>
    public void PlayOpen()
    {
        PlayPair(sfxDoorOpen, sfxChimeOpen);
    }

    /// <summary>Plays the paired door + chime close SFX events.</summary>
    public void PlayClose()
    {
        PlayPair(sfxDoorClose, sfxChimeClose);
    }

    private void PlayPair(SFXEvent primary, SFXEvent secondary)
    {
        // Play both on the same frame so they layer together.
        PlayEvent(primary);
        PlayEvent(secondary);
    }

    private void PlayEvent(SFXEvent evt)
    {
        if (evt == null || _source == null)
            return;

        var clip = evt.GetRandomClip();
        if (clip == null)
            return;

        float originalPitch = _source.pitch;

        float evtPitch = Random.Range(Mathf.Min(evt.pitchRange.x, evt.pitchRange.y), Mathf.Max(evt.pitchRange.x, evt.pitchRange.y));
        float jitterPitch = Random.Range(Mathf.Min(pitchJitter.x, pitchJitter.y), Mathf.Max(pitchJitter.x, pitchJitter.y));
        _source.pitch = evtPitch * jitterPitch;

        float evtVolume = Mathf.Clamp01(evt.volume);
        float jitterVolume = Random.Range(Mathf.Min(volumeJitter.x, volumeJitter.y), Mathf.Max(volumeJitter.x, volumeJitter.y));
        float finalVolume = Mathf.Clamp01(evtVolume * Mathf.Clamp01(jitterVolume));

        _source.spatialBlend = Mathf.Clamp01(evt.spatialBlend);
        _source.rolloffMode = evt.rolloff;
        _source.minDistance = Mathf.Max(0.01f, evt.minDistance);
        _source.maxDistance = Mathf.Max(_source.minDistance + 0.01f, evt.maxDistance);
        _source.dopplerLevel = Mathf.Max(0f, evt.dopplerLevel);

        _source.PlayOneShot(clip, finalVolume);
        _source.pitch = originalPitch;
    }
}

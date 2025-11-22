using UnityEngine;
using UnityEngine.EventSystems;
using JamesKJamKit.Services.Audio;

/// <summary>
/// Plays SFXEvent-driven one-shots when the mug is picked up or released.
/// Attach alongside DragItem2D on the mug's draggable collider object.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(AudioSource))]
public sealed class MugPickupAudio : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    [Header("SFX Events")]
    [Tooltip("Played when the mug is grabbed / picked up.")]
    [SerializeField] private SFXEvent sfxPickUp;

    [Tooltip("Played when the mug is released / put down.")]
    [SerializeField] private SFXEvent sfxPutDown;

    [Header("Jitter (natural variation)")]
    [SerializeField] private Vector2 pitchJitter = new Vector2(0.97f, 1.03f);
    [SerializeField] private Vector2 volumeJitter = new Vector2(0.94f, 1.00f);

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

    /// <summary>Plays the pickup sound (called on left-click down).</summary>
    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left)
            return;

        PlayEvent(sfxPickUp);
    }

    /// <summary>Plays the put-down sound (called on left-click release).</summary>
    public void OnPointerUp(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left)
            return;
    }

    /// <summary>Allows other scripts to trigger the pickup SFX manually.</summary>
    public void PlayPickUp() => PlayEvent(sfxPickUp);

    /// <summary>Allows other scripts to trigger the put-down SFX manually.</summary>
    public void PlayPutDown() => PlayEvent(sfxPutDown);

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

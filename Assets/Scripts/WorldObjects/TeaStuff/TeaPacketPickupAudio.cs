using UnityEngine;
using UnityEngine.EventSystems;
using JamesKJamKit.Services.Audio;

/// <summary>
/// Plays SFXEvent-driven one-shots when a tea packet is picked up or put down.
/// Attach to the draggable child (teaPacketMain) that has DragItem2D.
/// Gated so clicks on the tear-corner (ignore colliders) do not trigger audio.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(AudioSource))]
public sealed class TeaPacketPickupAudio : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    [Header("SFX Events")]
    [Tooltip("Played when the tea packet is grabbed / picked up.")]
    [SerializeField] private SFXEvent sfxPickUp;

    [Tooltip("Played when the tea packet is released / put down.")]
    [SerializeField] private SFXEvent sfxPutDown;

    [Header("Jitter (natural variation)")]
    [SerializeField] private Vector2 pitchJitter = new Vector2(0.97f, 1.03f);
    [SerializeField] private Vector2 volumeJitter = new Vector2(0.94f, 1.00f);

    [Header("Output")]
    [Tooltip("0 = fully 2D, 1 = fully 3D. Applied on Awake.")]
    [Range(0f, 1f)]
    [SerializeField] private float spatialBlend = 0f;

    private AudioSource _source;
    private DragItem2D _dragItem;
    private bool _pickupArmed;

    private void Awake()
    {
        _source = GetComponent<AudioSource>();
        if (_source == null)
            _source = gameObject.AddComponent<AudioSource>();

        _dragItem = GetComponent<DragItem2D>();

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

        if (_dragItem == null)
            _dragItem = GetComponent<DragItem2D>();
    }
#endif

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left)
            return;

        if (PointerOverIgnoredCollider(eventData))
            return; // Clicking tear-corner etc. — do not play

        PlayEvent(sfxPickUp);
        _pickupArmed = true;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left)
            return;

        if (_pickupArmed)
            PlayEvent(sfxPutDown);

        _pickupArmed = false;
    }

    public void PlayPickUp() => PlayEvent(sfxPickUp);
    public void PlayPutDown() => PlayEvent(sfxPutDown);

    private bool PointerOverIgnoredCollider(PointerEventData eventData)
    {
        if (_dragItem == null || _dragItem.ignoreColliders == null || _dragItem.ignoreColliders.Length == 0)
            return false;

        Camera cam = eventData.pressEventCamera != null ? eventData.pressEventCamera : Camera.main;
        if (cam == null) return false;

        Vector3 world = cam.ScreenToWorldPoint(eventData.position);
        world.z = 0f;

        foreach (var ignored in _dragItem.ignoreColliders)
        {
            if (ignored != null && ignored.OverlapPoint(world))
                return true;
        }
        return false;
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


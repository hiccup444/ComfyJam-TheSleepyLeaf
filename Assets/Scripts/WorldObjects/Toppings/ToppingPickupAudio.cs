using UnityEngine;
using UnityEngine.EventSystems;
using JamesKJamKit.Services.Audio;

/// <summary>
/// Handles pickup/apply SFX for toppings (e.g., lemon slice) driven by DragItem2D + ToppingItem.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(AudioSource))]
public sealed class ToppingPickupAudio : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IToppingAppliedListener
{
    [Header("SFX Events")]
    [Tooltip("Played when the topping is picked up / grabbed.")]
    [SerializeField] private SFXEvent sfxPickUp;

    [Tooltip("Played when the topping successfully lands on a cup.")]
    [SerializeField] private SFXEvent sfxApply;

    [Header("Variation")]
    [SerializeField] private Vector2 pitchJitter = new Vector2(0.97f, 1.03f);
    [SerializeField] private Vector2 volumeJitter = new Vector2(0.94f, 1.00f);

    [Header("Output")]
    [Tooltip("0 = fully 2D, 1 = fully 3D. Applied when playing clips.")]
    [Range(0f, 1f)]
    [SerializeField] private float spatialBlend = 0f;

    [Header("Apply SFX Handling")]
    [Tooltip("If true, the apply SFX plays on a temporary detached AudioSource so it keeps playing after the lemon is destroyed.")]
    [SerializeField] private bool detachApplyAudio = true;

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

    /// <summary>
    /// Called by ToppingSlot when it manually drives drag events on a freshly spawned topping.
    /// </summary>
    public void HandleManualPickup(PointerEventData eventData)
    {
        ProcessPointerDown(eventData);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        ProcessPointerDown(eventData);
    }

    void ProcessPointerDown(PointerEventData eventData)
    {
        if (eventData != null && eventData.button != PointerEventData.InputButton.Left)
            return;

        if (PointerOverIgnoredCollider(eventData))
            return;

        PlayEvent(sfxPickUp);
        _pickupArmed = true;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        // Put-down SFX is triggered when the topping actually applies to a cup (OnToppingApplied).
        if (eventData != null && eventData.button != PointerEventData.InputButton.Left)
            return;
    }

    public void OnToppingApplied(CupState cupState)
    {
        if (!_pickupArmed)
            return;

        PlayApplyEvent();
        _pickupArmed = false;
    }

    private bool PointerOverIgnoredCollider(PointerEventData eventData)
    {
        if (_dragItem == null || _dragItem.ignoreColliders == null || _dragItem.ignoreColliders.Length == 0)
            return false;

        Camera cam = eventData != null && eventData.pressEventCamera != null ? eventData.pressEventCamera : Camera.main;
        if (cam == null) return false;

        Vector3 world = eventData != null ? cam.ScreenToWorldPoint(eventData.position) : transform.position;
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
        float pitch = ComputePitch(evt);
        float volume = ComputeVolume(evt);

        ApplyEventSettings(_source, evt, pitch);
        _source.PlayOneShot(clip, volume);
        _source.pitch = originalPitch;
    }

    private void PlayApplyEvent()
    {
        if (sfxApply == null)
            return;

        if (detachApplyAudio)
        {
            PlayDetachedEvent(sfxApply);
        }
        else
        {
            PlayEvent(sfxApply);
        }
    }

    private float ComputePitch(SFXEvent evt)
    {
        float evtPitch = Random.Range(Mathf.Min(evt.pitchRange.x, evt.pitchRange.y), Mathf.Max(evt.pitchRange.x, evt.pitchRange.y));
        float jitPitch = Random.Range(Mathf.Min(pitchJitter.x, pitchJitter.y), Mathf.Max(pitchJitter.x, pitchJitter.y));
        return evtPitch * jitPitch;
    }

    private float ComputeVolume(SFXEvent evt)
    {
        float evtVolume = Mathf.Clamp01(evt.volume);
        float jitVol = Random.Range(Mathf.Min(volumeJitter.x, volumeJitter.y), Mathf.Max(volumeJitter.x, volumeJitter.y));
        return Mathf.Clamp01(evtVolume * Mathf.Clamp01(jitVol));
    }

    private void ApplyEventSettings(AudioSource target, SFXEvent evt, float pitch)
    {
        if (target == null || evt == null) return;
        target.pitch = pitch;
        target.spatialBlend = Mathf.Clamp01(evt.spatialBlend);
        target.rolloffMode = evt.rolloff;
        target.minDistance = Mathf.Max(0.01f, evt.minDistance);
        target.maxDistance = Mathf.Max(target.minDistance + 0.01f, evt.maxDistance);
        target.dopplerLevel = Mathf.Max(0f, evt.dopplerLevel);
    }

    private void PlayDetachedEvent(SFXEvent evt)
    {
        var clip = evt.GetRandomClip();
        if (clip == null)
            return;

        GameObject temp = new GameObject($"{name}_ApplySFX");
        temp.transform.position = transform.position;

        var tempSource = temp.AddComponent<AudioSource>();
        tempSource.playOnAwake = false;
        tempSource.loop = false;

        float pitch = ComputePitch(evt);
        float volume = ComputeVolume(evt);
        ApplyEventSettings(tempSource, evt, pitch);

        tempSource.clip = clip;
        tempSource.volume = volume;
        tempSource.Play();

        float clipDuration = clip.length;
        float adjustedDuration = clipDuration / Mathf.Max(0.01f, Mathf.Abs(tempSource.pitch));
        Object.Destroy(temp, adjustedDuration + 0.05f);
    }
}

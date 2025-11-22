// CupPourController.cs
using UnityEngine;
using JamesKJamKit.Services;
using JamesKJamKit.Services.Audio;

[DisallowMultipleComponent]
public sealed class CupPourController : MonoBehaviour
{
    [Header("Behavior")]
    public bool requireDragging = false;          // set true if you only want dump while dragging
    public float hoverDelay = 0.15f;
    public float pourTiltZ = -55f;
    public float tiltInTime = 0.15f;
    public float tiltOutTime = 0.12f;
    public float drainRatePerSec = 0.75f;

    [Tooltip("How long after last sink ping we still consider the cup 'in the zone'.")]
    public float zoneGrace = 0.15f;

    [Header("Attraction")]
    public bool attractToSinkPoint = true;
    public float attractLerp = 8f;

    [Header("Audio")]
    [Tooltip("Optional SFX event that loops while pouring into sink.")]
    public SFXEvent pourLoopEvent;
    [Tooltip("One-shot SFX event that plays when the cup starts dumping into the sink.")]
    public SFXEvent sinkPourSfx;

    [Header("Refs (auto if empty)")]
    public DragItem2D dragItem;
    public MugBeverageState beverageState;
    public CupState cupState;
    public Transform alignPoint;
    public MugIceState mugIceState;

    // runtime
    SinkZone2D _currentZone;
    float _enteredAt = -999f;
    float _lastSeenTime = -999f;   // refreshed each Enter/Stay
    float _tiltT;                  // 0..1 toward pour
    float _originalZ;

    // Performance optimization caches
    float _currentZAngle;          // cached Z rotation to avoid repeated eulerAngles access
    float _tiltInSpeed;            // cached reciprocal of tiltInTime
    float _tiltOutSpeed;           // cached reciprocal of tiltOutTime

    // Audio runtime
    AudioSource _pourLoopSource;   // tracks active pour loop
    bool _sinkPourTriggered;

    void Awake()
    {
        // Use GetComponent instead of GetComponentInChildren when possible
        if (!dragItem) dragItem = GetComponent<DragItem2D>();
        if (!beverageState) beverageState = GetComponent<MugBeverageState>();
        if (!cupState) cupState = GetComponent<CupState>();
        if (!mugIceState) mugIceState = GetComponent<MugIceState>();
        if (!alignPoint)
        {
            var tr = transform.Find("Visuals/FillPivot");
            alignPoint = tr ? tr : transform;
        }

        // Cache rotation and tilt speeds to avoid repeated calculations
        _originalZ = transform.eulerAngles.z;
        _currentZAngle = _originalZ;
        _tiltInSpeed = tiltInTime > 0f ? 1f / tiltInTime : 1f;
        _tiltOutSpeed = tiltOutTime > 0f ? 1f / tiltOutTime : 1f;
    }

    // Called by SinkZone2D on Enter/Stay
    public void RequestDumpOverSink(SinkZone2D zone)
    {
        _currentZone = zone;
        _lastSeenTime = Time.time;
        if (_enteredAt < 0f) _enteredAt = Time.time; // first seen
    }

    // Called by SinkZone2D on Exit
    public void StopDumpFromSink(SinkZone2D zone)
    {
        // Don't immediately clear _currentZone - let the grace period handle it
        // This prevents stuttering when hovering at the edge of the trigger zone
        // The Update loop will clear _currentZone when grace expires
    }

    void Update()
    {
        // Cache Time.time to avoid multiple property accesses
        float currentTime = Time.time;

        bool draggingOK = !requireDragging || (dragItem && dragItem.IsDragging);
        bool inZone = _currentZone != null && (currentTime - _lastSeenTime) <= zoneGrace;

        bool shouldDump = inZone
                          && draggingOK
                          && beverageState != null
                          && !beverageState.IsEmpty()
                          && currentTime >= (_enteredAt + hoverDelay);

        if (shouldDump)
        {
            // Start pour audio loop if not already playing
            if (_pourLoopSource == null && pourLoopEvent != null && AudioManager.Instance != null)
            {
                StartPourLoop();
            }
            if (!_sinkPourTriggered)
            {
                PlaySinkPourOneShot();
                _sinkPourTriggered = true;
            }

            // Inline position lerp to reduce transform access
            if (attractToSinkPoint && _currentZone)
            {
                Vector3 target = _currentZone.transform.position;
                transform.position = Vector3.Lerp(transform.position, target, 1f - Mathf.Exp(-attractLerp * Time.deltaTime));
            }

            // Tilt in - use cached speed (multiplication instead of division)
            _tiltT = Mathf.Clamp01(_tiltT + Time.deltaTime * _tiltInSpeed);
            _currentZAngle = Mathf.LerpAngle(_originalZ, pourTiltZ, _tiltT);
            transform.eulerAngles = new Vector3(0f, 0f, _currentZAngle);

            // Drain
            beverageState.DrainContent(drainRatePerSec * Time.deltaTime);
            if (beverageState.IsEmpty())
            {
                beverageState.ClearToEmpty();

                // Clear toppings and ice when cup is emptied (use null-conditional operators)
                cupState?.ClearToppings();
                mugIceState?.ClearIce();

                _currentZone = null;
                _enteredAt = -999f;

                // Stop pour audio when empty
                StopPourLoop();
                _sinkPourTriggered = false;
            }
        }
        else
        {
            // Stop pour audio when not dumping
            StopPourLoop();

            // If zone timed out, clear so we don't dump elsewhere
            if (!inZone && _currentZone != null)
            {
                _currentZone = null;
                _enteredAt = -999f;
            }

            // Tilt back - use cached speed (multiplication instead of division)
            if (_tiltT > 0f)
            {
                _tiltT = Mathf.Max(0f, _tiltT - Time.deltaTime * _tiltOutSpeed);
                _currentZAngle = Mathf.LerpAngle(_originalZ, pourTiltZ, _tiltT);
                transform.eulerAngles = new Vector3(0f, 0f, _currentZAngle);
            }
            _sinkPourTriggered = false;
        }
    }

    void OnDisable()
    {
        // Ensure audio stops when component is disabled
        StopPourLoop();
    }

    void StartPourLoop()
    {
        if (pourLoopEvent == null || AudioManager.Instance == null) return;
        if (_pourLoopSource != null && _pourLoopSource.isPlaying) return; // already playing

        var clip = pourLoopEvent.GetRandomClip();
        if (clip == null) return;

        // Get a looping audio source from AudioManager
        _pourLoopSource = AudioManager.Instance.PlayLoop(clip, spatial: pourLoopEvent.spatialBlend > 0f);

        if (_pourLoopSource != null)
        {
            // Apply SFXEvent settings
            _pourLoopSource.volume = Mathf.Clamp01(pourLoopEvent.volume);
            _pourLoopSource.pitch = Random.Range(pourLoopEvent.pitchRange.x, pourLoopEvent.pitchRange.y);
            _pourLoopSource.spatialBlend = Mathf.Clamp01(pourLoopEvent.spatialBlend);
            _pourLoopSource.rolloffMode = pourLoopEvent.rolloff;
            _pourLoopSource.minDistance = Mathf.Max(0.01f, pourLoopEvent.minDistance);
            _pourLoopSource.maxDistance = Mathf.Max(_pourLoopSource.minDistance + 0.01f, pourLoopEvent.maxDistance);
            _pourLoopSource.dopplerLevel = Mathf.Max(0f, pourLoopEvent.dopplerLevel);
            _pourLoopSource.transform.position = transform.position;
        }
    }

    void PlaySinkPourOneShot()
    {
        if (sinkPourSfx == null || AudioManager.Instance == null) return;
        AudioManager.Instance.PlaySFX(sinkPourSfx, transform);
    }

    void StopPourLoop()
    {
        if (_pourLoopSource == null) return;

        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.StopLoop(_pourLoopSource);
        }

        _pourLoopSource = null;
    }
}

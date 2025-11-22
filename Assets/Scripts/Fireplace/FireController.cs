using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using JamesKJamKit.Services;
using JamesKJamKit.Services.Audio;

/// <summary>
/// Controls a simple fireplace loop.
/// - Intensity is logsInFire / maxLogs, with a one-frame snap to 1 on AddLog.
/// - Maps intensity (0..1) to fireRoot.localPosition.y using an AnimationCurve and minY/maxY.
/// - Each log burns on its own timer. When a timer completes, one log is removed.
/// - Optionally uses unscaled time so burning continues through pause.
/// - Writes a shared TemperatureVariable (0..100) for other systems.
/// </summary>
[DisallowMultipleComponent]
public sealed class FireController : MonoBehaviour
{
    [Header("Fire Visual Mapping")]
    [Tooltip("Assign the visible 'Fire' GameObject root.")]
    public Transform fireRoot;

    [Tooltip("Y position when fire is out.")]
    public float minY = 0f;

    [Tooltip("Y position when fire is roaring.")]
    public float maxY = 1f;

    [Tooltip("Curve: input=intensity(0..1), output=normalized(0..1) for Lerp(minY,maxY).")]
    public AnimationCurve intensityToY = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Fuel / Burning")]
    [Min(1)] public int maxLogs = 3;
    [Tooltip("Seconds a single log burns before expiring.")]
    public float burnSecondsPerLog = 45f;
    [Tooltip("If true, logs keep burning while the game is paused (unscaled time).")]
    public bool useUnscaledTime = true;
    [Tooltip("How many logs the fire starts with when the scene loads.")]
    [Min(0)] public int startingLogs = 0;

    [Header("Temperature Output (0..100)")]
    public TemperatureVariable temperature;

    [Header("Events")] 
    public UnityEvent onLogAdded;
    public UnityEvent onLogBurnedOut;
    public UnityEvent onFireExtinguished;
    [Tooltip("Optional: invoked whenever temperature is updated (0..100).")]
    public UnityEvent<float> onTemperatureChanged;

    [Header("Audio")]
    [Tooltip("Ambient loop that plays whenever the fire has fuel.")]
    public SFXEvent fireLoopSfx;

    [Tooltip("Optional AudioSource used for the loop. If empty, one is created automatically.")]
    public AudioSource loopSource;

    [Tooltip("Minimum multiplier applied to the loop's volume at low intensity.")]
    [Range(0f, 1f)]
    public float loopVolumeMin = 0.25f;

    [Tooltip("Maximum multiplier applied to the loop's volume at full intensity.")]
    [Range(0f, 1f)]
    public float loopVolumeMax = 1f;

    [Tooltip("Play this flare SFX when a log is successfully added.")]
    public SFXEvent fireFlareSfx;

    [Header("Debug")]
    [SerializeField, ReadOnlyInspector] private int logsInFire = 0;

    // Runtime state
    private readonly List<Coroutine> _burnCoroutines = new();
    private bool _snapIntensityNextUpdate;
    private float _lastTemp = -999f;
    private AudioSource _runtimeLoopSource;
    private bool _loopPlaying;
    private bool _startingFuelApplied;
    private bool _startingFuelQueued;
    private Coroutine _startingFuelWaitRoutine;

    /// <summary>
    /// Adds a single log if capacity not reached. Starts a burn timer.
    /// Briefly snaps intensity to 1 for visual feedback.
    /// </summary>
    public bool AddLog()
    {
        return TryAddLogInternal(invokeEvents: true, playFlare: true);
    }

    private bool TryAddLogInternal(bool invokeEvents, bool playFlare)
    {
        if (logsInFire >= Mathf.Max(1, maxLogs))
        {
            return false;
        }

        logsInFire++;
        if (invokeEvents)
        {
            _snapIntensityNextUpdate = true;
            onLogAdded?.Invoke();
        }

        var co = StartCoroutine(BurnTimerRoutine(burnSecondsPerLog));
        _burnCoroutines.Add(co);

        if (playFlare)
        {
            PlayFireFlare();
        }

        return true;
    }

    private void Awake()
    {
        if (loopSource != null)
        {
            ConfigureLoopSource(loopSource);
        }
    }

    private void Start()
    {
        TryApplyStartingFuelAfterGameStart();
    }

    private void OnDisable()
    {
        StopFireLoop();
        UnregisterStartingFuelListener();
    }

    private void InitializeStartingFuel()
    {
        if (_startingFuelApplied)
            return;

        _startingFuelApplied = true;
        if (startingLogs <= 0)
            return;

        int targetLogs = Mathf.Clamp(startingLogs, 0, Mathf.Max(1, maxLogs));
        for (int i = 0; i < targetLogs; i++)
        {
            if (!TryAddLogInternal(invokeEvents: false, playFlare: false))
                break;
        }
    }

    private void TryApplyStartingFuelAfterGameStart()
    {
        if (startingLogs <= 0 || _startingFuelApplied)
            return;

        var manager = GameManager.Instance;
        if (manager == null)
        {
            StartGameManagerWatcher();
            return;
        }

        if (manager.HasGameStarted())
        {
            InitializeStartingFuel();
            return;
        }

        QueueStartingFuelAfterCutscene(manager);
    }

    private void StartGameManagerWatcher()
    {
        if (_startingFuelWaitRoutine != null)
            return;

        _startingFuelWaitRoutine = StartCoroutine(WaitForGameManager());
    }

    private IEnumerator WaitForGameManager()
    {
        while (GameManager.Instance == null)
        {
            yield return null;
        }

        _startingFuelWaitRoutine = null;
        TryApplyStartingFuelAfterGameStart();
    }

    private void QueueStartingFuelAfterCutscene(GameManager manager)
    {
        if (_startingFuelQueued)
            return;

        manager.OnDayStarted += HandleDayStarted;
        _startingFuelQueued = true;
    }

    private void HandleDayStarted()
    {
        var manager = GameManager.Instance;
        if (manager != null)
        {
            manager.OnDayStarted -= HandleDayStarted;
        }

        _startingFuelQueued = false;
        InitializeStartingFuel();
    }

    private void UnregisterStartingFuelListener()
    {
        if (_startingFuelWaitRoutine != null)
        {
            StopCoroutine(_startingFuelWaitRoutine);
            _startingFuelWaitRoutine = null;
        }

        if (_startingFuelQueued)
        {
            var manager = GameManager.Instance;
            if (manager != null)
            {
                manager.OnDayStarted -= HandleDayStarted;
            }

            _startingFuelQueued = false;
        }
    }

    private IEnumerator BurnTimerRoutine(float seconds)
    {
        if (seconds <= 0f)
        {
            // Edge case: instantly expire
            yield return null;
        }
        else if (useUnscaledTime)
        {
            yield return new WaitForSecondsRealtime(seconds);
        }
        else
        {
            yield return new WaitForSeconds(seconds);
        }

        // One log finished burning
        logsInFire = Mathf.Max(0, logsInFire - 1);
        onLogBurnedOut?.Invoke();

        if (logsInFire <= 0)
        {
            onFireExtinguished?.Invoke();
        }
    }

    private void Update()
    {
        float intensity = ComputeIntensity();
        ApplyFireHeight(intensity);
        UpdateTemperature(intensity);
        UpdateLoopAudio(intensity);
    }

    private float ComputeIntensity()
    {
        if (_snapIntensityNextUpdate)
        {
            _snapIntensityNextUpdate = false; // one-frame snap
            return 1f;
        }

        int cappedMax = Mathf.Max(1, maxLogs);
        return Mathf.Clamp01(logsInFire / (float)cappedMax);
    }

    private void ApplyFireHeight(float intensity)
    {
        if (!fireRoot) return;

        float t = intensityToY != null ? Mathf.Clamp01(intensityToY.Evaluate(Mathf.Clamp01(intensity))) : Mathf.Clamp01(intensity);
        float y = Mathf.Lerp(minY, maxY, t);
        // Make sure we don't exceed bounds (in case curve overshoots)
        y = Mathf.Clamp(y, Mathf.Min(minY, maxY), Mathf.Max(minY, maxY));

        var lp = fireRoot.localPosition;
        if (!Mathf.Approximately(lp.y, y))
        {
            lp.y = y;
            fireRoot.localPosition = lp;
        }
    }

    private void UpdateTemperature(float intensity)
    {
        float temp = Mathf.Clamp01(intensity) * 100f;
        if (temperature)
        {
            // Avoid re-invoking if not changed meaningfully
            if (!Mathf.Approximately(_lastTemp, temp))
            {
                temperature.Set(temp);
                onTemperatureChanged?.Invoke(temp);
                _lastTemp = temp;
            }
        }
        else
        {
            if (!Mathf.Approximately(_lastTemp, temp))
            {
                onTemperatureChanged?.Invoke(temp);
                _lastTemp = temp;
            }
        }
    }

    private void UpdateLoopAudio(float intensity)
    {
        if (fireLoopSfx == null)
        {
            StopFireLoop();
            return;
        }

        if (intensity <= 0f)
        {
            StopFireLoop();
            return;
        }

        var source = EnsureLoopSource();
        if (source == null)
            return;

        if (!_loopPlaying || source.clip == null || !source.isPlaying)
        {
            StartFireLoop(source);
        }

        source.volume = ComputeLoopVolume(intensity);
    }

    private float ComputeLoopVolume(float intensity)
    {
        if (fireLoopSfx == null)
            return 0f;

        float min = Mathf.Clamp01(loopVolumeMin);
        float max = Mathf.Clamp01(loopVolumeMax);
        if (max < min) max = min;

        float baseVol = Mathf.Clamp01(fireLoopSfx.volume);
        float mix = Mathf.Lerp(min, max, Mathf.Clamp01(intensity));
        return Mathf.Clamp01(baseVol * mix);
    }

    private void StartFireLoop(AudioSource source)
    {
        if (source == null || fireLoopSfx == null)
            return;

        var clip = fireLoopSfx.GetRandomClip();
        if (clip == null)
            return;

        float pitch = SamplePitch(fireLoopSfx);
        ApplyEventSettings(source, fireLoopSfx, pitch);
        source.clip = clip;
        source.loop = true;
        source.volume = 0f;
        source.Play();
        _loopPlaying = true;
    }

    private void StopFireLoop()
    {
        var source = GetCurrentLoopSource();
        if (source == null)
            return;

        if (source.isPlaying)
        {
            source.Stop();
        }

        source.clip = null;
        source.volume = 0f;
        _loopPlaying = false;
    }

    private void PlayFireFlare()
    {
        if (fireFlareSfx == null)
            return;

        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlaySFX(fireFlareSfx, transform);
            return;
        }

        PlayFallbackSfx(fireFlareSfx);
    }

    private void PlayFallbackSfx(SFXEvent evt)
    {
        if (evt == null)
            return;

        var clip = evt.GetRandomClip();
        if (clip == null)
            return;

        GameObject temp = new GameObject($"{name}_FireSFX");
        temp.transform.position = transform.position;

        var source = temp.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;

        float pitch = SamplePitch(evt);
        ApplyEventSettings(source, evt, pitch);

        source.clip = clip;
        source.volume = Mathf.Clamp01(evt.volume);
        source.Play();

        float duration = clip.length / Mathf.Max(0.01f, Mathf.Abs(source.pitch));
        Destroy(temp, duration + 0.05f);
    }

    private void ApplyEventSettings(AudioSource source, SFXEvent evt, float pitch)
    {
        if (source == null || evt == null)
            return;

        source.pitch = pitch;
        source.spatialBlend = Mathf.Clamp01(evt.spatialBlend);
        source.rolloffMode = evt.rolloff;
        source.minDistance = Mathf.Max(0.01f, evt.minDistance);
        source.maxDistance = Mathf.Max(source.minDistance + 0.01f, evt.maxDistance);
        source.dopplerLevel = Mathf.Max(0f, evt.dopplerLevel);
    }

    private float SamplePitch(SFXEvent evt)
    {
        if (evt == null)
            return 1f;

        float min = Mathf.Min(evt.pitchRange.x, evt.pitchRange.y);
        float max = Mathf.Max(evt.pitchRange.x, evt.pitchRange.y);
        return Random.Range(min, max);
    }

    private AudioSource EnsureLoopSource()
    {
        if (loopSource != null)
        {
            return loopSource;
        }

        if (_runtimeLoopSource == null)
        {
            _runtimeLoopSource = gameObject.AddComponent<AudioSource>();
            ConfigureLoopSource(_runtimeLoopSource);
        }

        return _runtimeLoopSource;
    }

    private AudioSource GetCurrentLoopSource()
    {
        return loopSource ?? _runtimeLoopSource;
    }

    private void ConfigureLoopSource(AudioSource source)
    {
        if (source == null)
            return;

        source.playOnAwake = false;
        source.loop = true;
        source.dopplerLevel = 0f;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (maxLogs < 1) maxLogs = 1;
        if (burnSecondsPerLog < 0f) burnSecondsPerLog = 0f;
        if (intensityToY == null || intensityToY.length == 0)
        {
            intensityToY = AnimationCurve.EaseInOut(0, 0, 1, 1);
        }
        loopVolumeMin = Mathf.Clamp01(loopVolumeMin);
        loopVolumeMax = Mathf.Clamp01(loopVolumeMax);
        if (loopVolumeMax < loopVolumeMin)
        {
            loopVolumeMax = loopVolumeMin;
        }

        if (loopSource != null)
        {
            ConfigureLoopSource(loopSource);
        }
    }
#endif
}

/// <summary>
/// Simple attribute to show read-only fields in the inspector (editor-only visual).
/// </summary>
public sealed class ReadOnlyInspectorAttribute : PropertyAttribute {}

#if UNITY_EDITOR
// Minimal drawer so [ReadOnlyInspector] shows disabled
[UnityEditor.CustomPropertyDrawer(typeof(ReadOnlyInspectorAttribute))]
public class ReadOnlyInspectorDrawer : UnityEditor.PropertyDrawer
{
    public override void OnGUI(UnityEngine.Rect position, UnityEditor.SerializedProperty property, UnityEngine.GUIContent label)
    {
        UnityEngine.GUI.enabled = false;
        UnityEditor.EditorGUI.PropertyField(position, property, label, true);
        UnityEngine.GUI.enabled = true;
    }

    public override float GetPropertyHeight(UnityEditor.SerializedProperty property, UnityEngine.GUIContent label)
        => UnityEditor.EditorGUI.GetPropertyHeight(property, label, true);
}
#endif

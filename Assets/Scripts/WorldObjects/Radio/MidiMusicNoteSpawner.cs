using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using JamesKJamKit.Services;
using JamesKJamKit.Services.Music;

public class MidiMusicNoteSpawner : MonoBehaviour
{
    [Header("Spawn Points")]
    [SerializeField] Transform[] spawnPoints;
    
    [Header("Note Sprites")]
    [SerializeField] Sprite[] noteSprites; // the 3 different note sprites
    
    [Header("Movement")]
    [SerializeField] float floatSpeed = 0.5f;
    [SerializeField] float floatSpeedVariance = 0.2f;
    [SerializeField] float swayAmount = 0.3f;
    [SerializeField] float swaySpeed = 2f;
    [SerializeField] float rotationSpeed = 30f;
    [SerializeField] float maxRotation = 30f;
    
    [Header("Scale")]
    [SerializeField] float baseScale = 1f;
    [SerializeField] float scaleVariance = 0.15f;
    [SerializeField] bool useIntensityForScale = true; // scale notes based on MIDI velocity
    
    [Header("Lifetime")]
    [SerializeField] float lifetimeSeconds = 6f;
    [SerializeField] float fadeOutDuration = 1f;
    [SerializeField] bool autoScaleLifetimeToTempo = true; // automatically adjust lifetime based on song tempo
    [SerializeField] float minLifetimeSeconds = 2f; // minimum lifetime for fast songs
    [SerializeField] float maxLifetimeSeconds = 8f; // maximum lifetime for slow songs
    
    [Header("Sorting")]
    [SerializeField] string sortingLayerName = "Stations-Mug";
    [SerializeField] int sortingOrder = 221;
    
    [Header("Timing")]
    [SerializeField] float lookAheadSeconds = 0.15f; // spawn notes slightly early for visual sync
    [SerializeField] int maxActiveNotes = 50; // safety limit
    [SerializeField] bool cullOldestWhenFull = true; // remove oldest notes if hitting max limit
    
    [Header("Simultaneous Note Handling")]
    [SerializeField] bool deduplicateSimultaneousNotes = true; // only spawn one note when multiple play at same time
    [SerializeField] float simultaneousThresholdSeconds = 0.05f; // notes within this time are considered simultaneous
    [SerializeField] SimultaneousNoteMode simultaneousMode = SimultaneousNoteMode.UseHighestIntensity;
    
    public enum SimultaneousNoteMode
    {
        UseHighestIntensity,  // pick the note with highest velocity
        UseLowestPitch,       // pick the note with lowest spawn point (bass note)
        UseHighestPitch,      // pick the note with highest spawn point (treble note)
        UseFirst              // just use whichever comes first in the list
    }
    
    [Header("Radio Pulse Effect")]
    [SerializeField] bool enableRadioPulse = true;
    [SerializeField] float radioPulseScale = 1.02f; // how much to scale
    [SerializeField] float radioPulseDuration = 0.1f; // how long the pulse lasts

    [Header("Object Pooling")]
    [SerializeField] int poolSize = 75; // pre-allocate more than maxActiveNotes for safety

    // object pooling
    Queue<GameObject> notePool = new Queue<GameObject>();
    List<GameObject> activeNotes = new List<GameObject>();

    // track playback state
    MidiTrackData currentMidiData;
    AudioSource currentAudioSource;
    int nextNoteIndex;
    bool isPlaying;

    // calculated tempo-based parameters
    float calculatedLifetime;
    float calculatedFloatSpeed;

    // deduplication tracking
    float lastSpawnedNoteTime = -1f;

    // mute state tracking (to detect mute/unmute transitions)
    bool wasMutedLastFrame = false;

    // reusable list for simultaneous note detection (optimization: avoid allocations)
    List<MidiNote> reusableSimultaneousNotes = new List<MidiNote>(11); // maxLookahead + 1

    // radio pulse tracking
    bool pulseX = true; // alternates between X and Y

    Vector3 radioOriginalScale;

    Coroutine radioPulseCoroutine;

    // cleanup optimization
    int framesSinceLastCleanup = 0;
    const int CLEANUP_INTERVAL = 60; // clean up every 60 frames instead of every frame
    
    void Start()
    {
        // Debug.Log($"[MidiMusicNoteSpawner] START - Platform: {Application.platform}");
        
        // find spawn points if not assigned
        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            var pointsParent = transform.Find("MusicNotePoints");
            if (pointsParent != null)
            {
                spawnPoints = new Transform[pointsParent.childCount];
                for (int i = 0; i < pointsParent.childCount; i++)
                {
                    spawnPoints[i] = pointsParent.GetChild(i);
                }
                // Debug.Log($"[MidiMusicNoteSpawner] Found {spawnPoints.Length} spawn points from children");
            }
            else
            {
                // Debug.LogError("[MidiMusicNoteSpawner] MusicNotePoints parent not found!");
            }
        }
        else
        {
            // Debug.Log($"[MidiMusicNoteSpawner] Using {spawnPoints.Length} assigned spawn points");
        }

        // Debug.Log($"[MidiMusicNoteSpawner] Note sprites assigned: {noteSprites?.Length ?? 0}");
        radioOriginalScale = transform.localScale;

        // initialize object pool
        InitializePool();

        // subscribe to pause events
        if (PauseController.Instance != null)
        {
            PauseController.Instance.OnPauseChanged += HandlePauseChanged;
        }

        // subscribe to track events
        if (MusicDirectorMidiExtension.Instance != null)
        {
            // Debug.Log("[MidiMusicNoteSpawner] MusicDirectorMidiExtension found, subscribing to events");
            MusicDirectorMidiExtension.Instance.OnTrackStarted += HandleTrackStarted;
            MusicDirectorMidiExtension.Instance.OnTrackStopped += HandleTrackStopped;
            
            // check if a track is already playing (handles late initialization)
            if (MusicDirectorMidiExtension.Instance.CurrentMidiData != null && 
                MusicDirectorMidiExtension.Instance.CurrentAudioSource != null &&
                MusicDirectorMidiExtension.Instance.CurrentAudioSource.isPlaying)
            {
                // Debug.Log("[MidiMusicNoteSpawner] Track already playing on Start, calling HandleTrackStarted");
                HandleTrackStarted(
                    MusicDirectorMidiExtension.Instance.CurrentMidiData,
                    MusicDirectorMidiExtension.Instance.CurrentAudioSource
                );
            }
        }
        else
        {
            // Debug.LogError("[MidiMusicNoteSpawner] MusicDirectorMidiExtension.Instance is NULL!");
            
            // Try to find it manually
            var extension = FindAnyObjectByType<MusicDirectorMidiExtension>();
            if (extension != null)
            {
                // Debug.LogError("[MidiMusicNoteSpawner] Found extension in scene but Instance is null - possible initialization order issue!");
            }
            // else
            // {
            //    Debug.LogError("[MidiMusicNoteSpawner] No MusicDirectorMidiExtension found in scene at all!");
            //}
        }
    }
    
    void OnDestroy()
    {
        StopPlayback();

        // unsubscribe from pause events
        if (PauseController.Instance != null)
        {
            PauseController.Instance.OnPauseChanged -= HandlePauseChanged;
        }

        // unsubscribe from events
        if (MusicDirectorMidiExtension.Instance != null)
        {
            MusicDirectorMidiExtension.Instance.OnTrackStarted -= HandleTrackStarted;
            MusicDirectorMidiExtension.Instance.OnTrackStopped -= HandleTrackStopped;
        }
    }
    
    void HandleTrackStarted(MidiTrackData midiData, AudioSource audioSource)
    {
        // Debug.Log("[MidiMusicNoteSpawner] HandleTrackStarted CALLED");

        currentMidiData = midiData;
        currentAudioSource = audioSource;
        isPlaying = false; // <-- prevent spawning until verified ready
        nextNoteIndex = 0;
        lastSpawnedNoteTime = -1f;

        // Reset radio scale to original as precaution
        transform.localScale = radioOriginalScale;
        
        // Only activate playback if MIDI data actually contains notes
        if (midiData != null && midiData.HasNotes)
        {
            CalculateTempoParameters(midiData);
            isPlaying = true;
            // Debug.Log($"[MidiMusicNoteSpawner] MIDI playback READY with AudioSource (notes={midiData.NoteCount})");
        }
        else
        {
            // Debug.LogWarning("[MidiMusicNoteSpawner] No MIDI notes found, skipping playback.");
        }
    }
    
    void CalculateTempoParameters(MidiTrackData midiData)
    {
        // calculate notes per second (tempo indicator)
        float notesPerSecond = midiData.NoteCount / Mathf.Max(0.1f, midiData.trackDurationSeconds);
        
        if (autoScaleLifetimeToTempo)
        {
            // scale lifetime inversely with tempo
            // fast songs (high notes/sec) = shorter lifetime
            // slow songs (low notes/sec) = longer lifetime
            
            // typical range: 1-10 notes per second
            // map to lifetime range
            float normalizedTempo = Mathf.InverseLerp(1f, 10f, notesPerSecond);
            calculatedLifetime = Mathf.Lerp(maxLifetimeSeconds, minLifetimeSeconds, normalizedTempo);
            
            // also scale float speed to match - faster songs need faster movement
            // so notes clear the screen before too many pile up
            float speedScale = calculatedLifetime / lifetimeSeconds;
            calculatedFloatSpeed = floatSpeed / speedScale;
        }
        else
        {
            // use inspector values
            calculatedLifetime = lifetimeSeconds;
            calculatedFloatSpeed = floatSpeed;
        }
    }
    
    void HandleTrackStopped()
    {
        StopPlayback();
        ReturnAllNotesToPool();
    }

    void HandlePauseChanged(bool paused)
    {
#if UNITY_EDITOR
        Debug.Log($"[MidiMusicNoteSpawner] HandlePauseChanged(paused={paused})");
#endif

        // When unpausing, skip missed notes by advancing nextNoteIndex
        if (!paused)
        {
            SkipMissedNotes("Unpaused");
        }
    }

    void SkipMissedNotes(string reason = "State changed")
    {
        if (currentMidiData == null || currentAudioSource == null)
            return;

        float currentTrackTime = currentAudioSource.time;

        // Fast-forward nextNoteIndex to skip notes that should have played
        int originalIndex = nextNoteIndex;
        while (nextNoteIndex < currentMidiData.notes.Count)
        {
            MidiNote note = currentMidiData.notes[nextNoteIndex];
            double noteSpawnTime = note.timeSeconds - lookAheadSeconds;

            // If this note should have already spawned, skip it
            if (currentTrackTime >= noteSpawnTime)
            {
                nextNoteIndex++;
            }
            else
            {
                // We've caught up to the current time
                break;
            }
        }

        if (nextNoteIndex > originalIndex)
        {
#if UNITY_EDITOR
            Debug.Log($"[MidiMusicNoteSpawner] {reason} - skipped {nextNoteIndex - originalIndex} missed notes (from index {originalIndex} to {nextNoteIndex})");
#endif
        }
    }

    void Update()
    {
        // Log every 60 frames to avoid spam
        if (Time.frameCount % 60 == 0)
        {
            bool musicPlaying = CheckIfMusicPlaying();
            // Debug.Log($"[MidiMusicNoteSpawner] Frame {Time.frameCount}: musicPlaying={musicPlaying}, isPlaying={isPlaying}, currentMidiData={(currentMidiData != null ? "EXISTS" : "NULL")}, nextNoteIndex={nextNoteIndex}/{(currentMidiData?.notes.Count ?? 0)}");
        }

        // detect mute/unmute transitions and skip missed notes when unmuting
        bool currentlyMuted = RadioButtons.IsMuted();
        if (wasMutedLastFrame && !currentlyMuted)
        {
            // just unmuted - skip notes that should have played during muted period
            SkipMissedNotes("Unmuted");
        }
        wasMutedLastFrame = currentlyMuted;

        // check if music is playing and should spawn notes
        bool shouldBeSpawning = CheckIfMusicPlaying();

        if (shouldBeSpawning && !isPlaying)
        {
            // Debug.Log("[MidiMusicNoteSpawner] Update: Starting playback");
            StartPlayback();
        }
        else if (!shouldBeSpawning && isPlaying)
        {
            // when muted, just pause spawning but DON'T reset nextNoteIndex
            // this way we stay synced when unmuted
            // Debug.Log("[MidiMusicNoteSpawner] Update: Pausing playback (muted)");
            isPlaying = false;
        }

        // spawn notes based on timing
        if (isPlaying && currentMidiData != null)
        {
            UpdateNoteSpawning();
        }

        // cleanup destroyed/inactive notes periodically (optimization: reduce GC pressure)
        framesSinceLastCleanup++;
        if (framesSinceLastCleanup >= CLEANUP_INTERVAL)
        {
            framesSinceLastCleanup = 0;
            CleanupInactiveNotes();
        }
    }
    
    bool CheckIfMusicPlaying()
    {
        // check if muted
        if (RadioButtons.IsMuted())
        {
            // if (Time.frameCount % 120 == 0) Debug.Log("[MidiMusicNoteSpawner] CheckIfMusicPlaying: Radio is muted");
            return false;
        }
        
        // check if MusicDirector has active music
        var musicDirector = MusicDirector.Instance;
        if (musicDirector == null)
        {
            // if (Time.frameCount % 120 == 0) Debug.LogWarning("[MidiMusicNoteSpawner] CheckIfMusicPlaying: MusicDirector.Instance is NULL");
            return false;
        }
        
        bool isNotPaused = musicDirector.CurrentState != MusicDirector.MusicState.Paused;
        // if (Time.frameCount % 120 == 0) Debug.Log($"[MidiMusicNoteSpawner] CheckIfMusicPlaying: State={musicDirector.CurrentState}, returning {isNotPaused}");
        
        return isNotPaused;
    }
    
    void StartPlayback()
    {
        var musicDirector = MusicDirector.Instance;
        if (musicDirector == null)
            return;
        
        if (currentMidiData != null && currentMidiData.HasNotes)
        {
            isPlaying = true;
            // Debug.Log($"[MidiMusicNoteSpawner] Resumed playback");
        }
    }
    
    void StopPlayback()
    {
        isPlaying = false;
        currentMidiData = null;
        nextNoteIndex = 0;
        
        // Debug.Log("[MidiMusicNoteSpawner] Stopped playback");
    }
    
    void UpdateNoteSpawning()
    {
        if (currentMidiData == null || nextNoteIndex >= currentMidiData.notes.Count)
            return;
        
        if (currentAudioSource == null || !currentAudioSource.isPlaying)
            return;
        
        // use AudioSource.time directly - works perfectly on all platforms including WebGL!
        float currentTrackTime = currentAudioSource.time;
        
        // spawn all notes that should appear now (with look-ahead)
        while (nextNoteIndex < currentMidiData.notes.Count)
        {
            MidiNote note = currentMidiData.notes[nextNoteIndex];
            double noteSpawnTime = note.timeSeconds - lookAheadSeconds;
            
            // check if it's time to spawn this note
            if (currentTrackTime >= noteSpawnTime)
            {
                // handle simultaneous notes (notes at the same time)
                if (deduplicateSimultaneousNotes && ShouldSkipSimultaneousNote(note, nextNoteIndex))
                {
                    nextNoteIndex++;
                    continue;
                }
                
                // check active note count and cull if needed
                if (activeNotes.Count >= maxActiveNotes)
                {
                    if (cullOldestWhenFull && activeNotes.Count > 0)
                    {
                        // return oldest note to pool to make room
                        GameObject oldestNote = activeNotes[0];
                        if (oldestNote != null)
                        {
                            ReturnNoteToPool(oldestNote);
                        }
                        activeNotes.RemoveAt(0);
                    }
                    else
                    {
                        // skip spawning this note
                        nextNoteIndex++;
                        continue;
                    }
                }
                
                SpawnNote(note);
                lastSpawnedNoteTime = note.timeSeconds;
                nextNoteIndex++;
            }
            else
            {
                // notes are sorted by time, so we can stop checking
                break;
            }
        }
    }
    
    bool ShouldSkipSimultaneousNote(MidiNote currentNote, int currentIndex)
    {
        // check if this note is too close to the last spawned note
        if (lastSpawnedNoteTime >= 0 && 
            Mathf.Abs(currentNote.timeSeconds - lastSpawnedNoteTime) <= simultaneousThresholdSeconds)
        {
            // this note is simultaneous with the last one, skip it
            return true;
        }
        
        // look ahead to see if there are better notes at the same time
        // limit lookahead to prevent performance issues with large note counts
        const int maxLookahead = 10;

        // use reusable list (optimization: avoid allocations)
        reusableSimultaneousNotes.Clear();
        reusableSimultaneousNotes.Add(currentNote);

        // gather notes within the simultaneous threshold (up to maxLookahead)
        int lookaheadCount = 0;
        for (int i = currentIndex + 1; i < currentMidiData.notes.Count && lookaheadCount < maxLookahead; i++)
        {
            MidiNote nextNote = currentMidiData.notes[i];

            float timeDiff = nextNote.timeSeconds - currentNote.timeSeconds;

            // early exit if we've gone past the threshold
            if (timeDiff > simultaneousThresholdSeconds)
                break;

            reusableSimultaneousNotes.Add(nextNote);
            lookaheadCount++;
        }

        // if only one note, don't skip
        if (reusableSimultaneousNotes.Count <= 1)
            return false;

        // pick the best note based on mode
        MidiNote selectedNote = SelectBestSimultaneousNote(reusableSimultaneousNotes);

        // skip this note if it's not the selected one
        return !NotesAreEqual(currentNote, selectedNote);
    }
    
    MidiNote SelectBestSimultaneousNote(List<MidiNote> notes)
    {
        switch (simultaneousMode)
        {
            case SimultaneousNoteMode.UseHighestIntensity:
                // pick note with highest velocity
                MidiNote highest = notes[0];
                foreach (var note in notes)
                {
                    if (note.intensity > highest.intensity)
                        highest = note;
                }
                return highest;
                
            case SimultaneousNoteMode.UseLowestPitch:
                // pick note with lowest spawn point (bass)
                MidiNote lowest = notes[0];
                foreach (var note in notes)
                {
                    if (note.spawnPointIndex < lowest.spawnPointIndex)
                        lowest = note;
                }
                return lowest;
                
            case SimultaneousNoteMode.UseHighestPitch:
                // pick note with highest spawn point (treble)
                MidiNote highestPitch = notes[0];
                foreach (var note in notes)
                {
                    if (note.spawnPointIndex > highestPitch.spawnPointIndex)
                        highestPitch = note;
                }
                return highestPitch;
                
            case SimultaneousNoteMode.UseFirst:
            default:
                return notes[0];
        }
    }
    
    bool NotesAreEqual(MidiNote a, MidiNote b)
    {
        return Mathf.Approximately(a.timeSeconds, b.timeSeconds) &&
               a.spawnPointIndex == b.spawnPointIndex &&
               a.noteTypeIndex == b.noteTypeIndex &&
               Mathf.Approximately(a.intensity, b.intensity);
    }
    
    void SpawnNote(MidiNote midiNote)
    {
        // Debug.Log($"[MidiMusicNoteSpawner] ★ SpawnNote CALLED for note at {midiNote.timeSeconds:F3}s");

        if (spawnPoints == null || spawnPoints.Length == 0)
        {
#if UNITY_EDITOR
            Debug.LogError("[MidiMusicNoteSpawner] ✗ SPAWN FAILED: No spawn points!");
#endif
            return;
        }

        if (noteSprites == null || noteSprites.Length == 0)
        {
            // Debug.LogError("[MidiMusicNoteSpawner] ✗ SPAWN FAILED: No note sprites!");
            return;
        }

        // Debug.Log($"[MidiMusicNoteSpawner] Spawning at point {midiNote.spawnPointIndex}, sprite {midiNote.noteTypeIndex}, intensity {midiNote.intensity:F2}");

        // trigger radio pulse effect
        if (enableRadioPulse)
        {
            TriggerRadioPulse();
        }

        // get spawn point (clamp to valid range)
        int spawnIndex = Mathf.Clamp(midiNote.spawnPointIndex, 0, spawnPoints.Length - 1);
        Transform spawnPoint = spawnPoints[spawnIndex];

        // get sprite (clamp to valid range)
        int spriteIndex = Mathf.Clamp(midiNote.noteTypeIndex, 0, noteSprites.Length - 1);
        Sprite noteSprite = noteSprites[spriteIndex];

        // get note from pool (optimization: reuse GameObjects instead of creating new ones)
        GameObject note = GetNoteFromPool();
        note.transform.position = spawnPoint.position;

        // calculate scale (base + variance + optional intensity)
        float scale = baseScale + Random.Range(-scaleVariance, scaleVariance);
        if (useIntensityForScale)
        {
            scale *= Mathf.Lerp(0.7f, 1.3f, midiNote.intensity);
        }
        note.transform.localScale = Vector3.one * scale;

        // update sprite renderer
        SpriteRenderer renderer = note.GetComponent<SpriteRenderer>();
        renderer.sprite = noteSprite;

        // use intensity for alpha/brightness
        if (useIntensityForScale)
        {
            Color color = renderer.color;
            color.a = Mathf.Lerp(0.7f, 1f, midiNote.intensity);
            renderer.color = color;
        }
        else
        {
            Color color = renderer.color;
            color.a = 1f;
            renderer.color = color;
        }

        // randomize speed slightly
        float randomFloatSpeed = calculatedFloatSpeed + Random.Range(-floatSpeedVariance, floatSpeedVariance);

        // reset and initialize behavior component
        MidiMusicNote noteComponent = note.GetComponent<MidiMusicNote>();
        noteComponent.ResetAndInitialize(
            this,
            randomFloatSpeed,
            swayAmount,
            swaySpeed,
            rotationSpeed,
            maxRotation,
            calculatedLifetime,
            fadeOutDuration
        );

        activeNotes.Add(note);

        // Debug.Log($"[MidiMusicNoteSpawner] ✓ Note spawned successfully! Active notes: {activeNotes.Count}");
    }
    
    // manual testing helper - call this to load specific MIDI data
    public void SetMidiData(MidiTrackData midiData)
    {
        currentMidiData = midiData;
        
        if (isPlaying)
        {
            // restart playback with new data
            nextNoteIndex = 0;
            // Debug.Log($"[MidiMusicNoteSpawner] Loaded MIDI data with {midiData.NoteCount} notes");
        }
    }
    
    void TriggerRadioPulse()
    {
        // stop previous pulse if still running
        if (radioPulseCoroutine != null)
        {
            StopCoroutine(radioPulseCoroutine);
        }
        
        radioPulseCoroutine = StartCoroutine(RadioPulseCoroutine());
    }
    
    IEnumerator RadioPulseCoroutine()
    {
        Vector3 pulseScale = radioOriginalScale;
        
        // alternate between X and Y
        if (pulseX)
        {
            pulseScale.x *= radioPulseScale;
        }
        else
        {
            pulseScale.y *= radioPulseScale;
        }
        
        // toggle for next note
        pulseX = !pulseX;
        
        // pulse to larger scale
        float elapsed = 0f;
        float halfDuration = radioPulseDuration * 0.5f;
        
        // scale up
        while (elapsed < halfDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / halfDuration;
            transform.localScale = Vector3.Lerp(radioOriginalScale, pulseScale, t);
            yield return null;
        }
        
        transform.localScale = pulseScale;
        
        // scale back down
        elapsed = 0f;
        while (elapsed < halfDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / halfDuration;
            transform.localScale = Vector3.Lerp(pulseScale, radioOriginalScale, t);
            yield return null;
        }
        
        transform.localScale = radioOriginalScale;
        radioPulseCoroutine = null;
    }
    
    // ==================== OBJECT POOLING METHODS ====================

    void InitializePool()
    {
        // pre-create pooled notes
        for (int i = 0; i < poolSize; i++)
        {
            GameObject note = CreateNewNoteObject();
            note.SetActive(false);
            notePool.Enqueue(note);
        }
    }

    GameObject CreateNewNoteObject()
    {
        // optimization: use constant name in builds to avoid string allocations
        #if UNITY_EDITOR
        GameObject note = new GameObject("MidiNote (Pooled)");
        #else
        GameObject note = new GameObject("MidiNote");
        #endif

        note.transform.SetParent(transform);

        // add sprite renderer
        SpriteRenderer renderer = note.AddComponent<SpriteRenderer>();
        renderer.sortingLayerName = sortingLayerName;
        renderer.sortingOrder = sortingOrder;

        // add behavior component
        note.AddComponent<MidiMusicNote>();

        return note;
    }

    GameObject GetNoteFromPool()
    {
        GameObject note;

        if (notePool.Count > 0)
        {
            note = notePool.Dequeue();
        }
        else
        {
            // pool exhausted, create new note (rare case)
            note = CreateNewNoteObject();
#if UNITY_EDITOR
            Debug.LogWarning($"[MidiMusicNoteSpawner] Pool exhausted! Creating new note. Consider increasing poolSize (current: {poolSize})");
#endif
        }

        note.SetActive(true);
        return note;
    }

    public void ReturnNoteToPool(GameObject note)
    {
        if (note == null)
            return;

        note.SetActive(false);
        notePool.Enqueue(note);
    }

    void ReturnAllNotesToPool()
    {
        // return all active notes to pool immediately
        for (int i = activeNotes.Count - 1; i >= 0; i--)
        {
            if (activeNotes[i] != null)
            {
                ReturnNoteToPool(activeNotes[i]);
            }
        }
        activeNotes.Clear();
    }

    void CleanupInactiveNotes()
    {
        // manual cleanup to avoid lambda allocation from RemoveAll
        for (int i = activeNotes.Count - 1; i >= 0; i--)
        {
            if (activeNotes[i] == null || !activeNotes[i].activeInHierarchy)
            {
                activeNotes.RemoveAt(i);
            }
        }
    }

    // ==================== DEBUG VISUALIZATION ====================

    // debug visualization
    void OnDrawGizmos()
    {
        if (spawnPoints == null || spawnPoints.Length == 0)
            return;
        
        // draw spawn points
        Gizmos.color = Color.cyan;
        for (int i = 0; i < spawnPoints.Length; i++)
        {
            if (spawnPoints[i] != null)
            {
                Gizmos.DrawWireSphere(spawnPoints[i].position, 0.1f);
                
                #if UNITY_EDITOR
                UnityEditor.Handles.Label(spawnPoints[i].position + Vector3.up * 0.2f, $"Point {i}");
                #endif
            }
        }
    }
}

// ==================== MIDI MUSIC NOTE BEHAVIOR COMPONENT ====================

public class MidiMusicNote : MonoBehaviour
{
    MidiMusicNoteSpawner spawner;
    float floatSpeed;
    float swayAmount;
    float swaySpeed;
    float rotationSpeed;
    float maxRotation;
    float lifetimeSeconds;
    float fadeOutDuration;

    float aliveTime = 0f;
    float swayDirection; // 1 or -1 to randomly reverse the sway direction
    float rotationDirection;
    float startX;
    SpriteRenderer _spriteRenderer;

    SpriteRenderer SpriteRenderer
    {
        get
        {
            if (_spriteRenderer == null)
                _spriteRenderer = GetComponent<SpriteRenderer>();
            return _spriteRenderer;
        }
    }

    // pooling-compatible initialization
    public void ResetAndInitialize(MidiMusicNoteSpawner noteSpawner, float floatSpd, float swayAmt, float swaySpd, float rotSpd, float maxRot, float lifetime, float fadeDuration)
    {
        spawner = noteSpawner;
        floatSpeed = floatSpd;
        swayAmount = swayAmt;
        swaySpeed = swaySpd;
        rotationSpeed = rotSpd;
        maxRotation = maxRot;
        lifetimeSeconds = lifetime;
        fadeOutDuration = fadeDuration;

        aliveTime = 0f;
        startX = transform.position.x;

        // randomly reverse sway direction for variety (some go left first, some right first)
        swayDirection = Random.value > 0.5f ? 1f : -1f;
        rotationDirection = Random.Range(-1f, 1f);
    }

    // legacy initialization for compatibility (if needed)
    public void Initialize(float floatSpd, float swayAmt, float swaySpd, float rotSpd, float maxRot, float lifetime, float fadeDuration)
    {
        floatSpeed = floatSpd;
        swayAmount = swayAmt;
        swaySpeed = swaySpd;
        rotationSpeed = rotSpd;
        maxRotation = maxRot;
        lifetimeSeconds = lifetime;
        fadeOutDuration = fadeDuration;

        aliveTime = 0f;
        startX = transform.position.x;

        // randomly reverse sway direction for variety
        swayDirection = Random.value > 0.5f ? 1f : -1f;
        rotationDirection = Random.Range(-1f, 1f);
    }

    void Update()
    {
        aliveTime += Time.deltaTime;

        // sway starts at 0 when aliveTime = 0, then oscillates
        // sin(0) = 0, so no jump on first frame
        float swayX = Mathf.Sin(aliveTime * swaySpeed) * swayAmount * swayDirection;

        // update position: float up + sway horizontally
        Vector3 pos = transform.position;
        pos.y += floatSpeed * Time.deltaTime;
        pos.x = startX + swayX;
        transform.position = pos;

        // rotate
        float rotationDelta = rotationDirection * rotationSpeed * Time.deltaTime;
        float currentZ = transform.eulerAngles.z;

        // normalize angle to -180 to 180 range
        if (currentZ > 180f) currentZ -= 360f;

        float newZ = Mathf.Clamp(currentZ + rotationDelta, -maxRotation, maxRotation);
        transform.rotation = Quaternion.Euler(0f, 0f, newZ);

        // fade out near end of lifetime
        if (aliveTime >= lifetimeSeconds - fadeOutDuration)
        {
            float fadeProgress = (aliveTime - (lifetimeSeconds - fadeOutDuration)) / fadeOutDuration;
            Color color = SpriteRenderer.color;
            color.a = Mathf.Lerp(color.a, 0f, fadeProgress);
            SpriteRenderer.color = color;
        }

        // return to pool after lifetime (optimization: reuse instead of destroy)
        if (aliveTime >= lifetimeSeconds)
        {
            if (spawner != null)
            {
                spawner.ReturnNoteToPool(gameObject);
            }
            else
            {
                // fallback if no spawner reference (shouldn't happen with pooling)
                Destroy(gameObject);
            }
        }
    }
}
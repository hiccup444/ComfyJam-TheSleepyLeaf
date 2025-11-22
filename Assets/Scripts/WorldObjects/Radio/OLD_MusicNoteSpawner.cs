using System.Collections.Generic;
using UnityEngine;
public class MusicNoteSpawner : MonoBehaviour
{
    [Header("Spawn Points")]
    [SerializeField] Transform[] spawnPoints;
    
    [Header("Note Sprites")]
    [SerializeField] Sprite[] noteSprites; // the 3 different note sprites
    
    [Header("Spawn Settings")]
    [SerializeField] float spawnInterval = 0.5f; // time between spawns
    [SerializeField] [Range(0f, 1f)] float spawnChance = 0.7f; // chance to spawn per interval
    [SerializeField] int maxActiveNotes = 17;
    
    [Header("Movement")]
    [SerializeField] float floatSpeed = 0.5f; // upward speed
    [SerializeField] float floatSpeedVariance = 0.2f; // random variation (+/-)
    [SerializeField] float swayAmount = 0.3f; // max horizontal sway distance
    [SerializeField] float swaySpeed = 2f; // speed of sway oscillation
    [SerializeField] float rotationSpeed = 30f; // degrees per second
    [SerializeField] float maxRotation = 30f; // max Z rotation angle
    
    [Header("Scale")]
    [SerializeField] float baseScale = 1f;
    [SerializeField] float scaleVariance = 0.15f; // random variation (+/-)
    
    [Header("Lifetime")]
    [SerializeField] float lifetimeSeconds = 6f;
    [SerializeField] float fadeOutDuration = 1f; // how long fade takes
    
    [Header("Sorting")]
    [SerializeField] string sortingLayerName = "Default";
    [SerializeField] int sortingOrder = 100;
    
    List<GameObject> activeNotes = new List<GameObject>();
    float nextSpawnMusicTime = 0f;

    void Start()
    {
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
            }
        }

        // Subscribe to pause events
        if (JamesKJamKit.Services.PauseController.Instance != null)
        {
            JamesKJamKit.Services.PauseController.Instance.OnPauseChanged += HandlePauseChanged;
        }
    }

    void OnDestroy()
    {
        // Unsubscribe from pause events
        if (JamesKJamKit.Services.PauseController.Instance != null)
        {
            JamesKJamKit.Services.PauseController.Instance.OnPauseChanged -= HandlePauseChanged;
        }
    }

    void HandlePauseChanged(bool paused)
    {
#if UNITY_EDITOR
        Debug.Log($"[MusicNoteSpawner] HandlePauseChanged(paused={paused})");
#endif

        // When unpausing, skip missed spawns by advancing to current music time
        if (!paused)
        {
            var musicDirector = JamesKJamKit.Services.MusicDirector.Instance;
            if (musicDirector != null && musicDirector.ActiveSource != null)
            {
                float currentMusicTime = musicDirector.ActiveSource.time;
                if (currentMusicTime > nextSpawnMusicTime)
                {
#if UNITY_EDITOR
                    Debug.Log($"[MusicNoteSpawner] Unpaused - skipping missed spawns. Advancing from {nextSpawnMusicTime:F2} to {currentMusicTime:F2}");
#endif
                    nextSpawnMusicTime = currentMusicTime;
                }
            }
        }
    }

    void Update()
    {
        // cleanup destroyed notes from list
        activeNotes.RemoveAll(n => n == null);

        // skip if music is not playing (paused or muted)
        if (!CheckIfMusicPlaying())
            return;

        var musicDirector = JamesKJamKit.Services.MusicDirector.Instance;
        if (musicDirector == null || musicDirector.ActiveSource == null)
            return;

        float currentMusicTime = musicDirector.ActiveSource.time;

        // check if we've reached or passed the next spawn time
        if (currentMusicTime >= nextSpawnMusicTime)
        {
#if UNITY_EDITOR
            Debug.Log($"[MusicNoteSpawner] Spawn time reached - MusicTime: {currentMusicTime:F2}, NextSpawn: {nextSpawnMusicTime:F2}");
#endif

            // schedule next spawn
            nextSpawnMusicTime = currentMusicTime + spawnInterval;

            // check if at max capacity
            if (activeNotes.Count >= maxActiveNotes)
            {
#if UNITY_EDITOR
                Debug.Log("[MusicNoteSpawner] Skipping spawn - at max capacity");
#endif
                return;
            }

            // random chance to spawn
            if (Random.value <= spawnChance)
            {
#if UNITY_EDITOR
                Debug.Log("[MusicNoteSpawner] Spawning note");
#endif
                SpawnNote();
            }
            else
            {
#if UNITY_EDITOR
                Debug.Log("[MusicNoteSpawner] Skipping spawn - random chance failed");
#endif
            }
        }
    }
    
    bool CheckIfMusicPlaying()
    {
        // check if muted via RadioButtons
        if (RadioButtons.IsMuted())
            return false;

        // check if MusicDirector has active music
        var musicDirector = JamesKJamKit.Services.MusicDirector.Instance;
        if (musicDirector == null)
            return false;

        // music is playing if not in paused state
        return musicDirector.CurrentState != JamesKJamKit.Services.MusicDirector.MusicState.Paused;
    }
    
    void SpawnNote()
    {
        if (spawnPoints == null || spawnPoints.Length == 0)
        {
#if UNITY_EDITOR
            Debug.LogWarning("[MusicNoteSpawner] No spawn points assigned!");
#endif
            return;
        }

        if (noteSprites == null || noteSprites.Length == 0)
        {
#if UNITY_EDITOR
            Debug.LogWarning("[MusicNoteSpawner] No note sprites assigned!");
#endif
            return;
        }
        
        // pick random spawn point and sprite
        Transform spawnPoint = spawnPoints[Random.Range(0, spawnPoints.Length)];
        Sprite noteSprite = noteSprites[Random.Range(0, noteSprites.Length)];
        
        // create note gameobject
        GameObject note = new GameObject("MusicNote");
        note.transform.position = spawnPoint.position;
        note.transform.SetParent(transform); // parent to spawner for organization
        
        // randomize scale
        float scale = baseScale + Random.Range(-scaleVariance, scaleVariance);
        note.transform.localScale = Vector3.one * scale;
        
        // add sprite renderer
        SpriteRenderer renderer = note.AddComponent<SpriteRenderer>();
        renderer.sprite = noteSprite;
        renderer.sortingLayerName = sortingLayerName;
        renderer.sortingOrder = sortingOrder;
        
        // randomize speed
        float randomFloatSpeed = floatSpeed + Random.Range(-floatSpeedVariance, floatSpeedVariance);
        
        // add behavior component
        MusicNote noteComponent = note.AddComponent<MusicNote>();
        noteComponent.Initialize(
            randomFloatSpeed,
            swayAmount,
            swaySpeed,
            rotationSpeed,
            maxRotation,
            lifetimeSeconds,
            fadeOutDuration
        );
        
        activeNotes.Add(note);
    }
    
}

public class MusicNote : MonoBehaviour
{
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
    
    public void Initialize(float floatSpd, float swayAmt, float swaySpd, float rotSpd, float maxRot, float lifetime, float fadeDuration)
    {
        floatSpeed = floatSpd;
        swayAmount = swayAmt;
        swaySpeed = swaySpd;
        rotationSpeed = rotSpd;
        maxRotation = maxRot;
        lifetimeSeconds = lifetime;
        fadeOutDuration = fadeDuration;
        
        startX = transform.position.x;
        
        // randomly reverse sway direction for variety (some go left first, some right first)
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
            color.a = 1f - fadeProgress;
            SpriteRenderer.color = color;
        }
        
        // destroy after lifetime
        if (aliveTime >= lifetimeSeconds)
        {
            Destroy(gameObject);
        }
    }
}
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class DustParticleSpawner : MonoBehaviour
{
    [Header("Spawn Configuration")]
    [SerializeField] Sprite dustSprite;
    [SerializeField] int gridColumns = 10;
    [SerializeField] int gridRows = 10;

    [Header("Parenting")]
    [SerializeField] Transform particleBehindAddons;
    [SerializeField] Transform particleAboveAddons;
    [SerializeField, Range(0f, 1f)] float behindAddonsRatio = 0.8f;

    [Header("Scale")]
    [SerializeField] float minScale = 0.1f;
    [SerializeField] float maxScale = 0.4f;

    [Header("Opacity")]
    [SerializeField] float fadeInDuration = 0.5f;
    [SerializeField] float minOpacity = 0.1f;
    [SerializeField] float maxOpacity = 0.45f;
    [SerializeField] Color dustTint = new Color(1f, 0.9f, 0.6f, 1f); // warm yellow tint

    [Header("Movement")]
    [SerializeField] float moveSpeed = 8f;
    [SerializeField] float moveSpeedVariance = 4f;
    [SerializeField] float noiseScale = 0.3f; // how chaotic the movement is
    [SerializeField] float directionChangeSpeed = 0.5f; // how quickly direction changes

    [Header("Lifetime")]
    [SerializeField] float minLifetime = 8f;
    [SerializeField] float maxLifetime = 15f;
    [SerializeField] float fadeOutDuration = 1f;

    [Header("Spawning")]
    [SerializeField] float spawnInterval = 0.3f;
    [SerializeField] int poolSize = 40;
    [SerializeField] int maxActiveParticles = 30;

    [Header("Sorting")]
    [SerializeField] Canvas canvas;

    // object pooling
    Queue<GameObject> particlePool = new Queue<GameObject>();
    List<GameObject> activeParticles = new List<GameObject>();

    // spawn timing
    float nextSpawnTime;

    // grid spawn system
    float cellWidth;
    float cellHeight;
    RectTransform canvasRectTransform;

    void Start()
    {
        // auto-find canvas if not assigned
        if (canvas == null)
        {
            canvas = GetComponentInParent<Canvas>();
        }

        // get canvas RectTransform and calculate grid
        if (canvas != null)
        {
            canvasRectTransform = canvas.GetComponent<RectTransform>();
            Vector2 canvasSize = canvasRectTransform.sizeDelta;
            cellWidth = canvasSize.x / gridColumns;
            cellHeight = canvasSize.y / gridRows;
        }

        // auto-find parent transforms if not assigned
        if (particleBehindAddons == null)
        {
            particleBehindAddons = transform.Find("ParticleBehindAddons");
        }

        if (particleAboveAddons == null)
        {
            particleAboveAddons = transform.Find("ParticleAboveAddons");
        }

        // initialize pool
        InitializePool();

        // set first spawn time
        nextSpawnTime = Time.time + Random.Range(0f, spawnInterval);
    }

    void Update()
    {
        // spawn new particles periodically
        if (Time.time >= nextSpawnTime && activeParticles.Count < maxActiveParticles)
        {
            SpawnParticle();
            nextSpawnTime = Time.time + spawnInterval + Random.Range(-spawnInterval * 0.3f, spawnInterval * 0.3f);
        }

        // cleanup inactive particles
        CleanupInactiveParticles();
    }

    void InitializePool()
    {
        for (int i = 0; i < poolSize; i++)
        {
            GameObject particle = CreateNewParticle();
            particle.SetActive(false);
            particlePool.Enqueue(particle);
        }
    }

    GameObject CreateNewParticle()
    {
        GameObject particle = new GameObject("Dust");

        // determine parent (80/20 split)
        Transform parent = Random.value < behindAddonsRatio ? particleBehindAddons : particleAboveAddons;
        particle.transform.SetParent(parent, false);

        // add Image component for UI rendering
        Image image = particle.AddComponent<Image>();
        image.sprite = dustSprite;
        image.raycastTarget = false;
        image.color = new Color(dustTint.r, dustTint.g, dustTint.b, 0f); // start transparent with warm yellow tint

        // add RectTransform (automatically added with Image)
        RectTransform rectTransform = particle.GetComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);

        // add behavior component
        particle.AddComponent<DustParticleBehavior>();

        return particle;
    }

    GameObject GetParticleFromPool()
    {
        GameObject particle;

        if (particlePool.Count > 0)
        {
            particle = particlePool.Dequeue();
        }
        else
        {
            // pool exhausted, create new particle
            particle = CreateNewParticle();
        }

        // re-assign parent (re-randomize 80/20 split for reused particles)
        Transform parent = Random.value < behindAddonsRatio ? particleBehindAddons : particleAboveAddons;
        particle.transform.SetParent(parent, false);

        particle.SetActive(true);
        return particle;
    }

    public void ReturnParticleToPool(GameObject particle)
    {
        if (particle == null)
            return;

        particle.SetActive(false);
        particlePool.Enqueue(particle);
    }

    void SpawnParticle()
    {
        if (dustSprite == null)
            return;

        // get particle from pool
        GameObject particle = GetParticleFromPool();

        // generate random spawn position using grid system
        int gridX = Random.Range(0, gridColumns);
        int gridY = Random.Range(0, gridRows);

        // random point within the selected cell
        float randomX = Random.Range(gridX * cellWidth, (gridX + 1) * cellWidth);
        float randomY = Random.Range(gridY * cellHeight, (gridY + 1) * cellHeight);

        // convert from canvas-space (0,0 at bottom-left) to anchored position (centered)
        Vector2 canvasSize = canvasRectTransform.sizeDelta;
        Vector2 anchoredPos = new Vector2(
            randomX - canvasSize.x * 0.5f,
            randomY - canvasSize.y * 0.5f
        );

        // set position
        RectTransform rectTransform = particle.GetComponent<RectTransform>();
        rectTransform.anchoredPosition = anchoredPos;

        // random scale
        float scale = Random.Range(minScale, maxScale);
        rectTransform.localScale = Vector3.one * scale;

        // random opacity target
        float targetOpacity = Random.Range(minOpacity, maxOpacity);

        // random lifetime
        float lifetime = Random.Range(minLifetime, maxLifetime);

        // random movement parameters
        float particleMoveSpeed = moveSpeed + Random.Range(-moveSpeedVariance, moveSpeedVariance);
        float noiseOffsetX = Random.Range(0f, 1000f); // random start in noise field
        float noiseOffsetY = Random.Range(0f, 1000f);

        // initialize behavior
        DustParticleBehavior behavior = particle.GetComponent<DustParticleBehavior>();
        behavior.Initialize(
            this,
            particleMoveSpeed,
            noiseScale,
            directionChangeSpeed,
            noiseOffsetX,
            noiseOffsetY,
            lifetime,
            fadeInDuration,
            fadeOutDuration,
            targetOpacity,
            dustTint
        );

        activeParticles.Add(particle);
    }

    void CleanupInactiveParticles()
    {
        for (int i = activeParticles.Count - 1; i >= 0; i--)
        {
            if (activeParticles[i] == null || !activeParticles[i].activeInHierarchy)
            {
                activeParticles.RemoveAt(i);
            }
        }
    }
}

public class DustParticleBehavior : MonoBehaviour
{
    DustParticleSpawner spawner;
    float moveSpeed;
    float noiseScale;
    float directionChangeSpeed;
    float noiseOffsetX;
    float noiseOffsetY;
    float lifetime;
    float fadeInDuration;
    float fadeOutDuration;
    float targetOpacity;
    Color tintColor;

    float aliveTime;
    Image _image;
    RectTransform _rectTransform;

    Image ImageComponent
    {
        get
        {
            if (_image == null)
                _image = GetComponent<Image>();
            return _image;
        }
    }

    RectTransform RectTransform
    {
        get
        {
            if (_rectTransform == null)
                _rectTransform = GetComponent<RectTransform>();
            return _rectTransform;
        }
    }

    public void Initialize(
        DustParticleSpawner particleSpawner,
        float speed,
        float noise,
        float changeSpeed,
        float offsetX,
        float offsetY,
        float life,
        float fadeIn,
        float fadeOut,
        float opacity,
        Color tint)
    {
        spawner = particleSpawner;
        moveSpeed = speed;
        noiseScale = noise;
        directionChangeSpeed = changeSpeed;
        noiseOffsetX = offsetX;
        noiseOffsetY = offsetY;
        lifetime = life;
        fadeInDuration = fadeIn;
        fadeOutDuration = fadeOut;
        targetOpacity = opacity;
        tintColor = tint;

        aliveTime = 0f;

        // reset color to transparent with tint
        Color color = new Color(tintColor.r, tintColor.g, tintColor.b, 0f);
        ImageComponent.color = color;
    }

    void Update()
    {
        aliveTime += Time.deltaTime;

        // use Perlin noise for smooth, organic movement in all directions
        float noiseTime = aliveTime * directionChangeSpeed;

        // sample 2D Perlin noise for X and Y direction
        // use different offsets so X and Y movements are independent
        float noiseX = Mathf.PerlinNoise(noiseOffsetX + noiseTime, noiseOffsetX);
        float noiseY = Mathf.PerlinNoise(noiseOffsetY, noiseOffsetY + noiseTime);

        // convert noise from 0-1 range to -1 to 1 range for bidirectional movement
        float dirX = (noiseX - 0.5f) * 2f;
        float dirY = (noiseY - 0.5f) * 2f;

        // apply noise scale and move speed
        Vector2 movement = new Vector2(dirX, dirY) * noiseScale * moveSpeed * Time.deltaTime;

        // update position
        Vector2 pos = RectTransform.anchoredPosition;
        pos += movement;
        RectTransform.anchoredPosition = pos;

        // fade in
        float currentAlpha;
        if (aliveTime < fadeInDuration)
        {
            float fadeProgress = aliveTime / fadeInDuration;
            currentAlpha = Mathf.Lerp(0f, targetOpacity, fadeProgress);
        }
        // fade out near end
        else if (aliveTime >= lifetime - fadeOutDuration)
        {
            float fadeProgress = (aliveTime - (lifetime - fadeOutDuration)) / fadeOutDuration;
            currentAlpha = Mathf.Lerp(targetOpacity, 0f, fadeProgress);
        }
        else
        {
            currentAlpha = targetOpacity;
        }

        // apply alpha while preserving tint
        Color color = new Color(tintColor.r, tintColor.g, tintColor.b, currentAlpha);
        ImageComponent.color = color;

        // return to pool after lifetime
        if (aliveTime >= lifetime)
        {
            if (spawner != null)
            {
                spawner.ReturnParticleToPool(gameObject);
            }
            else
            {
                Destroy(gameObject);
            }
        }
    }
}

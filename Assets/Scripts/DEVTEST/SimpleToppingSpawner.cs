// Assets/Scripts/Toppings/SimpleToppingSpawner.cs
using System.Collections.Generic;
using UnityEngine;

public class SimpleToppingSpawner : MonoBehaviour
{
    [Header("Sprites")]
    public Sprite[] sprites;

    [Header("Pooling & Limits")]
    public int poolSize = 500;
    public int maxActive = 500;

    [Header("Burst Controls")]
    public KeyCode burstKey = KeyCode.B;
    public int burstCount = 120;
    public bool holdKeyToPour = true;   // Hold B to stream
    public float grainsPerSecond = 600; // When holding key

    [Header("Initial Velocity")]
    public Transform mouth;             // If null, uses this transform
    public float launchSpeed = 3.5f;
    public float spread = 1.2f;         // XY random jitter

    [Header("Motion")]
    public float gravity = 9.81f;
    public float lateralDrift = 0.5f;
    public float maxSpeed = 8f;

    [Header("Lifetime")]
    public float lifeMin = 1.2f;
    public float lifeMax = 2.0f;

    [Header("Rendering")]
    public string sortingLayerName = "Default";
    public int sortingOrder = 100;

    [Header("Debug / Safety")]
    public bool autoTestOnStart = true; // Spawns a small burst after Play
    public int autoTestCount = 30;
    public float autoTestDelay = 0.15f;
    public bool verboseLogs = true;

    class Grain
    {
        public GameObject go;
        public SpriteRenderer sr;
        public Vector2 vel;
        public float life;
        public bool active;
    }

    readonly Queue<Grain> _pool = new();
    readonly List<Grain> _active = new(512);
    float _accum;

    void Start()
    {
#if UNITY_EDITOR
        if (sprites == null || sprites.Length == 0)
            Debug.LogWarning("[SimpleToppingSpawner] No sprites assigned — grains will spawn invisible.");
#endif

        if (poolSize <= 0) poolSize = 1;
        if (maxActive <= 0) maxActive = poolSize;

        for (int i = 0; i < poolSize; i++)
            _pool.Enqueue(CreateGrain(i));

        if (autoTestOnStart)
            Invoke(nameof(_AutoTest), autoTestDelay);

#if UNITY_EDITOR
        if (verboseLogs)
            Debug.Log($"[SimpleToppingSpawner] Ready. poolSize={poolSize}, maxActive={maxActive}, sprites={sprites?.Length ?? 0}");
#endif
    }

    void _AutoTest()
    {
        int spawned = Burst(autoTestCount);
#if UNITY_EDITOR
        if (verboseLogs)
            Debug.Log($"[SimpleToppingSpawner] AutoTest burst spawned: {spawned}");
#endif
    }

    Grain CreateGrain(int idx)
    {
        var go = new GameObject($"Grain_{idx}");
        go.transform.SetParent(transform, worldPositionStays: true);
        go.transform.position = GetMouthPos();

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sortingLayerName = sortingLayerName;
        sr.sortingOrder = sortingOrder;
        sr.enabled = false;

        return new Grain { go = go, sr = sr, active = false, life = 0f, vel = Vector2.zero };
    }

    void Update()
    {
        // Manual test: tap B for a burst
        if (Input.GetKeyDown(burstKey))
        {
            int spawned = Burst(burstCount);
#if UNITY_EDITOR
            if (verboseLogs) Debug.Log($"[SimpleToppingSpawner] Burst key pressed. Spawned={spawned}, active={_active.Count}");
#endif
        }

        // Continuous pour while holding B (optional)
        if (holdKeyToPour && Input.GetKey(burstKey))
        {
            _accum += grainsPerSecond * Time.deltaTime;
            while (_accum >= 1f)
            {
                _accum -= 1f;
                SpawnOne();
            }
        }

        float dt = Time.deltaTime;

        for (int i = _active.Count - 1; i >= 0; i--)
        {
            var g = _active[i];

            // motion
            float drift = (Mathf.PerlinNoise(Time.time * 2f, g.go.transform.position.y * 0.25f) - 0.5f) * 2f * lateralDrift;
            g.vel.x += drift * dt;
            g.vel.y -= gravity * dt;
            g.vel = Vector2.ClampMagnitude(g.vel, maxSpeed);
            g.go.transform.position += (Vector3)(g.vel * dt);

            // lifetime
            g.life -= dt;
            if (g.life <= 0f)
                Despawn(i);
        }
    }

    public int Burst(int count)
    {
        int spawnable = Mathf.Min(count, maxActive - _active.Count, _pool.Count);
        for (int i = 0; i < spawnable; i++)
            SpawnOne();
        return spawnable;
    }

    void SpawnOne()
    {
        if (_pool.Count == 0 || _active.Count >= maxActive) return;

        var g = _pool.Dequeue();

        // pick sprite
        if (sprites != null && sprites.Length > 0)
            g.sr.sprite = sprites[Random.Range(0, sprites.Length)];

        // variety
        g.go.transform.localScale = Vector3.one * Random.Range(0.9f, 1.1f);
        g.go.transform.rotation = Quaternion.Euler(0, 0, Random.Range(-15f, 15f));

        // position & velocity
        Vector2 pos = GetMouthPos();
        Vector2 dir = -(Vector2)GetMouthUp(); // pour along -up
        Vector2 rand = new Vector2(Random.Range(-spread, spread), Random.Range(-spread * 0.25f, spread * 0.25f));

        g.go.transform.position = pos;
        g.vel = dir.normalized * launchSpeed + rand;

        // lifetime
        g.life = Random.Range(lifeMin, lifeMax);

        // render on
        g.sr.enabled = true;
        g.active = true;

        _active.Add(g);
    }

    void Despawn(int activeIndex)
    {
        var g = _active[activeIndex];
        g.active = false;
        g.sr.enabled = false;
        g.vel = Vector2.zero;
        g.life = 0f;
        _active.RemoveAt(activeIndex);
        _pool.Enqueue(g);
    }

    Vector3 GetMouthPos() => mouth ? mouth.position : transform.position;
    Vector3 GetMouthUp()  => mouth ? mouth.up        : transform.up;

    void OnDrawGizmosSelected()
    {
        // Visualize spawn position and pour direction
        Vector3 p = mouth ? mouth.position : transform.position;
        Vector3 dir = -(mouth ? mouth.up : transform.up);
        Gizmos.DrawWireSphere(p, 0.05f);
        Gizmos.DrawLine(p, p + dir.normalized * 0.5f);
    }
}

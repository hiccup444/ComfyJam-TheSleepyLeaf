using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(IceScoopAudio))]
public sealed class IceScooperController : MonoBehaviour
{
    [Header("Findables / Tags")]
    [SerializeField] private string iceBucketTag = "IceBucket";
    [SerializeField] private string[] cupTags = new[] { "Cup", "CupSurface", "CupLiquid" };

    [Header("Bucket Proximity")]
    [SerializeField] private float scoopTriggerRadius = 0.6f;

    [Header("High-Reach Cup Detection")]
    [SerializeField] private Vector2 reachOriginOffset = new Vector2(0.15f, -0.05f);
    [SerializeField] private float dropReachY = 4.0f;
    [SerializeField] private float horizontalReach = 0.8f;
    [SerializeField] private LayerMask cupLayerMask = 0;
    [SerializeField] private float lockLoseDistance = 1.0f;

    [Header("Auto Tilt")]
    [SerializeField] private float scoopTiltZ = -35f;
    [SerializeField] private float dropTiltZ = 50f;
    [SerializeField] private float tiltSpeed = 240f;

    [Header("Scoop / Drop Timing")]
    [SerializeField] private float scoopDuration = 0.5f;
    [SerializeField] private float dropDuration = 0.6f;

    [Header("Debounce")]
    [SerializeField] private float cupHoverRequired = 0.35f;
    [SerializeField] private float dropCooldown = 0.6f;

    [Header("Ice Capacity")]
    [SerializeField] private int icePerScoop = 1;
    [SerializeField] private int scooperCapacity = 1;

    [Header("Physical Ice Cubes")]
    [SerializeField] private GameObject iceCubePrefab;
    [SerializeField] private GameObject iceCubesVisual;
    [Tooltip("Used only if no anchor is assigned.")]
    [SerializeField] private Vector2 cubeSpawnOffset = new Vector2(0.15f, -0.2f);
    [Tooltip("If set, spawn here EXACTLY (child of scoop).")]
    [SerializeField] private Transform cubeSpawnAnchor;
    [Tooltip("Velocity along anchor.right.")]
    [SerializeField] private Vector2 cubeInitialVelX = new Vector2(-0.25f, 0.25f);
    [Tooltip("Velocity along anchor.up (negative to go 'down' relative to mouth).")]
    [SerializeField] private Vector2 cubeInitialVelY = new Vector2(-0.4f, -0.8f);

    // runtime
    private int _carriedIce;
    private bool _isScooping;
    private bool _isDropping;
    private bool _dropQueued;
    private float _cupHoverT;
    private float _dropCDTimer;
    private bool _hasScoopeOnce; // track if we've scooped at least once

    // sticky cup lock
    private MugIceState _lockedMug;
    private Vector3 _lockedMugPosAtStart;

    // drop one-shot token
    private int _dropSerial;

    // edge trigger for hover-ready
    private bool _hoverWasReady;

    // audio
    private IceScoopAudio _audio;

    private void Awake()
    {
        _audio = GetComponent<IceScoopAudio>();
        UpdateIceVisual();
    }

    private void Update()
    {
        if (_dropCDTimer > 0f) _dropCDTimer -= Time.deltaTime;

        if (_isScooping) { RotateTowardZ(scoopTiltZ); return; }
        if (_isDropping) { RotateTowardZ(dropTiltZ); return; }

        // SCOOPING near bucket
        if (IsNearIceBucket())
        {
            if (_carriedIce < scooperCapacity && !_isScooping)
            {
                _isScooping = true; // pre-lock
                StartCoroutine(ScoopRoutine());
            }
            else
            {
                RotateTowardZ(0f);
            }

            ResetHover();
            _dropQueued = false;
            _hoverWasReady = false;
            return;
        }

        // DROPPING path
        if (_carriedIce > 0 && _dropCDTimer <= 0f)
        {
            if (TryAcquireCupInReach(out var mug))
            {
                if (_lockedMug != mug)
                {
                    _lockedMug = mug;
                    _cupHoverT = 0f;
                }

                _cupHoverT += Time.deltaTime;
                RotateTowardZ(dropTiltZ);

                // Edge-trigger hover-ready to avoid repeated queuing
                bool hoverReady = _cupHoverT >= cupHoverRequired;
                if (!_hoverWasReady && hoverReady && !_dropQueued)
                {
                    _dropQueued = true;
                    StartDropOnce();
                }
                _hoverWasReady = hoverReady;
            }
            else
            {
                ResetHover();
                RotateTowardZ(0f);
                _dropQueued = false;
                _hoverWasReady = false;
            }
        }
        else
        {
            ResetHover();
            RotateTowardZ(0f);
            _dropQueued = false;
            _hoverWasReady = false;
        }
    }

    private void StartDropOnce()
    {
        if (_isDropping || _lockedMug == null) return;

        _lockedMugPosAtStart = _lockedMug.transform.position;
        _dropSerial++;
        _isDropping = true; // pre-lock so Update can't enqueue a second one
        StartCoroutine(DropRoutine(_dropSerial));
    }

    private void ResetHover()
    {
        _cupHoverT = 0f;
        _lockedMug = null;
    }

    // ----------------- Scoop -----------------
    private IEnumerator ScoopRoutine()
    {
        float t = 0f;
        while (t < scoopDuration)
        {
            if (!IsNearIceBucket() || _carriedIce >= scooperCapacity) break;
            RotateTowardZ(scoopTiltZ);
            t += Time.deltaTime;
            yield return null;
        }

        if (IsNearIceBucket() && _carriedIce < scooperCapacity)
        {
            int space = scooperCapacity - _carriedIce;
            int scoop = Mathf.Clamp(icePerScoop, 1, space);
            _carriedIce += scoop;
            UpdateIceVisual();
            
            // Only play audio if we've scooped at least once before
            if (_hasScoopeOnce)
            {
                _audio?.PlayScoop();
            }
            else
            {
                _hasScoopeOnce = true; // Mark that first scoop happened (silently)
            }

#if UNITY_EDITOR
            Debug.Log($"[SCOOPER] Scooped {scoop} (carried={_carriedIce}/{scooperCapacity})", this);
#endif
        }

        _isScooping = false;
    }

    // ----------------- Drop -----------------
    private IEnumerator DropRoutine(int serial)
    {
        float t = 0f;

        while (t < dropDuration)
        {
            // Abort if a newer serial started
            if (serial != _dropSerial) yield break;

            RotateTowardZ(dropTiltZ);
            t += Time.deltaTime;
            yield return null;
        }

        // Validate sticky cup at end
        bool stillValid =
            (serial == _dropSerial) &&
            _lockedMug != null && _lockedMug.isActiveAndEnabled &&
            (Vector3.Distance(_lockedMug.transform.position, _lockedMugPosAtStart) <= lockLoseDistance);

        if (!stillValid)
        {
#if UNITY_EDITOR
            Debug.Log("[SCOOPER] Lost mug during drop or serial changed. Keeping ice.", this);
#endif
            _dropCDTimer = dropCooldown;
            _isDropping = false;
            _cupHoverT = 0f;
            _dropQueued = false;
            _hoverWasReady = false;
            yield break;
        }

        // ---- SPAWN ONCE ----
        int toSpawn = Mathf.Clamp(_carriedIce, 0, scooperCapacity);
        for (int i = 0; i < toSpawn; i++)
            SpawnPhysicalCube();

#if UNITY_EDITOR
        Debug.Log($"[SCOOPER] Drop serial #{serial} spawned {toSpawn} cube(s).", this);
#endif

        _carriedIce = 0;
        UpdateIceVisual();
        _audio?.PlayDrop();

        _dropCDTimer = dropCooldown;
        _isDropping = false;
        _cupHoverT = 0f;
        _dropQueued = false;
        _hoverWasReady = false;
    }

    private void SpawnPhysicalCube()
    {
        if (!iceCubePrefab) return;

        Vector3 spawn;
        Quaternion rotation;
        if (cubeSpawnAnchor)
        {
            spawn = cubeSpawnAnchor.position;
            rotation = cubeSpawnAnchor.rotation;
        }
        else
        {
            spawn = transform.TransformPoint((Vector3)cubeSpawnOffset);
            rotation = Quaternion.identity;
        }

        var go = Instantiate(iceCubePrefab, spawn, rotation);

        var rb = go.GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            if (cubeSpawnAnchor)
            {
                Vector2 v =
                    (Vector2)cubeSpawnAnchor.right * Random.Range(cubeInitialVelX.x, cubeInitialVelX.y) +
                    (Vector2)cubeSpawnAnchor.up    * Random.Range(cubeInitialVelY.x, cubeInitialVelY.y);
                rb.linearVelocity = v;
            }
            else
            {
                rb.linearVelocity = new Vector2(
                    Random.Range(cubeInitialVelX.x, cubeInitialVelX.y),
                    Random.Range(cubeInitialVelY.x, cubeInitialVelY.y)
                );
            }
            rb.angularVelocity = Random.Range(-180f, 180f);
        }
    }

    // ----------------- Detection -----------------
    private bool IsNearIceBucket()
    {
        var pos = transform.position;
        var hits = Physics2D.OverlapCircleAll(pos, scoopTriggerRadius);
        foreach (var h in hits)
        {
            if (h && h.gameObject.CompareTag(iceBucketTag))
                return true;
        }
        return false;
    }

    private bool TryAcquireCupInReach(out MugIceState mug)
    {
        mug = null;

        Vector3 top = transform.TransformPoint((Vector3)reachOriginOffset);
        Vector3 center = top + Vector3.down * (dropReachY * 0.5f);
        Vector2 size = new Vector2(horizontalReach * 2f, dropReachY);

        Collider2D[] results = (cupLayerMask.value != 0)
            ? Physics2D.OverlapBoxAll(center, size, 0f, cupLayerMask)
            : Physics2D.OverlapBoxAll(center, size, 0f);

        float bestScore = float.PositiveInfinity;
        MugIceState best = null;

        for (int i = 0; i < results.Length; i++)
        {
            var col = results[i];
            if (!col) continue;

            bool tagOK = false;
            for (int t = 0; t < cupTags.Length; t++)
                if (col.CompareTag(cupTags[t])) { tagOK = true; break; }
            if (!tagOK) continue;

            var candidate = col.GetComponentInParent<MugIceState>() ?? col.GetComponent<MugIceState>();
            if (!candidate) continue;

            float vy = Mathf.Abs(candidate.transform.position.y - center.y);
            if (vy < bestScore)
            {
                bestScore = vy;
                best = candidate;
            }
        }

        mug = best;
        return mug != null;
    }

    private void RotateTowardZ(float targetZ)
    {
        var e = transform.eulerAngles;
        float current = Normalize180(e.z);
        float target = Normalize180(targetZ);
        float next = Mathf.MoveTowardsAngle(current, target, tiltSpeed * Time.deltaTime);
        e.z = next;
        transform.eulerAngles = e;
    }

    private static float Normalize180(float z)
    {
        if (z > 180f) z -= 360f;
        if (z < -180f) z += 360f;
        return z;
    }

    private void UpdateIceVisual()
    {
        if (iceCubesVisual != null)
        {
            bool shouldShow = _carriedIce > 0;
            iceCubesVisual.SetActive(shouldShow);
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        // bucket proximity
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, scoopTriggerRadius);

        // reach rectangle BELOW the scooper
        Vector3 top = transform.TransformPoint((Vector3)reachOriginOffset);
        Vector3 center = top + Vector3.down * (dropReachY * 0.5f);
        Vector3 size = new Vector3(horizontalReach * 2f, dropReachY, 0f);
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireCube(center, size);

        // spawn point (exact)
        Gizmos.color = Color.green;
        Vector3 spawn = cubeSpawnAnchor ? cubeSpawnAnchor.position : transform.TransformPoint((Vector3)cubeSpawnOffset);
        Gizmos.DrawSphere(spawn, 0.035f);
    }
#endif
}
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
public sealed class IceCube2D : MonoBehaviour
{
    [Header("Lifetime (while NOT caught)")]
    [Tooltip("Max lifetime while free-falling/not caught.")]
    [SerializeField] private float maxLifetime = 6f;

    [Tooltip("Destroy after this if sleeping on the ground (only while not caught).")]
    [SerializeField] private float sleepKillDelay = 1.5f;

    [Header("Caught Behavior")]
    [Tooltip("If true, cubes will 'freeze' (no physics) a short time after being caught.")]
    [SerializeField] private bool freezeInCup = true;

    [Tooltip("Seconds after being caught before freezing in place.")]
    [SerializeField] private float freezeDelay = 0.75f;

    [Tooltip("If true, uses bodyType=Kinematic + FreezeAll instead of simulated=false.")]
    [SerializeField] private bool freezeUseKinematic = false;

    [Tooltip("Disable the collider when frozen so it never bumps other things.")]
    [SerializeField] private bool disableColliderOnFreeze = true;

    [Header("Catch Filter")]
    [Tooltip("Cube must be moving downward at least this much to be eligible (prevents rim grazes).")]
    [SerializeField] private float minDownwardSpeedToCatch = -0.05f;

    [Header("Damping When Caught (pre-freeze settle)")]
    [SerializeField] private float caughtLinearDrag = 2.0f;
    [SerializeField] private float caughtAngularDrag = 3.0f;

    private Rigidbody2D _rb;
    private Collider2D _col;
    private float _spawnT;
    private float _sleepSince = -1f;
    private float _caughtT = -1f;
    private bool _frozen;
    private bool _allowAutoFreeze = true;

    public bool IsCaught { get; private set; }

    private void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
        _col = GetComponent<Collider2D>();

        // Solid collider for bouncing pre-catch; cup catch area should be a trigger.
        _col.isTrigger = false;
    }

    private void OnEnable()
    {
        _spawnT = Time.time;
        _sleepSince = -1f;
        _caughtT = -1f;
        _frozen = false;
        _allowAutoFreeze = true;

        // Ensure physics is active when (re)enabled.
        if (_rb != null)
        {
            if (freezeUseKinematic)
            {
                _rb.bodyType = RigidbodyType2D.Dynamic;
                _rb.constraints = RigidbodyConstraints2D.None;
            }
            _rb.simulated = true;
        }
        if (_col != null) _col.enabled = true;
        IsCaught = false;
    }

    private void Update()
    {
        if (!IsCaught)
        {
            // Free cubes: despawn if too old or slept on ground
            if (Time.time - _spawnT >= maxLifetime)
            {
                Destroy(gameObject);
                return;
            }

            if (_rb != null && _rb.IsSleeping())
            {
                if (_sleepSince < 0f) _sleepSince = Time.time;
                if (Time.time - _sleepSince >= sleepKillDelay)
                {
                    Destroy(gameObject);
                    return;
                }
            }
            else
            {
                _sleepSince = -1f;
            }
        }
        else
        {
            // Caught: optionally freeze after a short delay (no auto-destroy)
            if (freezeInCup && _allowAutoFreeze && !_frozen && _caughtT > 0f && (Time.time - _caughtT) >= freezeDelay)
            {
                FreezeInPlace();
            }
        }
    }

    public bool RequestCatch()
    {
        if (IsCaught) return false;
        if (_rb != null && _rb.linearVelocity.y > minDownwardSpeedToCatch) return false;
        return true;
    }

    public void MarkCaught()
    {
        if (IsCaught) return;
        IsCaught = true;
        _caughtT = Time.time;
    }

    public void TryApplyCaughtDamping()
    {
        if (_rb == null || _frozen) return;
        _rb.linearDamping = Mathf.Max(_rb.linearDamping, caughtLinearDrag);
        _rb.angularDamping = Mathf.Max(_rb.angularDamping, caughtAngularDrag);
    }

    /// <summary>
    /// Prepare the cube to travel with the mug while being carried.
    /// - Keep it marked as caught.
    /// - Ensure physics won't interfere (kinematic, zero velocity).
    /// - Disable collider to avoid bumping other objects while dragging.
    /// </summary>
    public void UnfreezeForCarry()
    {
        IsCaught = true;
        _frozen = false;
        _allowAutoFreeze = false; // defer any auto-freeze until explicitly socketed

        if (_rb != null)
        {
            _rb.linearVelocity = Vector2.zero;
            _rb.angularVelocity = 0f;
            _rb.bodyType = RigidbodyType2D.Kinematic;
            _rb.constraints = RigidbodyConstraints2D.None;
            _rb.simulated = true;
        }

        if (_col != null)
            _col.enabled = false;
    }

    /// <summary>
    /// Immediately freezes the cube in place (useful if you want instant stick).
    /// </summary>
    public void FreezeNow()
    {
        if (!IsCaught) IsCaught = true;
        _caughtT = (_caughtT > 0f) ? _caughtT : Time.time;
        _allowAutoFreeze = true;
        FreezeInPlace();
    }

    private void FreezeInPlace()
    {
        if (_frozen) return;
        _frozen = true;

        if (_rb != null)
        {
            // Zero out motion first to avoid tiny post-freeze shifts.
            _rb.linearVelocity = Vector2.zero;
            _rb.angularVelocity = 0f;

            if (freezeUseKinematic)
            {
                _rb.bodyType = RigidbodyType2D.Kinematic;
                _rb.constraints = RigidbodyConstraints2D.FreezeAll;
            }
            else
            {
                _rb.simulated = false; // cheapest way to 'pause' physics entirely
            }
        }

        if (disableColliderOnFreeze && _col != null)
            _col.enabled = false;
    }

    void OnDestroy()
    {
        // If this physics child is part of a dual-renderer setup, clean up the visual root
        // to avoid leaving a topper orphaned in the scene when this cube is destroyed.
        var p = transform.parent;
        if (p && p.GetComponent<IceCubeDualRenderer>())
        {
            Destroy(p.gameObject);
        }
    }
}

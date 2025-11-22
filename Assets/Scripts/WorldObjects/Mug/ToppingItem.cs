using UnityEngine;

[RequireComponent(typeof(DragItem2D))]
public class ToppingItem : MonoBehaviour
{
    [Header("Topping Type")]
    [Tooltip("Identifier for this topping type")]
    [SerializeField] private string toppingType = "WhippedCream";

    [Header("Falling Behavior")]
    [Tooltip("Gravity scale when falling (for mint/rose)")]
    [SerializeField] private float fallingGravity = 2f;

    [Header("Drop Detection Settings")]
    [Tooltip("Radius to detect nearby cups when dropped")]
    [SerializeField] private float dropDetectionRadius = 0.8f;

    private DragItem2D dragItem;
    private Rigidbody2D rb;
    private bool isFalling = false;

    // Performance caches
    private bool _shouldFall;  // Cached result to avoid repeated ToLower() calls
    private Collider2D[] _overlapBuffer = new Collider2D[10];  // Reusable array for physics queries
    private const float CupSearchRadius = 0.18f;             // Radius fallback when point overlap misses
    private IToppingAppliedListener[] _cachedListeners;  // Cached listeners

    void Awake()
    {
        dragItem = GetComponent<DragItem2D>();
        rb = GetComponent<Rigidbody2D>();

        // Cache whether this topping should fall (avoid repeated ToLower() calls)
        _shouldFall = ShouldFallFromAbove();

        // If this topping should fall (mint/rose), ensure it has a Rigidbody2D
        if (_shouldFall && rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic; // Start kinematic, enable on drop
            rb.gravityScale = 0f;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        }

        // Cache listeners to avoid GetComponents allocation later
        _cachedListeners = GetComponents<IToppingAppliedListener>();
    }

    bool ShouldFallFromAbove()
    {
        string type = toppingType.ToLower();
        return type == "mint" || type == "rose";
    }
    
    // Called by DragItem2D when dropped
    public void OnDropped()
    {
        // For falling toppings (mint/rose), check if dropped near a cup first
        if (_shouldFall)
        {
            // Check if dropped within detection radius of a cup
            var cupState = FindCupInRadius(dropDetectionRadius);
            if (cupState != null)
            {
                // Apply directly without falling
                ApplyToppingToCup(cupState);
                Destroy(gameObject);
            }
            else
            {
                // Not near a cup, start falling normally
                StartFalling();
            }
        }
        else
        {
            // Original behavior for non-falling toppings (lemon, whipped cream)
            var cupState = FindCupUnderPointer();
            if (cupState != null)
            {
                ApplyToppingToCup(cupState);
            }

            // Destroy after drop
            Destroy(gameObject);
        }
    }

    void StartFalling()
    {
        isFalling = true;

        // Disable dragging
        if (dragItem != null)
        {
            dragItem.enabled = false;
        }

        // Enable physics
        if (rb != null)
        {
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.gravityScale = fallingGravity;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        }

        // Don't destroy immediately - wait for collision with cup
    }

    CupState FindCupInRadius(float radius)
    {
        // Search for cups within the specified radius
        int count = Physics2D.OverlapCircle(transform.position, radius, ContactFilter2D.noFilter, _overlapBuffer);
        return GetCupFromBuffer(count);
    }

    CupState FindCupUnderPointer()
    {
        int count = Physics2D.OverlapPoint(transform.position, ContactFilter2D.noFilter, _overlapBuffer);
        var cup = GetCupFromBuffer(count);
        if (cup != null)
            return cup;

        count = Physics2D.OverlapCircle(transform.position, CupSearchRadius, ContactFilter2D.noFilter, _overlapBuffer);
        return GetCupFromBuffer(count);
    }

    CupState GetCupFromBuffer(int count)
    {
        for (int i = 0; i < count; i++)
        {
            var hit = _overlapBuffer[i];
            if (hit == null) continue;

            var cup = hit.GetComponentInParent<CupState>() ?? hit.GetComponent<CupState>();
            if (cup != null)
                return cup;
        }

        return null;
    }

    void ApplyToppingToCup(CupState cupState)
    {
        if (cupState == null)
            return;

        if (!string.IsNullOrWhiteSpace(toppingType))
        {
            bool success = cupState.AddTopping(toppingType);
            if (success)
            {
                NotifyToppingApplied(cupState);
            }
        }
    }

    void NotifyToppingApplied(CupState cupState)
    {
        // Use cached listeners instead of GetComponents
        if (_cachedListeners == null || _cachedListeners.Length == 0) return;

        for (int i = 0; i < _cachedListeners.Length; i++)
        {
            if (_cachedListeners[i] == null) continue;
            try
            {
                _cachedListeners[i].OnToppingApplied(cupState);
            }
            catch { }
        }
    }

    // Detect collision with cup while falling
    void OnTriggerEnter2D(Collider2D other)
    {
        // Only process collisions if we're currently falling
        if (!isFalling) return;

        // Mint and rose must hit the LiquidInCup collider specifically (falls all the way into liquid)
        if (other.gameObject.name == "LiquidInCup")
        {
            var cupState = other.GetComponentInParent<CupState>() ?? other.GetComponent<CupState>();
            if (cupState != null)
            {
                ApplyToppingToCup(cupState);
                Destroy(gameObject);
            }
        }
    }
}

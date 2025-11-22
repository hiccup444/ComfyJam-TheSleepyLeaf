using UnityEngine;

public class MilkTiltVisual : MonoBehaviour
{
    [Header("Child Names")]
    [SerializeField] private string milkFrontName = "milkFront";
    [SerializeField] private string milkSideName  = "milkSide";
    [SerializeField] private string liquidName    = "liquid";
    
    [Header("Tilt Settings")]
    [Tooltip("Minimum tilt angle (deg) to switch to side view")]
    [SerializeField] private float tiltThreshold = 20f;
    [Tooltip("Max tilt angle (deg). Liquid only pours at this angle.")]
    [SerializeField] private float maxTiltAngle  = 45f;

    [Header("Auto Tilt Near Cup")]
    [Tooltip("Auto-tilt toward pour while a cup/serve zone is nearby (only while dragging).")]
    [SerializeField] private bool autoTiltNearCup = true;
    [Tooltip("Search radius (world units) for a nearby cup/serve zone.")]
    [SerializeField] private float autoTiltRadius = 0.6f;
    [Tooltip("Layer(s) used by mugs/cups or serve sockets.")]
    [SerializeField] private LayerMask cupMask;
    [Tooltip("Degrees per second to rotate toward the target angle.")]
    [SerializeField] private float autoTiltSpeed = 720f; // more aggressive
    [Tooltip("Exponent for closeness→tilt mapping. 1=linear, 2=stronger near cup.")]
    [Range(0.5f, 4f)]
    [SerializeField] private float tiltAggression = 2f;
    [Tooltip("If within this normalized distance (0 near, 1 at edge), snap to max tilt.")]
    [Range(0f, 0.5f)]
    [SerializeField] private float snapDistance01 = 0.18f;
    [Tooltip("If true, get extra aggression when a valid pour surface is detected.")]
    [SerializeField] private bool requireSurfaceForAggressiveTilt = false;
    [Tooltip("Optional curve to shape distance→weight (overrides aggression if set). x=dist01, y=weight.")]
    [SerializeField] private AnimationCurve tiltByDistance = null;

    [Header("Position Correction")]
    [Tooltip("Position offset to apply to liquid when rotated (to correct for rotation displacement)")]
    [SerializeField] private Vector2 liquidPositionOffset = new Vector2(0.58636f, -1.1845f);
    
    [Header("Pour Duration Requirement")]
    [Tooltip("Minimum time (seconds) player must pour before milk is registered")]
    [SerializeField] private float minPourDuration = 2f;
    
    [Tooltip("Tag that identifies actual cups (for pour timer - only counts when hitting cups)")]
    [SerializeField] private string cupTag = "Cup";
    
    private SpriteRenderer milkFrontRenderer;
    private SpriteRenderer milkSideRenderer;
    private SpriteRenderer liquidRenderer;
    private Transform      liquidTransform;
    private DragItem2D     dragItem;
    private Transform      trackedTransform;
    private LiquidStream   liquidStream;
    
    private Sprite frontSprite;
    private Sprite sideSprite;
    private Vector3 liquidOriginalLocalPosition;
    
    // Pour duration tracking
    private bool _isPouring = false;
    private float _pouringTime = 0f;
    private bool _milkRegistered = false; // True when 2-second timer completes (allows milk to be added)
    private MugBeverageState _targetCupWithMilk = null; // Tracks which cup received the milk

    /// <summary>
    /// Returns true if milk has been poured for the minimum required duration.
    /// </summary>
    public bool IsMilkRegistered => _milkRegistered;

    /// <summary>
    /// Returns current pouring time in seconds.
    /// </summary>
    public float PouringTime => _pouringTime;

    /// <summary>
    /// Returns true if milk has already been applied to the current target cup.
    /// </summary>
    public bool IsMilkAppliedToCup
    {
        get
        {
            // If target cup was destroyed or is null, milk can be poured again
            if (_targetCupWithMilk == null) return false;

            // Check if the cup still has milk (false if cup was emptied/cleared)
            return _targetCupWithMilk.HasMilk;
        }
    }

    /// <summary>
    /// Called by LiquidStream when milk has been successfully added to a cup.
    /// </summary>
    public void NotifyMilkApplied(MugBeverageState targetCup)
    {
        _targetCupWithMilk = targetCup;
#if UNITY_EDITOR
        Debug.Log($"[MilkTiltVisual] Milk applied to cup '{targetCup.name}' - pouring will now be blocked for this cup");
#endif
    }

    /// <summary>
    /// Reset pour tracking (e.g., when milk container is refilled or reset).
    /// </summary>
    public void ResetPourTracking()
    {
        _isPouring = false;
        _pouringTime = 0f;
        _milkRegistered = false;
        _targetCupWithMilk = null;
#if UNITY_EDITOR
        Debug.Log("[MilkTiltVisual] Pour tracking reset");
#endif
    }

    void Reset()
    {
        if (tiltByDistance == null)
            tiltByDistance = AnimationCurve.Linear(0f, 0f, 1f, 1f);
    }

    void Awake()
    {
        if (tiltByDistance == null)
            tiltByDistance = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        liquidStream = GetComponent<LiquidStream>();
        
        Transform milkFrontTransform = transform.Find(milkFrontName);
        Transform milkSideTransform  = transform.Find(milkSideName);
        
        if (milkFrontTransform != null)
        {
            dragItem          = milkFrontTransform.GetComponent<DragItem2D>();
            trackedTransform  = milkFrontTransform;
            milkFrontRenderer = milkFrontTransform.GetComponent<SpriteRenderer>();
            if (milkFrontRenderer != null) frontSprite = milkFrontRenderer.sprite;

            Transform liquidTransformChild = milkFrontTransform.Find(liquidName);
            if (liquidTransformChild != null)
            {
                liquidTransform = liquidTransformChild;
                liquidRenderer  = liquidTransformChild.GetComponent<SpriteRenderer>();
                liquidOriginalLocalPosition = liquidTransformChild.localPosition;
                if (liquidRenderer != null) liquidRenderer.enabled = false;
                else
                {
#if UNITY_EDITOR
                    Debug.LogWarning($"MilkTiltVisual: Found '{liquidName}' but no SpriteRenderer!");
#endif
                }
            }
            else
            {
#if UNITY_EDITOR
                Debug.LogWarning($"MilkTiltVisual: Could not find child '{liquidName}' under milkFront!");
#endif
            }
        }
        else
        {
#if UNITY_EDITOR
            Debug.LogError($"MilkTiltVisual: Could not find child '{milkFrontName}'!");
#endif
        }

        if (milkSideTransform != null)
        {
            milkSideRenderer = milkSideTransform.GetComponent<SpriteRenderer>();
            if (milkSideRenderer != null)
            {
                sideSprite = milkSideRenderer.sprite;
                milkSideRenderer.enabled = false;
            }
        }
        else
        {
#if UNITY_EDITOR
            Debug.LogError($"MilkTiltVisual: Could not find child '{milkSideName}'!");
#endif
        }
    }

    void LateUpdate()
    {
        if (trackedTransform == null || milkFrontRenderer == null || frontSprite == null || sideSprite == null)
            return;

        // --- Auto tilt: stronger + snap when very close ---
        if (autoTiltNearCup && dragItem != null && dragItem.IsDragging)
        {
            // If milk already applied to a cup, tilt back to normal
            if (IsMilkAppliedToCup)
            {
                float current = trackedTransform.localEulerAngles.z;
                if (current > 180f) current -= 360f;
                float next = Mathf.MoveTowardsAngle(current, 0f, autoTiltSpeed * Time.deltaTime);
                trackedTransform.localEulerAngles = new Vector3(0f, 0f, next);
            }
            else if (TryFindCup(out var _cupPos, out var dist01))
            {
                // Map distance→weight (0 near, 1 far)
                float weight;
                if (tiltByDistance != null)
                {
                    weight = Mathf.Clamp01(1f - tiltByDistance.Evaluate(dist01)); // curve defines "closeness"
                }
                else
                {
                    float closeness = 1f - dist01; // 0..1 (1 when touching)
                    weight = Mathf.Pow(Mathf.Clamp01(closeness), tiltAggression);
                }

                // Optional boost if we're actually over a valid pour surface
                if (requireSurfaceForAggressiveTilt && (liquidStream == null || !liquidStream.HasValidPourSurface()))
                {
                    weight *= 0.6f;
                }

                // Snap to max when very close
                bool snap = dist01 <= snapDistance01;

                float desired = snap
                    ? maxTiltAngle
                    : Mathf.Lerp(tiltThreshold, maxTiltAngle, weight);

                // Only tilt LEFT (positive Z)
                float current = trackedTransform.localEulerAngles.z;
                if (current > 180f) current -= 360f;
                desired = Mathf.Max(0f, desired);

                float next = Mathf.MoveTowardsAngle(current, desired, autoTiltSpeed * Time.deltaTime);
                trackedTransform.localEulerAngles = new Vector3(0f, 0f, next);
            }
            else
            {
                // No cup nearby → relax upright quickly
                float current = trackedTransform.localEulerAngles.z;
                if (current > 180f) current -= 360f;
                float next = Mathf.MoveTowardsAngle(current, 0f, autoTiltSpeed * Time.deltaTime);
                trackedTransform.localEulerAngles = new Vector3(0f, 0f, next);
            }
        }
        // --- end Auto tilt ---

        // Use the (possibly auto-adjusted) Z rotation for visual logic
        float zRotation = trackedTransform.eulerAngles.z;
        if (zRotation > 180f) zRotation -= 360f;
        
        bool shouldAutoResetSprite = dragItem == null || dragItem.resetRotationOnDrop;
        
        if (zRotation >= tiltThreshold)
        {
            if (milkFrontRenderer.sprite != sideSprite)
                milkFrontRenderer.sprite = sideSprite;
            
            // Be a bit lenient around max angle so it pours more reliably
            bool atMaxTilt = Mathf.Abs(zRotation - maxTiltAngle) < 2.5f; // widened band

            if (atMaxTilt)
            {
                // Stop pouring if milk has already been applied to a cup
                if (IsMilkAppliedToCup)
                {
                    if (liquidRenderer != null && liquidRenderer.enabled)
                        liquidRenderer.enabled = false;
                    if (liquidStream != null)
                        liquidStream.StopPouring();
                }
                else
                {
                    bool hasSurface = liquidStream != null && liquidStream.HasValidPourSurface();
                    bool pouringOntoCup = IsPouringOntoCup();

                    if (hasSurface)
                {
                    // Only count timer if pouring onto an actual cup (not counter/table)
                    if (pouringOntoCup)
                    {
                        // Track pouring time ONLY when hitting a cup
                        if (!_isPouring)
                        {
                            _isPouring = true;
#if UNITY_EDITOR
                            Debug.Log("[MilkTiltVisual] Started pouring onto cup");
#endif
                        }

                        _pouringTime += Time.deltaTime;

                        // Register milk after minimum pour duration
                        if (!_milkRegistered && _pouringTime >= minPourDuration)
                        {
                            _milkRegistered = true;
#if UNITY_EDITOR
                            Debug.Log($"[MilkTiltVisual] Milk registered after {_pouringTime:F2} seconds of pouring onto cup");
#endif
                        }
                    }
                    else
                    {
                        // Pouring onto surface but NOT a cup (counter, table, etc.)
                        if (_isPouring)
                        {
                            _isPouring = false;
#if UNITY_EDITOR
                            Debug.Log($"[MilkTiltVisual] Pouring onto non-cup surface - resetting timer (was at {_pouringTime:F2}s)");
#endif
                        }

                        // Reset timer and registration when pouring on non-cup surfaces
                        _pouringTime = 0f;
                        _milkRegistered = false;
                    }
                    
                    if (liquidRenderer != null)
                    {
                        if (!liquidRenderer.enabled) liquidRenderer.enabled = true;
                        liquidRenderer.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
                    }

                    if (liquidStream != null)
                        liquidStream.StartPouring();

                    if (liquidTransform != null)
                    {
                        float milkRotation = trackedTransform.localEulerAngles.z;
                        Vector3 liquidEuler = liquidTransform.localEulerAngles;
                        liquidEuler.z = -milkRotation;
                        liquidTransform.localEulerAngles = liquidEuler;

                        Vector3 correctedPos = liquidOriginalLocalPosition;
                        correctedPos.x += liquidPositionOffset.x;
                        correctedPos.y += liquidPositionOffset.y;
                        liquidTransform.localPosition = correctedPos;
                    }
                }
                else
                {
                    // At max tilt but NO valid surface at all - reset timer and registration
                    if (_isPouring)
                    {
                        _isPouring = false;
#if UNITY_EDITOR
                        Debug.Log($"[MilkTiltVisual] Lost valid surface - resetting pour timer and registration (was at {_pouringTime:F2}s)");
#endif
                    }

                    // Reset BOTH timer and registration when not hitting a valid surface
                    _pouringTime = 0f;
                    _milkRegistered = false;

                    if (liquidRenderer != null && liquidRenderer.enabled)
                        liquidRenderer.enabled = false;
                    if (liquidStream != null)
                        liquidStream.StopPouring();
                }
                }
            }
            else
            {
                // Not at max tilt - stop pouring and reset
                if (_isPouring)
                {
                    _isPouring = false;
#if UNITY_EDITOR
                    Debug.Log($"[MilkTiltVisual] Stopped pouring (not at max tilt) - resetting (was at {_pouringTime:F2}s)");
#endif
                }

                // Reset timer and registration when not pouring
                _pouringTime = 0f;
                _milkRegistered = false;

                if (liquidRenderer != null && liquidRenderer.enabled)
                    liquidRenderer.enabled = false;
                if (liquidStream != null)
                    liquidStream.StopPouring();
            }
        }
        else if (shouldAutoResetSprite || zRotation < tiltThreshold)
        {
            // Reset pouring state when returning to front view
            if (_isPouring)
            {
                _isPouring = false;
#if UNITY_EDITOR
                Debug.Log($"[MilkTiltVisual] Stopped pouring (returned to front view) - resetting (was at {_pouringTime:F2}s)");
#endif
            }

            // Reset timer and registration
            _pouringTime = 0f;
            _milkRegistered = false;
            
            if (milkFrontRenderer.sprite != frontSprite)
                milkFrontRenderer.sprite = frontSprite;
            
            if (liquidRenderer != null && liquidRenderer.enabled)
                liquidRenderer.enabled = false;
            
            if (liquidStream != null)
                liquidStream.StopPouring();
        }
    }

    // Finds a nearby cup within autoTiltRadius on cupMask.
    // Returns normalized distance dist01: 0 when touching, 1 at radius edge.
    bool TryFindCup(out Vector3 cupPos, out float dist01)
    {
        cupPos = default;
        dist01 = 1f;

        if (!autoTiltNearCup) return false;

        Vector3 p = trackedTransform != null ? trackedTransform.position : transform.position;
        Collider2D hit = Physics2D.OverlapCircle(p, autoTiltRadius, cupMask);
        if (!hit) return false;

        cupPos = hit.bounds.center;
        float d = Vector2.Distance(p, cupPos);
        dist01 = Mathf.Clamp01(d / autoTiltRadius);
        return true;
    }
    
    /// <summary>
    /// Check if we're pouring onto an actual cup (not just any surface like counter/table).
    /// Uses raycast to detect cup collider below the milk spout.
    /// This now properly validates minimum pour height from LiquidStream.
    /// </summary>
    bool IsPouringOntoCup()
    {
        if (raycastOrigin == null || liquidStream == null)
            return false;

        // HasValidPourSurface already checks minPourHeight requirement
        // So if this returns false, we're either too far OR there's no surface
        if (!liquidStream.HasValidPourSurface())
            return false;

        // Now check specifically if that surface is a cup
        float rayDistance = 10f; // Max pour distance
        RaycastHit2D hit = Physics2D.Raycast(raycastOrigin.position, Vector2.down, rayDistance);

        // Cup must be tagged correctly
        if (hit.collider != null && hit.collider.CompareTag(cupTag))
        {
            return true;
        }

        return false;
    }
    
    Transform raycastOrigin
    {
        get
        {
            if (liquidStream != null)
            {
                // Try to get raycast origin from LiquidStream via reflection
                var type = liquidStream.GetType();
                var field = type.GetField("raycastOrigin", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field != null)
                {
                    return field.GetValue(liquidStream) as Transform;
                }
            }
            return trackedTransform; // Fallback
        }
    }
    
    void OnDrawGizmosSelected()
    {
        if (!autoTiltNearCup) return;
        Gizmos.color = new Color(1f, 1f, 1f, 0.35f);
        Vector3 p = (transform.childCount > 0) ? transform.GetChild(0).position : transform.position;
        Gizmos.DrawWireSphere(p, autoTiltRadius);
    }
}

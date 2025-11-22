using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Handles spoon stirring mechanics for hot chocolate (or other powder drinks).
/// Player drags the spoon left/right while Y position is locked.
/// Spoon rotates and flips based on position, and tracks full stir cycles.
/// Uses Unity's new Input System (IPointerDownHandler, etc.) to avoid conflicts with DragItem2D.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
[RequireComponent(typeof(Collider2D))]
public sealed class SpoonStirrer : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler
{
    [Header("References")]
    [SerializeField] private MugBeverageState beverageState;
    [SerializeField] private SpriteRenderer spoonRenderer;
    
    [Tooltip("Mug's collider to disable while spoon is active (prevents grabbing mug)")]
    [SerializeField] private Collider2D mugCollider;

    [Header("Position Constraints")]
    [Tooltip("Minimum local X position (left side of mug)")]
    [SerializeField] private float minX = -1.2f;
    
    [Tooltip("Maximum local X position (right side of mug)")]
    [SerializeField] private float maxX = 0.4f;
    
    [Tooltip("Top Y position when stirring on top half")]
    [SerializeField] private float topY = 0.9f;
    
    [Tooltip("Bottom Y position when stirring on bottom half")]
    [SerializeField] private float bottomY = 0.8f;
    
    [Tooltip("Center X position where sprite flips")]
    [SerializeField] private float centerX = -0.4f;
    
    [Tooltip("Smooth time for Y position transitions (lower = faster, 0.05-0.15 recommended)")]
    [SerializeField] private float ySmoothTime = 0.1f;
    
    [Tooltip("X threshold from edges to start path transition (prevents abrupt switches at extremes)")]
    [SerializeField] private float edgeTransitionZone = 0.2f;

    [Header("Rotation")]
    [Tooltip("Max rotation angle at center (top path)")]
    [SerializeField] private float maxRotationTop = 15f;
    
    [Tooltip("Max rotation angle at center (bottom path)")]
    [SerializeField] private float maxRotationBottom = 15f;

    [Header("Sorting Order")]
    [Tooltip("Sorting order when on top path")]
    [SerializeField] private int sortingOrderTop = 10;
    
    [Tooltip("Sorting order when on bottom path")]
    [SerializeField] private int sortingOrderBottom = 11;

    [Header("Stir Detection")]
    [Tooltip("Number of full stir cycles required to complete stirring")]
    [SerializeField] private int requiredStirCycles = 2;
    
    [Tooltip("Minimum X distance traveled to count as a valid movement")]
    [SerializeField] private float minStirDistance = 0.3f;
    
    [Tooltip("Gradually change color as stirring progresses (smooth visual feedback)")]
    [SerializeField] private bool progressiveColorChange = true;

    [Header("State (Read-Only)")]
    [SerializeField] private int stirCyclesCompleted = 0;
    [SerializeField] private float partialStirProgress = 0f; // 0-1 progress within current cycle

    // Internal state
    private bool _isDragging;
    private Vector3 _dragOffset;
    private Camera _mainCamera;
    private Vector2 _lastPointerPosition;
    private bool _isOnLeftSide;     // true when X < centerX
    private bool _isOnBottomPath;   // true when on bottom half of stir
    private float _lastSignificantX; // track position for stir detection
    private bool _hasReachedLeft;   // tracking for one full cycle
    private bool _hasReachedRight;  // tracking for one full cycle
    private int _savedSortingOrder; // preserve sorting order from parent modifications
    private bool _reachedCenterThisCycle; // track if we've reached center in current half-cycle
    
    // Smoothing state
    private float _currentY;        // smoothed Y position
    private float _yVelocity;       // velocity for SmoothDamp
    private float _lastXPosition;   // track X movement for path detection
    private float _currentRotation; // smoothed rotation
    private float _rotationVelocity; // velocity for rotation smoothing

    private void Awake()
    {
        _mainCamera = Camera.main;

        if (spoonRenderer == null)
            spoonRenderer = GetComponent<SpriteRenderer>();

        if (beverageState == null)
            beverageState = GetComponentInParent<MugBeverageState>();

        // Start on right side, top path
        ResetToStartPosition();
        
        // Initialize smooth Y to starting position
        _currentY = transform.localPosition.y;
        _lastXPosition = transform.localPosition.x;
    }

    private void OnEnable()
    {
        stirCyclesCompleted = 0;
        partialStirProgress = 0f;
        _hasReachedLeft = false;
        _hasReachedRight = false;
        _reachedCenterThisCycle = false;
        _lastSignificantX = transform.localPosition.x;
        ResetToStartPosition();
        
        // Reset smoothing
        _currentY = transform.localPosition.y;
        _yVelocity = 0f;
        _lastXPosition = transform.localPosition.x;
        _currentRotation = 0f;
        _rotationVelocity = 0f;

        // Disable mug collider and DragItem2D so player can't grab the mug while stirring
        if (mugCollider != null)
        {
            mugCollider.enabled = false;
            #if UNITY_EDITOR
            Debug.Log("[SpoonStirrer] Disabled mug collider to prevent grabbing while stirring");
            #endif
        }

        // Also disable DragItem2D if present on parent
        var dragItem = GetComponentInParent<DragItem2D>();
        if (dragItem != null)
        {
            dragItem.enabled = false;
            #if UNITY_EDITOR
            Debug.Log("[SpoonStirrer] Disabled DragItem2D to prevent mug dragging while stirring");
            #endif
        }
    }

    private void OnDisable()
    {
        // Re-enable mug collider when spoon is hidden
        if (mugCollider != null)
        {
            mugCollider.enabled = true;
            #if UNITY_EDITOR
            Debug.Log("[SpoonStirrer] Re-enabled mug collider");
            #endif
        }

        // Re-enable DragItem2D
        var dragItem = GetComponentInParent<DragItem2D>();
        if (dragItem != null)
        {
            dragItem.enabled = true;
            #if UNITY_EDITOR
            Debug.Log("[SpoonStirrer] Re-enabled DragItem2D for mug dragging");
            #endif
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (_mainCamera == null) return;

        _isDragging = true;
        _lastPointerPosition = eventData.position;

        Vector3 mouseWorld = _mainCamera.ScreenToWorldPoint(_lastPointerPosition);
        mouseWorld.z = 0f;
        _dragOffset = transform.position - mouseWorld;

        #if UNITY_EDITOR
        Debug.Log("[SpoonStirrer] Pointer down - started dragging");
        #endif
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!_isDragging || _mainCamera == null) return;

        _lastPointerPosition = eventData.position;

        Vector3 mouseWorld = _mainCamera.ScreenToWorldPoint(_lastPointerPosition);
        mouseWorld.z = 0f;
        Vector3 targetWorld = mouseWorld + _dragOffset;

        // Convert to local position relative to parent
        Vector3 targetLocal = transform.parent != null
            ? transform.parent.InverseTransformPoint(targetWorld)
            : targetWorld;

        // Clamp X within bounds
        targetLocal.x = Mathf.Clamp(targetLocal.x, minX, maxX);

        // Track X movement direction
        float currentX = transform.localPosition.x;
        float deltaX = targetLocal.x - _lastXPosition;
        _lastXPosition = targetLocal.x;

        // Smarter path detection with edge transition zones
        // Only switch paths if we're not near the extremes (prevents rough transitions at edges)
        float distFromLeftEdge = Mathf.Abs(targetLocal.x - minX);
        float distFromRightEdge = Mathf.Abs(targetLocal.x - maxX);
        bool nearLeftEdge = distFromLeftEdge < edgeTransitionZone;
        bool nearRightEdge = distFromRightEdge < edgeTransitionZone;

        // Path switching logic: only change path in the middle zones, not at extremes
        if (!nearLeftEdge && !nearRightEdge)
        {
            // Left side going right = bottom path
            if (_isOnLeftSide && deltaX > 0.01f)
                _isOnBottomPath = true;
            // Right side going left = top path
            else if (!_isOnLeftSide && deltaX < -0.01f)
                _isOnBottomPath = false;
        }
        // When at edges, maintain current path to prevent jitter
        else if (nearLeftEdge && deltaX > 0.01f)
        {
            // Starting from left edge going right = switch to bottom
            _isOnBottomPath = true;
        }
        else if (nearRightEdge && deltaX < -0.01f)
        {
            // Starting from right edge going left = switch to top
            _isOnBottomPath = false;
        }

        // Determine target Y based on path
        float targetY = _isOnBottomPath ? bottomY : topY;

        // Smooth Y transition using SmoothDamp
        _currentY = Mathf.SmoothDamp(_currentY, targetY, ref _yVelocity, ySmoothTime);

        // Apply smoothed position
        targetLocal.y = _currentY;
        targetLocal.z = 0f;

        transform.localPosition = targetLocal;

        // Update visual state
        UpdateSpoonVisuals();

        // Track stir progress
        TrackStirProgress(targetLocal.x);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        _isDragging = false;
        
        // Keep spoon where player released it
        // Save the correct sorting order
        if (spoonRenderer != null)
        {
            int correctOrder = _isOnBottomPath ? sortingOrderBottom : sortingOrderTop;
            _savedSortingOrder = correctOrder;
            spoonRenderer.sortingOrder = correctOrder;
            #if UNITY_EDITOR
            Debug.Log($"[SpoonStirrer] Pointer up - setting sorting order to {correctOrder} (bottom path: {_isOnBottomPath})");
            #endif
        }
    }

    private void LateUpdate()
    {
        // Constantly enforce correct sorting order to prevent parent scripts from modifying it
        if (spoonRenderer != null && gameObject.activeInHierarchy)
        {
            int correctOrder = _isOnBottomPath ? sortingOrderBottom : sortingOrderTop;
            if (spoonRenderer.sortingOrder != correctOrder)
            {
                spoonRenderer.sortingOrder = correctOrder;
            }
        }
    }

    private void UpdateSpoonVisuals()
    {
        Vector3 localPos = transform.localPosition;
        float t = Mathf.InverseLerp(minX, maxX, localPos.x);

        // Determine if on left or right side
        bool wasOnLeftSide = _isOnLeftSide;
        _isOnLeftSide = localPos.x < centerX;

        // Flip sprite when crossing center
        if (wasOnLeftSide != _isOnLeftSide && spoonRenderer != null)
        {
            spoonRenderer.flipX = _isOnLeftSide;
            
            // Immediately invert rotation when flipping to maintain correct angle
            _currentRotation = -_currentRotation;
            _rotationVelocity = 0f; // Reset velocity for clean transition
        }

        // Calculate rotation based on distance from center
        float centerT = Mathf.InverseLerp(minX, maxX, centerX);
        float distanceFromCenter = Mathf.Abs(t - centerT);
        float normalizedDistance = distanceFromCenter / Mathf.Max(centerT, 1f - centerT);
        
        float maxRotation = _isOnBottomPath ? maxRotationBottom : maxRotationTop;
        float targetRotation = Mathf.Lerp(maxRotation, 0f, normalizedDistance);

        // Apply rotation with correct sign based on side
        if (_isOnLeftSide)
            targetRotation = -targetRotation;

        // Smooth rotation transition (fast smooth time for responsive feel)
        _currentRotation = Mathf.SmoothDampAngle(_currentRotation, targetRotation, ref _rotationVelocity, 0.05f);
        transform.localRotation = Quaternion.Euler(0f, 0f, _currentRotation);

        // Update sorting order based on path
        if (spoonRenderer != null)
        {
            spoonRenderer.sortingOrder = _isOnBottomPath ? sortingOrderBottom : sortingOrderTop;
        }
    }

    private void TrackStirProgress(float currentX)
    {
        // Check if we've reached center (for half-cycle detection)
        bool isNearCenter = Mathf.Abs(currentX - centerX) < 0.15f;
        
        // Track when we cross the center
        if (isNearCenter && !_reachedCenterThisCycle)
        {
            _reachedCenterThisCycle = true;
            
            // Half-cycle completed! Update color
            if (progressiveColorChange && beverageState != null)
            {
                // Calculate total progress including partial cycles
                // Each full cycle = 2 half-cycles
                float totalHalfCycles = (stirCyclesCompleted * 2f) + 1f; // +1 for this half-cycle
                float totalRequiredHalfCycles = requiredStirCycles * 2f;
                partialStirProgress = Mathf.Clamp01(totalHalfCycles / totalRequiredHalfCycles);

                beverageState.UpdateStirProgress(partialStirProgress);
                #if UNITY_EDITOR
                Debug.Log($"[SpoonStirrer] Half-cycle completed! Progress: {partialStirProgress:F2} ({totalHalfCycles}/{totalRequiredHalfCycles} half-cycles)");
                #endif
            }
        }
        
        // Reset center flag when we move away from center
        if (!isNearCenter && _reachedCenterThisCycle)
        {
            _reachedCenterThisCycle = false;
        }

        // Check if we've moved significantly
        if (Mathf.Abs(currentX - _lastSignificantX) > minStirDistance)
        {
            // Track left/right extremes
            if (currentX <= minX + 0.1f)
                _hasReachedLeft = true;
            else if (currentX >= maxX - 0.1f)
                _hasReachedRight = true;

            _lastSignificantX = currentX;
        }

        // Check if full cycle is complete (left -> right or right -> left)
        if (_hasReachedLeft && _hasReachedRight)
        {
            stirCyclesCompleted++;
            _hasReachedLeft = false;
            _hasReachedRight = false;

            #if UNITY_EDITOR
            Debug.Log($"[SpoonStirrer] Full stir cycle {stirCyclesCompleted}/{requiredStirCycles} completed!");
            #endif

            // Apply progressive color change for the full cycle completion
            if (progressiveColorChange && beverageState != null)
            {
                float totalHalfCycles = stirCyclesCompleted * 2f;
                float totalRequiredHalfCycles = requiredStirCycles * 2f;
                partialStirProgress = Mathf.Clamp01(totalHalfCycles / totalRequiredHalfCycles);
                
                beverageState.UpdateStirProgress(partialStirProgress);
            }

            // Check if stirring is done
            if (stirCyclesCompleted >= requiredStirCycles)
            {
                CompleteStirring();
            }
        }
    }

    private void CompleteStirring()
    {
        #if UNITY_EDITOR
        Debug.Log("[SpoonStirrer] Stirring complete!");
        #endif

        if (beverageState != null)
            beverageState.CompleteStir();

        // Disable this component (spoon will be hidden by beverageState)
        enabled = false;
    }

    private void ResetToStartPosition()
    {
        // Start on right side, top path
        transform.localPosition = new Vector3(maxX, topY, 0f);
        transform.localRotation = Quaternion.identity;
        
        if (spoonRenderer != null)
        {
            spoonRenderer.flipX = false;
            spoonRenderer.sortingOrder = sortingOrderTop;
        }

        _isOnLeftSide = false;
        _isOnBottomPath = false;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (spoonRenderer == null)
            spoonRenderer = GetComponent<SpriteRenderer>();
    }
#endif
}
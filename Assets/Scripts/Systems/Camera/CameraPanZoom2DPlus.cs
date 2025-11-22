using UnityEngine;
using UnityEngine.EventSystems;

[RequireComponent(typeof(Camera))]
public sealed class CameraPanZoom2DPlus : MonoBehaviour
{
    [Header("Mouse Wheel Pan")]
    [SerializeField] float wheelPanSpeed = 2f;
    [SerializeField] float wheelDecel = 100f;

    [Header("Pan (MMB Drag)")]
    [SerializeField] float dragSpeed = 1f;
    [SerializeField] bool  allowPanOverUI = false;

    [Header("Pan (Edge Scroll) — Smooth")]
    [SerializeField] bool  edgePanEnabled = true;
    [Tooltip("No panning until the cursor is within this many pixels of an edge.")]
    [SerializeField] int   edgeInnerPixels = 8;
    [Tooltip("Additional pixels inward from the inner zone used as a gradient band up to full speed.")]
    [SerializeField] int   edgeOuterPixels = 28;
    [Tooltip("World-units/sec when fully pressed to the edge.")]
    [SerializeField] float edgeMaxSpeed   = 14f;
    [Tooltip("How fast velocity ramps up toward the target.")]
    [SerializeField] float edgeAccel      = 80f;
    [Tooltip("How fast velocity ramps down when leaving the edge.")]
    [SerializeField] float edgeDecel      = 100f;
    [Tooltip("Ignore tiny strengths to avoid micro jiggle near corners.")]
    [SerializeField] float minStrengthToPan = 0.06f;

    [Header("Edge Pan Boost (while dragging items)")]
    [SerializeField] float dragEdgeBoost = 2.2f;  // multiplier applied when boosted
    [SerializeField] bool  allowEdgePanWhileDragging = true;
    bool draggingBoost = false;                   // set by DragItem2D via SetDraggingBoost

    [Header("World Bounds (clamps camera center)")]
    [SerializeField] float minX = -20f;
    [SerializeField] float maxX =  20f;
    [SerializeField] float minY = -10f;
    [SerializeField] float maxY =  10f;

    [Header("Custom Cursor")]
    [Tooltip("Texture for the OS cursor (set Texture Type: Cursor).")]
    [SerializeField] Texture2D cursorTexture;
    [Tooltip("Hotspot in pixels from top-left of texture.")]
    [SerializeField] Vector2   cursorHotspot = new(0, 0);
    [SerializeField] CursorMode cursorMode    = CursorMode.Auto;

    Camera cam;
    Vector3 dragStartWorld;
    bool draggingMMB;

    // Smoothed edge-pan velocity (world units/sec)
    Vector2 _edgeVel;
    
    // Smoothed wheel-pan velocity (world units/sec)
    float _wheelVel;

    void OnEnable()
    {
        cam = GetComponent<Camera>();
        cam.orthographic = true;

        if (cursorTexture != null)
            Cursor.SetCursor(cursorTexture, cursorHotspot, cursorMode);
    }

    void OnDisable()
    {
        Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
    }

    void Update()
    {
        // Don't allow camera control when day is complete (summary showing)
        if (GameManager.Instance != null && GameManager.Instance.IsDayComplete())
            return;
            
        HandleWheelPan();
        HandleMMBPan();
        HandleEdgePan();
        ClampToBounds();
    }

    // Called by DragItem2D (or anything) to boost edge pan while dragging
    public void SetDraggingBoost(bool on) => draggingBoost = on;

    void HandleWheelPan()
    {
        float scroll = Input.mouseScrollDelta.y;
        
        // Accumulate scroll into velocity (like a push)
        if (!Mathf.Approximately(scroll, 0f))
        {
            _wheelVel += scroll * wheelPanSpeed;
        }
        
        float dt = Time.deltaTime;
        
        // Always decelerate toward zero
        _wheelVel = Mathf.MoveTowards(_wheelVel, 0f, wheelDecel * dt);
        
        if (Mathf.Abs(_wheelVel) > 0.001f)
            transform.position += Vector3.up * _wheelVel * dt;
    }

    void HandleMMBPan()
    {
        bool pointerOverUI = EventSystem.current && EventSystem.current.IsPointerOverGameObject();
        if (!allowPanOverUI && pointerOverUI) return;

        if (Input.GetMouseButtonDown(2))
        {
            draggingMMB = true;
            dragStartWorld = cam.ScreenToWorldPoint(Input.mousePosition);
            dragStartWorld.z = 0f;
        }
        else if (Input.GetMouseButtonUp(2))
        {
            draggingMMB = false;
        }

        if (draggingMMB)
        {
            Vector3 currentWorld = cam.ScreenToWorldPoint(Input.mousePosition);
            currentWorld.z = 0f;
            Vector3 delta = (dragStartWorld - currentWorld);
            transform.position += delta * dragSpeed;
        }
    }

    void HandleEdgePan()
    {
        if (!edgePanEnabled) return;

        bool pointerOverUI = EventSystem.current && EventSystem.current.IsPointerOverGameObject();

        // While dragging an item, optionally bypass UI blocking so edge-pan still works
        if (!allowPanOverUI && pointerOverUI && !(allowEdgePanWhileDragging && draggingBoost))
            return;

        Vector3 mp = Input.mousePosition;
        int inner = edgeInnerPixels;
        int outer = edgeOuterPixels;

        // Slightly widen band while dragging to make it easier to trigger
        if (draggingBoost)
            outer += 12;

        // Signed strengths per axis (-1..1). Negative = left/bottom, Positive = right/top
        float sx = EdgeSignedStrength(mp.x, Screen.width,  inner, outer);
        float sy = EdgeSignedStrength(mp.y, Screen.height, inner, outer);

        // Ignore tiny strengths to prevent flicker/jitter
        if (Mathf.Abs(sx) < minStrengthToPan) sx = 0f;
        if (Mathf.Abs(sy) < minStrengthToPan) sy = 0f;

        float boost = draggingBoost ? dragEdgeBoost : 1f;

        // Target velocity (component-wise, keep diagonals sane)
        Vector2 targetVel = new Vector2(
            sx * edgeMaxSpeed * boost,
            sy * edgeMaxSpeed * boost
        );

        float dt = Time.deltaTime;

        // Smooth acceleration/deceleration per axis
        _edgeVel.x = Mathf.MoveTowards(_edgeVel.x, targetVel.x,
            (Mathf.Abs(targetVel.x) > 0.001f ? edgeAccel : edgeDecel) * dt);
        _edgeVel.y = Mathf.MoveTowards(_edgeVel.y, targetVel.y,
            (Mathf.Abs(targetVel.y) > 0.001f ? edgeAccel : edgeDecel) * dt);

        if (_edgeVel.sqrMagnitude > 0f)
            transform.position += (Vector3)_edgeVel * dt;
    }

    // Returns -1..1. Near left/bottom edges -> negative; right/top -> positive; 0 elsewhere.
    float EdgeSignedStrength(float coord, float total, int inner, int outer)
    {
        // Distance from the nearest edge on each side
        float dLow  = coord;           // from left/bottom
        float dHigh = total - coord;   // from right/top

        float left  = EdgeStrength01(dLow,  inner, outer);
        float right = EdgeStrength01(dHigh, inner, outer);

        // Right minus left gives signed strength
        return right - left;
    }

    // 0 outside the band; ramps up to 1 as you move from (inner+outer) toward the edge.
    float EdgeStrength01(float distFromEdge, int inner, int outer)
    {
        if (distFromEdge <= inner) return 1f; // inside the inner zone: full force
        if (distFromEdge <= inner + outer)
            return Mathf.InverseLerp(inner + outer, inner, distFromEdge); // gradient
        return 0f;
    }

    void ClampToBounds()
    {
        float vert  = cam.orthographicSize;
        float horiz = vert * cam.aspect;

        float cx = Mathf.Clamp(transform.position.x, minX + horiz, maxX - horiz);
        float cy = Mathf.Clamp(transform.position.y, minY + vert,  maxY - vert);

        transform.position = new Vector3(cx, cy, transform.position.z);
    }
}
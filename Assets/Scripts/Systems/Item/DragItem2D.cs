using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using Comfy.Camera;

[RequireComponent(typeof(Collider2D))]
public sealed class DragItem2D : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler
{
    [Header("Drag Feel")]
    [Tooltip("Lower = more smoothing. 0.05–0.12 feels good.")]
    public float smoothTime = 0.08f;
    [Tooltip("Cap the max speed to avoid overshoot on big camera moves.")]
    public float maxSpeed = 50f;
    [Tooltip("While dragging, temporarily ignore raycasts so edge-pan/UI never block.")]
    public bool ignoreRaycastWhileDragging = true;

    [Header("Sorting")]
    public bool raiseOnPick = true;
    [Tooltip("Amount to change sorting order by (can be negative). Example: 5 raises by +5, -1 lowers by -1")]
    public int sortingOrderChange = 5;
    [Tooltip("If true, apply sorting change to all SpriteRenderer children. If false, only this object's SpriteRenderer")]
    public bool applyToAllChildren = false;

    [Header("Snapping")]
    public float snapRadius = 0.5f;
    public bool parentToSnap = true;

    [Header("Snap Glide")]
    [Tooltip("Animate into the socket instead of teleporting.")]
    public bool glideOnSnap = true;
    [Tooltip("Time (seconds) to glide into the socket.")]
    public float snapGlideTime = 0.18f;
    [Tooltip("If > 0, clamps glide speed (units/sec). 0 = no clamp.")]
    public float snapGlideMaxSpeed = 0f;
    [Tooltip("Easing from 0..1 over time (default ease-in-out).")]
    public AnimationCurve snapGlideCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Tilt (Rotation)")]
    [Tooltip("Allow tilting the object while dragging using A/D keys")]
    public bool allowTilt = false;
    [Tooltip("Maximum tilt angle in degrees (applied both left and right)")]
    public float maxTiltAngle = 45f;
    [Tooltip("Speed of tilting in degrees per second")]
    public float tiltSpeed = 180f;
    [Tooltip("Reset rotation when dropped")]
    public bool resetRotationOnDrop = true;

    [Header("Follow Style")]
    [Tooltip("Snap the object directly to the pointer instead of smoothing when dragging.")]
    public bool instantFollowWhileDragging = true;

    [Header("Ignore Specific Colliders")]
    [Tooltip("If clicking on these colliders, don't start dragging (e.g., tear corners)")]
    public Collider2D[] ignoreColliders;
    public static DragItem2D Current { get; private set; }

    // Optional camera helpers you already have
    CameraFollowDuringDrag2D camFollower;     // BeginFollow/EndFollow
    CameraPanZoom2DPlus camPanBoost;          // SetDraggingBoost(true/false)

    Camera cam;
    CameraComfortRuntime comfy;            // Comfy Camera integration
    DragFocusTarget2D comfyFocus;          // ICameraFocusTarget adapter
    SpriteRenderer sr;
    SpriteRenderer[] allRenderers; // for applyToAllChildren
    int originalOrder;
    int originalLayer;

    // Drag state
    public bool dragging;
    public Vector3 grabOffsetWorld;      // world offset from cursor to pivot
    Vector2 lastPointerScreen;    // last screen position from OnDrag
    Vector3 smoothVel;            // SmoothDamp velocity carrier
    Vector3 pendingTarget;        // target world pos computed in LateUpdate

    // Public accessors for other scripts
    public bool IsDragging => dragging;
    public int OriginalSortingOrder => originalOrder;
    public int CurrentSortingBoost => raiseOnPick ? sortingOrderChange : 0;

    // Tilt state
    float currentTiltAngle;       // current Z rotation in degrees
    float originalRotation;       // rotation at pickup (changes each pickup)
    float permanentOriginalRotation; // true original rotation (set once in Awake)
    bool hasSetPermanentRotation; // track if we've set the permanent rotation

    // (Optional) Rigidbody2D path (recommended if you already use Rigidbodies)
    Rigidbody2D rb;
    public bool useRigidbody = false;   // if true, uses rb.MovePosition in FixedUpdate
    bool hasFixedTarget;

    // Cup glue (optional; null-safe)
    CupSnapper cupSnapper;

    void Awake()
    {
        cam = Camera.main;
        sr  = GetComponent<SpriteRenderer>();
        rb  = GetComponent<Rigidbody2D>();
        cupSnapper = GetComponent<CupSnapper>();

        if (sr) originalOrder = sr.sortingOrder;
        originalLayer = gameObject.layer;

        // Cache all sprite renderers if we're applying to children
        if (applyToAllChildren)
        {
            allRenderers = GetComponentsInChildren<SpriteRenderer>(true);
        }

        if (cam)
        {
            camFollower = cam.GetComponent<CameraFollowDuringDrag2D>();
            camPanBoost = cam.GetComponent<CameraPanZoom2DPlus>();
        }

        // Try to locate the Comfy Camera runtime in the scene
        comfy = Object.FindFirstObjectByType<CameraComfortRuntime>();

        if (useRigidbody && rb)
        {
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        }

        // Store the permanent original rotation (only set once)
        if (!hasSetPermanentRotation)
        {
            permanentOriginalRotation = transform.eulerAngles.z;
            hasSetPermanentRotation = true;
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        // Only allow left mouse button to drag
        if (eventData.button != PointerEventData.InputButton.Left)
            return;

        // Check if we clicked on an ignored collider (like a tear corner)
        if (ignoreColliders != null && ignoreColliders.Length > 0)
        {
            Vector2 worldPoint = cam.ScreenToWorldPoint(eventData.position);
            foreach (var ignoredCol in ignoreColliders)
            {
                if (ignoredCol != null && ignoredCol.OverlapPoint(worldPoint))
                {
                    // Clicked on an ignored collider, don't start dragging
                    return;
                }
            }
        }

        dragging = true;
        Current = this;
        CustomCursorManager.Instance?.StartGrabCursor();
#if UNITY_EDITOR
        Debug.Log("Dragging: " + DragItem2D.Current.name);
#endif
        lastPointerScreen = eventData.position;

        // compute grab offset in world
        var world = cam.ScreenToWorldPoint(lastPointerScreen);
        world.z = transform.position.z;
        grabOffsetWorld = transform.position - world;

        // Refresh renderer cache to include any dynamically added children (e.g., ice cubes in mug)
        if (applyToAllChildren)
        {
            allRenderers = GetComponentsInChildren<SpriteRenderer>(true);
        }

        if (raiseOnPick)
        {
            if (applyToAllChildren && allRenderers != null)
            {
                foreach (var renderer in allRenderers)
                {
                    if (renderer != null)
                        renderer.sortingOrder += sortingOrderChange;
                }
            }
            else if (sr)
            {
                sr.sortingOrder += sortingOrderChange;
            }
        }

        if (ignoreRaycastWhileDragging)
            gameObject.layer = LayerMask.NameToLayer("Ignore Raycast");

        // Skip legacy camera follow/pan when stations mode is active
        bool stationsMode = comfy != null && comfy.Settings.stationMode;
        if (!stationsMode)
        {
            camFollower?.BeginFollow(transform);
            camPanBoost?.SetDraggingBoost(true);
        }

        // Inform the comfort rig to keep this item centered while dragging
        if (comfy == null)
            comfy = Object.FindFirstObjectByType<CameraComfortRuntime>();
        if (comfy != null)
        {
            if (comfyFocus == null)
                comfyFocus = GetComponent<DragFocusTarget2D>() ?? gameObject.AddComponent<DragFocusTarget2D>();
            comfy.SetFocusTargets(comfyFocus, null, true);
        }

        // Reset smooth state so we don't inherit stale velocity
        smoothVel = Vector3.zero;
        hasFixedTarget = false;

        // Store current rotation for this pickup
        originalRotation = transform.eulerAngles.z;

        // Initialize tilt to current absolute rotation
        float currentRotation = transform.eulerAngles.z;
        if (currentRotation > 180f) currentRotation -= 360f;
        currentTiltAngle = currentRotation;

        // tell socket to release if we were snapped
        cupSnapper?.OnPickUp();

        // Cancel any snap glide in progress when picking up again
        StopAllCoroutines();
    }

    // IMPORTANT: do NOT move the object here. Just cache the latest pointer position.
    public void OnDrag(PointerEventData eventData)
    {
        if (!dragging) return;
        lastPointerScreen = eventData.position;
    }

    // Move AFTER the camera pans/zooms for the frame to avoid mismatch jitter.
    void LateUpdate()
    {
        if (!dragging) return;

        // Handle tilting with A/D keys
        if (allowTilt)
        {
            HandleTilt();
        }

        // Recompute target with the *final* camera transform for this frame.
        var world = cam.ScreenToWorldPoint(lastPointerScreen);
        world.z = transform.position.z;
        Vector3 target = world + grabOffsetWorld;
        pendingTarget = target;

        if (useRigidbody && rb)
        {
            hasFixedTarget = true;
        }
        else
        {
            if (instantFollowWhileDragging)
            {
                transform.position = pendingTarget;
                smoothVel = Vector3.zero;
            }
            else
            {
                Vector3 next = Vector3.SmoothDamp(transform.position, pendingTarget, ref smoothVel, smoothTime, maxSpeed, Time.unscaledDeltaTime);
                transform.position = next;
            }
        }
    }

    void HandleTilt()
    {
        float tiltInput = 0f;

        // A key = tilt left (positive), D key = tilt right (negative)
        if (Input.GetKey(KeyCode.A))
            tiltInput = 1f;
        else if (Input.GetKey(KeyCode.D))
            tiltInput = -1f;

        // Apply tilt
        if (Mathf.Abs(tiltInput) > 0.01f)
        {
            currentTiltAngle += tiltInput * tiltSpeed * Time.deltaTime;
            currentTiltAngle = Mathf.Clamp(currentTiltAngle, -maxTiltAngle, maxTiltAngle);
        }

        // Update rotation (permanent original rotation + tilt)
        Vector3 euler = transform.eulerAngles;
        euler.z = currentTiltAngle;
        transform.eulerAngles = euler;
    }

    // Physics-friendly move (if you opted into Rigidbody)
    void FixedUpdate()
    {
        if (!(useRigidbody && rb && hasFixedTarget)) return;

        if (instantFollowWhileDragging)
        {
            rb.MovePosition(pendingTarget);
            hasFixedTarget = false;
            smoothVel = Vector3.zero;
            return;
        }

        Vector3 next = Vector3.SmoothDamp(rb.position, pendingTarget, ref smoothVel, smoothTime, maxSpeed, Time.fixedDeltaTime);
        rb.MovePosition(next);
        hasFixedTarget = false;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        dragging = false;
        CustomCursorManager.Instance?.StopGrabCursor();
        if (Current == this)
        {
#if UNITY_EDITOR
            Debug.Log($"Dropped item: {name}");
#endif
            Current = null;
        }

        TrySnap(); // uses SnapPoint2D adapter

        // Refresh renderer cache again to ensure we revert all children we raised
        if (applyToAllChildren)
        {
            allRenderers = GetComponentsInChildren<SpriteRenderer>(true);
        }

        // Restore sorting order
        if (raiseOnPick)
        {
            if (applyToAllChildren && allRenderers != null)
            {
                foreach (var renderer in allRenderers)
                {
                    if (renderer != null)
                        renderer.sortingOrder -= sortingOrderChange;
                }
            }
            else if (sr)
            {
                sr.sortingOrder = originalOrder;
            }
        }

        if (ignoreRaycastWhileDragging)
            gameObject.layer = originalLayer;

        bool stationsMode = comfy != null && comfy.Settings.stationMode;
        if (!stationsMode)
        {
            camFollower?.EndFollow();
            camPanBoost?.SetDraggingBoost(false);
        }

        // Release comfort rig focus when drop completes
        if (comfy != null)
        {
            comfy.SetFocusTargets(null, null, false);
        }

        // Only reset rotation if enabled (reset to 0 degrees)
        if (allowTilt && resetRotationOnDrop)
        {
            Vector3 euler = transform.eulerAngles;
            euler.z = 0f;
            transform.eulerAngles = euler;
            currentTiltAngle = 0f; // Reset tilt tracking
        }
    }

    void TrySnap()
    {
        SnapPoint2D best = null;
        float bestDist = snapRadius;

        // Find all potential snap points (sockets expose this via the adapter)
        foreach (var sp in FindObjectsByType<SnapPoint2D>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            float d = Vector2.Distance(sp.transform.position, transform.position);
            if (d <= bestDist && sp.CanAccept(this))
            {
                best = sp; bestDist = d;
            }
        }

        if (!best) return;

        // If gliding is enabled, animate towards the socket then finalize
        if (glideOnSnap && snapGlideTime > 0f)
        {
            StopAllCoroutines(); // just in case
            StartCoroutine(GlideToSocket(best));
            return;
        }

        // Instant snap (old behavior)
        if (useRigidbody && rb)
            rb.position = best.transform.position;
        else
            transform.position = best.transform.position;

        if (parentToSnap) transform.SetParent(best.transform, true);

        // Let the socket system take ownership (parenting/flags/etc)
        best.NotifySnapped(this);

        // Notify any listeners on this item that we've been snapped
        NotifySnapListeners(best);
    }

    IEnumerator GlideToSocket(SnapPoint2D targetSocket)
    {
        if (targetSocket == null) yield break;

        Vector3 startPos = useRigidbody && rb ? (Vector3)rb.position : transform.position;
        Vector3 endPos   = targetSocket.transform.position;

        float t = 0f;
        Vector3 lastPos = startPos;

        // While gliding, ignore accidental physics bumps
        if (useRigidbody && rb)
            rb.linearVelocity = Vector2.zero;

        while (t < snapGlideTime)
        {
            // If we somehow started dragging again, abort the glide
            if (dragging) yield break;
            if (targetSocket == null) yield break;

            t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(t / snapGlideTime);
            float eased = snapGlideCurve != null ? snapGlideCurve.Evaluate(u) : u;

            // Desired position this frame
            Vector3 desired = Vector3.LerpUnclamped(startPos, endPos, eased);

            // Optional speed clamp
            if (snapGlideMaxSpeed > 0f)
            {
                float maxStep = snapGlideMaxSpeed * Time.unscaledDeltaTime;
                Vector3 step = desired - lastPos;
                if (step.magnitude > maxStep) desired = lastPos + step.normalized * maxStep;
            }

            if (useRigidbody && rb)
                rb.MovePosition(desired);
            else
                transform.position = desired;

            lastPos = desired;
            yield return null;
        }

        // Snap exactly to the socket at the end
        if (useRigidbody && rb)
            rb.position = endPos;
        else
            transform.position = endPos;

        if (parentToSnap) transform.SetParent(targetSocket.transform, true);

        // Finalize with socket notify
        targetSocket.NotifySnapped(this);

        // Notify any listeners on this item that we've been snapped
        NotifySnapListeners(targetSocket);
    }

    void NotifySnapListeners(SnapPoint2D socket)
    {
        var listeners = GetComponents<IOnSnappedListener>();
        if (listeners == null || listeners.Length == 0) return;
        foreach (var l in listeners)
        {
            if (l == null) continue;
            try { l.OnSnapped(socket); } catch { }
        }
    }
}

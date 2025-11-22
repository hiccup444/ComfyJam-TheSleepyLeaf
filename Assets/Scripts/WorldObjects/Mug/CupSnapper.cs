using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public sealed class CupSnapper : MonoBehaviour
{
    [Header("Search (used only if you call OnDrop instead of DragItem2D adapter)")]
    [SerializeField] private LayerMask socketLayer;     // set to your "Socket" layer
    [SerializeField, Min(0.01f)] private float searchRadius = 0.35f;

    [Header("State (read-only)")]
    [SerializeField] private bool isSnapped = false;
    public bool IsSnapped => isSnapped;
    public SnapSocket CurrentSocket { get; private set; }

    private static readonly HashSet<int> WarnedSockets = new();
    private bool serveCoordinatorLock;
    public bool IsServeCoordinatorLocked => serveCoordinatorLock;
    public void SetServeCoordinatorLock(bool value) => serveCoordinatorLock = value;

    // Call when the player starts dragging/picks up the cup
    public void OnPickUp()
    {
        if (isSnapped && CurrentSocket != null)
        {
            CurrentSocket.Release(this);
            CurrentSocket = null;
            isSnapped = false;
        }

        // Notify sorting/ice systems that we're no longer socketed
        var toggle = GetComponentInParent<CupSortingGroupToggle>();
        toggle?.SetInSocket(false);
    }

    // Optional path if you don't use DragItem2D.TrySnap():
    // Call this on release to auto-find the nearest socket by layer.
    public void OnDrop()
    {
        var hits = Physics2D.OverlapCircleAll(transform.position, searchRadius, socketLayer);
        SnapSocket best = null;
        float bestDist = float.PositiveInfinity;

        foreach (var h in hits)
        {
            var socket = h.GetComponent<SnapSocket>();
            if (socket == null) continue;
            if (!socket.CanAccept(this)) continue;

            var p = socket.transform.position;
            float d = (transform.position - p).sqrMagnitude;
            if (d < bestDist)
            {
                bestDist = d;
                best = socket;
            }
        }

        if (best != null)
            best.Snap(this);
    }

    // Called by SnapSocket when snapping is approved
    public void SetSnapped(SnapSocket socket, float settleSpeed)
    {
        if (serveCoordinatorLock)
        {
#if UNITY_EDITOR
            Debug.Log("[SNAP] ServeCoordinator lock active; ignoring SetSnapped", this);
#endif
            return;
        }

        if (serveCoordinatorLock || CustomerServeZone.IsCupRegistered(gameObject.GetInstanceID()))
        {
#if UNITY_EDITOR
            Debug.Log("[SNAP] ServeCoordinator/ServeZone lock active; ignoring snap attempt.", this);
#endif
            socket = null;
        }

        CurrentSocket = socket;
        isSnapped = true;

        var target = socket && socket.transform ? (socket.snapPoint ? socket.snapPoint : socket.transform) : null;
        if (target)
        {
            transform.SetParent(socket.transform, worldPositionStays: true);
            transform.position = target.position;
            transform.rotation = target.rotation;

            if (TryGetComponent<Rigidbody2D>(out var rb2d))
            {
                // Keeping linearVelocity to match your existing codebase.
                rb2d.linearVelocity = Vector2.zero;
                rb2d.angularVelocity = 0f;
            }
        }

        // Inform sorting/ice systems that we're now socketed
        var toggle = GetComponentInParent<CupSortingGroupToggle>();
        toggle?.SetInSocket(true);

        // Play mug put-down SFX only when successfully snapped into a socket.
        var pickupAudio = GetComponentInChildren<MugPickupAudio>();
        pickupAudio?.PlayPutDown();

        // --- Notify ServeHandoff that a cup was dropped here ---
        var handoff = socket ? socket.GetComponentInParent<ServeHandoff>() : null;
        if (handoff != null)
        {
            handoff.OnCupDropped(gameObject);
#if UNITY_EDITOR
            Debug.Log("[SNAP] Notified ServeHandoff.OnCupDropped()", gameObject);
#endif
        }
        else if (socket && Application.isPlaying)
        {
            var id = socket.GetInstanceID();
            if (WarnedSockets.Add(id))
            {
#if UNITY_EDITOR
                Debug.Log("[SNAP] No ServeHandoff found on/above socket '" + socket.name + "'. Socket will not trigger ServeHandoff.", socket);
#endif
            }
        }
    }

    public void ForceRelease(bool detachTransform = true)
    {
        var socket = CurrentSocket;
        if (socket != null)
        {
            socket.Release(this);
            CurrentSocket = null;
            isSnapped = false;
        }

        if (detachTransform)
        {
            transform.SetParent(null, worldPositionStays: true);
        }
    }

    void OnDestroy()
    {
        ForceRelease(detachTransform: false);
    }
}

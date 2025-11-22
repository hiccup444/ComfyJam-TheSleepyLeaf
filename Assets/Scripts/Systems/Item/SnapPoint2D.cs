using UnityEngine;

[RequireComponent(typeof(SnapSocket))]
public sealed class SnapPoint2D : MonoBehaviour
{
    // ------------------------
    // Configurable filtering
    // ------------------------
    [Header("Filters (empty = accept all)")]
    [Tooltip("Unity Tags this socket accepts. Leave empty to accept all tags.")]
    [SerializeField] private string[] allowedTags;

    [Tooltip("Layers this socket accepts. Default (~0) = all layers.")]
    [SerializeField] private LayerMask allowedLayers = ~0;

    [Header("Generic Fallback")]
    [Tooltip("If the dragged item has no CupSnapper (or the socket won't accept it), allow a generic snap (parenting only).")]
    [SerializeField] private bool allowGenericSnapIfNoCupSnapper = true;

    private SnapSocket socket;

    void Awake() => socket = GetComponent<SnapSocket>();

    /// <summary>
    /// Returns true if this snap point is willing to accept the given item,
    /// based on layer/tag filters and (if present) the socket’s rules.
    /// </summary>
    public bool CanAccept(DragItem2D item)
    {
        if (socket == null || item == null) return false;

        var go = item.gameObject;

        // Layer filter
        if (((1 << go.layer) & allowedLayers) == 0)
            return false;

        // Tag filter (empty list => accept all)
        if (allowedTags != null && allowedTags.Length > 0)
        {
            bool tagMatched = false;
            foreach (var t in allowedTags)
            {
                if (!string.IsNullOrEmpty(t) && go.CompareTag(t))
                {
                    tagMatched = true;
                    break;
                }
            }
            if (!tagMatched) return false;
        }

        // If the item has a CupSnapper and the socket approves, accept
        if (go.TryGetComponent(out CupSnapper cup) && socket.CanAccept(cup))
            return true;

        // Look for ANY component on this GameObject that implements IGenericSnapReceiver
        var generic = GetGenericReceiver();
        if (generic != null)
            return generic.CanAccept(go);

        // Otherwise, allow generic snap (parenting only) if enabled
        return allowGenericSnapIfNoCupSnapper;
    }

    /// <summary>
    /// Called after DragItem2D has moved/parented the item here.
    /// Delegates to the socket if possible; otherwise performs a generic fallback.
    /// </summary>
    public void NotifySnapped(DragItem2D item)
    {
        if (socket == null || item == null) return;

        var go = item.gameObject;

        // Preferred path: CupSnapper + socket rules
        if (go.TryGetComponent(out CupSnapper cup))
        {
            if (socket.CanAccept(cup))
            {
                socket.Snap(cup);
                return;
            }
            // Respect socket refusal for cup items
#if UNITY_EDITOR
            Debug.Log($"[SnapPoint2D] Socket refused CupSnapper on '{go.name}'.", this);
#endif
            return;
        }

        // Generic path via optional interface on *any* component at this socket
        var generic = GetGenericReceiver();
        if (generic != null)
        {
            if (generic.CanAccept(go))
            {
                generic.Snap(go);
                return;
            }
#if UNITY_EDITOR
            Debug.Log($"[SnapPoint2D] Generic receiver refused '{go.name}'.", this);
#endif
            return;
        }

        // Final fallback: do nothing special; DragItem2D already positioned/parented
        if (allowGenericSnapIfNoCupSnapper)
        {
#if UNITY_EDITOR
            Debug.Log($"[SnapPoint2D] Generic snap accepted '{go.name}' (no CupSnapper, no generic receiver).", this);
#endif
            return;
        }

#if UNITY_EDITOR
        Debug.Log($"[SnapPoint2D] No acceptable snap handler for '{go.name}'.", this);
#endif
    }

    /// <summary>
    /// Finds the first MonoBehaviour on this GameObject that implements IGenericSnapReceiver.
    /// (Unity's GetComponent<T>() can't directly search for interfaces unless T : Component.)
    /// </summary>
    private IGenericSnapReceiver GetGenericReceiver()
    {
        var behaviours = GetComponents<MonoBehaviour>();
        foreach (var mb in behaviours)
        {
            if (mb is IGenericSnapReceiver rcv) return rcv;
        }
        return null;
    }
}

/// <summary>
/// Optional: implement on any component at the socket to support non-cup items (IceScoop, Spoon, etc.)
/// </summary>
public interface IGenericSnapReceiver
{
    bool CanAccept(GameObject go);
    void Snap(GameObject go);
}

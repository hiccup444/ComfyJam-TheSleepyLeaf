using UnityEngine;

/// <summary>
/// 2D trigger that accepts FirewoodItem.
/// On collision with a wood piece, calls FireController.AddLog() and consumes the wood.
/// Ensure either this zone or the wood piece has a Rigidbody2D for trigger messages.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider2D))]
public sealed class FireZone : MonoBehaviour
{
    [Tooltip("The FireController to receive logs.")]
    public FireController fireController;

    [Tooltip("Enable debug logs for enter events.")]
    public bool debugLogs = false;

    private void Reset()
    {
        var col = GetComponent<Collider2D>();
        if (col) col.isTrigger = true;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!fireController) return;

        // Try exact object first, then parent
        var wood = other.GetComponent<FirewoodItem>();
        if (!wood) wood = other.GetComponentInParent<FirewoodItem>();
        if (!wood) return;

#if UNITY_EDITOR
        if (debugLogs)
            Debug.Log($"[FireZone] Wood entered: {wood.name}", this);
#endif

        bool accepted = fireController.AddLog();
        if (accepted)
        {
            wood.Consume();
        }
        else if (debugLogs)
        {
#if UNITY_EDITOR
            Debug.Log("[FireZone] Fire at capacity; wood ignored.", this);
#endif
        }
    }
}


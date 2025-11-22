// SinkZone2D.cs
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public sealed class SinkZone2D : MonoBehaviour
{
    [Tooltip("Leave empty to accept any tag (e.g., Cup).")]
    [SerializeField] string[] allowedTags;

    [Tooltip("Optional layer filter. ~0 = all layers.")]
    [SerializeField] LayerMask allowedLayers = ~0;

    [Tooltip("Print trigger/debug info to the Console.")]
    [SerializeField] bool debugLogs = false;

    void Reset()
    {
        var col = GetComponent<Collider2D>();
        col.isTrigger = true;
    }

    bool PassesFilters(GameObject go)
    {
        if (((1 << go.layer) & allowedLayers) == 0) return false;
        if (allowedTags == null || allowedTags.Length == 0) return true;
        foreach (var t in allowedTags)
            if (!string.IsNullOrEmpty(t) && go.CompareTag(t))
                return true;
        return false;
    }

    void NotifyEnterOrStay(Collider2D other, string phase)
    {
        if (!PassesFilters(other.gameObject)) return;

        var cupPour = other.GetComponentInParent<CupPourController>();
        if (cupPour != null)
        {
#if UNITY_EDITOR
            if (debugLogs) Debug.Log($"[SinkZone2D] {phase} → {cupPour.name}", this);
#endif
            cupPour.RequestDumpOverSink(this); // refresh "seen" timestamp every frame
        }
        else if (debugLogs)
        {
#if UNITY_EDITOR
            Debug.Log($"[SinkZone2D] {phase} but no CupPourController on {other.name}/{other.transform.root.name}", this);
#endif
        }
    }

    void OnTriggerEnter2D(Collider2D other) => NotifyEnterOrStay(other, "Enter");
    void OnTriggerStay2D (Collider2D other) => NotifyEnterOrStay(other, "Stay");

    void OnTriggerExit2D(Collider2D other)
    {
        var cupPour = other.GetComponentInParent<CupPourController>();
        if (cupPour != null)
        {
#if UNITY_EDITOR
            if (debugLogs) Debug.Log($"[SinkZone2D] Exit → {cupPour.name}", this);
#endif
            cupPour.StopDumpFromSink(this); // explicit clear on exit
        }
    }
}

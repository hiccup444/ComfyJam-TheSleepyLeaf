using UnityEngine;

[ExecuteAlways]
public sealed class SnapSocket : MonoBehaviour
{
    [Header("Accepts")]
    [SerializeField] private string acceptTag = "Cup";

    [Header("Snap")]
    [Tooltip("Optional precise alignment point; if null, uses this transform.")]
    public Transform snapPoint;
    [SerializeField, Min(0f)] private float snapLerpSpeed = 30f; // reserved if you add smooth settle later

    [Header("State (read-only)")]
    [SerializeField] private bool occupied = false;
    public bool Occupied => occupied;
    public CupSnapper CurrentCup { get; private set; }

    public bool CanAccept(CupSnapper cup)
    {
        if (occupied || cup == null) return false;
        if (!string.IsNullOrEmpty(acceptTag) && !cup.CompareTag(acceptTag)) return false;
        return true;
    }

    public void Snap(CupSnapper cup)
    {
        if (!CanAccept(cup)) return;
        if (cup.IsServeCoordinatorLocked)
        {
#if UNITY_EDITOR
            Debug.Log("[SNAP] Cup locked by ServeCoordinator; snap skipped.", this);
#endif
            return;
        }
        occupied = true;
        CurrentCup = cup;
        cup.SetSnapped(this, snapLerpSpeed);
#if UNITY_EDITOR
        Debug.Log($"[SNAP] Snapped '{cup.name}' to '{name}'", this);
#endif
    }

    public void Release(CupSnapper cup)
    {
        if (cup != null && CurrentCup == cup)
        {
            occupied = false;
            CurrentCup = null;
        }
    }

    private void OnDrawGizmos()
    {
        var t = snapPoint ? snapPoint : transform;
        Gizmos.color = new Color(0.3f, 1f, 0.7f, 0.35f);
        Gizmos.DrawSphere(t.position, 0.08f);
        Gizmos.color = new Color(0.3f, 1f, 0.7f, 1f);
        Gizmos.DrawWireSphere(t.position, 0.12f);

        // forward/up arrow (2D assumes Z-forward)
        Vector3 dir = t.up * 0.25f;
        Gizmos.DrawLine(t.position, t.position + dir);
    }
}

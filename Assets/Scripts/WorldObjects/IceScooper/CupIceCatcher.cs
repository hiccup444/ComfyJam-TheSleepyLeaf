using UnityEngine;

/// Put this on Mug/Visuals/CupCatchArea (trigger collider).
[RequireComponent(typeof(Collider2D))]
public sealed class CupIceCatcher : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private MugIceState mugIce;      // auto-find if null
    [SerializeField] private Transform iceParent;     // Mug/Visuals/IceContainer (optional)

    [Header("Scoring")]
    [SerializeField] private int icePerCube = 1;

    [Header("Visual Cap (optional)")]
    [Tooltip("If > 0, destroy oldest child cubes when this many are already parented.")]
    [SerializeField] private int maxVisualCubes = 0;

    private Collider2D _trigger;

    void Awake()
    {
        _trigger = GetComponent<Collider2D>();
        if (!_trigger.isTrigger)
#if UNITY_EDITOR
            Debug.LogWarning("[CupIceCatcher] Catch collider is not a trigger.");
#endif

        if (!mugIce) mugIce = GetComponentInParent<MugIceState>();

        // Prefer serialized iceParent; otherwise try to find/create it at runtime.
        if (!iceParent && mugIce)
        {
            iceParent = FindIceContainer(mugIce.transform);
        }
    }

    void OnTriggerEnter2D(Collider2D other) => TryCatch(other);

    void TryCatch(Collider2D other)
    {
        if (!mugIce) return;

        var cube = other.GetComponent<IceCube2D>();
        if (!cube) return;
        if (!cube.RequestCatch()) return; // only once & when falling in

        // Dual-renderer root so both sprites (inside/over) move together
        var dual = other.GetComponentInParent<IceCubeDualRenderer>();

        cube.MarkCaught();
        cube.TryApplyCaughtDamping();
        if (dual) dual.MarkCaught();

        // Parent under Visuals/IceContainer (create if missing)
        var parent = iceParent ? iceParent : GetIceParent();
        (dual ? dual.transform : cube.transform).SetParent(parent, true);

        // Optional: cap visuals (remove oldest)
        if (maxVisualCubes > 0)
        {
            int toCull = Mathf.Max(0, parent.childCount - maxVisualCubes);
            for (int i = 0; i < parent.childCount && toCull > 0; i++)
            {
                var child = parent.GetChild(i);
                if (!child) continue;
                if (child.GetComponent<IceCubeDualRenderer>() || child.GetComponentInChildren<IceCube2D>())
                {
                    Object.Destroy(child.gameObject);
                    toCull--;
                }
            }
        }

        mugIce.AddIce(icePerCube);
        // TODO: SFX/VFX here if needed
    }

    // --- helpers ---

    Transform GetIceParent()
    {
        if (!mugIce) return transform;
        // Use cached if we have it, otherwise find/create
        return iceParent = iceParent ? iceParent : FindIceContainer(mugIce.transform);
    }

    static Transform FindIceContainer(Transform mugRoot)
    {
        var container = mugRoot.Find("Visuals/IceContainer");
        if (container) return container;

        var visuals = mugRoot.Find("Visuals");
        var go = new GameObject("IceContainer");
        var t = go.transform;
        t.SetParent(visuals ? visuals : mugRoot, true);
        t.localPosition = Vector3.zero;
        t.localRotation = Quaternion.identity;
        t.localScale = Vector3.one;
        return t;
    }
}

using UnityEngine;

/// <summary>
/// Minimal cup ice tracker. Attach to the mug (same GameObject as MugBeverageState
/// or a parent). Other systems can call AddIce(amount) to increment.
/// </summary>
[DisallowMultipleComponent]
public sealed class MugIceState : MonoBehaviour
{
    [SerializeField, Tooltip("Current number of ice units in the cup.")]
    private int iceCount;

    [Header("Optional Visual Root")]
    [SerializeField, Tooltip("If set, all IceCube2D children under this transform are destroyed when ice is cleared. Defaults to this transform.")]
    private Transform iceVisualRoot;

    public int IceCount => iceCount;
    public bool HasIce => iceCount > 0;

    /// <summary>Adds 'amount' ice to the cup.</summary>
    public void AddIce(int amount)
    {
        if (amount <= 0) return;
        iceCount += amount;
        // TODO: hook up visuals (enable ice overlay, update sprite, etc.)
    }

    /// <summary>Clears all ice (e.g., when dumping the cup).</summary>
    public void ClearIce()
    {
        iceCount = 0;
        // TODO: hide ice overlay visuals if you add them.

        var root = iceVisualRoot != null ? iceVisualRoot : transform;
        if (root == null) return;

        var cubes = root.GetComponentsInChildren<IceCube2D>(includeInactive: true);
        for (int i = 0; i < cubes.Length; i++)
        {
            var cube = cubes[i];
            if (cube != null)
                Destroy(cube.gameObject);
        }
    }
}

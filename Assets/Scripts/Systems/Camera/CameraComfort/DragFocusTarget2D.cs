using UnityEngine;
using Comfy.Camera;

/// <summary>
/// Simple adapter so draggables can act as camera focus targets for the comfort rig.
/// Computes bounds from Collider2D or Renderer, with a small fallback size.
/// </summary>
[DisallowMultipleComponent]
public sealed class DragFocusTarget2D : MonoBehaviour, ICameraFocusTarget
{
    [Tooltip("Extra padding (world units) added to the computed bounds.")]
    public float padding = 0.25f;

    public Bounds GetFocusBounds()
    {
        // Prefer collider bounds for an accurate shape while dragging
        var col = GetComponent<Collider2D>();
        if (col != null)
        {
            var b = col.bounds;
            if (padding > 0f) b.Expand(padding * 2f);
            return b;
        }

        // Fallback to any renderer bounds (sprite, etc.)
        var rend = GetComponentInChildren<Renderer>();
        if (rend != null)
        {
            var b = rend.bounds;
            if (padding > 0f) b.Expand(padding * 2f);
            return b;
        }

        // Last resort: tiny bounds around transform
        var pos = transform.position;
        var bounds = new Bounds(pos, new Vector3(0.1f, 0.1f, 0.1f));
        if (padding > 0f) bounds.Expand(padding * 2f);
        return bounds;
    }
}


using UnityEngine;

public sealed class IceCubeDualRenderer : MonoBehaviour
{
    [Header("Renderers")]
    [SerializeField] SpriteRenderer insideRenderer; // masked piece (inside cup)
    [SerializeField] SpriteRenderer overRenderer;   // unmasked topper (above water)

    [Header("Sorting")]
    [SerializeField] string sortingLayer = "Stations-Mug";
    [SerializeField] int insideOrder = 298; // below rim, above water
    [SerializeField] int overOrder   = 299; // just under rim (mugFront=300)

    void Reset() {
        var srs = GetComponentsInChildren<SpriteRenderer>(true);
        if (srs.Length >= 2) { insideRenderer = srs[0]; overRenderer = srs[1]; }
    }

    void OnEnable() => MarkAirborne(); // spawn state = only topper visible

    public void MarkAirborne()
    {
        if (insideRenderer)
        {
            insideRenderer.enabled = false;
            insideRenderer.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
            insideRenderer.sortingLayerName = sortingLayer;
            insideRenderer.sortingOrder = insideOrder;
        }
        if (overRenderer)
        {
            overRenderer.enabled = true;
            overRenderer.maskInteraction = SpriteMaskInteraction.None;
            overRenderer.sortingLayerName = sortingLayer;
            overRenderer.sortingOrder = overOrder;
        }
    }

    public void MarkCaught() // call when it lands in cup
    {
        if (insideRenderer)
        {
            insideRenderer.enabled = true;
            insideRenderer.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
            insideRenderer.sortingLayerName = sortingLayer;
            insideRenderer.sortingOrder = insideOrder;
        }
        if (overRenderer)
        {
            overRenderer.enabled = true;
            overRenderer.maskInteraction = SpriteMaskInteraction.None;
            overRenderer.sortingLayerName = sortingLayer;
            overRenderer.sortingOrder = overOrder;
        }
    }
}

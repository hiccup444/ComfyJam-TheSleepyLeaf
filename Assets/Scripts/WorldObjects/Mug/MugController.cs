// MugController.cs
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Controls a single mug's fill sequence: shows stream, raises in-cup water under a mask,
/// and (optionally) runs a dual-layer shimmer using an overlay stream renderer.
/// UI buttons should call FillWith(LiquidIngredient).
/// </summary>
[DisallowMultipleComponent]
public sealed class MugController : MonoBehaviour
{
    [Header("Required References")]
    [SerializeField] private Transform fillPivot;                 // Bottom-center of interior; parent of inCupRenderer
    [SerializeField] private SpriteRenderer inCupRenderer;        // "Water in cup" surface (masked). Disabled by default.
    [SerializeField] private SpriteMask waterMask;                // Mug interior mask (clips inCupRenderer)
    [SerializeField] private Transform liquidSurface;             // Trigger that marks the liquid surface height
    [SerializeField] private BoxCollider2D liquidSurfaceCollider; // Thin trigger collider sitting at the surface
    [SerializeField] private float liquidSurfaceThickness = 0.05f;

    [Header("Stream (Falling Column)")]
    [SerializeField] private GameObject streamGO;                 // Parent GO for the falling stream pieces (enable/disable)
    [SerializeField] private SpriteRenderer streamRenderer;       // Base stream column (e.g., water_stream)
    [SerializeField] private SpriteRenderer streamOverlayRenderer;// Overlay stream (e.g., water_effect), lower alpha for shimmer

    [Header("Stream Reveal / Overlay Fades")]
    [Tooltip("Time for the stream column to grow from 0→full length. Curve controls ease (default ease-in).")]
    [SerializeField] private float streamRevealSeconds = 0.30f;
    [SerializeField] private AnimationCurve streamRevealCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    [Tooltip("Fade-in time for the overlay (water effect) alpha when pouring starts.")]
    [SerializeField] private float overlayFadeInSeconds = 0.20f;
    [Tooltip("Fade-out time for the overlay alpha when the pour stops.")]
    [SerializeField] private float overlayFadeOutSeconds = 0.15f;

    [Header("Sorting / Visual UX")]
    [Tooltip("Temporarily raise all sprite renderers' sorting order while filling to ensure visibility.")]
    [SerializeField] private int raiseSortingOrderOnFill = 100;
    [SerializeField] private bool restoreSortingOrderAfterFill = true;
    
    [Tooltip("All sprite renderers in the mug hierarchy that should have sorting raised during fill.")]
    [SerializeField] private List<SpriteRenderer> mugSpriteRenderers = new List<SpriteRenderer>();

    [Header("Timing")]
    [Tooltip("Delay before the in-cup water begins rising (lets the stream hit the rim first).")]
    [SerializeField] private float riseDelaySeconds = 0.15f;

    [Tooltip("Extra time to leave the stream visible after the rise ends.")]
    [SerializeField] private float streamTailSeconds = 0.10f;

    [Header("State (Read-Only)")]
    [SerializeField, Range(0f, 1f)] private float currentFill = 0f;
    public bool IsFilling => _isFilling;
    public float CurrentFill => currentFill;

    private AudioSource _audio;
    private bool _isFilling;
    private Vector3 _surfaceBaseScale = Vector3.one;
    private Vector3 _streamBaseScale = Vector3.one;
    private Vector2 _streamBaseSize = Vector2.one;
    private bool _useRendererSize = false;
    private Vector3 _overlayBaseScale = Vector3.one;
    private float _liquidSurfaceLocalZ;
    private Dictionary<SpriteRenderer, int> _savedSortingOrders = new Dictionary<SpriteRenderer, int>();

    private const string CoFill = nameof(FillRoutine);
    private const string CoShimmer = nameof(Shimmer);
    private const string CoReveal = nameof(RevealStream);
    private const string CoFadeOverlayIn = nameof(FadeOverlayIn);
    private const string CoFadeOverlayOut = nameof(FadeOverlayOut);
    private const float MinSurfaceThickness = 0.001f;

    private void Awake()
    {
        // Auto-populate sprite renderers if list is empty
        if (mugSpriteRenderers.Count == 0)
        {
            mugSpriteRenderers.AddRange(GetComponentsInChildren<SpriteRenderer>(true));
        }
        
        // Save original sorting orders for all renderers
        foreach (var renderer in mugSpriteRenderers)
        {
            if (renderer != null)
                _savedSortingOrders[renderer] = renderer.sortingOrder;
        }

        _audio = GetComponent<AudioSource>();
        if (_audio == null) _audio = gameObject.AddComponent<AudioSource>();

        // In-cup renderer starts hidden and clipped by mask
        if (inCupRenderer != null)
        {
            _surfaceBaseScale = inCupRenderer.transform.localScale;
            inCupRenderer.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
            inCupRenderer.gameObject.SetActive(false); // start empty
        }

        // Stream pieces off by default
        if (streamGO != null) streamGO.SetActive(false);
        if (streamOverlayRenderer != null) streamOverlayRenderer.enabled = false;
        if (streamRenderer != null)
        {
            _streamBaseScale = streamRenderer.transform.localScale;
            _useRendererSize = streamRenderer.drawMode != SpriteDrawMode.Simple;
            if (_useRendererSize)
            {
                _streamBaseSize = streamRenderer.size;
                if (_streamBaseSize == Vector2.zero)
                {
                    _streamBaseSize = new Vector2(1f, 1f);
                }
            }
        }
        if (streamOverlayRenderer != null)
            _overlayBaseScale = streamOverlayRenderer.transform.localScale;

        // FillPivot guard: if not assigned but we have an inCupRenderer, assume its parent
        if (fillPivot == null && inCupRenderer != null)
        {
            fillPivot = inCupRenderer.transform.parent != null
                ? inCupRenderer.transform.parent
                : transform;
        }

        ResolveLiquidSurface();
        UpdateLiquidSurface();
    }

    /// <summary>
    /// Entry point called by UI buttons. Starts a pour using the given ingredient preset.
    /// </summary>
    public void FillWith(LiquidIngredient ingredient)
    {
        if (ingredient == null) return;
        if (_isFilling || inCupRenderer == null) return;

        // Raise sorting order for all mug sprites for clarity while filling
        foreach (var renderer in mugSpriteRenderers)
        {
            if (renderer != null)
                renderer.sortingOrder = raiseSortingOrderOnFill;
        }

        // Prepare visuals
        // In-cup surface
        inCupRenderer.gameObject.SetActive(true);
        if (ingredient.inCupSprite != null)
            inCupRenderer.sprite = ingredient.inCupSprite;
        inCupRenderer.color = ingredient.tint; // tint multiplies base sprite

        // Stream base
        if (streamRenderer != null)
        {
            if (ingredient.streamSprite != null)
                streamRenderer.sprite = ingredient.streamSprite;
            streamRenderer.color = ingredient.tint;
        }

        // Stream overlay (dual-layer shimmer)
        if (streamOverlayRenderer != null)
        {
            StopCoroutine(CoFadeOverlayIn);
            StopCoroutine(CoFadeOverlayOut);
            // Use the same sprite as base unless artist provided a separate one via prefab
            if (streamOverlayRenderer.sprite == null && streamRenderer != null)
                streamOverlayRenderer.sprite = streamRenderer.sprite;

            Color oc = ingredient.tint;
            oc.a = ingredient.overlayAlpha; // overlay alpha from SO
            if (overlayFadeInSeconds > 0f)
                oc.a = 0f;
            streamOverlayRenderer.color = oc;
            streamOverlayRenderer.enabled = ingredient.shimmerEnabled || ingredient.overlayAlpha > 0f;
        }

        // Reset or additive fill
        if (!ingredient.additive)
            currentFill = 0f;
        currentFill = Mathf.Clamp01(currentFill);

        // Start stream
        if (streamGO != null) streamGO.SetActive(true);
        if (ingredient.sfxStart != null) _audio.PlayOneShot(ingredient.sfxStart);

        // Prepare stream reveal
        if (streamRenderer != null)
        {
            streamRenderer.enabled = true;
            if (streamRevealSeconds > 0f)
            {
                if (_useRendererSize)
                {
                    streamRenderer.size = new Vector2(_streamBaseSize.x, 0f);
                }
                else
                {
                    var tr = streamRenderer.transform;
                    tr.localScale = new Vector3(_streamBaseScale.x, 0f, _streamBaseScale.z);
                }
                StopCoroutine(CoReveal);
                StartCoroutine(CoReveal, ingredient);
            }
            else
            {
                if (_useRendererSize)
                    streamRenderer.size = _streamBaseSize;
                else
                    streamRenderer.transform.localScale = _streamBaseScale;
            }
        }

        // Fade overlay in
        if (streamOverlayRenderer != null && overlayFadeInSeconds > 0f)
        {
            StopCoroutine(CoFadeOverlayIn);
            StartCoroutine(CoFadeOverlayIn, ingredient);
        }

        // Start shimmer (if enabled)
        if (ingredient.shimmerEnabled && streamOverlayRenderer != null)
        {
            StopCoroutine(CoShimmer);
            StartCoroutine(CoShimmer, ingredient);
        }

        // Begin fill routine
        StopCoroutine(CoFill);
        StartCoroutine(CoFill, ingredient);

        UpdateLiquidSurface();
    }

    private IEnumerator FillRoutine(LiquidIngredient ingredient)
    {
        _isFilling = true;

        // Let the stream visually "arrive" before the water rises
        if (riseDelaySeconds > 0f)
            yield return new WaitForSeconds(riseDelaySeconds);

        float start = currentFill;
        float end = Mathf.Clamp01(Mathf.Max(currentFill, ingredient.targetFill));
        float dur = Mathf.Max(0.01f, ingredient.fillSeconds);
        float t = 0f;

        var surfaceTr = inCupRenderer.transform;

        // Animate the in-cup surface scale Y from current -> target
        while (t < 1f)
        {
            t += Time.deltaTime / dur;
            // Smooth step for nicer ease-in/out
            float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            currentFill = Mathf.Lerp(start, end, u);

            surfaceTr.localScale = new Vector3(
                _surfaceBaseScale.x,
                _surfaceBaseScale.y * Mathf.Clamp01(currentFill),
                _surfaceBaseScale.z
            );

            UpdateLiquidSurface();

            yield return null;
        }

        // Clamp final
        currentFill = end;
        surfaceTr.localScale = new Vector3(
            _surfaceBaseScale.x,
            _surfaceBaseScale.y * currentFill,
            _surfaceBaseScale.z
        );

        UpdateLiquidSurface();

        // Tail the stream a hair longer for believability
        if (streamTailSeconds > 0f)
            yield return new WaitForSeconds(streamTailSeconds);

        // Stop shimmer and reset overlay transforms
        StopCoroutine(CoShimmer);
        if (streamOverlayRenderer != null)
        {
            streamOverlayRenderer.transform.localPosition = Vector3.zero;
            streamOverlayRenderer.transform.localScale = _overlayBaseScale;
            // keep enabled state; we'll hide with the stream GO below
        }

        StopCoroutine(CoFadeOverlayIn);
        if (streamOverlayRenderer != null && overlayFadeOutSeconds > 0f)
        {
            StopCoroutine(CoFadeOverlayOut);
            yield return StartCoroutine(CoFadeOverlayOut, ingredient);
        }

        // Turn off stream
        if (streamGO != null) streamGO.SetActive(false);
        if (ingredient.sfxStop != null) _audio.PlayOneShot(ingredient.sfxStop);

        // Restore sorting orders
        if (restoreSortingOrderAfterFill)
        {
            foreach (var kvp in _savedSortingOrders)
            {
                if (kvp.Key != null)
                    kvp.Key.sortingOrder = kvp.Value;
            }
        }

        _isFilling = false;
    }

    private IEnumerator Shimmer(LiquidIngredient ingredient)
    {
        if (streamOverlayRenderer == null)
            yield break;

        // Randomize phase so multiple mugs don't sync perfectly
        float t0 = Random.value * 10f;
        var tr = streamOverlayRenderer.transform;
        Vector3 basePos = tr.localPosition;
        Vector3 baseScale = tr.localScale;

        while (true)
        {
            float t = t0 + Time.time * ingredient.shimmerSpeed;

            // Gentle XY sway
            float dx = Mathf.Sin(t) * ingredient.swayXY.x;
            float dy = Mathf.Cos(t * 1.3f) * ingredient.swayXY.y;

            // Subtle vertical stretch
            float sy = 1f + Mathf.Sin(t * 0.7f) * ingredient.yStretch;

            tr.localPosition = basePos + new Vector3(dx, dy, 0f);
            tr.localScale = new Vector3(baseScale.x, baseScale.y * sy, baseScale.z);

            yield return null;
        }
    }

    private IEnumerator RevealStream(LiquidIngredient ingredient)
    {
        if (streamRenderer == null)
            yield break;

        float t = 0f;
        float dur = Mathf.Max(0.01f, streamRevealSeconds);
        while (t < 1f)
        {
            t += Time.deltaTime / dur;
            float u = streamRevealCurve != null ? streamRevealCurve.Evaluate(Mathf.Clamp01(t)) : Mathf.Clamp01(t);
            if (_useRendererSize)
            {
                float newY = Mathf.Lerp(0f, _streamBaseSize.y, u);
                streamRenderer.size = new Vector2(_streamBaseSize.x, newY);
            }
            else
            {
                float newY = Mathf.Lerp(0f, _streamBaseScale.y, u);
                var tr = streamRenderer.transform;
                tr.localScale = new Vector3(_streamBaseScale.x, newY, _streamBaseScale.z);
            }
            yield return null;
        }

        if (_useRendererSize)
            streamRenderer.size = _streamBaseSize;
        else
            streamRenderer.transform.localScale = _streamBaseScale;
    }

    private IEnumerator FadeOverlayIn(LiquidIngredient ingredient)
    {
        if (streamOverlayRenderer == null)
            yield break;

        float dur = Mathf.Max(0.01f, overlayFadeInSeconds);
        float t = 0f;
        Color c = streamOverlayRenderer.color;
        float startA = c.a;
        float endA = ingredient.overlayAlpha;
        while (t < 1f)
        {
            t += Time.deltaTime / dur;
            float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            c.a = Mathf.Lerp(startA, endA, u);
            streamOverlayRenderer.color = c;
            yield return null;
        }

        c.a = endA;
        streamOverlayRenderer.color = c;
    }

    private IEnumerator FadeOverlayOut(LiquidIngredient ingredient)
    {
        if (streamOverlayRenderer == null)
            yield break;

        float dur = Mathf.Max(0.01f, overlayFadeOutSeconds);
        float t = 0f;
        Color c = streamOverlayRenderer.color;
        float startA = c.a;
        const float endA = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / dur;
            float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            c.a = Mathf.Lerp(startA, endA, u);
            streamOverlayRenderer.color = c;
            yield return null;
        }

        c.a = endA;
        streamOverlayRenderer.color = c;
    }

    /// <summary>
    /// Resets the mug to empty (no animation). Handy for testing.
    /// </summary>
    [ContextMenu("Debug: Empty Mug")]
    public void EmptyToZero()
    {
        currentFill = 0f;
        if (inCupRenderer != null)
        {
            inCupRenderer.gameObject.SetActive(true); // keep visible for editing
            var s = inCupRenderer.transform.localScale;
            inCupRenderer.transform.localScale = new Vector3(_surfaceBaseScale.x, 0f, _surfaceBaseScale.z);
        }
        if (streamGO != null) streamGO.SetActive(false);
        StopAllCoroutines();
        _isFilling = false;
        if (restoreSortingOrderAfterFill)
        {
            foreach (var kvp in _savedSortingOrders)
            {
                if (kvp.Key != null)
                    kvp.Key.sortingOrder = kvp.Value;
            }
        }

        UpdateLiquidSurface();
    }

    void ResolveLiquidSurface()
    {
        if (liquidSurface == null && fillPivot != null)
        {
            var candidate = fillPivot.Find("LiquidSurface");
            if (candidate != null)
                liquidSurface = candidate;
        }

        if (liquidSurface != null && liquidSurfaceCollider == null)
            liquidSurfaceCollider = liquidSurface.GetComponent<BoxCollider2D>();

        if (liquidSurface != null)
            _liquidSurfaceLocalZ = liquidSurface.localPosition.z;
    }

    void UpdateLiquidSurface()
    {
        if (liquidSurface == null || inCupRenderer == null)
            return;

        var sprite = inCupRenderer.sprite;
        if (sprite == null)
            return;

        var surfaceTr = inCupRenderer.transform;
        Vector3 local = surfaceTr.localPosition;
        Vector3 scale = surfaceTr.localScale;

        var spriteBounds = sprite.bounds;
        var spriteCenter = spriteBounds.center;
        var spriteExtents = spriteBounds.extents;

        float localX = local.x + spriteCenter.x * scale.x;
        float localY = local.y + (spriteCenter.y + spriteExtents.y) * scale.y;

        if (liquidSurface.parent == fillPivot)
        {
            liquidSurface.localPosition = new Vector3(localX, localY, _liquidSurfaceLocalZ);
        }
        else if (fillPivot != null)
        {
            var targetWorld = fillPivot.TransformPoint(new Vector3(localX, localY, _liquidSurfaceLocalZ));
            liquidSurface.position = new Vector3(targetWorld.x, targetWorld.y, liquidSurface.position.z);
        }
        else
        {
            var targetWorld = surfaceTr.TransformPoint(new Vector3(spriteCenter.x, spriteCenter.y + spriteExtents.y, _liquidSurfaceLocalZ));
            liquidSurface.position = new Vector3(targetWorld.x, targetWorld.y, liquidSurface.position.z);
        }

        if (liquidSurfaceCollider != null)
        {
            float width = spriteBounds.size.x * Mathf.Abs(scale.x);
            float height = Mathf.Max(MinSurfaceThickness, liquidSurfaceThickness);

            var lossy = liquidSurface.lossyScale;
            if (Mathf.Abs(lossy.x) > Mathf.Epsilon)
                width /= Mathf.Abs(lossy.x);
            if (Mathf.Abs(lossy.y) > Mathf.Epsilon)
                height /= Mathf.Abs(lossy.y);

            liquidSurfaceCollider.size = new Vector2(width, height);
            liquidSurfaceCollider.offset = Vector2.zero;
            if (!liquidSurfaceCollider.isTrigger)
                liquidSurfaceCollider.isTrigger = true;
        }
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        ResolveLiquidSurface();
        UpdateLiquidSurface();
    }
#endif
}
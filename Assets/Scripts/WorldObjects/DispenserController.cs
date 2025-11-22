// DispenserController.cs (only the fill→water call changed to a single snap update)
using System.Collections;
using UnityEngine;
using UnityEngine.Events;

public enum WaterTemp { Cold, Hot }

public sealed class DispenserController : MonoBehaviour
{
    [Header("Socket detection (no SnapSocket API needed)")]
    [SerializeField] private Transform socketPoint;
    [SerializeField] private float socketSearchRadius = 0.25f;
    [SerializeField] private LayerMask mugMask = ~0;

    [Header("Timing")]
    [SerializeField] private float fillSeconds = 1.5f;
    [SerializeField] private AnimationCurve fillCurve = AnimationCurve.Linear(0, 0, 1, 1);

    [Header("Colors")]
    [SerializeField] private Color coldColor = new Color(0.43f, 0.77f, 1f);
    [SerializeField] private Color hotColor  = new Color(1f, 0.43f, 0.43f);

    [Header("Volume Add (Non-Reset)")]
    [Tooltip("How much volume to add per full fill (match MugBeverageState.maxVolume for a complete fill).")]
    [SerializeField] private float addVolumePerFill = 1f;

    [Header("Events")]
    public UnityEvent OnFillStarted;
    public UnityEvent<float> OnFillProgress;
    public UnityEvent OnFillCompleted;
    public UnityEvent OnFillCancelled;

    public bool IsBusy => _running != null;
    public WaterTemp LastFillTemp { get; private set; } = WaterTemp.Cold;

    Coroutine _running;

    public void TryFillCold() => TryFill(WaterTemp.Cold);
    public void TryFillHot()  => TryFill(WaterTemp.Hot);

    public void CancelFill()
    {
        if (_running != null)
        {
            StopCoroutine(_running);
            _running = null;
            if (FindMug(out var mugRoot, out _, out var streamRoot, out _, out _))
            {
                if (streamRoot) streamRoot.gameObject.SetActive(false);

                // Re-enable dragging when fill is cancelled
                var dragItem = mugRoot != null ? mugRoot.GetComponent<DragItem2D>() : null;
                if (dragItem != null)
                    dragItem.enabled = true;
            }
            OnFillCancelled?.Invoke();
        }
    }

    void TryFill(WaterTemp temp)
    {
        if (_running != null) return;
        if (!FindMug(out var mugRoot, out var liquidSR, out var streamRoot, out var streamSR, out var streamOverlaySR))
            return;

        LastFillTemp = temp;
        _running = StartCoroutine(FillRoutine(temp, mugRoot, liquidSR, streamRoot, streamSR, streamOverlaySR));
    }

    IEnumerator FillRoutine(WaterTemp temp, Transform mugRoot, SpriteRenderer liquidSR, Transform streamRoot, SpriteRenderer streamSR, SpriteRenderer streamOverlaySR)
    {
        var color = (temp == WaterTemp.Cold) ? coldColor : hotColor;

        var beverageState = mugRoot != null ? mugRoot.GetComponentInChildren<MugBeverageState>() : null;
        if (beverageState != null && beverageState.LiquidRenderer != null)
            liquidSR = beverageState.LiquidRenderer;

        // CRITICAL: Enable the liquid renderer GameObject at the start
        if (liquidSR != null)
        {
            liquidSR.gameObject.SetActive(true);
            liquidSR.enabled = true;

            // If beverage state exists and has existing ingredients (like milk), use that color for the fill animation
            if (beverageState != null && beverageState.HasMilk)
            {
                Color existingColor = beverageState.GetCurrentVisualColor();
                // Set alpha to 1 for the fill animation (even if it was 0 before water was added)
                existingColor.a = 1f;
                liquidSR.color = existingColor;
            }
            else
            {
                liquidSR.color = color;
            }
        }

        // Turn on stream + tint visuals
        if (streamRoot) streamRoot.gameObject.SetActive(true);
        if (streamSR) streamSR.color = color;
        if (streamOverlaySR) streamOverlaySR.color = color;

        OnFillStarted?.Invoke();

        // Disable dragging during fill to prevent player from pulling mug away
        var dragItem = mugRoot != null ? mugRoot.GetComponent<DragItem2D>() : null;
        if (dragItem != null)
            dragItem.enabled = false;

        // Animate fill height
        var fillPivot = mugRoot.Find("Visuals/FillPivot");
        float t = 0f;
        float dur = Mathf.Max(0.01f, fillSeconds);

        if (fillPivot)
        {
            var s = fillPivot.localScale;
            s.y = 0f;
            fillPivot.localScale = s;
        }

        while (t < dur)
        {
            t += Time.deltaTime;
            float raw = Mathf.Clamp01(t / dur);
            float curved = fillCurve != null ? Mathf.Clamp01(fillCurve.Evaluate(raw)) : raw;

            if (fillPivot)
            {
                var s = fillPivot.localScale;
                s.y = curved;
                fillPivot.localScale = s;
            }

            OnFillProgress?.Invoke(curved);
            yield return null;
        }

        if (fillPivot)
        {
            var s = fillPivot.localScale;
            s.y = 1f;
            fillPivot.localScale = s;
        }
        
        // NOW register the water with volume (after animation completes)
        if (beverageState != null && addVolumePerFill > 0f)
        {
            beverageState.RegisterWaterNonReset(temp, color, addVolumePerFill);
        }
        
        if (streamRoot) streamRoot.gameObject.SetActive(false);

        var cupState = mugRoot.GetComponentInChildren<CupState>();
        if (cupState != null) cupState.SetFillAmount(1f);

        // Re-enable dragging after fill completes
        if (dragItem != null)
            dragItem.enabled = true;

        OnFillCompleted?.Invoke();
        _running = null;
    }

    bool FindMug(out Transform mugRoot, out SpriteRenderer liquidSR, out Transform streamRoot, out SpriteRenderer streamSR, out SpriteRenderer streamOverlaySR)
    {
        mugRoot = null; liquidSR = null; streamRoot = null; streamSR = null; streamOverlaySR = null;
        if (socketPoint == null) return false;

        var hits = Physics2D.OverlapCircleAll(socketPoint.position, socketSearchRadius, mugMask);
        float bestDist = float.PositiveInfinity;
        Collider2D best = null;

        foreach (var h in hits)
        {
            if (h == null) continue;
            var tr = h.transform;
            bool looksLikeCup = (tr.CompareTag("Cup") || tr.GetComponentInParent<CupState>() != null);
            if (!looksLikeCup) continue;

            float d = Vector2.SqrMagnitude((Vector2)tr.position - (Vector2)socketPoint.position);
            if (d < bestDist) { bestDist = d; best = h; }
        }

        if (best == null) return false;

        mugRoot = GetMugRoot(best.transform);
        if (mugRoot == null) return false;

        var liquidTr = mugRoot.Find("Visuals/FillPivot/LiquidInCup");
        if (liquidTr) liquidSR = liquidTr.GetComponent<SpriteRenderer>();

        streamRoot = mugRoot.Find("Stream");
        if (streamRoot)
        {
            var s = streamRoot.Find("SR_Stream");
            if (s) streamSR = s.GetComponent<SpriteRenderer>();
            var so = streamRoot.Find("SR_StreamOverlay");
            if (so) streamOverlaySR = so.GetComponent<SpriteRenderer>();
        }

        return true;
    }

    Transform GetMugRoot(Transform t)
    {
        var cur = t;
        while (cur != null)
        {
            if (cur.GetComponent<UnityEngine.Rendering.SortingGroup>() != null) return cur;
            if (cur.GetComponent<MugController>() != null) return cur;
            if (cur.GetComponent<CupState>() != null) return cur;
            cur = cur.parent;
        }
        return t;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (socketPoint == null) return;
        Gizmos.color = new Color(0, 0.7f, 1f, 0.25f);
        Gizmos.DrawWireSphere(socketPoint.position, socketSearchRadius);
    }
#endif
}
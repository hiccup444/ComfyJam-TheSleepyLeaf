using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Fades in/out a HoverCanvas CanvasGroup when hovering over a TeaPacket.
/// Automatically hides when the packet is being dragged or is ripped open.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class TeaPacketHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("References")]
    [Tooltip("The CanvasGroup to fade (should be on HoverCanvas child)")]
    [SerializeField] private CanvasGroup hoverCanvasGroup;

    [Header("Fade Settings")]
    [Tooltip("How fast the canvas fades in/out")]
    [SerializeField] private float fadeSpeed = 8f;

    [Tooltip("Target alpha when hovering (fully visible)")]
    [SerializeField] private float visibleAlpha = 1f;

    [Tooltip("Target alpha when not hovering (invisible)")]
    [SerializeField] private float hiddenAlpha = 0f;

    [Tooltip("Delay after dragging stops before hover text can show")]
    [SerializeField] private float postDragDelay = 2f;

    // Component references
    private TeaPacket teaPacket;
    private DragItem2D dragItem;

    // State
    private bool isHovering = false;
    private float targetAlpha = 0f;
    private bool wasDragging = false;
    private float dragEndTime = 0f;
    private bool hasBeenInteractedWith = false;

    void Awake()
    {
        // Find the TeaPacket component (should be on parent or this object)
        teaPacket = GetComponentInParent<TeaPacket>();
        if (teaPacket == null)
            teaPacket = GetComponent<TeaPacket>();

        // Find the DragItem2D component (usually on this object)
        dragItem = GetComponent<DragItem2D>();
        if (dragItem == null)
            dragItem = GetComponentInParent<DragItem2D>();

        // Auto-find HoverCanvas if not assigned (should be a child of teaPacketMain)
        if (hoverCanvasGroup == null)
        {
            Transform hoverCanvas = transform.Find("HoverCanvas");
            if (hoverCanvas != null)
            {
                hoverCanvasGroup = hoverCanvas.GetComponent<CanvasGroup>();
                if (hoverCanvasGroup == null)
                {
#if UNITY_EDITOR
                    Debug.LogError($"TeaPacketHover: Found HoverCanvas but it has no CanvasGroup component!");
#endif
                }
            }
            else
            {
#if UNITY_EDITOR
                Debug.LogError($"TeaPacketHover: Could not find 'HoverCanvas' child on {gameObject.name}!");
#endif
            }
        }

        // Ensure proper scale for HoverCanvas and its children
        if (hoverCanvasGroup != null)
        {
            hoverCanvasGroup.transform.localScale = Vector3.one;

            // Also set HoverText to scale (1,1,1) if it exists
            Transform hoverText = hoverCanvasGroup.transform.Find("HoverText");
            if (hoverText != null)
            {
                hoverText.localScale = Vector3.one;
            }
        }

        // Start hidden
        if (hoverCanvasGroup != null)
        {
            hoverCanvasGroup.alpha = hiddenAlpha;
            hoverCanvasGroup.interactable = false;
            hoverCanvasGroup.blocksRaycasts = false;
        }

        targetAlpha = hiddenAlpha;
    }

    void Start()
    {
        // Check parent name since this script is on teaPacketMain
        string parentName = transform.parent != null ? transform.parent.name : "NO_PARENT";
#if UNITY_EDITOR
        Debug.Log($"TeaPacketHover Start() called on: {gameObject.name}, parent: {parentName}");
#endif

        // Special positioning for ginger packet - do this in Start() to ensure UI is ready
        if (parentName.Contains("ginger"))
        {
#if UNITY_EDITOR
            Debug.Log($"Found ginger packet! hoverCanvasGroup null? {hoverCanvasGroup == null}");
#endif

            if (hoverCanvasGroup != null)
            {
                Transform hoverText = hoverCanvasGroup.transform.Find("HoverText");
#if UNITY_EDITOR
                Debug.Log($"HoverText found? {hoverText != null}");
#endif

                if (hoverText != null)
                {
                    RectTransform rectTransform = hoverText.GetComponent<RectTransform>();
#if UNITY_EDITOR
                    Debug.Log($"RectTransform found? {rectTransform != null}");
#endif

                    if (rectTransform != null)
                    {
                        rectTransform.anchoredPosition = new Vector2(-1.30f, -21f);
#if UNITY_EDITOR
                        Debug.Log($"Set ginger packet HoverText position to {rectTransform.anchoredPosition}");
#endif
                    }
                }
            }
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        isHovering = true;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isHovering = false;
    }

    void Update()
    {
        if (hoverCanvasGroup == null) return;

        // Track when dragging ends
        bool currentlyDragging = IsDragging();
        if (wasDragging && !currentlyDragging)
        {
            // Just stopped dragging
            dragEndTime = Time.time;
        }
        wasDragging = currentlyDragging;

        // First-time interaction handling for ginger packet
        if (!hasBeenInteractedWith && (isHovering || currentlyDragging))
        {
            hasBeenInteractedWith = true;

            string parentName = transform.parent != null ? transform.parent.name : "";
            if (parentName.Contains("ginger"))
            {
                Transform hoverText = hoverCanvasGroup.transform.Find("HoverText");
                if (hoverText != null)
                {
                    RectTransform rectTransform = hoverText.GetComponent<RectTransform>();
                    if (rectTransform != null)
                    {
                        rectTransform.anchoredPosition = new Vector2(-1.30f, -21f);
                    }
                }
            }
        }

        // Check if enough time has passed since dragging stopped
        bool canShowAfterDrag = !currentlyDragging && (Time.time - dragEndTime >= postDragDelay);

        // Determine if we should show the hover canvas
        bool shouldShow = isHovering && canShowAfterDrag && !IsRipped();

        // Update target alpha
        targetAlpha = shouldShow ? visibleAlpha : hiddenAlpha;

        // Smoothly fade to target
        if (Mathf.Abs(hoverCanvasGroup.alpha - targetAlpha) > 0.01f)
        {
            hoverCanvasGroup.alpha = Mathf.Lerp(hoverCanvasGroup.alpha, targetAlpha, fadeSpeed * Time.deltaTime);
        }
        else
        {
            hoverCanvasGroup.alpha = targetAlpha;
        }

        // Update interactivity (only when visible)
        hoverCanvasGroup.interactable = hoverCanvasGroup.alpha > 0.5f;
    }

    /// <summary>
    /// Returns true if the packet is currently being dragged
    /// </summary>
    private bool IsDragging()
    {
        if (dragItem == null) return false;
        return dragItem.IsDragging;
    }

    /// <summary>
    /// Returns true if the packet has been ripped open
    /// </summary>
    private bool IsRipped()
    {
        if (teaPacket == null) return false;
        return teaPacket.IsRipped;
    }

    /// <summary>
    /// Force hide the hover canvas (useful for external scripts)
    /// </summary>
    public void ForceHide()
    {
        isHovering = false;
        targetAlpha = hiddenAlpha;
        if (hoverCanvasGroup != null)
        {
            hoverCanvasGroup.alpha = hiddenAlpha;
        }
    }

    /// <summary>
    /// Force show the hover canvas (useful for external scripts)
    /// </summary>
    public void ForceShow()
    {
        if (!IsRipped() && !IsDragging())
        {
            isHovering = true;
            targetAlpha = visibleAlpha;
        }
    }
}

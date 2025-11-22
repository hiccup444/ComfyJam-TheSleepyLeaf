using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;
using System.Collections;

public class OrderTitleHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("References")]
    [SerializeField] private TextMeshProUGUI orderTitleText;
    [SerializeField] private SpriteRenderer orderSprite;
    [SerializeField] private SpriteRenderer orderOutline;
    
    [Header("Fade Settings")]
    [SerializeField] private float fadeInDuration = 1f;
    [SerializeField] private float fadeOutDuration = 1f;
    [SerializeField] private float titleDisplayDuration = 2f;
    
    [Header("Behaviour")]
    [SerializeField] private bool autoAddPhysics2DRaycaster = true;
    [SerializeField] private bool debugLogs = false;
    
    private bool isHovering = false;
    private bool hasShownInitially = false;
    private bool pendingReveal = false;
    private Coroutine fadeCoroutine;
    private CanvasGroup titleCanvasGroup;
    
    private void Awake()
    {
        // Ensure TMP title doesn't block pointer events (let sprite receive hover)
        if (orderTitleText != null)
        {
            orderTitleText.raycastTarget = false;
        }

        // Get or add CanvasGroup for title text alpha control
        if (orderTitleText != null)
        {
            titleCanvasGroup = orderTitleText.GetComponent<CanvasGroup>();
            if (titleCanvasGroup == null)
            {
                titleCanvasGroup = orderTitleText.gameObject.AddComponent<CanvasGroup>();
            }
            titleCanvasGroup.alpha = 0f;
        }
        
        // Start all sprites at 0 alpha
        if (orderSprite != null)
        {
            Color c = orderSprite.color;
            c.a = 0f;
            orderSprite.color = c;
        }
        
        if (orderOutline != null)
        {
            Color c = orderOutline.color;
            c.a = 0f;
            orderOutline.color = c;
        }
    }
    
    private void Start()
    {
        // Check for collider - required for pointer events!
        Collider2D col = GetComponent<Collider2D>();
        if (col == null)
        {
#if UNITY_EDITOR
            Debug.LogWarning($"[OrderTitleHover] No Collider2D on {gameObject.name}. Adding BoxCollider2D for hover detection.");
#endif
            BoxCollider2D box = gameObject.AddComponent<BoxCollider2D>();
            
            // Try to size it based on the sprite if available
            if (orderSprite != null)
            {
                box.size = orderSprite.bounds.size;
                box.offset = orderSprite.bounds.center - transform.position;
            }
            else
            {
                // Default size
                box.size = new Vector2(1f, 1f);
            }
        }

        // Ensure a Physics2DRaycaster exists so IPointerEnter/Exit works on 2D sprites
        // If missing, optionally add one to the main camera
        if (autoAddPhysics2DRaycaster)
        {
            var cam = Camera.main;
            if (cam != null && cam.GetComponent<Physics2DRaycaster>() == null)
            {
#if UNITY_EDITOR
                if (debugLogs) Debug.Log("[OrderTitleHover] Adding Physics2DRaycaster to main Camera for hover detection.", cam);
#endif
                cam.gameObject.AddComponent<Physics2DRaycaster>();
            }
        }

        // Sanity: warn if there's no EventSystem (pointer events won't fire)
        if (EventSystem.current == null && debugLogs)
        {
#if UNITY_EDITOR
            Debug.LogWarning("[OrderTitleHover] No EventSystem found in scene. Pointer hover events require an EventSystem.");
#endif
        }
    }
    
    private void OnEnable()
    {
        // If reveal was requested while disabled, trigger it now
        if (pendingReveal && !hasShownInitially)
        {
            pendingReveal = false;
            hasShownInitially = true;
            StartCoroutine(InitialRevealSequence());
        }
    }
    
    public void SetOrderDisplayName(string displayName)
    {
        if (orderTitleText != null)
        {
            orderTitleText.text = displayName;
        }
        
        // Queue reveal sequence for when GameObject is enabled
        if (!hasShownInitially)
        {
            if (gameObject.activeInHierarchy)
            {
                hasShownInitially = true;
                StartCoroutine(InitialRevealSequence());
            }
            else
            {
                // Will trigger in OnEnable
                pendingReveal = true;
            }
        }
    }
    
    private IEnumerator InitialRevealSequence()
    {
        // Fade in all three elements over fadeInDuration
        float elapsed = 0f;
        
        while (elapsed < fadeInDuration)
        {
            elapsed += Time.deltaTime;
            float alpha = Mathf.Clamp01(elapsed / fadeInDuration);
            
            // Fade sprites
            if (orderSprite != null)
            {
                Color c = orderSprite.color;
                c.a = alpha;
                orderSprite.color = c;
            }
            
            if (orderOutline != null)
            {
                Color c = orderOutline.color;
                c.a = alpha;
                orderOutline.color = c;
            }
            
            // Fade title text
            if (titleCanvasGroup != null)
            {
                titleCanvasGroup.alpha = alpha;
            }
            
            yield return null;
        }
        
        // Ensure full alpha
        if (orderSprite != null)
        {
            Color c = orderSprite.color;
            c.a = 1f;
            orderSprite.color = c;
        }
        
        if (orderOutline != null)
        {
            Color c = orderOutline.color;
            c.a = 1f;
            orderOutline.color = c;
        }
        
        if (titleCanvasGroup != null)
        {
            titleCanvasGroup.alpha = 1f;
        }
        
        // Wait for titleDisplayDuration
        yield return new WaitForSeconds(titleDisplayDuration);
        
        // Fade out title only (keep sprites visible)
        if (!isHovering && titleCanvasGroup != null)
        {
            yield return FadeTitleTo(0f);
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        isHovering = true;
#if UNITY_EDITOR
        if (debugLogs) Debug.Log($"[OrderTitleHover] Pointer ENTER on {gameObject.name}");
#endif

        if (fadeCoroutine != null)
        {
            StopCoroutine(fadeCoroutine);
        }
        
        fadeCoroutine = StartCoroutine(FadeTitleTo(1f));
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isHovering = false;
#if UNITY_EDITOR
        if (debugLogs) Debug.Log($"[OrderTitleHover] Pointer EXIT on {gameObject.name}");
#endif

        if (fadeCoroutine != null)
        {
            StopCoroutine(fadeCoroutine);
        }
        
        fadeCoroutine = StartCoroutine(FadeTitleTo(0f));
    }

    // Fallback for cases where an EventSystem/Physics2DRaycaster isn't present
    private void OnMouseEnter()
    {
        isHovering = true;
#if UNITY_EDITOR
        if (debugLogs) Debug.Log($"[OrderTitleHover] Mouse ENTER (fallback) on {gameObject.name}");
#endif
        if (fadeCoroutine != null) StopCoroutine(fadeCoroutine);
        fadeCoroutine = StartCoroutine(FadeTitleTo(1f));
    }

    private void OnMouseExit()
    {
        isHovering = false;
#if UNITY_EDITOR
        if (debugLogs) Debug.Log($"[OrderTitleHover] Mouse EXIT (fallback) on {gameObject.name}");
#endif
        if (fadeCoroutine != null) StopCoroutine(fadeCoroutine);
        fadeCoroutine = StartCoroutine(FadeTitleTo(0f));
    }
    
    private IEnumerator FadeTitleTo(float targetAlpha)
    {
        if (titleCanvasGroup == null) yield break;
        
        float startAlpha = titleCanvasGroup.alpha;
        float duration = targetAlpha > startAlpha ? fadeInDuration : fadeOutDuration;
        float elapsed = 0f;
        
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            titleCanvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, elapsed / duration);
            yield return null;
        }
        
        titleCanvasGroup.alpha = targetAlpha;
    }
}

using UnityEngine;
using JamesKJamKit.Services;
using JamesKJamKit.Services.Audio;

[RequireComponent(typeof(SpriteRenderer))]
[RequireComponent(typeof(Collider2D))]
public class OpenCloseSign : MonoBehaviour
{
    [Header("Sprites")]
    [Tooltip("Sprite shown when shop is open")]
    [SerializeField] private Sprite openSprite;
    
    [Tooltip("Sprite shown when shop is closed")]
    [SerializeField] private Sprite closedSprite;
    
    [Header("Highlight")]
    [Tooltip("GameObject with SpriteRenderer to pulse when player should open shop")]
    [SerializeField] private GameObject signHighlight;
    
    [Tooltip("Alpha change per second (0.5 = slow 2s fade, 1.0 = 1s fade, 2.0 = fast 0.5s fade)")]
    [SerializeField] private float highlightFadeSpeed = 1f;
    
    [Tooltip("Minimum alpha during pulse (0 = fully transparent)")]
    [SerializeField] [Range(0f, 1f)] private float minPulseAlpha = 0f;
    
    [Tooltip("Maximum alpha during pulse (1 = fully opaque)")]
    [SerializeField] [Range(0f, 1f)] private float maxPulseAlpha = 1f;

    [Header("SFX")]
    [Tooltip("Event played when the sign flips to open the shop")]
    [SerializeField] private SFXEvent sfxOpenEvent;

    [Tooltip("Event played when the sign flips to close the shop")]
    [SerializeField] private SFXEvent sfxCloseEvent;
    
    private SpriteRenderer spriteRenderer;
    private SpriteRenderer highlightSpriteRenderer;
    private bool isShopOpen = false;
    private bool subscribedToEvents = false;
    private bool isHighlightPulsing = false;
    private float highlightAlpha = 0f;
    private bool highlightFadingIn = true;
    private bool hasPlayedFirstOpenSound = false;
    
    void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();

#if UNITY_EDITOR
        Debug.Log($"[OpenCloseSign] Awake - highlightFadeSpeed={highlightFadeSpeed}");
#endif

        // Get highlight sprite renderer if assigned
        if (signHighlight != null)
        {
            highlightSpriteRenderer = signHighlight.GetComponent<SpriteRenderer>();
            if (highlightSpriteRenderer == null)
            {
#if UNITY_EDITOR
                Debug.LogWarning("[OpenCloseSign] signHighlight has no SpriteRenderer component!");
#endif
            }
            // Start with highlight disabled
            signHighlight.SetActive(false);
        }
    }
    
    void Start()
    {
        // Sync after GameManager has initialized
        SubscribeToGameManager();
        SyncFromGameManager();
    }
    
    void OnEnable()
    {
        SubscribeToGameManager();
        SyncFromGameManager();
    }
    
    void OnDisable()
    {
        UnsubscribeFromGameManager();
    }

    void OnMouseDown()
    {
        var gameManager = GameManager.Instance;

        if (gameManager == null)
        {
#if UNITY_EDITOR
            Debug.LogError("GameManager not found! Cannot toggle shop.");
#endif
            return;
        }

        // Block opening/closing during tutorial
        if (TutorialManager.Instance != null && TutorialManager.Instance.IsTutorialActive())
        {
#if UNITY_EDITOR
            Debug.Log("[OpenCloseSign] Shop toggle disabled during tutorial.");
#endif
            return;
        }

        SubscribeToGameManager();

        if (gameManager.IsDayComplete())
        {
#if UNITY_EDITOR
            Debug.LogWarning("Cannot open/close shop - day is over!");
#endif
            return;
        }

        // First flip should start the game before opening the shop.
        if (!gameManager.HasGameStarted())
        {
            gameManager.StartGame();
        }

        ToggleShop(gameManager);
    }
    
    void ToggleShop(GameManager gameManager)
    {
        if (gameManager.IsShopOpen())
        {
            gameManager.CloseShop();
        }
        else
        {
            gameManager.OpenShop();
        }
    }

    void SubscribeToGameManager()
    {
        if (subscribedToEvents) return;

        var gameManager = GameManager.Instance;
        if (gameManager == null) return;

        gameManager.OnShopOpened += HandleShopOpened;
        gameManager.OnShopClosed += HandleShopClosed;
        gameManager.OnDayStarted += HandleDayStarted;
        subscribedToEvents = true;
    }

    void UnsubscribeFromGameManager()
    {
        if (!subscribedToEvents) return;

        var gameManager = GameManager.Instance;
        if (gameManager != null)
        {
            gameManager.OnShopOpened -= HandleShopOpened;
            gameManager.OnShopClosed -= HandleShopClosed;
            gameManager.OnDayStarted -= HandleDayStarted;
        }

        subscribedToEvents = false;
    }

    void SyncFromGameManager()
    {
        var gameManager = GameManager.Instance;
        if (gameManager != null)
        {
            isShopOpen = gameManager.IsShopOpen();
        }
        else
        {
            isShopOpen = false;
        }

        UpdateSprite();
    }

    void HandleShopOpened()
    {
        isShopOpen = true;
        UpdateSprite();

        // Don't play sound on first open (during/after cutscene)
        if (hasPlayedFirstOpenSound)
        {
            PlaySfx(sfxOpenEvent);
        }
        else
        {
            hasPlayedFirstOpenSound = true;
#if UNITY_EDITOR
            Debug.Log("[OpenCloseSign] First shop open - skipping audio");
#endif
        }

        StopHighlightPulse();
    }

    void HandleShopClosed()
    {
        isShopOpen = false;
        UpdateSprite();
        PlaySfx(sfxCloseEvent);
    }
    
    void HandleDayStarted()
    {
        var gameManager = GameManager.Instance;
        if (gameManager == null) return;
        
        // Only show highlight on day 2 and beyond (when tutorial is complete)
        if (gameManager.GetCurrentDay() >= 2 && !isShopOpen)
        {
            StartHighlightPulse();
        }
    }
    
    void Update()
    {
        if (isHighlightPulsing)
        {
            UpdateHighlightPulse();
        }
    }
    
    void StartHighlightPulse()
    {
        if (signHighlight == null || highlightSpriteRenderer == null) return;
        
        signHighlight.SetActive(true);
        isHighlightPulsing = true;
        highlightAlpha = minPulseAlpha;
        highlightFadingIn = true;

#if UNITY_EDITOR
        Debug.Log($"[OpenCloseSign] Started highlight pulse - highlightFadeSpeed={highlightFadeSpeed}");
#endif
    }
    
    void StopHighlightPulse()
    {
        if (signHighlight == null) return;
        
        isHighlightPulsing = false;
        signHighlight.SetActive(false);

#if UNITY_EDITOR
        Debug.Log("[OpenCloseSign] Stopped highlight pulse - shop opened");
#endif
    }
    
    void UpdateHighlightPulse()
    {
        if (highlightSpriteRenderer == null) return;
        
        // Use speed directly for linear, predictable control
        float actualSpeed = highlightFadeSpeed;
        float deltaChange = Time.deltaTime * actualSpeed;
        
        // Fade in and out between min and max alpha values
        if (highlightFadingIn)
        {
            highlightAlpha += deltaChange;
            if (highlightAlpha >= maxPulseAlpha)
            {
                highlightAlpha = maxPulseAlpha;
                highlightFadingIn = false;
            }
        }
        else
        {
            highlightAlpha -= deltaChange;
            if (highlightAlpha <= minPulseAlpha)
            {
                highlightAlpha = minPulseAlpha;
                highlightFadingIn = true;
            }
        }
        
        // Apply alpha to sprite
        Color color = highlightSpriteRenderer.color;
        color.a = highlightAlpha;
        highlightSpriteRenderer.color = color;
        
        // Debug logging every 60 frames (~1 second)
        if (Time.frameCount % 60 == 0)
        {
#if UNITY_EDITOR
            Color actualColor = highlightSpriteRenderer.color;
            Debug.Log($"[OpenCloseSign] SET alpha={highlightAlpha:F4}, ACTUAL renderer alpha={actualColor.a:F4}, speed={actualSpeed}, inspector speed={highlightFadeSpeed}");
#endif
        }
    }
    
    void UpdateSprite()
    {
        if (spriteRenderer == null) return;
        
        if (isShopOpen)
        {
            if (openSprite != null)
            {
                spriteRenderer.sprite = openSprite;
            }
        }
        else
        {
            if (closedSprite != null)
            {
                spriteRenderer.sprite = closedSprite;
            }
        }
    }
    
    private void PlaySfx(SFXEvent evt)
    {
        if (evt == null) return;
        AudioManager.Instance?.PlaySFX(evt, transform);
    }

    public void ForceClose()
    {
        var gameManager = GameManager.Instance;
        if (gameManager != null)
        {
            gameManager.CloseShop();
        }
        else
        {
            isShopOpen = false;
            UpdateSprite();
        }
    }
    
    public bool IsShopOpen() => isShopOpen;
}

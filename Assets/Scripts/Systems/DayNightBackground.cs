using UnityEngine;

public class DayNightBackground : MonoBehaviour
{
    [SerializeField] SpriteRenderer backgroundSprite;
    
    [Header("Transition Settings")]
    [Tooltip("At what point in the day does night start (0-1)")]
    [SerializeField] float nightStartTime = 0.7f;
    
    [Tooltip("How long the transition takes (as percentage of day)")]
    [SerializeField] float transitionDuration = 0.15f;
    
    [Header("Sway Settings")]
    [Tooltip("Enable horizontal swaying animation")]
    [SerializeField] bool enableSway = true;

    [Tooltip("Maximum scale change (0.02 = sways from 0.98 to 1.02)")]
    public float swayAmount = 0.02f;
    
    [Tooltip("How long one complete sway cycle takes (in seconds)")]
    [SerializeField] float swayCycleDuration = 8f;
    
    [Header("Wind Calm Settings")]
    [Tooltip("Enable random pauses as if wind has calmed")]
    [SerializeField] bool enableWindCalms = true;
    
    [Tooltip("Minimum time between wind calms (seconds)")]
    [SerializeField] float minTimeBetweenCalms = 15f;
    
    [Tooltip("Maximum time between wind calms (seconds)")]
    [SerializeField] float maxTimeBetweenCalms = 45f;
    
    [Tooltip("Minimum duration of a wind calm (seconds)")]
    [SerializeField] float minCalmDuration = 2f;
    
    [Tooltip("Maximum duration of a wind calm (seconds)")]
    [SerializeField] float maxCalmDuration = 6f;
    
    private Material backgroundMaterial;
    private Vector3 originalScale;
    private float swayTime = 0f;
    private bool isCalm = false;
    private float calmTimer = 0f;
    private float nextCalmTime = 0f;
    
    void Awake()
    {
        // Store original scale
        originalScale = transform.localScale;
        
        if (backgroundSprite != null)
        {
            // Create instance of material so we don't modify the shared one
            backgroundMaterial = new Material(backgroundSprite.material);
            backgroundSprite.material = backgroundMaterial;
        }
    }
    
    void Start()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnDayTimeChanged += UpdateTransition;
            // Set initial state
            UpdateTransition(GameManager.Instance.GetNormalizedDayTime());
        }
        
        // Schedule first wind calm
        if (enableWindCalms)
        {
            nextCalmTime = Random.Range(minTimeBetweenCalms, maxTimeBetweenCalms);
        }
    }
    
    void OnDisable()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnDayTimeChanged -= UpdateTransition;
        }
    }
    
    void Update()
    {
        if (enableSway)
        {
            // Handle wind calm timing
            if (enableWindCalms)
            {
                HandleWindCalm();
            }
            
            // Only update sway if not in a calm period
            if (!isCalm)
            {
                UpdateSway();
            }
        }
    }
    
    void HandleWindCalm()
    {
        if (isCalm)
        {
            // We're in a calm period - count down
            calmTimer -= Time.deltaTime;
            
            if (calmTimer <= 0f)
            {
                // Calm period over - resume swaying
                isCalm = false;
                // Schedule next calm
                nextCalmTime = Random.Range(minTimeBetweenCalms, maxTimeBetweenCalms);
            }
        }
        else
        {
            // Normal swaying - count down to next calm
            nextCalmTime -= Time.deltaTime;
            
            if (nextCalmTime <= 0f)
            {
                // Time to start a calm period
                // Only start calm when we're near scale 1.0 for smoothness
                float currentScaleOffset = transform.localScale.x - originalScale.x;
                
                if (Mathf.Abs(currentScaleOffset) < 0.005f)
                {
                    // We're close to neutral - start the calm
                    isCalm = true;
                    calmTimer = Random.Range(minCalmDuration, maxCalmDuration);
                    
                    // Snap to original scale
                    Vector3 calmScale = originalScale;
                    transform.localScale = calmScale;
                }
                else
                {
                    // Not at neutral yet - wait a bit longer
                    nextCalmTime = 0.5f;
                }
            }
        }
    }
    
    void UpdateSway()
    {
        // Increment time
        swayTime += Time.deltaTime;
        
        // Use sine wave for smooth back-and-forth motion
        // This goes from -1 to +1 in a smooth wave
        float sineWave = Mathf.Sin((swayTime / swayCycleDuration) * 2f * Mathf.PI);
        
        // Map sine wave to scale change
        float scaleOffset = sineWave * swayAmount;
        
        // Apply scale - enforce original scale when crossing zero
        Vector3 newScale = originalScale;
        
        // When we're very close to the original scale (within 0.001), snap to it
        // This prevents drift over time
        if (Mathf.Abs(scaleOffset) < 0.001f)
        {
            newScale.x = originalScale.x;
            // Reset time to prevent float precision issues over long periods
            if (swayTime >= swayCycleDuration)
            {
                swayTime = 0f;
            }
        }
        else
        {
            newScale.x = originalScale.x + scaleOffset;
        }
        
        transform.localScale = newScale;
    }
    
    void UpdateTransition(float normalizedTime)
    {
        if (backgroundMaterial == null)
        {
            return;
        }
        
        // Calculate transition value (0 = full day, 1 = full night)
        float transitionValue = 0f;
        
        if (normalizedTime >= nightStartTime)
        {
            // Map from nightStartTime to nightStartTime + transitionDuration
            float transitionProgress = (normalizedTime - nightStartTime) / transitionDuration;
            transitionValue = Mathf.Clamp01(transitionProgress);
        }
        backgroundMaterial.SetFloat("_Transition", transitionValue);
    }
    
    void OnDestroy()
    {
        // Clean up instanced material
        if (backgroundMaterial != null)
        {
            Destroy(backgroundMaterial);
        }
    }
}
using UnityEngine;
using System;
using System.Collections.Generic;

// simple serializable dictionary for Unity inspector
[System.Serializable]
public class SerializableDictionary<TKey, TValue>
{
    [SerializeField] private List<TKey> keys = new List<TKey>();
    [SerializeField] private List<TValue> values = new List<TValue>();

    private Dictionary<TKey, TValue> dictionary;
    private bool isInitialized = false;

    public TValue this[TKey key]
    {   
        get
        {
            EnsureInitialized();
            return dictionary[key];
        }
        set
        {
            EnsureInitialized();
            dictionary[key] = value;
            SyncToLists();
        }
    }

    public bool ContainsKey(TKey key)
    {
        EnsureInitialized();
        return dictionary.ContainsKey(key);
    }

    // Only rebuild dictionary once on first access
    private void EnsureInitialized()
    {
        if (isInitialized) return;

        dictionary = new Dictionary<TKey, TValue>(keys.Count);
        for (int i = 0; i < keys.Count && i < values.Count; i++)
        {
            if (!dictionary.ContainsKey(keys[i]))
            {
                dictionary[keys[i]] = values[i];
            }
        }
        isInitialized = true;
    }

    // Only called when dictionary is modified at runtime
    private void SyncToLists()
    {
        keys.Clear();
        values.Clear();

        foreach (var kvp in dictionary)
        {
            keys.Add(kvp.Key);
            values.Add(kvp.Value);
        }
    }
}

public class GameManager : MonoBehaviour
{
    // Singleton pattern
    public static GameManager Instance { get; private set; }

    [Header("Shop State")]
    [SerializeField] bool shopOpen = false;
    
    [Header("Day/Night Cycle")]
    [Tooltip("How long a full day lasts in real-time seconds")]
    [SerializeField] float dayDurationSeconds = 900f; // 15 minutes

    [Tooltip("What percentage of the day is 'daytime' (shop hours)")]
    [Range(0f, 1f)]
    [SerializeField] float daytimePercentage = 0.7f; // 70% day, 30% night
    
    private bool gameStarted = false;
    private float currentDayTime = 0f; // 0 to dayDurationSeconds
    private bool isNightTime = false;
    private bool isDayComplete = false; // Prevents EndDay from firing multiple times

    // Cached values for performance
    private float cachedNightStartTime;
    private float cachedInvDayDuration;
    private float lastBroadcastNormalizedTime = -1f;
    private const float timeChangeBroadcastThreshold = 0.01f; // Only broadcast when time changes by 1%

    // CustomerManager reference for early day end
    private CustomerManager customerManager;
    
    [Header("Game Progress")]
    [SerializeField] int currentDay = 1;
    [SerializeField] bool hasPlayedIntroCutscene = false; // Tracks if intro cutscene was shown
    [SerializeField] public bool hasCompletedTutorial = false; // Tracks if tutorial was completed
    
    [Header("Lifetime Stats")]
    [SerializeField] int lifetimeCustomersCorrect = 0;
    [SerializeField] int lifetimeCustomersIncorrect = 0;
    
    [Header("Daily Stats (Today)")]
    [SerializeField] int dailyCustomersCorrect = 0;
    [SerializeField] int dailyCustomersIncorrect = 0;
    
    [Header("Money")]
    [SerializeField] float currentMoney = 0f;
    [SerializeField] float dailyMoneyEarned = 0f;
    [SerializeField] float dailyMoneySpent = 0f;
    [SerializeField] float dailyTipsEarned = 0f;
    
    [Header("Max Tip System")]
    [Tooltip("Tracks max tip chance for each customer (by CustomerData name)")]
    [SerializeField] private SerializableDictionary<string, float> maxTipChances = new SerializableDictionary<string, float>();
    [Tooltip("Chance increase per correct service without max tip")]
    [SerializeField] private float maxTipChanceIncrement = 0.2f; // 20%
    
    [Header("Business Settings")]
    [Tooltip("Minimum base payment per correct order (before tip)")]
    [SerializeField] float minOrderPayment = 32f;

    [Tooltip("Maximum base payment per correct order (before tip)")]
    [SerializeField] float maxOrderPayment = 55f;

    [Tooltip("Percentage of daily earnings taken as business expenses")]
    [Range(0f, 1f)]
    [SerializeField] float dailyExpensePercentage = 0.3f; // 30%

    [Header("Weather System")]
    [Tooltip("Base chance for rain to occur (20%)")]
    [SerializeField] float baseRainChance = 0.2f;

    [Tooltip("Chance increase per day without rain (10%)")]
    [SerializeField] float rainChanceIncreasePerDay = 0.1f;

    [Tooltip("Minimum rain duration in seconds")]
    [SerializeField] float minRainDuration = 60f; // 1 minute

    [Tooltip("Maximum rain duration in seconds")]
    [SerializeField] float maxRainDuration = 120f; // 2 minutes

    [Tooltip("Sway amount during rain")]
    [SerializeField] float rainSwayAmount = 0.1f;

    [Tooltip("Reference to DayNightBackground for sway control")]
    [SerializeField] DayNightBackground dayNightBackground;

    [Tooltip("Reference to rain particle system")]
    [SerializeField] GameObject rainParticles;

    [Header("Wet Floor System")]
    [Tooltip("References to wet floor sprite renderers")]
    [SerializeField] SpriteRenderer[] wetFloorRenderers = new SpriteRenderer[4];

    [Tooltip("Opacity increase per customer entering during rain")]
    [SerializeField] float wetFloorOpacityIncrement = 0.1f;

    [Tooltip("Maximum wet floor opacity")]
    [SerializeField] float maxWetFloorOpacity = 0.3f;

    [Tooltip("How long it takes for wet floor to fade out after rain stops (seconds)")]
    [SerializeField] float wetFloorFadeDuration = 30f;

    // Weather state
    public bool isRaining { get; private set; } = false;
    private float currentRainChance = 0.2f;
    private int daysSinceLastRain = 0;
    private bool hasRainedToday = false;
    private float rainTimer = 0f;
    private float rainDuration = 0f;
    private float defaultSwayAmount = 0.02f;
    private Coroutine swayTransitionCoroutine = null;
    private float currentWetFloorOpacity = 0f;
    private Coroutine wetFloorFadeCoroutine = null;

    [Header("Weather Transition")]
    [Tooltip("How long sway transition takes (seconds)")]
    [SerializeField] float swayTransitionDuration = 2f;

    // Events for other systems to listen to
    public event Action OnShopOpened;
    public event Action OnShopClosed;
    public event Action OnDayStarted;
    public event Action OnNightStarted;
    public event Action OnDayComplete; // Fires when day ends, before summary shown
    public event Action<float> OnDayTimeChanged; // Passes normalized time (0-1)
    public event Action<int> OnDayChanged;
    public event Action<float> OnMoneyChanged;

    void Awake()
    {
        // Singleton setup
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (transform.parent != null)
        {
            transform.SetParent(null, true);
        }

        DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
        // If intro cutscene will play, start with shop open (cutscene shows it opening)
        // Otherwise start with shop closed
        if (!hasPlayedIntroCutscene)
        {
            shopOpen = true; // Will be shown as open at end of cutscene
#if UNITY_EDITOR
            Debug.Log($"Game started. Day {currentDay}. Shop initialized as OPEN (cutscene will play).");
#endif
        }
        else
        {
            shopOpen = false;
#if UNITY_EDITOR
            Debug.Log($"Game started. Day {currentDay}. Shop is closed.");
#endif
        }

        currentDayTime = 0f;

        // Pre-calculate cached values for performance
        cachedNightStartTime = dayDurationSeconds * daytimePercentage;
        cachedInvDayDuration = 1f / dayDurationSeconds;

        // Find and subscribe to CustomerManager for early day end
        customerManager = FindFirstObjectByType<CustomerManager>();
        if (customerManager != null)
            customerManager.OnAllCustomersServedInLateDay += HandleAllCustomersServedInLateDay;
    }

    void OnDestroy()
    {
        if (customerManager != null)
            customerManager.OnAllCustomersServedInLateDay -= HandleAllCustomersServedInLateDay;
    }

    void Update()
    {
        // Time doesn't progress until game started AND tutorial completed
        if (!gameStarted || !hasCompletedTutorial) return;

        UpdateDayNightCycle();
        UpdateWeather();
    }

    void HandleAllCustomersServedInLateDay()
    {
        // Only end day early if we're past 90% and day isn't already complete
        if (GetNormalizedDayTime() >= 0.9f && !isDayComplete)
        {
#if UNITY_EDITOR
            Debug.Log("[GameManager] All customers served in late day (past 90%) - ending day early!");
#endif
            EndDay();
        }
    }

    void UpdateDayNightCycle()
    {
        // Advance time (half speed when shop is closed)
        float timeMultiplier = shopOpen ? 1f : 0.5f;
        currentDayTime += Time.deltaTime * timeMultiplier;

        // Calculate normalized day progress (0 to 1) - use cached inverse
        float normalizedTime = currentDayTime * cachedInvDayDuration;

        // Only broadcast time change if it exceeds threshold (reduces event spam)
        if (Mathf.Abs(normalizedTime - lastBroadcastNormalizedTime) >= timeChangeBroadcastThreshold)
        {
            lastBroadcastNormalizedTime = normalizedTime;
            OnDayTimeChanged?.Invoke(normalizedTime);
        }

        // Check when nighttime begins (for visual effects, doesn't close shop) - use cached value
        if (!isNightTime && currentDayTime >= cachedNightStartTime)
        {
            // Transition to night (visual only)
            isNightTime = true;
            OnNightStarted?.Invoke();
#if UNITY_EDITOR
            Debug.Log("Night has fallen (visual effects only - shop can stay open).");
#endif
        }

        // Check if day is over
        if (currentDayTime >= dayDurationSeconds && !isDayComplete)
        {
            // Day finished - end day and show summary
            EndDay();
        }
    }

    void UpdateWeather()
    {
        if (isRaining)
        {
            // Count down rain timer
            rainTimer -= Time.deltaTime;

            if (rainTimer <= 0f)
            {
                // Rain period over
                StopRain();
            }
        }
        else if (!hasRainedToday && !isDayComplete)
        {
            // Check for rain start randomly throughout the day
            // Small chance each frame to make it feel natural
            float frameRainChance = currentRainChance * Time.deltaTime * 0.001f; // Very small per-frame chance

            if (UnityEngine.Random.value < frameRainChance)
            {
                StartRain();
            }
        }
    }

    void StartRain()
    {
        if (isRaining || hasRainedToday) return;

        isRaining = true;
        hasRainedToday = true;
        rainDuration = UnityEngine.Random.Range(minRainDuration, maxRainDuration);
        rainTimer = rainDuration;

        // Stop any existing transition
        if (swayTransitionCoroutine != null)
        {
            StopCoroutine(swayTransitionCoroutine);
        }

        // Store default sway amount
        if (dayNightBackground != null)
        {
            defaultSwayAmount = dayNightBackground.swayAmount;
            swayTransitionCoroutine = StartCoroutine(TransitionSwayToRain());
        }

#if UNITY_EDITOR
        Debug.Log($"[Weather] Rain starting! Duration: {rainDuration:F1}s. Transitioning sway to {rainSwayAmount}");
#endif
    }

    void StopRain()
    {
        if (!isRaining) return;

        isRaining = false;

        // Stop any existing transition
        if (swayTransitionCoroutine != null)
        {
            StopCoroutine(swayTransitionCoroutine);
        }

        // Transition sway back to default
        if (dayNightBackground != null)
        {
            swayTransitionCoroutine = StartCoroutine(TransitionSwayToDefault());
        }

        // Start fading out wet floor
        if (currentWetFloorOpacity > 0f)
        {
            if (wetFloorFadeCoroutine != null)
            {
                StopCoroutine(wetFloorFadeCoroutine);
            }
            wetFloorFadeCoroutine = StartCoroutine(FadeOutWetFloor());
        }

#if UNITY_EDITOR
        Debug.Log($"[Weather] Rain stopping. Transitioning sway back to {defaultSwayAmount}");
#endif
    }

    /// <summary>
    /// Called by customers when they enter during rain to increase wet floor opacity
    /// </summary>
    public void OnCustomerEnteredDuringRain()
    {
        if (!isRaining) return;

        // Increase wet floor opacity
        currentWetFloorOpacity = Mathf.Min(currentWetFloorOpacity + wetFloorOpacityIncrement, maxWetFloorOpacity);
        UpdateWetFloorOpacity(currentWetFloorOpacity);

#if UNITY_EDITOR
        Debug.Log($"[Weather] Customer entered during rain. Wet floor opacity: {currentWetFloorOpacity:F2}");
#endif
    }

    void UpdateWetFloorOpacity(float opacity)
    {
        if (wetFloorRenderers == null || wetFloorRenderers.Length == 0) return;

        foreach (var renderer in wetFloorRenderers)
        {
            if (renderer == null) continue;

            Color color = renderer.color;
            color.a = opacity;
            renderer.color = color;
        }
    }

    System.Collections.IEnumerator FadeOutWetFloor()
    {
        float startOpacity = currentWetFloorOpacity;
        float elapsed = 0f;

        while (elapsed < wetFloorFadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / wetFloorFadeDuration;
            currentWetFloorOpacity = Mathf.Lerp(startOpacity, 0f, t);
            UpdateWetFloorOpacity(currentWetFloorOpacity);
            yield return null;
        }

        // Ensure we reach zero
        currentWetFloorOpacity = 0f;
        UpdateWetFloorOpacity(currentWetFloorOpacity);

#if UNITY_EDITOR
        Debug.Log("[Weather] Wet floor fully dried");
#endif

        wetFloorFadeCoroutine = null;
    }

    System.Collections.IEnumerator TransitionSwayToRain()
    {
        if (dayNightBackground == null) yield break;

        float startSway = dayNightBackground.swayAmount;
        float elapsed = 0f;

        // Smoothly increase sway
        while (elapsed < swayTransitionDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / swayTransitionDuration;
            dayNightBackground.swayAmount = Mathf.Lerp(startSway, rainSwayAmount, t);
            yield return null;
        }

        dayNightBackground.swayAmount = rainSwayAmount;

        // Enable rain particles after sway reaches max
        if (rainParticles != null)
        {
            rainParticles.SetActive(true);
        }

#if UNITY_EDITOR
        Debug.Log($"[Weather] Rain particles enabled at full sway");
#endif

        swayTransitionCoroutine = null;
    }

    System.Collections.IEnumerator TransitionSwayToDefault()
    {
        if (dayNightBackground == null) yield break;

        // Disable rain particles first
        if (rainParticles != null)
        {
            rainParticles.SetActive(false);
        }

        float startSway = dayNightBackground.swayAmount;
        float elapsed = 0f;

        // Smoothly decrease sway
        while (elapsed < swayTransitionDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / swayTransitionDuration;
            dayNightBackground.swayAmount = Mathf.Lerp(startSway, defaultSwayAmount, t);
            yield return null;
        }

        dayNightBackground.swayAmount = defaultSwayAmount;

#if UNITY_EDITOR
        Debug.Log($"[Weather] Rain fully stopped. Sway restored to {defaultSwayAmount}");
#endif

        swayTransitionCoroutine = null;
    }

    public void StartGame(bool startWithShopOpen = false) // call at end of cutscene/tutorial for beginning of game (after loading main scene)
    {
        // Reset everything to initial state
        gameStarted = true; // this enables time to begin moving (at 0.5 speed when shop is closed)
        currentDay = 1;
        currentDayTime = 0f;
        isNightTime = false;
        isDayComplete = false;

        lifetimeCustomersCorrect = 0;
        lifetimeCustomersIncorrect = 0;
        dailyCustomersCorrect = 0;
        dailyCustomersIncorrect = 0;
        currentMoney = 0f;
        dailyMoneyEarned = 0f;
        dailyMoneySpent = 0f;
        dailyTipsEarned = 0f;

        // Reset weather state
        currentRainChance = baseRainChance;
        daysSinceLastRain = 0;
        hasRainedToday = false;
        isRaining = false;
        if (rainParticles != null)
        {
            rainParticles.SetActive(false);
        }

        // Reset wet floor
        currentWetFloorOpacity = 0f;
        UpdateWetFloorOpacity(0f);
        if (wetFloorFadeCoroutine != null)
        {
            StopCoroutine(wetFloorFadeCoroutine);
            wetFloorFadeCoroutine = null;
        }

        // Set initial shop state based on parameter
        if (startWithShopOpen)
        {
            shopOpen = true;
            OnShopOpened?.Invoke();
#if UNITY_EDITOR
            Debug.Log("Game started! Day 1 begins. Shop starts OPEN.");
#endif
        }
        else
        {
            CloseShopInternal(forceNotify: true);
#if UNITY_EDITOR
            Debug.Log("Game started! Day 1 begins. Shop starts CLOSED.");
#endif
        }

        OnDayStarted?.Invoke();

    }
    
    void EndDay()
    {
        isDayComplete = true;

        // Close the shop at end of day
        if (shopOpen)
        {
            CloseShop();
        }

        // Stop any ongoing rain when day ends
        if (isRaining)
        {
            StopRain();
#if UNITY_EDITOR
            Debug.Log("[Weather] Rain stopped due to day ending");
#endif
        }

        // Calculate business expenses (30% of earnings + money spent)
        float businessExpenses = (dailyMoneyEarned * dailyExpensePercentage) + dailyMoneySpent;

        // Stop time progression
#if UNITY_EDITOR
        Debug.Log($"Day {currentDay} complete!");
        Debug.Log($"Today's Stats - Correct: {dailyCustomersCorrect}, Incorrect: {dailyCustomersIncorrect}");
        Debug.Log($"Money Earned: ${dailyMoneyEarned:F2}, Expenses: ${businessExpenses:F2}");
#endif

        OnDayComplete?.Invoke();

        // UI will show summary and call AdvanceToNextDay() when player clicks Continue
    }

    public void AdvanceToNextDay()
    {
        // Before modifying currentDay, apply increase to payment if performance was good
        if (dailyCustomersCorrect > dailyCustomersIncorrect)
        {
            // If current day is < 10, use 10% increase; else 5%
            float multiplier = (currentDay < 10) ? 1.10f : 1.05f;

            minOrderPayment *= multiplier;
            maxOrderPayment *= multiplier;

#if UNITY_EDITOR
            Debug.Log($"Payment range increased by {(multiplier - 1f) * 100f}% -> Min: ${minOrderPayment:F2}, Max: ${maxOrderPayment:F2}");
#endif
        }
#if UNITY_EDITOR
        else
        {
            Debug.Log("No payment increase today (not enough correct customers).");
        }
#endif

        // Update rain chance based on whether it rained today
        if (hasRainedToday)
        {
            // Reset rain chance and counter
            currentRainChance = baseRainChance;
            daysSinceLastRain = 0;
#if UNITY_EDITOR
            Debug.Log($"[Weather] It rained today! Rain chance reset to {currentRainChance * 100f:F0}%");
#endif
        }
        else
        {
            // Increase rain chance for next day
            daysSinceLastRain++;
            currentRainChance = baseRainChance + (daysSinceLastRain * rainChanceIncreasePerDay);
#if UNITY_EDITOR
            Debug.Log($"[Weather] No rain for {daysSinceLastRain} day(s). Rain chance now {currentRainChance * 100f:F0}%");
#endif
        }

        // Proceed to next day
        currentDay++;
        currentDayTime = 0f;
        isNightTime = false;
        isDayComplete = false;

        // Stop any ongoing rain and reset daily weather
        if (isRaining)
        {
            StopRain();
        }
        hasRainedToday = false;

        // Reset wet floor for new day
        currentWetFloorOpacity = 0f;
        UpdateWetFloorOpacity(0f);
        if (wetFloorFadeCoroutine != null)
        {
            StopCoroutine(wetFloorFadeCoroutine);
            wetFloorFadeCoroutine = null;
        }

        CloseShopInternal(forceNotify: true);

        // Clean up loose teabags from previous day
        CleanupLooseTeabags();

        OnDayChanged?.Invoke(currentDay);
        OnDayStarted?.Invoke();

#if UNITY_EDITOR
        Debug.Log($"Day {currentDay} has started! Shop is closed.");
#endif

        // Reset daily stats for the new day
        ResetDailyStats();
    }
    
    public void OpenShop()
    {

        if (!gameStarted)
        {
#if UNITY_EDITOR
            Debug.LogWarning("Cannot open shop - game hasn't started yet!");
#endif
            return;
        }

        if (isDayComplete)
        {
#if UNITY_EDITOR
            Debug.LogWarning("Cannot open shop - day summary in progress!");
#endif
            return;
        }

        if (shopOpen)
        {
#if UNITY_EDITOR
            Debug.LogWarning("Shop is already open!");
#endif
            return;
        }

        shopOpen = true;
        OnShopOpened?.Invoke();

#if UNITY_EDITOR
        Debug.Log("Shop is now OPEN!");
#endif

        // TODO: Enable shop interactions
    }

    public void CloseShop()
    {
        CloseShopInternal(forceNotify: false);
    }

    void CloseShopInternal(bool forceNotify)
    {
        if (!shopOpen && !forceNotify)
        {
#if UNITY_EDITOR
            Debug.LogWarning("Shop is already closed!");
#endif
            return;
        }

        shopOpen = false;

        OnShopClosed?.Invoke();

#if UNITY_EDITOR
        Debug.Log("Shop is now CLOSED.");
#endif

        // TODO: Disable shop interactions
    }

    public bool IsShopOpen() => shopOpen;
    public bool IsNightTime() => isNightTime;
    public bool HasGameStarted() => gameStarted;
    public bool IsDayComplete() => isDayComplete;
    
    public void RegisterCorrectService()
    {
        lifetimeCustomersCorrect++;
        dailyCustomersCorrect++;

        // Add random order payment between min and max
        float orderPayment = UnityEngine.Random.Range(minOrderPayment, maxOrderPayment);
        AddMoney(orderPayment);
        dailyMoneyEarned += orderPayment;

#if UNITY_EDITOR
        Debug.Log($"[GM] Correct -> +${orderPayment:F2} (balance=${currentMoney:F2})");
        Debug.Log($"Correct service! Payment: ${orderPayment:F2}. Today: {dailyCustomersCorrect}, Lifetime: {lifetimeCustomersCorrect}");
#endif
    }

    public void RegisterIncorrectService()
    {
        lifetimeCustomersIncorrect++;
        dailyCustomersIncorrect++;

        // Still pay for incorrect orders (but no tip)
        float orderPayment = UnityEngine.Random.Range(minOrderPayment, maxOrderPayment);
        AddMoney(orderPayment);
        dailyMoneyEarned += orderPayment;

#if UNITY_EDITOR
        Debug.Log($"[GM] Incorrect order -> +${orderPayment:F2} (no tip) (balance=${currentMoney:F2})");
        Debug.Log($"Incorrect service! Payment: ${orderPayment:F2} (no tip). Today: {dailyCustomersIncorrect}, Lifetime: {lifetimeCustomersIncorrect}");
#endif
    }

    public int GetCustomersServedCorrectly() => dailyCustomersCorrect;
    public int GetCustomersServedIncorrectly() => dailyCustomersIncorrect;
    public int GetTotalCustomersServed() => dailyCustomersCorrect + dailyCustomersIncorrect;
    
    public int GetLifetimeCustomersCorrect() => lifetimeCustomersCorrect;
    public int GetLifetimeCustomersIncorrect() => lifetimeCustomersIncorrect;
    public int GetLifetimeTotalCustomers() => lifetimeCustomersCorrect + lifetimeCustomersIncorrect;


    
    public void AddMoney(float amount)
    {
        currentMoney += amount;
        OnMoneyChanged?.Invoke(currentMoney);
#if UNITY_EDITOR
        Debug.Log($"Added ${amount:F2}. Total: ${currentMoney:F2}");
#endif
    }

    public void AddTip(float amount, CustomerData customerData = null, bool isMaxTip = false)
    {
        currentMoney += amount;
        dailyMoneyEarned += amount;
        dailyTipsEarned += amount;
        OnMoneyChanged?.Invoke(currentMoney);
        
        // handle max tip chance tracking
        if (customerData != null)
        {
            string customerKey = customerData.customerName;
            
            if (isMaxTip)
            {
                // reset chance after receiving max tip
                maxTipChances[customerKey] = 0f;
#if UNITY_EDITOR
                Debug.Log($"Max tip received from {customerKey}! ${amount:F2}. Chance reset to 0%.");
#endif
            }
            else
            {
                // increase chance for next time
                if (!maxTipChances.ContainsKey(customerKey))
                {
                    maxTipChances[customerKey] = 0f;
                }

                maxTipChances[customerKey] += maxTipChanceIncrement;
                maxTipChances[customerKey] = Mathf.Min(maxTipChances[customerKey], 1f); // cap at 100%

#if UNITY_EDITOR
                Debug.Log($"Tip received from {customerKey}: ${amount:F2}. Max tip chance increased to {maxTipChances[customerKey] * 100f:F0}%.");
#endif
            }
        }
#if UNITY_EDITOR
        else
        {
            Debug.Log($"Tip received: ${amount:F2}. Total: ${currentMoney:F2}");
        }
#endif
    }
    
    public float GetMaxTipChance(CustomerData customerData)
    {
        if (customerData == null) return 0f;
        
        string customerKey = customerData.customerName;
        if (maxTipChances.ContainsKey(customerKey))
        {
            return maxTipChances[customerKey];
        }
        
        return 0f;
    }
    
    public bool RollForMaxTip(CustomerData customerData)
    {
        if (customerData == null) return false;

        float chance = GetMaxTipChance(customerData);
        if (chance <= 0f) return false;

        float roll = UnityEngine.Random.value;
        bool success = roll < chance;

#if UNITY_EDITOR
        Debug.Log($"Max tip roll for {customerData.customerName}: {roll:F3} < {chance:F3}? {success}");
#endif
        return success;
    }

    public bool SpendMoney(float amount)
    {
        if (currentMoney >= amount)
        {
            currentMoney -= amount;
            dailyMoneySpent += amount;
            OnMoneyChanged?.Invoke(currentMoney);
#if UNITY_EDITOR
            Debug.Log($"Spent ${amount:F2}. Remaining: ${currentMoney:F2}");
#endif
            return true;
        }

#if UNITY_EDITOR
        Debug.LogWarning($"Not enough money! Need ${amount:F2}, have ${currentMoney:F2}");
#endif
        return false;
    }

    public float GetMoney() => currentMoney;
    public float GetDailyMoneyEarned() => dailyMoneyEarned;
    public float GetDailyMoneySpent() => dailyMoneySpent;
    public float GetDailyTipsEarned() => dailyTipsEarned;
    
    public float GetDailyExpenses()
    {
        return (dailyMoneyEarned * dailyExpensePercentage) + dailyMoneySpent;
    }
    
    public float GetDailyNetProfit()
    {
        return dailyMoneyEarned - GetDailyExpenses();
    }


    
    public int GetCurrentDay() => currentDay;
    
    public float GetNormalizedDayTime() => currentDayTime / dayDurationSeconds;
    
    public float GetDayTimeRemaining() => dayDurationSeconds - currentDayTime;
    
    public string GetTimeOfDayString()
    {
        float normalizedTime = GetNormalizedDayTime();
        int hours = Mathf.FloorToInt(normalizedTime * 24f);
        int minutes = Mathf.FloorToInt((normalizedTime * 24f - hours) * 60f);
        return $"{hours:D2}:{minutes:D2}";
    }

    void ResetDailyStats()
    {
        // Reset only today's stats (lifetime stats persist)
        dailyCustomersCorrect = 0;
        dailyCustomersIncorrect = 0;
        dailyMoneyEarned = 0f;
        dailyMoneySpent = 0f;
        dailyTipsEarned = 0f;

#if UNITY_EDITOR
        Debug.Log($"Daily stats reset for Day {currentDay}");
#endif
    }

    /// <summary>
    /// Destroys all loose teabags not contained in packets (cleanup for new day)
    /// </summary>
    void CleanupLooseTeabags()
    {
        Teabag[] allTeabags = FindObjectsByType<Teabag>(FindObjectsSortMode.None);
        int cleanedCount = 0;

        foreach (Teabag teabag in allTeabags)
        {
            if (teabag == null) continue;

            // Skip prefab assets (not scene instances)
            if (teabag.gameObject.scene.name == null) continue;

            // Destroy if loose (no parent = not in a packet)
            if (teabag.transform.parent == null)
            {
#if UNITY_EDITOR
                Debug.Log($"[GameManager] Destroying loose teabag: {teabag.gameObject.name}");
#endif
                Destroy(teabag.gameObject);
                cleanedCount++;
            }
        }

#if UNITY_EDITOR
        if (cleanedCount > 0)
        {
            Debug.Log($"[GameManager] Cleaned up {cleanedCount} loose teabag(s) for new day");
        }
#endif
    }



    
    // Cutscene tracking
    public bool HasPlayedIntroCutscene() => hasPlayedIntroCutscene;
    
    public void MarkIntroCutscenePlayed()
    {
        hasPlayedIntroCutscene = true;
#if UNITY_EDITOR
        Debug.Log("Intro cutscene marked as played");
#endif
    }

    // Tutorial tracking
    public bool HasCompletedTutorial() => hasCompletedTutorial;

    public void MarkTutorialCompleted()
    {
        hasCompletedTutorial = true;
#if UNITY_EDITOR
        Debug.Log("Tutorial marked as completed - time will now progress");
#endif
    }

    public void SaveGame()
    {
        // TODO: Implement save system
#if UNITY_EDITOR
        Debug.Log("Game saved!");
#endif
    }

    public void LoadGame()
    {
        // TODO: Implement load system
#if UNITY_EDITOR
        Debug.Log("Game loaded!");
#endif
    }

    [ContextMenu("Start Game (Debug)")]
    void Debug_StartGame()
    {
        StartGame();
    }
    
    [ContextMenu("Force Open Shop")]
    void Debug_ForceOpenShop()
    {
        OpenShop();
    }

    [ContextMenu("Force Close Shop")]
    void Debug_ForceCloseShop()
    {
        CloseShop();
    }

    [ContextMenu("Skip to Night")]
    void Debug_SkipToNight()
    {
        currentDayTime = dayDurationSeconds * daytimePercentage;
    }

    [ContextMenu("Skip to Next Day")]
    void Debug_SkipToNextDay()
    {
        currentDayTime = dayDurationSeconds;
    }

    [ContextMenu("End Day")]
    void Debug_EndDay()
    {
        EndDay();
    }

    [ContextMenu("Add $100")]
    void Debug_AddMoney()
    {
        AddMoney(100f);
    }

    [ContextMenu("Start Rain")]
    void Debug_StartRain()
    {
        if (!hasRainedToday)
        {
            StartRain();
        }
        else
        {
            Debug.LogWarning("Rain has already occurred today!");
        }
    }

    [ContextMenu("Stop Rain")]
    void Debug_StopRain()
    {
        StopRain();
    }

    [ContextMenu("Test Wet Floor +0.1")]
    void Debug_IncreaseWetFloor()
    {
        OnCustomerEnteredDuringRain();
    }
}
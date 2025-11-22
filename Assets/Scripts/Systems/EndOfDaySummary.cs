using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using JamesKJamKit.Services;
using JamesKJamKit.Services.Audio;
using System.Collections;

public class EndOfDaySummary : MonoBehaviour
{
    [Header("Text Fields - Stats Section")]
    [SerializeField] private TextMeshProUGUI dayCompletedText;
    [SerializeField] private TextMeshProUGUI totalOrdersText;
    [SerializeField] private TextMeshProUGUI correctOrdersText;
    [SerializeField] private TextMeshProUGUI incorrectOrdersText;
    [SerializeField] private TextMeshProUGUI successRateText;
    
    [Header("Text Fields - Money Section")]
    [SerializeField] private TextMeshProUGUI moneyEarnedText;
    [SerializeField] private TextMeshProUGUI tipsReceivedText;
    [SerializeField] private TextMeshProUGUI moneySpentText;
    [SerializeField] private TextMeshProUGUI currentMoneyText;

    [Header("Money Animation")]
    [SerializeField] private float moneyCountDuration = 1.5f; // How long to count up money values
    [SerializeField] private float popFontSizeIncrease = 3f; // How much to increase font size on pop
    [SerializeField] private float popDuration = 0.3f; // How long the pop animation lasts

    [Header("Main Menu Settings")]
    [Tooltip("Name of the main menu scene to load")]
    [SerializeField] private string mainMenuSceneName = "MainMenu";

    [Header("Animation Settings")]
    [SerializeField] private RectTransform panelRectTransform; // The "Panel" child to animate
    [SerializeField] private float slideSpeed = 5f; // How fast it slides in/out
    [SerializeField] private CanvasGroup completedParentCanvasGroup; // CompletedParent canvas group
    [SerializeField] private CanvasGroup quitButtonCanvasGroup; // QuitButton canvas group
    [SerializeField] private CanvasGroup nextDayButtonCanvasGroup; // NextDayButton canvas group
    [SerializeField] private float buttonFadeDuration = 0.5f; // How long buttons take to fade in

    [Header("Bounce Settings")]
    [SerializeField] private float bounceHeight = 50f; // How high to bounce (in UI units)
    [SerializeField] private float bounceDuration = 0.4f; // How long the bounce lasts
    [SerializeField] private float bounceFrequency = 2f; // How many bounces

    [Header("Audio")]
    [SerializeField] private SFXEvent sfxLanding; // Sound when panel lands at desired position
    [SerializeField] private SFXEvent sfxNextDay; // Sound when advancing to next day
    [SerializeField] private SFXEvent sfxNightfall; // Sound when night background fades in

    [Header("Timing")]
    [SerializeField] private float completedParentDelay = 0.5f; // Delay before sliding out after showing CompletedParent

    [Header("Completed Stamp Animation")]
    [SerializeField] private RectTransform completedParentRectTransform; // CompletedParent transform for scaling
    [SerializeField] private float stampScaleDuration = 0.4f; // How long the scale animation takes
    [SerializeField] private float stampScaleOvershoot = 1.1f; // Scale overshoot amount

    [Header("Night Background")]
    [SerializeField] private GameObject backWallNight; // The BackWallNight GameObject to fade in/out
    [SerializeField] private SpriteRenderer backWallNightRenderer; // SpriteRenderer for BackWallNight
    [SerializeField] private float backWallFadeDuration = 1.0f; // How long the fade takes
    [SerializeField] private float nightfallAudioFadeInDuration = 2.0f; // How long the audio fade in takes
    [SerializeField] private float nightfallAudioFadeOutDuration = 1.5f; // How long the audio fade out takes

    [Header("Clock Night Colors")]
    [SerializeField] private Image clockFrameImage; // Image for clockFrame
    [SerializeField] private Image clockFaceImage; // Image for clockFace
    private Color nightClockColor = new Color(0.73f, 0.73f, 0.73f, 1f); // BABABA

    private Vector3 hiddenPosition = new Vector3(-98, 100, 0); // Position when off-screen (above)
    private Vector3 visiblePosition = new Vector3(-98, -1000, 0); // Position when on-screen (below)
    private bool isAnimatingIn = false;
    private bool isAnimatingOut = false;
    private bool isBouncing = false;
    private bool bounceFinished = false;
    private bool landingSfxPlayed = false;
    private float bounceStartTime = 0f;
    private AudioSource nightfallAudioSource; // Reference to the playing nightfall audio

    void Awake()
    {
        // Auto-find Panel RectTransform if not assigned
        if (panelRectTransform == null)
        {
            Transform panel = transform.Find("Panel");
            if (panel != null)
            {
                panelRectTransform = panel.GetComponent<RectTransform>();
            }
        }

        // Auto-find CompletedParent canvas group if not assigned
        if (completedParentCanvasGroup == null)
        {
            Transform completedParent = transform.Find("CompletedParent");
            if (completedParent != null)
            {
                completedParentCanvasGroup = completedParent.GetComponent<CanvasGroup>();
            }
        }

        // Auto-find CompletedParent RectTransform if not assigned
        if (completedParentRectTransform == null)
        {
            Transform completedParent = transform.Find("CompletedParent");
            if (completedParent != null)
            {
                completedParentRectTransform = completedParent.GetComponent<RectTransform>();
            }
        }

        // Auto-find BackWallNight if not assigned
        if (backWallNight == null)
        {
            backWallNight = GameObject.Find("BackWallNight");
        }

        // Auto-find BackWallNight SpriteRenderer if not assigned
        if (backWallNightRenderer == null && backWallNight != null)
        {
            backWallNightRenderer = backWallNight.GetComponent<SpriteRenderer>();
        }

        // Auto-find clock images if not assigned
        if (clockFrameImage == null)
        {
            GameObject clockFrame = GameObject.Find("clockFrame");
            if (clockFrame != null)
            {
                clockFrameImage = clockFrame.GetComponent<Image>();
            }
        }

        if (clockFaceImage == null)
        {
            GameObject clockFace = GameObject.Find("clockFace");
            if (clockFace != null)
            {
                clockFaceImage = clockFace.GetComponent<Image>();
            }
        }
    }

    void Start()
    {
        // Subscribe to GameManager events
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnDayComplete += ShowSummary;
#if UNITY_EDITOR
            Debug.Log("EndOfDaySummary: Subscribed to OnDayComplete event");
#endif
        }
        else
        {
#if UNITY_EDITOR
            Debug.LogWarning("EndOfDaySummary: GameManager.Instance is null!");
#endif
        }

        // Initialize Panel to hidden position (keep GameObject active to receive events)
        if (panelRectTransform != null)
        {
            panelRectTransform.anchoredPosition = hiddenPosition;
        }

        // Make sure CompletedParent starts hidden
        if (completedParentCanvasGroup != null)
        {
            completedParentCanvasGroup.alpha = 0f;
        }

        // Make sure buttons start hidden
        if (quitButtonCanvasGroup != null)
        {
            quitButtonCanvasGroup.alpha = 0f;
        }

        if (nextDayButtonCanvasGroup != null)
        {
            nextDayButtonCanvasGroup.alpha = 0f;
        }

        // Make sure BackWallNight starts disabled and at 0 alpha
        if (backWallNight != null)
        {
            backWallNight.SetActive(false);
        }

        if (backWallNightRenderer != null)
        {
            Color color = backWallNightRenderer.color;
            color.a = 0f;
            backWallNightRenderer.color = color;
        }

        // Don't deactivate the GameObject - we need it active to receive events
        // The Panel is already off-screen at hiddenPosition
    }

    void Update()
    {
        // Animate the panel sliding in/out with bounce
        if (panelRectTransform != null && (isAnimatingIn || isAnimatingOut))
        {
            Vector3 targetPos;
            float distanceToTarget;

            if (isAnimatingIn)
            {
                // OPENING ANIMATION - slide down to visible position
                targetPos = visiblePosition;
            }
            else // isAnimatingOut
            {
                // CLOSING ANIMATION - slide up to hidden position
                targetPos = hiddenPosition;
            }

            // Move towards target position at constant speed
            Vector3 newPosition = Vector3.MoveTowards(
                panelRectTransform.anchoredPosition,
                targetPos,
                slideSpeed * Time.deltaTime * 100f // Multiply by 100 for units per second
            );

            // Calculate distance after lerping to check if we've arrived
            distanceToTarget = Vector3.Distance(newPosition, targetPos);

            // Check if we've just reached the target and should start bouncing
            if (isAnimatingIn && !isBouncing && !bounceFinished && distanceToTarget < 5f)
            {
                isBouncing = true;
                bounceStartTime = Time.time;
                landingSfxPlayed = false;
                newPosition = targetPos; // Snap to exact target when starting bounce
            }

            // Apply bounce effect if we're bouncing
            if (isBouncing && isAnimatingIn)
            {
                float bounceTime = Time.time - bounceStartTime;
                if (bounceTime < bounceDuration)
                {
                    // Damped sine wave for bounce (bouncing UP from the target position)
                    float bounceProgress = bounceTime / bounceDuration;
                    float dampening = 1f - bounceProgress; // Goes from 1 to 0
                    float bounce = Mathf.Sin(bounceTime * Mathf.PI * bounceFrequency * 2f) * bounceHeight * dampening;
                    newPosition.y += bounce; // Bounce upward (positive Y)

                    // Play landing SFX at the start of bounce
                    if (!landingSfxPlayed && bounceTime > 0.05f)
                    {
                        PlaySfx(sfxLanding);
                        landingSfxPlayed = true;
                    }
                }
                else
                {
                    isBouncing = false; // Bounce finished
                    bounceFinished = true; // Mark bounce as complete
                }
            }

            panelRectTransform.anchoredPosition = newPosition;

            // Check if animation is complete
            if (isAnimatingOut)
            {
                distanceToTarget = Vector3.Distance(panelRectTransform.anchoredPosition, targetPos);
                if (distanceToTarget < 5f)
                {
                    // Finished sliding out - just stop animating, keep GameObject active
                    isAnimatingOut = false;
                }
            }
        }
    }

    void OnDestroy()
    {
        // Unsubscribe from events
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnDayComplete -= ShowSummary;
        }
    }

    void ShowSummary()
    {
        if (GameManager.Instance == null) return;

#if UNITY_EDITOR
        Debug.Log("ShowSummary called!");
#endif

        // Get stats from GameManager
        int day = GameManager.Instance.GetCurrentDay();
        int customersTotal = GameManager.Instance.GetTotalCustomersServed();
        int customersCorrect = GameManager.Instance.GetCustomersServedCorrectly();
        int customersIncorrect = GameManager.Instance.GetCustomersServedIncorrectly();
        float moneyEarned = GameManager.Instance.GetDailyMoneyEarned();
        float tipsEarned = GameManager.Instance.GetDailyTipsEarned();
        float moneySpent = GameManager.Instance.GetDailyExpenses(); // This includes 30% + actual spending
        float currentMoney = GameManager.Instance.GetMoney();

        // Calculate success rate
        float successRate = customersTotal > 0 ? (customersCorrect / (float)customersTotal) * 100f : 0f;

        // Update UI text fields - stats section (no animation, preserve color tags)
        UpdateTextField(dayCompletedText, $"Day {day} Complete!");
        UpdateTextField(totalOrdersText, $"Total Orders: <color=white>{customersTotal}</color>");
        UpdateTextField(correctOrdersText, $"Correct Orders: <color=#06402B>{customersCorrect}</color>");
        UpdateTextField(incorrectOrdersText, $"Incorrect Orders: <color=#8B0000>{customersIncorrect}</color>");

        // Replace {successRate} placeholder in the existing text and change color to white
        if (successRateText != null)
        {
            string text = successRateText.text;
            // Replace the yellow color with white
            text = text.Replace("#9B870D", "white");
            // Replace the placeholder
            text = text.Replace("{successRate}", $"{successRate:F1}");
            successRateText.text = text;
        }

        // Set money fields to blank initially (just the label, no value)
        UpdateTextField(moneyEarnedText, "Money Earned: <color=#06402B>");
        UpdateTextField(tipsReceivedText, "Tips Received: <color=#06402B>");
        UpdateTextField(moneySpentText, "Expenses: <color=#8B0000>");
        UpdateTextField(currentMoneyText, "Current Money: <color=white>");

        // Start sequential money counter animations after panel settles
        StartCoroutine(SequentialMoneyAnimations(moneyEarned, tipsEarned, moneySpent, currentMoney));

        // Reset animation state
        isAnimatingIn = true;
        isAnimatingOut = false;
        isBouncing = false;
        bounceFinished = false;
        landingSfxPlayed = false;

        // Make sure CompletedParent is hidden
        if (completedParentCanvasGroup != null)
        {
            completedParentCanvasGroup.alpha = 0f;
        }

        // Make sure buttons are hidden initially
        if (quitButtonCanvasGroup != null)
        {
            quitButtonCanvasGroup.alpha = 0f;
        }

        if (nextDayButtonCanvasGroup != null)
        {
            nextDayButtonCanvasGroup.alpha = 0f;
        }

        // Make sure Panel starts at hidden position
        if (panelRectTransform != null)
        {
            panelRectTransform.anchoredPosition = hiddenPosition;
#if UNITY_EDITOR
            Debug.Log($"Panel position set to hidden: {hiddenPosition}. Starting animation!");
#endif
        }
        else
        {
#if UNITY_EDITOR
            Debug.LogError("panelRectTransform is null!");
#endif
        }

        // GameObject is already active (stays active to receive events)
#if UNITY_EDITOR
        Debug.Log("End of Day Summary shown - animating in");
#endif
    }

    void UpdateTextField(TextMeshProUGUI textField, string value)
    {
        if (textField != null)
        {
            textField.text = value;
        }
    }

    IEnumerator SequentialMoneyAnimations(float moneyEarned, float tipsEarned, float moneySpent, float currentMoney)
    {
        // Fade in BackWallNight at the same time panel is sliding in
        StartCoroutine(FadeInBackWallNight());

        // Wait until panel has finished bouncing and settled
        while (isBouncing || !bounceFinished)
        {
            yield return null;
        }

        // Now animate each money field in sequence with color tags
        yield return StartCoroutine(AnimateMoney(moneyEarnedText, "Money Earned: <color=#06402B>$", "</color>", 0f, moneyEarned));
        yield return StartCoroutine(AnimateMoney(tipsReceivedText, "Tips Received: <color=#06402B>$", "</color>", 0f, tipsEarned));
        yield return StartCoroutine(AnimateMoney(moneySpentText, "Expenses: <color=#8B0000>$", "</color>", 0f, moneySpent));
        yield return StartCoroutine(AnimateMoney(currentMoneyText, "Current Money: <color=white>$", "</color>", 0f, currentMoney));

        // After all money animations are complete, fade in the buttons
        yield return StartCoroutine(FadeInButtons());
    }

    IEnumerator FadeInButtons()
    {
        float elapsed = 0f;

        while (elapsed < buttonFadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / buttonFadeDuration);

            // Fade both buttons in at the same time
            if (quitButtonCanvasGroup != null)
            {
                quitButtonCanvasGroup.alpha = t;
            }

            if (nextDayButtonCanvasGroup != null)
            {
                nextDayButtonCanvasGroup.alpha = t;
            }

            yield return null;
        }

        // Snap to full alpha
        if (quitButtonCanvasGroup != null)
        {
            quitButtonCanvasGroup.alpha = 1f;
        }

        if (nextDayButtonCanvasGroup != null)
        {
            nextDayButtonCanvasGroup.alpha = 1f;
        }
    }

    IEnumerator AnimateMoney(TextMeshProUGUI textField, string prefix, string suffix, float startValue, float endValue)
    {
        if (textField == null) yield break;

        // Store original font size
        float originalFontSize = textField.fontSize;

        // Count up animation
        float elapsed = 0f;
        float currentValue = startValue;

        while (elapsed < moneyCountDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / moneyCountDuration);
            currentValue = Mathf.Lerp(startValue, endValue, t);

            textField.text = $"{prefix}{currentValue:F2}{suffix}";
            yield return null;
        }

        // Snap exactly to end value
        textField.text = $"{prefix}{endValue:F2}{suffix}";

        // Pop animation - grow then shrink
        elapsed = 0f;
        while (elapsed < popDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / popDuration);

            // Use sine wave for smooth pop in and out
            float popProgress = Mathf.Sin(t * Mathf.PI);
            float currentFontSize = originalFontSize + (popFontSizeIncrease * popProgress);

            textField.fontSize = currentFontSize;
            yield return null;
        }

        // Snap back to original size
        textField.fontSize = originalFontSize;
    }

    IEnumerator AnimateCompletedStamp()
    {
        if (completedParentRectTransform == null) yield break;

        // Store original scale
        Vector3 originalScale = Vector3.one;

        // Reset scale to 0
        completedParentRectTransform.localScale = Vector3.zero;

        float elapsed = 0f;
        float halfDuration = stampScaleDuration / 2f;

        // First half: scale from 0 to overshoot (1.1)
        while (elapsed < halfDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / halfDuration);

            // Use ease-out for smooth scaling
            float easeT = 1f - Mathf.Pow(1f - t, 3f);
            float scale = Mathf.Lerp(0f, stampScaleOvershoot, easeT);

            completedParentRectTransform.localScale = Vector3.one * scale;
            yield return null;
        }

        // Second half: scale from overshoot (1.1) back to 1.0
        elapsed = 0f;
        while (elapsed < halfDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / halfDuration);

            // Use ease-in for smooth scaling back
            float easeT = Mathf.Pow(t, 2f);
            float scale = Mathf.Lerp(stampScaleOvershoot, 1f, easeT);

            completedParentRectTransform.localScale = Vector3.one * scale;
            yield return null;
        }

        // Snap to final scale
        completedParentRectTransform.localScale = originalScale;
    }

    IEnumerator FadeInBackWallNight()
    {
        if (backWallNight == null || backWallNightRenderer == null) yield break;

        // Enable the GameObject
        backWallNight.SetActive(true);

        // Play nightfall sound effect and start fading it in
        nightfallAudioSource = PlaySfxWithFade(sfxNightfall);

        float elapsed = 0f;
        Color color = backWallNightRenderer.color;

        while (elapsed < backWallFadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / backWallFadeDuration);

            color.a = t;
            backWallNightRenderer.color = color;

            // Lerp clock colors from white to night color
            if (clockFrameImage != null)
            {
                clockFrameImage.color = Color.Lerp(Color.white, nightClockColor, t);
            }

            if (clockFaceImage != null)
            {
                clockFaceImage.color = Color.Lerp(Color.white, nightClockColor, t);
            }

            yield return null;
        }

        // Snap to full alpha
        color.a = 1f;
        backWallNightRenderer.color = color;

        // Snap clocks to night color
        if (clockFrameImage != null)
        {
            clockFrameImage.color = nightClockColor;
        }

        if (clockFaceImage != null)
        {
            clockFaceImage.color = nightClockColor;
        }
    }

    IEnumerator FadeOutBackWallNight()
    {
        if (backWallNight == null || backWallNightRenderer == null) yield break;

        // Start fading out the audio
        if (nightfallAudioSource != null)
        {
            StartCoroutine(FadeOutAudio(nightfallAudioSource, nightfallAudioFadeOutDuration));
        }

        float elapsed = 0f;
        Color color = backWallNightRenderer.color;

        while (elapsed < backWallFadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / backWallFadeDuration);

            color.a = 1f - t;
            backWallNightRenderer.color = color;

            // Lerp clock colors from night color back to white
            if (clockFrameImage != null)
            {
                clockFrameImage.color = Color.Lerp(nightClockColor, Color.white, t);
            }

            if (clockFaceImage != null)
            {
                clockFaceImage.color = Color.Lerp(nightClockColor, Color.white, t);
            }

            yield return null;
        }

        // Snap to zero alpha and disable
        color.a = 0f;
        backWallNightRenderer.color = color;
        backWallNight.SetActive(false);

        // Snap clocks back to white
        if (clockFrameImage != null)
        {
            clockFrameImage.color = Color.white;
        }

        if (clockFaceImage != null)
        {
            clockFaceImage.color = Color.white;
        }
    }

    public void OnContinueButtonClicked()
    {
        // Play next day SFX
        PlaySfx(sfxNextDay);

        // Show CompletedParent with scale animation
        if (completedParentCanvasGroup != null)
        {
            completedParentCanvasGroup.alpha = 1f;
        }

        // Start scale animation
        StartCoroutine(AnimateCompletedStamp());

#if UNITY_EDITOR
        Debug.Log("Continuing to next day - showing CompletedParent...");
#endif

        // Start coroutine to wait, then slide out and advance day
        StartCoroutine(AdvanceDayAfterAnimation());
    }

    private System.Collections.IEnumerator AdvanceDayAfterAnimation()
    {
        // Wait for the delay before sliding out
        yield return new WaitForSeconds(completedParentDelay);

        // Start fade out of BackWallNight and slide-up animation at the same time
        StartCoroutine(FadeOutBackWallNight());

        // Start slide-up animation
        isAnimatingIn = false;
        isAnimatingOut = true;
        isBouncing = false;
        bounceFinished = false;

#if UNITY_EDITOR
        Debug.Log("Sliding out...");
#endif

        // Wait until the panel has slid off screen
        while (isAnimatingOut)
        {
            yield return null;
        }

        // Reset CompletedParent alpha for next time
        if (completedParentCanvasGroup != null)
        {
            completedParentCanvasGroup.alpha = 0f;
        }

        // Now advance to next day
        if (GameManager.Instance != null)
        {
            GameManager.Instance.AdvanceToNextDay();
        }

#if UNITY_EDITOR
        Debug.Log("Advanced to next day");
#endif
    }

    public void OnMainMenuButtonClicked()
    {
#if UNITY_EDITOR
        Debug.Log("Returning to main menu - sliding out...");
#endif

        // Start fade out of BackWallNight
        StartCoroutine(FadeOutBackWallNight());

        // Start slide-up animation
        isAnimatingIn = false;
        isAnimatingOut = true;
        isBouncing = false;
        bounceFinished = false;

        // Start coroutine to return to menu after animation
        StartCoroutine(ReturnToMenuAfterAnimation());
    }

    private System.Collections.IEnumerator ReturnToMenuAfterAnimation()
    {
        // Wait until the panel has slid off screen
        while (isAnimatingOut)
        {
            yield return null;
        }

        // Destroy the GameManager singleton so it doesn't persist
        if (GameManager.Instance != null)
        {
            Destroy(GameManager.Instance.gameObject);
        }

        // Load main menu scene
        SceneManager.LoadScene(mainMenuSceneName);
    }

    void PlaySfx(SFXEvent evt)
    {
        if (evt == null) return;
        AudioManager.Instance?.PlaySFX(evt, transform);
    }

    AudioSource PlaySfxWithFade(SFXEvent evt)
    {
        if (evt == null || AudioManager.Instance == null) return null;

        // Get a random clip from the event
        var clip = evt.GetRandomClip();
        if (clip == null) return null;

        // Create a temporary GameObject for the audio source
        GameObject audioObj = new GameObject("NightfallAudio");
        audioObj.transform.SetParent(transform);
        AudioSource audioSource = audioObj.AddComponent<AudioSource>();

        // Configure the audio source
        audioSource.clip = clip;
        audioSource.loop = false;
        audioSource.spatialBlend = Mathf.Clamp01(evt.spatialBlend);
        audioSource.pitch = Random.Range(evt.pitchRange.x, evt.pitchRange.y);
        audioSource.volume = 0f;

        // Play the clip
        audioSource.Play();

        // Start fade in
        StartCoroutine(FadeInAudio(audioSource, evt.volume, nightfallAudioFadeInDuration));

        return audioSource;
    }

    IEnumerator FadeInAudio(AudioSource source, float targetVolume, float duration)
    {
        if (source == null) yield break;

        float elapsed = 0f;
        float startVolume = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            if (source != null)
            {
                source.volume = Mathf.Lerp(startVolume, targetVolume, t);
            }

            yield return null;
        }

        // Snap to target volume
        if (source != null)
        {
            source.volume = targetVolume;
        }
    }

    IEnumerator FadeOutAudio(AudioSource source, float duration)
    {
        if (source == null) yield break;

        float elapsed = 0f;
        float startVolume = source.volume;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            if (source != null)
            {
                source.volume = Mathf.Lerp(startVolume, 0f, t);
            }

            yield return null;
        }

        // Stop the audio and clean up the GameObject
        if (source != null)
        {
            source.Stop();
            if (source.gameObject != null)
            {
                Destroy(source.gameObject);
            }
        }
    }
}
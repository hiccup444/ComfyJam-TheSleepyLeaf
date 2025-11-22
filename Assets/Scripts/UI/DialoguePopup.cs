using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

public class DialoguePopup : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("TextMeshPro text component (preferred)")]
    public TextMeshProUGUI tmpText;
    
    [Tooltip("Legacy Text component (fallback)")]
    public Text legacyText;
    
    [Tooltip("DialogueChoices GameObject with Yes/No buttons (optional)")]
    public GameObject dialogueChoices;
    
    [Header("Animation (Optional)")]
    [Tooltip("Animate the popup appearing")]
    public bool animateAppear = true;
    
    [Tooltip("Scale animation duration")]
    public float animationDuration = 0.3f;
    
    [Tooltip("Delay after dialogue fully fades out before next dialogue can appear")]
    public float delayAfterFadeOut = 0.5f;
    
    [Header("Typewriter Effect")]
    [Tooltip("Enable typewriter effect for text")]
    public bool enableTypewriter = true;

    [Tooltip("Characters revealed per second")]
    public float charactersPerSecond = 30f;

    [Tooltip("Pause duration after punctuation (. or !)")]
    public float punctuationPauseDuration = 0.25f;

    [Tooltip("Duration to display dialogue after skipping typewriter (in seconds)")]
    public float skippedDisplayDuration = 0.5f;

    [Header("DialogSpeech (Optional)")]
    [Tooltip("DialogSpeech voice for letter blips (optional)")]
    public DialogSpeechVoice dialogSpeech;

    private float defaultCharactersPerSecond;
    private bool skipTypewriter = false;
    private Coroutine currentTypewriterCoroutine;

    void Awake()
    {
        defaultCharactersPerSecond = charactersPerSecond;
        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null)
            canvasGroup = gameObject.AddComponent<CanvasGroup>();

        // Get the first child's RectTransform (the Panel) to animate that instead of Canvas
        if (transform.childCount > 0)
        {
            panelRectTransform = transform.GetChild(0).GetComponent<RectTransform>();
            if (panelRectTransform != null)
            {
                originalPanelScale = panelRectTransform.localScale;
            }
        }

        if (animateAppear)
        {
            // Start invisible/small if animating
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
                canvasGroup.blocksRaycasts = false; // Don't block raycasts when hidden
            }
            if (panelRectTransform != null)
            {
                panelRectTransform.localScale = Vector3.zero;
            }
        }
    }

    public void ApplyDialogSpeechSettings(DialogSpeechSettings settings)
    {
        dialogSpeech?.ApplySettings(settings);
        if (settings != null)
        {
            if (settings.typewriterCharsPerSecond > 0f)
            {
                charactersPerSecond = settings.typewriterCharsPerSecond;
            }
            else
            {
                float baseSeconds = Mathf.Max(settings.baseCharSeconds, 0.0001f);
                float speedMul = Mathf.Max(settings.speakerSpeedMul, 0.0001f);
                charactersPerSecond = Mathf.Max(1f, speedMul / baseSeconds);
            }
        }
        else
        {
            charactersPerSecond = defaultCharactersPerSecond;
        }
    }
    
    [SerializeField] private TextMeshProUGUI autoResizeText;
    [SerializeField] private RectTransform popupRect;
    [SerializeField] private Vector2 verticalPadding = new Vector2(0f, 20f);
    
    public CanvasGroup canvasGroup;
    public RectTransform panelRectTransform; // Animate the panel, not the canvas
    public Vector3 originalPanelScale;
    public string fullText = ""; // Store the complete text for typewriter effect
    
    // Player interaction state
    private bool waitingForPlayerResponse = false;
    private bool playerRespondedYes = false;
    private System.Action<bool> playerResponseCallback;
    
    void LateUpdate()
    {
        if (autoResizeText == null || popupRect == null) return;

        // Measure how tall the text wants to be for its current width
        float preferredHeight = autoResizeText.GetPreferredValues(autoResizeText.text, popupRect.rect.width, 0f).y;

        // Only expand upward, never shrink below original height
        float targetHeight = Mathf.Max(preferredHeight + verticalPadding.y, popupRect.sizeDelta.y);

        if (!Mathf.Approximately(popupRect.sizeDelta.y, targetHeight))
        {
            popupRect.sizeDelta = new Vector2(popupRect.sizeDelta.x, targetHeight);
        }
    }
    
    public void SetText(string text)
    {
        // Ensure the GameObject is active so coroutines can run
        if (!gameObject.activeSelf)
        {
            gameObject.SetActive(true);
        }

        fullText = text;
        skipTypewriter = false; // Reset skip flag when setting new text

        // If typewriter is disabled, just set the text immediately
        if (!enableTypewriter)
        {
            if (tmpText != null)
            {
                tmpText.text = text;
            }
            else if (legacyText != null)
            {
                legacyText.text = text;
            }
            else
            {
#if UNITY_EDITOR
                Debug.LogWarning("DialoguePopup: No text component assigned!");
#endif
            }
        }
        else
        {
            // Start with empty text - typewriter will reveal it
            if (tmpText != null)
            {
                tmpText.text = "";
            }
            else if (legacyText != null)
            {
                legacyText.text = "";
            }
        }
    }

    /// <summary>
    /// Skip the typewriter effect and show the full text immediately
    /// </summary>
    public void SkipTypewriter()
    {
        skipTypewriter = true;

        // Stop dialog speech audio
        if (dialogSpeech != null)
        {
            var audioSource = dialogSpeech.GetComponent<AudioSource>();
            if (audioSource != null)
            {
                audioSource.Stop();
            }
        }
    }
    
    public void ShowForDuration(float duration)
    {
        StopAllCoroutines();
        StartCoroutine(ShowRoutine(duration));
    }
    
    /// <summary>
    /// Show dialogue from DialogueData with a callback when complete
    /// </summary>
    public void ShowDialogue(DialogueData dialogue, System.Action onComplete = null, float displayDuration = 2f)
    {
        if (dialogue == null)
        {
#if UNITY_EDITOR
            Debug.LogWarning("[DialoguePopup] No dialogue data provided!");
#endif
            onComplete?.Invoke();
            return;
        }
        
        string line = dialogue.GetRandomLine();
        SetText(line);
        
        StopAllCoroutines();
        
        // Check if this is a player interactable dialogue
        if (dialogue.isPlayerInteractable)
        {
            StartCoroutine(ShowPlayerInteractableDialogueRoutine(onComplete));
        }
        else
        {
            StartCoroutine(ShowDialogueRoutine(displayDuration, onComplete));
        }
    }
    
    /// <summary>
    /// Show dialogue that stays visible indefinitely (no auto-hide, no callback)
    /// Use HideDialogue() to manually hide it later
    /// </summary>
    public void ShowDialoguePersistent(DialogueData dialogue)
    {
        if (dialogue == null)
        {
#if UNITY_EDITOR
            Debug.LogWarning("[DialoguePopup] No dialogue data provided!");
#endif
            return;
        }

        string line = dialogue.GetRandomLine();
        SetText(line);
        
        StopAllCoroutines();
        StartCoroutine(ShowDialoguePersistentRoutine());
    }
    
    /// <summary>
    /// Manually hide the dialogue popup (for use with ShowDialoguePersistent)
    /// </summary>
    public void HideDialogue()
    {
        StopAllCoroutines();
        StartCoroutine(HideDialogueRoutine());
    }
    
    IEnumerator ShowDialoguePersistentRoutine()
    {
        // Animate appearing
        if (animateAppear)
        {
            yield return StartCoroutine(AnimateAppear());
        }
        
        // Typewriter effect
        if (enableTypewriter && !string.IsNullOrEmpty(fullText))
        {
            yield return StartCoroutine(TypewriterEffect());
        }
        
        // Stay visible indefinitely - no timer, no callback, no fade out
        // The caller must manually call HideDialogue() when ready
    }
    
    IEnumerator HideDialogueRoutine()
    {
        // Animate disappearing
        if (animateAppear)
        {
            yield return StartCoroutine(AnimateDisappear());
        }
        
        // Wait after fade out
        yield return new WaitForSeconds(delayAfterFadeOut);
        
        // Deactivate the popup
        gameObject.SetActive(false);
    }
    
    /// <summary>
    /// Show dialogue with Yes/No choices and wait for player response
    /// </summary>
    public void ShowDialogueWithChoices(DialogueData dialogue, System.Action<bool> onPlayerResponse)
    {
        if (dialogue == null)
        {
#if UNITY_EDITOR
            Debug.LogWarning("[DialoguePopup] No dialogue data provided!");
#endif
            onPlayerResponse?.Invoke(false);
            return;
        }
        
        string line = dialogue.GetRandomLine();
        SetText(line);
        
        StopAllCoroutines();
        StartCoroutine(ShowPlayerInteractableDialogueRoutine(onPlayerResponse));
    }
    
    IEnumerator ShowPlayerInteractableDialogueRoutine(System.Action onCompleteSimple)
    {
        // Convert simple callback to response callback
        System.Action<bool> responseCallback = (response) => onCompleteSimple?.Invoke();
        yield return StartCoroutine(ShowPlayerInteractableDialogueRoutine(responseCallback));
    }
    
    IEnumerator ShowPlayerInteractableDialogueRoutine(System.Action<bool> onPlayerResponse)
    {
        playerResponseCallback = onPlayerResponse;
        waitingForPlayerResponse = true;
        
        // Hide choices initially and ensure alpha is 0
        CanvasGroup choicesCanvasGroup = null;
        if (dialogueChoices != null)
        {
            dialogueChoices.SetActive(false);
            choicesCanvasGroup = dialogueChoices.GetComponent<CanvasGroup>();
            if (choicesCanvasGroup != null)
            {
                choicesCanvasGroup.alpha = 0f;
            }
        }
        
        // Animate appearing
        if (animateAppear)
        {
            yield return StartCoroutine(AnimateAppear());
        }
        
        // Typewriter effect
        if (enableTypewriter && !string.IsNullOrEmpty(fullText))
        {
            yield return StartCoroutine(TypewriterEffect());
        }
        
        // Show dialogue choices after typing completes and fade them in
        if (dialogueChoices != null)
        {
            dialogueChoices.SetActive(true);
            
            // Fade in the choices if there's a canvas group
            if (choicesCanvasGroup != null)
            {
                yield return StartCoroutine(FadeInChoices(choicesCanvasGroup));
            }
        }
        else
        {
#if UNITY_EDITOR
            Debug.LogWarning("[DialoguePopup] DialogueChoices GameObject not assigned! Cannot show Yes/No options.");
#endif
        }
        
        // Wait for player to respond
        yield return new WaitUntil(() => !waitingForPlayerResponse);
        
        // Fade out and hide choices
        if (dialogueChoices != null)
        {
            if (choicesCanvasGroup != null)
            {
                yield return StartCoroutine(FadeOutChoices(choicesCanvasGroup));
            }
            dialogueChoices.SetActive(false);
        }
        
        // Animate disappearing
        if (animateAppear)
        {
            yield return StartCoroutine(AnimateDisappear());
        }
        
        // Wait after fade out
        yield return new WaitForSeconds(delayAfterFadeOut);
        
        // Invoke callback with player's response
        playerResponseCallback?.Invoke(playerRespondedYes);
    }
    
    IEnumerator ShowDialogueRoutine(float duration, System.Action onComplete)
    {
        yield return StartCoroutine(ShowRoutine(duration));
        onComplete?.Invoke();
    }
    
    /// <summary>
    /// Gets the total time this dialogue will be visible (fade in + typing time + display duration + fade out + delay)
    /// </summary>
    public float GetTotalDisplayTime(float displayDuration)
    {
        float totalTime = 0f;

        // Add fade in animation time
        if (animateAppear)
        {
            totalTime += animationDuration;
        }

        // Add typing time
        if (enableTypewriter && !string.IsNullOrEmpty(fullText))
        {
            // If typewriter was skipped, use shorter duration
            if (skipTypewriter)
            {
                totalTime += skippedDisplayDuration;
            }
            else
            {
                // Base typing time
                float typingTime = fullText.Length / charactersPerSecond;

                // Add punctuation pauses
                int punctuationCount = 0;
                int ellipsisCount = 0;

                for (int i = 0; i < fullText.Length; i++)
                {
                    char c = fullText[i];

                    // Count exclamation marks
                    if (c == '!')
                    {
                        punctuationCount++;
                    }
                    // Count periods, but handle ellipsis specially
                    else if (c == '.')
                    {
                        // Check if this is part of an ellipsis
                        if (i >= 2 && fullText[i - 1] == '.' && fullText[i - 2] == '.')
                        {
                            // This is the third dot in "..."
                            ellipsisCount++;
                        }
                        else if (i >= 1 && fullText[i - 1] == '.')
                        {
                            // This is the second dot - skip
                        }
                        else if (i < fullText.Length - 1 && fullText[i + 1] == '.')
                        {
                            // This is the first dot and more follow - skip
                        }
                        else
                        {
                            // Regular period
                            punctuationCount++;
                        }
                    }
                }

                // Add pause time (punctuation pauses + ellipsis pauses)
                typingTime += (punctuationCount + ellipsisCount) * punctuationPauseDuration;
                totalTime += typingTime;
            }
        }

        // Add display duration (use shorter if skipped)
        float actualDisplayDuration = skipTypewriter ? skippedDisplayDuration : displayDuration;
        totalTime += actualDisplayDuration;

        // Add fade out animation time
        if (animateAppear)
        {
            float fadeDuration = animationDuration * 0.5f;
            totalTime += fadeDuration;
        }

        // Add delay after fade out
        totalTime += delayAfterFadeOut;

        return totalTime;
    }
    
    IEnumerator ShowRoutine(float duration)
    {
        // Ensure we start invisible
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
        }
        if (panelRectTransform != null)
        {
            panelRectTransform.localScale = Vector3.zero;
        }

        // Animate appearing
        if (animateAppear)
        {
            yield return StartCoroutine(AnimateAppear());
        }

        // Typewriter effect
        if (enableTypewriter && !string.IsNullOrEmpty(fullText))
        {
            yield return StartCoroutine(TypewriterEffect());
        }

        // Wait for duration AFTER typing is complete
        // Use shorter duration if typewriter was skipped
        float displayDuration = skipTypewriter ? skippedDisplayDuration : duration;
        yield return new WaitForSeconds(displayDuration);

        // Animate disappearing
        if (animateAppear)
        {
            yield return StartCoroutine(AnimateDisappear());
        }

        // Wait after fade out before next dialogue can appear
        yield return new WaitForSeconds(delayAfterFadeOut);
    }
    
    public IEnumerator AnimateAppear()
    {
        float elapsed = 0f;

        // Enable raycasts when appearing
        if (canvasGroup != null)
        {
            canvasGroup.blocksRaycasts = true;
        }

        while (elapsed < animationDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / animationDuration;

            // Ease out curve
            float eased = 1f - Mathf.Pow(1f - t, 3f);

            if (canvasGroup != null)
            {
                canvasGroup.alpha = eased;
            }

            if (panelRectTransform != null)
            {
                // Animate panel scale, not canvas scale
                panelRectTransform.localScale = originalPanelScale * eased;
            }

            yield return null;
        }

        // Ensure final values
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f;
        }
        if (panelRectTransform != null)
        {
            panelRectTransform.localScale = originalPanelScale;
        }
    }
    
    IEnumerator TypewriterEffect()
    {
        if (string.IsNullOrEmpty(fullText))
            yield break;

        int totalCharacters = fullText.Length;
        float timePerCharacter = 1f / charactersPerSecond;
        int currentCharacter = 0;

        while (currentCharacter <= totalCharacters)
        {
            // Check if skip was requested
            if (skipTypewriter)
            {
                // Show full text immediately
                if (tmpText != null)
                {
                    tmpText.text = fullText;
                }
                else if (legacyText != null)
                {
                    legacyText.text = fullText;
                }
                yield break;
            }

            string visibleText = fullText.Substring(0, currentCharacter);

            if (tmpText != null)
            {
                tmpText.text = visibleText;
            }
            else if (legacyText != null)
            {
                legacyText.text = visibleText;
            }

            if (dialogSpeech && currentCharacter > 0)
            {
                char ch = fullText[currentCharacter - 1];
                if (char.IsLetter(ch))
                {
                    dialogSpeech.PlayForChar(ch);
                }
            }
            
            // Check if we just revealed a punctuation character
            if (currentCharacter > 0 && currentCharacter <= totalCharacters)
            {
                char lastChar = fullText[currentCharacter - 1];
                
                // Check for ellipsis (...) - only pause once at the end
                if (lastChar == '.' && currentCharacter >= 3)
                {
                    // Check if this is part of an ellipsis
                    bool isEllipsis = fullText[currentCharacter - 1] == '.' &&
                                     currentCharacter >= 2 && fullText[currentCharacter - 2] == '.' &&
                                     currentCharacter >= 3 && fullText[currentCharacter - 3] == '.';
                    
                    if (isEllipsis)
                    {
                        // This is the third dot in "..." - pause here
                        yield return new WaitForSeconds(timePerCharacter);
                        yield return new WaitForSeconds(punctuationPauseDuration);
                        currentCharacter++;
                        continue;
                    }
                    else if (currentCharacter >= 2 && fullText[currentCharacter - 2] == '.')
                    {
                        // This is the second dot - don't pause, it's part of ellipsis
                        currentCharacter++;
                        yield return new WaitForSeconds(timePerCharacter);
                        continue;
                    }
                    else if (currentCharacter < totalCharacters && fullText[currentCharacter] == '.')
                    {
                        // This is the first dot and more dots follow - don't pause yet
                        currentCharacter++;
                        yield return new WaitForSeconds(timePerCharacter);
                        continue;
                    }
                    else
                    {
                        // Regular period - pause
                        yield return new WaitForSeconds(timePerCharacter);
                        yield return new WaitForSeconds(punctuationPauseDuration);
                        currentCharacter++;
                        continue;
                    }
                }
                else if (lastChar == '!')
                {
                    // Pause after exclamation mark
                    yield return new WaitForSeconds(timePerCharacter);
                    yield return new WaitForSeconds(punctuationPauseDuration);
                    currentCharacter++;
                    continue;
                }
            }
            
            currentCharacter++;
            yield return new WaitForSeconds(timePerCharacter);
        }
        
        // Ensure full text is visible
        if (tmpText != null)
        {
            tmpText.text = fullText;
        }
        else if (legacyText != null)
        {
            legacyText.text = fullText;
        }
    }
    
    IEnumerator AnimateDisappear()
    {
        float elapsed = 0f;
        float fadeDuration = animationDuration * 0.5f; // Faster fade out

        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = 1f - (elapsed / fadeDuration);

            if (canvasGroup != null)
            {
                canvasGroup.alpha = t;
            }

            yield return null;
        }

        // Disable raycasts when fully hidden
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = false;
        }
    }
    
    // -------------------------------
    // Player Response Methods
    // -------------------------------
    
    /// <summary>
    /// Call this from Yes button's OnClick event
    /// </summary>
    public void OnPlayerRespondYes()
    {
        if (waitingForPlayerResponse)
        {
            playerRespondedYes = true;
            waitingForPlayerResponse = false;
#if UNITY_EDITOR
            Debug.Log("[DialoguePopup] Player responded: YES");
#endif
        }
    }
    
    /// <summary>
    /// Call this from No button's OnClick event
    /// </summary>
    public void OnPlayerRespondNo()
    {
        if (waitingForPlayerResponse)
        {
            playerRespondedYes = false;
            waitingForPlayerResponse = false;
#if UNITY_EDITOR
            Debug.Log("[DialoguePopup] Player responded: NO");
#endif
        }
    }
    
    // -------------------------------
    // Dialogue Choices Fade Animations
    // -------------------------------
    
    /// <summary>
    /// Fade in the dialogue choices canvas group
    /// </summary>
    IEnumerator FadeInChoices(CanvasGroup choicesCanvasGroup, float duration = 0.3f)
    {
        float elapsed = 0f;
        
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            // Ease out
            float eased = 1f - Mathf.Pow(1f - t, 2f);
            choicesCanvasGroup.alpha = eased;
            yield return null;
        }
        
        choicesCanvasGroup.alpha = 1f;
    }
    
    /// <summary>
    /// Fade out the dialogue choices canvas group
    /// </summary>
    IEnumerator FadeOutChoices(CanvasGroup choicesCanvasGroup, float duration = 0.2f)
    {
        float elapsed = 0f;
        float startAlpha = choicesCanvasGroup.alpha;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            choicesCanvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, t);
            yield return null;
        }

        choicesCanvasGroup.alpha = 0f;
    }

    // -------------------------------
    // Click Detection for Skipping
    // -------------------------------

    /// <summary>
    /// Handle click on dialogue box to skip typewriter
    /// Note: Requires a Collider2D component on the dialogue box
    /// </summary>
    void OnMouseDown()
    {
        if (enableTypewriter && !string.IsNullOrEmpty(fullText))
        {
            SkipTypewriter();
        }
    }
}

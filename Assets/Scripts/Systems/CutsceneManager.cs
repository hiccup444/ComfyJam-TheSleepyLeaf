using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using JamesKJamKit.Services.Audio;

public class CutsceneManager : MonoBehaviour
{
    [Header("Cutscene Images")]
    [SerializeField] private Image cutsceneImage1;
    [SerializeField] private Image cutsceneImage2;
    [SerializeField] private Image cutsceneImage3;
    [SerializeField] private Image cutsceneImage4;
    [SerializeField] private Image cutsceneImage5;
    
    [Header("Image 5 Swap Settings")]
    [SerializeField] private Sprite cutsceneImage5Alternate1;
    [SerializeField] private Sprite cutsceneImage5Alternate2;
    [SerializeField] private float image5SwapDelay = 0.25f;
    [SerializeField] private float image5FadeDuration = 3f;

    [Header("Audio")]
    [Tooltip("SFX event to play when final sprite swap happens (sign opening)")]
    [SerializeField] private JamesKJamKit.Services.Audio.SFXEvent signOpenSfx;
    
    [Header("Timing Settings (Auto-Calculated)")]
    [Tooltip("Base duration for fade transitions (will be scaled to fit total duration)")]
    [SerializeField] private float fadeOutDuration = 2.5f;
    [Tooltip("Base duration for Image3 pan animation (will be scaled to fit total duration)")]
    [SerializeField] private float image3PanDuration = 7.5f;

    [Header("Image 3 Animation Settings")]
    [SerializeField] private float targetYPosition = 108f;

    [Header("Image 4 Animation Settings")]
    [SerializeField] private float image4PanDuration = 3.2f;
    [SerializeField] private float image4PauseDuration = 1.3f;
    [SerializeField] private float image4ZoomDuration = 2.029f;

    [Header("Image 5 Animation Settings")]
    [SerializeField] private float image5PauseBeforeSwap = 1f;
    [SerializeField] private float image4TargetScale = 1.8f;
    [SerializeField] private float image4TargetX = -580f;
    [SerializeField] private float image4TargetY = 0f;
    
    [Header("Cutscene Canvas")]
    [SerializeField] private GameObject cutsceneCanvas;
    
    [Header("Timing Settings")]
    [Tooltip("Target total duration for the cutscene (used as fallback if MusicDirector audio unavailable)")]
    [SerializeField] private float targetCutsceneDuration = 15f;
    [Tooltip("If true, automatically scales all timing to fit actual audio length from MusicDirector")]
    [SerializeField] private bool autoScaleTiming = true;
    
    private bool isCutscenePlaying = false;

    public bool IsCutscenePlaying => isCutscenePlaying;
    
    // Calculated timing values
    private float calculatedFadeOutDuration;
    private float calculatedImage3PanDuration;
    private float calculatedImage4PanDuration;
    private float calculatedImage4PauseDuration;
    private float calculatedImage4ZoomDuration;
    private float calculatedImage5PauseBeforeSwap;
    private float calculatedImage5FadeDuration;
    private bool _handlingPauseDuringCutscene;
    
    void Start()
    {
        // cutscene entry conditions
        bool hasPlayed = GameManager.Instance != null && GameManager.Instance.HasPlayedIntroCutscene();
        var md = JamesKJamKit.Services.MusicDirector.Instance;
        var mdState = md != null ? md.CurrentState.ToString() : "<no MusicDirector>";
        var mdClip = (md != null && md.ActiveSource != null && md.ActiveSource.clip != null) ? md.ActiveSource.clip.name : "NULL";
        var mdPlaying = (md != null && md.ActiveSource != null) && md.ActiveSource.isPlaying;
#if UNITY_EDITOR
        Debug.Log($"[CutsceneManager] Start() | HasPlayedIntroCutscene={hasPlayed} | MusicDirectorState={mdState} | ActiveClip={mdClip} | isPlaying={mdPlaying}");
#endif
        // Check if we should play the cutscene
        if (GameManager.Instance != null && !GameManager.Instance.HasPlayedIntroCutscene())
        {
            StartCutscene();
        }
        else
        {
            // Hide cutscene canvas if already played
            if (cutsceneCanvas != null)
            {
                cutsceneCanvas.SetActive(false);
            }
            
            // Start the game immediately if cutscene was already played
            if (GameManager.Instance != null && !GameManager.Instance.HasGameStarted())
            {
                GameManager.Instance.StartGame();
            }
        }
    }

    private void OnEnable()
    {
        if (JamesKJamKit.Services.PauseController.Instance != null)
        {
            JamesKJamKit.Services.PauseController.Instance.OnPauseChanged += HandlePauseChangedDuringCutscene;
        }
    }

    private void OnDisable()
    {
        if (JamesKJamKit.Services.PauseController.Instance != null)
        {
            JamesKJamKit.Services.PauseController.Instance.OnPauseChanged -= HandlePauseChangedDuringCutscene;
        }
    }

    private void CalculateTimings()
    {
        if (!autoScaleTiming)
        {
            calculatedFadeOutDuration = fadeOutDuration;
            calculatedImage3PanDuration = image3PanDuration;
            calculatedImage4PanDuration = image4PanDuration;
            calculatedImage4PauseDuration = image4PauseDuration;
            calculatedImage4ZoomDuration = image4ZoomDuration;
            calculatedImage5PauseBeforeSwap = image5PauseBeforeSwap;
            calculatedImage5FadeDuration = image5FadeDuration;
            return;
        }

        // Try to get the actual audio clip length from MusicDirector
        float actualAudioLength = targetCutsceneDuration;
        if (JamesKJamKit.Services.MusicDirector.Instance != null &&
            JamesKJamKit.Services.MusicDirector.Instance.ActiveSource != null &&
            JamesKJamKit.Services.MusicDirector.Instance.ActiveSource.clip != null)
        {
            actualAudioLength = JamesKJamKit.Services.MusicDirector.Instance.ActiveSource.clip.length;
#if UNITY_EDITOR
            Debug.Log($"[CutsceneManager] Using MusicDirector audio length: {actualAudioLength:F3}s");
#endif
        }
        else
        {
#if UNITY_EDITOR
            Debug.LogWarning($"[CutsceneManager] Could not get audio length from MusicDirector, using target duration: {targetCutsceneDuration:F3}s");
#endif
        }

        // Calculate the original total duration for the parts that sync with audio
        // (everything EXCEPT the final fade and Image5 pause/swaps - those happen after audio ends)
        // Note: Image4 pan overlaps with Image3 fade, so we don't add it to the timeline
        // Timeline: Image1 → Image2 → Image3 (Image4 pan overlaps during Image3) → Image4 pause → Image4 zoom → [AUDIO ENDS]
        float originalAudioSyncedDuration =
            fadeOutDuration +                    // Image 1 fade (2.5s)
            fadeOutDuration +                    // Image 2 fade (2.5s)
            image3PanDuration +                  // Image 3 pan/fade (7.5s) - Image4 pan happens during this
            image4PauseDuration +                // Image 4 pause after pan completes (0s)
            image4ZoomDuration;                  // Image 4 zoom (1.529s)
                                                 // Total: 2.5 + 2.5 + 7.5 + 0 + 1.529 = 14.029s
                                                 // NOTE: Image4 starts at 40% through Image3 = 5 + 3 = 8s
                                                 // NOTE: Zoom starts at 8 + 4.5 + 0 = 12.5s
                                                 // NOTE: Image 5 pause/swaps/fade play after audio ends

        // Calculate scale factor to fit actual audio length
        float scaleFactor = actualAudioLength / originalAudioSyncedDuration;

        // Apply scale factor to all durations EXCEPT Image5 pause, swaps, and final fade
        calculatedFadeOutDuration = fadeOutDuration * scaleFactor;
        calculatedImage3PanDuration = image3PanDuration * scaleFactor;
        calculatedImage4PanDuration = image4PanDuration * scaleFactor;
        calculatedImage4PauseDuration = image4PauseDuration * scaleFactor;
        calculatedImage4ZoomDuration = image4ZoomDuration * scaleFactor;

        // Image5 pause and final fade are NOT scaled - they play after the audio ends
        calculatedImage5PauseBeforeSwap = image5PauseBeforeSwap;
        calculatedImage5FadeDuration = image5FadeDuration;

#if UNITY_EDITOR
        Debug.Log($"[CutsceneManager] Timing scaled by {scaleFactor:F3}x to fit {actualAudioLength:F3}s audio duration");
        Debug.Log($"[CutsceneManager] Original synced duration: {originalAudioSyncedDuration:F3}s → Actual audio: {actualAudioLength:F3}s");
        Debug.Log($"[CutsceneManager] Image4 zoom starts at: {(fadeOutDuration + fadeOutDuration + image3PanDuration * 0.4 + image4PanDuration + image4PauseDuration) * scaleFactor:F3}s (target: 12.5s)");
        Debug.Log($"[CutsceneManager] Image5 pause ({calculatedImage5PauseBeforeSwap:F3}s) and final fade ({calculatedImage5FadeDuration:F3}s) play after audio ends");
#endif
    }
    
    public void StartCutscene()
    {
        if (isCutscenePlaying) return;
        
        isCutscenePlaying = true;

        // before sequence begins
        var md = JamesKJamKit.Services.MusicDirector.Instance;
        var mdState = md != null ? md.CurrentState.ToString() : "<no MusicDirector>";
        bool expectCutsceneState = md != null && mdState == JamesKJamKit.Services.MusicDirector.MusicState.Cutscene.ToString();
#if UNITY_EDITOR
        Debug.Log($"[CutsceneManager] StartCutscene() | HasPlayedIntroCutscene={GameManager.Instance != null && GameManager.Instance.HasPlayedIntroCutscene()} | MusicDirectorState={mdState} | IsCutsceneStateExpected={expectCutsceneState}");
#endif
        
        // Calculate timings based on MusicDirector's active audio clip
        CalculateTimings();
        
        // Ensure canvas is active
        if (cutsceneCanvas != null)
        {
            cutsceneCanvas.SetActive(true);
        }
        
        // Initialize all images to full alpha
        SetImageAlpha(cutsceneImage1, 1f);
        SetImageAlpha(cutsceneImage2, 1f);
        SetImageAlpha(cutsceneImage3, 1f);
        SetImageAlpha(cutsceneImage4, 1f);
        SetImageAlpha(cutsceneImage5, 1f);
        
        // Reset Image 3 transform (keep initial scale of 1.2, reset position to Y=0)
        if (cutsceneImage3 != null)
        {
            RectTransform rectTransform = cutsceneImage3.GetComponent<RectTransform>();
            if (rectTransform != null)
            {
                rectTransform.localScale = Vector3.one * 1.2f;
                rectTransform.anchoredPosition = new Vector2(rectTransform.anchoredPosition.x, 0f);
            }
        }

        // Reset Image 4 transform (scale 1.2, position Y=-108)
        if (cutsceneImage4 != null)
        {
            RectTransform rectTransform = cutsceneImage4.GetComponent<RectTransform>();
            if (rectTransform != null)
            {
                rectTransform.localScale = Vector3.one * 1.2f;
                rectTransform.anchoredPosition = new Vector2(rectTransform.anchoredPosition.x, -108f);
            }
        }

#if UNITY_EDITOR
        Debug.Log("Starting intro cutscene...");
#endif
        StartCoroutine(PlayCutsceneSequence());
    }


    private IEnumerator PlayCutsceneSequence()
    {
        float startTime = Time.time;

        // Phase 1: Fade out CutsceneImage1
#if UNITY_EDITOR
        Debug.Log($"[CutsceneManager] T+{Time.time - startTime:F3}s: Fading out CutsceneImage1 (duration: {calculatedFadeOutDuration:F3}s)");
#endif
        yield return StartCoroutine(FadeOutImage(cutsceneImage1, calculatedFadeOutDuration));
#if UNITY_EDITOR
        Debug.Log($"[CutsceneManager] T+{Time.time - startTime:F3}s: Image1 fade complete");
#endif

        // Phase 2: Fade out CutsceneImage2
#if UNITY_EDITOR
        Debug.Log($"[CutsceneManager] T+{Time.time - startTime:F3}s: Fading out CutsceneImage2 (duration: {calculatedFadeOutDuration:F3}s)");
#endif
        yield return StartCoroutine(FadeOutImage(cutsceneImage2, calculatedFadeOutDuration));
#if UNITY_EDITOR
        Debug.Log($"[CutsceneManager] T+{Time.time - startTime:F3}s: Image2 fade complete");
#endif

        // Phase 3: Pan and fade out CutsceneImage3 (no scaling)
        // Image4 will start panning when Image3 becomes >50% faded (overlapping animation)
#if UNITY_EDITOR
        Debug.Log($"[CutsceneManager] T+{Time.time - startTime:F3}s: Animating CutsceneImage3 (duration: {calculatedImage3PanDuration:F3}s)");
#endif

        // Start Image3 animation and Image4 animation in parallel (Image4 starts during Image3 fade)
        Coroutine image3Coroutine = StartCoroutine(AnimateImage3(calculatedImage3PanDuration));

        // Wait until 40% through Image3 pan before starting Image4
        float image3FadeStartTime = calculatedImage3PanDuration * 0.4f;
#if UNITY_EDITOR
        Debug.Log($"[CutsceneManager] T+{Time.time - startTime:F3}s: Waiting {image3FadeStartTime:F3}s before starting Image4 pan (40% through Image3)");
#endif
        yield return new WaitForSeconds(image3FadeStartTime);

        // Start Image4 pan (this will run in parallel with Image3 fade)
        float image4StartTime = Time.time - startTime;
#if UNITY_EDITOR
        Debug.Log($"[CutsceneManager] T+{image4StartTime:F3}s: Starting CutsceneImage4 pan (panDuration: {calculatedImage4PanDuration:F3}s, pauseDuration: {calculatedImage4PauseDuration:F3}s, zoomDuration: {calculatedImage4ZoomDuration:F3}s)");
        float expectedZoomStart = image4StartTime + calculatedImage4PanDuration + calculatedImage4PauseDuration;
        Debug.Log($"[CutsceneManager] Image4 zoom expected to start at T+{expectedZoomStart:F3}s (target: 12.5s)");
#endif

        Coroutine image4Coroutine = StartCoroutine(AnimateImage4(
            calculatedImage4PanDuration,
            calculatedImage4PauseDuration,
            calculatedImage4ZoomDuration
        ));

        // Wait for Image3 to finish
        yield return image3Coroutine;
#if UNITY_EDITOR
        Debug.Log($"[CutsceneManager] T+{Time.time - startTime:F3}s: Image3 complete");
#endif

        // Wait for Image4 to finish (pan + pause + zoom)
        yield return image4Coroutine;
#if UNITY_EDITOR
        Debug.Log($"[CutsceneManager] T+{Time.time - startTime:F3}s: Image4 complete (zoom should have ended)");
#endif

        // Phase 4: Pause on Image5 before sprite swapping (audio ends here)
#if UNITY_EDITOR
        Debug.Log($"[CutsceneManager] T+{Time.time - startTime:F3}s: Pausing on Image5 for {image5PauseBeforeSwap:F3}s");
#endif
        yield return new WaitForSeconds(image5PauseBeforeSwap);

        // Phase 5: Swap CutsceneImage5 sprite (3 times with delays)
#if UNITY_EDITOR
        Debug.Log($"[CutsceneManager] T+{Time.time - startTime:F3}s: Animating CutsceneImage5 (swapping sprites)");
#endif
        yield return StartCoroutine(SwapImage5Sprites());
#if UNITY_EDITOR
        Debug.Log($"[CutsceneManager] T+{Time.time - startTime:F3}s: Image5 sprite swaps complete");
#endif

        // Phase 6: Pause for 1 second after final sprite swap
#if UNITY_EDITOR
        Debug.Log($"[CutsceneManager] T+{Time.time - startTime:F3}s: Pausing for 1 second after final sprite swap");
#endif
        yield return new WaitForSeconds(1f);

        // Audio should end here - STOP the current track and switch to game music BEFORE final fade
#if UNITY_EDITOR
        Debug.Log("Stopping cutscene audio and switching to game music...");
#endif
        if (JamesKJamKit.Services.MusicDirector.Instance != null)
        {
            // Stop the current audio source to prevent replay
            var activeSource = JamesKJamKit.Services.MusicDirector.Instance.ActiveSource;
            if (activeSource != null && activeSource.isPlaying)
            {
#if UNITY_EDITOR
                Debug.Log($"[CutsceneManager] Pre-handoff | ActiveClip={activeSource.clip?.name ?? "NULL"} | isPlaying={activeSource.isPlaying}");
#endif
                activeSource.Stop();
#if UNITY_EDITOR
                Debug.Log("[CutsceneManager] Stopped cutscene audio to prevent replay");
#endif
            }

            // Now switch to game music - this will start playing game music immediately
            JamesKJamKit.Services.MusicDirector.Instance.SetState(
                JamesKJamKit.Services.MusicDirector.MusicState.Game
            );
            var mdLocal = JamesKJamKit.Services.MusicDirector.Instance;
            if (mdLocal != null)
            {
                mdLocal.EnsureGamePlaylistAndClearHistory();
            }
            CleanupCutsceneAudio();
            var post = JamesKJamKit.Services.MusicDirector.Instance.ActiveSource;
#if UNITY_EDITOR
            Debug.Log($"[CutsceneManager] Handoff complete | MD.State={JamesKJamKit.Services.MusicDirector.Instance.CurrentState} | ActiveClip={post?.clip?.name ?? "NULL"} | isPlaying={(post!=null && post.isPlaying)}");
#endif
        }

        // Phase 7: Fade out CutsceneImage5 (game music plays during this)
#if UNITY_EDITOR
        Debug.Log("Fading out CutsceneImage5...");
#endif
        yield return StartCoroutine(FadeOutImage(cutsceneImage5, calculatedImage5FadeDuration));

        // Cutscene complete
#if UNITY_EDITOR
        Debug.Log("Cutscene complete!");
#endif
        OnCutsceneComplete();
    }
    
    private IEnumerator FadeOutImage(Image image, float duration)
    {
        if (image == null)
        {
#if UNITY_EDITOR
            Debug.LogWarning("Attempting to fade null image!");
#endif
            yield break;
        }

        float elapsedTime = 0f;
        float startAlpha = image.color.a;
        Color c;

        while (elapsedTime < duration)
        {
            elapsedTime += Time.unscaledDeltaTime;
            float t = elapsedTime / duration;

            // Fade alpha from current to 0
            float newAlpha = Mathf.Lerp(startAlpha, 0f, t);
            c = image.color;
            c.a = newAlpha;
            image.color = c;

            yield return null;
        }

        // Ensure final alpha is exactly 0
        c = image.color;
        c.a = 0f;
        image.color = c;
    }
    
    private IEnumerator AnimateImage3(float duration)
    {
        if (cutsceneImage3 == null)
        {
#if UNITY_EDITOR
            Debug.LogWarning("CutsceneImage3 is null!");
#endif
            yield break;
        }

        RectTransform rectTransform = cutsceneImage3.GetComponent<RectTransform>();
        if (rectTransform == null)
        {
#if UNITY_EDITOR
            Debug.LogWarning("CutsceneImage3 has no RectTransform!");
#endif
            yield break;
        }

        Vector2 startPosition = rectTransform.anchoredPosition;
        Vector2 targetPosition = new Vector2(startPosition.x, targetYPosition);
        float startAlpha = cutsceneImage3.color.a;
        Color c;

        // Pan down and fade out simultaneously (no scaling phase)
        float elapsedTime = 0f;
        while (elapsedTime < duration)
        {
            elapsedTime += Time.unscaledDeltaTime;
            float t = elapsedTime / duration;

            // Pan down to target Y position
            Vector2 newPosition = Vector2.Lerp(startPosition, targetPosition, t);
            rectTransform.anchoredPosition = newPosition;

            // Fade out alpha - only start fading after 60% of the pan is complete
            float fadeStartPoint = 0.6f; // Don't start fading until 60% through
            float fadeT = 0f;
            if (t > fadeStartPoint)
            {
                // Remap t from [fadeStartPoint, 1] to [0, 1] for the fade
                fadeT = (t - fadeStartPoint) / (1f - fadeStartPoint);
            }
            float newAlpha = Mathf.Lerp(startAlpha, 0f, fadeT);
            c = cutsceneImage3.color;
            c.a = newAlpha;
            cutsceneImage3.color = c;

            yield return null;
        }

        // Ensure final values are set
        rectTransform.anchoredPosition = targetPosition;
        c = cutsceneImage3.color;
        c.a = 0f;
        cutsceneImage3.color = c;
    }
    
    private IEnumerator AnimateImage4(float panDuration, float pauseDuration, float zoomDuration)
    {
        if (cutsceneImage4 == null)
        {
#if UNITY_EDITOR
            Debug.LogWarning("CutsceneImage4 is null!");
#endif
            yield break;
        }

        RectTransform rectTransform = cutsceneImage4.GetComponent<RectTransform>();
        if (rectTransform == null)
        {
#if UNITY_EDITOR
            Debug.LogWarning("CutsceneImage4 has no RectTransform!");
#endif
            yield break;
        }

        // Phase 1: Pan from Y=-108 to Y=0 (seamless continuation from Image3)
#if UNITY_EDITOR
        Debug.Log($"[CutsceneManager][Image4] Starting PAN phase (duration: {panDuration:F3}s)");
#endif
        Vector2 panStartPosition = rectTransform.anchoredPosition;
        Vector2 panTargetPosition = new Vector2(panStartPosition.x, 0f);

        float elapsedTime = 0f;
        while (elapsedTime < panDuration)
        {
            elapsedTime += Time.unscaledDeltaTime;
            float t = elapsedTime / panDuration;

            Vector2 newPosition = Vector2.Lerp(panStartPosition, panTargetPosition, t);
            rectTransform.anchoredPosition = newPosition;

            yield return null;
        }

        // Ensure pan ends exactly at Y=0
        rectTransform.anchoredPosition = panTargetPosition;
#if UNITY_EDITOR
        Debug.Log($"[CutsceneManager][Image4] PAN phase complete");
#endif

        // Phase 2: Pause (stay still)
#if UNITY_EDITOR
        Debug.Log($"[CutsceneManager][Image4] Starting PAUSE phase (duration: {pauseDuration:F3}s)");
#endif
        yield return new WaitForSeconds(pauseDuration);
#if UNITY_EDITOR
        Debug.Log($"[CutsceneManager][Image4] PAUSE phase complete");
#endif

        // Phase 3: Zoom (scale to 1.8, move to X=-580, Y=0)
#if UNITY_EDITOR
        Debug.Log($"[CutsceneManager][Image4] Starting ZOOM phase (duration: {zoomDuration:F3}s)");
#endif
        Vector3 startScale = rectTransform.localScale;
        Vector3 targetScale = Vector3.one * image4TargetScale;
        Vector2 zoomStartPosition = rectTransform.anchoredPosition;
        Vector2 zoomTargetPosition = new Vector2(image4TargetX, image4TargetY);
        float startAlpha = cutsceneImage4.color.a;
        Color c;

        elapsedTime = 0f;
        while (elapsedTime < zoomDuration)
        {
            elapsedTime += Time.unscaledDeltaTime;
            float t = elapsedTime / zoomDuration;

            // Scale to target
            Vector3 newScale = Vector3.Lerp(startScale, targetScale, t);
            rectTransform.localScale = newScale;

            // Move to target position
            Vector2 newPosition = Vector2.Lerp(zoomStartPosition, zoomTargetPosition, t);
            rectTransform.anchoredPosition = newPosition;

            // Fade out near the end - only start fading after 80% of the zoom
            float fadeStartPoint = 0.8f;
            float fadeT = 0f;
            if (t > fadeStartPoint)
            {
                // Remap t from [fadeStartPoint, 1] to [0, 1] for the fade
                fadeT = (t - fadeStartPoint) / (1f - fadeStartPoint);
            }
            float newAlpha = Mathf.Lerp(startAlpha, 0f, fadeT);
            c = cutsceneImage4.color;
            c.a = newAlpha;
            cutsceneImage4.color = c;

            yield return null;
        }

        // Ensure final values are set
        rectTransform.localScale = targetScale;
        rectTransform.anchoredPosition = zoomTargetPosition;
        c = cutsceneImage4.color;
        c.a = 0f;
        cutsceneImage4.color = c;
#if UNITY_EDITOR
        Debug.Log($"[CutsceneManager][Image4] ZOOM phase complete");
#endif
    }
    
    private IEnumerator SwapImage5Sprites()
    {
        if (cutsceneImage5 == null)
        {
#if UNITY_EDITOR
            Debug.LogWarning("CutsceneImage5 is null!");
#endif
            yield break;
        }

        if (cutsceneImage5Alternate1 == null || cutsceneImage5Alternate2 == null)
        {
#if UNITY_EDITOR
            Debug.LogWarning("CutsceneImage5 alternate sprites are not assigned!");
#endif
        }
        else
        {
            // Image 1 is already showing (default)
#if UNITY_EDITOR
            Debug.Log("Showing Image 1 (default)...");
#endif
            yield return new WaitForSeconds(image5SwapDelay);

            // First swap: switch to alternate sprite 1 (Image 2)
#if UNITY_EDITOR
            Debug.Log("Swapping to Image 2 (swap 1)...");
#endif
            cutsceneImage5.sprite = cutsceneImage5Alternate1;
            yield return new WaitForSeconds(image5SwapDelay);

            // Second swap: switch to alternate sprite 2 (Image 3) - play sign open sound
#if UNITY_EDITOR
            Debug.Log("Swapping to Image 3 (swap 2)...");
#endif
            cutsceneImage5.sprite = cutsceneImage5Alternate2;

            // Play sign opening sound effect
            if (signOpenSfx != null)
            {
                JamesKJamKit.Services.AudioManager.Instance?.PlaySFX(signOpenSfx, transform);
#if UNITY_EDITOR
                Debug.Log("[CutsceneManager] Playing sign open SFX on final sprite swap");
#endif
            }

            yield return new WaitForSeconds(image5SwapDelay);
        }
        
        // Audio ends here - ready to switch to game music
    }
    
    private IEnumerator AnimateImage5()
    {
        // Swap sprites first
        yield return StartCoroutine(SwapImage5Sprites());

        // Then fade out
#if UNITY_EDITOR
        Debug.Log("Fading out CutsceneImage5...");
#endif
        yield return StartCoroutine(FadeOutImage(cutsceneImage5, calculatedImage5FadeDuration));
    }
    
    private void SetImageAlpha(Image image, float alpha)
    {
        if (image == null) return;

        Color color = image.color;
        color.a = alpha;
        image.color = color;
    }
    
    private void OnCutsceneComplete()
    {
        isCutscenePlaying = false;
        
        // Belt-and-suspenders: ensure music base state is Game after cutscene
        var md = JamesKJamKit.Services.MusicDirector.Instance;
        if (md != null)
        {
            md.EnsureGamePlaylistAndClearHistory();
        }

        // Ensure the game is NOT paused when we hand off
        var pause = JamesKJamKit.Services.PauseController.Instance;
        if (pause != null)
        {
            pause.SetPaused(false);
        }
        
        // Mark cutscene as played in GameManager
        if (GameManager.Instance != null)
        {
            GameManager.Instance.MarkIntroCutscenePlayed();

            // Start the game now that cutscene is done - shop starts OPEN after cutscene
            if (!GameManager.Instance.HasGameStarted())
            {
                GameManager.Instance.StartGame(startWithShopOpen: true);
            }
        }
        
        // Hide or destroy the cutscene canvas
        if (cutsceneCanvas != null)
        {
            CleanupCutsceneAudio();
            // count AudioSources under canvas before hiding
            var sources = cutsceneCanvas.GetComponentsInChildren<UnityEngine.AudioSource>(true);
#if UNITY_EDITOR
            Debug.Log($"[CutsceneManager] OnCutsceneComplete() hiding canvas | AudioSourceCount={sources?.Length ?? 0}");
#endif
            cutsceneCanvas.SetActive(false);
        }
    }

    // Helper to build a friendly hierarchy path for logs only
    private static string GetHierarchyPath(Transform t)
    {
        if (t == null) return "<null>";
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        while (t != null)
        {
            if (sb.Length == 0) sb.Insert(0, t.name); else sb.Insert(0, t.name + "/");
            t = t.parent;
        }
        return sb.ToString();
    }
    
    private void HandlePauseChangedDuringCutscene(bool paused)
    {
        if (!isCutscenePlaying) return;
        if (!paused) return;
        if (_handlingPauseDuringCutscene) return;
        try
        {
            _handlingPauseDuringCutscene = true;
#if UNITY_EDITOR
            Debug.Log("[CutsceneManager] Pause requested during cutscene → preventing pause.");
#endif
            var pause = JamesKJamKit.Services.PauseController.Instance;
            if (pause != null)
            {
                pause.SetPaused(false);
            }
        }
        finally
        {
            _handlingPauseDuringCutscene = false;
        }
    }

    private void CleanupCutsceneAudio()
    {
        if (cutsceneCanvas == null) return;
        var sources = cutsceneCanvas.GetComponentsInChildren<UnityEngine.AudioSource>(true);
        foreach (var a in sources)
        {
            if (a == null) continue;
            a.Stop();
            a.clip = null;
            a.playOnAwake = false;
            a.enabled = false;
        }
    }
    
    // Debug method to skip cutscene
    [ContextMenu("Skip Cutscene")]
    public void SkipCutscene()
    {
        if (isCutscenePlaying)
        {
            StopAllCoroutines();

            // Make sure we are not paused before handing off to gameplay
            var pause = JamesKJamKit.Services.PauseController.Instance;
            if (pause != null)
            {
                pause.SetPaused(false);
            }

            // Stop any cutscene audio and force switch to Game playlist/state
            var md = JamesKJamKit.Services.MusicDirector.Instance;
            if (md != null)
            {
                var activeSource = md.ActiveSource;
                if (activeSource != null && activeSource.isPlaying)
                {
#if UNITY_EDITOR
                    Debug.Log($"[CutsceneManager] SkipCutscene: Stopping active cutscene clip {activeSource.clip?.name ?? "NULL"}");
#endif
                    activeSource.Stop();
                }
                md.EnsureGamePlaylistAndClearHistory();
            }
            CleanupCutsceneAudio();
            OnCutsceneComplete();
        }
    }
    
    // Manual trigger for testing
    [ContextMenu("Play Cutscene")]
    public void PlayCutsceneManual()
    {
        StartCutscene();
    }
}

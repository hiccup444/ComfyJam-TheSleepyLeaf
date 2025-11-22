using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using Comfy.Camera;
using JamesKJamKit.Services;
using JamesKJamKit.Services.Audio;

[RequireComponent(typeof(Collider2D))]
public class ExaminableSprite2D : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] GameObject recipeGuideUI; // The UI GameObject (RecipeGuide) - parent for positioning
    [SerializeField] RectTransform recipeRectTransform; // RectTransform for positioning
    [SerializeField] CanvasGroup recipeImageCanvasGroup; // RecipeImage canvas group for fade control
    [SerializeField] CanvasGroup panelCanvasGroup; // Panel canvas group for fade control
    [SerializeField] float slideSpeed = 5f; // How fast it slides in/out
    [SerializeField] float fadeSpeed = 5f; // How fast it fades in/out
    [SerializeField] float panelFadeDelay = 0.2f; // Delay before panel starts fading in after bounce

    [Header("Camera")]
    [SerializeField] CameraComfortRuntime cameraRuntime;

    CameraEdgeStationSwitcher edgeSwitcher;

    [Header("Audio")]
    [SerializeField] SFXEvent sfxSlideIn; // Sound when RecipeImage slides in
    [SerializeField] SFXEvent sfxSlideOut; // Sound when RecipeImage slides out
    [SerializeField] float slideInSfxDelay = 0f; // Delay before playing slide in sound
    [SerializeField] float slideOutSfxDelay = 0f; // Delay before playing slide out sound

    bool isExamining = false;
    bool isClosing = false; // Track if we're animating the close
    bool isBouncing = false; // Track if we're in bounce phase
    bool bounceFinished = false; // Track if bounce animation is complete
    bool panelFadingOut = false; // Track if we're fading out panel before closing
    float bounceStartTime = 0f; // When the bounce started
    float panelFadeStartTime = 0f; // When panel fade started
    float slideInSfxStartTime = 0f; // When slide in started (for delayed SFX)
    float slideOutSfxStartTime = 0f; // When slide out started (for delayed SFX)
    bool slideInSfxPlayed = false; // Track if slide in SFX has been played
    bool slideOutSfxPlayed = false; // Track if slide out SFX has been played
    Vector3 hiddenPosition = new Vector3(0, 1005, 0); // Position when off-screen
    Vector3 visiblePosition = new Vector3(0, 0, 0); // Position when on-screen

    [Header("Bounce Settings")]
    [SerializeField] float bounceHeight = 50f; // How high to bounce (in UI units)
    [SerializeField] float bounceDuration = 0.4f; // How long the bounce lasts
    [SerializeField] float bounceFrequency = 2f; // How many bounces

    Camera cam;
    Collider2D col;
    
    void Awake()
    {
        col = GetComponent<Collider2D>();
        cam = Camera.main;

        // Auto-find camera runtime if not assigned
        if (cameraRuntime == null)
            cameraRuntime = FindFirstObjectByType<CameraComfortRuntime>();

        // Find the edge station switcher component
        if (cameraRuntime != null)
            edgeSwitcher = cameraRuntime.GetComponent<CameraEdgeStationSwitcher>();

        // Get components from recipeGuideUI if not assigned
        if (recipeGuideUI != null)
        {
            if (recipeRectTransform == null)
                recipeRectTransform = recipeGuideUI.GetComponent<RectTransform>();

            // Auto-find canvas groups from children
            if (recipeImageCanvasGroup == null)
            {
                Transform recipeImageTransform = recipeGuideUI.transform.Find("RecipeImage");
                if (recipeImageTransform != null)
                    recipeImageCanvasGroup = recipeImageTransform.GetComponent<CanvasGroup>();
            }

            if (panelCanvasGroup == null)
            {
                Transform panelTransform = recipeGuideUI.transform.Find("Panel");
                if (panelTransform != null)
                    panelCanvasGroup = panelTransform.GetComponent<CanvasGroup>();
            }

            // Initialize to hidden state
            if (recipeRectTransform != null)
                recipeRectTransform.anchoredPosition = hiddenPosition;

            if (recipeImageCanvasGroup != null)
                recipeImageCanvasGroup.alpha = 0f;

            if (panelCanvasGroup != null)
                panelCanvasGroup.alpha = 0f;

            // Make sure it starts disabled
            if (!isExamining)
                recipeGuideUI.SetActive(false);
        }
    }
    
    void Update()
    {
        // Check for mouse clicks using new Input System
        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            if (isExamining)
            {
                // If already examining, check if we clicked on RecipeImage
                // Clicking anywhere else (including the Panel background) should exit
                if (!IsMouseOverRecipeImage())
                {
                    StopExamining();
                }
            }
            else
            {
                // Check if we're over the sprite first (before UI check)
                bool overSprite = IsMouseOverSprite();
                bool overUI = IsPointerOverUIElement();

                // If we clicked on the sprite, start examining (even if there's UI in the way)
                if (overSprite && !overUI)
                {
                    StartExamining();
                }
            }
        }

        // Allow ANY key press to exit examine mode
        if (isExamining)
        {
            if (Keyboard.current.anyKey.wasPressedThisFrame)
            {
                StopExamining();
            }
        }

        // Handle delayed SFX playback for slide in
        if (isExamining && !slideInSfxPlayed && (Time.time - slideInSfxStartTime) >= slideInSfxDelay)
        {
            PlaySfx(sfxSlideIn);
            slideInSfxPlayed = true;
        }

        // Handle delayed SFX playback for slide out
        if (isClosing && !slideOutSfxPlayed && (Time.time - slideOutSfxStartTime) >= slideOutSfxDelay)
        {
            PlaySfx(sfxSlideOut);
            slideOutSfxPlayed = true;
        }

        // Animate the RecipeUI sliding in/out
        if (recipeRectTransform != null && recipeGuideUI != null && recipeGuideUI.activeSelf)
        {
            Vector3 targetPos;
            float recipeImageTargetAlpha;
            float panelTargetAlpha;
            float distanceToTarget = 0f;

            if (isExamining && !isClosing)
            {
                // OPENING ANIMATION
                // Sliding IN - move to visible position (0, 0, 0) and fade RecipeImage to alpha 1
                targetPos = visiblePosition;
                recipeImageTargetAlpha = 1f;

                // Check if we've reached the target and should start bouncing
                distanceToTarget = Vector3.Distance(recipeRectTransform.anchoredPosition, targetPos);

                // Only start bouncing if we're not already bouncing and we just arrived
                if (!isBouncing && !bounceFinished && distanceToTarget < 5f && distanceToTarget > 0.5f)
                {
                    isBouncing = true;
                    bounceStartTime = Time.time;
                    panelFadeStartTime = Time.time; // Start panel fade timer as soon as we reach position
                }

                // Panel fades in as soon as we reach Y=0 (even while bouncing)
                if (isBouncing || bounceFinished)
                {
                    panelTargetAlpha = 1f;
                }
                else
                {
                    panelTargetAlpha = 0f;
                }
            }
            else if (isClosing)
            {
                // CLOSING ANIMATION
                // First fade out Panel, then slide up and fade RecipeImage
                if (panelFadingOut)
                {
                    // Panel is fading out, don't move yet
                    targetPos = recipeRectTransform.anchoredPosition; // Stay in place
                    recipeImageTargetAlpha = 1f; // Keep RecipeImage visible
                    panelTargetAlpha = 0f; // Fade out Panel
                }
                else
                {
                    // Panel is done, slide up and fade RecipeImage
                    targetPos = hiddenPosition;
                    recipeImageTargetAlpha = 0f;
                    panelTargetAlpha = 0f;
                }

                isBouncing = false; // No bounce when closing
            }
            else
            {
                // Shouldn't reach here, but just in case
                targetPos = recipeRectTransform.anchoredPosition;
                recipeImageTargetAlpha = recipeImageCanvasGroup != null ? recipeImageCanvasGroup.alpha : 0f;
                panelTargetAlpha = panelCanvasGroup != null ? panelCanvasGroup.alpha : 0f;
            }

            // Recalculate distance for final check
            distanceToTarget = Vector3.Distance(recipeRectTransform.anchoredPosition, targetPos);

            // Lerp towards target position (only if not bouncing)
            Vector3 newPosition;
            if (!isBouncing)
            {
                newPosition = Vector3.Lerp(
                    recipeRectTransform.anchoredPosition,
                    targetPos,
                    slideSpeed * Time.deltaTime
                );
            }
            else
            {
                // While bouncing, lock to target position
                newPosition = targetPos;
            }

            // Apply bounce effect if we're bouncing
            if (isBouncing && isExamining)
            {
                float bounceTime = Time.time - bounceStartTime;
                if (bounceTime < bounceDuration)
                {
                    // Damped sine wave for bounce
                    float bounceProgress = bounceTime / bounceDuration;
                    float dampening = 1f - bounceProgress; // Goes from 1 to 0
                    float bounce = Mathf.Sin(bounceTime * Mathf.PI * bounceFrequency * 2f) * bounceHeight * dampening;
                    newPosition.y += bounce;
                }
                else
                {
                    isBouncing = false; // Bounce finished
                    bounceFinished = true; // Mark bounce as complete
                    panelFadeStartTime = Time.time; // Start panel fade timer
                }
            }

            recipeRectTransform.anchoredPosition = newPosition;

            // Fade in/out the RecipeImage canvas group
            if (recipeImageCanvasGroup != null)
            {
                recipeImageCanvasGroup.alpha = Mathf.Lerp(
                    recipeImageCanvasGroup.alpha,
                    recipeImageTargetAlpha,
                    fadeSpeed * Time.deltaTime
                );
            }

            // Fade in/out the Panel canvas group (with delay when opening)
            if (panelCanvasGroup != null)
            {
                // If opening and bounce just finished, apply delay
                if (isExamining && bounceFinished && (Time.time - panelFadeStartTime) >= panelFadeDelay)
                {
                    panelCanvasGroup.alpha = Mathf.Lerp(
                        panelCanvasGroup.alpha,
                        panelTargetAlpha,
                        fadeSpeed * Time.deltaTime
                    );
                }
                else if (!isExamining) // Closing, no delay
                {
                    panelCanvasGroup.alpha = Mathf.Lerp(
                        panelCanvasGroup.alpha,
                        panelTargetAlpha,
                        fadeSpeed * Time.deltaTime
                    );
                }
            }

            // Handle closing states
            if (isClosing)
            {
                if (panelFadingOut)
                {
                    // Check if panel has faded out
                    if (panelCanvasGroup != null && panelCanvasGroup.alpha < 0.05f)
                    {
                        panelFadingOut = false; // Panel done, start sliding
                    }
                }
                else
                {
                    // Check if slide and RecipeImage fade are complete
                    if (distanceToTarget < 5f && recipeImageCanvasGroup != null && recipeImageCanvasGroup.alpha < 0.05f)
                    {
                        recipeGuideUI.SetActive(false);
                        isClosing = false; // Reset closing flag
                    }
                }
            }
        }
    }
    
    void StartExamining()
    {
        isExamining = true;
        isBouncing = false; // Reset bounce flag
        bounceFinished = false; // Reset bounce finished flag
        isClosing = false; // Make sure closing is false
        panelFadingOut = false; // Reset panel fade out flag

        // Reset audio flags and start timer for slide in
        slideInSfxPlayed = false;
        slideInSfxStartTime = Time.time;

        // Activate and show the RecipeGuideUI
        if (recipeGuideUI != null && recipeRectTransform != null)
        {
            // Make sure it starts at the hidden position
            recipeRectTransform.anchoredPosition = hiddenPosition;

            // Make sure alphas start at 0
            if (recipeImageCanvasGroup != null)
                recipeImageCanvasGroup.alpha = 0f;

            if (panelCanvasGroup != null)
                panelCanvasGroup.alpha = 0f;

            recipeGuideUI.SetActive(true);
        }

        // Block camera controls by disabling the rig and clearing input
        if (cameraRuntime != null)
        {
            cameraRuntime.EnableRig(false);
            // Force clear any velocity/input immediately
            if (cameraRuntime.Rig != null)
            {
                cameraRuntime.Rig.ForceEnable();
            }
        }

        // Disable edge station switcher to prevent edge detection
        if (edgeSwitcher != null)
        {
            edgeSwitcher.enabled = false;
        }
    }

    void StopExamining()
    {
        isExamining = false;
        isClosing = true; // Set closing flag so animation continues
        panelFadingOut = true; // Start by fading out panel first
        bounceFinished = false; // Reset for next time

        // Reset audio flags and start timer for slide out
        slideOutSfxPlayed = false;
        slideOutSfxStartTime = Time.time;

        // Re-enable camera controls and clear any accumulated input
        if (cameraRuntime != null)
        {
            cameraRuntime.EnableRig(true);
            // Force clear any velocity/input that was accumulated while disabled
            if (cameraRuntime.Rig != null)
            {
                cameraRuntime.Rig.ForceEnable();
            }
        }

        // Re-enable edge station switcher
        if (edgeSwitcher != null)
        {
            edgeSwitcher.enabled = true;
        }
    }
    
    bool IsMouseOverSprite()
    {
        if (cam == null)
            return false;

        Vector2 mousePos = cam.ScreenToWorldPoint(Mouse.current.position.ReadValue());
        RaycastHit2D hit = Physics2D.Raycast(mousePos, Vector2.zero);

        return hit.collider != null && hit.collider.gameObject == gameObject;
    }

    bool IsMouseOverRecipeImage()
    {
        if (recipeImageCanvasGroup == null)
            return false;

        // First try UI raycast - check if we hit the RecipeImage specifically
        if (EventSystem.current != null)
        {
            var pointerData = new PointerEventData(EventSystem.current)
            {
                position = Mouse.current.position.ReadValue()
            };

            var results = new System.Collections.Generic.List<RaycastResult>();
            EventSystem.current.RaycastAll(pointerData, results);

            // Check if any of the raycasted UI elements are the RecipeImage or its children
            GameObject recipeImageObj = recipeImageCanvasGroup.gameObject;
            foreach (var result in results)
            {
                // Check if this result is the RecipeImage or a child of it
                Transform current = result.gameObject.transform;
                while (current != null)
                {
                    if (current.gameObject == recipeImageObj)
                    {
                        return true;
                    }
                    current = current.parent;
                }
            }
        }

        // Fallback: Check RectTransform bounds of RecipeImage only
        RectTransform recipeImageRect = recipeImageCanvasGroup.GetComponent<RectTransform>();
        if (recipeImageRect != null)
        {
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                recipeImageRect,
                Mouse.current.position.ReadValue(),
                null, // Overlay canvas
                out Vector2 localMousePosition))
            {
                return recipeImageRect.rect.Contains(localMousePosition);
            }
        }

        return false;
    }

    bool IsPointerOverUIElement()
    {
        if (EventSystem.current == null)
            return false;

        var pointerData = new PointerEventData(EventSystem.current)
        {
            position = Mouse.current.position.ReadValue()
        };

        var results = new System.Collections.Generic.List<RaycastResult>();
        EventSystem.current.RaycastAll(pointerData, results);

        bool overOtherUI = false;
        foreach (var result in results)
        {
            if (result.gameObject != gameObject)
            {
                overOtherUI = true;
                break;
            }
        }

        return overOtherUI;
    }

    void PlaySfx(SFXEvent evt)
    {
        if (evt == null) return;
        AudioManager.Instance?.PlaySFX(evt, transform);
    }
}
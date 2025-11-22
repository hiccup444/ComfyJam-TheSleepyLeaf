using UnityEngine;
using JamesKJamKit.Services;
using System.Collections;

namespace JamesKJamKit.UI
{
    /// <summary>
    /// Pause menu wrapper that reacts to pause state changes and exposes button hooks.
    /// </summary>
    public sealed class PauseMenu : MonoBehaviour
    {
        [SerializeField]
        private RectTransform pauseMenuRect;

        [SerializeField]
        private OptionsPanel optionsPanel;

        [SerializeField]
        private CanvasGroup pauseMenuCanvasGroup;

        [Header("Page Flip References")]
        [SerializeField]
        private RectTransform pageFlipRect;

        [SerializeField]
        private CanvasGroup pageFlipCanvasGroup;

        [SerializeField]
        private CanvasGroup pausePageCanvasGroup;

        [SerializeField]
        private CanvasGroup optionsPageCanvasGroup;

        [Header("Quit Confirmation")]
        [SerializeField]
        private GameObject quitConfirmation;

        [SerializeField]
        private CanvasGroup quitConfirmationCanvasGroup;

        [Header("Slide Animation Settings")]
        [Tooltip("Duration for pause menu to slide down (entry)")]
        [SerializeField]
        private float slideInDuration = 0.6f;

        [Tooltip("Duration for pause menu to slide up (exit)")]
        [SerializeField]
        private float slideOutDuration = 0.5f;

        [Tooltip("Animation curve for slide in (starts fast, slows down heavily at end)")]
        [SerializeField]
        private AnimationCurve slideInCurve = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 2f),     // Start: slow out tangent for smooth start
            new Keyframe(1f, 1f, 0.3f, 0f)    // End: gentle in tangent for heavy slowdown
        );

        [Tooltip("Animation curve for slide out (ease in-out reversed)")]
        [SerializeField]
        private AnimationCurve slideOutCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Page Flip Animation Settings")]
        [Tooltip("Duration for the entire flip animation")]
        [SerializeField]
        private float flipDuration = 0.8f;

        [Tooltip("Animation curve for the flip (applies to both halves)")]
        [SerializeField]
        private AnimationCurve flipCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        // Slide animation state
        private Vector3 hiddenPosition = new Vector3(0, 1000, 0); // Off-screen (above)
        private Vector3 visiblePosition = new Vector3(0, 0, 0); // On-screen
        private bool isAnimating = false;
        private bool isVisible = false;
        private float animationStartTime = 0f;
        private Vector3 animationStartPos = Vector3.zero;

        // Page flip state
        private readonly Vector2 startPosition = new Vector2(285, -94);
        private readonly Vector2 startSize = new Vector2(507, 672);
        private readonly Vector3 startScale = new Vector3(1, 1, 1);
        private readonly Vector2 midPosition = new Vector2(34, -94);
        private readonly Vector2 midSize = new Vector2(0, 672);
        private readonly Vector2 endPosition = new Vector2(-218, -94);
        private readonly Vector2 endSize = new Vector2(507, 672);
        private readonly Vector3 endScale = new Vector3(-1, 1, 1);

        private bool isFlipping = false;
        private bool isOnPausePage = true; // true = pause page, false = options page

        private void Awake()
        {
            // Initialize pause menu to hidden position
            if (pauseMenuRect != null)
            {
                pauseMenuRect.anchoredPosition = hiddenPosition;
            }

            // Disable interaction when hidden
            if (pauseMenuCanvasGroup != null)
            {
                pauseMenuCanvasGroup.interactable = false;
                pauseMenuCanvasGroup.blocksRaycasts = false;
            }

            // Initialize page flip (hidden until animation starts)
            if (pageFlipRect != null)
            {
                pageFlipRect.anchoredPosition = startPosition;
                pageFlipRect.sizeDelta = startSize;
                pageFlipRect.localScale = startScale;
            }

            if (pageFlipCanvasGroup != null)
            {
                pageFlipCanvasGroup.alpha = 0f;
            }

            // Initialize page visibility - pause page visible, options page hidden and disabled
            if (pausePageCanvasGroup != null)
            {
                pausePageCanvasGroup.alpha = 1f;
            }

            if (optionsPageCanvasGroup != null)
            {
                optionsPageCanvasGroup.alpha = 0f;
                optionsPageCanvasGroup.gameObject.SetActive(false);
            }

            // Initialize quit confirmation (hidden)
            if (quitConfirmation != null)
            {
                quitConfirmation.SetActive(false);
            }

            if (quitConfirmationCanvasGroup != null)
            {
                quitConfirmationCanvasGroup.alpha = 0f;
                quitConfirmationCanvasGroup.interactable = false;
                quitConfirmationCanvasGroup.blocksRaycasts = false;
            }

            isOnPausePage = true;
        }

        private void OnEnable()
        {
            if (PauseController.Instance != null)
            {
                PauseController.Instance.OnPauseChanged += HandlePauseChanged;
            }
        }

        private void OnDisable()
        {
            if (PauseController.Instance != null)
            {
                PauseController.Instance.OnPauseChanged -= HandlePauseChanged;
            }
        }

        private void Update()
        {
            // Animate pause menu sliding with easing curves
            if (isAnimating && pauseMenuRect != null)
            {
                float elapsed = Time.unscaledTime - animationStartTime;
                float duration = isVisible ? slideInDuration : slideOutDuration;
                AnimationCurve curve = isVisible ? slideInCurve : slideOutCurve;
                Vector3 targetPos = isVisible ? visiblePosition : hiddenPosition;

                if (elapsed >= duration)
                {
                    // Animation complete
                    pauseMenuRect.anchoredPosition = targetPos;
                    isAnimating = false;

                    // If we just finished sliding out, disable interaction
                    if (!isVisible && pauseMenuCanvasGroup != null)
                    {
                        pauseMenuCanvasGroup.interactable = false;
                        pauseMenuCanvasGroup.blocksRaycasts = false;
                    }
                }
                else
                {
                    // Evaluate curve and lerp position
                    float t = Mathf.Clamp01(elapsed / duration);
                    float curveValue = curve.Evaluate(t);
                    pauseMenuRect.anchoredPosition = Vector3.Lerp(animationStartPos, targetPos, curveValue);
                }
            }
        }

        public void OnResumePressed()
        {
            PlayClick();
            PauseController.Instance?.SetPaused(false);
        }

        public void OnOptionsPressed()
        {
            PlayClick();
            if (!isFlipping && isOnPausePage)
            {
                StartCoroutine(FlipToOptions());
            }
        }

        public void OnReturnPressed()
        {
            PlayClick();
            if (!isFlipping && !isOnPausePage)
            {
                StartCoroutine(FlipToOptions()); // Same flip animation, just swap pages
            }
        }

        public void OnMainMenuPressed()
        {
            PlayClick();
            ShowQuitConfirmation();
        }

        private IEnumerator FlipToOptions()
        {
            if (pageFlipRect == null || pageFlipCanvasGroup == null)
            {
                yield break;
            }

            isFlipping = true;

            // If flipping back to pause, hide options page immediately
            if (!isOnPausePage && optionsPageCanvasGroup != null)
            {
                optionsPageCanvasGroup.alpha = 0f;
                optionsPageCanvasGroup.gameObject.SetActive(false);
            }

            // Show page flip at start of animation
            pageFlipCanvasGroup.alpha = 1f;

            float halfDuration = flipDuration / 2f;

            // PHASE 1: Flip first half (start to spine)
            float elapsed = 0f;
            while (elapsed < halfDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / halfDuration);
                float curveValue = flipCurve.Evaluate(t);

                // Lerp position and size to mid-point
                pageFlipRect.anchoredPosition = Vector2.Lerp(startPosition, midPosition, curveValue);
                pageFlipRect.sizeDelta = Vector2.Lerp(startSize, midSize, curveValue);

                yield return null;
            }

            // Snap to exact mid values
            pageFlipRect.anchoredPosition = midPosition;
            pageFlipRect.sizeDelta = midSize;

            // PHASE 2: Flip the scale at the spine
            pageFlipRect.localScale = endScale;

            // PHASE 3: Flip second half (spine to final position)
            elapsed = 0f;
            while (elapsed < halfDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / halfDuration);
                float curveValue = flipCurve.Evaluate(t);

                // Lerp position and size to end position
                pageFlipRect.anchoredPosition = Vector2.Lerp(midPosition, endPosition, curveValue);
                pageFlipRect.sizeDelta = Vector2.Lerp(midSize, endSize, curveValue);

                yield return null;
            }

            // Snap to exact final values
            pageFlipRect.anchoredPosition = endPosition;
            pageFlipRect.sizeDelta = endSize;

            // CLEANUP: Swap page visibility
            if (isOnPausePage)
            {
                // Flipping from pause to options
                if (pausePageCanvasGroup != null)
                {
                    pausePageCanvasGroup.alpha = 0f;
                }

                if (optionsPageCanvasGroup != null)
                {
                    optionsPageCanvasGroup.gameObject.SetActive(true);
                    optionsPageCanvasGroup.alpha = 1f;
                }

                isOnPausePage = false;
            }
            else
            {
                // Flipping from options back to pause (options already hidden at start)
                if (pausePageCanvasGroup != null)
                {
                    pausePageCanvasGroup.alpha = 1f;
                }

                isOnPausePage = true;
            }

            // Fade out PageFlip
            pageFlipCanvasGroup.alpha = 0f;

            // Reset PageFlip to original state
            pageFlipRect.anchoredPosition = startPosition;
            pageFlipRect.sizeDelta = startSize;
            pageFlipRect.localScale = startScale;

            isFlipping = false;
        }

        private void HandlePauseChanged(bool paused)
        {
            if (paused)
            {
                // Enable interaction and start slide-in animation
                if (pauseMenuCanvasGroup != null)
                {
                    pauseMenuCanvasGroup.interactable = true;
                    pauseMenuCanvasGroup.blocksRaycasts = true;
                }

                if (pauseMenuRect != null && !isAnimating)
                {
                    isVisible = true;
                    isAnimating = true;
                    animationStartTime = Time.unscaledTime;
                    animationStartPos = pauseMenuRect.anchoredPosition;
                }
            }
            else
            {
                // Start slide-out animation (interaction will be disabled when animation completes)
                if (pauseMenuRect != null && !isAnimating)
                {
                    isVisible = false;
                    isAnimating = true;
                    animationStartTime = Time.unscaledTime;
                    animationStartPos = pauseMenuRect.anchoredPosition;
                }

                // Reset to pause page when unpausing
                ResetToPauseMenuPage();
            }
        }

        private void ResetToPauseMenuPage()
        {
            // Reset page visibility to pause page
            if (pausePageCanvasGroup != null)
            {
                pausePageCanvasGroup.alpha = 1f;
            }

            if (optionsPageCanvasGroup != null)
            {
                optionsPageCanvasGroup.alpha = 0f;
                optionsPageCanvasGroup.gameObject.SetActive(false);
            }

            // Reset page flip (hide it)
            if (pageFlipRect != null)
            {
                pageFlipRect.anchoredPosition = startPosition;
                pageFlipRect.sizeDelta = startSize;
                pageFlipRect.localScale = startScale;
            }

            if (pageFlipCanvasGroup != null)
            {
                pageFlipCanvasGroup.alpha = 0f;
            }

            // Hide quit confirmation
            HideQuitConfirmation();

            isOnPausePage = true;
            isFlipping = false;
        }

        public void OnConfirmQuitYes()
        {
            PlayClick();
            HideQuitConfirmation();
            PauseController.Instance?.SetPaused(false);
            SceneRouter.Instance?.LoadMainMenu();
        }

        public void OnConfirmQuitNo()
        {
            PlayClick();
            HideQuitConfirmation();
        }

        private void ShowQuitConfirmation()
        {
            if (quitConfirmation != null)
            {
                quitConfirmation.SetActive(true);
            }

            if (quitConfirmationCanvasGroup != null)
            {
                quitConfirmationCanvasGroup.alpha = 1f;
                quitConfirmationCanvasGroup.interactable = true;
                quitConfirmationCanvasGroup.blocksRaycasts = true;
            }
        }

        private void HideQuitConfirmation()
        {
            if (quitConfirmationCanvasGroup != null)
            {
                quitConfirmationCanvasGroup.alpha = 0f;
                quitConfirmationCanvasGroup.interactable = false;
                quitConfirmationCanvasGroup.blocksRaycasts = false;
            }

            if (quitConfirmation != null)
            {
                quitConfirmation.SetActive(false);
            }
        }

        private void PlayClick()
        {
            AudioManager.Instance?.PlayUiClick();
        }
    }
}

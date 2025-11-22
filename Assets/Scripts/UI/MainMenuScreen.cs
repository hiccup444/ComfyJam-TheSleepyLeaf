using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using System.Collections;
using JamesKJamKit.Services;

namespace JamesKJamKit.UI
{
    /// <summary>
    /// Handles button callbacks for the main menu scene.
    /// </summary>
    public sealed class MainMenuScreen : MonoBehaviour
    {
        [SerializeField]
        private OptionsPanel optionsPanel;

        [SerializeField]
        private RectTransform optionsPanelRect;

        [SerializeField]
        private RectTransform creditsPanelRect;

        [SerializeField]
        private CreditsPageFlip creditsPageFlip;

        [Header("Background Animation")]
        [SerializeField]
        private Image backgroundImage;

        [SerializeField]
        private Sprite[] backgroundSprites = new Sprite[3];

        [SerializeField]
        private float waitDuration = 2f;

        [SerializeField]
        private float transitionDelay = 0.5f;

        [Header("Steam Animation")]
        [Tooltip("First CanvasGroup for crossfading between frames")]
        [SerializeField]
        private CanvasGroup steamCanvasGroupA;

        [Tooltip("Second CanvasGroup for crossfading between frames (should be layered on top of A)")]
        [SerializeField]
        private CanvasGroup steamCanvasGroupB;

        [Tooltip("First Image for displaying steam sprites")]
        [SerializeField]
        private Image steamRendererA;

        [Tooltip("Second Image for displaying steam sprites")]
        [SerializeField]
        private Image steamRendererB;

        [Tooltip("Array of sprites to cycle through for steam animation")]
        [SerializeField]
        private Sprite[] steamFrames;

        [Tooltip("Time in seconds for crossfade transition between frames")]
        [SerializeField]
        private float steamCrossfadeDuration = 0.5f;

        [Tooltip("Time to hold each frame before transitioning to next")]
        [SerializeField]
        private float steamFrameHoldDuration = 0.3f;

        [Tooltip("Target alpha for steam (0-1 range)")]
        [SerializeField, Range(0f, 1f)]
        private float steamTargetAlpha = 0.235f; // 60/255 ≈ 0.235

        [Tooltip("Duration for initial fade-in of steam")]
        [SerializeField]
        private float steamFadeInDuration = 1f;

        [Header("Options Panel Animation")]
        [Tooltip("Duration for options panel to slide down (entry)")]
        [SerializeField]
        private float optionsSlideInDuration = 0.6f;

        [Tooltip("Duration for options panel to slide up (exit)")]
        [SerializeField]
        private float optionsSlideOutDuration = 0.5f;

        [Tooltip("Animation curve for slide in")]
        [SerializeField]
        private AnimationCurve optionsSlideInCurve = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 2f),
            new Keyframe(1f, 1f, 0.3f, 0f)
        );

        [Tooltip("Animation curve for slide out")]
        [SerializeField]
        private AnimationCurve optionsSlideOutCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        private Vector3 optionsHiddenPosition = new Vector3(0, 1500, 0); // Off-screen (above)
        private Vector3 optionsVisiblePosition = new Vector3(0, 0, 0); // On-screen
        private bool isOptionsAnimating = false;
        private bool isOptionsVisible = false;
        private float optionsAnimationStartTime = 0f;
        private Vector3 optionsAnimationStartPos = Vector3.zero;

        [Header("Credits Panel Animation")]
        [Tooltip("Duration for credits panel to slide down (entry)")]
        [SerializeField]
        private float creditsSlideInDuration = 0.6f;

        [Tooltip("Duration for credits panel to slide up (exit)")]
        [SerializeField]
        private float creditsSlideOutDuration = 0.5f;

        [Tooltip("Animation curve for slide in (starts fast, slows down heavily at end)")]
        [SerializeField]
        private AnimationCurve creditsSlideInCurve = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 2f),     // Start: slow out tangent for smooth start
            new Keyframe(1f, 1f, 0.3f, 0f)    // End: gentle in tangent for heavy slowdown
        );

        [Tooltip("Animation curve for slide out (ease in-out reversed)")]
        [SerializeField]
        private AnimationCurve creditsSlideOutCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        private Vector3 creditsHiddenPosition = new Vector3(0, 1500, 0); // Off-screen (above)
        private Vector3 creditsVisiblePosition = new Vector3(0, 0, 0); // On-screen
        private bool isCreditsAnimating = false;
        private bool isCreditsVisible = false;
        private float creditsAnimationStartTime = 0f;
        private Vector3 creditsAnimationStartPos = Vector3.zero;

        // Cached for performance
        private Keyboard cachedKeyboard;

        private void Start()
        {
            // Cache keyboard for performance
            cachedKeyboard = Keyboard.current;

            if (backgroundImage != null && backgroundSprites.Length == 3)
            {
                StartCoroutine(AnimateBackground());
            }

            if (steamCanvasGroupA != null && steamCanvasGroupB != null &&
                steamRendererA != null && steamRendererB != null &&
                steamFrames != null && steamFrames.Length > 0)
            {
                StartCoroutine(AnimateSteamCrossfade());
            }

            // Initialize options panel to hidden position
            if (optionsPanelRect != null)
            {
                optionsPanelRect.anchoredPosition = optionsHiddenPosition;
            }

            // Initialize credits panel to hidden position
            if (creditsPanelRect != null)
            {
                creditsPanelRect.anchoredPosition = creditsHiddenPosition;
            }
        }

        private void Update()
        {
            // Handle keyboard input to close options
            if (isOptionsVisible && cachedKeyboard != null && cachedKeyboard.anyKey.wasPressedThisFrame)
            {
                CloseOptions();
            }

            // Handle keyboard input to close credits
            if (isCreditsVisible && cachedKeyboard != null && cachedKeyboard.anyKey.wasPressedThisFrame)
            {
                CloseCredits();
            }

            // Animate options panel sliding with easing curves
            if (isOptionsAnimating && optionsPanelRect != null)
            {
                float elapsed = Time.time - optionsAnimationStartTime;
                float duration = isOptionsVisible ? optionsSlideInDuration : optionsSlideOutDuration;
                AnimationCurve curve = isOptionsVisible ? optionsSlideInCurve : optionsSlideOutCurve;
                Vector3 targetPos = isOptionsVisible ? optionsVisiblePosition : optionsHiddenPosition;

                if (elapsed >= duration)
                {
                    // Animation complete
                    optionsPanelRect.anchoredPosition = targetPos;
                    isOptionsAnimating = false;
                }
                else
                {
                    // Evaluate curve and lerp position
                    float t = Mathf.Clamp01(elapsed / duration);
                    float curveValue = curve.Evaluate(t);
                    optionsPanelRect.anchoredPosition = Vector3.Lerp(optionsAnimationStartPos, targetPos, curveValue);
                }
            }

            // Animate credits panel sliding with easing curves
            if (isCreditsAnimating && creditsPanelRect != null)
            {
                float elapsed = Time.time - creditsAnimationStartTime;
                float duration = isCreditsVisible ? creditsSlideInDuration : creditsSlideOutDuration;
                AnimationCurve curve = isCreditsVisible ? creditsSlideInCurve : creditsSlideOutCurve;
                Vector3 targetPos = isCreditsVisible ? creditsVisiblePosition : creditsHiddenPosition;

                if (elapsed >= duration)
                {
                    // Animation complete
                    creditsPanelRect.anchoredPosition = targetPos;
                    isCreditsAnimating = false;

                    // If we just finished sliding in, start revealing page one content
                    if (isCreditsVisible && creditsPageFlip != null)
                    {
                        creditsPageFlip.StartPageOneReveal();
                    }
                    // If we just finished sliding out, reset the credits content
                    else if (!isCreditsVisible && creditsPageFlip != null)
                    {
                        creditsPageFlip.ResetCredits();
                    }
                }
                else
                {
                    // Evaluate curve and lerp position
                    float t = Mathf.Clamp01(elapsed / duration);
                    float curveValue = curve.Evaluate(t);
                    creditsPanelRect.anchoredPosition = Vector3.Lerp(creditsAnimationStartPos, targetPos, curveValue);
                }
            }
        }

        public void OnPlayPressed()
        {
            PlayClick();
            SceneRouter.Instance?.LoadGame();
        }

        public void OnOptionsPressed()
        {
            PlayClick();
            if (optionsPanelRect != null && !isOptionsVisible && !isOptionsAnimating)
            {
                isOptionsVisible = true;
                isOptionsAnimating = true;
                optionsAnimationStartTime = Time.time;
                optionsAnimationStartPos = optionsPanelRect.anchoredPosition;
            }
        }

        public void OnCreditsPressed()
        {
            PlayClick();
            if (creditsPanelRect != null && !isCreditsVisible && !isCreditsAnimating)
            {
                isCreditsVisible = true;
                isCreditsAnimating = true;
                creditsAnimationStartTime = Time.time;
                creditsAnimationStartPos = creditsPanelRect.anchoredPosition;
            }
        }

        public void OnQuitPressed()
        {
            PlayClick();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        public void OnPanelClosed(GameObject panel)
        {
            if (panel != null)
            {
                // If this is the options panel, slide it out instead of disabling
                if (optionsPanelRect != null && panel == optionsPanelRect.gameObject)
                {
                    PlayClick();
                    CloseOptions();
                }
                // If this is the credits panel, slide it out instead of disabling
                else if (creditsPanelRect != null && panel == creditsPanelRect.gameObject)
                {
                    PlayClick();
                    CloseCredits();
                }
                else
                {
                    panel.SetActive(false);
                }
            }
        }

        public void OnOptionsPanelClosed()
        {
            PlayClick();
            CloseOptions();
        }

        public void OnCreditsPanelClosed()
        {
            PlayClick();
            CloseCredits();
        }

        private void CloseOptions()
        {
            if (optionsPanelRect != null && isOptionsVisible && !isOptionsAnimating)
            {
                isOptionsVisible = false;
                isOptionsAnimating = true;
                optionsAnimationStartTime = Time.time;
                optionsAnimationStartPos = optionsPanelRect.anchoredPosition;
            }
        }

        private void CloseCredits()
        {
            if (creditsPanelRect != null && isCreditsVisible && !isCreditsAnimating)
            {
                isCreditsVisible = false;
                isCreditsAnimating = true;
                creditsAnimationStartTime = Time.time;
                creditsAnimationStartPos = creditsPanelRect.anchoredPosition;
            }
        }

        private void PlayClick()
        {
            AudioManager.Instance?.PlayUiClick();
        }

        private IEnumerator AnimateBackground()
        {
            while (true)
            {
                // Image 1 - Wait
                backgroundImage.sprite = backgroundSprites[0];
                yield return new WaitForSecondsRealtime(waitDuration);

                // Transition to Image 2
                yield return new WaitForSecondsRealtime(transitionDelay);
                backgroundImage.sprite = backgroundSprites[1];

                // Transition to Image 3
                yield return new WaitForSecondsRealtime(transitionDelay);
                backgroundImage.sprite = backgroundSprites[2];

                // Image 3 - Wait
                yield return new WaitForSecondsRealtime(waitDuration);

                // Reverse: Transition to Image 2
                yield return new WaitForSecondsRealtime(transitionDelay);
                backgroundImage.sprite = backgroundSprites[1];

                // Reverse: Transition back to Image 1
                yield return new WaitForSecondsRealtime(transitionDelay);
            }
        }

        private IEnumerator AnimateSteamCrossfade()
        {
            if (steamCanvasGroupA == null || steamCanvasGroupB == null)
                yield break;

            if (steamRendererA == null || steamRendererB == null)
                yield break;

            if (steamFrames == null || steamFrames.Length == 0)
            {
                yield break;
            }

            int currentFrameIndex = 0;

            // Initialize: A shows first frame, both canvas groups start at alpha 0
            steamRendererA.sprite = steamFrames[0];
            steamRendererB.sprite = steamFrames.Length > 1 ? steamFrames[1] : steamFrames[0];

            steamCanvasGroupA.alpha = 0f;
            steamCanvasGroupB.alpha = 0f;

            // Fade in canvas group A from 0 to target alpha
            float elapsed = 0f;
            while (elapsed < steamFadeInDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / steamFadeInDuration);

                steamCanvasGroupA.alpha = Mathf.Lerp(0f, steamTargetAlpha, t);

                yield return null;
            }

            // Ensure final fade-in value
            steamCanvasGroupA.alpha = steamTargetAlpha;

            // Now begin the crossfade loop
            while (true)
            {
                // Hold current frame
                yield return new WaitForSecondsRealtime(steamFrameHoldDuration);

                // Determine next frame
                int nextFrameIndex = (currentFrameIndex + 1) % steamFrames.Length;

                // Set the fading-in renderer to the next frame
                steamRendererB.sprite = steamFrames[nextFrameIndex];

                // Crossfade: A fades out, B fades in
                elapsed = 0f;
                while (elapsed < steamCrossfadeDuration)
                {
                    elapsed += Time.deltaTime;
                    float t = Mathf.Clamp01(elapsed / steamCrossfadeDuration);

                    steamCanvasGroupA.alpha = Mathf.Lerp(steamTargetAlpha, 0f, t);
                    steamCanvasGroupB.alpha = Mathf.Lerp(0f, steamTargetAlpha, t);

                    yield return null;
                }

                // Ensure final values
                steamCanvasGroupA.alpha = 0f;
                steamCanvasGroupB.alpha = steamTargetAlpha;

                // Swap roles: what was B is now A
                currentFrameIndex = nextFrameIndex;

                // Swap the canvas groups and renderers for next iteration
                (steamCanvasGroupA, steamCanvasGroupB) = (steamCanvasGroupB, steamCanvasGroupA);
                (steamRendererA, steamRendererB) = (steamRendererB, steamRendererA);
            }
        }
    }
}

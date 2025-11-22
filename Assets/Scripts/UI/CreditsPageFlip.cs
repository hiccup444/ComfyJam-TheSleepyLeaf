using UnityEngine;
using TMPro;
using System.Collections;

namespace JamesKJamKit.UI
{
    /// <summary>
    /// Handles page flip animation for the credits book.
    /// </summary>
    public class CreditsPageFlip : MonoBehaviour
    {
        [Header("References")]
        [SerializeField]
        private RectTransform pageFlipRect;

        [SerializeField]
        private CanvasGroup pageFlipCanvasGroup;

        [SerializeField]
        private CanvasGroup pageOneCanvasGroup;

        [SerializeField]
        private CanvasGroup pageTwoCanvasGroup;

        [SerializeField]
        private CanvasGroup pageThreeCanvasGroup;

        [SerializeField]
        private CanvasGroup logoCanvasGroup;

        [Header("Page One Content")]
        [SerializeField]
        private RectTransform bookmarkArtMask;

        [SerializeField]
        private RectTransform bookmarkAudioMask;

        [SerializeField]
        private RectTransform artGroupRect;

        [SerializeField]
        private RectTransform audioGroupRect;

        [SerializeField]
        private TextMeshProUGUI artist2DHeader;

        [SerializeField]
        private CanvasGroup artist2DHeaderLines;

        [SerializeField]
        private TextMeshProUGUI artist2D1;

        [SerializeField]
        private TextMeshProUGUI artist2D2;

        [SerializeField]
        private TextMeshProUGUI artist2D3;

        [SerializeField]
        private TextMeshProUGUI uiUxHeader;

        [SerializeField]
        private CanvasGroup uiUxHeaderLines;

        [SerializeField]
        private TextMeshProUGUI uiUx1;

        [SerializeField]
        private TextMeshProUGUI audioHeader1;

        [SerializeField]
        private CanvasGroup audioHeader1Lines;

        [SerializeField]
        private TextMeshProUGUI audio1;

        [SerializeField]
        private TextMeshProUGUI audioHeader2;

        [SerializeField]
        private CanvasGroup audioHeader2Lines;

        [SerializeField]
        private TextMeshProUGUI audio2;

        [Header("Page Two Content")]
        [SerializeField]
        private RectTransform bookmarkProgrammerMask;

        [SerializeField]
        private RectTransform bookmarkNarrativeMask;

        [SerializeField]
        private RectTransform programmerGroupRect;

        [SerializeField]
        private RectTransform narrativeGroupRect;

        [SerializeField]
        private TextMeshProUGUI programmerHeader;

        [SerializeField]
        private CanvasGroup programmerHeaderLines;

        [SerializeField]
        private TextMeshProUGUI programmer1;

        [SerializeField]
        private TextMeshProUGUI programmer2;

        [SerializeField]
        private TextMeshProUGUI narrativeHeader1;

        [SerializeField]
        private CanvasGroup narrativeHeader1Lines;

        [SerializeField]
        private TextMeshProUGUI narrative1;

        [SerializeField]
        private TextMeshProUGUI narrativeHeader2;

        [SerializeField]
        private CanvasGroup narrativeHeader2Lines;

        [SerializeField]
        private TextMeshProUGUI narrative2;

        [Header("Animation Settings")]
        [Tooltip("Duration for bookmark expand animation")]
        [SerializeField]
        private float bookmarkExpandDuration = 0.5f;

        [Tooltip("Characters per second for typewriter effect")]
        [SerializeField]
        private float typewriterSpeed = 30f;

        [Tooltip("Duration for header pulse effect")]
        [SerializeField]
        private float headerPulseDuration = 0.3f;

        [Tooltip("Delay before auto-flipping to next page")]
        [SerializeField]
        private float autoFlipDelay = 2f;

        [Tooltip("Duration for Page Three fade-in")]
        [SerializeField]
        private float pageThreeFadeDuration = 1f;

        [Tooltip("Duration for the entire flip animation")]
        [SerializeField]
        private float flipDuration = 0.8f;

        [Tooltip("Animation curve for the flip (applies to both halves)")]
        [SerializeField]
        private AnimationCurve flipCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        // Original/Starting state
        private readonly Vector2 startPosition = new Vector2(185, -4);
        private readonly Vector2 startSize = new Vector2(356, 724);
        private readonly Vector3 startScale = new Vector3(1, 1, 1);

        // Mid-flip state (at spine)
        private readonly Vector2 midPosition = new Vector2(8, -4);
        private readonly Vector2 midSize = new Vector2(2, 724);

        // Final state (flipped - returns to original width)
        private readonly Vector2 endPosition = new Vector2(-168, -5);
        private readonly Vector2 endSize = new Vector2(356, 724);
        private readonly Vector3 endScale = new Vector3(-1, 1, 1);

        private bool isFlipping = false;
        private int currentPage = 1; // Track which page we're on (1, 2, or 3)
        private bool pageOneRevealed = false;
        private Coroutine activeRevealCoroutine = null;

        // Store original text content before hiding (Page One)
        private string storedHeaderText = "";
        private string storedArtist1Text = "";
        private string storedArtist2Text = "";
        private string storedArtist3Text = "";
        private string storedUiUxHeaderText = "";
        private string storedUiUx1Text = "";
        private string storedAudioHeader1Text = "";
        private string storedAudio1Text = "";
        private string storedAudioHeader2Text = "";
        private string storedAudio2Text = "";

        // Store original text content before hiding (Page Two)
        private string storedProgrammerHeaderText = "";
        private string storedProgrammer1Text = "";
        private string storedProgrammer2Text = "";
        private string storedNarrativeHeader1Text = "";
        private string storedNarrative1Text = "";
        private string storedNarrativeHeader2Text = "";
        private string storedNarrative2Text = "";

        private void Start()
        {
            // Store original text before clearing
            StoreOriginalText();

            // Initialize page one content to hidden state before credits button is clicked
            ResetPageOneContent();

            // Initialize page two content to hidden state
            ResetPageTwoContent();
        }

        private void StoreOriginalText()
        {
            // Page One
            if (artist2DHeader != null)
                storedHeaderText = artist2DHeader.text;
            if (artist2D1 != null)
                storedArtist1Text = artist2D1.text;
            if (artist2D2 != null)
                storedArtist2Text = artist2D2.text;
            if (artist2D3 != null)
                storedArtist3Text = artist2D3.text;
            if (uiUxHeader != null)
                storedUiUxHeaderText = uiUxHeader.text;
            if (uiUx1 != null)
                storedUiUx1Text = uiUx1.text;
            if (audioHeader1 != null)
                storedAudioHeader1Text = audioHeader1.text;
            if (audio1 != null)
                storedAudio1Text = audio1.text;
            if (audioHeader2 != null)
                storedAudioHeader2Text = audioHeader2.text;
            if (audio2 != null)
                storedAudio2Text = audio2.text;

            // Page Two
            if (programmerHeader != null)
                storedProgrammerHeaderText = programmerHeader.text;
            if (programmer1 != null)
                storedProgrammer1Text = programmer1.text;
            if (programmer2 != null)
                storedProgrammer2Text = programmer2.text;
            if (narrativeHeader1 != null)
                storedNarrativeHeader1Text = narrativeHeader1.text;
            if (narrative1 != null)
                storedNarrative1Text = narrative1.text;
            if (narrativeHeader2 != null)
                storedNarrativeHeader2Text = narrativeHeader2.text;
            if (narrative2 != null)
                storedNarrative2Text = narrative2.text;
        }

        [ContextMenu("Flip Page")]
        public void FlipPage()
        {
            if (!isFlipping)
            {
                StartCoroutine(FlipPageAnimation());
            }
        }

        /// <summary>
        /// Called by MainMenuScreen when credits panel finishes sliding down.
        /// Starts the page one content reveal animation.
        /// </summary>
        public void StartPageOneReveal()
        {
            if (!pageOneRevealed)
            {
                activeRevealCoroutine = StartCoroutine(RevealPageOneContent());
                pageOneRevealed = true;
            }
        }

        /// <summary>
        /// Stops any active reveal animation and immediately resets content.
        /// Called when credits are closed mid-animation.
        /// </summary>
        public void StopRevealAnimation()
        {
            if (activeRevealCoroutine != null)
            {
                StopCoroutine(activeRevealCoroutine);
                activeRevealCoroutine = null;
            }
        }

        private IEnumerator FlipPageAnimation()
        {
            if (pageFlipRect == null || pageFlipCanvasGroup == null)
            {
#if UNITY_EDITOR
                Debug.LogWarning("[CreditsPageFlip] Missing references!");
#endif
                yield break;
            }

            isFlipping = true;

            float halfDuration = flipDuration / 2f;

            // PHASE 1: Flip first half (start to spine)
            float elapsed = 0f;
            while (elapsed < halfDuration)
            {
                elapsed += Time.deltaTime;
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
                elapsed += Time.deltaTime;
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

            // CLEANUP: Update page visibility based on which page we're flipping from
            if (currentPage == 1)
            {
                // Hide PageOne, show PageTwo
                if (pageOneCanvasGroup != null)
                {
                    pageOneCanvasGroup.alpha = 0f;
                }

                if (pageTwoCanvasGroup != null)
                {
                    pageTwoCanvasGroup.alpha = 1f;
                }

                currentPage = 2;
            }
            else if (currentPage == 2)
            {
                // Hide PageTwo, show PageThree
                if (pageTwoCanvasGroup != null)
                {
                    pageTwoCanvasGroup.alpha = 0f;
                }

                if (pageThreeCanvasGroup != null)
                {
                    pageThreeCanvasGroup.alpha = 1f;
                }

                currentPage = 3;
            }

            // Fade out PageFlip
            pageFlipCanvasGroup.alpha = 0f;

            // Reset PageFlip to original state
            pageFlipRect.anchoredPosition = startPosition;
            pageFlipRect.sizeDelta = startSize;
            pageFlipRect.localScale = startScale;

            // Fade back in (ready for next flip)
            pageFlipCanvasGroup.alpha = 1f;

            isFlipping = false;

            // After flip completes, start revealing page content
            if (currentPage == 2)
            {
                activeRevealCoroutine = StartCoroutine(RevealPageTwoContent());
            }
            else if (currentPage == 3)
            {
                activeRevealCoroutine = StartCoroutine(RevealPageThreeContent());
            }
        }

        /// <summary>
        /// Resets the credits to their initial state (call when credits are closed).
        /// </summary>
        [ContextMenu("Reset Credits")]
        public void ResetCredits()
        {
            // Stop any active reveal animation
            StopRevealAnimation();

            // Reset page alphas to initial state
            if (pageOneCanvasGroup != null)
            {
                pageOneCanvasGroup.alpha = 1f;
            }

            if (pageTwoCanvasGroup != null)
            {
                pageTwoCanvasGroup.alpha = 0f;
            }

            if (pageThreeCanvasGroup != null)
            {
                pageThreeCanvasGroup.alpha = 0f;
            }

            if (logoCanvasGroup != null)
            {
                logoCanvasGroup.alpha = 0f;
            }

            // Reset PageFlip to original state
            if (pageFlipRect != null)
            {
                pageFlipRect.anchoredPosition = startPosition;
                pageFlipRect.sizeDelta = startSize;
                pageFlipRect.localScale = startScale;
            }

            if (pageFlipCanvasGroup != null)
            {
                pageFlipCanvasGroup.alpha = 1f;
            }

            currentPage = 1;
            isFlipping = false;
            pageOneRevealed = false;

            // Reset Page One and Page Two content
            ResetPageOneContent();
            ResetPageTwoContent();
        }

        private void ResetPageOneContent()
        {
            // Reset bookmark widths
            if (bookmarkArtMask != null)
            {
                bookmarkArtMask.sizeDelta = new Vector2(0, bookmarkArtMask.sizeDelta.y);
            }

            if (bookmarkAudioMask != null)
            {
                bookmarkAudioMask.sizeDelta = new Vector2(0, bookmarkAudioMask.sizeDelta.y);
            }

            // Reset text group widths
            if (artGroupRect != null)
            {
                artGroupRect.sizeDelta = new Vector2(0, artGroupRect.sizeDelta.y);
            }

            if (audioGroupRect != null)
            {
                audioGroupRect.sizeDelta = new Vector2(0, audioGroupRect.sizeDelta.y);
            }

            // Reset text visibility
            if (artist2DHeader != null)
            {
                artist2DHeader.text = "";
                artist2DHeader.maxVisibleCharacters = 0;
            }

            if (artist2DHeaderLines != null)
            {
                artist2DHeaderLines.alpha = 0f;
            }

            if (artist2D1 != null)
            {
                artist2D1.text = "";
                artist2D1.maxVisibleCharacters = 0;
            }

            if (artist2D2 != null)
            {
                artist2D2.text = "";
                artist2D2.maxVisibleCharacters = 0;
            }

            if (artist2D3 != null)
            {
                artist2D3.text = "";
                artist2D3.maxVisibleCharacters = 0;
            }

            if (uiUxHeader != null)
            {
                uiUxHeader.text = "";
                uiUxHeader.maxVisibleCharacters = 0;
            }

            if (uiUxHeaderLines != null)
            {
                uiUxHeaderLines.alpha = 0f;
            }

            if (uiUx1 != null)
            {
                uiUx1.text = "";
                uiUx1.maxVisibleCharacters = 0;
            }

            if (audioHeader1 != null)
            {
                audioHeader1.text = "";
                audioHeader1.maxVisibleCharacters = 0;
            }

            if (audioHeader1Lines != null)
            {
                audioHeader1Lines.alpha = 0f;
            }

            if (audio1 != null)
            {
                audio1.text = "";
                audio1.maxVisibleCharacters = 0;
            }

            if (audioHeader2 != null)
            {
                audioHeader2.text = "";
                audioHeader2.maxVisibleCharacters = 0;
            }

            if (audioHeader2Lines != null)
            {
                audioHeader2Lines.alpha = 0f;
            }

            if (audio2 != null)
            {
                audio2.text = "";
                audio2.maxVisibleCharacters = 0;
            }
        }

        private void ResetPageTwoContent()
        {
            // Reset bookmark widths
            if (bookmarkProgrammerMask != null)
            {
                bookmarkProgrammerMask.sizeDelta = new Vector2(0, bookmarkProgrammerMask.sizeDelta.y);
            }

            if (bookmarkNarrativeMask != null)
            {
                bookmarkNarrativeMask.sizeDelta = new Vector2(0, bookmarkNarrativeMask.sizeDelta.y);
            }

            // Reset text group widths
            if (programmerGroupRect != null)
            {
                programmerGroupRect.sizeDelta = new Vector2(0, programmerGroupRect.sizeDelta.y);
            }

            if (narrativeGroupRect != null)
            {
                narrativeGroupRect.sizeDelta = new Vector2(0, narrativeGroupRect.sizeDelta.y);
            }

            // Reset text visibility
            if (programmerHeader != null)
            {
                programmerHeader.text = "";
                programmerHeader.maxVisibleCharacters = 0;
            }

            if (programmerHeaderLines != null)
            {
                programmerHeaderLines.alpha = 0f;
            }

            if (programmer1 != null)
            {
                programmer1.text = "";
                programmer1.maxVisibleCharacters = 0;
            }

            if (programmer2 != null)
            {
                programmer2.text = "";
                programmer2.maxVisibleCharacters = 0;
            }

            if (narrativeHeader1 != null)
            {
                narrativeHeader1.text = "";
                narrativeHeader1.maxVisibleCharacters = 0;
            }

            if (narrativeHeader1Lines != null)
            {
                narrativeHeader1Lines.alpha = 0f;
            }

            if (narrative1 != null)
            {
                narrative1.text = "";
                narrative1.maxVisibleCharacters = 0;
            }

            if (narrativeHeader2 != null)
            {
                narrativeHeader2.text = "";
                narrativeHeader2.maxVisibleCharacters = 0;
            }

            if (narrativeHeader2Lines != null)
            {
                narrativeHeader2Lines.alpha = 0f;
            }

            if (narrative2 != null)
            {
                narrative2.text = "";
                narrative2.maxVisibleCharacters = 0;
            }
        }

        private IEnumerator RevealPageOneContent()
        {
            // Phase 1: Expand Art bookmark and text group
            float elapsed = 0f;
            while (elapsed < bookmarkExpandDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / bookmarkExpandDuration);

                if (bookmarkArtMask != null)
                {
                    float width = Mathf.Lerp(0f, 371f, t);
                    bookmarkArtMask.sizeDelta = new Vector2(width, bookmarkArtMask.sizeDelta.y);
                }

                if (artGroupRect != null)
                {
                    float width = Mathf.Lerp(0f, 330f, t);
                    artGroupRect.sizeDelta = new Vector2(width, artGroupRect.sizeDelta.y);
                }

                yield return null;
            }

            // Snap to final values
            if (bookmarkArtMask != null)
                bookmarkArtMask.sizeDelta = new Vector2(371f, bookmarkArtMask.sizeDelta.y);
            if (artGroupRect != null)
                artGroupRect.sizeDelta = new Vector2(330f, artGroupRect.sizeDelta.y);

            // Phase 2: Typewriter effect for 2DArtistHeader
            if (artist2DHeader != null)
            {
                yield return StartCoroutine(TypewriterEffect(artist2DHeader, storedHeaderText));

                // Pulse effect on header
                yield return StartCoroutine(PulseHeaderEffect(artist2DHeader));

                // Reveal lines under header
                if (artist2DHeaderLines != null)
                {
                    artist2DHeaderLines.alpha = 1f;
                }
            }

            // Phase 3: Typewriter for artist names in order
            if (artist2D1 != null)
            {
                yield return StartCoroutine(TypewriterEffect(artist2D1, storedArtist1Text));
            }

            if (artist2D2 != null)
            {
                yield return StartCoroutine(TypewriterEffect(artist2D2, storedArtist2Text));
            }

            if (artist2D3 != null)
            {
                yield return StartCoroutine(TypewriterEffect(artist2D3, storedArtist3Text));
            }

            // Phase 4: Typewriter effect for UI/UX Header
            if (uiUxHeader != null)
            {
                yield return StartCoroutine(TypewriterEffect(uiUxHeader, storedUiUxHeaderText));

                // Pulse effect on header
                yield return StartCoroutine(PulseHeaderEffect(uiUxHeader));

                // Reveal lines under header
                if (uiUxHeaderLines != null)
                {
                    uiUxHeaderLines.alpha = 1f;
                }
            }

            // Phase 5: Typewriter for UI/UX name
            if (uiUx1 != null)
            {
                yield return StartCoroutine(TypewriterEffect(uiUx1, storedUiUx1Text));
            }

            // Phase 6: Expand Audio bookmark and text group
            elapsed = 0f;
            while (elapsed < bookmarkExpandDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / bookmarkExpandDuration);

                if (bookmarkAudioMask != null)
                {
                    float width = Mathf.Lerp(0f, 371f, t);
                    bookmarkAudioMask.sizeDelta = new Vector2(width, bookmarkAudioMask.sizeDelta.y);
                }

                if (audioGroupRect != null)
                {
                    float width = Mathf.Lerp(0f, 330f, t);
                    audioGroupRect.sizeDelta = new Vector2(width, audioGroupRect.sizeDelta.y);
                }

                yield return null;
            }

            // Snap to final values
            if (bookmarkAudioMask != null)
                bookmarkAudioMask.sizeDelta = new Vector2(371f, bookmarkAudioMask.sizeDelta.y);
            if (audioGroupRect != null)
                audioGroupRect.sizeDelta = new Vector2(330f, audioGroupRect.sizeDelta.y);

            // Phase 7: Typewriter effect for Audio Header 1
            if (audioHeader1 != null)
            {
                yield return StartCoroutine(TypewriterEffect(audioHeader1, storedAudioHeader1Text));

                // Pulse effect on header
                yield return StartCoroutine(PulseHeaderEffect(audioHeader1));

                // Reveal lines under header
                if (audioHeader1Lines != null)
                {
                    audioHeader1Lines.alpha = 1f;
                }
            }

            // Phase 8: Typewriter for audio1 name
            if (audio1 != null)
            {
                yield return StartCoroutine(TypewriterEffect(audio1, storedAudio1Text));
            }

            // Phase 9: Typewriter effect for Audio Header 2
            if (audioHeader2 != null)
            {
                yield return StartCoroutine(TypewriterEffect(audioHeader2, storedAudioHeader2Text));

                // Pulse effect on header
                yield return StartCoroutine(PulseHeaderEffect(audioHeader2));

                // Reveal lines under header
                if (audioHeader2Lines != null)
                {
                    audioHeader2Lines.alpha = 1f;
                }
            }

            // Phase 10: Typewriter for audio2 name
            if (audio2 != null)
            {
                yield return StartCoroutine(TypewriterEffect(audio2, storedAudio2Text));
            }

            // Wait before auto-flipping to Page Two
            yield return new WaitForSeconds(autoFlipDelay);
            FlipPage();
        }

        private IEnumerator RevealPageTwoContent()
        {
            // Phase 1: Expand Programmer bookmark and text group
            float elapsed = 0f;
            while (elapsed < bookmarkExpandDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / bookmarkExpandDuration);

                if (bookmarkProgrammerMask != null)
                {
                    float width = Mathf.Lerp(0f, 371f, t);
                    bookmarkProgrammerMask.sizeDelta = new Vector2(width, bookmarkProgrammerMask.sizeDelta.y);
                }

                if (programmerGroupRect != null)
                {
                    float width = Mathf.Lerp(0f, 330f, t);
                    programmerGroupRect.sizeDelta = new Vector2(width, programmerGroupRect.sizeDelta.y);
                }

                yield return null;
            }

            // Snap to final values
            if (bookmarkProgrammerMask != null)
                bookmarkProgrammerMask.sizeDelta = new Vector2(371f, bookmarkProgrammerMask.sizeDelta.y);
            if (programmerGroupRect != null)
                programmerGroupRect.sizeDelta = new Vector2(330f, programmerGroupRect.sizeDelta.y);

            // Phase 2: Typewriter effect for Programmer Header
            if (programmerHeader != null)
            {
                yield return StartCoroutine(TypewriterEffect(programmerHeader, storedProgrammerHeaderText));

                // Pulse effect on header
                yield return StartCoroutine(PulseHeaderEffect(programmerHeader));

                // Reveal lines under header
                if (programmerHeaderLines != null)
                {
                    programmerHeaderLines.alpha = 1f;
                }
            }

            // Phase 3: Typewriter for programmer names in order
            if (programmer1 != null)
            {
                yield return StartCoroutine(TypewriterEffect(programmer1, storedProgrammer1Text));
            }

            if (programmer2 != null)
            {
                yield return StartCoroutine(TypewriterEffect(programmer2, storedProgrammer2Text));
            }

            // Phase 4: Expand Narrative bookmark and text group
            elapsed = 0f;
            while (elapsed < bookmarkExpandDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / bookmarkExpandDuration);

                if (bookmarkNarrativeMask != null)
                {
                    float width = Mathf.Lerp(0f, 371f, t);
                    bookmarkNarrativeMask.sizeDelta = new Vector2(width, bookmarkNarrativeMask.sizeDelta.y);
                }

                if (narrativeGroupRect != null)
                {
                    float width = Mathf.Lerp(0f, 330f, t);
                    narrativeGroupRect.sizeDelta = new Vector2(width, narrativeGroupRect.sizeDelta.y);
                }

                yield return null;
            }

            // Snap to final values
            if (bookmarkNarrativeMask != null)
                bookmarkNarrativeMask.sizeDelta = new Vector2(371f, bookmarkNarrativeMask.sizeDelta.y);
            if (narrativeGroupRect != null)
                narrativeGroupRect.sizeDelta = new Vector2(330f, narrativeGroupRect.sizeDelta.y);

            // Phase 5: Typewriter effect for Narrative Header 1
            if (narrativeHeader1 != null)
            {
                yield return StartCoroutine(TypewriterEffect(narrativeHeader1, storedNarrativeHeader1Text));

                // Pulse effect on header
                yield return StartCoroutine(PulseHeaderEffect(narrativeHeader1));

                // Reveal lines under header
                if (narrativeHeader1Lines != null)
                {
                    narrativeHeader1Lines.alpha = 1f;
                }
            }

            // Phase 6: Typewriter for narrative1 name
            if (narrative1 != null)
            {
                yield return StartCoroutine(TypewriterEffect(narrative1, storedNarrative1Text));
            }

            // Phase 7: Typewriter effect for Narrative Header 2
            if (narrativeHeader2 != null)
            {
                yield return StartCoroutine(TypewriterEffect(narrativeHeader2, storedNarrativeHeader2Text));

                // Pulse effect on header
                yield return StartCoroutine(PulseHeaderEffect(narrativeHeader2));

                // Reveal lines under header
                if (narrativeHeader2Lines != null)
                {
                    narrativeHeader2Lines.alpha = 1f;
                }
            }

            // Phase 8: Typewriter for narrative2 name
            if (narrative2 != null)
            {
                yield return StartCoroutine(TypewriterEffect(narrative2, storedNarrative2Text));
            }

            // Wait before auto-flipping to Page Three
            yield return new WaitForSeconds(autoFlipDelay);
            FlipPage();
        }

        private IEnumerator RevealPageThreeContent()
        {
            if (pageThreeCanvasGroup == null)
                yield break;

            // Start from alpha 0 and fade in to 1 for both Page Three and Logo
            float elapsed = 0f;
            while (elapsed < pageThreeFadeDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / pageThreeFadeDuration);

                pageThreeCanvasGroup.alpha = Mathf.Lerp(0f, 1f, t);

                if (logoCanvasGroup != null)
                {
                    logoCanvasGroup.alpha = Mathf.Lerp(0f, 1f, t);
                }

                yield return null;
            }

            // Ensure final values
            pageThreeCanvasGroup.alpha = 1f;

            if (logoCanvasGroup != null)
            {
                logoCanvasGroup.alpha = 1f;
            }
        }

        private IEnumerator TypewriterEffect(TextMeshProUGUI textField, string fullText)
        {
            if (textField == null || string.IsNullOrEmpty(fullText))
                yield break;

            textField.text = fullText;
            textField.maxVisibleCharacters = 0;

            int totalCharacters = fullText.Length;
            float timePerCharacter = 1f / typewriterSpeed;

            for (int i = 0; i <= totalCharacters; i++)
            {
                textField.maxVisibleCharacters = i;
                yield return new WaitForSeconds(timePerCharacter);
            }

            textField.maxVisibleCharacters = totalCharacters;
        }

        private IEnumerator PulseHeaderEffect(TextMeshProUGUI textField)
        {
            if (textField == null)
                yield break;

            float originalSize = 40f;
            float targetSize = 42f;
            float halfDuration = headerPulseDuration / 2f;

            // Phase 1: Scale up from 40 to 42
            float elapsed = 0f;
            while (elapsed < halfDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / halfDuration);
                textField.fontSize = Mathf.Lerp(originalSize, targetSize, t);
                yield return null;
            }

            textField.fontSize = targetSize;

            // Phase 2: Scale back down from 42 to 40
            elapsed = 0f;
            while (elapsed < halfDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / halfDuration);
                textField.fontSize = Mathf.Lerp(targetSize, originalSize, t);
                yield return null;
            }

            textField.fontSize = originalSize;
        }
    }
}

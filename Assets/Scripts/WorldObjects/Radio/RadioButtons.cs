using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using JamesKJamKit.Services;

[RequireComponent(typeof(SpriteRenderer))]
[RequireComponent(typeof(Collider2D))]
public class RadioButtons : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerEnterHandler, IPointerExitHandler
{
    public enum ButtonType
    {
        VolumeUp,
        VolumeDown,
        Mute,
        Next,
        Back
    }
    
    [Header("Button Settings")]
    [SerializeField] ButtonType buttonType;
    
    [Header("Sprites")]
    [SerializeField] Sprite normalSprite;
    [SerializeField] Sprite pressedSprite;
    
    [Header("Visual Feedback")]
    [SerializeField] float pressedDuration = 0.1f;
    [SerializeField] Color hoverColor = new Color(0.7f, 0.7f, 0.7f, 1f); // dimmed color on hover

    [Header("Volume Settings")]
    [SerializeField] float volumeStepDb = 5f; // how much to adjust volume per click

    SpriteRenderer spriteRenderer;
    static bool isMuted = false; // shared state across all radio buttons
    static float savedMusicDb = 0f; // saved volume for unmute
    Coroutine pressRoutine;
    Color normalColor = Color.white;
    bool isHovering = false;
    
    void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        
        if (normalSprite == null && spriteRenderer.sprite != null)
        {
            normalSprite = spriteRenderer.sprite;
        }
    }

    void OnEnable()
    {
        // sync mute button visual state when enabled
        if (buttonType == ButtonType.Mute)
        {
            spriteRenderer.sprite = isMuted ? pressedSprite : normalSprite;
        }
    }

    public static bool IsMuted()
    {
        return isMuted;
    }
    
    public void OnPointerDown(PointerEventData eventData)
    {
        if (pressedSprite != null && spriteRenderer != null)
        {
            spriteRenderer.sprite = pressedSprite;
        }
    }
    
    public void OnPointerUp(PointerEventData eventData)
    {
        HandleButtonAction();
        
        if (buttonType == ButtonType.Mute)
        {
            // mute button stays in current state
            spriteRenderer.sprite = isMuted ? pressedSprite : normalSprite;
        }
        else
        {
            // other buttons briefly show pressed then return to normal
            if (pressRoutine != null)
                StopCoroutine(pressRoutine);
            
            pressRoutine = StartCoroutine(ReturnToNormalAfterDelay());
        }
    }
    
    IEnumerator ReturnToNormalAfterDelay()
    {
        yield return new WaitForSeconds(pressedDuration);
        
        if (spriteRenderer != null && normalSprite != null)
        {
            spriteRenderer.sprite = normalSprite;
        }
    }
    
    void HandleButtonAction()
    {
        // play UI click sound
        AudioManager.Instance?.PlayUiClick();
        
        switch (buttonType)
        {
            case ButtonType.VolumeUp:
                OnVolumeUp();
                break;
            case ButtonType.VolumeDown:
                OnVolumeDown();
                break;
            case ButtonType.Mute:
                OnMute();
                break;
            case ButtonType.Next:
                OnNext();
                break;
            case ButtonType.Back:
                OnBack();
                break;
        }
    }
    
    void OnVolumeUp()
    {
        if (SaveSettings.Instance == null) return;
        
        var currentDb = SaveSettings.Instance.Data.musicDb;
        
        // if volume is very low (effectively muted), jump to a reasonable starting volume
        if (currentDb < -40f)
        {
            SaveSettings.Instance.SetMusicDb(-20f); // jump to audible level
            #if UNITY_EDITOR
            Debug.Log("[RadioButtons] Volume Up: -20.0dB (jumped from very low)");
            #endif
        }
        else
        {
            var newDb = Mathf.Min(currentDb + volumeStepDb, 0f); // cap at 0dB
            SaveSettings.Instance.SetMusicDb(newDb);
            #if UNITY_EDITOR
            Debug.Log($"[RadioButtons] Volume Up: {newDb:F1}dB");
            #endif
        }
        
        // if was muted, unmute
        if (isMuted)
        {
            isMuted = false;
            UpdateAllMuteButtons();
        }
    }

    void OnVolumeDown()
    {
        if (SaveSettings.Instance == null) return;
        
        var currentDb = SaveSettings.Instance.Data.musicDb;
        var newDb = Mathf.Max(currentDb - volumeStepDb, -80f); // floor at -80dB
        SaveSettings.Instance.SetMusicDb(newDb);
        
        // if was muted, unmute
        if (isMuted)
        {
            isMuted = false;
            UpdateAllMuteButtons();
        }

        #if UNITY_EDITOR
        Debug.Log($"[RadioButtons] Volume Down: {newDb:F1}dB");
        #endif
    }
    
    void OnMute()
    {
        if (SaveSettings.Instance == null) return;
        
        isMuted = !isMuted;
        
        if (isMuted)
        {
            // save current volume and mute
            savedMusicDb = SaveSettings.Instance.Data.musicDb;
            SaveSettings.Instance.SetMusicDb(-80f); // effectively silent
            #if UNITY_EDITOR
            Debug.Log("[RadioButtons] Muted");
            #endif
        }
        else
        {
            // restore saved volume
            SaveSettings.Instance.SetMusicDb(savedMusicDb);
            #if UNITY_EDITOR
            Debug.Log($"[RadioButtons] Unmuted - restored to {savedMusicDb:F1}dB");
            #endif
        }
        
        UpdateAllMuteButtons();
    }
    
    void OnNext()
    {
        if (JamesKJamKit.Services.MusicDirector.Instance != null)
        {
            JamesKJamKit.Services.MusicDirector.Instance.SkipToNextTrack();
            #if UNITY_EDITOR
            Debug.Log("[RadioButtons] Next track");
            #endif
        }
    }

    void OnBack()
    {
        if (JamesKJamKit.Services.MusicDirector.Instance != null)
        {
            JamesKJamKit.Services.MusicDirector.Instance.SkipToPreviousTrack();
            #if UNITY_EDITOR
            Debug.Log("[RadioButtons] Previous track");
            #endif
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        isHovering = true;
        if (spriteRenderer != null)
        {
            spriteRenderer.color = hoverColor;
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isHovering = false;
        if (spriteRenderer != null)
        {
            spriteRenderer.color = normalColor;
        }
    }

    void UpdateAllMuteButtons()
    {
        // update all mute button visuals to match shared state
        var allButtons = FindObjectsByType<RadioButtons>(FindObjectsSortMode.None);
        foreach (var button in allButtons)
        {
            if (button.buttonType == ButtonType.Mute)
            {
                button.spriteRenderer.sprite = isMuted ? button.pressedSprite : button.normalSprite;
            }
        }
    }
}
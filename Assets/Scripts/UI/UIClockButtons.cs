using UnityEngine;
using UnityEngine.EventSystems;
using JamesKJamKit.Services;
using JamesKJamKit.UI;

public class UIClockButtons : MonoBehaviour
{
    [SerializeField] OptionsPanel optionsPanel;
    
    public void OnPauseClicked()
    {
        DeselectButton();
        
        if (PauseController.Instance != null)
        {
            PauseController.Instance.SetPaused(true);
        }
    }
    
    public void OnSettingsClicked()
    {
        DeselectButton();
        
        // pause the game
        if (PauseController.Instance != null)
        {
            PauseController.Instance.SetPaused(true);
        }
        
        // open settings panel directly (bypass pause menu)
        if (optionsPanel != null)
        {
            optionsPanel.gameObject.SetActive(true);
        }
    }
    
    void DeselectButton()
    {
        // clear the selected object so button returns to normal/hover state
        EventSystem.current.SetSelectedGameObject(null);
    }
}
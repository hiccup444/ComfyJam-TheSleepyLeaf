using UnityEngine;
using TMPro;

public class UIDayCounter : MonoBehaviour
{
    [SerializeField] TMP_Text dayText;
    
    void Start()
    {
        if (dayText == null)
            dayText = GetComponent<TMP_Text>();
        
        if (GameManager.Instance != null)
        {
            // subscribe to day change events
            GameManager.Instance.OnDayStarted += UpdateDayText;
            GameManager.Instance.OnDayChanged += OnDayChanged;
            
            // set initial text
            UpdateDayText();
        }
        else
        {
            #if UNITY_EDITOR
            Debug.LogError("UIDayCounter: GameManager.Instance is null!");
            #endif
        }
    }
    
    void OnDisable()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnDayStarted -= UpdateDayText;
            GameManager.Instance.OnDayChanged -= OnDayChanged;
        }
    }
    
    void OnDayChanged(int newDay)
    {
        UpdateDayText();
    }
    
    void UpdateDayText()
    {
        if (dayText == null || GameManager.Instance == null) return;
        
        int currentDay = GameManager.Instance.GetCurrentDay();
        dayText.text = $"Day {currentDay}";
    }
}
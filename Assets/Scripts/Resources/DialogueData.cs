using UnityEngine;

[CreateAssetMenu(fileName = "New Dialogue", menuName = "Data Assets/Dialogue Data")]
public class DialogueData : ScriptableObject
{
    [Tooltip("Name of this dialogue set")]
    public string dialogueName;
    
    [Header("Dialogue Lines")]
    [Tooltip("Possible lines for this dialogue set.")]
    [TextArea(2, 4)]
    public string[] lines;
    
    [Header("Player Interaction")]
    [Tooltip("If true, shows Yes/No choices after dialogue and waits for player response")]
    public bool isPlayerInteractable = false;
    
    public string GetRandomLine()
    {
        if (lines == null || lines.Length == 0)
        {
#if UNITY_EDITOR
            Debug.LogWarning($"DialogueData '{dialogueName}' has no lines!");
#endif
            return "";
        }
        
        return lines[Random.Range(0, lines.Length)];
    }
}
using UnityEngine;

[System.Serializable]
public class OrderPreference
{
    public string orderName;
    public DialogueData orderDialogue;
}

[CreateAssetMenu(fileName = "New Customer", menuName = "Data Assets/Customer Data")]
public class CustomerData : ScriptableObject
{
    [Header("Identity")]
    public string customerName = "Customer";

    [Header("Prefab")]
    [Tooltip("Prefab to spawn for this customer (must have Customer component)")]
    public GameObject customerPrefab;

    [Header("Dialogue (References)")]
    public DialogueData greetingDialogue;
    public DialogueData happyDialogue;
    public DialogueData disappointedDialogue;
    [Tooltip("Optional DialogSpeech voice settings specific to this customer.")]
    public DialogSpeechSettings dialogSpeechSettings;
    
    [Header("Tutorial Dialogues (Optional)")]
    [Tooltip("Additional dialogues for tutorial customers - can be accessed by index")]
    public DialogueData[] tutorialDialogues;

    [Header("Order Preferences")]
    public OrderPreference[] preferredOrders;

    [Header("Behavior")]
    [Range(10f, 500f)]
    public float patienceTime = 180f;
    
    [Tooltip("Y-axis offset for counter/waiting points (e.g., -9.2 for turtle, -8.5 for squirrel)")]
    public float counterYOffset = 0f;

    [Header("Tips")]
    [Range(0f, 50f)]
    public float minTipAmount = 3f;

    [Range(0f, 50f)]
    public float maxTipAmount = 19f;

    [Range(0f, 100f)]
    public float specialMaxTipAmount = 20f;

    public OrderPreference GetRandomOrder()
    {
        if (preferredOrders == null || preferredOrders.Length == 0)
        {
#if UNITY_EDITOR
            Debug.LogWarning($"Customer '{customerName}' has no preferred orders!");
#endif
            return null;
        }

        return preferredOrders[Random.Range(0, preferredOrders.Length)];
    }
    
    /// <summary>
    /// Get a specific tutorial dialogue by index
    /// </summary>
    public DialogueData GetTutorialDialogue(int index)
    {
        if (tutorialDialogues == null || tutorialDialogues.Length == 0)
        {
#if UNITY_EDITOR
            Debug.LogWarning($"Customer '{customerName}' has no tutorial dialogues!");
#endif
            return null;
        }

        if (index < 0 || index >= tutorialDialogues.Length)
        {
#if UNITY_EDITOR
            Debug.LogWarning($"Customer '{customerName}' tutorial dialogue index {index} out of range (0-{tutorialDialogues.Length - 1})!");
#endif
            return null;
        }

        return tutorialDialogues[index];
    }
    
    /// <summary>
    /// Check if customer has tutorial dialogues
    /// </summary>
    public bool HasTutorialDialogues()
    {
        return tutorialDialogues != null && tutorialDialogues.Length > 0;
    }
}

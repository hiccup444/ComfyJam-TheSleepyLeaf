using UnityEngine;

/// <summary>
/// Marker component that tags a collider as interactable for team-defined helpers or future systems.
/// </summary>
[RequireComponent(typeof(Collider2D))]
[AddComponentMenu("Comfy/UI/Interactable Object")]
public sealed class InteractableObject : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("If disabled, systems can ignore this object even if they look for InteractableObject.")]
    [SerializeField] bool isInteractable = true;

    [Header("Debug")]
    [Tooltip("Enable temporary logging when interactable hooks run")]
    [SerializeField] bool debugLog = false;

    public bool IsInteractable
    {
        get => isInteractable;
        set => isInteractable = value;
    }

    void OnValidate()
    {
        Collider2D col = GetComponent<Collider2D>();
        if (col == null)
        {
            Debug.LogWarning($"[InteractableObject] {name} requires a Collider2D component!", this);
        }
    }
}

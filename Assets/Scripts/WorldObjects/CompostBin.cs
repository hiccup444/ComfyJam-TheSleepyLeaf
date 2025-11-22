using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Events;

public class CompostBin : MonoBehaviour
{
    [Header("References")]
    [SerializeField] GameObject compostOpen;
    [SerializeField] GameObject compostClosed;
    
    [Header("Protected Items")]
    [Tooltip("Items in this list cannot be thrown away")]
    [SerializeField] GameObject[] protectedItems;
    
    [Header("Detection Settings")]
    [SerializeField] Vector2 boxSize = new Vector2(2f, 2f);
    [SerializeField] Vector2 boxOffset = Vector2.zero;
    [Tooltip("How often to check for hovering items (in seconds)")]
    [SerializeField] float checkInterval = 0.1f;

    [Header("Events")]
    public UnityEvent<GameObject> OnItemComposted;
    
    private HashSet<GameObject> hoveringItems = new HashSet<GameObject>();
    private float nextCheckTime;
    
    void Start()
    {
        if (compostOpen != null)
            compostOpen.SetActive(false);
        if (compostClosed != null)
            compostClosed.SetActive(true);
    }
    
    void Update()
    {
        // Periodically check for dragged items near the compost
        if (Time.time >= nextCheckTime)
        {
            nextCheckTime = Time.time + checkInterval;
            CheckForHoveringItems();
        }
    }

    void CheckForHoveringItems()
    {
        // Find all DragItem2D objects in the scene
        DragItem2D[] allDragItems = FindObjectsByType<DragItem2D>(FindObjectsSortMode.None);

        bool anyItemHovering = false;
        HashSet<GameObject> stillHovering = new HashSet<GameObject>();

        foreach (DragItem2D dragItem in allDragItems)
        {
            if (dragItem == null) continue;

            // Check if item is protected
            if (IsProtectedItem(dragItem.gameObject))
                continue;

            // Check if item is being dragged and is near the compost
            if (dragItem.IsDragging)
            {
                if (IsInBox(dragItem.transform.position))
                {
                    anyItemHovering = true;
                    stillHovering.Add(dragItem.gameObject);

                    // Track this item
                    if (!hoveringItems.Contains(dragItem.gameObject))
                    {
                        hoveringItems.Add(dragItem.gameObject);
                    }
                }
            }
            else if (hoveringItems.Contains(dragItem.gameObject))
            {
                // Item was hovering and is no longer being dragged - it was dropped!

                if (IsInBox(dragItem.transform.position))
                {
                    // Find the root object with IRespawnable (the parent, not the draggable child)
                    IRespawnable respawnable = dragItem.GetComponent<IRespawnable>();
                    GameObject objectToDestroy = dragItem.gameObject;

                    // If the draggable item doesn't have IRespawnable, check its parent
                    if (respawnable == null && dragItem.transform.parent != null)
                    {
                        respawnable = dragItem.transform.parent.GetComponent<IRespawnable>();
                        if (respawnable != null)
                        {
                            objectToDestroy = dragItem.transform.parent.gameObject;
                        }
                    }

                    // Special case: Check if this is a teabag (StringTop being dragged)
                    // Search up the hierarchy for a Teabag component
                    if (respawnable == null)
                    {
                        Transform current = dragItem.transform;
                        while (current != null)
                        {
                            Teabag teabag = current.GetComponent<Teabag>();
                            if (teabag != null)
                            {
                                // Found the teabag parent - destroy this instead
                                objectToDestroy = current.gameObject;
                                break;
                            }
                            current = current.parent;

                            // Stop if we hit TeaHolder to avoid destroying it
                            if (current != null && current.name == "TeaHolder")
                                break;
                        }
                    }

                    if (respawnable != null)
                    {
                        // Let the item respawn itself before we destroy it
                        respawnable.RespawnBeforeDestroy();
                    }

                    // Destroy the entire item (parent with all children)
                    if (OnItemComposted != null)
                        OnItemComposted.Invoke(objectToDestroy);

                    Destroy(objectToDestroy);
                    hoveringItems.Remove(dragItem.gameObject);
                }
                else
                {
                    // Item was dragged away before being dropped
                    hoveringItems.Remove(dragItem.gameObject);
                }
            }
        }

        // Update the compost open/closed state
        if (compostOpen != null)
        {
            compostOpen.SetActive(anyItemHovering);
        }
        if (compostClosed != null)
        {
            compostClosed.SetActive(!anyItemHovering);
        }

        // Clean up items that are no longer hovering
        hoveringItems = stillHovering;
    }
    
    bool IsInBox(Vector2 position)
    {
        Vector2 center = (Vector2)transform.position + boxOffset;
        Vector2 halfSize = boxSize * 0.5f;
        
        return position.x >= center.x - halfSize.x &&
               position.x <= center.x + halfSize.x &&
               position.y >= center.y - halfSize.y &&
               position.y <= center.y + halfSize.y;
    }
    
    bool IsProtectedItem(GameObject item)
    {
        if (protectedItems == null || protectedItems.Length == 0)
            return false;
            
        foreach (GameObject protectedItem in protectedItems)
        {
            if (protectedItem == item)
                return true;
        }
        
        return false;
    }
    
    // Visualize the detection box in the editor
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Vector2 center = (Vector2)transform.position + boxOffset;
        Gizmos.DrawWireCube(center, boxSize);
    }
}
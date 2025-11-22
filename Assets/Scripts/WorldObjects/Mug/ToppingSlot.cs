using UnityEngine;
using UnityEngine.EventSystems;
using System.Collections;

[RequireComponent(typeof(Collider2D))]
public class ToppingSlot : MonoBehaviour, IPointerDownHandler
{
    [Header("Topping Prefab")]
    [Tooltip("The prefab to spawn when this slot is clicked")]
    [SerializeField] private GameObject toppingPrefab;
    
    [Header("Spawn Settings")]
    [Tooltip("Parent for spawned toppings (leave null to spawn in world root)")]
    [SerializeField] private Transform spawnParent;
    
    private Camera cam;
    
    void Awake()
    {
        cam = Camera.main;
    }
    
    public void OnPointerDown(PointerEventData eventData)
    {
#if UNITY_EDITOR
        if (toppingPrefab == null)
        {
            Debug.LogWarning($"ToppingSlot on {gameObject.name} has no prefab assigned!", this);
            return;
        }
#endif

        // Spawn at cursor position
        Vector3 worldPos = cam.ScreenToWorldPoint(eventData.position);
        worldPos.z = 0f; // Keep it at z=0 for 2D

        // Spawn the topping
        GameObject newTopping = Instantiate(toppingPrefab, worldPos, Quaternion.identity);
        newTopping.name = toppingPrefab.name; // Remove (Clone) suffix

        // Set parent if specified
        if (spawnParent != null)
        {
            newTopping.transform.SetParent(spawnParent, true);
            // Maintain world position after parenting
            newTopping.transform.position = worldPos;
        }

        // Find the DragItem2D component (should be on the prefab or its children)
        DragItem2D dragItem = newTopping.GetComponent<DragItem2D>();
        if (dragItem == null)
        {
            dragItem = newTopping.GetComponentInChildren<DragItem2D>();
        }

        if (dragItem != null)
        {
            // Cache ToppingItem to avoid GetComponent in coroutine
            ToppingItem toppingItem = dragItem.GetComponent<ToppingItem>();

            // Start a coroutine to manually feed drag events
            StartCoroutine(ManualDragRoutine(dragItem, toppingItem, eventData));
        }
#if UNITY_EDITOR
        else
        {
            Debug.LogWarning($"Spawned topping {newTopping.name} has no DragItem2D component!", newTopping);
        }
#endif
    }
    
    IEnumerator ManualDragRoutine(DragItem2D dragItem, ToppingItem toppingItem, PointerEventData originalEventData)
    {
        // Create a new event data for the spawned item
        PointerEventData newEventData = new PointerEventData(EventSystem.current)
        {
            position = originalEventData.position,
            button = originalEventData.button,
            clickCount = originalEventData.clickCount,
            clickTime = originalEventData.clickTime
        };

        // Trigger pointer down on the new item
        dragItem.OnPointerDown(newEventData);
        NotifyPickupAudio(dragItem, newEventData);

        // Keep dragging while mouse is held
        while (Input.GetMouseButton(0))
        {
            newEventData.position = Input.mousePosition;
            dragItem.OnDrag(newEventData);
            yield return null;
        }

        // Release when mouse is released
        dragItem.OnPointerUp(newEventData);

        // Notify ToppingItem that it was dropped (using cached reference)
        if (toppingItem != null)
        {
            toppingItem.OnDropped();
        }
    }

    void NotifyPickupAudio(DragItem2D dragItem, PointerEventData eventData)
    {
        if (dragItem == null) return;

        ToppingPickupAudio audio = dragItem.GetComponent<ToppingPickupAudio>();
        if (audio == null)
            audio = dragItem.GetComponentInParent<ToppingPickupAudio>();
        if (audio == null)
            audio = dragItem.GetComponentInChildren<ToppingPickupAudio>();

        audio?.HandleManualPickup(eventData);
    }
}

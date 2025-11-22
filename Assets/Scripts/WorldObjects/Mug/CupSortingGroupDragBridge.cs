// CupSortingGroupDragBridge.cs  (put on the SAME object as DragItem2D)
using UnityEngine;
using UnityEngine.EventSystems;

public sealed class CupSortingGroupDragBridge : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    CupSortingGroupToggle toggle;

    void Awake() => toggle = GetComponentInParent<CupSortingGroupToggle>();

    public void OnPointerDown(PointerEventData e) => toggle?.OnPickedUp();
    public void OnPointerUp(PointerEventData   e) => toggle?.OnDropped();
}

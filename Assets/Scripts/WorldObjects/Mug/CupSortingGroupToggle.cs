// CupSortingGroupToggle.cs  (put on the mug root WITH a SortingGroup added but disabled)
using UnityEngine;
using UnityEngine.Rendering;

public sealed class CupSortingGroupToggle : MonoBehaviour
{
    [Header("Sorting while dragging")]
    public string sortingLayerName = "Stations-Mug";
    public int baseOrderWhenDragging = 310;   // sits above mugFront 300, below streams 320
    public int raiseByWhileDragging  = 0;     // optional extra lift

    SortingGroup sg;
    int lastFreeOrder;
    int lastFreeLayerId;
    int dragLayerId;
    bool inSocket;
    bool serveLock;

    void Awake()
    {
        sg = GetComponent<SortingGroup>();
        if (!sg) sg = gameObject.AddComponent<SortingGroup>();
        lastFreeOrder = sg.sortingOrder;
        lastFreeLayerId = sg.sortingLayerID;
        dragLayerId = SortingLayer.NameToID(sortingLayerName);
        sg.enabled = false; // off while sitting
    }

    public void OnPickedUp()
    {
        if (serveLock) return;

        lastFreeLayerId = sg.sortingLayerID;
        lastFreeOrder = sg.sortingOrder;

        sg.sortingLayerID = dragLayerId;
        sg.sortingOrder   = baseOrderWhenDragging + raiseByWhileDragging;
        sg.enabled = true;
        inSocket = false;

        var cubes = GetComponentsInChildren<IceCube2D>(true);
        foreach (var c in cubes)
            c?.UnfreezeForCarry();
    }

    public void OnDropped()
    {
        if (serveLock) return;

        if (inSocket)
        {
            sg.enabled = false;
        }
        else
        {
            sg.sortingLayerID = lastFreeLayerId;
            sg.sortingOrder = lastFreeOrder;
            sg.enabled = true;
        }
    }

    public void SetInSocket(bool value)
    {
        inSocket = value;
        if (serveLock)
        {
            if (inSocket)
                sg.enabled = false;
            return;
        }

        if (inSocket)
        {
            sg.enabled = false;
            var cubes = GetComponentsInChildren<IceCube2D>(true);
            foreach (var c in cubes)
                c?.FreezeNow();
        }
        else
        {
            OnDropped();
        }
    }

    public void EnterServeLock()
    {
        serveLock = true;
        lastFreeLayerId = sg.sortingLayerID;
        lastFreeOrder = sg.sortingOrder;
        sg.enabled = true;
    }

    public void ExitServeLock()
    {
        serveLock = false;
        if (inSocket)
        {
            sg.enabled = false;
        }
        else
        {
            sg.sortingLayerID = lastFreeLayerId;
            sg.sortingOrder = lastFreeOrder;
            sg.enabled = true;
        }
    }
}

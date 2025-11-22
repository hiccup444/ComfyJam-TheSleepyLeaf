using UnityEngine;

[RequireComponent(typeof(SpriteRenderer))]
[RequireComponent(typeof(Collider2D))]
public class FridgeDoor : MonoBehaviour
{
    [SerializeField] private SpriteRenderer myRenderer;
    [SerializeField] private Collider2D myCollider;
    [SerializeField] private SpriteRenderer otherRenderer;
    [SerializeField] private Collider2D otherCollider;
    [SerializeField] private bool clickToToggle = true;

    private FridgeDoorAudio _audio;

    void Awake()
    {
        if (!myRenderer)  myRenderer  = GetComponent<SpriteRenderer>();
        if (!myCollider)  myCollider  = GetComponent<Collider2D>();

        if (!otherRenderer)
        {
            if (name.Contains("Closed"))
                otherRenderer = transform.parent.Find("fridge_doorOpen")?.GetComponent<SpriteRenderer>();
            else if (name.Contains("Open"))
                otherRenderer = transform.parent.Find("fridge_doorClosed")?.GetComponent<SpriteRenderer>();
        }
        if (!otherCollider && otherRenderer)
            otherCollider = otherRenderer.GetComponent<Collider2D>();

        _audio = GetComponentInParent<FridgeDoorAudio>();

        // Ensure colliders match current visibility at start
        SyncColliders();
    }

    private void SyncColliders()
    {
        if (myCollider && myRenderer) myCollider.enabled = myRenderer.enabled;
        if (otherCollider && otherRenderer) otherCollider.enabled = otherRenderer.enabled;
    }

    private void ShowThisHideOther(bool playOpenIfThisIsOpen)
    {
        if (!myRenderer || !otherRenderer) return;

        // Toggle visibility
        myRenderer.enabled = true;
        if (myCollider) myCollider.enabled = true;

        otherRenderer.enabled = false;
        if (otherCollider) otherCollider.enabled = false;

        // Audio (optional)
        if (playOpenIfThisIsOpen)
        {
            if (name.Contains("Open"))  _audio?.PlayOpen();
            if (name.Contains("Closed")) _audio?.PlayClose();
        }
    }

    public void Open()
    {
        // If this is the OPEN door, show it; otherwise forward
        if (name.Contains("Open"))  ShowThisHideOther(true);
        else otherRenderer?.GetComponent<FridgeDoor>()?.Open();
    }

    public void Close()
    {
        if (name.Contains("Closed")) ShowThisHideOther(true);
        else otherRenderer?.GetComponent<FridgeDoor>()?.Close();
    }

    public void Toggle()
    {
        if (!myRenderer || !otherRenderer) return;

        // Only toggle from whichever door is currently shown
        if (myRenderer.enabled)
        {
            if (name.Contains("Closed")) otherRenderer.GetComponent<FridgeDoor>()?.Open();
            else if (name.Contains("Open")) otherRenderer.GetComponent<FridgeDoor>()?.Close();
        }
    }

    void OnMouseDown()
    {
        // Only respond if THIS sprite is visible (its collider is enabled only when visible)
        if (!clickToToggle || !myRenderer || !myRenderer.enabled) return;
        Toggle();
    }
}

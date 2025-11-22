using UnityEngine;

public class DoorAnimation : MonoBehaviour
{
    [SerializeField] Animator doorAnimator;
    [SerializeField] GameObject doorOpen;
    [SerializeField] GameObject doorClosed;
    [SerializeField] string animationName = "DoorOpen";
    
    void Start()
    {
        // Stop animation on start
        if (doorAnimator != null)
        {
            doorAnimator.enabled = false;
        }
    }
    
    void Update()
    {
        if (doorAnimator == null) return;
        
        // Check if doorOpen is active
        if (doorOpen != null && doorOpen.activeSelf)
        {
            // Play animation on loop
            if (!doorAnimator.enabled)
            {
                doorAnimator.enabled = true;
                doorAnimator.Play(animationName, 0, 0f);
            }
        }
        else
        {
            // Stop animation when doorClosed is active
            doorAnimator.enabled = false;
        }
    }
}
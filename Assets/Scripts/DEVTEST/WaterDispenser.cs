using UnityEngine;

public class WaterDispenser : MonoBehaviour
{
    void OnMouseDown()
    {
        string buttonName = gameObject.name;

        if (buttonName == "hot_button")
        {
#if UNITY_EDITOR
            Debug.Log("Hot button clicked!");
#endif
        }
        else if (buttonName == "cold_button")
        {
#if UNITY_EDITOR
            Debug.Log("Cold button clicked!");
#endif
        }
    }
}
using UnityEngine;

[ExecuteAlways, DisallowMultipleComponent]
public class SafeArea : MonoBehaviour
{
    [SerializeField] bool simulateInEditor = false; // tick to visualize in Editor
    RectTransform rt;
    Rect lastSafe;

    void OnEnable() { rt = GetComponent<RectTransform>(); Apply(); }
    void Update()
    {
        var sa = GetSafeArea();
        if (sa != lastSafe) { Apply(); }
    }

    Rect GetSafeArea()
    {
#if UNITY_EDITOR
        if (!simulateInEditor) return Screen.safeArea; // usually full screen on desktop
        // Simple notch sim: shrink 60px from top on tall screens
        var r = Screen.safeArea;
        if (Screen.height > Screen.width) r.height -= 60f;
        return r;
#else
        return Screen.safeArea;
#endif
    }

    void Apply()
    {
        if (rt == null) return;
        lastSafe = GetSafeArea();

        var canvas = rt.GetComponentInParent<Canvas>();
        if (canvas == null || canvas.renderMode != RenderMode.ScreenSpaceOverlay)
        {
            // For Camera/World canvases, you might convert via camera; overlay is simplest.
        }

        // Convert safe area from pixels to anchor space (0–1)
        var anchorMin = lastSafe.position;
        var anchorMax = lastSafe.position + lastSafe.size;
        anchorMin.x /= Screen.width;
        anchorMin.y /= Screen.height;
        anchorMax.x /= Screen.width;
        anchorMax.y /= Screen.height;

        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}

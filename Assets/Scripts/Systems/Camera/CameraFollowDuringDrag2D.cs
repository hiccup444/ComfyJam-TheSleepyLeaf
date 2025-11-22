using UnityEngine;

[RequireComponent(typeof(Camera))]
public sealed class CameraFollowDuringDrag2D : MonoBehaviour
{
    [Header("Follow")]
    [SerializeField] float followLerp = 8f;   // higher = snappier
    [SerializeField] Vector2 screenOffset = Vector2.zero; // optional offset in world units

    [Header("World Bounds")]
    [SerializeField] float minX = -20f, maxX = 20f;
    [SerializeField] float minY = -10f, maxY = 10f;

    Transform target;   // the dragged item
    Camera cam;

    void Awake() { cam = GetComponent<Camera>(); cam.orthographic = true; }

    public void BeginFollow(Transform t) { target = t; }
    public void EndFollow()              { target = null; }

    void LateUpdate()
    {
        if (!target) return;
        
        // Don't follow when day is complete (summary showing)
        if (GameManager.Instance != null && GameManager.Instance.IsDayComplete())
            return;

        // Desired camera center = target position + optional offset
        Vector3 desired = target.position + (Vector3)screenOffset;
        desired.z = transform.position.z;

        // Smoothly move camera
        transform.position = Vector3.Lerp(transform.position, desired, followLerp * Time.deltaTime);

        // Clamp so camera view stays inside bounds
        float vert = cam.orthographicSize;
        float horiz = vert * cam.aspect;
        float cx = Mathf.Clamp(transform.position.x, minX + horiz, maxX - horiz);
        float cy = Mathf.Clamp(transform.position.y, minY + vert,  maxY - vert);
        transform.position = new Vector3(cx, cy, transform.position.z);
    }
}
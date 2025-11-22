using UnityEngine;

[ExecuteAlways]
public sealed class CupSource : MonoBehaviour
{
    [Header("Spawn")]
    [SerializeField] private GameObject cupPrefab;
    [SerializeField] private bool spawnOnStart = true;

    [Tooltip("Automatically spawn cups on a timer while the game is playing.")]
    [SerializeField] private bool autoSpawn = false;

    [Tooltip("Delay before the first auto-spawn (seconds).")]
    [SerializeField, Min(0f)] private float autoSpawnFirstDelay = 0.5f;

    [Tooltip("Interval between auto-spawns (seconds).")]
    [SerializeField, Min(0.1f)] private float autoSpawnInterval = 3f;

    [Tooltip("Maximum number of cups allowed in the scene at once (tag-based count). 0 = no cap.")]
    [SerializeField, Min(0)] private int maxLiveCups = 1;

    [Tooltip("Vertical offset when instantiating the cup.")]
    [SerializeField, Min(0f)] private float spawnYOffset = 0f;

    [Tooltip("Tag used to count live cups when enforcing the cap.")]
    [SerializeField] private string cupTag = "Cup";

    [Header("Gizmo")]
    [SerializeField] private Color gizmoColor = new Color(0.2f, 0.8f, 1f, 0.8f);
    [SerializeField, Min(0.05f)] private float gizmoRadius = 0.25f;
    [SerializeField] private bool drawArrow = true;

    private Coroutine _autoRoutine;

    private void OnEnable()
    {
        if (Application.isPlaying && autoSpawn)
            _autoRoutine = StartCoroutine(AutoSpawnLoop());
    }

    private void Start()
    {
        if (!Application.isPlaying) return;

        if (spawnOnStart)
            Spawn();

        // If autoSpawn was turned on mid-edit and component enabled after Start,
        // ensure the loop is running.
        if (autoSpawn && _autoRoutine == null)
            _autoRoutine = StartCoroutine(AutoSpawnLoop());
    }

    private void OnDisable()
    {
        if (_autoRoutine != null)
        {
            StopCoroutine(_autoRoutine);
            _autoRoutine = null;
        }
    }

    private System.Collections.IEnumerator AutoSpawnLoop()
    {
        if (autoSpawnFirstDelay > 0f)
            yield return new WaitForSeconds(autoSpawnFirstDelay);

        var wait = new WaitForSeconds(autoSpawnInterval);
        while (true)
        {
            if (ShouldSpawnNow())
                Spawn();

            yield return wait;
        }
    }

    private bool ShouldSpawnNow()
    {
        if (cupPrefab == null) return false;
        if (maxLiveCups <= 0) return true;

        // Count current cups by tag (simple + good enough for MVP).
        // Make sure your Cup prefab is tagged appropriately.
        int current = 0;
        var objs = GameObject.FindGameObjectsWithTag(cupTag);
        if (objs != null) current = objs.Length;

        return current < maxLiveCups;
    }

    [ContextMenu("Spawn Cup Now")]
    public void Spawn()
    {
        if (cupPrefab == null)
        {
#if UNITY_EDITOR
            Debug.LogWarning("[CupSource] No cupPrefab assigned.", this);
#endif
            return;
        }

        var pos = transform.position + Vector3.up * spawnYOffset;
        var cup = Instantiate(cupPrefab, pos, transform.rotation);

        // Optional: zero physics so newly spawned cups are calm.
        if (cup.TryGetComponent<Rigidbody>(out var rb))
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        if (cup.TryGetComponent<Rigidbody2D>(out var rb2d))
        {
            rb2d.linearVelocity = Vector2.zero;
            rb2d.angularVelocity = 0f;
        }
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = gizmoColor;
        var p = transform.position;

        // Filled disc look (simple fan)
        DrawSolidDisc(p, Vector3.forward, gizmoRadius);

        Gizmos.color = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, 1f);
        Gizmos.DrawWireSphere(p, gizmoRadius);

        if (drawArrow)
        {
            var dir = transform.up * Mathf.Max(gizmoRadius * 0.9f, 0.15f);
            Gizmos.DrawLine(p, p + dir);
            var right = Quaternion.AngleAxis(25f, Vector3.forward) * -dir * 0.35f;
            var left  = Quaternion.AngleAxis(-25f, Vector3.forward) * -dir * 0.35f;
            Gizmos.DrawLine(p + dir, p + dir + right);
            Gizmos.DrawLine(p + dir, p + dir + left);
        }
    }

    private static void DrawSolidDisc(Vector3 center, Vector3 normal, float radius)
    {
        const int steps = 24;
        Vector3 x = Vector3.right, y = Vector3.up;

        if (Vector3.Dot(normal.normalized, Vector3.forward) < 0.99f)
        {
            x = Vector3.Cross(normal, Vector3.forward).normalized;
            y = Vector3.Cross(normal, x).normalized;
        }

        var prev = center + (x * radius);
        for (int i = 1; i <= steps; i++)
        {
            float a = (i / (float)steps) * Mathf.PI * 2f;
            var next = center + (x * Mathf.Cos(a) + y * Mathf.Sin(a)) * radius;
            Gizmos.DrawLine(center, prev);
            Gizmos.DrawLine(prev, next);
            Gizmos.DrawLine(next, center);
            prev = next;
        }
    }
}

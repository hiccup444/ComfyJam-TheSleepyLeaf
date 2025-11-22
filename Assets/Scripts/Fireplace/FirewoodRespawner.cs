using UnityEngine;

/// <summary>
/// Spawns a firewood prefab at a fixed location when signaled (e.g., via FirewoodItem.onConsumed).
/// </summary>
[DisallowMultipleComponent]
public sealed class FirewoodRespawner : MonoBehaviour
{
    [Tooltip("Prefab of the firewood item to spawn.")]
    public FirewoodItem firewoodPrefab;

    [Tooltip("Local offset relative to this transform for spawning the wood.")]
    public Vector3 spawnOffset;

    [Tooltip("Spawn one immediately on Start.")]
    public bool spawnOnStart = true;

    private FirewoodItem _current;

    private void Start()
    {
        if (spawnOnStart && _current == null)
        {
            SpawnFirewood();
        }
    }

    /// <summary>
    /// Instantiates a new firewood item at the configured location.
    /// Designed to be called from FirewoodItem.onConsumed.
    /// </summary>
    public void SpawnFirewood()
    {
        if (firewoodPrefab == null)
        {
#if UNITY_EDITOR
            Debug.LogWarning("FirewoodRespawner has no prefab assigned.", this);
#endif
            return;
        }

        Vector3 spawnPosition = transform.position + spawnOffset;
        _current = Instantiate(firewoodPrefab, spawnPosition, Quaternion.identity);

        // If the respawner sits near the scene origin, keep the respawned wood parented to keep hierarchy clean.
        _current.transform.SetParent(transform.parent, true);

        // Wire its onConsumed UnityEvent back to this respawner so it can continually respawn.
        _current.onConsumed?.AddListener(SpawnFirewood);
    }
}

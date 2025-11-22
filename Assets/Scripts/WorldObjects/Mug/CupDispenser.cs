using UnityEngine;

public sealed class CupDispenser : MonoBehaviour
{
    [Header("Spawn")]
    [SerializeField] private GameObject cupPrefab;
    [SerializeField] private Transform spawnPoint;

    public GameObject SpawnFreshCup()
    {
        if (cupPrefab == null || spawnPoint == null)
        {
#if UNITY_EDITOR
            Debug.LogWarning("[CUP DISPENSER] Missing cupPrefab or spawnPoint.", this);
#endif
            return null;
        }

        var cup = Instantiate(cupPrefab, spawnPoint.position, spawnPoint.rotation);
        cup.name = "mug";
        return cup;
    }
}

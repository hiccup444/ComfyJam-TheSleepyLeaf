using UnityEngine;

[DisallowMultipleComponent]
public class AcornDropper : MonoBehaviour
{
    [SerializeField] private GameObject acornPrefab;
    [SerializeField] private Transform spawnPoint;
    [SerializeField, Min(0f)] private float horizontalSpread = 0.03f;

    [Header("Acorn Count (Random Range)")]
    [SerializeField, Min(1)] private int minAcornsPerServe = 1;
    [SerializeField, Min(1)] private int maxAcornsPerServe = 3;

    [SerializeField] private ServeCoordinator coordinator;

    private void OnEnable()
    {
        if (coordinator == null)
            coordinator = ServeCoordinator.Instance != null
                ? ServeCoordinator.Instance
                : Object.FindFirstObjectByType<ServeCoordinator>();

        if (coordinator != null)
            coordinator.OnServeSucceeded += HandleSuccessfulServe;
    }

    private void OnDisable()
    {
        if (coordinator != null)
            coordinator.OnServeSucceeded -= HandleSuccessfulServe;
    }

    private void HandleSuccessfulServe()
    {
        if (acornPrefab == null || spawnPoint == null)
            return;

        int min = Mathf.Min(minAcornsPerServe, maxAcornsPerServe);
        int max = Mathf.Max(minAcornsPerServe, maxAcornsPerServe);

        int acornCount = Random.Range(min, max + 1); // inclusive max

        for (int i = 0; i < acornCount; i++)
        {
            Vector3 spawnPos = spawnPoint.position;
            spawnPos.x += Random.Range(-horizontalSpread, horizontalSpread);
            Instantiate(acornPrefab, spawnPos, Quaternion.identity);
        }
    }
}

using UnityEngine;

public class AcornTester : MonoBehaviour
{
    [SerializeField] private GameObject acornPrefab;
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private float spawnInterval = 1.5f;

    private float timer;

    private void Update()
    {
        if (acornPrefab == null || spawnPoint == null) return;

        timer += Time.deltaTime;
        if (timer >= spawnInterval)
        {
            timer = 0f;

            var pos = spawnPoint.position;
            pos.x += Random.Range(-0.03f, 0.03f); // tiny horizontal spread

            Instantiate(acornPrefab, pos, Quaternion.identity);
        }
    }
}

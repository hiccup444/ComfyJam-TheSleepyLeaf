using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public class CustomerManager : MonoBehaviour
{
    [Header("Customer Resources")]
    [Tooltip("All possible customers that can spawn")]
    [SerializeField] CustomerData[] availableCustomers;

    [Header("Spawn Settings")]
    [Tooltip("Where customers appear (e.g., doorway)")]
    [SerializeField] Transform spawnPoint;

    [Tooltip("Point where door opens and sorting is restored")]
    [SerializeField] Transform openDoorPoint;

    [Tooltip("Door threshold point customers pass through")]
    [SerializeField] Transform doorPoint;

    [Tooltip("Door object to show when closed")]
    [SerializeField] GameObject doorClosed;

    [Tooltip("Door object to show when open")]
    [SerializeField] GameObject doorOpen;

    [Header("Counter Points (Queue System)")]
    [Tooltip("Counter points where customers will wait in order. First available is used.")]
    [SerializeField] Transform[] counterPoints;

    [Header("Wave-Based Spawning")]
    [Tooltip("Max customers waiting at once")]
    [SerializeField] int maxCustomersAtOnce = 3;

    [Header("Spawn Timing")]
    [Tooltip("Time range for first customer to spawn after shop opens")]
    [SerializeField] float firstCustomerMinTime = 3f;
    [SerializeField] float firstCustomerMaxTime = 10f;

    [Tooltip("Spawn interval between customers (seconds)")]
    [SerializeField] float minSpawnInterval = 10f;
    [SerializeField] float maxSpawnInterval = 30f;

    private List<Customer> activeCustomers;
    private bool spawningEnabled = false;
    private Coroutine spawnCoroutine;
    private bool hasSpawnedFirstCustomer = false;

    // Cached collections to reduce allocations
    private HashSet<Transform> occupiedPointsCache = new HashSet<Transform>();
    private List<Customer> waitingCustomersCache = new List<Customer>(3);

    // Cached WaitForSeconds to reduce GC allocations
    private WaitForSeconds waitOneSecond;

    // Event system for queue management
    public event System.Action OnCounterAssignmentsChanged;

    // Manager-level caching for occupied points (performance optimization)
    private HashSet<Transform> cachedOccupiedPoints = new HashSet<Transform>();
    private bool occupiedPointsCacheDirty = true;

    // Early day end system
    public event System.Action OnAllCustomersServedInLateDay;
    private bool hasStoppedSpawningForLateDay = false;
    private const float LATE_DAY_THRESHOLD = 0.9f; // Stop spawning at 90% day completion

    void Start()
    {
        // Initialize collections with proper capacity
        activeCustomers = new List<Customer>(maxCustomersAtOnce);

        // Initialize cached WaitForSeconds
        waitOneSecond = new WaitForSeconds(1f);

        // Listen to GameManager events
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnShopOpened += StartSpawning;
            GameManager.Instance.OnShopClosed += StopSpawning;
            GameManager.Instance.OnNightStarted += StopSpawning;
            GameManager.Instance.OnDayStarted += OnNewDay;
            GameManager.Instance.OnDayComplete += OnDayComplete;
        }

        // Validation
        if (availableCustomers == null || availableCustomers.Length == 0)
        {
#if UNITY_EDITOR
            Debug.LogError("CustomerManager: No customer data assigned!");
#endif
        }

        if (counterPoints == null || counterPoints.Length == 0)
        {
#if UNITY_EDITOR
            Debug.LogWarning("CustomerManager: No counter points assigned! Customers won't know where to go.");
#endif
        }
    }

    void OnDestroy()
    {
        // Unsubscribe from events
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnShopOpened -= StartSpawning;
            GameManager.Instance.OnShopClosed -= StopSpawning;
            GameManager.Instance.OnNightStarted -= StopSpawning;
            GameManager.Instance.OnDayStarted -= OnNewDay;
            GameManager.Instance.OnDayComplete -= OnDayComplete;
        }
    }

    void OnNewDay()
    {
        // Reset first customer flag for new day
        hasSpawnedFirstCustomer = false;
        hasStoppedSpawningForLateDay = false;

        // Force remove any customers still lingering from previous day (failsafe)
        ForceRemoveAllCustomers();
    }

    void OnDayComplete()
    {
        // Stop spawning when day ends
        // Customers will walk out naturally via OnShopClosed event (fired before OnDayComplete)
        // Any remaining customers will be force-removed when next day starts
        StopSpawning();
#if UNITY_EDITOR
        Debug.Log("CustomerManager: Day complete - customers should walk out naturally via shop close");
#endif
    }

    void StartSpawning()
    {
        if (spawningEnabled) return;

        spawningEnabled = true;
#if UNITY_EDITOR
        Debug.Log("CustomerManager: Started spawning customers");
#endif

        if (spawnCoroutine != null)
        {
            StopCoroutine(spawnCoroutine);
        }
        spawnCoroutine = StartCoroutine(SpawnRoutine());
    }

    void StopSpawning()
    {
        spawningEnabled = false;
#if UNITY_EDITOR
        Debug.Log("CustomerManager: Stopped spawning customers");
#endif

        if (spawnCoroutine != null)
        {
            StopCoroutine(spawnCoroutine);
            spawnCoroutine = null;
        }
    }

    void ForceRemoveAllCustomers()
    {
        // Force remove all active customers - used as failsafe when next day starts
        // Iterate backwards to avoid ToArray() allocation
        for (int i = activeCustomers.Count - 1; i >= 0; i--)
        {
            if (activeCustomers[i] != null)
            {
                Destroy(activeCustomers[i].gameObject);
            }
        }
        activeCustomers.Clear();
#if UNITY_EDITOR
        Debug.Log("CustomerManager: Force removed all remaining customers");
#endif
    }

    IEnumerator SpawnRoutine()
    {
        while (spawningEnabled)
        {
            // Don't spawn normal customers if tutorial hasn't been completed
            if (GameManager.Instance != null && !GameManager.Instance.HasCompletedTutorial())
            {
                yield return waitOneSecond; // Use cached WaitForSeconds
                continue;
            }

            // Stop spawning when 90% through the day
            if (GameManager.Instance != null && GameManager.Instance.GetNormalizedDayTime() >= LATE_DAY_THRESHOLD)
            {
                if (!hasStoppedSpawningForLateDay)
                {
                    hasStoppedSpawningForLateDay = true;
#if UNITY_EDITOR
                    Debug.Log("[CustomerManager] Day is 90% complete - stopping customer spawning");
#endif
                }

                // Check if store is empty and we can end day early
                if (activeCustomers.Count == 0)
                {
#if UNITY_EDITOR
                    Debug.Log("[CustomerManager] Store empty in late day - triggering early day end");
#endif
                    OnAllCustomersServedInLateDay?.Invoke();
                }

                yield return waitOneSecond;
                continue; // Don't spawn new customers
            }

            float waitTime;

            // First customer of the day spawns quickly
            if (!hasSpawnedFirstCustomer)
            {
                waitTime = Random.Range(firstCustomerMinTime, firstCustomerMaxTime);
                hasSpawnedFirstCustomer = true;
#if UNITY_EDITOR
                Debug.Log($"[CustomerManager] First customer wait time: {waitTime}s");
#endif
            }
            else
            {
                // All subsequent customers use consistent spawn interval
                waitTime = Random.Range(minSpawnInterval, maxSpawnInterval);
#if UNITY_EDITOR
                Debug.Log($"[CustomerManager] Next customer wait time: {waitTime}s (Active: {activeCustomers.Count}/{maxCustomersAtOnce})");
#endif
            }

            // Wait for interval
            yield return new WaitForSeconds(waitTime);

#if UNITY_EDITOR
            Debug.Log($"[CustomerManager] Wait complete. Active customers: {activeCustomers.Count}/{maxCustomersAtOnce}");
#endif

            // Check if shop is still open before spawning
            if (!spawningEnabled || GameManager.Instance == null || !GameManager.Instance.IsShopOpen())
            {
#if UNITY_EDITOR
                Debug.Log("Shop closed, pausing spawn");
#endif
                yield return new WaitUntil(() => GameManager.Instance != null && GameManager.Instance.IsShopOpen());
                continue;
            }

            // Check if we can spawn more
            if (activeCustomers.Count < maxCustomersAtOnce)
            {
#if UNITY_EDITOR
                Debug.Log($"[CustomerManager] Spawning customer (Active: {activeCustomers.Count})");
#endif
                SpawnRandomCustomer();
            }
            else
            {
#if UNITY_EDITOR
                Debug.Log($"[CustomerManager] At capacity ({activeCustomers.Count}/{maxCustomersAtOnce}), skipping spawn");
#endif
            }
        }
    }

    public void SpawnRandomCustomer()
    {
        if (availableCustomers == null || availableCustomers.Length == 0)
        {
#if UNITY_EDITOR
            Debug.LogWarning("No customers available to spawn!");
#endif
            return;
        }

        // Pick random customer
        CustomerData randomCustomer = availableCustomers[Random.Range(0, availableCustomers.Length)];
        SpawnCustomer(randomCustomer);
    }

    public Customer SpawnCustomer(CustomerData customerData)
    {
        if (customerData == null) return null;

        // Get prefab from customer data
        GameObject prefabToSpawn = customerData.customerPrefab;
        if (prefabToSpawn == null)
        {
#if UNITY_EDITOR
            Debug.LogError($"CustomerData '{customerData.customerName}' has no prefab assigned!");
#endif
            return null;
        }

        // Find the first available counter point
        Transform targetCounter = GetNextAvailableCounterPoint();

        if (targetCounter == null)
        {
#if UNITY_EDITOR
            Debug.LogWarning("No available counter points! Cannot spawn customer.");
#endif
            return null;
        }

        // Spawn at spawn point with no rotation
        Vector3 spawnPos = spawnPoint != null ? spawnPoint.position : transform.position;
        GameObject customerObj = Instantiate(prefabToSpawn, spawnPos, Quaternion.identity);

        // Initialize customer (pass spawn point as exit point so they walk back there)
        Customer customer = customerObj.GetComponent<Customer>();
        if (customer != null)
        {
            customer.Initialize(customerData, targetCounter, spawnPoint, openDoorPoint, doorPoint, doorClosed, doorOpen);
            customer.OnLeave += () => OnCustomerLeave(customer);
            customer.OnReadyToAdvance += () => TryMoveWaitingCustomersToCounter();
            activeCustomers.Add(customer);

            // Mark cache dirty and fire event for new customer
            occupiedPointsCacheDirty = true;
            OnCounterAssignmentsChanged?.Invoke();

            // Update sorting orders for all customers
            UpdateCustomerSortingOrders();

#if UNITY_EDITOR
            Debug.Log($"Spawned customer: {customerData.customerName} going to {targetCounter.name}");
#endif
        }

        return customer;
    }
    
    /// <summary>
    /// Spawns a tutorial customer - bypasses tutorial completion check
    /// </summary>
    public Customer SpawnTutorialCustomer(CustomerData customerData)
    {
        if (customerData == null)
        {
#if UNITY_EDITOR
            Debug.LogError("Cannot spawn tutorial customer - no data provided!");
#endif
            return null;
        }

#if UNITY_EDITOR
        Debug.Log($"[CustomerManager] Spawning tutorial customer: {customerData.customerName}");
#endif
        return SpawnCustomer(customerData);
    }

    public Transform GetNextAvailableCounterPoint()
    {
        if (counterPoints == null || counterPoints.Length == 0)
            return null;

        // Use cached HashSet to avoid repeated allocations - clear and reuse
        occupiedPointsCache.Clear();

        int customerCount = activeCustomers.Count;
        for (int i = 0; i < customerCount; i++)
        {
            var customer = activeCustomers[i];
            if (customer != null)
            {
                // Get the counter point this customer is heading to/at
                Transform customerTarget = customer.GetTargetCounterPoint();
                if (customerTarget != null)
                {
                    occupiedPointsCache.Add(customerTarget);
                }
            }
        }

        // Find first counter point that's not occupied - HashSet.Contains is O(1)
        int pointCount = counterPoints.Length;
        for (int i = 0; i < pointCount; i++)
        {
            if (!occupiedPointsCache.Contains(counterPoints[i]))
            {
                return counterPoints[i];
            }
        }

        // All points occupied
        return null;
    }

    void RebuildOccupiedPointsCache()
    {
        if (!occupiedPointsCacheDirty) return;

        cachedOccupiedPoints.Clear();
        for (int i = 0; i < activeCustomers.Count; i++)
        {
            Transform target = activeCustomers[i]?.GetTargetCounterPoint();
            if (target != null)
                cachedOccupiedPoints.Add(target);
        }
        occupiedPointsCacheDirty = false;
    }

    public Transform GetBestAvailableCounterPointForMe(Customer requestingCustomer)
    {
        if (counterPoints == null || counterPoints.Length == 0)
            return null;

        RebuildOccupiedPointsCache(); // Only rebuilds if dirty

        Transform currentTarget = requestingCustomer?.GetTargetCounterPoint();
        Transform bestPoint = null;
        int bestIndex = int.MaxValue;

        // Find lowest-indexed available counter point
        for (int i = 0; i < counterPoints.Length; i++)
        {
            Transform point = counterPoints[i];

            // Available if: not occupied OR occupied by requesting customer
            bool isAvailable = !cachedOccupiedPoints.Contains(point) || point == currentTarget;

            if (isAvailable && i < bestIndex)
            {
                bestPoint = point;
                bestIndex = i;
            }
        }

        return bestPoint;
    }

    void OnCustomerLeave(Customer customer)
    {
        activeCustomers.Remove(customer);
        if (customer != null)
        {
#if UNITY_EDITOR
            Debug.Log($"[CUST-MGR] '{customer.name}' left (removed from active list; active={activeCustomers.Count})");
#endif
        }
#if UNITY_EDITOR
        Debug.Log($"Customer left. Active customers: {activeCustomers.Count}");
#endif

        // Mark cache as dirty
        occupiedPointsCacheDirty = true;

        // Update sorting orders for remaining customers
        UpdateCustomerSortingOrders();

        // check if any waiting customers can move up to counter
        TryMoveWaitingCustomersToCounter();

        // Fire event for repositioning
        OnCounterAssignmentsChanged?.Invoke();

        // Check for early day end - check actual day time instead of relying on flag
        if (GameManager.Instance != null &&
            GameManager.Instance.GetNormalizedDayTime() >= LATE_DAY_THRESHOLD &&
            activeCustomers.Count == 0)
        {
#if UNITY_EDITOR
            Debug.Log("[CUST-MGR] All customers served in late day - triggering early day end");
#endif
            OnAllCustomersServedInLateDay?.Invoke();
        }
    }

    void TryMoveWaitingCustomersToCounter()
    {
        // Use cached list to avoid repeated allocations - clear and reuse
        waitingCustomersCache.Clear();

        int customerCount = activeCustomers.Count;
        for (int i = 0; i < customerCount; i++)
        {
            var customer = activeCustomers[i];
            if (customer != null &&
                !customer.CanOrder() &&
                customer.GetState() == CustomerState.Waiting) // They finished greeting!
            {
                waitingCustomersCache.Add(customer);
            }
        }

        if (waitingCustomersCache.Count == 0)
            return;

        // find available ORDERING counter (not waiting points)
        Transform availableCounter = GetNextAvailableCounterPoint();

        // Make sure it's actually the ordering counter, not a waiting point
        if (availableCounter == null || availableCounter.name != "CounterPoint")
            return;

        // move first waiting customer to counter
        Customer firstWaiting = waitingCustomersCache[0];
#if UNITY_EDITOR
        Debug.Log($"[CUST-MGR] Moving {firstWaiting.data.customerName} from waiting to {availableCounter.name}");
#endif

        // tell customer to move to the counter
        firstWaiting.ForceAdvanceTo(availableCounter);

        // Mark cache dirty since assignment changed
        occupiedPointsCacheDirty = true;
    }

    public int GetActiveCustomerCount() => activeCustomers.Count;
    public List<Customer> GetActiveCustomers() => new List<Customer>(activeCustomers);

    /// <summary>
    /// Updates sorting orders for all active customers based on their position in the queue.
    /// First customer (index 0) gets +2, second gets +1, third gets +0.
    /// </summary>
    void UpdateCustomerSortingOrders()
    {
        for (int i = 0; i < activeCustomers.Count; i++)
        {
            if (activeCustomers[i] != null)
            {
                // Calculate offset: first customer gets highest offset
                int offset = Mathf.Max(0, 2 - i);
                activeCustomers[i].SetSortingOrderOffset(offset);
            }
        }
    }

     // Debug functions
#if UNITY_EDITOR
    [ContextMenu("Spawn Random Customer")]
    void Debug_SpawnCustomer()
    {
        SpawnRandomCustomer();
    }

    // Spawns the first customer in the list (if exists)
    [ContextMenu("Spawn Customer 0")]
    void Debug_SpawnCustomer0()
    {
        if (availableCustomers.Length > 0)
            SpawnCustomer(availableCustomers[0]);
        else
            Debug.LogWarning("No customer at index 0 in availableCustomers");
    }

    // Spawns the second customer in the list (if exists)
    [ContextMenu("Spawn Customer 1")]
    void Debug_SpawnCustomer1()
    {
        if (availableCustomers.Length > 1)
            SpawnCustomer(availableCustomers[1]);
        else
            Debug.LogWarning("No customer at index 1 in availableCustomers");
    }

    [ContextMenu("Spawn Customer 2")]
    void Debug_SpawnCustomer2()
    {
        if (availableCustomers.Length > 2)
            SpawnCustomer(availableCustomers[2]);
        else
            Debug.LogWarning("No customer at index 2 in availableCustomers");
    }

    [ContextMenu("Spawn Customer 3")]
    void Debug_SpawnCustomer3()
    {
        if (availableCustomers.Length > 3)
            SpawnCustomer(availableCustomers[3]);
        else
            Debug.LogWarning("No customer at index 3 in availableCustomers");
    }

    [ContextMenu("Spawn Customer 4")]
    void Debug_SpawnCustomer4()
    {
        if (availableCustomers.Length > 4)
            SpawnCustomer(availableCustomers[4]);
        else
            Debug.LogWarning("No customer at index 4 in availableCustomers");
    }

    [ContextMenu("Spawn Customer 5")]
    void Debug_SpawnCustomer5()
    {
        if (availableCustomers.Length > 5)
            SpawnCustomer(availableCustomers[5]);
        else
            Debug.LogWarning("No customer at index 5 in availableCustomers");
    }
#endif
}
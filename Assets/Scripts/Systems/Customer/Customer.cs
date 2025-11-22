using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using TMPro;

[RequireComponent(typeof(SpriteRenderer))]
public class Customer : MonoBehaviour
{
    // dialogue coordination
    private static Queue<Customer> dialogueQueue = new Queue<Customer>();
    private static bool isDialoguePlaying = false;

    [Header("Data")]
    public CustomerData data;
    [SerializeField] private RecipeRegistry recipeRegistry;

    [Header("Sprite References")]
    [SerializeField] private SpriteRenderer bodySpriteRenderer;

    [Header("Body Sprites (Prefab Assigned)")]
    [SerializeField] private Sprite defaultBodySprite;
    [SerializeField] private Sprite happyBodySprite;
    [SerializeField] private Sprite disappointedBodySprite;

    [Header("OrderSprite")]
    [SerializeField] private SpriteRenderer orderSpriteRenderer;
    [SerializeField] private SpriteRenderer orderOutlineRenderer;
    [SerializeField] private SpriteRenderer orderSpriteTempRenderer;

    [Header("Temperature Sprites")]
    [SerializeField] private Sprite hotSprite;
    [SerializeField] private Sprite coldSprite;

    [Header("Movement")]
    [SerializeField] public float moveSpeed = 2f;
    [SerializeField] public float startScale = 0.8f;
    [SerializeField] public float targetScale = 1.0f;
    [SerializeField] public float bounceHeight = 0.1f;
    [SerializeField] public float bounceFrequency = 4f;
    public bool autoDialogueEnabled = true;


    [Header("Dialogue Display")]
    [SerializeField] public float dialogueDuration = 3f;

    [Header("Timing")]
    [SerializeField] public float greetingPauseDuration = 4f;
    [SerializeField] public float orderPauseDuration = 1f;

    [Header("Counter Point Settings")]
    [SerializeField] public string orderingCounterPointName = "CounterPoint";

    [Header("Cup Pickup")]
    [SerializeField] private Transform cupPickupPoint;

    [Header("Tip Display")]
    [SerializeField] private Color normalTipColor = Color.green;
    [SerializeField] private Color maxTipColor = Color.yellow;
    [SerializeField] private float tipFloatDuration = 2f;
    [SerializeField] private float tipFloatHeight = 1.5f;
    [SerializeField] private float tipTargetScale = 1.5f;

    [Header("Exit")]
    [SerializeField] public Transform exitPoint;

    [Header("Squish Animation")]
    [SerializeField] private float squishAmount = 0.1f;
    [SerializeField] private float squishDuration = 0.1f;
    private bool isSquishing = false;

    [Header("Rain Color")]
    [SerializeField] private Color rainColor = new Color(0.7f, 0.7f, 0.7f, 1f); // B3B3B3
    [SerializeField] private float rainColorFadeDuration = 45f; // 45 seconds to fade back to white
    private bool spawnedDuringRain = false;
    private Coroutine rainColorFadeCoroutine = null;

    [Header("Door")]
    [SerializeField] public Transform openDoorPoint;
    [SerializeField] public Transform doorPoint;
    public GameObject doorClosed;
    public GameObject doorOpen;
    public DoorAudioFeedback doorAudio;
    public SpriteRenderer OrderSpriteRenderer => orderSpriteRenderer;
    public SpriteRenderer OrderOutlineRenderer => orderOutlineRenderer;

    [Header("Sorting")]
    [SerializeField] private int spawnSortingOrder = -5;
    [SerializeField] private int queueBaseSortingOrder = 650;

    // Cached WaitForSeconds to reduce GC allocations
    private WaitForSeconds waitTwoSeconds;
    private WaitForSeconds waitOneSecond;
    private WaitForSeconds waitPointThreeSeconds;
    private WaitForSeconds waitPointTwoSeconds;
    private WaitForSeconds waitPointOneSeconds;

    // Cached values for Update optimization
    private float cachedNormalizedPatience;
    private bool patienceEventTriggered;

    // Pre-calculated bounce constants to reduce per-frame calculations
    private float bounceMoveSpeedMultiplier;
    private float bounceExitSpeedMultiplier;

    // state
    public OrderPreference currentOrderPreference;
    public string currentOrder;
    public float patienceTimer;
    public float maxPatienceTime;
    public bool hasReceivedOrder = false;
    public CustomerState currentState = CustomerState.Arriving;
    public bool isAtCounter = false;
    public bool canOrder = false;
    public bool isDisappointed = false;
    public bool receivedIncorrectOrder = false;
    public float bounceTimer = 0f;
    public bool hasEnteredThroughDoor = false;
    public Transform targetCounterPoint;

    // Event-driven queue repositioning
    private bool shouldRecheckPosition = false;

    // body root for scale
    public Transform bodyTransform;

    // original sorting orders so door masking can work - pre-sized for typical customer
    public readonly Dictionary<SpriteRenderer, int> originalSortingOrders = new Dictionary<SpriteRenderer, int>(8);

    // UI refs for new prefab layout
    private Transform canvasRoot;          // BearCanvas
    private DialoguePopup dialoguePopup;   // World-space UI: CustomerCanvas/DialoguePopup
    public Transform orderRoot;           // BearCanvas/Order
    private TextMeshProUGUI tipTMP;        // BearCanvas/Tip/tipTMP

    // events
    public System.Action<string> OnSpeak;
    public System.Action<string> OnOrderPlaced;
    public System.Action OnLeave;
    public System.Action OnReadyToAdvance;
    public System.Action OnDoorOpened;
    public System.Action<float> OnPatienceChanged;
    public System.Action<Transform> OnCupRequested;
    private int currentQueueOffset;
    private bool hasQueueOffsetAssigned;

    private void OnEnable()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnShopClosed += HandleShopClosed;
    }

    private void OnDisable()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnShopClosed -= HandleShopClosed;
    }

    public void Awake()
    {
        // Initialize cached WaitForSeconds instances
        waitTwoSeconds = new WaitForSeconds(2f);
        waitOneSecond = new WaitForSeconds(1f);
        waitPointThreeSeconds = new WaitForSeconds(0.3f);
        waitPointTwoSeconds = new WaitForSeconds(0.2f);
        waitPointOneSeconds = new WaitForSeconds(0.1f);

        bodyTransform = transform.Find("Body");
        if (bodyTransform == null)
#if UNITY_EDITOR
            Debug.LogError($"No 'Body' child found on {gameObject.name}! Create a Body child object.");
#endif

        // Get sprite renderer from Body child if available, otherwise from root
        if (bodySpriteRenderer == null)
        {
            if (bodyTransform != null)
                bodySpriteRenderer = bodyTransform.GetComponent<SpriteRenderer>();

            if (bodySpriteRenderer == null)
                bodySpriteRenderer = GetComponent<SpriteRenderer>();
        }

        // Find the world-space dialogue popup in the scene
        GameObject customerCanvas = GameObject.Find("CustomerCanvas");
        if (customerCanvas != null)
        {
            dialoguePopup = customerCanvas.GetComponentInChildren<DialoguePopup>(includeInactive: true);
            if (dialoguePopup == null)
            {
#if UNITY_EDITOR
                Debug.LogError($"'{name}' could not find DialoguePopup in CustomerCanvas!");
#endif
            }
        }
        else
        {
#if UNITY_EDITOR
            Debug.LogError($"'{name}' could not find CustomerCanvas in scene!");
#endif
        }

        // Still look for prefab UI components (Order and Tip remain on prefab)
        canvasRoot = transform.Find("BearCanvas");
        if (canvasRoot != null)
        {
            orderRoot = canvasRoot.Find("Order");
            if (orderRoot == null)
            {
#if UNITY_EDITOR
                Debug.LogError($"'{name}' missing BearCanvas/Order.");
#endif
            }

            var tipRoot = canvasRoot.Find("Tip");
            if (tipRoot == null)
            {
#if UNITY_EDITOR
                Debug.LogError($"'{name}' missing BearCanvas/Tip.");
#endif
            }
            else
            {
                tipTMP = tipRoot.GetComponentInChildren<TextMeshProUGUI>(includeInactive: true);
                if (tipTMP != null)
                    tipTMP.gameObject.SetActive(false);
            }

            if (orderRoot != null)
            {
                orderRoot.gameObject.SetActive(false);

                // Find OrderSpriteTemp (child of BearCanvas/Order/MiniOrder)
                if (orderSpriteTempRenderer == null)
                {
                    Transform miniOrder = orderRoot.Find("MiniOrder");
#if UNITY_EDITOR
                    Debug.Log($"[Customer] Looking for MiniOrder: {(miniOrder != null ? "FOUND" : "NOT FOUND")}");
#endif

                    if (miniOrder != null)
                    {
                        Transform tempTransform = miniOrder.Find("OrderSpriteTemp");
#if UNITY_EDITOR
                        Debug.Log($"[Customer] Looking for OrderSpriteTemp: {(tempTransform != null ? "FOUND" : "NOT FOUND")}");
#endif

                        if (tempTransform != null)
                        {
                            orderSpriteTempRenderer = tempTransform.GetComponent<SpriteRenderer>();
#if UNITY_EDITOR
                            Debug.Log($"[Customer] OrderSpriteTemp SpriteRenderer: {(orderSpriteTempRenderer != null ? "FOUND" : "NOT FOUND")}");
#endif

                            if (orderSpriteTempRenderer != null)
                            {
                                // Start with alpha at 0
                                Color c = orderSpriteTempRenderer.color;
                                c.a = 0f;
                                orderSpriteTempRenderer.color = c;
#if UNITY_EDITOR
                                Debug.Log($"[Customer] Set OrderSpriteTemp initial alpha to 0");
#endif
                            }
                        }
                    }
                }
            }
        }
    }

    public void Initialize(
        CustomerData customerData,
        Transform counterPoint,
        Transform exit = null,
        Transform openDoor = null,
        Transform door = null,
        GameObject doorClosedObj = null,
        GameObject doorOpenObj = null)
    {
        data = customerData;
        targetCounterPoint = counterPoint;
        exitPoint = exit;
        openDoorPoint = openDoor;
        doorPoint = door;
        doorClosed = doorClosedObj;
        doorOpen = doorOpenObj;

        // door audio discovery
        doorAudio =
            (doorPoint != null ? (doorPoint.GetComponent<DoorAudioFeedback>() ?? doorPoint.GetComponentInParent<DoorAudioFeedback>() ?? doorPoint.GetComponentInChildren<DoorAudioFeedback>(true)) : null)
            ?? (doorClosed != null ? (doorClosed.GetComponent<DoorAudioFeedback>() ?? doorClosed.GetComponentInParent<DoorAudioFeedback>() ?? doorClosed.GetComponentInChildren<DoorAudioFeedback>(true)) : null)
            ?? (doorOpen != null ? (doorOpen.GetComponent<DoorAudioFeedback>() ?? doorOpen.GetComponentInParent<DoorAudioFeedback>() ?? doorOpen.GetComponentInChildren<DoorAudioFeedback>(true)) : null);

        if (doorClosed != null) doorClosed.SetActive(true);
        if (doorOpen != null) doorOpen.SetActive(false);

        // push customer behind door initially - optimized with array to avoid dictionary resizing
        var allRenderers = GetComponentsInChildren<SpriteRenderer>(false); // don't need inactive renderers
        int rendererCount = allRenderers.Length;
        for (int i = 0; i < rendererCount; i++)
        {
            var r = allRenderers[i];
            originalSortingOrders[r] = r.sortingOrder;
            r.sortingOrder = spawnSortingOrder;
        }

        if (bodyTransform != null)
            transform.localScale = Vector3.one * startScale;

        // set body sprite to default mood on spawn
        SetBodyDefault();

        // Check if it's raining and apply rain color if needed
        if (GameManager.Instance != null && GameManager.Instance.isRaining)
        {
            spawnedDuringRain = true;
            ApplyRainColor();
        }

        // check ordering point - use string.Equals for better performance
        if (counterPoint != null && string.Equals(counterPoint.name, orderingCounterPointName, System.StringComparison.Ordinal))
        {
            canOrder = true;
#if UNITY_EDITOR
            Debug.Log($"{customerData.customerName} assigned to ordering counter - can place order");
#endif
        }
        else
        {
            canOrder = false;
#if UNITY_EDITOR
            Debug.Log($"{customerData.customerName} assigned to waiting point ({counterPoint?.name}) - will only greet");
#endif
        }

        maxPatienceTime = data.patienceTime;
        patienceTimer = maxPatienceTime;

        // Pre-calculate bounce speed multipliers
        bounceMoveSpeedMultiplier = moveSpeed * bounceFrequency;
        bounceExitSpeedMultiplier = (moveSpeed + 1f) * bounceFrequency; // for incorrect orders

        currentOrderPreference = data.GetRandomOrder();
        currentOrder = currentOrderPreference != null ? currentOrderPreference.orderName : "Unknown Order";
        if (currentOrderPreference == null)
        {
#if UNITY_EDITOR
            Debug.LogWarning($"{customerData.customerName} has no valid order preferences!");
#endif
        }

        // Subscribe to counter assignment changes
        var manager = FindFirstObjectByType<CustomerManager>();
        if (manager != null)
            manager.OnCounterAssignmentsChanged += OnCounterAssignmentsChangedHandler;

        StartCoroutine(CustomerRoutine());
    }

    public void Update()
    {
        // Early return if not in waiting state - most common case first
        if (currentState != CustomerState.Waiting) return;

        // Only drain patience when customer is waiting at the correct counter and hasn't received their drink
        if (!hasReceivedOrder && isAtCounter && canOrder)
        {
            patienceTimer -= Time.deltaTime;

            // Clamp immediately to avoid extra check
            if (patienceTimer < 0f)
                patienceTimer = 0f;

            // Cache normalized value to avoid recalculating
            float newNormalizedPatience = patienceTimer / maxPatienceTime;

            // Only fire event if patience changed by more than 5% to reduce event overhead
            if (Mathf.Abs(newNormalizedPatience - cachedNormalizedPatience) > 0.05f)
            {
                cachedNormalizedPatience = newNormalizedPatience;
                OnPatienceChanged?.Invoke(cachedNormalizedPatience);
            }

            // If below 20% patience and not already showing disappointment
            // Use new value for check to ensure it's current
            if (newNormalizedPatience <= 0.2f && !patienceEventTriggered)
            {
                patienceEventTriggered = true;
                SetBodyDisappointed();
            }
        }
    }

    void OnDestroy()
    {
        var manager = FindFirstObjectByType<CustomerManager>();
        if (manager != null)
            manager.OnCounterAssignmentsChanged -= OnCounterAssignmentsChangedHandler;
    }

    void OnCounterAssignmentsChangedHandler()
    {
        // Only customers who can't order (waiting at waiting points or arriving to waiting points) should respond
        // Customers at the ordering counter or already able to order should not reposition
        if (canOrder)
            return;

        // Only respond if arriving or waiting (not ordering, receiving, or leaving)
        if (currentState != CustomerState.Arriving && currentState != CustomerState.Waiting)
            return;

        // Check if there's a better position available
        var manager = FindFirstObjectByType<CustomerManager>();
        Transform betterPosition = manager?.GetBestAvailableCounterPointForMe(this);

        if (betterPosition != null && betterPosition != targetCounterPoint)
        {
            // If we're currently in the middle of moving (Arriving state), just set the flag
            if (currentState == CustomerState.Arriving)
            {
                shouldRecheckPosition = true;
            }
            // If we're already waiting (standing still), we need to start moving again
            else if (currentState == CustomerState.Waiting)
            {
#if UNITY_EDITOR
                Debug.Log($"[{data.customerName}] Moving from {targetCounterPoint.name} to {betterPosition.name}");
#endif
                targetCounterPoint = betterPosition;

                // Update canOrder based on new position
                canOrder = betterPosition.name == orderingCounterPointName;

                // Start moving to new position
                StopAllCoroutines();
                StartCoroutine(MoveToNewPosition());
            }
        }
    }

    IEnumerator MoveToNewPosition()
    {
        currentState = CustomerState.Arriving;
        isAtCounter = false;

        yield return StartCoroutine(MoveToCounter());

        isAtCounter = true;
        currentState = CustomerState.Waiting;

        // If we moved to the ordering counter, we need to greet and order
        if (canOrder)
        {
            // This customer was already waiting, so they should now greet and order
            yield return StartCoroutine(WaitForDialogueTurn());

            currentState = CustomerState.Greeting;

            if (data.greetingDialogue != null)
            {
                string greeting = data.greetingDialogue.GetRandomLine();
                Speak(greeting);
            }

            // Wait for dialogue to finish (either typed or skipped)
            if (dialoguePopup != null)
            {
                while (dialoguePopup.gameObject.activeSelf && dialoguePopup.canvasGroup != null && dialoguePopup.canvasGroup.alpha > 0f)
                {
                    yield return null;
                }
            }

            currentState = CustomerState.Ordering;

            if (currentOrderPreference != null && currentOrderPreference.orderDialogue != null)
            {
                string orderText = currentOrderPreference.orderDialogue.GetRandomLine();
                Speak(orderText);
            }

            OnOrderPlaced?.Invoke(currentOrder);

            // Wait for dialogue to finish (either typed or skipped), then wait for pause
            if (dialoguePopup != null)
            {
                while (dialoguePopup.gameObject.activeSelf && dialoguePopup.canvasGroup != null && dialoguePopup.canvasGroup.alpha > 0f)
                {
                    yield return null;
                }
            }

            // Short pause after dialogue finishes
            yield return new WaitForSeconds(orderPauseDuration);

            ReleaseDialogueTurn();

            if (orderRoot != null)
            {
                orderRoot.gameObject.SetActive(true);
                ShowOrderTemperatureSprite();
            }

            currentState = CustomerState.Waiting;
        }
        else
        {
            // Still at a waiting point, notify that we're ready to advance if counter opens
            OnReadyToAdvance?.Invoke();
        }
    }

    // -------------------------------
    // Temperature Sprite Helper
    // -------------------------------

    public void SetOrderTemperatureSprite(WaterSource waterSource)
    {
#if UNITY_EDITOR
        Debug.Log($"[Customer] SetOrderTemperatureSprite called with waterSource: {waterSource}");
#endif

        if (orderSpriteTempRenderer == null)
        {
#if UNITY_EDITOR
            Debug.LogWarning($"[Customer] orderSpriteTempRenderer is NULL - cannot set temperature sprite!");
#endif
            return;
        }

        // Set sprite based on water source (hot or cold)
        if (waterSource == WaterSource.Hot)
        {
            orderSpriteTempRenderer.sprite = hotSprite;
#if UNITY_EDITOR
            Debug.Log($"[Customer] Set HOT sprite: {(hotSprite != null ? "SUCCESS" : "FAILED - hotSprite is null")}");
#endif
        }
        else if (waterSource == WaterSource.Cold)
        {
            orderSpriteTempRenderer.sprite = coldSprite;
#if UNITY_EDITOR
            Debug.Log($"[Customer] Set COLD sprite: {(coldSprite != null ? "SUCCESS" : "FAILED - coldSprite is null")}");
#endif
        }
        else
        {
            // For other water sources (if any), default to no sprite
            orderSpriteTempRenderer.sprite = null;
#if UNITY_EDITOR
            Debug.Log($"[Customer] WaterSource is {waterSource} (not Hot or Cold) - no sprite set");
#endif
            return;
        }

        // Start with alpha 0 - will be faded in when order becomes visible
        Color c = orderSpriteTempRenderer.color;
        c.a = 0f;
        orderSpriteTempRenderer.color = c;
#if UNITY_EDITOR
        Debug.Log($"[Customer] Set temperature sprite alpha to 0, ready for fade-in");
#endif
    }

    public void ShowOrderTemperatureSprite()
    {
#if UNITY_EDITOR
        Debug.Log($"[Customer] ShowOrderTemperatureSprite called");
        Debug.Log($"[Customer] orderSpriteTempRenderer: {(orderSpriteTempRenderer != null ? "EXISTS" : "NULL")}");
        Debug.Log($"[Customer] orderSpriteTempRenderer.sprite: {(orderSpriteTempRenderer != null && orderSpriteTempRenderer.sprite != null ? orderSpriteTempRenderer.sprite.name : "NULL")}");
#endif

        if (orderSpriteTempRenderer != null && orderSpriteTempRenderer.sprite != null)
        {
#if UNITY_EDITOR
            Debug.Log($"[Customer] Starting FadeInTemperatureSprite coroutine");
#endif
            StartCoroutine(FadeInTemperatureSprite());
        }
        else
        {
#if UNITY_EDITOR
            Debug.LogWarning($"[Customer] Cannot show temperature sprite - renderer or sprite is null!");
#endif
        }
    }

    IEnumerator FadeInTemperatureSprite()
    {
#if UNITY_EDITOR
        Debug.Log($"[Customer] FadeInTemperatureSprite coroutine started");
#endif

        if (orderSpriteTempRenderer == null)
        {
#if UNITY_EDITOR
            Debug.LogWarning($"[Customer] FadeIn failed - orderSpriteTempRenderer is null");
#endif
            yield break;
        }

        float fadeDuration = 0.3f;
        float elapsed = 0f;
        Color c = orderSpriteTempRenderer.color;

#if UNITY_EDITOR
        Debug.Log($"[Customer] Starting fade from alpha {c.a} to 1.0 over {fadeDuration}s");
#endif

        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            c.a = Mathf.Lerp(0f, 1f, elapsed / fadeDuration);
            orderSpriteTempRenderer.color = c;
            yield return null;
        }

        // Ensure fully visible
        c.a = 1f;
        orderSpriteTempRenderer.color = c;
#if UNITY_EDITOR
        Debug.Log($"[Customer] Temperature sprite fade complete - alpha now: {orderSpriteTempRenderer.color.a}");
#endif
    }

    // -------------------------------
    // Mood Sprite Helpers
    // -------------------------------

    public void SetBodyDefault()
    {
        if (bodySpriteRenderer != null && defaultBodySprite != null)
            bodySpriteRenderer.sprite = defaultBodySprite;

        isDisappointed = false;
        patienceEventTriggered = false; // Reset event flag
    }

    public void SetBodyHappy()
    {
        if (bodySpriteRenderer != null && happyBodySprite != null)
            bodySpriteRenderer.sprite = happyBodySprite;

        isDisappointed = false;
        patienceEventTriggered = false; // Reset event flag
    }

    public void SetBodyDisappointed()
    {
        if (bodySpriteRenderer != null && disappointedBodySprite != null)
            bodySpriteRenderer.sprite = disappointedBodySprite;

        isDisappointed = true;
    }

    // -------------------------------
    // Rain Color Helpers
    // -------------------------------

    void ApplyRainColor()
    {
        if (bodySpriteRenderer == null) return;

        // Set rain color (gray - B3B3B3)
        bodySpriteRenderer.color = rainColor;

#if UNITY_EDITOR
        Debug.Log($"[Customer] {data.customerName} spawned during rain - applying gray color");
#endif

        // Start fading back to white
        if (rainColorFadeCoroutine != null)
        {
            StopCoroutine(rainColorFadeCoroutine);
        }
        rainColorFadeCoroutine = StartCoroutine(FadeRainColorToWhite());
    }

    IEnumerator FadeRainColorToWhite()
    {
        if (bodySpriteRenderer == null) yield break;

        float elapsed = 0f;
        Color startColor = rainColor;
        Color targetColor = Color.white;

        while (elapsed < rainColorFadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / rainColorFadeDuration;
            bodySpriteRenderer.color = Color.Lerp(startColor, targetColor, t);
            yield return null;
        }

        // Ensure we reach white
        bodySpriteRenderer.color = targetColor;

#if UNITY_EDITOR
        Debug.Log($"[Customer] {data.customerName} rain color faded to white");
#endif

        rainColorFadeCoroutine = null;
    }

    // -------------------------------
    // Dialogue System Integration
    // -------------------------------

    public float Speak(string line)
    {
        if (string.IsNullOrEmpty(line)) return 0f;

        if (line.Contains("{order}") && !string.IsNullOrEmpty(currentOrder))
        {
            string displayName = ResolveOrderDisplayName(currentOrder);
            line = line.Replace("{order}", displayName);
        }

        if (dialoguePopup != null)
        {
            ApplyDialogSpeechSettings(dialoguePopup);
            dialoguePopup.SetText(line);
            dialoguePopup.ShowForDuration(dialogueDuration);

            // Return the total time including typing
            return dialoguePopup.GetTotalDisplayTime(dialogueDuration);
        }

        return 0f;
    }

    protected void ApplyDialogSpeechSettings(DialoguePopup popup)
    {
        if (popup == null) return;
        popup.ApplyDialogSpeechSettings(data?.dialogSpeechSettings);
    }

    // --------------------------------
    // Dialogue Queue Lock / Release
    // --------------------------------

    public IEnumerator WaitForDialogueTurn()
    {
        dialogueQueue.Enqueue(this);
        while (dialogueQueue.Count > 0 && dialogueQueue.Peek() != this)
            yield return null;

        while (isDialoguePlaying)
            yield return null;

        isDialoguePlaying = true;
    }

    public void ReleaseDialogueTurn()
    {
        if (dialogueQueue.Count > 0 && dialogueQueue.Peek() == this)
            dialogueQueue.Dequeue();

        isDialoguePlaying = false;
    }

    void RemoveFromDialogueQueue()
    {
        if (dialogueQueue.Count > 0 && dialogueQueue.Peek() == this)
        {
            dialogueQueue.Dequeue();
            isDialoguePlaying = false;
        }
        else
        {
            var temp = new Queue<Customer>();
            while (dialogueQueue.Count > 0)
            {
                var c = dialogueQueue.Dequeue();
                if (c != this) temp.Enqueue(c);
            }
            dialogueQueue = temp;
        }
    }

    // -------------------------------
    // Order Reception & Reactions
    // -------------------------------

    public void ReceiveOrder(string orderReceived)
    {
        if (!GameManager.Instance.hasCompletedTutorial)
        {
            var tutorialManager = FindFirstObjectByType<TutorialManager>();
            if (tutorialManager != null)
                tutorialManager.OnTutorialDrinkServed();
        }

        if (!autoDialogueEnabled)
        {
#if UNITY_EDITOR
            Debug.Log("[Customer] Auto dialogue disabled during tutorial.");
#endif
            return;
        }

        if (hasReceivedOrder) return;

        // Must be at the counter, waiting, and able to order
        if (!isAtCounter || currentState != CustomerState.Waiting || !canOrder)
        {
#if UNITY_EDITOR
            Debug.LogWarning($"{data.customerName} is not ready to receive an order (canOrder: {canOrder}, state: {currentState})");
#endif
            return;
        }

        hasReceivedOrder = true;

        // Hide order UI
        if (orderRoot != null)
            orderRoot.gameObject.SetActive(false);

        currentState = CustomerState.Receiving;

        // Slide cup animation trigger
        if (cupPickupPoint != null)
            OnCupRequested?.Invoke(cupPickupPoint);

        // Check if correct order
        if (orderReceived == currentOrder)
        {
            SetBodyHappy();
            float dialogueDuration = HandleCorrectOrderResponse();
            StartCoroutine(LeaveHappyAfterDialogue(dialogueDuration));
            if (this is TutorialCustomer)
            {
#if UNITY_EDITOR
                Debug.Log("[Customer] Tutorial customer served — notifying TutorialManager.");
#endif
                if (TutorialManager.Instance != null)
                {
                    TutorialManager.Instance.OnTutorialOrderComplete(true);
                }
            }
        }
        else
        {
            receivedIncorrectOrder = true;
            SetBodyDisappointed();
            float dialogueDuration = HandleIncorrectOrderResponse();
            StartCoroutine(LeaveSadAfterDialogue(dialogueDuration));
            if (this is TutorialCustomer)
            {
#if UNITY_EDITOR
                Debug.Log("[Customer] Tutorial customer served — notifying TutorialManager.");
#endif
                if (TutorialManager.Instance != null)
                {
                    TutorialManager.Instance.OnTutorialOrderComplete(true);
                }
            }
        }
    }

    float HandleCorrectOrderResponse()
    {
        float speakDuration = 0f;

        if (!GameManager.Instance.hasCompletedTutorial)
        {
#if UNITY_EDITOR
            Debug.Log("[TutorialManager] Skipping normal customer response during tutorial completion");
#endif
            return speakDuration;
        }

        if (data.happyDialogue != null)
        {
            string line = data.happyDialogue.GetRandomLine();
            speakDuration = Speak(line);
        }

        bool isMaxTip = GameManager.Instance != null && GameManager.Instance.RollForMaxTip(data);
        float tipAmount = isMaxTip ? data.specialMaxTipAmount : Random.Range(data.minTipAmount, data.maxTipAmount);

        ShowTip(tipAmount, isMaxTip);
        GameManager.Instance?.AddTip(tipAmount, data, isMaxTip);
        GameManager.Instance?.RegisterCorrectService();

        return speakDuration;
    }

    float HandleIncorrectOrderResponse()
    {
        float speakDuration = 0f;

        if (!GameManager.Instance.hasCompletedTutorial)
        {
#if UNITY_EDITOR
            Debug.Log("[TutorialManager] Skipping normal customer response during tutorial completion");
#endif
            return speakDuration;
        }

        if (data.disappointedDialogue != null)
        {
            string line = data.disappointedDialogue.GetRandomLine();
            speakDuration = Speak(line);
        }

        GameManager.Instance?.RegisterIncorrectService();

        return speakDuration;
    }

    // -------------------------------
    // Tip Floating & Fade
    // -------------------------------

    void ShowTip(float amount, bool isMax)
    {
        if (tipTMP == null) return;

        tipTMP.text = $"+${amount:F2}";
        tipTMP.color = isMax ? maxTipColor : normalTipColor;
        tipTMP.gameObject.SetActive(true);

        StartCoroutine(AnimateTip());
    }

    IEnumerator AnimateTip()
    {
        if (tipTMP == null) yield break;

        Transform tipRoot = tipTMP.transform.parent; // "Tip" gameobject with separate canvas
        tipRoot.gameObject.SetActive(true);

        // Detach from BearCanvas so it stays in world space when customer leaves
        tipRoot.SetParent(null, true); // Keep world position

        // Fix canvas settings so it renders in world properly
        var tipCanvas = tipRoot.GetComponent<Canvas>();
        if (tipCanvas != null)
        {
            tipCanvas.renderMode = RenderMode.WorldSpace; // failsafe
            tipCanvas.worldCamera = Camera.main; // failsafe
        }

        // Cache start values
        Vector3 startPos = tipRoot.position;
        Vector3 startScale = tipRoot.localScale;
        Color startColor = tipTMP.color;

        // Pre-calculate constants outside loop to reduce per-frame calculations
        float invDuration = 1f / tipFloatDuration;
        Vector3 targetScale = startScale * tipTargetScale;
        Vector3 upOffset = Vector3.up * tipFloatHeight;

        float elapsed = 0f;

        // Animate: float upward + fade
        while (elapsed < tipFloatDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed * invDuration; // Use pre-calculated inverse

            // Move upward in world space (necessary so tip stays in place when customer leaves)
            tipRoot.position = startPos + upOffset * t;

            // Scale effect - use Vector3.LerpUnclamped for slight performance gain
            tipRoot.localScale = Vector3.LerpUnclamped(startScale, targetScale, t);

            // Fade out in second half - optimized calculation
            if (t > 0.5f)
            {
                float fadeT = (t - 0.5f) * 2f;
                Color c = startColor;
                c.a = 1f - fadeT; // Slightly faster than Mathf.Lerp
                tipTMP.color = c;
            }

            yield return null;
        }

        // Clean up after animation
        Destroy(tipRoot.gameObject);
    }

    // -------------------------------
    // Leaving (Happy / Sad)
    // -------------------------------

    IEnumerator LeaveHappyAfterDialogue(float dialogueDuration)
    {
        // Wait for dialogue to finish (either typed or skipped)
        if (dialoguePopup != null)
        {
            // Wait until dialogue is no longer showing (fully typed or skipped)
            while (dialoguePopup.gameObject.activeSelf && dialoguePopup.canvasGroup != null && dialoguePopup.canvasGroup.alpha > 0f)
            {
                yield return null;
            }
        }

        currentState = CustomerState.Leaving;
        isAtCounter = false;
        OnReadyToAdvance?.Invoke();

        yield return StartCoroutine(WalkToExit());
        OnLeave?.Invoke();
        Destroy(gameObject);
    }

    IEnumerator LeaveSadAfterDialogue(float dialogueDuration)
    {
        // Wait for dialogue to finish (either typed or skipped)
        if (dialoguePopup != null)
        {
            // Wait until dialogue is no longer showing (fully typed or skipped)
            while (dialoguePopup.gameObject.activeSelf && dialoguePopup.canvasGroup != null && dialoguePopup.canvasGroup.alpha > 0f)
            {
                yield return null;
            }
        }

        currentState = CustomerState.Leaving;
        isAtCounter = false;
        OnReadyToAdvance?.Invoke();

        yield return StartCoroutine(WalkToExit());
        OnLeave?.Invoke();
        Destroy(gameObject);
    }

    // Legacy methods kept for compatibility (not used in normal flow)
    IEnumerator LeaveHappy()
    {
        yield return waitTwoSeconds;
        currentState = CustomerState.Leaving;
        isAtCounter = false;
        OnReadyToAdvance?.Invoke();
        yield return waitOneSecond;

        yield return StartCoroutine(WalkToExit());
        OnLeave?.Invoke();
        Destroy(gameObject);
    }

    IEnumerator LeaveSad()
    {
        yield return waitTwoSeconds;
        currentState = CustomerState.Leaving;
        isAtCounter = false;
        OnReadyToAdvance?.Invoke();
        yield return waitOneSecond;

        yield return StartCoroutine(WalkToExit());
        OnLeave?.Invoke();
        Destroy(gameObject);
    }

    // ---------------------------------
    // Main Customer Flow Routine
    // ---------------------------------

    IEnumerator CustomerRoutine()
    {
        currentState = CustomerState.Arriving;

        if (targetCounterPoint != null)
            yield return StartCoroutine(MoveToCounter());

        isAtCounter = true;
        ApplyQueueSortingOrderIfReady();

        // If this is actual ordering point → greet and place order
        if (canOrder)
        {
            // Wait for dialogue turn to greet
            yield return StartCoroutine(WaitForDialogueTurn());
            currentState = CustomerState.Greeting;

            if (data.greetingDialogue != null)
            {
                string greeting = data.greetingDialogue.GetRandomLine();
                Speak(greeting);
            }

            // Wait for dialogue to finish (either typed or skipped)
            if (dialoguePopup != null)
            {
                // Wait until dialogue is no longer showing (fully typed or skipped)
                while (dialoguePopup.gameObject.activeSelf && dialoguePopup.canvasGroup != null && dialoguePopup.canvasGroup.alpha > 0f)
                {
                    yield return null;
                }
            }

            currentState = CustomerState.Ordering;

            if (currentOrderPreference != null && currentOrderPreference.orderDialogue != null)
            {
                string orderText = currentOrderPreference.orderDialogue.GetRandomLine();
                Speak(orderText);
            }

            OnOrderPlaced?.Invoke(currentOrder);

            // Wait for dialogue to finish (either typed or skipped), then wait for pause
            if (dialoguePopup != null)
            {
                // Wait until dialogue is no longer showing (fully typed or skipped)
                while (dialoguePopup.gameObject.activeSelf && dialoguePopup.canvasGroup != null && dialoguePopup.canvasGroup.alpha > 0f)
                {
                    yield return null;
                }
            }

            // Short pause after dialogue finishes
            yield return new WaitForSeconds(orderPauseDuration);

            ReleaseDialogueTurn();

            if (orderRoot != null)
            {
                orderRoot.gameObject.SetActive(true);
                ShowOrderTemperatureSprite();
            }

            currentState = CustomerState.Waiting;
        }
        else
        {
            // At waiting point - skip all dialogue, just wait silently
            currentState = CustomerState.Waiting;
#if UNITY_EDITOR
            Debug.Log($"{data.customerName} waiting silently at {targetCounterPoint.name}");
#endif

            // Notify manager that this customer is now ready to advance if a spot opens
            OnReadyToAdvance?.Invoke();
        }
    }

    // ---------------------------------
    // Basic Movement to Counter
    // ---------------------------------

    public IEnumerator MoveToCounter()
    {
        Vector3 startPos = transform.position;

        // Step 1: Move to openDoorPoint (if any) - skip if already entered
        if (openDoorPoint != null && !hasEnteredThroughDoor)
        {
            Vector3 doorOpenPos = openDoorPoint.position;
            while (Vector3.Distance(transform.position, doorOpenPos) > 0.1f)
            {
                float deltaTime = Time.deltaTime;
                transform.position = Vector3.MoveTowards(
                    transform.position, doorOpenPos, moveSpeed * deltaTime);

                // Update bounce timer based on movement speed - use pre-calculated multiplier
                bounceTimer += deltaTime * bounceMoveSpeedMultiplier;
                float bounce = Mathf.Abs(Mathf.Sin(bounceTimer)) * bounceHeight;

                float prog = Mathf.InverseLerp(startPos.y, doorOpenPos.y, transform.position.y);
                float scale = Mathf.Lerp(startScale, startScale * 1.05f, prog);
                if (bodyTransform != null)
                {
                    transform.localScale = Vector3.one * scale;
                    // Apply bounce to body Y position - reuse Vector3 to reduce allocations
                    Vector3 bodyPos = bodyTransform.localPosition;
                    bodyPos.y = bounce;
                    bodyTransform.localPosition = bodyPos;
                }

                yield return null;
            }

            if (doorClosed != null) doorClosed.SetActive(false);
            if (doorOpen != null) doorOpen.SetActive(true);
            doorAudio?.PlayOpen();

            // Fire event that door has opened
            OnDoorOpened?.Invoke();

            // Notify GameManager if entering during rain (for wet floor tracking)
            if (spawnedDuringRain && GameManager.Instance != null)
            {
                GameManager.Instance.OnCustomerEnteredDuringRain();
            }

            foreach (var kvp in originalSortingOrders)
            {
                if (kvp.Key != null)
                    kvp.Key.sortingOrder = kvp.Value;
            }

            yield return waitPointThreeSeconds;
        }

        // Step 2: Move through doorPoint - skip if already entered
        if (doorPoint != null && !hasEnteredThroughDoor)
        {
            Vector3 midPos = transform.position;
            Vector3 doorPos = doorPoint.position;
            while (Vector3.Distance(transform.position, doorPos) > 0.1f)
            {
                float deltaTime = Time.deltaTime;
                transform.position = Vector3.MoveTowards(
                    transform.position, doorPos, moveSpeed * deltaTime);

                // Update bounce timer based on movement speed - use pre-calculated multiplier
                bounceTimer += deltaTime * bounceMoveSpeedMultiplier;
                float bounce = Mathf.Abs(Mathf.Sin(bounceTimer)) * bounceHeight;

                float prog = Mathf.InverseLerp(midPos.y, doorPos.y, transform.position.y);
                float scale = Mathf.Lerp(startScale * 1.05f, startScale * 1.1f, prog);
                if (bodyTransform != null)
                {
                    transform.localScale = Vector3.one * scale;
                    // Apply bounce to body Y position - reuse Vector3 to reduce allocations
                    Vector3 bodyPos = bodyTransform.localPosition;
                    bodyPos.y = bounce;
                    bodyTransform.localPosition = bodyPos;
                }

                yield return null;
            }
        }

        // Mark that we've entered through the door
        hasEnteredThroughDoor = true;
        ApplyQueueSortingOrderIfReady();

        // Step 3: Move to assigned counter/wait point
        Vector3 fromPos = transform.position;
        Vector3 targetPos = targetCounterPoint.position;
        
        // Apply Y offset from customer data
        if (data != null)
        {
            targetPos.y += data.counterYOffset;
        }
        
        float totalDist = Vector3.Distance(fromPos, targetPos);

        // Store current scale at start of movement
        float startingScale = transform.localScale.x;

        float elapsed = 0f;
        bool doorClosedAlready = false;
        float timeToCloseDoor = 0.7f;

        while (Vector3.Distance(transform.position, targetPos) > 0.1f)
        {
            // Check if we should recalculate target (event-driven, not per-frame)
            if (shouldRecheckPosition)
            {
                shouldRecheckPosition = false;

                var manager = FindFirstObjectByType<CustomerManager>();
                Transform newTarget = manager?.GetBestAvailableCounterPointForMe(this);

                if (newTarget != null && newTarget != targetCounterPoint)
                {
                    // Seamlessly redirect to new position
                    targetCounterPoint = newTarget;
                    targetPos = targetCounterPoint.position;

                    if (data != null)
                        targetPos.y += data.counterYOffset;

                    // Recalculate for smooth transition
                    fromPos = transform.position;
                    totalDist = Vector3.Distance(fromPos, targetPos);
                    startingScale = transform.localScale.x;

#if UNITY_EDITOR
                    Debug.Log($"[{data.customerName}] Redirecting to {newTarget.name} mid-movement");
#endif
                }
            }

            float deltaTime = Time.deltaTime;
            transform.position = Vector3.MoveTowards(transform.position, targetPos, moveSpeed * deltaTime);

            // Update bounce timer based on movement speed - use pre-calculated multiplier
            bounceTimer += deltaTime * bounceMoveSpeedMultiplier;
            float bounce = Mathf.Abs(Mathf.Sin(bounceTimer)) * bounceHeight;

            float dist = Vector3.Distance(fromPos, transform.position);
            float prog = Mathf.Clamp01(dist / totalDist);

            // Only interpolate scale if we just entered (otherwise keep current scale)
            float currentScale = startingScale;
            if (startingScale < targetScale)
            {
                // We're scaling up from door entry
                currentScale = Mathf.Lerp(startingScale, targetScale, prog);
            }

            if (bodyTransform != null)
            {
                transform.localScale = Vector3.one * currentScale;
                // Apply bounce to body Y position - reuse Vector3 to reduce allocations
                Vector3 bodyPos = bodyTransform.localPosition;
                bodyPos.y = bounce;
                bodyTransform.localPosition = bodyPos;
            }

            elapsed += deltaTime;
            if (!doorClosedAlready && elapsed >= timeToCloseDoor)
            {
                if (doorClosed != null) doorClosed.SetActive(true);
                if (doorOpen != null) doorOpen.SetActive(false);
                doorAudio?.PlayClose();
                doorClosedAlready = true;
            }

            yield return null;
        }

        transform.position = targetPos;
        if (bodyTransform != null)
        {
            transform.localScale = Vector3.one * targetScale;
            // Reset body position after movement
            bodyTransform.localPosition = Vector3.zero;
        }

#if UNITY_EDITOR
        Debug.Log($"{data.customerName} arrived at {targetCounterPoint.name}");
#endif
    }

    // ---------------------------------
    // Leaving / Exit Movement
    // ---------------------------------

    IEnumerator WalkToExit()
    {
        if (exitPoint == null)
        {
#if UNITY_EDITOR
            Debug.LogWarning($"{data.customerName} has no exit point!");
#endif
            yield break;
        }

        // Reset sorting order to base value when leaving
        if (bodySpriteRenderer != null)
        {
            bodySpriteRenderer.sortingOrder = 650;
        }

        // Use faster speed if they got incorrect order
        float exitSpeed = receivedIncorrectOrder ? moveSpeed + 1f : moveSpeed;
        float exitBounceMultiplier = receivedIncorrectOrder ? bounceExitSpeedMultiplier : bounceMoveSpeedMultiplier;

        Vector3 startPos = transform.position;

        // --- STEP 1: WALK BACK TO THE INTERIOR DOOR POINT (doorPoint) ---
        if (doorPoint != null)
        {
            Vector3 doorPos = doorPoint.position;
            while (Vector3.Distance(transform.position, doorPos) > 0.1f)
            {
                float deltaTime = Time.deltaTime;
                transform.position = Vector3.MoveTowards(transform.position, doorPos, exitSpeed * deltaTime);

                // Bounce + scale while moving - use pre-calculated multiplier
                bounceTimer += deltaTime * exitBounceMultiplier;
                float bounce = Mathf.Abs(Mathf.Sin(bounceTimer)) * bounceHeight;

                float prog = Mathf.InverseLerp(startPos.y, doorPos.y, transform.position.y);
                float sc = Mathf.Lerp(targetScale, startScale * 1.1f, prog);
                if (bodyTransform != null)
                {
                    transform.localScale = Vector3.one * sc;
                    // Apply bounce to body Y position - reuse Vector3 to reduce allocations
                    Vector3 bodyPos = bodyTransform.localPosition;
                    bodyPos.y = bounce;
                    bodyTransform.localPosition = bodyPos;
                }

                yield return null;
            }
            yield return waitPointTwoSeconds;
        }

        // --- STEP 2: WALK TO openDoorPoint ---
        if (openDoorPoint != null)
        {
            Vector3 midPos = transform.position;
            Vector3 openPos = openDoorPoint.position;
            while (Vector3.Distance(transform.position, openPos) > 0.1f)
            {
                float deltaTime = Time.deltaTime;
                transform.position = Vector3.MoveTowards(transform.position, openPos, exitSpeed * deltaTime);

                bounceTimer += deltaTime * exitBounceMultiplier;
                float bounce = Mathf.Abs(Mathf.Sin(bounceTimer)) * bounceHeight;

                float prog = Mathf.InverseLerp(midPos.y, openPos.y, transform.position.y);
                float sc = Mathf.Lerp(startScale * 1.1f, startScale * 1.05f, prog);
                if (bodyTransform != null)
                {
                    transform.localScale = Vector3.one * sc;
                    // Apply bounce to body Y position - reuse Vector3 to reduce allocations
                    Vector3 bodyPos = bodyTransform.localPosition;
                    bodyPos.y = bounce;
                    bodyTransform.localPosition = bodyPos;
                }

                yield return null;
            }

            // --- OPEN DOOR ONLY IF CHILD HASN'T ALREADY OPENED IT ---
            bool doorAlreadyOpen = (doorOpen != null && doorOpen.activeSelf);
            if (!doorAlreadyOpen)
            {
                if (doorClosed != null) doorClosed.SetActive(false);
                if (doorOpen != null) doorOpen.SetActive(true);
                doorAudio?.PlayOpen();
                OnDoorOpened?.Invoke();
            }

            // Push sorting order behind door mask - use cached renderers from dictionary
            foreach (var kvp in originalSortingOrders)
            {
                if (kvp.Key != null)
                    kvp.Key.sortingOrder = -5;
            }

            // Door STAYS OPEN – DO NOT CLOSE IT HERE
            yield return waitPointOneSeconds;
        }

        // --- STEP 3: WALK FROM DOOR TO EXIT POINT OUTSIDE ---
        Vector3 fromPos = transform.position;
        Vector3 targetPos = exitPoint.position;
        float totalDist = Vector3.Distance(fromPos, targetPos);

        while (Vector3.Distance(transform.position, targetPos) > 0.1f)
        {
            float deltaTime = Time.deltaTime;
            transform.position = Vector3.MoveTowards(transform.position, targetPos, exitSpeed * deltaTime);

            bounceTimer += deltaTime * exitBounceMultiplier;
            float bounce = Mathf.Abs(Mathf.Sin(bounceTimer)) * bounceHeight;

            float dist = Vector3.Distance(fromPos, transform.position);
            float prog = Mathf.Clamp01(dist / totalDist);
            float sc = Mathf.Lerp(startScale * 1.05f, startScale, prog);

            if (bodyTransform != null)
            {
                transform.localScale = Vector3.one * sc;
                bodyTransform.localPosition = new Vector3(
                    bodyTransform.localPosition.x,
                    bounce,
                    bodyTransform.localPosition.z);
            }

            yield return null;
        }

        // --- NOW THAT MAIN CUSTOMER IS OUTSIDE → CLOSE THE DOOR ---
        if (doorClosed != null) doorClosed.SetActive(true);
        if (doorOpen != null) doorOpen.SetActive(false);
        doorAudio?.PlayClose();

        transform.position = targetPos;

        if (bodyTransform != null)
        {
            transform.localScale = Vector3.one * startScale;
            bodyTransform.localPosition = Vector3.zero;
        }

#if UNITY_EDITOR
        Debug.Log($"{data.customerName} exited");
#endif
    }

    // ---------------------------------
    // Shop Closed Handler
    // ---------------------------------

    void HandleShopClosed()
    {
        if (currentState != CustomerState.Leaving && !hasReceivedOrder)
        {
#if UNITY_EDITOR
            Debug.Log($"{data.customerName} is leaving because shop closed.");
#endif

            if (orderRoot != null)
                orderRoot.gameObject.SetActive(false);

            // Hide any active dialogue immediately
            if (dialoguePopup != null)
                dialoguePopup.HideDialogue();

            RemoveFromDialogueQueue();

            StopAllCoroutines();
            StartCoroutine(LeaveWithoutDialogue());
        }
    }

    IEnumerator LeaveWithoutDialogue()
    {
        currentState = CustomerState.Leaving;
        isAtCounter = false;

        yield return StartCoroutine(WalkToExit());
        OnLeave?.Invoke();
        Destroy(gameObject);
    }

    // ---------------------------------
    // Public info helpers
    // ---------------------------------

    public CustomerState GetState() => currentState;
    public bool IsAtCounter() => isAtCounter;
    public bool CanOrder() => canOrder;
    public string GetCurrentOrder() => currentOrder;
    public float GetNormalizedPatience() => patienceTimer / maxPatienceTime;

    /// <summary>
    /// Sets the sorting order offset for this customer based on their position in queue.
    /// First customer gets +2, second gets +1, third gets +0.
    /// </summary>
    public void SetSortingOrderOffset(int offset)
    {
        currentQueueOffset = offset;
        hasQueueOffsetAssigned = true;
        ApplyQueueSortingOrderIfReady();
    }

    private void ApplyQueueSortingOrderIfReady()
    {
        if (!hasQueueOffsetAssigned || !hasEnteredThroughDoor)
            return;

        // Don't change sorting order if customer is leaving
        if (currentState == CustomerState.Leaving)
            return;

        if (bodySpriteRenderer != null)
        {
            bodySpriteRenderer.sortingOrder = queueBaseSortingOrder + currentQueueOffset;
        }
    }

    // Used by ServeHandoff.cs to know where the cup should slide to
    public Transform GetCupPickupPoint()
    {
        return cupPickupPoint != null ? cupPickupPoint : this.transform;
    }

    // Used by CustomerManager to track which counter this customer occupies
    public Transform GetTargetCounterPoint()
    {
        // Free up counter when leaving
        if (currentState == CustomerState.Leaving)
            return null;

        return targetCounterPoint;
    }

    // Allows CustomerManager to move a waiting customer up to a free counter
    public void ForceAdvanceTo(Transform newCounterPoint)
    {
        if (newCounterPoint == null) return;

        // Update target
        targetCounterPoint = newCounterPoint;
        canOrder = true;

        // Stop current behavior and walk to new counter
        StopAllCoroutines();
        StartCoroutine(ForceMoveToCounter());
    }

    private IEnumerator ForceMoveToCounter()
    {
        currentState = CustomerState.Arriving;
        isAtCounter = false;

        yield return StartCoroutine(MoveToCounter());

        isAtCounter = true;
        
        // Now that we're at the counter and can order, greet first then place order
        if (canOrder)
        {
            // Wait for dialogue turn to greet
            yield return StartCoroutine(WaitForDialogueTurn());

            currentState = CustomerState.Greeting;

            if (data.greetingDialogue != null)
            {
                string greeting = data.greetingDialogue.GetRandomLine();
                Speak(greeting);
            }

            // Wait for dialogue to finish (either typed or skipped)
            if (dialoguePopup != null)
            {
                // Wait until dialogue is no longer showing (fully typed or skipped)
                while (dialoguePopup.gameObject.activeSelf && dialoguePopup.canvasGroup != null && dialoguePopup.canvasGroup.alpha > 0f)
                {
                    yield return null;
                }
            }

            currentState = CustomerState.Ordering;

            if (currentOrderPreference != null && currentOrderPreference.orderDialogue != null)
            {
                string orderText = currentOrderPreference.orderDialogue.GetRandomLine();
                Speak(orderText);
            }

            OnOrderPlaced?.Invoke(currentOrder);

            // Wait for dialogue to finish (either typed or skipped), then wait for pause
            if (dialoguePopup != null)
            {
                // Wait until dialogue is no longer showing (fully typed or skipped)
                while (dialoguePopup.gameObject.activeSelf && dialoguePopup.canvasGroup != null && dialoguePopup.canvasGroup.alpha > 0f)
                {
                    yield return null;
                }
            }

            // Short pause after dialogue finishes
            yield return new WaitForSeconds(orderPauseDuration);

            ReleaseDialogueTurn();

            if (orderRoot != null)
            {
                orderRoot.gameObject.SetActive(true);
                ShowOrderTemperatureSprite();
            }

            currentState = CustomerState.Waiting;
        }
        else
        {
            currentState = CustomerState.Waiting;
        }
    }

    string ResolveOrderDisplayName(string orderId)
    {
        // try registry first
        if (recipeRegistry != null && recipeRegistry.TryGetRecipe(orderId, out var so) && so != null)
        {
            if (!string.IsNullOrEmpty(so.displayName))
                return so.displayName;

            // displayName not set; prettify asset name
            return PrettifyOrderId(orderId);
        }

        // no registry or not found; prettify asset name
        return PrettifyOrderId(orderId);
    }

    string PrettifyOrderId(string s)
    {
        // convert underscores/hyphens to spaces; collapse repeats
        // ex: "Hot_Lavender_Tea_Milk_Lemon" -> "Hot Lavender Tea Milk Lemon"
        s = s.Replace('_', ' ').Replace('-', ' ');
        // collapse multiple spaces
        int len;
        do { len = s.Length; s = s.Replace("  ", " "); } while (s.Length != len);
        return s.Trim();
    }

    // ---------------------------------
    // Click Detection & Squish Animation
    // ---------------------------------

    /// <summary>
    /// Handle click on customer to skip dialogue and play squish animation
    /// Note: Requires a PolygonCollider2D on the Body child object
    /// </summary>
    void OnMouseDown()
    {
        // Skip dialogue if it's playing
        if (dialoguePopup != null && dialoguePopup.enableTypewriter && !string.IsNullOrEmpty(dialoguePopup.fullText))
        {
            dialoguePopup.SkipTypewriter();
        }

        // Play squish animation
        if (!isSquishing)
        {
            StartCoroutine(SquishAnimation());
        }
    }

    IEnumerator SquishAnimation()
    {
        if (bodyTransform == null) yield break;

        isSquishing = true;

        // Store original scale
        Vector3 originalScale = bodyTransform.localScale;
        Vector3 squishScale = originalScale - Vector3.one * squishAmount;

        // Squish down
        float elapsed = 0f;
        while (elapsed < squishDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / squishDuration;
            bodyTransform.localScale = Vector3.Lerp(originalScale, squishScale, t);
            yield return null;
        }

        // Squish back up
        elapsed = 0f;
        while (elapsed < squishDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / squishDuration;
            bodyTransform.localScale = Vector3.Lerp(squishScale, originalScale, t);
            yield return null;
        }

        // Ensure we're back to original scale
        bodyTransform.localScale = originalScale;

        isSquishing = false;
    }
}

public enum CustomerState
{
    Arriving,
    Greeting,
    Ordering,
    Waiting,
    Receiving,
    Leaving
}

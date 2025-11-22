using UnityEngine;
using System.Collections;
using System.Linq;

/// <summary>
/// Tutorial dialogue array indices - maps to CustomerData.tutorialDialogues
/// </summary>
public enum TutorialDialogue
{
    SkipTutorialQuestion = 0,    // "Want to skip tutorial?"
    SkipTutorialGoodbye = 1,     // "Goodbye" when they skip
    CameraControlsQuestion = 2,  // "Want to learn hotkeys?"
    HotkeyTutorial = 3,          // "Press 1, 2, 3 to jump"
    MousePanTutorial = 4,        // "Move mouse to edge to pan"
    OrderExplanation1 = 5,       // "Here's your order" (chained)
    OrderExplanation2 = 6,       // "Make lavender tea" (chained)
    GoToKitchen = 7,             // "Head to the kitchen"
    PlaceMugOnDispenser = 8,     // "Place mug on dispenser"
    FillWithHotWater = 9,        // "Fill with hot water"
    WrongWaterTemp = 10,         // "Wrong temperature - dump it"
    GrabLavenderPacket = 11,     // "Grab lavender tea packet"
    SteepTeabag = 12,            // "Steep teabag 3 times"
    WrongTeaOrToppings = 13,     // "Wrong tea/toppings - dump it" (DEPRECATED - use WrongTea or WrongToppings)
    CompostTeabag = 14,          // "Compost the used teabag"
    ServeDrink = 15,             // "Serve the drink"
    FinalFarewell = 16,          // "Thanks for the tea!"
    WrongToppings = 17,          // "Wrong toppings - dump it and try again"
    EmptyCup = 18,               // "Empty cup served - customer leaves"
    JustWater = 19,              // "Just water served - customer leaves"
    ReminderAfter5Minutes = 20,  // "Reminder: Make lavender tea" (after 5 min)
    IntroToTutorial = 21         // "Intro dialogue" (shown after greeting, before skip question)
}

/// <summary>
/// Manages the tutorial flow after the intro cutscene completes.
/// Spawns tutorial customer and guides the player through making their first order.
/// </summary>
public class TutorialManager : MonoBehaviour
{
    // -------------------- SINGLETON --------------------
    public static TutorialManager Instance { get; private set; }


    // -------------------- INSPECTOR FIELDS --------------------
    [Header("Tutorial Customer")]
    [Tooltip("Prefab with TutorialCustomer component")]
    [SerializeField] private GameObject tutorialCustomerPrefab;

    [Tooltip("CustomerData asset for the tutorial customer")]
    [SerializeField] private CustomerData tutorialCustomerData;

    [Tooltip("Custom greeting dialogue for tutorial (optional)")]
    [SerializeField] private DialogueData tutorialGreetingDialogue;

    [Tooltip("Delay after cutscene ends before spawning tutorial customer")]
    [SerializeField] private float spawnDelay = 1f;

    [Header("References")]
    [SerializeField] private CustomerManager customerManager;
    [SerializeField] private RecipeRegistry recipeRegistry;
    [SerializeField] private Comfy.Camera.CameraStations cameraStations;

    [Header("Spawn Points")]
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private Transform counterPoint;
    [SerializeField] private Transform openDoorPoint;
    [SerializeField] private Transform doorPoint;

    [Header("Door Objects")]
    [SerializeField] private GameObject doorClosed;
    [SerializeField] private GameObject doorOpen;


    // -------------------- PRIVATE STATE --------------------
    private TutorialCustomer tutorialCustomer;
    private bool tutorialStarted = false;
    private bool tutorialCompleted = false;
    
    // Hotkey tutorial tracking
    private bool hasPressedAnyStationKey = false;
    private bool waitingForStationReturn = false;
    
    // Mouse panning tutorial tracking
    private bool hasPannedToAnyStation = false;
    private bool waitingForMousePanReturn = false;
    
    // Kitchen tutorial tracking
    private bool waitingForKitchenEntry = false;
    private bool waitingForMugPlacement = false;
    private bool waitingForTeaPacketPickup = false;
    private DispenserController trackedDispenser = null;
    private bool hasAdvancedPastWaterFill = false;

    // Wrong water/tea tracking
    private bool showingWrongWaterWarning = false;
    private bool showingWrongTeaWarning = false;
    private MugBeverageState trackedMugForReset = null;

    // Single tutorial mug (only one exists during tutorial)
    private MugBeverageState tutorialMug = null;

    private int previousStationIndex = -1;

    // 5-minute reminder timer
    private float orderPlacedTime = -1f;
    private bool hasShownReminderDialogue = false;

    // Cached WaitForSeconds to reduce GC allocations
    private readonly WaitForSeconds waitPointTwoSeconds = new WaitForSeconds(0.2f);
    private readonly WaitForSeconds waitPointThreeSeconds = new WaitForSeconds(0.3f);
    private readonly WaitForSeconds waitPointFiveSeconds = new WaitForSeconds(0.5f);
    private readonly WaitForSeconds waitThreeSeconds = new WaitForSeconds(3f);
    private readonly WaitForSeconds waitFourSeconds = new WaitForSeconds(4f);
    private readonly WaitForSeconds waitSixSeconds = new WaitForSeconds(6f);


    // -------------------- UNITY LIFECYCLE --------------------
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        // Find CustomerManager if not assigned
        if (customerManager == null)
            customerManager = Object.FindFirstObjectByType<CustomerManager>();

        // Find CameraStations if not assigned
        if (cameraStations == null)
            cameraStations = Object.FindFirstObjectByType<Comfy.Camera.CameraStations>();

        // Cache the single tutorial mug (only one exists during tutorial)
        tutorialMug = Object.FindFirstObjectByType<MugBeverageState>();
#if UNITY_EDITOR
        if (tutorialMug != null)
            Debug.Log($"[TutorialManager] Cached tutorial mug: {tutorialMug.name}");
        else
            Debug.LogWarning("[TutorialManager] No MugBeverageState found in scene!");
#endif

        // Subscribe to camera station changes (event-driven)
        Comfy.Camera.CameraStations.OnStationChanged += OnCameraStationChanged;

        // Listen for cutscene completion
        var cutsceneManager = Object.FindFirstObjectByType<CustomerManager>();
        if (cutsceneManager != null && GameManager.Instance != null)
            GameManager.Instance.OnShopOpened += OnShopOpened;
    }

    private void Update()
    {
        // Early exit: tutorial is complete, no need to update
        if (tutorialCompleted) return;

        // 5-minute reminder check
        if (!hasShownReminderDialogue && orderPlacedTime > 0f)
        {
            float elapsedTime = Time.time - orderPlacedTime;
            if (elapsedTime >= 300f) // 5 minutes = 300 seconds
            {
                hasShownReminderDialogue = true;
                ShowReminderDialogue();
            }
        }
    }

    // EVENT-DRIVEN: Camera station changed
    private void OnCameraStationChanged(int newStation)
    {
#if UNITY_EDITOR
        Debug.Log($"[TutorialManager] Station changed from {previousStationIndex} to {newStation}");
#endif

        // --- HOTKEY TUTORIAL TRACKING ---
        if (waitingForStationReturn)
        {
            // Any station change marks the requirement as complete
            if (!hasPressedAnyStationKey)
            {
                hasPressedAnyStationKey = true;
#if UNITY_EDITOR
                Debug.Log("[TutorialManager]   Player changed stations - hotkey requirement met!");
#endif
            }

            // Once they've changed stations, wait for them to return to station 0
            if (hasPressedAnyStationKey && newStation == 0 && previousStationIndex != 0)
            {
                waitingForStationReturn = false;
#if UNITY_EDITOR
                Debug.Log("[TutorialManager]     Player returned to counter - showing Array 4 (mouse panning) after delay");
#endif
                StartCoroutine(DelayedDialogue(() => OnHotkeyTutorialComplete(), 0.5f));
            }
        }

        // --- MOUSE PANNING TUTORIAL TRACKING ---
        if (waitingForMousePanReturn)
        {
            // Any station change marks the requirement as complete
            if (!hasPannedToAnyStation)
            {
                hasPannedToAnyStation = true;
#if UNITY_EDITOR
                Debug.Log("[TutorialManager]   Player changed stations - mouse pan requirement met!");
#endif
            }

            // Once they've changed stations, wait for them to return to station 0
            if (hasPannedToAnyStation && newStation == 0 && previousStationIndex != 0)
            {
                waitingForMousePanReturn = false;
#if UNITY_EDITOR
                Debug.Log("[TutorialManager]     Player returned to counter - showing Array 6 (order) after delay");
#endif
                StartCoroutine(DelayedDialogue(() => OnMousePanTutorialComplete(), 0.5f));
            }
        }

        // --- KITCHEN ENTRY TRACKING (ARRAY 8) ---
        if (waitingForKitchenEntry)
        {
            if (newStation == 1 && previousStationIndex != 1) // Station 1 is the kitchen
            {
                waitingForKitchenEntry = false;
#if UNITY_EDITOR
                Debug.Log("[TutorialManager]   Player entered kitchen (Station 1) via station change event");
#endif

                // Subscribe to global validation events (active for rest of tutorial)
                StartGlobalValidationMonitoring();

                ShowKitchenDialogue8();
            }
        }

        previousStationIndex = newStation;
    }

    private void OnDestroy()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnShopOpened -= OnShopOpened;

        // Unsubscribe from camera events
        Comfy.Camera.CameraStations.OnStationChanged -= OnCameraStationChanged;

        // Unsubscribe from global validation events
        StopGlobalValidationMonitoring();

        // Unsubscribe from mug brewing events
        MugBeverageState.OnSteepRegistered -= OnTeaSteepRegistered;
        MugBeverageState.OnBrewingComplete -= OnTeaBrewingComplete;
        MugBeverageState.OnTeaTypeSet -= OnTeaTypeSet;
    }

    // -------------------- EVENT HANDLERS --------------------
    private void OnShopOpened()
    {
        if (GameManager.Instance != null &&
            GameManager.Instance.HasPlayedIntroCutscene() &&
            !GameManager.Instance.HasCompletedTutorial() &&
            !tutorialStarted)
        {
            tutorialStarted = true;
            StartCoroutine(StartTutorialRoutine());
        }
    }

    private void OnTutorialCustomerArrivedAtCounter()
    {
#if UNITY_EDITOR
        Debug.Log("[TutorialManager] Tutorial customer arrived at counter");
#endif
        tutorialCustomer?.ShowTutorialGreeting();
    }

    private void OnTutorialGreetingComplete()
    {
#if UNITY_EDITOR
        Debug.Log("[TutorialManager] Tutorial greeting complete - showing Array 21 before skip tutorial dialogue");
#endif

        if (tutorialCustomer == null)
        {
#if UNITY_EDITOR
            Debug.LogError("[TutorialManager] Tutorial customer is null!");
#endif
            return;
        }

        if (tutorialCustomer.data != null && tutorialCustomer.data.HasTutorialDialogues())
        {
#if UNITY_EDITOR
            Debug.Log($"[TutorialManager] CustomerData has {tutorialCustomer.data.tutorialDialogues.Length} tutorial dialogues");
#endif
            // Show Array 21 first, then show skip tutorial question
            tutorialCustomer.ShowTutorialDialogue(21, () =>
            {
#if UNITY_EDITOR
                Debug.Log("[TutorialManager] Array 21 complete - now showing skip tutorial dialogue");
#endif
                tutorialCustomer.ShowTutorialDialogueWithChoices((int)TutorialDialogue.SkipTutorialQuestion, OnSkipTutorialResponse);
            });
        }
        else
        {
#if UNITY_EDITOR
            Debug.LogError("[TutorialManager] CustomerData has no tutorial dialogues set up!");
#endif
        }
    }

    private void OnSkipTutorialResponse(bool playerWantsToSkip)
    {
#if UNITY_EDITOR
        Debug.Log($"[TutorialManager] Skip tutorial response: {playerWantsToSkip}");
#endif

        if (playerWantsToSkip)
        {
#if UNITY_EDITOR
            Debug.Log("[TutorialManager] Player chose to skip tutorial");
#endif

            // Show disappointed sprite briefly
            tutorialCustomer?.SetBodyDisappointed();

            tutorialCustomer?.ShowTutorialDialogue((int)TutorialDialogue.SkipTutorialGoodbye, () =>
            {
#if UNITY_EDITOR
                Debug.Log("[TutorialManager] Goodbye dialogue complete - customer leaving");
#endif
                tutorialCustomer.TriggerLeave(wasCorrect: true, immediate: true);
                StartCoroutine(CompleteTutorialRoutine());
            });

            // Switch back to normal after 0.5 seconds
            StartCoroutine(ResetSpriteAfterDelay(1.5f));
        }
        else
        {
#if UNITY_EDITOR
            Debug.Log("[TutorialManager] Player chose to do the tutorial - showing camera controls question");
#endif
            tutorialCustomer?.ShowTutorialDialogueWithChoices((int)TutorialDialogue.CameraControlsQuestion, OnCameraControlsResponse);
        }
    }

    private void OnCameraControlsResponse(bool playerWantsHotkeys)
    {
#if UNITY_EDITOR
        Debug.Log($"[TutorialManager] Camera controls response: {(playerWantsHotkeys ? "YES (hotkeys)" : "NO (skipping hotkeys)")}");
#endif

        if (playerWantsHotkeys)
        {
#if UNITY_EDITOR
            Debug.Log("[TutorialManager] Player chose YES - showing hotkey tutorial");
#endif

            // Show hotkey tutorial (try pressing 1, 2, 3)
            // Set flags BEFORE showing dialogue to avoid race condition
            waitingForStationReturn = true;
            hasPressedAnyStationKey = false;
            previousStationIndex = 0; // Start at counter
#if UNITY_EDITOR
            Debug.Log("[TutorialManager] Hotkey tutorial flags set - waiting for player to press any station key and return to counter");
#endif
            tutorialCustomer?.ShowTutorialDialogue((int)TutorialDialogue.HotkeyTutorial, null);
        }
        else
        {
#if UNITY_EDITOR
            Debug.Log("[TutorialManager] Player chose NO - skipping to order dialogue");
#endif

            // Show order explanation (chained - won't disable popup)
            tutorialCustomer?.ShowTutorialDialogueChained((int)TutorialDialogue.OrderExplanation1, () =>
            {
                OnOrderSetupAndShowArray7();
            });
        }
    }

    private void OnHotkeyTutorialComplete()
    {
#if UNITY_EDITOR
        Debug.Log("[TutorialManager] Hotkey tutorial complete - showing mouse panning tutorial");
#endif

        // Show happy sprite briefly
        tutorialCustomer?.SetBodyHappy();

        // Show mouse edge panning tutorial
        // Set flags BEFORE showing dialogue to avoid race condition
        waitingForMousePanReturn = true;
        hasPannedToAnyStation = false;
        previousStationIndex = 0; // At counter
#if UNITY_EDITOR
        Debug.Log("[TutorialManager] Mouse pan tutorial flags set - waiting for player to pan using mouse/arrows and return to counter");
#endif
        tutorialCustomer?.ShowTutorialDialogue((int)TutorialDialogue.MousePanTutorial, null);

        // Switch back to normal after 0.5 seconds
        StartCoroutine(ResetSpriteAfterDelay(1.5f));
    }

    private void OnMousePanTutorialComplete()
    {
#if UNITY_EDITOR
        Debug.Log("[TutorialManager] Mouse panning tutorial complete - showing order");
#endif

        // Show happy sprite briefly
        tutorialCustomer?.SetBodyHappy();

        // Show order explanation (chained - won't disable popup)
        tutorialCustomer?.ShowTutorialDialogueChained((int)TutorialDialogue.OrderExplanation2, () =>
        {
            OnOrderSetupAndShowArray7();
        });

        // Switch back to normal after 1.5 seconds
        StartCoroutine(ResetSpriteAfterDelay(1.5f));
    }

    private void OnOrderSetupAndShowArray7()
    {
#if UNITY_EDITOR
        Debug.Log("[TutorialManager] Order dialogue complete - setting up order, showing icon, and go to kitchen prompt");
#endif

        // Set up the order
        var order = tutorialCustomer.GetComponent<CustomerOrder>();
        if (order != null)
        {
            order.SetRegistry(recipeRegistry);
            order.ManuallyPlaceOrder("Hot_Lavender_Tea");
#if UNITY_EDITOR
            Debug.Log("[TutorialManager] Order placed: Hot_Lavender_Tea (icon shown automatically)");
#endif
        }
        else
        {
#if UNITY_EDITOR
            Debug.LogWarning("[TutorialManager] No CustomerOrder found on TutorialCustomer!");
#endif
        }

        // Show the order icon
        Transform orderRoot = tutorialCustomer.transform.Find("BearCanvas/Order");
        if (orderRoot != null)
        {
            orderRoot.gameObject.SetActive(true);
#if UNITY_EDITOR
            Debug.Log("[TutorialManager] Order icon activated - now showing go to kitchen prompt");
#endif
        }
        else
        {
#if UNITY_EDITOR
            Debug.LogWarning("[TutorialManager] Missing order icon transform on TutorialCustomer!");
#endif
        }

        // Start 5-minute reminder timer
        orderPlacedTime = Time.time;
#if UNITY_EDITOR
        Debug.Log("[TutorialManager] Started 5-minute reminder timer");
#endif

        // Show go to kitchen prompt
        tutorialCustomer.ShowTutorialDialogue((int)TutorialDialogue.GoToKitchen, OnPostOrderDialogueComplete);
    }

    private void OnPostOrderDialogueComplete()
    {
#if UNITY_EDITOR
        Debug.Log("[TutorialManager] Go to kitchen dialogue complete - waiting for player to enter kitchen (Station 1)");
#endif

        waitingForKitchenEntry = true;

        int currentStation = GetCurrentCameraStation();
        if (currentStation == 1)
        {
            // Player already in kitchen, trigger immediately
            waitingForKitchenEntry = false;
#if UNITY_EDITOR
            Debug.Log("[TutorialManager] Player already in kitchen - starting global monitoring and showing place mug prompt");
#endif

            // Start global validation monitoring (same as when entering via station change)
            StartGlobalValidationMonitoring();

            ShowKitchenDialogue8();
        }
    }

    /// <summary>
    /// Ensures the DispenserController reference is cached. Call this before using trackedDispenser.
    /// </summary>
    private void EnsureDispenserReference()
    {
        if (trackedDispenser == null)
            trackedDispenser = FindFirstObjectByType<DispenserController>();
    }

    private void ShowKitchenDialogue8()
    {
#if UNITY_EDITOR
        Debug.Log("[TutorialManager] [PlaceMugOnDispenser] Validating: Should we ask player to place mug on dispenser?");
#endif

        // GOAL: Get player to place mug on dispenser
        // Only validate if they've already done THIS step or steps BEYOND it

        // Check if they've already gone way past this step (drink ready)
        if (IsCorrectTeaInMug(out var teaMug, out bool isBrewed) && isBrewed &&
            teaMug.WaterTemperature == WaterTemp.Hot && !HasWrongToppings(teaMug))
        {
            if (UsedLavenderTeabagExists())
            {
#if UNITY_EDITOR
                Debug.Log("[PlaceMugOnDispenser] ✓ Perfect drink ready → Skip to compost prompt");
#endif
                ShowArray14AndWaitForCompost();
            }
            else
            {
#if UNITY_EDITOR
                Debug.Log("[PlaceMugOnDispenser] ✓ Perfect drink ready → Skip to serve prompt");
#endif
                tutorialCustomer?.ShowTutorialDialogueKitchen((int)TutorialDialogue.ServeDrink, null);
            }
            return;
        }

        // Check if they're brewing (past this step)
        if (IsCorrectTeaInMug(out var teaMugPartial, out bool _))
        {
#if UNITY_EDITOR
            Debug.Log($"[PlaceMugOnDispenser] ✓ Brewing in progress → Skip to steep prompt");
#endif
            StartCoroutine(ShowArray12AndWaitForBrewRoutine());
            return;
        }

        // Check if mug already on dispenser (they've done THIS step)
        if (IsMugOnDispenser(out var mugOnDispenser))
        {
#if UNITY_EDITOR
            Debug.Log("[PlaceMugOnDispenser] ✓ Mug already on dispenser → Skip to fill water prompt");
#endif
            ShowKitchenDialogue9();
            return;
        }

        // Otherwise, show place mug prompt normally
#if UNITY_EDITOR
        Debug.Log("[PlaceMugOnDispenser] → Showing place mug on dispenser prompt");
#endif
        tutorialCustomer?.ShowTutorialDialogueKitchen((int)TutorialDialogue.PlaceMugOnDispenser, null);
        waitingForMugPlacement = true;
        FindAndSubscribeToDispenser();
    }

    private IEnumerator WaitForSnapEvent(SnapSocket socket)
    {
        // Show place mug prompt right away
        tutorialCustomer.ShowTutorialDialogueKitchen((int)TutorialDialogue.PlaceMugOnDispenser, null);
#if UNITY_EDITOR
        Debug.Log("[TutorialManager] Showing place mug prompt — waiting for cup...");
#endif

        while (socket != null && !socket.Occupied) yield return null;

        if (socket != null && socket.Occupied)
        {
            tutorialCustomer?.HideTutorialDialogue(); // hide place mug prompt
            ShowKitchenDialogue9();                   // advance to fill water prompt
            yield break;
        }
    }


    private void FindAndSubscribeToDispenser()
    {
        // Find the DispenserController in the scene if we don't have it yet
        EnsureDispenserReference();

        if (trackedDispenser != null)
        {
#if UNITY_EDITOR
            Debug.Log("[TutorialManager] Found DispenserController - subscribing to events");
#endif

            // Subscribe to fill started event to detect when filling begins
            trackedDispenser.OnFillStarted.AddListener(OnWaterFillStarted);
        }
        else
        {
#if UNITY_EDITOR
            Debug.LogWarning("[TutorialManager] Could not find DispenserController in scene!");
#endif
            return;
        }

        // Always start checking for mug placement (even if we already had trackedDispenser)
#if UNITY_EDITOR
        Debug.Log("[FindAndSubscribeToDispenser] Starting CheckForMugPlacementRoutine");
#endif
        StartCoroutine(CheckForMugPlacementRoutine());
    }

    private IEnumerator CheckForMugPlacementRoutine()
    {
#if UNITY_EDITOR
        Debug.Log("[CheckForMugPlacementRoutine] Starting to check for mug placement on dispenser...");
        Debug.Log($"[CheckForMugPlacementRoutine] waitingForMugPlacement = {waitingForMugPlacement}");
#endif

        while (waitingForMugPlacement)
        {
            yield return new WaitForSeconds(0.2f); // Check every 0.2 seconds
#if UNITY_EDITOR
            Debug.Log($"[CheckForMugPlacementRoutine] Polling... waitingForMugPlacement={waitingForMugPlacement}");
#endif

            if (trackedDispenser != null)
            {
                // Try using IsMugOnDispenser first
                if (IsMugOnDispenser(out var mugFound))
                {
                    waitingForMugPlacement = false;
#if UNITY_EDITOR
                    Debug.Log($"[CheckForMugPlacementRoutine] ✓ Mug detected via IsMugOnDispenser: {mugFound.name}");
#endif
                    ShowKitchenDialogue9();
                    yield break;
                }
            }
            else
            {
#if UNITY_EDITOR
                Debug.LogWarning("[CheckForMugPlacementRoutine] trackedDispenser is null!");
#endif
            }
        }

#if UNITY_EDITOR
        Debug.Log("[CheckForMugPlacementRoutine] Exited - waitingForMugPlacement became false");
#endif
    }

    private void ShowKitchenDialogue9()
    {
#if UNITY_EDITOR
        Debug.Log("[TutorialManager] [FillWithHotWater] Validating: Should we ask player to fill mug with hot water?");
#endif

        EnsureDispenserReference();

        // GOAL: Get player to fill mug with HOT water
        // Validate: Has mug been filled? If yes, is it correct or wrong?

        // Check if they've already gone way past this step (drink ready)
        if (IsCorrectTeaInMug(out var teaMug, out bool isBrewed) && isBrewed &&
            teaMug.WaterTemperature == WaterTemp.Hot && !HasWrongToppings(teaMug))
        {
            if (UsedLavenderTeabagExists())
            {
#if UNITY_EDITOR
                Debug.Log("[FillWithHotWater] ✓ Perfect drink ready → Skip to compost prompt");
#endif
                ShowArray14AndWaitForCompost();
            }
            else
            {
#if UNITY_EDITOR
                Debug.Log("[FillWithHotWater] ✓ Perfect drink ready → Skip to serve prompt");
#endif
                tutorialCustomer?.ShowTutorialDialogueKitchen((int)TutorialDialogue.ServeDrink, null);
            }
            return;
        }

        // Check if they're brewing (past this step)
        if (IsCorrectTeaInMug(out var teaMugPartial, out bool _))
        {
#if UNITY_EDITOR
            Debug.Log($"[FillWithHotWater] ✓ Brewing in progress → Skip to steep prompt");
#endif
            StartCoroutine(ShowArray12AndWaitForBrewRoutine());
            return;
        }

        // Check if mug is filled (they've done THIS step or messed up)
        if (IsMugFilled(out var filledMug, out WaterTemp waterTemp))
        {
            // Check for wrong conditions first
            if (filledMug.HasTea && filledMug.TeaType != TeaType.Lavender)
            {
#if UNITY_EDITOR
                Debug.Log($"[FillWithHotWater] ✗ WRONG TEA ({filledMug.TeaType}) → Wrong tea warning, reset to place mug");
#endif
                showingWrongTeaWarning = true;
                trackedMugForReset = filledMug;
                tutorialCustomer?.ShowTutorialDialogueKitchen((int)TutorialDialogue.WrongTeaOrToppings, null);
                StartCoroutine(WaitForMugEmptyThenResetToArray8());
                return;
            }

            if (HasWrongToppings(filledMug))
            {
#if UNITY_EDITOR
                Debug.Log("[FillWithHotWater] ✗ WRONG TOPPINGS → Wrong toppings warning, reset to place mug");
#endif
                showingWrongTeaWarning = true;
                trackedMugForReset = filledMug;
                tutorialCustomer?.ShowTutorialDialogueKitchen((int)TutorialDialogue.WrongTeaOrToppings, null);
                StartCoroutine(WaitForMugEmptyThenResetToArray8());
                return;
            }

            if (waterTemp == WaterTemp.Cold)
            {
#if UNITY_EDITOR
                Debug.Log("[FillWithHotWater] ✗ WRONG TEMPERATURE (cold) → Wrong temp warning, reset to place mug");
#endif
                showingWrongWaterWarning = true;
                trackedMugForReset = filledMug;
                tutorialCustomer?.ShowTutorialDialogueKitchen((int)TutorialDialogue.WrongWaterTemp, null);
                StartCoroutine(WaitForMugEmptyThenResetToArray8());
                return;
            }

            // Mug filled correctly with hot water!
#if UNITY_EDITOR
            Debug.Log("[FillWithHotWater] ✓ Hot water filled → Skip to grab tea packet prompt");
#endif
            ShowKitchenDialogue11WaitForTeaPacket();
            return;
        }

        // Mug not filled yet - show fill hot water prompt normally
#if UNITY_EDITOR
        Debug.Log("[FillWithHotWater] → Showing fill with hot water prompt");
#endif
        tutorialCustomer?.ShowTutorialDialogueKitchen((int)TutorialDialogue.FillWithHotWater, null);

        if (trackedDispenser != null)
        {
            trackedDispenser.OnFillStarted.RemoveListener(OnWaterFillStarted);
            trackedDispenser.OnFillCompleted.RemoveListener(OnWaterFillCompleted);
            trackedDispenser.OnFillStarted.AddListener(OnWaterFillStarted);
            trackedDispenser.OnFillCompleted.AddListener(OnWaterFillCompleted);
        }
    }

    private MugBeverageState FindMugNearDispenser(DispenserController dispenser)
    {
        if (dispenser == null) return null;
        var socket = dispenser.transform.Find("SocketPoint");
        if (socket == null) return null;

        foreach (var hit in Physics2D.OverlapCircleAll(socket.position, 0.25f))
        {
            var mug = hit.GetComponentInParent<MugBeverageState>();
            if (mug != null) return mug;
        }
        return null;
    }

    private void OnWaterFillStarted()
    {
        if (hasAdvancedPastWaterFill) return;
        hasAdvancedPastWaterFill = true;

#if UNITY_EDITOR
        Debug.Log("[OnWaterFillStarted] Water fill started — advancing once");
#endif

        trackedDispenser.OnFillStarted.RemoveListener(OnWaterFillStarted);
        trackedDispenser.OnFillCompleted.RemoveListener(OnWaterFillCompleted);

        var mug = FindMugNearDispenser(trackedDispenser);
#if UNITY_EDITOR
        Debug.Log($"[OnWaterFillStarted] FindMugNearDispenser returned: {(mug != null ? mug.name : "NULL")}");
#endif

        WaterTemp temp = trackedDispenser.LastFillTemp;
#if UNITY_EDITOR
        Debug.Log($"[OnWaterFillStarted] Dispenser LastFillTemp: {temp}");
#endif

        if (mug != null && mug.WaterTemperature != null)
        {
            temp = mug.WaterTemperature.Value;
#if UNITY_EDITOR
            Debug.Log($"[OnWaterFillStarted] Using mug's WaterTemperature: {temp}");
#endif
        }

        tutorialCustomer?.HideTutorialDialogue();

        if (temp == WaterTemp.Cold)
        {
#if UNITY_EDITOR
            Debug.Log("[OnWaterFillStarted] Cold water detected - showing wrong temp warning");
#endif
            ShowKitchenDialogue10ThenContinue();
        }
        else
        {
#if UNITY_EDITOR
            Debug.Log("[OnWaterFillStarted] Hot water - showing grab tea packet prompt");
#endif
            ShowKitchenDialogue11WaitForTeaPacket();
        }
    }

    private void OnWaterFillCompleted()
    {
        if (hasAdvancedPastWaterFill) return;
        hasAdvancedPastWaterFill = true;

#if UNITY_EDITOR
        Debug.Log("[TutorialManager]   Water fill completed — advancing once");
#endif

        trackedDispenser.OnFillStarted.RemoveListener(OnWaterFillStarted);
        trackedDispenser.OnFillCompleted.RemoveListener(OnWaterFillCompleted);

        MugBeverageState mug = FindMugNearDispenser(trackedDispenser)
                            ?? FindObjectsByType<MugBeverageState>(FindObjectsSortMode.None)
                                .FirstOrDefault(m => m != null && m.HasWater);

        if (mug == null)
        {
#if UNITY_EDITOR
            Debug.LogWarning("[TutorialManager] Fill completed but no mug found.");
#endif
            return;
        }

        WaterTemp temp = mug.WaterTemperature ?? trackedDispenser.LastFillTemp;

        tutorialCustomer?.HideTutorialDialogue();

        if (temp == WaterTemp.Cold)
            ShowKitchenDialogue10ThenContinue();
        else
            ShowKitchenDialogue11WaitForTeaPacket();
    }


    private void ShowKitchenDialogue10ThenContinue()
    {
        if (tutorialCustomer != null)
        {
#if UNITY_EDITOR
            Debug.Log("[ShowKitchenDialogue10ThenContinue] Showing wrong water temperature warning");
#endif
            showingWrongWaterWarning = true;

            // Get the tutorial mug (with fallback if reference was lost)
            MugBeverageState mug = GetTutorialMug();
            if (mug != null)
            {
#if UNITY_EDITOR
                Debug.Log($"[ShowKitchenDialogue10ThenContinue] Using tutorial mug: {mug.name}");
#endif
                trackedMugForReset = mug;
                // Start monitoring for mug being emptied - reset to place mug prompt
#if UNITY_EDITOR
                Debug.Log("[ShowKitchenDialogue10ThenContinue] Starting WaitForMugEmptyThenResetToArray8 coroutine");
#endif
                StartCoroutine(WaitForMugEmptyThenResetToArray8());
            }
            else
            {
#if UNITY_EDITOR
                Debug.LogError("[ShowKitchenDialogue10ThenContinue] No mug found in scene - cannot start monitoring coroutine!");
#endif
            }

            // Show wrong temp warning - player should dump the mug
            tutorialCustomer.ShowTutorialDialogueKitchen((int)TutorialDialogue.WrongWaterTemp, null);

            // Note: After dumping, WaitForMugEmptyThenResetToArray8() will return to place mug prompt
        }
    }

    /// <summary>
    /// Gets the tutorial mug. Uses cached reference if available, otherwise finds it in scene.
    /// There's always exactly one mug during the tutorial.
    /// </summary>
    private MugBeverageState GetTutorialMug()
    {
        // Use cached reference if still valid
        if (tutorialMug != null)
            return tutorialMug;

        // Fallback: find the mug in scene (there's always one during tutorial)
        tutorialMug = FindFirstObjectByType<MugBeverageState>();
#if UNITY_EDITOR
        if (tutorialMug != null)
            Debug.Log($"[GetTutorialMug] Re-cached tutorial mug: {tutorialMug.name}");
        else
            Debug.LogWarning("[GetTutorialMug] No MugBeverageState found in scene!");
#endif

        return tutorialMug;
    }

    private IEnumerator WaitForMugEmptyThenResetToArray9()
    {
#if UNITY_EDITOR
        Debug.Log("[TutorialManager] Monitoring mug for emptying (reset to fill water prompt)...");
#endif

        // Wait a frame to ensure mug reference is valid
        yield return null;

        while (trackedMugForReset != null)
        {
            // Check if mug is now empty
            if (trackedMugForReset.IsEmpty())
            {
#if UNITY_EDITOR
                Debug.Log("[TutorialManager] Mug emptied - resetting to fill water prompt");
#endif

                // Clear flags
                showingWrongWaterWarning = false;
                showingWrongTeaWarning = false;
                hasAdvancedPastWaterFill = false;
                trackedMugForReset = null;

                // Hide current dialogue
                tutorialCustomer?.HideTutorialDialogue();

                // Return to fill water prompt - ask them to fill with correct water
                yield return waitPointFiveSeconds;
                ShowKitchenDialogue9();
                yield break;
            }

            yield return waitPointThreeSeconds;
        }
    }

    private IEnumerator WaitForMugEmptyThenResetToArray8()
    {
#if UNITY_EDITOR
        Debug.Log($"[WaitForMugEmptyThenResetToArray8] START - trackedMugForReset={(trackedMugForReset != null ? trackedMugForReset.name : "null")}");
#endif

        // Wait a frame to ensure mug reference is valid
        yield return null;

        if (trackedMugForReset == null)
        {
#if UNITY_EDITOR
            Debug.LogWarning("[WaitForMugEmptyThenResetToArray8] trackedMugForReset is null after waiting 1 frame - exiting early!");
#endif
            yield break;
        }

#if UNITY_EDITOR
        Debug.Log($"[WaitForMugEmptyThenResetToArray8] After 1 frame wait - trackedMugForReset={trackedMugForReset.name}");
#endif

        while (trackedMugForReset != null)
        {
#if UNITY_EDITOR
            Debug.Log($"[WaitForMugEmptyThenResetToArray8] Polling... mug={trackedMugForReset.name}, isEmpty={trackedMugForReset.IsEmpty()}");
#endif

            // Check if mug is now empty
            if (trackedMugForReset.IsEmpty())
            {
#if UNITY_EDITOR
                Debug.Log("[WaitForMugEmptyThenResetToArray8] ✓ Mug emptied - resetting to place mug prompt");
#endif

                // Clear flags
                showingWrongWaterWarning = false;
                showingWrongTeaWarning = false;
                hasAdvancedPastWaterFill = false;
                waitingForMugPlacement = false;
                trackedMugForReset = null;

                // Hide current dialogue
                tutorialCustomer?.HideTutorialDialogue();

                // Return to place mug prompt - ask them to place mug on dispenser again
                yield return waitPointFiveSeconds;
                ShowKitchenDialogue8();
                yield break;
            }

            yield return waitPointThreeSeconds;
        }

#if UNITY_EDITOR
        Debug.Log("[WaitForMugEmptyThenResetToArray8] Exited loop - trackedMugForReset became null externally");
#endif
    }

    private void ShowKitchenDialogue11WaitForTeaPacket()
    {
#if UNITY_EDITOR
        Debug.Log("[TutorialManager] [GrabLavenderPacket] Validating: Should we ask player to grab lavender packet?");
#endif

        // Get the tutorial mug
        MugBeverageState mug = GetTutorialMug();
        if (mug == null)
        {
#if UNITY_EDITOR
            Debug.LogError("[GrabLavenderPacket] No tutorial mug found!");
#endif
            return;
        }

        // VALIDATION: Check for WRONG tea first
        if (mug.HasTea && mug.TeaType != TeaType.Lavender)
        {
#if UNITY_EDITOR
            Debug.Log($"[GrabLavenderPacket] ✗ WRONG TEA ({mug.TeaType}) → Wrong tea warning, reset to place mug");
#endif
            showingWrongTeaWarning = true;
            trackedMugForReset = mug;
            tutorialCustomer?.ShowTutorialDialogueKitchen((int)TutorialDialogue.WrongTeaOrToppings, null);
            StartCoroutine(WaitForMugEmptyThenResetToArray8());
            return;
        }

        // VALIDATION: Check if lavender tea already brewed
        if (IsCorrectTeaInMug(out var teaMug, out bool isBrewed))
        {
            if (isBrewed)
            {
#if UNITY_EDITOR
                Debug.Log("[GrabLavenderPacket] ✓ Lavender tea already brewed → Skip to compost prompt");
#endif
                ShowArray14AndWaitForCompost();
                return;
            }
            else
            {
#if UNITY_EDITOR
                Debug.Log($"[GrabLavenderPacket] ✓ Lavender tea in mug ({teaMug._steepCount}/3 steeps) → Skip to steep prompt");
#endif
                StartCoroutine(ShowArray12AndWaitForBrewRoutine());
                return;
            }
        }

        // VALIDATION: Check if lavender packet already torn/teabag present
        if (IsLavenderPacketInScene(out TeaPacket packet))
        {
            var teabagObj = GameObject.Find("TeabagParent");
            if (teabagObj != null && teabagObj.activeInHierarchy)
            {
                var teabag = teabagObj.GetComponent<Teabag>();
                if (teabag != null && teabag.teaDefinition != null && teabag.teaDefinition.teaType == TeaType.Lavender)
                {
#if UNITY_EDITOR
                    Debug.Log("[GrabLavenderPacket] ✓ Active lavender teabag found → Skip to steep prompt");
#endif
                    StartCoroutine(ShowArray12AndWaitForBrewRoutine());
                    return;
                }
            }
        }

        // DEFAULT: Normal behavior - ask player to grab lavender packet
#if UNITY_EDITOR
        Debug.Log("[GrabLavenderPacket] → Showing grab lavender packet prompt");
#endif
        tutorialCustomer?.ShowTutorialDialogueKitchen((int)TutorialDialogue.GrabLavenderPacket, null);

        // Note: Wrong tea detection handled by global validation monitoring
        StartCoroutine(SubscribeWhenLavenderPacketGrabbed());
    }

    private IEnumerator SubscribeWhenLavenderPacketGrabbed()
    {
        TeaPacket target = null;
        while (target == null)
        {
            if (DragItem2D.Current != null)
            {
                var tr = DragItem2D.Current.transform;
                var parent = tr ? tr.parent : null;
                if (parent != null && parent.name == "lavender_packet")
                {
                    target = parent.GetComponent<TeaPacket>();
                    if (target != null)
                    {
                        target.OnTearOpen += OnLavenderPacketTorn;
#if UNITY_EDITOR
                        Debug.Log("[TutorialManager] Subscribed to lavender_packet.OnTearOpen");
#endif
                        yield break;
                    }
                }
            }
            yield return null;
        }
    }

    private IEnumerator WaitForLavenderPacketTornRoutine()
    {
#if UNITY_EDITOR
        Debug.Log("[TutorialManager] Tracking lavender packet tear event...");
#endif

        TeaPacket targetPacket = null;
        bool eventHooked = false;

        while (true)
        {
            yield return new WaitForSeconds(0.1f);

            // Check what player is dragging
            if (DragItem2D.Current != null)
            {
                Transform held = DragItem2D.Current.transform;
                if (held != null && held.parent != null && held.parent.name == "lavender_packet")
                {
                    // Find TeaPacket on the parent
                    targetPacket = held.parent.GetComponent<TeaPacket>();
                    if (targetPacket != null && !eventHooked)
                    {
                        targetPacket.OnTearOpen += OnLavenderPacketTorn;
                        eventHooked = true;
#if UNITY_EDITOR
                        Debug.Log("[TutorialManager] Subscribed to lavender_packet.OnTearOpen");
#endif
                    }
                }
            }

            // Exit if we already subscribed and event fired
            if (targetPacket == null && eventHooked)
                break;
        }
    }

    public void OnTutorialDrinkServed()
    {
        if (GameManager.Instance.hasCompletedTutorial) return;

#if UNITY_EDITOR
        Debug.Log("[TutorialManager] Player served tutorial drink — starting completion sequence");
#endif

        var popup = tutorialCustomer?.tutorialPopupCounter;
        if (popup == null) return;

        // Disable the "Order" UI instantly
#if UNITY_EDITOR
        Debug.Log("[TutorialManager] Disabled Order Icon");
#endif
        var orderObj = tutorialCustomer.transform.Find("BearCanvas/Order");
        if (orderObj != null)
            orderObj.gameObject.SetActive(false);

        if (!popup.gameObject.activeSelf)
            popup.gameObject.SetActive(true);

#if UNITY_EDITOR
        Debug.Log("[TutorialManager] Shown Final Dialogue");
#endif
        GameObject obj = GameObject.Find("CustomerCanvas/TutorialPopupKitchen");
        if (obj != null)
            obj.SetActive(false);
        popup.ApplyDialogSpeechSettings(tutorialCustomer?.data?.dialogSpeechSettings);

        popup.ShowDialogue(
            tutorialCustomer.data.GetTutorialDialogue((int)TutorialDialogue.FinalFarewell),
            () =>
            {
#if UNITY_EDITOR
                Debug.Log("[TutorialManager] Started Final Dialogue Fade");
#endif
                popup.StartCoroutine(FadeAndExitRoutine(popup));
                OnTutorialOrderComplete(true);
#if UNITY_EDITOR
            Debug.Log("[TutorialManager] Tutorial Order Complete");
#endif
            }
        );
    }

    private IEnumerator FadeAndExitRoutine(DialoguePopup popup)
    {
        yield return new WaitForSeconds(3f);
        popup.HideDialogue();
        yield return new WaitForSeconds(1f);

        popup.gameObject.SetActive(false);
    }


    private IEnumerator FadeCanvasGroup(CanvasGroup group, float from, float to, float duration)
    {
        float t = 0f;
        group.alpha = from;
        while (t < duration)
        {
            t += Time.deltaTime;
            group.alpha = Mathf.Lerp(from, to, t / duration);
            yield return null;
        }
        group.alpha = to;
        group.interactable = false;
        group.blocksRaycasts = false;
    }

    private void OnLavenderPacketTorn()
    {
#if UNITY_EDITOR
        Debug.Log("[TutorialManager] Lavender packet torn — advancing to steep prompt");
#endif
        tutorialCustomer?.HideTutorialDialogue();
        tutorialCustomer?.ShowTutorialDialogueKitchen((int)TutorialDialogue.SteepTeabag, null);

        // Continue normal steep monitoring
        StartCoroutine(ShowArray12AndWaitForBrewRoutine());
    }


    private bool _wrongTeaShown = false;

    private IEnumerator ShowArray12AndWaitForBrewRoutine()
    {
        yield return waitPointTwoSeconds;
#if UNITY_EDITOR
        Debug.Log("[TutorialManager] Showing steep teabag prompt - waiting for lavender tea brewing...");
#endif
        tutorialCustomer?.ShowTutorialDialogueKitchen((int)TutorialDialogue.SteepTeabag, null);

        // Subscribe to brewing events (event-driven instead of polling)
        MugBeverageState.OnSteepRegistered += OnTeaSteepRegistered;
        MugBeverageState.OnBrewingComplete += OnTeaBrewingComplete;
        MugBeverageState.OnTeaTypeSet += OnTeaTypeSet;

        _wrongTeaShown = false;
    }

    // EVENT-DRIVEN: Called when tea type is set on a mug
    private void OnTeaTypeSet(TeaType teaType)
    {
        if (!waitingForTeaPacketPickup) return; // Only care during brewing tutorial

        // Check for wrong tea
        if (teaType != TeaType.None && teaType != TeaType.Lavender && !_wrongTeaShown)
        {
            _wrongTeaShown = true;
            showingWrongTeaWarning = true;
#if UNITY_EDITOR
            Debug.Log("[TutorialManager] Wrong teabag detected — showing Array 13 warning");
#endif

            // Find the mug with wrong tea
            foreach (var mug in FindObjectsByType<MugBeverageState>(FindObjectsSortMode.None))
            {
                if (mug != null && mug.TeaType == teaType)
                {
                    trackedMugForReset = mug;
                    // Start monitoring for mug being emptied (reuses same coroutine)
                    StartCoroutine(WaitForMugEmptyThenResetToArray9());
                    break;
                }
            }

            // Show wrong tea/toppings warning - player should dump the mug
            tutorialCustomer?.ShowTutorialDialogueKitchen((int)TutorialDialogue.WrongTeaOrToppings, null);

            // Note: We don't return to grab lavender packet anymore
            // Instead, WaitForMugEmptyThenResetToArray9() will return to fill water prompt when mug is dumped
        }
        else if (teaType == TeaType.Lavender)
        {
            // Correct tea - reset wrong flag
            _wrongTeaShown = false;
        }
    }

    private IEnumerator ReturnToLavenderPacketPrompt()
    {
        yield return waitSixSeconds; // Wait for dialogue to show

#if UNITY_EDITOR
        Debug.Log("[TutorialManager] Returning to grab lavender packet prompt");
#endif
        tutorialCustomer?.ShowTutorialDialogueKitchen((int)TutorialDialogue.GrabLavenderPacket, null);

        // Re-hook the tear-open event for lavender packet
        TeaPacket packet = FindFirstObjectByType<TeaPacket>();
        if (packet != null && packet.teaDefinition != null &&
            packet.teaDefinition.name.ToLower().Contains("lavender"))
        {
            // Unsubscribe first to avoid duplicates
            packet.OnTearOpen -= OnLavenderPacketTorn;
            packet.OnTearOpen += OnLavenderPacketTorn;
        }
    }

    // EVENT-DRIVEN: Called when a steep is registered
    private void OnTeaSteepRegistered(int steepCount)
    {
        // Can add feedback here if needed
#if UNITY_EDITOR
        Debug.Log($"[TutorialManager] Tea steep registered: {steepCount}/3");
#endif
    }

    // EVENT-DRIVEN: Called when brewing is complete (3 steeps)
    private void OnTeaBrewingComplete(TeaType teaType)
    {
        if (teaType == TeaType.Lavender)
        {
#if UNITY_EDITOR
            Debug.Log("[TutorialManager] ✓ Lavender tea brewed (3 dips) — showing compost prompt");
#endif
            tutorialCustomer?.HideTutorialDialogue();

            // Unsubscribe from brewing events
            MugBeverageState.OnSteepRegistered -= OnTeaSteepRegistered;
            MugBeverageState.OnBrewingComplete -= OnTeaBrewingComplete;
            MugBeverageState.OnTeaTypeSet -= OnTeaTypeSet;

            ShowArray14AndWaitForCompost();
        }
    }

    private void ShowArray14AndWaitForCompost()
    {
#if UNITY_EDITOR
        Debug.Log("[TutorialManager] Showing compost prompt, then auto-continue");
#endif
        tutorialCustomer?.ShowTutorialDialogueKitchen((int)TutorialDialogue.CompostTeabag, () =>
        {
            // After compost prompt finishes, show serve drink prompt automatically
#if UNITY_EDITOR
            Debug.Log("[TutorialManager] Compost prompt complete - showing serve prompt");
#endif
            tutorialCustomer?.ShowTutorialDialogueKitchen((int)TutorialDialogue.ServeDrink, null);
        });
    }


    private MugBeverageState _cachedLavenderMug;

    private IEnumerator WaitForLavenderMugRoutine()
    {
        float timeout = 8f;
        float elapsed = 0f;
        while (elapsed < timeout)
        {
            foreach (var m in FindObjectsByType<MugBeverageState>(FindObjectsSortMode.None))
            {
                if (m != null && m.HasWater && m.TeaType == TeaType.Lavender)
                {
                    _cachedLavenderMug = m;
#if UNITY_EDITOR
                    Debug.Log("[TutorialManager] Found lavender mug after delay.");
#endif
                    yield break;
                }
            }
            elapsed += 0.25f;
            yield return new WaitForSeconds(0.25f);
        }
        _cachedLavenderMug = null;
    }

    // -------------------- GLOBAL VALIDATION MONITORING --------------------

    private void StartGlobalValidationMonitoring()
    {
#if UNITY_EDITOR
        Debug.Log("[TutorialManager] Starting global validation monitoring (tea type, toppings)");
#endif

        // Subscribe to tea type changes
        MugBeverageState.OnTeaTypeSet += OnGlobalTeaTypeSet;
#if UNITY_EDITOR
        Debug.Log("[TutorialManager] Subscribed to MugBeverageState.OnTeaTypeSet");
#endif

        // Subscribe to ANY topping addition (rose, mint, lemon via CupState)
        CupState.OnToppingAdded += OnGlobalToppingAdded;
#if UNITY_EDITOR
        Debug.Log("[TutorialManager] Subscribed to CupState.OnToppingAdded");
#endif

        // Subscribe to milk addition (milk uses separate system via MugBeverageState)
        MugBeverageState.OnMilkAdded += OnGlobalMilkAdded;
#if UNITY_EDITOR
        Debug.Log("[TutorialManager] Subscribed to MugBeverageState.OnMilkAdded");
#endif

        // Note: We don't monitor OnPowderAdded because powder drinks aren't part of this tutorial
    }

    private void StopGlobalValidationMonitoring()
    {
#if UNITY_EDITOR
        Debug.Log("[TutorialManager] Stopping global validation monitoring");
#endif

        MugBeverageState.OnTeaTypeSet -= OnGlobalTeaTypeSet;
        CupState.OnToppingAdded -= OnGlobalToppingAdded;
        MugBeverageState.OnMilkAdded -= OnGlobalMilkAdded;
    }

    private void OnGlobalTeaTypeSet(TeaType teaType)
    {
        // Ignore if tutorial is completed or not yet in kitchen
        if (tutorialCompleted || waitingForKitchenEntry) return;

        // Ignore if already showing a warning
        if (showingWrongTeaWarning || showingWrongWaterWarning) return;

#if UNITY_EDITOR
        Debug.Log($"[GlobalValidation] Tea type set: {teaType}");
#endif

        if (teaType != TeaType.Lavender)
        {
#if UNITY_EDITOR
            Debug.Log($"[GlobalValidation] ✗ WRONG TEA ({teaType}) detected → Wrong tea warning, reset to place mug");
#endif

            // Hide current dialogue
            tutorialCustomer?.HideTutorialDialogue();

            // Show wrong tea warning and reset
            MugBeverageState mug = GetTutorialMug();
            if (mug != null)
            {
                showingWrongTeaWarning = true;
                trackedMugForReset = mug;
                tutorialCustomer?.ShowTutorialDialogueKitchen((int)TutorialDialogue.WrongTeaOrToppings, null);
                StartCoroutine(WaitForMugEmptyThenResetToArray8());
            }
        }
    }

    private void OnGlobalToppingAdded(string toppingType)
    {
        // Ignore if tutorial is completed or not yet in kitchen
        if (tutorialCompleted || waitingForKitchenEntry) return;

        // Ignore if already showing a warning
        if (showingWrongTeaWarning || showingWrongWaterWarning) return;

#if UNITY_EDITOR
        Debug.Log($"[GlobalValidation] Topping '{toppingType}' added - checking if this is wrong...");
#endif

        // The tutorial drink (Hot Lavender Tea) should have NO toppings at all
        // Any topping (rose, mint, lemon) is wrong
        MugBeverageState mug = GetTutorialMug();
        if (mug != null && HasWrongToppings(mug))
        {
#if UNITY_EDITOR
            Debug.Log($"[GlobalValidation] ✗ WRONG TOPPING ('{toppingType}' not allowed) → Wrong toppings warning, reset to place mug");
#endif

            // Hide current dialogue
            tutorialCustomer?.HideTutorialDialogue();

            showingWrongTeaWarning = true;
            trackedMugForReset = mug;
            tutorialCustomer?.ShowTutorialDialogueKitchen((int)TutorialDialogue.WrongToppings, null);
            StartCoroutine(WaitForMugEmptyThenResetToArray8());
        }
    }

    private void OnGlobalMilkAdded()
    {
        // Ignore if tutorial is completed or not yet in kitchen
        if (tutorialCompleted || waitingForKitchenEntry) return;

        // Ignore if already showing a warning
        if (showingWrongTeaWarning || showingWrongWaterWarning) return;

#if UNITY_EDITOR
        Debug.Log($"[GlobalValidation] Milk added - checking if this is wrong...");
#endif

        // The tutorial drink (Hot Lavender Tea) should have NO milk
        MugBeverageState mug = GetTutorialMug();
        if (mug != null && HasWrongToppings(mug))
        {
#if UNITY_EDITOR
            Debug.Log($"[GlobalValidation] ✗ WRONG TOPPING (milk not allowed) → Wrong toppings warning, reset to place mug");
#endif

            // Hide current dialogue
            tutorialCustomer?.HideTutorialDialogue();

            showingWrongTeaWarning = true;
            trackedMugForReset = mug;
            tutorialCustomer?.ShowTutorialDialogueKitchen((int)TutorialDialogue.WrongToppings, null);
            StartCoroutine(WaitForMugEmptyThenResetToArray8());
        }
    }

    // -------------------- COROUTINES --------------------
    private IEnumerator StartTutorialRoutine()
    {
        yield return new WaitForSeconds(spawnDelay);
#if UNITY_EDITOR
        Debug.Log("[TutorialManager] Starting tutorial - spawning tutorial customer");
#endif

        if (tutorialCustomerPrefab == null || tutorialCustomerData == null)
        {
#if UNITY_EDITOR
            Debug.LogError("[TutorialManager] Missing tutorialCustomerPrefab or tutorialCustomerData!");
#endif
            yield break;
        }

        if (spawnPoint == null || counterPoint == null)
        {
#if UNITY_EDITOR
            Debug.LogError("[TutorialManager] Missing spawn or counter points!");
#endif
            yield break;
        }

        GameObject customerObj = Instantiate(tutorialCustomerPrefab, spawnPoint.position, Quaternion.identity);
        tutorialCustomer = customerObj.GetComponent<TutorialCustomer>();

        if (tutorialCustomer == null)
        {
#if UNITY_EDITOR
            Debug.LogError("[TutorialManager] Spawned prefab does not have TutorialCustomer component!");
#endif
            yield break;
        }

        tutorialCustomer.InitializeTutorial(
            tutorialCustomerData,
            counterPoint,
            tutorialGreetingDialogue,
            spawnPoint,
            openDoorPoint,
            doorPoint,
            doorClosed,
            doorOpen
        );

#if UNITY_EDITOR
        Debug.Log("[TutorialManager] Tutorial customer spawned and initialized successfully");
#endif

        tutorialCustomer.OnTutorialArrivedAtCounter += OnTutorialCustomerArrivedAtCounter;
        tutorialCustomer.OnTutorialGreetingComplete += OnTutorialGreetingComplete;

        // Start global validation monitoring immediately (detects wrong tea/toppings/water at any time)
        StartGlobalValidationMonitoring();
    }

    private IEnumerator CompleteTutorialRoutine()
    {
        yield return new WaitForSeconds(3f);
        tutorialCompleted = true;

        GameManager.Instance?.MarkTutorialCompleted();
#if UNITY_EDITOR
        Debug.Log("[TutorialManager] Tutorial complete! Normal gameplay begins.");
#endif
    }

    private IEnumerator DelayedDialogue(System.Action dialogueAction, float delay)
    {
        yield return new WaitForSeconds(delay);
        dialogueAction?.Invoke();
    }

    private IEnumerator ResetSpriteAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        tutorialCustomer?.SetBodyDefault();
    }


    // -------------------- STATE VALIDATION --------------------
    // These methods check actual game state to handle out-of-order player actions

    /// <summary>
    /// Check if a mug is currently placed on the dispenser
    /// </summary>
    private bool IsMugOnDispenser(out MugBeverageState mug)
    {
        mug = null;
        EnsureDispenserReference();

        if (trackedDispenser == null)
        {
#if UNITY_EDITOR
            Debug.Log("[IsMugOnDispenser] No DispenserController found");
#endif
            return false;
        }

        // Check SnapSocket
        var socket = trackedDispenser.GetComponentInChildren<SnapSocket>();
        if (socket != null)
        {
#if UNITY_EDITOR
            Debug.Log($"[IsMugOnDispenser] Socket found - Occupied={socket.Occupied}, CurrentCup={(socket.CurrentCup != null ? socket.CurrentCup.name : "null")}");
#endif

            if (socket.Occupied && socket.CurrentCup != null)
            {
                mug = socket.CurrentCup.GetComponent<MugBeverageState>();
                if (mug != null)
                {
#if UNITY_EDITOR
                    Debug.Log($"[IsMugOnDispenser] ✓ Found mug on dispenser via SnapSocket: {mug.name}");
#endif
                    return true;
                }
            }
        }
        else
        {
#if UNITY_EDITOR
            Debug.Log("[IsMugOnDispenser] No SnapSocket found on dispenser");
#endif
        }

        // Fallback: Physics check
        Transform socketPoint = trackedDispenser.transform.Find("SocketPoint");
        if (socketPoint != null)
        {
            var hits = Physics2D.OverlapCircleAll(socketPoint.position, 0.25f);
#if UNITY_EDITOR
            Debug.Log($"[IsMugOnDispenser] Physics check found {hits.Length} objects near SocketPoint");
#endif

            foreach (var hit in hits)
            {
                if (hit != null && hit.CompareTag("Cup"))
                {
                    mug = hit.GetComponent<MugBeverageState>();
                    if (mug == null)
                        mug = hit.GetComponentInParent<MugBeverageState>();
                    if (mug != null)
                    {
#if UNITY_EDITOR
                        Debug.Log($"[IsMugOnDispenser] ✓ Found mug via physics: {mug.name}");
#endif
                        return true;
                    }
                }
            }
        }
        else
        {
#if UNITY_EDITOR
            Debug.Log("[IsMugOnDispenser] No SocketPoint found on dispenser");
#endif
        }

#if UNITY_EDITOR
        Debug.Log("[IsMugOnDispenser] No mug found on dispenser");
#endif
        return false;
    }

    /// <summary>
    /// Check if any mug in the scene has water
    /// </summary>
    private bool IsMugFilled(out MugBeverageState filledMug, out WaterTemp temperature)
    {
        filledMug = null;
        temperature = WaterTemp.Cold;

        var mugs = FindObjectsByType<MugBeverageState>(FindObjectsSortMode.None);
#if UNITY_EDITOR
        Debug.Log($"[IsMugFilled] Found {mugs.Length} MugBeverageState objects in scene");
#endif

        foreach (var mug in mugs)
        {
#if UNITY_EDITOR
            Debug.Log($"[IsMugFilled] Checking mug '{mug.name}': HasWater={mug.HasWater}");
#endif
            if (mug != null && mug.HasWater)
            {
                filledMug = mug;
                temperature = mug.WaterTemperature ?? WaterTemp.Cold;
#if UNITY_EDITOR
                Debug.Log($"[IsMugFilled] ✓ Found filled mug: {mug.name}, temp={temperature}");
#endif
                return true;
            }
        }

#if UNITY_EDITOR
        Debug.Log("[IsMugFilled] No filled mug found");
#endif
        return false;
    }

    /// <summary>
    /// Check if the correct tea (Lavender) is in any mug, and if it's brewed
    /// </summary>
    private bool IsCorrectTeaInMug(out MugBeverageState teaMug, out bool isBrewed)
    {
        teaMug = null;
        isBrewed = false;

        foreach (var mug in FindObjectsByType<MugBeverageState>(FindObjectsSortMode.None))
        {
            if (mug != null && mug.TeaType == TeaType.Lavender)
            {
                teaMug = mug;
                isBrewed = mug._steepCount >= 3;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Check if a lavender tea packet exists in the scene
    /// </summary>
    private bool IsLavenderPacketInScene(out TeaPacket packet)
    {
        packet = null;

        var teabagObj = GameObject.Find("TeabagParent");
        if (teabagObj != null && teabagObj.activeInHierarchy)
        {
            var teabag = teabagObj.GetComponent<Teabag>();
            if (teabag != null && teabag.teaDefinition != null && teabag.teaDefinition.teaType == TeaType.Lavender)
            {
                return true;
            }
        }

        // Check all TeaPacket objects
        foreach (var p in FindObjectsByType<TeaPacket>(FindObjectsSortMode.None))
        {
            if (p != null && p.teaDefinition != null && p.teaDefinition.teaType == TeaType.Lavender)
            {
                packet = p;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Check if dispenser is currently filling
    /// </summary>
    private bool IsDispenserBusy(out WaterTemp temperature)
    {
        temperature = WaterTemp.Cold;
        EnsureDispenserReference();

        if (trackedDispenser == null) return false;

        if (trackedDispenser.IsBusy)
        {
            temperature = trackedDispenser.LastFillTemp;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Check if mug has any toppings or ice (tutorial drink should have none)
    /// </summary>
    private bool HasWrongToppings(MugBeverageState mug)
    {
        if (mug == null) return false;

        var cupState = mug.GetComponent<CupState>();
        if (cupState == null) return false;

        // Tutorial drink should have NO toppings at all
        return cupState.Toppings != null && cupState.Toppings.Count > 0;
    }

    /// <summary>
    /// Check if a used lavender teabag exists in the scene (for composting)
    /// </summary>
    private bool UsedLavenderTeabagExists()
    {
        var teabagObj = GameObject.Find("TeabagParent");
        if (teabagObj != null && teabagObj.activeInHierarchy)
        {
            var teabag = teabagObj.GetComponent<Teabag>();
            if (teabag != null && teabag.teaDefinition != null && teabag.teaDefinition.teaType == TeaType.Lavender)
            {
                return true;
            }
        }
        return false;
    }

    // -------------------- HELPER METHODS --------------------
    private int GetCurrentCameraStation()
    {
        if (cameraStations == null) return -1;
        return cameraStations.CurrentIndex;
    }


    // -------------------- PUBLIC INTERFACE --------------------
    /// <summary>Call this when the player has successfully made the tutorial order</summary>
    public void OnTutorialOrderComplete(bool wasCorrect = true)
    {
        tutorialCustomer.TriggerLeave(wasCorrect: true, immediate: true);
        StartCoroutine(CompleteTutorialRoutine());
    }

    /// <summary>Check if tutorial is currently active</summary>
    public bool IsTutorialActive() => tutorialStarted && !tutorialCompleted;

    /// <summary>Get the tutorial customer reference</summary>
    public TutorialCustomer GetTutorialCustomer() => tutorialCustomer;

    // -------------------- NEW ARRAY VALIDATION --------------------

    /// <summary>
    /// Called by ServeHandoff before normal validation. Returns true if tutorial handles this serve, false to continue normal flow.
    /// </summary>
    public bool TryHandleTutorialServe(GameObject cup)
    {
        if (!IsTutorialActive()) return false;

        var mugBeverage = cup.GetComponent<MugBeverageState>();
        if (mugBeverage == null)
        {
            mugBeverage = cup.GetComponentInChildren<MugBeverageState>();
        }

        if (mugBeverage == null)
        {
#if UNITY_EDITOR
            Debug.LogWarning("[TutorialManager] No MugBeverageState found on served cup!");
#endif
            return false;
        }

        // Check for empty cup (Array 18)
        if (mugBeverage.IsEmpty())
        {
#if UNITY_EDITOR
            Debug.Log("[TutorialManager] Empty cup served - showing Array 18");
#endif
            ShowEmptyCupDialogue();
            return true; // Tutorial handles this - don't continue with normal validation
        }

        // Check for just water (Array 19)
        if (mugBeverage.HasWater && !mugBeverage.HasTea && !mugBeverage.HasMilk)
        {
#if UNITY_EDITOR
            Debug.Log("[TutorialManager] Just water served - showing Array 19");
#endif
            ShowJustWaterDialogue();
            return true; // Tutorial handles this - don't continue with normal validation
        }

        // Otherwise, let normal validation proceed
        return false;
    }

    private void ShowReminderDialogue()
    {
        if (tutorialCustomer == null || tutorialCompleted) return;

#if UNITY_EDITOR
        Debug.Log("[TutorialManager] Showing 5-minute reminder (Array 20)");
#endif
        tutorialCustomer.ShowTutorialDialogueKitchen((int)TutorialDialogue.ReminderAfter5Minutes, null);

        // Auto-fade after a delay (like normal dialogue)
        StartCoroutine(FadeReminderAfterDelay());
    }

    private IEnumerator FadeReminderAfterDelay()
    {
        yield return waitFourSeconds;
        tutorialCustomer?.HideTutorialDialogue();
    }

    private void ShowEmptyCupDialogue()
    {
        if (tutorialCustomer == null || tutorialCompleted) return;

#if UNITY_EDITOR
        Debug.Log("[TutorialManager] Showing empty cup dialogue (Array 18) at counter");
#endif

        // Clear order icon before showing dialogue
        ClearOrderIcon();

        // Hide any active kitchen dialogue
        tutorialCustomer.HideTutorialDialogue();

        // Force counter popup (reset kitchen flag)
        tutorialCustomer.ForceCounterPopup();

        // Show at counter popup (not kitchen), then customer leaves
        tutorialCustomer.ShowTutorialDialogue((int)TutorialDialogue.EmptyCup, () =>
        {
#if UNITY_EDITOR
            Debug.Log("[TutorialManager] Empty cup dialogue complete - customer leaving");
#endif
            OnTutorialOrderComplete(false);
        });
    }

    private void ShowJustWaterDialogue()
    {
        if (tutorialCustomer == null || tutorialCompleted) return;

#if UNITY_EDITOR
        Debug.Log("[TutorialManager] Showing just water dialogue (Array 19) at counter");
#endif

        // Clear order icon before showing dialogue
        ClearOrderIcon();

        // Hide any active kitchen dialogue
        tutorialCustomer.HideTutorialDialogue();

        // Force counter popup (reset kitchen flag)
        tutorialCustomer.ForceCounterPopup();

        // Show at counter popup (not kitchen), then customer leaves
        tutorialCustomer.ShowTutorialDialogue((int)TutorialDialogue.JustWater, () =>
        {
#if UNITY_EDITOR
            Debug.Log("[TutorialManager] Just water dialogue complete - customer leaving");
#endif
            OnTutorialOrderComplete(false);
        });
    }

    private void ClearOrderIcon()
    {
        if (tutorialCustomer == null) return;

        // Find and clear the order icon
        Transform orderRoot = tutorialCustomer.transform.Find("BearCanvas/Order");
        if (orderRoot != null)
        {
            orderRoot.gameObject.SetActive(false);
#if UNITY_EDITOR
            Debug.Log("[TutorialManager] Order icon cleared");
#endif
        }

        // Also try to clear via OrderIconSimple component
        var icon = tutorialCustomer.GetComponentInChildren<OrderIconSimple>(includeInactive: true);
        if (icon != null)
        {
            icon.Clear();
#if UNITY_EDITOR
            Debug.Log("[TutorialManager] OrderIconSimple cleared");
#endif
        }
    }
}

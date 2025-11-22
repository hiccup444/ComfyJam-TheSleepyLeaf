using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using TMPro;

/// <summary>
/// Tutorial customer that extends Customer behavior with tutorial-specific dialogue
/// and manual control over ordering and leaving.
/// </summary>
public class TutorialCustomer : Customer
{
    // -------------------- FIELDS --------------------
    [Header("Tutorial Settings")]
    [Tooltip("Custom greeting dialogue for tutorial")]
    [SerializeField] private DialogueData tutorialGreeting;

    [Tooltip("If true, tutorial must manually call TriggerOrder()")]
    [SerializeField] private bool manualOrderControl = true;

    public System.Action OnTutorialArrivedAtCounter;
    public System.Action OnTutorialGreetingComplete;

    // Auto-found worldspace UI references
    public DialoguePopup tutorialPopupCounter;
    private DialoguePopup tutorialPopupKitchen;
    private GameObject dialogueChoices;

    private bool hasArrivedAtCounter = false;
    private bool tutorialGreetingPlayed = false;
    private bool useKitchenPopup = false; // Switch to kitchen popup after index 6


    // -------------------- UNITY METHODS --------------------
    private new void Awake()
    {
        // CRITICAL: Call base Awake to find bodyTransform and initialize components
        base.Awake();
        
        // Then find our tutorial-specific UI elements
        FindTutorialUIElements();
    }

    private new void Update()
    {
        // Always allow base Update to run for non-manual behavior (like patience draining when not in manual mode)
        if (!manualOrderControl)
        {
            base.Update();
        }
        
        // Handle only tutorial-specific arrival logic
        if (!hasArrivedAtCounter && isAtCounter && currentState == CustomerState.Greeting)
        {
            hasArrivedAtCounter = true;
            #if UNITY_EDITOR
            Debug.Log("[TutorialCustomer] Arrived at counter - firing event");
            #endif
            OnTutorialArrivedAtCounter?.Invoke();
        }
    }


    // -------------------- INITIALIZATION --------------------
    /// <summary>
    /// Override CustomerRoutine to provide manual control over greeting and ordering
    /// </summary>
    private IEnumerator TutorialCustomerRoutine()
    {
        currentState = CustomerState.Arriving;

        if (targetCounterPoint != null)
            yield return StartCoroutine(MoveToCounter());

        isAtCounter = true;

        // In manual mode, we set the state but don't auto-show dialogue
        if (manualOrderControl)
        {
            currentState = CustomerState.Greeting;
            #if UNITY_EDITOR
            Debug.Log("[TutorialCustomer] At counter - waiting for manual greeting trigger");
            #endif
            // Tutorial will manually call ShowTutorialGreeting() when ready
        }
        else
        {
            // Not in manual mode - use normal customer behavior
            if (canOrder)
            {
                yield return StartCoroutine(WaitForDialogueTurn());
                currentState = CustomerState.Greeting;

                float actualGreetingDuration = 0f;
                if (data.greetingDialogue != null)
                {
                    string greeting = data.greetingDialogue.GetRandomLine();
                    actualGreetingDuration = Speak(greeting);
                }

                yield return new WaitForSeconds(actualGreetingDuration > 0f ? actualGreetingDuration : greetingPauseDuration);

                currentState = CustomerState.Ordering;

                float actualOrderDuration = 0f;
                if (currentOrderPreference != null && currentOrderPreference.orderDialogue != null)
                {
                    string orderText = currentOrderPreference.orderDialogue.GetRandomLine();
                    actualOrderDuration = Speak(orderText);
                }

                OnOrderPlaced?.Invoke(currentOrder);
                
                yield return new WaitForSeconds(actualOrderDuration > 0f ? actualOrderDuration + orderPauseDuration : dialogueDuration + orderPauseDuration);

                ReleaseDialogueTurn();

                if (orderRoot != null)
                    orderRoot.gameObject.SetActive(true);

                currentState = CustomerState.Waiting;
            }
            else
            {
                currentState = CustomerState.Waiting;
                #if UNITY_EDITOR
                Debug.Log($"{data.customerName} waiting silently at {targetCounterPoint.name}");
                #endif
                OnReadyToAdvance?.Invoke();
            }
        }
    }

    /// <summary>
    /// Automatically find tutorial UI elements in the scene
    /// </summary>
    private void FindTutorialUIElements()
    {
        // Find CustomerCanvas in scene
        GameObject customerCanvas = GameObject.Find("CustomerCanvas");
        if (customerCanvas == null)
        {
            #if UNITY_EDITOR
            Debug.LogError("[TutorialCustomer] CustomerCanvas not found in scene!");
            #endif
            return;
        }

        // Find TutorialPopupCounter
        Transform counterPopupTransform = customerCanvas.transform.Find("TutorialPopupCounter");
        if (counterPopupTransform != null)
        {
            tutorialPopupCounter = counterPopupTransform.GetComponent<DialoguePopup>();
            if (tutorialPopupCounter != null)
            {
                // Find the text component
                Transform textTransform = counterPopupTransform.Find("TutorialPopupCounterText");
                if (textTransform != null)
                {
                    tutorialPopupCounter.tmpText = textTransform.GetComponent<TMPro.TextMeshProUGUI>();
                    if (tutorialPopupCounter.tmpText == null)
                    {
                        tutorialPopupCounter.legacyText = textTransform.GetComponent<UnityEngine.UI.Text>();
                    }
                }
                #if UNITY_EDITOR
                Debug.Log("[TutorialCustomer] Found TutorialPopupCounter");
                #endif
            }
        }
        else
        {
            #if UNITY_EDITOR
            Debug.LogWarning("[TutorialCustomer] TutorialPopupCounter not found in CustomerCanvas!");
            #endif
        }

        // Find TutorialPopupKitchen
        Transform kitchenPopupTransform = customerCanvas.transform.Find("TutorialPopupKitchen");
        if (kitchenPopupTransform != null)
        {
            tutorialPopupKitchen = kitchenPopupTransform.GetComponent<DialoguePopup>();
            if (tutorialPopupKitchen != null)
            {
                // Find the text component
                Transform textTransform = kitchenPopupTransform.Find("TutorialPopupKitchenText");
                if (textTransform != null)
                {
                    tutorialPopupKitchen.tmpText = textTransform.GetComponent<TMPro.TextMeshProUGUI>();
                    if (tutorialPopupKitchen.tmpText == null)
                    {
                        tutorialPopupKitchen.legacyText = textTransform.GetComponent<UnityEngine.UI.Text>();
                    }
                }
                #if UNITY_EDITOR
                Debug.Log("[TutorialCustomer] Found TutorialPopupKitchen");
                #endif
            }
        }
        else
        {
            #if UNITY_EDITOR
            Debug.LogWarning("[TutorialCustomer] TutorialPopupKitchen not found in CustomerCanvas!");
            #endif
        }

        // Find DialogueChoices
        Transform dialogueChoicesTransform = customerCanvas.transform.Find("DialogueChoices");
        if (dialogueChoicesTransform != null)
        {
            dialogueChoices = dialogueChoicesTransform.gameObject;
            
            // Assign button listeners for Yes/No choices
            AssignDialogueChoiceButtons(dialogueChoicesTransform);

            #if UNITY_EDITOR
            Debug.Log("[TutorialCustomer] Found DialogueChoices");
            #endif
        }
        else
        {
            #if UNITY_EDITOR
            Debug.LogWarning("[TutorialCustomer] DialogueChoices not found in CustomerCanvas!");
            #endif
        }
    }

    /// <summary>
    /// Assign button click listeners for dialogue choices
    /// </summary>
    private void AssignDialogueChoiceButtons(Transform dialogueChoicesTransform)
    {
        // Find Yes button
        Transform yesButtonTransform = dialogueChoicesTransform.Find("DialogueChoiceYes");
        if (yesButtonTransform != null)
        {
            UnityEngine.UI.Button yesButton = yesButtonTransform.GetComponent<UnityEngine.UI.Button>();
            if (yesButton != null)
            {
                // Clear any existing listeners and add our method
                yesButton.onClick.RemoveAllListeners();
                yesButton.onClick.AddListener(() => OnDialogueChoiceYes());
                #if UNITY_EDITOR
                Debug.Log("[TutorialCustomer] Assigned Yes button listener");
                #endif
            }
            else
            {
                #if UNITY_EDITOR
                Debug.LogWarning("[TutorialCustomer] DialogueChoiceYes has no Button component!");
                #endif
            }
        }
        else
        {
            #if UNITY_EDITOR
            Debug.LogWarning("[TutorialCustomer] DialogueChoiceYes not found!");
            #endif
        }

        // Find No button
        Transform noButtonTransform = dialogueChoicesTransform.Find("DialogueChoiceNo");
        if (noButtonTransform != null)
        {
            UnityEngine.UI.Button noButton = noButtonTransform.GetComponent<UnityEngine.UI.Button>();
            if (noButton != null)
            {
                // Clear any existing listeners and add our method
                noButton.onClick.RemoveAllListeners();
                noButton.onClick.AddListener(() => OnDialogueChoiceNo());
                #if UNITY_EDITOR
                Debug.Log("[TutorialCustomer] Assigned No button listener");
                #endif
            }
            else
            {
                #if UNITY_EDITOR
                Debug.LogWarning("[TutorialCustomer] DialogueChoiceNo has no Button component!");
                #endif
            }
        }
        else
        {
            #if UNITY_EDITOR
            Debug.LogWarning("[TutorialCustomer] DialogueChoiceNo not found!");
            #endif
        }
    }

    /// <summary>
    /// Initialize tutorial customer with optional custom greeting
    /// </summary>
    public void InitializeTutorial(
        CustomerData customerData,
        Transform counterPoint,
        DialogueData customGreeting = null,
        Transform exit = null,
        Transform openDoor = null,
        Transform door = null,
        GameObject doorClosedObj = null,
        GameObject doorOpenObj = null)
    {
        #if UNITY_EDITOR
        Debug.Log("[TutorialCustomer] InitializeTutorial called");
        #endif

        if (customGreeting != null)
            tutorialGreeting = customGreeting;

        // Set up all the base customer data (but don't call Initialize which would start CustomerRoutine)
        data = customerData;
        targetCounterPoint = counterPoint;
        exitPoint = exit;
        openDoorPoint = openDoor;
        doorPoint = door;
        doorClosed = doorClosedObj;
        doorOpen = doorOpenObj;

        // Door audio discovery (copied from base Initialize)
        doorAudio =
            (doorPoint != null ? (doorPoint.GetComponent<DoorAudioFeedback>() ?? doorPoint.GetComponentInParent<DoorAudioFeedback>() ?? doorPoint.GetComponentInChildren<DoorAudioFeedback>(true)) : null)
            ?? (doorClosed != null ? (doorClosed.GetComponent<DoorAudioFeedback>() ?? doorClosed.GetComponentInParent<DoorAudioFeedback>() ?? doorClosed.GetComponentInChildren<DoorAudioFeedback>(true)) : null)
            ?? (doorOpen != null ? (doorOpen.GetComponent<DoorAudioFeedback>() ?? doorOpen.GetComponentInParent<DoorAudioFeedback>() ?? doorOpen.GetComponentInChildren<DoorAudioFeedback>(true)) : null);

        if (doorClosed != null) doorClosed.SetActive(true);
        if (doorOpen != null) doorOpen.SetActive(false);

        // Push customer behind door initially
        var allRenderers = GetComponentsInChildren<SpriteRenderer>(includeInactive: true);
        foreach (var r in allRenderers)
        {
            originalSortingOrders[r] = r.sortingOrder;
            r.sortingOrder = -5;
        }

        if (bodyTransform != null)
            transform.localScale = Vector3.one * startScale;

        // Set body sprite to default mood on spawn
        SetBodyDefault();

        // Check ordering point
        if (counterPoint != null && counterPoint.name == orderingCounterPointName)
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

        currentOrderPreference = data.GetRandomOrder();
        currentOrder = currentOrderPreference != null ? currentOrderPreference.orderName : "Unknown Order";
        if (currentOrderPreference == null)
        {
            #if UNITY_EDITOR
            Debug.LogWarning($"{customerData.customerName} has no valid order preferences!");
            #endif
        }

        // Start our custom tutorial routine instead of base CustomerRoutine
        StartCoroutine(TutorialCustomerRoutine());

        #if UNITY_EDITOR
        Debug.Log($"[TutorialCustomer] Initialized - startScale: {startScale}, targetScale: {targetScale}, moveSpeed: {moveSpeed}");
        #endif
    }


    // -------------------- DIALOGUE METHODS --------------------
    /// <summary>Show tutorial greeting dialogue</summary>
    public void ShowTutorialGreeting()
    {
        if (tutorialGreetingPlayed) return;

        if (tutorialGreeting != null && tutorialPopupCounter != null)
        {
            tutorialGreetingPlayed = true;
            tutorialPopupCounter.gameObject.SetActive(true);
            ApplyDialogSpeechSettings(tutorialPopupCounter);
            tutorialPopupCounter.ShowDialogue(tutorialGreeting, () =>
            {
                tutorialPopupCounter.gameObject.SetActive(false);
                OnTutorialGreetingComplete?.Invoke();
            });
        }
        else
        {
            #if UNITY_EDITOR
            Debug.LogWarning("[TutorialCustomer] No tutorial greeting or counter popup assigned!");
            #endif
            OnTutorialGreetingComplete?.Invoke();
        }
    }


    /// <summary>Show a tutorial dialogue by index from CustomerData</summary>
    public void ShowTutorialDialogue(int index, System.Action onComplete = null)
    {
        if (data == null || !data.HasTutorialDialogues())
        {
            #if UNITY_EDITOR
            Debug.LogWarning("[TutorialCustomer] No tutorial dialogues available in CustomerData!");
            #endif
            onComplete?.Invoke();
            return;
        }

        // Switch to kitchen popup for indices 8-16 (brewing instructions)
        // Arrays 0-6, 17-19 use counter popup
        // Array 20 uses kitchen popup
        if (index >= 8 && index <= 16)
        {
            useKitchenPopup = true;
        }
        else if (index == 20)
        {
            useKitchenPopup = true;
        }
        else
        {
            useKitchenPopup = false;
        }

        DialogueData dialogue = data.GetTutorialDialogue(index);
        if (dialogue != null)
        {
            DialoguePopup targetPopup = useKitchenPopup ? tutorialPopupKitchen : tutorialPopupCounter;
            
            if (targetPopup != null)
            {
                targetPopup.gameObject.SetActive(true);
                ApplyDialogSpeechSettings(targetPopup);
                targetPopup.ShowDialogue(dialogue, () =>
                {
                    targetPopup.gameObject.SetActive(false);
                    onComplete?.Invoke();
                });
            }
            else
            {
                #if UNITY_EDITOR
                Debug.LogWarning($"[TutorialCustomer] {(useKitchenPopup ? "Kitchen" : "Counter")} popup not assigned!");
                #endif
                onComplete?.Invoke();
            }
        }
        else
        {
            onComplete?.Invoke();
        }
    }

    /// <summary>Show a tutorial dialogue without disabling popup - for chaining dialogues</summary>
    public void ShowTutorialDialogueChained(int index, System.Action onComplete = null)
    {
        if (data == null || !data.HasTutorialDialogues())
        {
            #if UNITY_EDITOR
            Debug.LogWarning("[TutorialCustomer] No tutorial dialogues available in CustomerData!");
            #endif
            onComplete?.Invoke();
            return;
        }

        // Switch to kitchen popup after index 6
        if (index > 6)
        {
            useKitchenPopup = true;
        }

        DialogueData dialogue = data.GetTutorialDialogue(index);
        if (dialogue != null)
        {
            DialoguePopup targetPopup = useKitchenPopup ? tutorialPopupKitchen : tutorialPopupCounter;
            
            if (targetPopup != null)
            {
                // Don't reactivate if already active - just show new dialogue
                if (!targetPopup.gameObject.activeSelf)
                    targetPopup.gameObject.SetActive(true);
                    
                ApplyDialogSpeechSettings(targetPopup);
                targetPopup.ShowDialogue(dialogue, () =>
                {
                    // DON'T disable popup - allow chaining
                    onComplete?.Invoke();
                });
            }
            else
            {
                #if UNITY_EDITOR
                Debug.LogWarning($"[TutorialCustomer] {(useKitchenPopup ? "Kitchen" : "Counter")} popup not assigned!");
                #endif
                onComplete?.Invoke();
            }
        }
        else
        {
            onComplete?.Invoke();
        }
    }

    /// <summary>Show a tutorial dialogue specifically on the kitchen popup</summary>
    public void ShowTutorialDialogueKitchen(int index, System.Action onComplete = null)
    {
        if (data == null || !data.HasTutorialDialogues())
        {
            #if UNITY_EDITOR
            Debug.LogWarning("[TutorialCustomer] No tutorial dialogues available in CustomerData!");
            #endif
            onComplete?.Invoke();
            return;
        }

        DialogueData dialogue = data.GetTutorialDialogue(index);
        if (dialogue != null)
        {
            if (tutorialPopupKitchen != null)
            {
                // Activate kitchen popup if not already active
                if (!tutorialPopupKitchen.gameObject.activeSelf)
                    tutorialPopupKitchen.gameObject.SetActive(true);

                ApplyDialogSpeechSettings(tutorialPopupKitchen);
                
                // If no completion callback provided, use persistent mode (stays visible indefinitely)
                if (onComplete == null)
                {
                    tutorialPopupKitchen.ShowDialoguePersistent(dialogue);
                    #if UNITY_EDITOR
                    Debug.Log($"[TutorialCustomer] Showing persistent kitchen dialogue (Array {index}) - will stay visible until manually hidden");
                    #endif
                }
                else
                {
                    // With callback - normal timed display
                    tutorialPopupKitchen.ShowDialogue(dialogue, () =>
                    {
                        tutorialPopupKitchen.gameObject.SetActive(false);
                        onComplete?.Invoke();
                    });
                }
            }
            else
            {
                #if UNITY_EDITOR
                Debug.LogWarning("[TutorialCustomer] Kitchen popup not assigned!");
                #endif
                onComplete?.Invoke();
            }
        }
        else
        {
            onComplete?.Invoke();
        }
    }


    /// <summary>Show a tutorial dialogue with Yes/No choices</summary>
    public void ShowTutorialDialogueWithChoices(int index, System.Action<bool> onPlayerResponse)
    {
        if (data == null || !data.HasTutorialDialogues())
        {
            #if UNITY_EDITOR
            Debug.LogWarning("[TutorialCustomer] No tutorial dialogues available in CustomerData!");
            #endif
            onPlayerResponse?.Invoke(false);
            return;
        }

        // Switch to kitchen popup after index 6
        if (index > 6)
        {
            useKitchenPopup = true;
        }

        DialogueData dialogue = data.GetTutorialDialogue(index);
        if (dialogue != null)
        {
            DialoguePopup targetPopup = useKitchenPopup ? tutorialPopupKitchen : tutorialPopupCounter;
            
            if (targetPopup != null)
            {
                // Assign dialogue choices to the popup if not already set
                if (targetPopup.dialogueChoices == null && dialogueChoices != null)
                {
                    targetPopup.dialogueChoices = dialogueChoices;
                }
                
                targetPopup.gameObject.SetActive(true);
                ApplyDialogSpeechSettings(targetPopup);
                targetPopup.ShowDialogueWithChoices(dialogue, (playerSaidYes) =>
                {
                    targetPopup.gameObject.SetActive(false);
                    onPlayerResponse?.Invoke(playerSaidYes);
                });
            }
            else
            {
                #if UNITY_EDITOR
                Debug.LogWarning($"[TutorialCustomer] {(useKitchenPopup ? "Kitchen" : "Counter")} popup not assigned!");
                #endif
                onPlayerResponse?.Invoke(false);
            }
        }
        else
        {
            onPlayerResponse?.Invoke(false);
        }
    }


    /// <summary>Shows a tutorial dialogue line that stays visible until hidden manually.</summary>
    public void ShowPersistentTutorialDialogue(int index)
    {
        if (data == null || !data.HasTutorialDialogues())
        {
            #if UNITY_EDITOR
            Debug.LogWarning("[TutorialCustomer] No tutorial dialogues available in CustomerData!");
            #endif
            return;
        }

        // Switch to kitchen popup after index 6
        if (index > 6)
        {
            useKitchenPopup = true;
        }

        DialogueData dialogue = data.GetTutorialDialogue(index);
        if (dialogue == null)
        {
            #if UNITY_EDITOR
            Debug.LogWarning($"[TutorialCustomer] No dialogue found at index {index}");
            #endif
            return;
        }

        DialoguePopup targetPopup = useKitchenPopup ? tutorialPopupKitchen : tutorialPopupCounter;

        if (targetPopup == null)
        {
            #if UNITY_EDITOR
            Debug.LogWarning($"[TutorialCustomer] {(useKitchenPopup ? "Kitchen" : "Counter")} popup not assigned!");
            #endif
            return;
        }

        targetPopup.gameObject.SetActive(true);
        targetPopup.StopAllCoroutines();

        string line = dialogue.GetRandomLine();
        targetPopup.fullText = line;

        if (targetPopup.tmpText != null)
            targetPopup.tmpText.text = line;
        else if (targetPopup.legacyText != null)
            targetPopup.legacyText.text = line;

        targetPopup.canvasGroup.alpha = 1f;
        targetPopup.StartCoroutine(targetPopup.AnimateAppear());
        #if UNITY_EDITOR
        Debug.Log($"[TutorialCustomer] Persistent dialogue shown on {(useKitchenPopup ? "Kitchen" : "Counter")} popup: \"{line}\"");
        #endif
    }


    /// <summary>Manually hide the currently visible tutorial dialogue popup</summary>
    public void HideTutorialDialogue()
    {
        if (tutorialPopupCounter != null && tutorialPopupCounter.gameObject.activeSelf)
        {
            tutorialPopupCounter.HideDialogue();
        }

        if (tutorialPopupKitchen != null && tutorialPopupKitchen.gameObject.activeSelf)
        {
            tutorialPopupKitchen.HideDialogue();
        }

        #if UNITY_EDITOR
        Debug.Log("[TutorialCustomer] Tutorial dialogue hiding with animation");
        #endif
    }

    /// <summary>Force next dialogue to use counter popup instead of kitchen popup</summary>
    public void ForceCounterPopup()
    {
        useKitchenPopup = false;
        #if UNITY_EDITOR
        Debug.Log("[TutorialCustomer] Forced to use counter popup for next dialogue");
        #endif
    }

    /// <summary>Called when player clicks Yes button</summary>
    private void OnDialogueChoiceYes()
    {
        // Forward to the appropriate popup's handler
        if (tutorialPopupCounter != null && tutorialPopupCounter.gameObject.activeSelf)
        {
            tutorialPopupCounter.OnPlayerRespondYes();
        }
        else if (tutorialPopupKitchen != null && tutorialPopupKitchen.gameObject.activeSelf)
        {
            tutorialPopupKitchen.OnPlayerRespondYes();
        }
    }

    /// <summary>Called when player clicks No button</summary>
    private void OnDialogueChoiceNo()
    {
        // Forward to the appropriate popup's handler
        if (tutorialPopupCounter != null && tutorialPopupCounter.gameObject.activeSelf)
        {
            tutorialPopupCounter.OnPlayerRespondNo();
        }
        else if (tutorialPopupKitchen != null && tutorialPopupKitchen.gameObject.activeSelf)
        {
            tutorialPopupKitchen.OnPlayerRespondNo();
        }
    }


    // -------------------- ORDER & LEAVE METHODS --------------------
    /// <summary>Manually trigger the order display</summary>
    public void TriggerOrder()
    {
        if (currentOrderPreference != null)
        {
            OnOrderPlaced?.Invoke(currentOrder);
            StartCoroutine(ShowOrderDialogueRoutine());
        }
        else
        {
            #if UNITY_EDITOR
            Debug.LogWarning("[TutorialCustomer] No order preference set!");
            #endif
        }
    }


    private IEnumerator ShowOrderDialogueRoutine()
    {
        yield return new WaitForSeconds(0.5f);

        if (currentOrderPreference != null && currentOrderPreference.orderDialogue != null)
        {
            if (tutorialPopupCounter != null)
            {
                tutorialPopupCounter.gameObject.SetActive(true);
                ApplyDialogSpeechSettings(tutorialPopupCounter);
                tutorialPopupCounter.ShowDialogue(currentOrderPreference.orderDialogue, () =>
                {
                    tutorialPopupCounter.gameObject.SetActive(false);
                });
            }
            else
            {
                #if UNITY_EDITOR
                Debug.LogWarning("[TutorialCustomer] Counter popup not assigned for order dialogue!");
                #endif
            }
        }
    }


    /// <summary>Manually trigger customer to leave</summary>
    public void TriggerLeave(bool wasCorrect = true, bool immediate = false)
    {
        if (immediate)
        {
            StartCoroutine(WalkOutImmediately());
            return;
        }

        StartCoroutine(LeaveRoutine(wasCorrect));
    }


    private IEnumerator LeaveRoutine(bool wasCorrect)
    {
        if (wasCorrect)
            SetBodyHappy();
        else
            SetBodyDisappointed();

        yield return new WaitForSeconds(0.5f);

        DialogueData responseDialogue = data != null
            ? (wasCorrect ? data.happyDialogue : data.disappointedDialogue)
            : null;

        if (responseDialogue != null && tutorialPopupCounter != null)
        {
            tutorialPopupCounter.gameObject.SetActive(true);
            ApplyDialogSpeechSettings(tutorialPopupCounter);
            tutorialPopupCounter.ShowDialogue(responseDialogue, () =>
            {
                tutorialPopupCounter.gameObject.SetActive(false);
                BeginLeaving();
            });
        }
        else
        {
            BeginLeaving();
        }
    }


    private void BeginLeaving()
    {
        currentState = CustomerState.Leaving;
        OnLeave?.Invoke();

        if (exitPoint != null)
            StartCoroutine(WalkToExit());
        else
            Destroy(gameObject, 0.5f);
    }


    private IEnumerator WalkOutImmediately()
    {
        #if UNITY_EDITOR
        Debug.Log("[TutorialCustomer] Leaving immediately (walk to door)");
        #endif

        if (openDoorPoint != null)
        {
            Vector3 startPos = transform.position;
            float startingScale = transform.localScale.x;
            float totalDist = Vector3.Distance(startPos, openDoorPoint.position);
            
            while (Vector3.Distance(transform.position, openDoorPoint.position) > 0.1f)
            {
                transform.position = Vector3.MoveTowards(transform.position, openDoorPoint.position, moveSpeed * Time.deltaTime);
                
                // Add bouncing animation
                bounceTimer += Time.deltaTime * moveSpeed * bounceFrequency;
                float bounce = Mathf.Abs(Mathf.Sin(bounceTimer)) * bounceHeight;
                
                // Add scaling (shrink as moving away)
                float dist = Vector3.Distance(startPos, transform.position);
                float prog = Mathf.Clamp01(dist / totalDist);
                float currentScale = Mathf.Lerp(startingScale, startScale, prog);
                
                if (bodyTransform != null)
                {
                    transform.localScale = Vector3.one * currentScale;
                    bodyTransform.localPosition = new Vector3(0f, bounce, 0f);
                }
                
                yield return null;
            }
        }

        if (doorClosed != null && doorOpen != null)
        {
            doorClosed.SetActive(false);
            doorOpen.SetActive(true);
            yield return new WaitForSeconds(0.3f);
            doorClosed.SetActive(true);
            doorOpen.SetActive(false);
        }

        Destroy(gameObject, 0.2f);
    }


    private IEnumerator WalkToExit()
    {
        Vector3 startPos = transform.position;
        float startingScale = transform.localScale.x;
        float totalDist = Vector3.Distance(startPos, exitPoint.position);
        
        while (Vector3.Distance(transform.position, exitPoint.position) > 0.1f)
        {
            transform.position = Vector3.MoveTowards(transform.position, exitPoint.position, moveSpeed * Time.deltaTime);

            // Bouncing animation
            bounceTimer += Time.deltaTime * moveSpeed * bounceFrequency;
            float bounce = Mathf.Abs(Mathf.Sin(bounceTimer)) * bounceHeight;

            // Scaling (shrink as moving away)
            float dist = Vector3.Distance(startPos, transform.position);
            float prog = Mathf.Clamp01(dist / totalDist);
            float currentScale = Mathf.Lerp(startingScale, startScale, prog);

            if (bodyTransform != null)
            {
                transform.localScale = Vector3.one * currentScale;
                bodyTransform.localPosition = new Vector3(0f, bounce, 0f);
            }

            yield return null;
        }

        if (doorPoint != null && doorClosed != null && doorOpen != null)
        {
            if (doorClosed != null) doorClosed.SetActive(false);
            if (doorOpen != null) doorOpen.SetActive(true);

            if (doorAudio != null) doorAudio.PlayOpen();
            yield return new WaitForSeconds(0.3f);

            if (doorClosed != null) doorClosed.SetActive(true);
            if (doorOpen != null) doorOpen.SetActive(false);

            if (doorAudio != null) doorAudio.PlayClose();
        }

        Destroy(gameObject, 0.2f);
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class ServeCoordinator : MonoBehaviour
{
    public static ServeCoordinator Instance { get; private set; }
    private const int ServeSortingBoost = 50;

    [Header("Optional: set directly if you want to force a specific customer")]
    [SerializeField] private Customer targetCustomer;

    [Header("Cup Respawn")]
    [SerializeField] private CupDispenser cupDispenser;   // <- assign in Inspector
    
    [Header("Cup Slide Settings")]
    [Tooltip("Speed at which cup slides to customer")]
    [SerializeField] private float cupSlideSpeed = 3f;
    
    [Tooltip("Maximum time for cup slide animation")]
    [SerializeField] private float cupSlideDuration = 1.5f;

    private readonly HashSet<int> cupsBeingServed = new HashSet<int>();
    public event System.Action OnServeSucceeded;
    public event System.Action OnServeFailed;

    private struct RendererSortingSnapshot
    {
        public SpriteRenderer Renderer;
        public int OriginalLayer;
        public int OriginalOrder;
        public int LockedLayer;
        public int LockedOrder;
    }

    private struct SortingGroupSnapshot
    {
        public SortingGroup Group;
        public int OriginalLayer;
        public int OriginalOrder;
        public bool OriginalEnabled;
        public int LockedLayer;
        public int LockedOrder;
        public bool LockedEnabled;
    }

    private struct SpriteMaskSnapshot
    {
        public SpriteMask Mask;
        public int OriginalFrontLayer;
        public int OriginalBackLayer;
        public int OriginalFrontOrder;
        public int OriginalBackOrder;
        public int LockedFrontLayer;
        public int LockedBackLayer;
        public int LockedFrontOrder;
        public int LockedBackOrder;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            #if UNITY_EDITOR
            Debug.LogWarning("[ServeCoordinator] Duplicate coordinator detected, destroying.", this);
            #endif
            Destroy(this);
            return;
        }
        Instance = this;
    }

    private IEnumerator FadeCup(SpriteRenderer[] renderers, Transform cupTransform, bool fadeDown, Vector3 baseScale, float duration = 0.4f)
    {
        if (renderers == null || renderers.Length == 0 || cupTransform == null)
        {
#if UNITY_EDITOR
            Debug.LogWarning("[SERVE][FADE] Missing renderers or transform, skipping fade", cupTransform);
#endif
            yield break;
        }

        var startScale = cupTransform.localScale;
        var targetScale = fadeDown ? 0.9f * baseScale : baseScale;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / duration);

            cupTransform.localScale = Vector3.Lerp(startScale, targetScale, k);

            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null) continue;
                var c = renderers[i].color;
                c.a = Mathf.Lerp(fadeDown ? 1f : 0f, fadeDown ? 0f : 1f, k);
                renderers[i].color = c;
            }
            if (t > duration && fadeDown)
            {
                #if UNITY_EDITOR
                Debug.LogWarning("[SERVE][FADE] Fade-down overshoot, correcting alpha/scale", cupTransform);
                #endif
            }
            yield return null;
        }
    }

    public bool TryServe(GameObject cup, Customer forcedCustomer = null, bool destroyCupAfterSuccess = true)
    {
        #if UNITY_EDITOR
        Debug.Log($"[SERVE] Cup dropped on '{name}'", this);
        #endif
        if (cup == null) return false;

        int cupId = cup.GetInstanceID();
        if (cupsBeingServed.Contains(cupId))
        {
            #if UNITY_EDITOR
            Debug.Log("[SERVE] Cup is already being processed, ignoring duplicate drop.", cup);
            #endif
            return false;
        }

        // Check if tutorial wants to handle this serve (empty cup, just water, etc.)
        var tutorialManager = TutorialManager.Instance;
        if (tutorialManager != null && tutorialManager.TryHandleTutorialServe(cup))
        {
            #if UNITY_EDITOR
            Debug.Log("[SERVE] Tutorial handled this serve - skipping normal validation");
            #endif
            return true;
        }

        var customerOrder = ResolveCustomerOrder(forcedCustomer);
        if (customerOrder != null)
        {
            #if UNITY_EDITOR
            Debug.Log($"[SERVE] Using Customer '{customerOrder.name}' (has ActiveRecipe={(customerOrder.ActiveRecipe != null)})", this);
            #endif
        }

        if (customerOrder == null) return false;

        if (customerOrder.ActiveRecipe == null)
        {
            #if UNITY_EDITOR
            Debug.LogWarning("[SERVE][WARN] No active recipe; skipping validation", this);
            #endif
            return false;
        }

        var activeRecipe = customerOrder.ActiveRecipe;
        
        // Check final state (but continue to validation even if it fails)
        bool meetsBasicRequirements = MeetsRecipeFinalState(cup, activeRecipe, out var failureReason);
        if (!meetsBasicRequirements && !string.IsNullOrEmpty(failureReason))
        {
            #if UNITY_EDITOR
            Debug.Log(failureReason, cup);
            #endif
        }

        var adapter = new CupStateAdapter(cup);
        var cupDump = adapter.ToString();

        List<string> hints = null;
        var customer = customerOrder.GetComponent<Customer>();
        var validationResult = customerOrder.TryValidate(adapter, out var score, out var grade, out hints, cupDump);
        bool isCorrect = validationResult && (grade == RecipeGrade.Perfect || grade == RecipeGrade.Good);

        if (isCorrect)
        {
            #if UNITY_EDITOR
            Debug.Log($"[SERVE] Cup validated with grade {grade} (score {score}).", cup);
            if (hints != null && hints.Count > 0)
            {
                foreach (var hint in hints) Debug.Log(hint, cup);
            }
            #endif

            // CLEAR MINI ICON on success
            var icon = customerOrder.GetComponentInChildren<OrderIconSimple>(includeInactive: true);
            if (icon == null)
            {
                var customerTemp = customerOrder.GetComponentInParent<Customer>();
                icon = customerTemp ? customerTemp.GetComponentInChildren<OrderIconSimple>(true) : null;
            }

            if (icon != null)
            {
                icon.Clear();
            }
            else
            {
                #if UNITY_EDITOR
                Debug.LogWarning("[OrderIcon] Could not find icon to clear after serve.", customerOrder);
                #endif
            }

            StartServeCoroutine(cup, customer, destroyCupAfterSuccess, true);
            return true;
        }
        else
        {
            #if UNITY_EDITOR
            Debug.Log("[SERVE] Validation failed — customer will receive incorrect order.", cup);
            if (hints != null && hints.Count > 0)
            {
                foreach (var hint in hints) Debug.LogWarning("[HINT] " + hint, cup);
            }
            #endif

            if (customer != null)
            {
                StartServeCoroutine(cup, customer, destroyCupAfterFade: false, spawnReplacementCup: false);
            }
            else
            {
                #if UNITY_EDITOR
                Debug.LogWarning("[SERVE] No customer found - leaving cup in place.", cup);
                #endif
            }

            OnServeFailed?.Invoke();
            return false;
        }
    }

    private CustomerOrder ResolveCustomerOrder(Customer forcedCustomer)
    {
        if (forcedCustomer != null)
        {
            if (!IsCustomerReadyToServe(forcedCustomer))
            {
                #if UNITY_EDITOR
                Debug.Log($"[SERVE] {forcedCustomer.name} is not ready to be served yet.", forcedCustomer);
                #endif
                return null;
            }

            if (forcedCustomer.TryGetComponent<CustomerOrder>(out var forcedOrder))
            {
                if (forcedOrder.ActiveRecipe != null)
                    return forcedOrder;

                #if UNITY_EDITOR
                Debug.LogWarning($"[SERVE] {forcedCustomer.name} has no active recipe yet.", forcedCustomer);
                #endif
                return null;
            }

            #if UNITY_EDITOR
            Debug.LogWarning($"[SERVE] {forcedCustomer.name} is missing a CustomerOrder component.", forcedCustomer);
            #endif
            return null;
        }

        if (targetCustomer != null && IsCustomerReadyToServe(targetCustomer) &&
            targetCustomer.TryGetComponent<CustomerOrder>(out var targetOrder) && targetOrder.ActiveRecipe != null)
        {
            return targetOrder;
        }

        var orders = Object.FindObjectsByType<CustomerOrder>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var customerOrder in orders)
        {
            if (customerOrder.ActiveRecipe == null)
                continue;

            var customer = customerOrder.GetComponent<Customer>();
            if (!IsCustomerReadyToServe(customer))
                continue;

            return customerOrder;
        }

        return null;
    }

    private static bool MeetsRecipeFinalState(GameObject cup, RecipeSO recipe, out string failureReason)
    {
        failureReason = string.Empty;
        if (recipe == null)
            return true;

        if (cup == null)
        {
            failureReason = "[SERVE] No cup provided for validation.";
            return false;
        }

        var beverageState = cup.GetComponentInChildren<MugBeverageState>();
        var iceState = cup.GetComponentInChildren<MugIceState>();

        if (recipe.tea != null)
        {
            if (beverageState == null)
            {
                failureReason = "[SERVE] Tea recipe requires a MugBeverageState component.";
                return false;
            }

            var expectedTea = recipe.tea.teaType;
            if (expectedTea != TeaType.None && beverageState.TeaType != expectedTea)
            {
                failureReason = $"[SERVE] Expected tea {expectedTea} but cup has {beverageState.TeaType}.";
                return false;
            }

            var temp = beverageState.WaterTemperature;
            if (recipe.requiredWater == WaterSource.Hot && (!temp.HasValue || temp.Value != WaterTemp.Hot))
            {
                failureReason = "[SERVE] Requires hot water.";
                return false;
            }

            if (recipe.requiredWater == WaterSource.Cold && (!temp.HasValue || temp.Value != WaterTemp.Cold))
            {
                failureReason = "[SERVE] Requires cold water.";
                return false;
            }

            if (recipe.brewedRequired && !beverageState.IsBrewed)
            {
                failureReason = "[SERVE] Tea must be fully steeped.";
                return false;
            }
        }

        if (recipe.iceRequired || recipe.minIceChips > 0)
        {
            if (iceState == null)
            {
                failureReason = "[SERVE] Recipe requires ice but cup has no MugIceState.";
                return false;
            }

            if (recipe.minIceChips > 0 && iceState.IceCount < recipe.minIceChips)
            {
                failureReason = $"[SERVE] Recipe requires at least {recipe.minIceChips} ice chips (has {iceState.IceCount}).";
                return false;
            }

            if (recipe.iceRequired && iceState.IceCount <= 0)
            {
                failureReason = "[SERVE] Recipe requires ice.";
                return false;
            }
        }

        return true;
    }

    private void StartServeCoroutine(GameObject cup, Customer customer, bool destroyCupAfterFade, bool spawnReplacementCup)
    {
        if (cup == null || customer == null)
            return;

        int cupId = cup.GetInstanceID();
        if (!cupsBeingServed.Add(cupId))
            return;

        var snapper = cup.GetComponent<CupSnapper>();
        snapper?.SetServeCoordinatorLock(true);

        var sortingToggle = cup.GetComponentInParent<CupSortingGroupToggle>();
        sortingToggle?.EnterServeLock();

        StartCoroutine(HandleServeResult(cup, customer, destroyCupAfterFade, spawnReplacementCup, cupId));
    }

    private IEnumerator HandleServeResult(GameObject cup, Customer customer, bool destroyCupAfterFade, bool spawnReplacementCup, int cupId)
    {
        var collider = cup.GetComponent<Collider2D>();
        if (collider) collider.enabled = false;

        var snapper = cup.GetComponent<CupSnapper>();
        if (snapper != null)
            snapper.ForceRelease();

        var steam = cup.transform.Find("Visuals/steam");
        if (steam != null)
            steam.gameObject.SetActive(false);

        var originalScale = cup.transform.localScale;
        var rendererLocks = CaptureRendererSortingData(cup);
        var sortingGroupLocks = CaptureSortingGroupData(cup);
        var spriteMaskLocks = CaptureSpriteMaskData(cup);
        #if UNITY_EDITOR
        Debug.Log($"[SERVE][LOCK] Engaging serve-time layer lock (sprites={rendererLocks.Length}, sortingGroups={sortingGroupLocks.Length}, spriteMasks={spriteMaskLocks.Length})", cup);
        #endif
        var cupRenderers = ExtractRenderers(rendererLocks);
        ApplyServeLayerLock(rendererLocks, sortingGroupLocks, "initial");
        ApplySpriteMaskLock(spriteMaskLocks, "initial");

        try
        {
            if (customer == null)
            {
                #if UNITY_EDITOR
                Debug.LogWarning("[SERVE] No customer provided - destroying cup immediately", cup);
                #endif
                Destroy(cup);
                OnServeFailed?.Invoke();
                yield break;
            }

            var pickupPoint = customer.GetCupPickupPoint();
            if (pickupPoint == null)
            {
                if (destroyCupAfterFade)
                {
                    #if UNITY_EDITOR
                    Debug.LogWarning($"[SERVE] Customer {customer.name} has no cup pickup point assigned - destroying cup immediately", cup);
                    #endif
                    Destroy(cup);
                    OnServeFailed?.Invoke();
                }
                else
                {
                    #if UNITY_EDITOR
                    Debug.LogWarning($"[SERVE] Customer {customer.name} has no cup pickup point assigned - leaving cup in place", cup);
                    #endif
                    RestoreOriginalSorting(cup, rendererLocks, sortingGroupLocks, spriteMaskLocks);
                    if (collider) collider.enabled = true;
                }
                yield break;
            }

            #if UNITY_EDITOR
            Debug.Log($"[SERVE] Sliding cup to {customer.name}'s pickup point", cup);
            #endif
            Vector3 targetPos = pickupPoint.position;
            float elapsedTime = 0f;
            while (elapsedTime < cupSlideDuration && Vector3.Distance(cup.transform.position, targetPos) > 0.01f)
            {
                elapsedTime += Time.deltaTime;
                cup.transform.position = Vector3.MoveTowards(cup.transform.position, targetPos, cupSlideSpeed * Time.deltaTime);
                ApplyServeLayerLock(rendererLocks, sortingGroupLocks, "slide");
                ApplySpriteMaskLock(spriteMaskLocks, "slide");
                yield return null;
            }

            cup.transform.position = targetPos;
            #if UNITY_EDITOR
            Debug.Log($"[SERVE] Cup reached customer pickup point", cup);
            #endif
            ApplyServeLayerLock(rendererLocks, sortingGroupLocks, "post-slide");
            ApplySpriteMaskLock(spriteMaskLocks, "post-slide");

            if (destroyCupAfterFade)
            {
                cup.transform.SetParent(customer.transform);
                ApplyServeLayerLock(rendererLocks, sortingGroupLocks, "success-pre-fade");
                ApplySpriteMaskLock(spriteMaskLocks, "success-pre-fade");
                #if UNITY_EDITOR
                Debug.Log("[SERVE][FADE] Starting fade-down for successful serve", cup);
                #endif
                yield return FadeCup(cupRenderers, cup.transform, fadeDown: true, originalScale);
                #if UNITY_EDITOR
                Debug.Log("[SERVE][FADE] Fade-down complete for successful serve", cup);
                Debug.Log("[SERVE] Cup delivered to customer, destroying cup now", cup);
                #endif
                Destroy(cup);
                OnServeSucceeded?.Invoke();

                if (spawnReplacementCup)
                    SpawnReplacementCup();
            }
            else
            {
                cup.transform.SetParent(customer.transform);
                ApplyServeLayerLock(rendererLocks, sortingGroupLocks, "fail-pre-fade");
                ApplySpriteMaskLock(spriteMaskLocks, "fail-pre-fade");
                #if UNITY_EDITOR
                Debug.Log("[SERVE][FADE] Starting fade-down for incorrect serve", cup);
                #endif
                yield return FadeCup(cupRenderers, cup.transform, fadeDown: true, originalScale);
                #if UNITY_EDITOR
                Debug.Log("[SERVE][FADE] Fade-down complete for incorrect serve", cup);
                #endif

                cup.transform.SetParent(null, worldPositionStays: true);
                #if UNITY_EDITOR
                Debug.Log("[SERVE] Incorrect recipe served; setting cup back on counter.", cup);
                #endif
                RestoreOriginalSorting(cup, rendererLocks, sortingGroupLocks, spriteMaskLocks);
                if (collider) collider.enabled = true;
                #if UNITY_EDITOR
                Debug.Log("[SERVE][FADE] Starting fade-up as cup is returned to counter", cup);
                #endif
                yield return FadeCup(cupRenderers, cup.transform, fadeDown: false, originalScale);
                #if UNITY_EDITOR
                Debug.Log("[SERVE][FADE] Fade-up complete, cup restored on counter", cup);
                #endif
            }
        }
        finally
        {
            cupsBeingServed.Remove(cupId);
            var snapLock = cup.GetComponent<CupSnapper>();
            snapLock?.SetServeCoordinatorLock(false);

            var sortingToggle = cup.GetComponentInParent<CupSortingGroupToggle>();
            sortingToggle?.ExitServeLock();
        }
    }

    private RendererSortingSnapshot[] CaptureRendererSortingData(GameObject cup)
    {
        var renderers = cup.GetComponentsInChildren<SpriteRenderer>(includeInactive: false);
        var snapshots = new RendererSortingSnapshot[renderers.Length];
        #if UNITY_EDITOR
        Debug.Log($"[SERVE][LOCK] Capturing {snapshots.Length} SpriteRenderers for '{cup.name}'", cup);
        #endif

        for (int i = 0; i < renderers.Length; i++)
        {
            var sr = renderers[i];
            snapshots[i] = new RendererSortingSnapshot
            {
                Renderer = sr,
                OriginalLayer = sr != null ? sr.sortingLayerID : 0,
                OriginalOrder = sr != null ? sr.sortingOrder : 0,
                LockedLayer = sr != null ? sr.sortingLayerID : 0,
                LockedOrder = sr != null ? sr.sortingOrder + ServeSortingBoost : 0
            };

            if (sr != null)
            {
                sr.sortingOrder = snapshots[i].LockedOrder;
                sr.sortingLayerID = snapshots[i].LockedLayer;
                #if UNITY_EDITOR
                Debug.Log($"[SERVE][LOCK][SR] '{sr.name}' order {snapshots[i].OriginalOrder} -> {snapshots[i].LockedOrder} | layer {SortingLayer.IDToName(snapshots[i].OriginalLayer)}", sr);
                #endif
            }
        }

        return snapshots;
    }

    private SortingGroupSnapshot[] CaptureSortingGroupData(GameObject cup)
    {
        var groups = cup.GetComponentsInChildren<SortingGroup>(includeInactive: true);
        var snapshots = new SortingGroupSnapshot[groups.Length];
        #if UNITY_EDITOR
        Debug.Log($"[SERVE][LOCK] Capturing {snapshots.Length} SortingGroups for '{cup.name}'", cup);
        #endif

        for (int i = 0; i < groups.Length; i++)
        {
            var sg = groups[i];
            snapshots[i] = new SortingGroupSnapshot
            {
                Group = sg,
                OriginalLayer = sg != null ? sg.sortingLayerID : 0,
                OriginalOrder = sg != null ? sg.sortingOrder : 0,
                OriginalEnabled = sg != null && sg.enabled,
                LockedLayer = sg != null ? sg.sortingLayerID : 0,
                LockedOrder = sg != null ? sg.sortingOrder + ServeSortingBoost : 0,
                LockedEnabled = sg != null && sg.enabled
            };

            if (sg != null)
            {
                sg.sortingOrder = snapshots[i].LockedOrder;
                #if UNITY_EDITOR
                Debug.Log($"[SERVE][LOCK][SG] '{sg.name}' order {snapshots[i].OriginalOrder} -> {snapshots[i].LockedOrder} | layer {SortingLayer.IDToName(snapshots[i].OriginalLayer)} | enabled={snapshots[i].OriginalEnabled}", sg);
                #endif
            }
        }

        return snapshots;
    }

    private SpriteMaskSnapshot[] CaptureSpriteMaskData(GameObject cup)
    {
        var masks = cup.GetComponentsInChildren<SpriteMask>(includeInactive: true);
        var snapshots = new SpriteMaskSnapshot[masks.Length];
        #if UNITY_EDITOR
        Debug.Log($"[SERVE][LOCK] Capturing {snapshots.Length} SpriteMasks for '{cup.name}'", cup);
        #endif

        for (int i = 0; i < masks.Length; i++)
        {
            var mask = masks[i];
            snapshots[i] = new SpriteMaskSnapshot
            {
                Mask = mask,
                OriginalFrontLayer = mask != null ? mask.frontSortingLayerID : 0,
                OriginalBackLayer = mask != null ? mask.backSortingLayerID : 0,
                OriginalFrontOrder = mask != null ? mask.frontSortingOrder : 0,
                OriginalBackOrder = mask != null ? mask.backSortingOrder : 0,
                LockedFrontLayer = mask != null ? mask.frontSortingLayerID : 0,
                LockedBackLayer = mask != null ? mask.backSortingLayerID : 0,
                LockedFrontOrder = mask != null ? mask.frontSortingOrder + ServeSortingBoost : 0,
                LockedBackOrder = mask != null ? mask.backSortingOrder + ServeSortingBoost : 0
            };

            if (mask != null)
            {
                mask.frontSortingOrder = snapshots[i].LockedFrontOrder;
                mask.backSortingOrder = snapshots[i].LockedBackOrder;
                mask.frontSortingLayerID = snapshots[i].LockedFrontLayer;
                mask.backSortingLayerID = snapshots[i].LockedBackLayer;
                #if UNITY_EDITOR
                Debug.Log($"[SERVE][LOCK][MASK] '{mask.name}' front {snapshots[i].OriginalFrontOrder}->{snapshots[i].LockedFrontOrder} | back {snapshots[i].OriginalBackOrder}->{snapshots[i].LockedBackOrder}", mask);
                #endif
            }
        }

        return snapshots;
    }

    private static SpriteRenderer[] ExtractRenderers(RendererSortingSnapshot[] snapshots)
    {
        if (snapshots == null || snapshots.Length == 0) return System.Array.Empty<SpriteRenderer>();

        var renderers = new SpriteRenderer[snapshots.Length];
        for (int i = 0; i < snapshots.Length; i++)
            renderers[i] = snapshots[i].Renderer;
        return renderers;
    }

    private void ApplyServeLayerLock(RendererSortingSnapshot[] renderers, SortingGroupSnapshot[] groups, string stageTag)
    {
        if (renderers != null)
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                var snapshot = renderers[i];
                var sr = snapshot.Renderer;
                if (sr == null) continue;

                if (sr.sortingOrder != snapshot.LockedOrder)
                {
                    #if UNITY_EDITOR
                    Debug.Log($"[SERVE][LOCK][SR] {stageTag} order drift on '{sr.name}': {sr.sortingOrder} -> {snapshot.LockedOrder}", sr);
                    #endif
                    sr.sortingOrder = snapshot.LockedOrder;
                }
                if (sr.sortingLayerID != snapshot.LockedLayer)
                {
                    #if UNITY_EDITOR
                    Debug.Log($"[SERVE][LOCK][SR] {stageTag} layer drift on '{sr.name}': {SortingLayer.IDToName(sr.sortingLayerID)} -> {SortingLayer.IDToName(snapshot.LockedLayer)}", sr);
                    #endif
                    sr.sortingLayerID = snapshot.LockedLayer;
                }
            }
        }

        if (groups != null)
        {
            for (int i = 0; i < groups.Length; i++)
            {
                var snapshot = groups[i];
                var sg = snapshot.Group;
                if (sg == null) continue;

                if (sg.sortingOrder != snapshot.LockedOrder)
                {
                    #if UNITY_EDITOR
                    Debug.Log($"[SERVE][LOCK][SG] {stageTag} order drift on '{sg.name}': {sg.sortingOrder} -> {snapshot.LockedOrder}", sg);
                    #endif
                    sg.sortingOrder = snapshot.LockedOrder;
                }
                if (sg.sortingLayerID != snapshot.LockedLayer)
                {
                    #if UNITY_EDITOR
                    Debug.Log($"[SERVE][LOCK][SG] {stageTag} layer drift on '{sg.name}': {SortingLayer.IDToName(sg.sortingLayerID)} -> {SortingLayer.IDToName(snapshot.LockedLayer)}", sg);
                    #endif
                    sg.sortingLayerID = snapshot.LockedLayer;
                }
                if (sg.enabled != snapshot.LockedEnabled)
                {
                    #if UNITY_EDITOR
                    Debug.Log($"[SERVE][LOCK][SG] {stageTag} enabled drift on '{sg.name}': {sg.enabled} -> {snapshot.LockedEnabled}", sg);
                    #endif
                    sg.enabled = snapshot.LockedEnabled;
                }
            }
        }
    }

    private void ApplySpriteMaskLock(SpriteMaskSnapshot[] masks, string stageTag)
    {
        if (masks == null) return;

        for (int i = 0; i < masks.Length; i++)
        {
            var snapshot = masks[i];
            var mask = snapshot.Mask;
            if (mask == null) continue;

            if (mask.frontSortingOrder != snapshot.LockedFrontOrder)
            {
                #if UNITY_EDITOR
                Debug.Log($"[SERVE][LOCK][MASK] {stageTag} front order drift on '{mask.name}': {mask.frontSortingOrder} -> {snapshot.LockedFrontOrder}", mask);
                #endif
                mask.frontSortingOrder = snapshot.LockedFrontOrder;
            }
            if (mask.backSortingOrder != snapshot.LockedBackOrder)
            {
                #if UNITY_EDITOR
                Debug.Log($"[SERVE][LOCK][MASK] {stageTag} back order drift on '{mask.name}': {mask.backSortingOrder} -> {snapshot.LockedBackOrder}", mask);
                #endif
                mask.backSortingOrder = snapshot.LockedBackOrder;
            }
            if (mask.frontSortingLayerID != snapshot.LockedFrontLayer)
            {
                #if UNITY_EDITOR
                Debug.Log($"[SERVE][LOCK][MASK] {stageTag} front layer drift on '{mask.name}'", mask);
                #endif
                mask.frontSortingLayerID = snapshot.LockedFrontLayer;
            }
            if (mask.backSortingLayerID != snapshot.LockedBackLayer)
            {
                #if UNITY_EDITOR
                Debug.Log($"[SERVE][LOCK][MASK] {stageTag} back layer drift on '{mask.name}'", mask);
                #endif
                mask.backSortingLayerID = snapshot.LockedBackLayer;
            }
        }
    }

    private void RestoreOriginalSorting(GameObject cup, RendererSortingSnapshot[] renderers, SortingGroupSnapshot[] groups, SpriteMaskSnapshot[] masks)
    {
        if (renderers != null)
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                var snapshot = renderers[i];
                var sr = snapshot.Renderer;
                if (sr == null) continue;
                sr.sortingLayerID = snapshot.OriginalLayer;
                sr.sortingOrder = snapshot.OriginalOrder;
            }
        }

        if (groups != null)
        {
            for (int i = 0; i < groups.Length; i++)
            {
                var snapshot = groups[i];
                var sg = snapshot.Group;
                if (sg == null) continue;
                sg.sortingLayerID = snapshot.OriginalLayer;
                sg.sortingOrder = snapshot.OriginalOrder;
                sg.enabled = snapshot.OriginalEnabled;
            }
        }

        if (masks != null)
        {
            for (int i = 0; i < masks.Length; i++)
            {
                var snapshot = masks[i];
                var mask = snapshot.Mask;
                if (mask == null) continue;
                mask.frontSortingLayerID = snapshot.OriginalFrontLayer;
                mask.backSortingLayerID = snapshot.OriginalBackLayer;
                mask.frontSortingOrder = snapshot.OriginalFrontOrder;
                mask.backSortingOrder = snapshot.OriginalBackOrder;
            }
        }

        #if UNITY_EDITOR
        Debug.Log("[SERVE][LOCK] Restored original sorting layers/orders after serve.", cup);
        #endif
    }

    private void SpawnReplacementCup()
    {
        if (cupDispenser == null)
        {
            #if UNITY_EDITOR
            Debug.LogWarning("[SERVE] No CupDispenser assigned — cannot spawn new cup.", this);
            #endif
            return;
        }

        var newCup = cupDispenser.SpawnFreshCup();
        if (newCup != null)
        {
            #if UNITY_EDITOR
            Debug.Log("[SERVE] Spawned fresh cup.", newCup);
            #endif
        }
    }

    private static bool IsCustomerReadyToServe(Customer customer)
    {
        if (customer == null)
            return false;

        return customer.IsAtCounter() && customer.CanOrder() && !customer.hasReceivedOrder;
    }
}

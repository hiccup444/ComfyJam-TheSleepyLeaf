using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class CupState : MonoBehaviour
{
    /// <summary>
    /// Fired when any topping is added. Provides the topping type.
    /// </summary>
    public static event Action<string> OnToppingAdded;

    [Header("Fill State")]
    [SerializeField, Range(0f, 1f)] private float minFillNormalized = 0.75f;
    [SerializeField, Range(0f, 1f)] private float fillNormalized = 0f;
    [SerializeField] private bool filled = false;
    [SerializeField] private List<string> toppings = new List<string>();

    [Header("Optional Visual")]
    [SerializeField] private GameObject liquidVisual; // enable/disable on fill
    
    [Header("Lemon Visuals")]
    [Tooltip("Lemon visual GameObjects - one will be randomly enabled when lemon topping is added")]
    [SerializeField] private GameObject[] lemonVisuals;
    
    [Header("Mint Visual")]
    [Tooltip("Mint visual GameObject - enabled when mint topping is added (requires liquid)")]
    [SerializeField] private GameObject mintVisual;
    
    [Header("Rose Petals Visuals")]
    [Tooltip("Rose petals visual GameObjects - one will be randomly enabled when rose topping is added (requires liquid)")]
    [SerializeField] private GameObject[] rosePetalsVisuals;

    /// <summary>
    /// Current normalized fill value (0..1).
    /// </summary>
    public float FillNormalized => fillNormalized;

    /// <summary>
    /// Minimum normalized fill required for <see cref="IsFilled"/> to report true.
    /// </summary>
    public float MinFillNormalized => minFillNormalized;

    /// <summary>
    /// Returns true when the cup meets or exceeds the minimum fill requirement.
    /// </summary>
    public bool IsFilled => filled;

    /// <summary>
    /// Current toppings applied to this cup.
    /// </summary>
    public IReadOnlyList<string> Toppings => toppings;

    /// <summary>
    /// Clamp and store the fill amount, updating the filled flag/visuals.
    /// </summary>
    /// <param name="normalizedAmount">Normalized fill amount (0..1).</param>
    public void SetFillAmount(float normalizedAmount)
    {
        fillNormalized = Mathf.Clamp01(normalizedAmount);
        filled = fillNormalized >= minFillNormalized;

        if (liquidVisual)
            liquidVisual.SetActive(fillNormalized > 0f);
    }

    /// <summary>
    /// Convenience for additive style pours.
    /// </summary>
    public void AddFillAmount(float normalizedDelta)
    {
        SetFillAmount(fillNormalized + normalizedDelta);
    }

    /// <summary>
    /// Update the minimum fill requirement and refresh current state.
    /// </summary>
    public void SetMinimumFillRequirement(float normalizedAmount)
    {
        minFillNormalized = Mathf.Clamp01(normalizedAmount);
        SetFillAmount(fillNormalized);
    }

    /// <summary>
    /// Backwards-compatible API; true -> full, false -> empty.
    /// </summary>
    public void SetFilled(bool v)
    {
        SetFillAmount(v ? 1f : 0f);
    }

    /// <summary>
    /// Add a topping identifier to the cup (duplicates ignored, case-insensitive).
    /// Returns true if the topping was successfully added, false if rejected.
    /// </summary>
    public bool AddTopping(string toppingType)
    {
        if (string.IsNullOrWhiteSpace(toppingType))
            return false;

        string normalized = toppingType.Trim();

        // Check for duplicates
        for (int i = 0; i < toppings.Count; i++)
        {
            if (string.Equals(toppings[i], normalized, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        // Mint and Rose require actual liquid in the cup (check MugBeverageState)
        bool requiresLiquid = string.Equals(normalized, "Mint", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(normalized, "Rose", StringComparison.OrdinalIgnoreCase);

        if (requiresLiquid)
        {
            // Check MugBeverageState for actual liquid volume
            var beverageState = GetComponent<MugBeverageState>();
            if (beverageState == null)
                beverageState = GetComponentInChildren<MugBeverageState>();

            bool hasLiquid = beverageState != null && !beverageState.IsEmpty();

            if (!hasLiquid)
            {
                #if UNITY_EDITOR
                Debug.Log($"[TOPPING] {normalized} requires liquid in the cup first!", this);
                #endif
                return false;
            }
        }

        toppings.Add(normalized);

        // Fire topping added event
        #if UNITY_EDITOR
        Debug.Log($"[CupState] Topping '{normalized}' successfully added - firing OnToppingAdded event");
        #endif
        OnToppingAdded?.Invoke(normalized);

        // Handle lemon visual activation
        if (string.Equals(normalized, "Lemon", StringComparison.OrdinalIgnoreCase))
        {
            EnableRandomLemonVisual();
        }
        // Handle mint visual activation
        else if (string.Equals(normalized, "Mint", StringComparison.OrdinalIgnoreCase))
        {
            if (mintVisual != null)
                mintVisual.SetActive(true);
        }
        // Handle rose petals visual activation
        else if (string.Equals(normalized, "Rose", StringComparison.OrdinalIgnoreCase))
        {
            EnableRandomRosePetalsVisual();
        }

        return true;
    }

    /// <summary>
    /// Disable all lemon visuals, then enable one at random.
    /// </summary>
    private void EnableRandomLemonVisual()
    {
        if (lemonVisuals == null || lemonVisuals.Length == 0)
            return;

        // Disable all first
        for (int i = 0; i < lemonVisuals.Length; i++)
        {
            if (lemonVisuals[i] != null)
                lemonVisuals[i].SetActive(false);
        }

        // Enable one at random
        int randomIndex = UnityEngine.Random.Range(0, lemonVisuals.Length);
        if (lemonVisuals[randomIndex] != null)
            lemonVisuals[randomIndex].SetActive(true);
    }

    /// <summary>
    /// Disable all rose petals visuals, then enable one at random.
    /// </summary>
    private void EnableRandomRosePetalsVisual()
    {
        if (rosePetalsVisuals == null || rosePetalsVisuals.Length == 0)
            return;

        // Disable all first
        for (int i = 0; i < rosePetalsVisuals.Length; i++)
        {
            if (rosePetalsVisuals[i] != null)
                rosePetalsVisuals[i].SetActive(false);
        }

        // Enable one at random
        int randomIndex = UnityEngine.Random.Range(0, rosePetalsVisuals.Length);
        if (rosePetalsVisuals[randomIndex] != null)
            rosePetalsVisuals[randomIndex].SetActive(true);
    }

    /// <summary>
    /// Clear all applied toppings and hide all topping visuals.
    /// </summary>
    public void ClearToppings()
    {
        toppings.Clear();
        ClearAllToppingVisuals();
    }

    /// <summary>
    /// Hide all topping visuals (lemon, mint, rose petals).
    /// </summary>
    private void ClearAllToppingVisuals()
    {
        // Hide all lemon visuals
        if (lemonVisuals != null)
        {
            for (int i = 0; i < lemonVisuals.Length; i++)
            {
                if (lemonVisuals[i] != null)
                    lemonVisuals[i].SetActive(false);
            }
        }

        // Hide mint visual
        if (mintVisual != null)
            mintVisual.SetActive(false);

        // Hide all rose petals visuals
        if (rosePetalsVisuals != null)
        {
            for (int i = 0; i < rosePetalsVisuals.Length; i++)
            {
                if (rosePetalsVisuals[i] != null)
                    rosePetalsVisuals[i].SetActive(false);
            }
        }
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        minFillNormalized = Mathf.Clamp01(minFillNormalized);
        SetFillAmount(fillNormalized);
    }
#endif
}
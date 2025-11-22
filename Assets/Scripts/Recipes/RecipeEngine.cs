using System;
using System.Collections.Generic;
using UnityEngine;

public enum RecipeGrade
{
    Fail,
    Good,
    Perfect
}

public interface IRecipeEngine
{
    void SetActiveRecipe(RecipeSO recipe);
    IReadOnlyList<ChecklistItem> GetChecklist(ICupState cup);
    (float score, RecipeGrade grade, List<string> hints) Validate(ICupState cup);
}

public sealed class RecipeEngine : IRecipeEngine
{
    private const int DefaultRequiredDips = 3;

    private readonly List<ChecklistItem> _checklistBuffer = new List<ChecklistItem>();
    private RecipeSO _activeRecipe;

    public void SetActiveRecipe(RecipeSO recipe)
    {
        _activeRecipe = recipe;
    }

    public IReadOnlyList<ChecklistItem> GetChecklist(ICupState cup)
    {
        _checklistBuffer.Clear();
        if (_activeRecipe == null || cup == null)
            return _checklistBuffer;

        AddTeaTypeItem(cup);  // NEW: Check tea type first
        AddFillItem(cup);
        AddWaterItem(cup);
        AddDipsItem(cup);     // skipped for powder recipes (e.g., Hot Chocolate)
        AddMilkItem(cup);
        AddSugarItem(cup);
        AddIceItem(cup);
        AddPowderItem(cup);
        AddToppings(cup);

        int requiredCount = 0;
        foreach (var item in _checklistBuffer)
        {
            if (item.Required)
                requiredCount++;
        }

        #if UNITY_EDITOR
        Debug.Log($"[RECIPE] Checklist: {_checklistBuffer.Count} items (required={requiredCount})");
        #endif
        return _checklistBuffer;
    }

    public (float score, RecipeGrade grade, List<string> hints) Validate(ICupState cup)
    {
        var hints = new List<string>();
        if (_activeRecipe == null || cup == null)
            return (0f, RecipeGrade.Fail, hints);

        var checklist = GetChecklist(cup);
        if (checklist.Count == 0)
            return (1f, RecipeGrade.Perfect, hints);

        int requiredCount = 0;
        int completeCount = 0;
        bool hasHardFailure = false;
        bool hasSoftIssue = false;

        foreach (var item in checklist)
        {
            if (item.Required) requiredCount++;
            if (!item.Required) continue;

            switch (item.Kind)
            {
                case ChecklistKind.Dips:
                    EvaluateDips(cup.Dips, ref completeCount, ref hasHardFailure, ref hasSoftIssue, hints);
                    break;

                default:
                    if (item.Complete)
                    {
                        completeCount++;
                    }
                    else
                    {
                        hasHardFailure = true;
                        var hint = GetHintFor(item);
                        hints.Add(hint);
                        #if UNITY_EDITOR
                        Debug.Log($"[RECIPE] Missing {item.Kind} ({hint})");
                        #endif
                    }
                    break;
            }
        }

        if (requiredCount == 0)
            return (1f, RecipeGrade.Perfect, hints);

        float score = Mathf.Clamp01(requiredCount == 0 ? 1f : (float)completeCount / requiredCount);
        RecipeGrade grade =
            hasHardFailure ? RecipeGrade.Fail :
            hasSoftIssue   ? RecipeGrade.Good :
            (completeCount == requiredCount ? RecipeGrade.Perfect : RecipeGrade.Good);

        #if UNITY_EDITOR
        Debug.Log($"[RECIPE] Validate -> score={score:F2} grade={grade} hardFails={hasHardFailure} softIssues={hasSoftIssue}");
        #endif
        return (score, grade, hints);
    }

    // -------------------- Checklist builders --------------------

    private void AddTeaTypeItem(ICupState cup)
    {
        // Only validate tea type if the recipe requires a specific tea
        if (_activeRecipe.tea == null)
            return;
        
        TeaType requiredTea = _activeRecipe.tea.teaType;
        
        // Skip validation if recipe doesn't specify a tea type
        if (requiredTea == TeaType.None)
            return;
        
        bool matches = cup.TeaType == requiredTea;
        _checklistBuffer.Add(new ChecklistItem(
            ChecklistKind.TeaType, requiredTea.ToString(),
            (float)requiredTea, (float)cup.TeaType,
            true, matches));

        if (!matches)
        {
            #if UNITY_EDITOR
            Debug.Log($"[RECIPE] Tea type mismatch: expected {requiredTea}, got {cup.TeaType}");
            #endif
        }
    }

    private void AddFillItem(ICupState cup)
    {
        if (_activeRecipe.requiresFull)
        {
            bool complete = cup.IsFull;
            _checklistBuffer.Add(new ChecklistItem(
                ChecklistKind.Fill, null,
                1f, complete ? 1f : 0f,
                true, complete));
        }
    }

    private void AddWaterItem(ICupState cup)
    {
        if (_activeRecipe.requiredWater == WaterSource.None)
            return;

        bool matches = cup.Water == _activeRecipe.requiredWater;
        _checklistBuffer.Add(new ChecklistItem(
            ChecklistKind.Water, null,
            (float)_activeRecipe.requiredWater, (float)cup.Water,
            true, matches));
    }

    private void AddDipsItem(ICupState cup)
    {
        // Skip dips for powder-based recipes (e.g., Hot Chocolate) or when no tea is defined.
        if (_activeRecipe.tea == null) return;
        if (_activeRecipe.tea.powderRequired) return;

        // Using a constant for now; if you later add per-tea dip counts, plug it in here.
        int requiredDips = DefaultRequiredDips;

        bool isComplete = cup.Dips >= requiredDips;
        _checklistBuffer.Add(new ChecklistItem(
            ChecklistKind.Dips, null,
            requiredDips, cup.Dips,
            true, isComplete));
    }

    private void AddMilkItem(ICupState cup)
    {
        bool milkRequired = (_activeRecipe.milkRequiredOverride || (_activeRecipe.tea != null && _activeRecipe.tea.milkRequired));

        if (!milkRequired && cup.Milk == MilkKind.None)
            return;

        bool allowed = IsMilkAllowed(cup.Milk);
        bool complete = milkRequired ? (cup.Milk != MilkKind.None && allowed) : allowed;

        _checklistBuffer.Add(new ChecklistItem(
            ChecklistKind.Milk, null,
            1f, cup.Milk == MilkKind.None ? 0f : 1f,
            milkRequired, complete));
    }

    private void AddSugarItem(ICupState cup)
    {
        if (_activeRecipe.sugarRequired)
        {
            bool complete = cup.HasSugar;
            _checklistBuffer.Add(new ChecklistItem(
                ChecklistKind.Sugar, null,
                1f, complete ? 1f : 0f,
                true, complete));
        }
    }

    private void AddIceItem(ICupState cup)
    {
        if (_activeRecipe.iceRequired)
        {
            bool complete = cup.HasIce;
            _checklistBuffer.Add(new ChecklistItem(
                ChecklistKind.Ice, null,
                1f, complete ? 1f : 0f,
                true, complete));
        }
    }

    private void AddPowderItem(ICupState cup)
    {
        // If the active tea requires powder (e.g., Hot Chocolate), require HasPowder on the cup.
        if (_activeRecipe.tea != null && _activeRecipe.tea.powderRequired)
        {
            bool complete = cup.HasPowder;
            _checklistBuffer.Add(new ChecklistItem(
                ChecklistKind.Powder, null,
                1f, complete ? 1f : 0f,
                true, complete));
        }
    }

    private void AddToppings(ICupState cup)
    {
        if (_activeRecipe.requiredToppings == null)
            return;

        var toppings = cup.Toppings ?? Array.Empty<string>();
        foreach (var topping in _activeRecipe.requiredToppings)
        {
            if (string.IsNullOrEmpty(topping))
                continue;

            bool hasTopping = Contains(toppings, topping);
            _checklistBuffer.Add(new ChecklistItem(
                ChecklistKind.Topping, topping,
                1f, hasTopping ? 1f : 0f,
                true, hasTopping));
        }
    }

    // -------------------- Validation helpers --------------------

    private void EvaluateDips(
        int dips,
        ref int completeCount,
        ref bool hasHardFailure,
        ref bool hasSoftIssue,
        List<string> hints)
    {
        // Use constant requirement (default) — avoids relying on ChecklistItem internals.
        int requiredDips = DefaultRequiredDips;

        if (dips < requiredDips)
        {
            hasHardFailure = true;
            hints.Add("Tea was under-steeped.");
            #if UNITY_EDITOR
            Debug.Log("[RECIPE] Missing Dips (Tea was under-steeped.)");
            #endif
        }
        else
        {
            completeCount++;
            if (dips > requiredDips)
            {
                hasSoftIssue = true;
                hints.Add("Tea was steeped for too long.");
            }
        }
    }

    private static bool Contains(IEnumerable<string> source, string value)
    {
        foreach (var item in source)
        {
            if (string.Equals(item, value, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private bool IsMilkAllowed(MilkKind milk)
    {
        if (milk == MilkKind.None)
            return !_activeRecipe.milkRequiredOverride && !(_activeRecipe.tea != null && _activeRecipe.tea.milkRequired);

        if (_activeRecipe.milkAllowedKinds == null || _activeRecipe.milkAllowedKinds.Length == 0)
            return false;

        foreach (var allowed in _activeRecipe.milkAllowedKinds)
        {
            if (allowed == milk)
                return true;
        }
        return false;
    }

    private string GetHintFor(ChecklistItem item)
    {
        switch (item.Kind)
        {
            case ChecklistKind.TeaType:
                return $"Wrong tea type (expected {item.Id}).";
            case ChecklistKind.Fill:
                return "Cup needs to be filled.";
            case ChecklistKind.Water:
                return _activeRecipe.requiredWater == WaterSource.Hot ? "Requires hot water." : "Requires cold water.";
            case ChecklistKind.Dips:
                return "Tea was under-steeped.";
            case ChecklistKind.Milk:
                return "Milk requirement not met.";
            case ChecklistKind.Sugar:
                return "Sugar is missing.";
            case ChecklistKind.Ice:
                return "Ice is missing.";
            case ChecklistKind.Powder:
                return "Powder is missing.";
            case ChecklistKind.Topping:
                return $"Missing topping: {item.Id}.";
            default:
                return "Requirement not met.";
        }
    }
}
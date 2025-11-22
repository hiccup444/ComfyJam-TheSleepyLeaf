using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public sealed class CupStateAdapter : ICupState
{
    private static readonly IReadOnlyCollection<string> EmptyToppings = Array.Empty<string>();
    private static readonly FieldInfo SteepCountField =
        typeof(MugBeverageState).GetField(
            "_steepCount",
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);

    private readonly CupState _cupState;
    private readonly MugBeverageState _beverageState;
    private readonly MugIceState _iceState;

    public CupStateAdapter(GameObject cupObject)
    {
        if (cupObject == null)
            return;

        _cupState = cupObject.GetComponent<CupState>();
        _beverageState = cupObject.GetComponent<MugBeverageState>();

        // Prefer parent search in case the collider you hit is a child
        _iceState =
            cupObject.GetComponentInParent<MugIceState>() ??
            cupObject.GetComponent<MugIceState>();
    }

    public CupStateAdapter(CupState cupState, MugBeverageState beverageState)
    {
        _cupState = cupState;
        _beverageState = beverageState;

        // Try to find MugIceState from either provided component roots
        _iceState =
            (beverageState != null ? beverageState.GetComponent<MugIceState>() : null) ??
            (cupState != null ? cupState.GetComponent<MugIceState>() : null);
    }

    // Full if CupState says so OR meets its minimum fill threshold
    public bool IsFull =>
        _cupState != null &&
        (_cupState.IsFilled || _cupState.FillNormalized >= _cupState.MinFillNormalized);

    public WaterSource Water
    {
        get
        {
            if (_beverageState == null || !_beverageState.HasWater)
                return WaterSource.None;

            var temp = _beverageState.WaterTemperature;
            if (!temp.HasValue) return WaterSource.None;
            return temp.Value == WaterTemp.Hot ? WaterSource.Hot : WaterSource.Cold;
        }
    }

    public TeaType TeaType
    {
        get
        {
            if (_beverageState == null)
                return TeaType.None;
            
            return _beverageState.TeaType;
        }
    }

    public int Dips => GetSteepCount();

    public MilkKind Milk
    {
        get
        {
            if (_beverageState == null || !_beverageState.HasMilk)
                return MilkKind.None;

            // Map to whatever milk kinds your recipes allow; adjust as needed.
            return MilkKind.Dairy;
        }
    }

    public bool HasSugar => false;

    // 🔑 Now reflects MugIceState so RecipeEngine can detect ice.
    public bool HasIce => _iceState != null && _iceState.HasIce;

    // 🔑 Powder comes from MugBeverageState.
    public bool HasPowder => _beverageState != null && _beverageState.HasPowder;

    public IReadOnlyCollection<string> Toppings
    {
        get
        {
            if (_cupState != null)
            {
                var list = _cupState.Toppings;
                if (list != null && list.Count > 0)
                    return list;
            }

            return EmptyToppings;
        }
    }

    public override string ToString() => Dump();

    private int GetSteepCount()
    {
        if (_beverageState == null || SteepCountField == null)
            return 0;

        try
        {
            return (int)SteepCountField.GetValue(_beverageState);
        }
        catch
        {
            return 0;
        }
    }

    private string Dump()
    {
        var tops = Toppings == null ? "[]" : "[" + string.Join(", ", Toppings) + "]";
        return $"full={IsFull} water={Water} teaType={TeaType} dips={Dips} milk={Milk} sugar={HasSugar} ice={HasIce} powder={HasPowder} toppings={tops}";
    }
}

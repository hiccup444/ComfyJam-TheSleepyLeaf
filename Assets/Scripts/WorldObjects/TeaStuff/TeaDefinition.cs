using UnityEngine;

public enum TeaColorRule
{
    Single = 0,     // keep legacy targetColor behavior
    SwitchOnMilk,   // preMilkColor -> postMilkColor once milk is added
    SwitchOnPowder  // prePowderColor -> postPowderColor once powder is added
}

[CreateAssetMenu(fileName = "TeaDefinition", menuName = "Beverages/Tea Definition")]
public sealed class TeaDefinition : ScriptableObject
{
    [Tooltip("Type identifier used for order validation.")]
    public TeaType teaType = TeaType.None; // <- uses your existing enum

    [Header("Color")]
    [Tooltip("Legacy single target color (kept for backwards compatibility).")]
    public Color targetColor = Color.white;

    [Tooltip("How color should be chosen. 'Single' preserves current behavior.")]
    public TeaColorRule colorRule = TeaColorRule.Single;

    [Tooltip("Used when colorRule = SwitchOnMilk. Color before milk is added.")]
    public Color preMilkColor = Color.white;

    [Tooltip("Used when colorRule = SwitchOnMilk. Color after milk is added.")]
    public Color postMilkColor = Color.white;

    [Tooltip("Used when colorRule = SwitchOnPowder. Color before powder is added.")]
    public Color prePowderColor = Color.white;

    [Tooltip("Used when colorRule = SwitchOnPowder. Color after powder is added.")]
    public Color postPowderColor = Color.white;

    [Header("Requirements")]
    [Tooltip("True if the recipe requires milk (e.g., London Fog, Hot Chocolate).")]
    public bool milkRequired = false;

    [Tooltip("True if this drink requires powder (e.g., Hot Chocolate).")]
    public bool powderRequired = false;
}

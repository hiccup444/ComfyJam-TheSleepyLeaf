using UnityEngine;

[CreateAssetMenu(fileName = "Recipe", menuName = "Beverages/Recipe", order = 0)]
public sealed class RecipeSO : ScriptableObject
{
    [Header("Base Tea")]
    public TeaDefinition tea;

    [Header("Cup Requirements")]
    public WaterSource requiredWater = WaterSource.Hot;
    public bool requiresFull = true;
    public bool brewedRequired = false;

    [Header("Milk")]
    public bool milkRequiredOverride = false;
    public MilkKind[] milkAllowedKinds = new[] { MilkKind.Dairy, MilkKind.Oat };

    [Header("Extras")]
    public bool sugarRequired = false;
    public bool iceRequired = false;
    public int minIceChips = 0;
    public string[] requiredToppings = System.Array.Empty<string>();

    [Header("Presentation")]
    public string displayName;
    public Sprite icon;
}

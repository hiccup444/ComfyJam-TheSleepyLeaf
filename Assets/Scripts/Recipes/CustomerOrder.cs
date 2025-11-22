using System;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

[DisallowMultipleComponent]
[RequireComponent(typeof(Customer))]
public sealed class CustomerOrder : MonoBehaviour
{
    [SerializeField] private bool manualControl = false;

    [SerializeField]
    private RecipeRegistry recipeRegistry;

    public event Action<RecipeSO> OnOrderAssigned;
    public event Action<float, RecipeGrade, List<string>> OnValidated;

    public RecipeSO ActiveRecipe => _activeRecipe;
    public string ActiveOrderId => _activeOrderId;

    private readonly RecipeEngine _engine = new RecipeEngine();
    private Customer _customer;
    private RecipeSO _activeRecipe;
    private string _activeOrderId;

    private void Awake()
    {
        _customer = GetComponent<Customer>();
        RecipeIconCache.Init();
    }

    private void OnEnable()
    {
        if (_customer != null && !manualControl)
        {
#if UNITY_EDITOR
            Debug.Log("[ORDER] Subscribed to Customer.OnOrderPlaced", this);
#endif
            _customer.OnOrderPlaced += HandleOrderPlaced;
        }
    }

    private void OnDisable()
    {
        if (_customer != null && !manualControl)
        {
#if UNITY_EDITOR
            Debug.Log("[ORDER] Unsubscribed to Customer.OnOrderPlaced", this);
#endif
            _customer.OnOrderPlaced -= HandleOrderPlaced;
        }
    }

    public void SetRegistry(RecipeRegistry registry)
    {
        recipeRegistry = registry;
    }

    /// <summary>
    /// Manually trigger order placement (for tutorial use)
    /// </summary>
    public void ManuallyPlaceOrder(string orderId)
    {
        HandleOrderPlaced(orderId);
    }

    public IReadOnlyList<ChecklistItem> GetChecklist(ICupState cup)
    {
        if (_activeRecipe == null)
            return Array.Empty<ChecklistItem>();

        _engine.SetActiveRecipe(_activeRecipe);
        return _engine.GetChecklist(cup);
    }

    public bool TryValidate(ICupState cup, out float score, out RecipeGrade grade, out List<string> hints, string cupDump = null)
    {
        score = 0f;
        grade = RecipeGrade.Fail;
        hints = new List<string>();

        if (_activeRecipe == null || _customer == null || cup == null)
            return false;

        _engine.SetActiveRecipe(_activeRecipe);
        var result = _engine.Validate(cup);
        score = result.score;
        grade = result.grade;
        hints = result.hints ?? new List<string>();

        if (string.IsNullOrEmpty(cupDump))
        {
            cupDump = cup == null
                ? "<null>"
                : $"full={cup.IsFull} water={cup.Water} dips={cup.Dips} milk={cup.Milk} sugar={cup.HasSugar} ice={cup.HasIce} powder={cup.HasPowder} toppings=[{string.Join(", ", cup.Toppings ?? Array.Empty<string>())}]";
        }

#if UNITY_EDITOR
        Debug.Log($"[ORDER] TryValidate cup={cupDump} -> score={score:F2} grade={grade} hints=[{string.Join(", ", hints)}]", this);
#endif

        OnValidated?.Invoke(score, grade, new List<string>(hints));

        if (string.IsNullOrEmpty(_activeOrderId))
            return true;

        bool isCorrect = grade == RecipeGrade.Perfect || grade == RecipeGrade.Good;
#if UNITY_EDITOR
        Debug.Log($"[ORDER] Send result to Customer: {(isCorrect ? "CORRECT" : "INCORRECT")}", this);
#endif

        if (isCorrect)
        {
            _customer.ReceiveOrder(_activeOrderId);
        }
        else
        {
            _customer.ReceiveOrder("__INVALID__");
        }

        return true;
    }

    private void HandleOrderPlaced(string orderId)
    {
        _activeOrderId = orderId;
        _activeRecipe = null;

        if (recipeRegistry.TryGetRecipe(orderId, out var recipe))
        {
            _activeRecipe = recipe;
            _engine.SetActiveRecipe(_activeRecipe);
        }
        else
        {
            // Try exact name RecipeSO match
            RecipeSO baseRecipe = Resources.LoadAll<RecipeSO>("")
                .FirstOrDefault(r => r.name == orderId || r.displayName == orderId);

            if (baseRecipe != null)
            {
                recipeRegistry.keys.Add(orderId);
                recipeRegistry.recipes.Add(baseRecipe);
#if UNITY_EDITOR
                UnityEditor.EditorUtility.SetDirty(recipeRegistry);
#endif

                _activeRecipe = baseRecipe;
                _engine.SetActiveRecipe(_activeRecipe);

#if UNITY_EDITOR
                Debug.Log($"[ORDER] Registered missing recipe '{orderId}' to RecipeRegistry.", this);
#endif
            }
            else
            {
#if UNITY_EDITOR
                Debug.LogWarning($"[ORDER][WARN] No recipe or matching RecipeSO found for '{orderId}'", this);
#endif
            }
        }

        // ðŸ”¹ Assign order sprite to UI
        AssignOrderSprite(orderId);

        OnOrderAssigned?.Invoke(_activeRecipe);
    }

    private string ConvertOrderIdToSpriteName(string orderId)
    {
        // Recipe format varies based on tea type:
        // - Hot_Chocolate_... (no Tea word, 2nd underscore)
        // - Hot_Ginger_Green_Tea_... (two-word tea name, 4th underscore)
        // - Hot_Chamomile_Tea_... (single-word tea name, 3rd underscore)
        // Sprite format always has double underscore before ingredients
        
        // Special case: Hot Chocolate (no "Tea" word)
        if (orderId.StartsWith("Hot_Chocolate_") || orderId.StartsWith("Iced_Chocolate_"))
        {
            // Find 2nd underscore and insert after it
            int underscoreCount = 0;
            for (int i = 0; i < orderId.Length; i++)
            {
                if (orderId[i] == '_')
                {
                    underscoreCount++;
                    if (underscoreCount == 2)
                    {
                        return orderId.Insert(i + 1, "_");
                    }
                }
            }
            return orderId;
        }
        
        // Special case: Two-word tea names (Ginger Green)
        if (orderId.Contains("_Ginger_Green_"))
        {
            // Find 4th underscore and insert after it
            int underscoreCount = 0;
            for (int i = 0; i < orderId.Length; i++)
            {
                if (orderId[i] == '_')
                {
                    underscoreCount++;
                    if (underscoreCount == 4)
                    {
                        return orderId.Insert(i + 1, "_");
                    }
                }
            }
            return orderId;
        }
        
        // Default case: Single-word tea names (find 3rd underscore)
        int count = 0;
        for (int i = 0; i < orderId.Length; i++)
        {
            if (orderId[i] == '_')
            {
                count++;
                if (count == 3)
                {
                    return orderId.Insert(i + 1, "_");
                }
            }
        }

        // No ingredients, return as-is
        return orderId;
    }
    private void AssignOrderSprite(string orderId)
    {
        if (_customer == null) return;

        SpriteRenderer mainSprite = _customer.OrderSpriteRenderer;          // OrderSprite
        SpriteRenderer outlineSprite = _customer.OrderOutlineRenderer;      // OrderOutline

        if (mainSprite == null) return;

        // Convert into correct sprite sheet format if needed (double underscore logic)
        string spriteName = ConvertOrderIdToSpriteName(orderId);

        // Load from cached recipe icons
        Sprite sprite = RecipeIconCache.Get(spriteName);

        if (sprite != null)
        {
            // Apply to main
            mainSprite.sprite = sprite;
            mainSprite.enabled = true;

            // Apply to outline (if assigned in prefab)
            if (outlineSprite != null)
            {
                outlineSprite.sprite = sprite;
                outlineSprite.enabled = true;

                // Update order title hover text with display name
                OrderTitleHover titleHover = outlineSprite.GetComponent<OrderTitleHover>();
                if (titleHover != null && _activeRecipe != null)
                {
                    titleHover.SetOrderDisplayName(_activeRecipe.displayName);
                }
            }
        }
        else
        {
#if UNITY_EDITOR
            Debug.LogWarning($"[ORDER] No sprite found for '{spriteName}'");
#endif
            mainSprite.sprite = null;
            if (outlineSprite != null) outlineSprite.sprite = null;
        }

        // Set temperature sprite based on recipe water source
        if (_activeRecipe != null)
        {
#if UNITY_EDITOR
            Debug.Log($"[CustomerOrder] Calling SetOrderTemperatureSprite with requiredWater: {_activeRecipe.requiredWater}");
#endif
            _customer.SetOrderTemperatureSprite(_activeRecipe.requiredWater);
        }
        else
        {
#if UNITY_EDITOR
            Debug.LogWarning($"[CustomerOrder] Cannot set temperature sprite - _activeRecipe is null");
#endif
        }
    }
}

// Caches order sprites from all sheets in Resources/OrderSprites/
static class RecipeIconCache
{
    private static Dictionary<string, Sprite> _lookup;

    public static void Init()
    {
        if (_lookup != null) return; // Already loaded

        _lookup = new Dictionary<string, Sprite>();

        LoadSheet("OrderSprites/RecipeIcons_Sheet0");
        LoadSheet("OrderSprites/RecipeIcons_Sheet1");
        LoadSheet("OrderSprites/RecipeIcons_Sheet2");

#if UNITY_EDITOR
        Debug.Log($"[RecipeIconCache] Loaded {_lookup.Count} order sprites.");
#endif
    }

    private static void LoadSheet(string path)
    {
        Sprite[] sprites = Resources.LoadAll<Sprite>(path);
        foreach (var s in sprites)
        {
            if (!_lookup.ContainsKey(s.name))
                _lookup.Add(s.name, s);
        }
    }

    public static Sprite Get(string orderId)
    {
        if (_lookup == null) Init();
        _lookup.TryGetValue(orderId, out var sprite);
        return sprite;
    }
}
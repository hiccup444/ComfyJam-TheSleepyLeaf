using System.Collections.Generic;
using UnityEngine;
using System.Linq;

[CreateAssetMenu(fileName = "RecipeRegistry", menuName = "Beverages/Recipe Registry", order = 0)]
public sealed class RecipeRegistry : ScriptableObject
{
    [SerializeField]
    public List<string> keys = new List<string>();

    [SerializeField]
    public List<RecipeSO> recipes = new List<RecipeSO>();

    private readonly Dictionary<string, RecipeSO> _cache = new Dictionary<string, RecipeSO>();
    private bool _isCacheValid;

    public RecipeSO FindRecipeByFlexibleName(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;

        // Normalize both sides: remove spaces, underscores, hyphens, make lowercase
        string cleaned = CleanKey(id);

        // Force registry cache to rebuild
        EnsureCache();

        // Compare against cleaned keys
        foreach (var pair in _cache)
        {
            if (CleanKey(pair.Key) == cleaned)
                return pair.Value;
        }

        return null;
    }

    private string CleanKey(string s)
    {
        return new string(s.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }

    public bool TryGetRecipe(string id, out RecipeSO recipe)
    {
        EnsureCache();
        if (string.IsNullOrEmpty(id))
        {
            recipe = null;
            return false;
        }

        return _cache.TryGetValue(id, out recipe);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        _isCacheValid = false;
    }
#endif

    private void EnsureCache()
    {
        if (_isCacheValid)
            return;

        _cache.Clear();
        for (int i = 0; i < Mathf.Min(keys.Count, recipes.Count); i++)
        {
            var key = keys[i];
            var recipe = recipes[i];
            if (string.IsNullOrEmpty(key) || recipe == null)
                continue;

            _cache[key] = recipe;
        }

        _isCacheValid = true;
    }
}

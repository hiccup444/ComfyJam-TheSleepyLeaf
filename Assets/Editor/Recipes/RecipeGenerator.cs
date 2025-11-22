#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

public class RecipeGenerator : EditorWindow
{
    private string outputFolder = "Assets/Recipes/Generated";
    private string teaDefinitionsFolder = "Assets/Data/Teas";
    private bool generateAll = true;
    private TeaType selectedTeaType = TeaType.Chamomile;
    private int recipeCount = 0;
    private Dictionary<TeaType, TeaDefinition> teaDefinitionCache = new Dictionary<TeaType, TeaDefinition>();
    
    [MenuItem("Tools/Recipes/Generate Recipes")]
    static void ShowWindow()
    {
        var window = GetWindow<RecipeGenerator>("Recipe Generator");
        window.minSize = new Vector2(400, 350);
        window.Show();
    }
    
    void OnGUI()
    {
        GUILayout.Label("Recipe Generator", EditorStyles.boldLabel);
        EditorGUILayout.Space();
        
        outputFolder = EditorGUILayout.TextField("Output Folder", outputFolder);
        EditorGUILayout.HelpBox("Recipes will be saved to this folder. Will be created if it doesn't exist.", MessageType.Info);
        
        EditorGUILayout.Space();
        
        teaDefinitionsFolder = EditorGUILayout.TextField("Tea Definitions Folder", teaDefinitionsFolder);
        EditorGUILayout.HelpBox("Folder containing the TeaDefinition assets.", MessageType.Info);
        
        EditorGUILayout.Space();
        
        generateAll = EditorGUILayout.Toggle("Generate All Recipes", generateAll);
        
        if (!generateAll)
        {
            selectedTeaType = (TeaType)EditorGUILayout.EnumPopup("Tea Type", selectedTeaType);
        }
        
        EditorGUILayout.Space();
        
        if (GUILayout.Button("Generate Recipes", GUILayout.Height(30)))
        {
            GenerateRecipes();
        }
        
        if (recipeCount > 0)
        {
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox($"Last generation created {recipeCount} recipes.", MessageType.Info);
        }
    }
    
    void GenerateRecipes()
    {
        // Load all TeaDefinition assets first
        LoadTeaDefinitions();
        
        if (teaDefinitionCache.Count == 0)
        {
            EditorUtility.DisplayDialog("Error", 
                $"No TeaDefinition assets found in '{teaDefinitionsFolder}' or subfolders.\n\nPlease create TeaDefinition assets for each tea type first, or update the folder path.", 
                "OK");
            return;
        }
        
        // Create output folder if it doesn't exist
        if (!AssetDatabase.IsValidFolder(outputFolder))
        {
            string[] folders = outputFolder.Split('/');
            string currentPath = folders[0];
            
            for (int i = 1; i < folders.Length; i++)
            {
                string newPath = currentPath + "/" + folders[i];
                if (!AssetDatabase.IsValidFolder(newPath))
                {
                    AssetDatabase.CreateFolder(currentPath, folders[i]);
                }
                currentPath = newPath;
            }
        }
        
        recipeCount = 0;
        
        List<TeaType> teaTypes = generateAll 
            ? new List<TeaType> { TeaType.Chamomile, TeaType.GingerGreen, TeaType.HotChocolate, TeaType.Lavender, TeaType.LondonFog, TeaType.Peppermint }
            : new List<TeaType> { selectedTeaType };
        
        foreach (var teaType in teaTypes)
        {
            if (teaType == TeaType.None) continue;
            
            if (!teaDefinitionCache.ContainsKey(teaType))
            {
                Debug.LogWarning($"[RecipeGenerator] No TeaDefinition found for {teaType}, skipping.");
                continue;
            }
            
            GenerateRecipesForTea(teaType);
        }
        
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        
        Debug.Log($"[RecipeGenerator] Generated {recipeCount} recipes in '{outputFolder}'");
        EditorUtility.DisplayDialog("Success", $"Generated {recipeCount} recipes!", "OK");
    }
    
    void LoadTeaDefinitions()
    {
        teaDefinitionCache.Clear();
        
        // Find all TeaDefinition assets in the project
        string[] guids = AssetDatabase.FindAssets("t:TeaDefinition", new[] { teaDefinitionsFolder });
        
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            TeaDefinition teaDef = AssetDatabase.LoadAssetAtPath<TeaDefinition>(path);
            
            if (teaDef != null && teaDef.teaType != TeaType.None)
            {
                if (!teaDefinitionCache.ContainsKey(teaDef.teaType))
                {
                    teaDefinitionCache[teaDef.teaType] = teaDef;
                    Debug.Log($"[RecipeGenerator] Found TeaDefinition for {teaDef.teaType} at {path}");
                }
            }
        }
    }
    
    void GenerateRecipesForTea(TeaType teaType)
    {
        // Get the existing tea definition asset
        TeaDefinition teaDef = teaDefinitionCache[teaType];
        
        // Hot chocolate has special rules
        bool isHotChocolate = teaType == TeaType.HotChocolate;
        
        // Water temperatures (hot chocolate can only be hot)
        List<WaterSource> waterTypes = isHotChocolate 
            ? new List<WaterSource> { WaterSource.Hot }
            : new List<WaterSource> { WaterSource.Hot, WaterSource.Cold };
        
        foreach (var waterType in waterTypes)
        {
            // Ice options (ice doesn't go with hot water, or hot chocolate at all)
            List<bool> iceOptions = (waterType == WaterSource.Hot || isHotChocolate) 
                ? new List<bool> { false }
                : new List<bool> { false, true };
            
            foreach (bool hasIce in iceOptions)
            {
                // Milk options (hot chocolate always requires milk, others optional)
                List<bool> milkOptions = isHotChocolate 
                    ? new List<bool> { true }
                    : new List<bool> { false, true };
                
                foreach (bool hasMilk in milkOptions)
                {
                    // Generate all topping combinations
                    // Hot chocolate can't have lemon
                    List<string> availableToppings = isHotChocolate 
                        ? new List<string> { "Rose", "Mint" }
                        : new List<string> { "Lemon", "Rose", "Mint" };
                    
                    // Generate all possible topping combinations (including none)
                    var toppingCombinations = GetAllToppingCombinations(availableToppings);
                    
                    foreach (var toppings in toppingCombinations)
                    {
                        CreateRecipe(teaType, teaDef, waterType, hasIce, hasMilk, toppings);
                    }
                }
            }
        }
    }
    
    List<List<string>> GetAllToppingCombinations(List<string> toppings)
    {
        var combinations = new List<List<string>>();
        
        // Start with no toppings
        combinations.Add(new List<string>());
        
        // Generate all combinations (1, 2, or 3 toppings)
        int n = toppings.Count;
        
        // Single toppings
        for (int i = 0; i < n; i++)
        {
            combinations.Add(new List<string> { toppings[i] });
        }
        
        // Pairs of toppings
        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                combinations.Add(new List<string> { toppings[i], toppings[j] });
            }
        }
        
        // All three toppings (if available)
        if (n == 3)
        {
            combinations.Add(new List<string> { toppings[0], toppings[1], toppings[2] });
        }
        
        return combinations;
    }
    
    void CreateRecipe(TeaType teaType, TeaDefinition teaDef, WaterSource waterType, bool hasIce, bool hasMilk, List<string> toppings)
    {
        RecipeSO recipe = ScriptableObject.CreateInstance<RecipeSO>();
        
        // Set tea definition
        recipe.tea = teaDef;
        
        // Set water requirements
        recipe.requiredWater = waterType;
        recipe.requiresFull = true;
        recipe.brewedRequired = !teaDef.powderRequired; // teabags need brewing, powder doesn't
        
        // Set milk requirements
        // If the tea itself requires milk (like London Fog or Hot Chocolate), don't override
        // Otherwise, set milkRequiredOverride based on this specific recipe variation
        if (teaDef.milkRequired)
        {
            // Tea already requires milk, just ensure allowed kinds are set
            recipe.milkRequiredOverride = false;
            recipe.milkAllowedKinds = new MilkKind[] { MilkKind.Dairy, MilkKind.Oat };
        }
        else
        {
            // This is an optional milk variant
            recipe.milkRequiredOverride = hasMilk;
            recipe.milkAllowedKinds = hasMilk 
                ? new MilkKind[] { MilkKind.Dairy, MilkKind.Oat }
                : new MilkKind[] { };
        }
        
        // Set ice requirements
        recipe.iceRequired = hasIce;
        recipe.minIceChips = hasIce ? 1 : 0;
        
        // Set toppings
        recipe.requiredToppings = toppings.ToArray();
        
        // Generate display name
        string displayName = GenerateDisplayName(teaType, waterType, hasIce, hasMilk, toppings, teaDef);
        recipe.displayName = displayName;
        
        // Generate file name (sanitized)
        string fileName = SanitizeFileName(displayName);
        string assetPath = $"{outputFolder}/{fileName}.asset";
        
        // Check if asset already exists
        if (AssetDatabase.LoadAssetAtPath<RecipeSO>(assetPath) != null)
        {
            // Skip if already exists
            return;
        }
        
        // Create the asset
        AssetDatabase.CreateAsset(recipe, assetPath);
        recipeCount++;
    }
    
    string GenerateDisplayName(TeaType teaType, WaterSource waterType, bool hasIce, bool hasMilk, List<string> toppings, TeaDefinition teaDef)
    {
        string name = "";
        
        // Temperature (skip for Hot Chocolate since it's already in the name)
        if (teaType != TeaType.HotChocolate)
        {
            name += waterType == WaterSource.Hot ? "Hot " : "Iced ";
        }
        
        // Tea name
        name += GetTeaDisplayName(teaType);
        
        // Build modifiers list (milk + toppings)
        List<string> modifiers = new List<string>();
        
        // Milk (only add to modifiers if it's optional and added, not if tea already requires it)
        if (hasMilk && !teaDef.milkRequired)
        {
            modifiers.Add("Milk");
        }
        
        // Add toppings
        modifiers.AddRange(toppings);
        
        // Format modifiers with dash separator
        if (modifiers.Count > 0)
        {
            name += " - ";
            name += string.Join(", ", modifiers);
        }
        
        return name;
    }
    
    string GetTeaDisplayName(TeaType teaType)
    {
        switch (teaType)
        {
            case TeaType.Chamomile: return "Chamomile Tea";
            case TeaType.GingerGreen: return "Ginger Green Tea";
            case TeaType.HotChocolate: return "Hot Chocolate";
            case TeaType.Lavender: return "Lavender Tea";
            case TeaType.LondonFog: return "London Fog";
            case TeaType.Peppermint: return "Peppermint Tea";
            default: return teaType.ToString();
        }
    }
    
    string SanitizeFileName(string name)
    {
        // Remove invalid characters for file names
        string sanitized = name.Replace(" ", "_");
        sanitized = sanitized.Replace(",", "");
        sanitized = sanitized.Replace("'", "");
        return sanitized;
    }
}
#endif
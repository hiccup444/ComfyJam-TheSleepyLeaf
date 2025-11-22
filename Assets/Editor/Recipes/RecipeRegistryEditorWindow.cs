using UnityEngine;
using UnityEditor;
using System.Linq;

public class RecipeRegistryEditorWindow : EditorWindow
{
    public RecipeRegistry registry;
    public DefaultAsset recipeFolder; // Folder containing RecipeSO assets

    [MenuItem("Tools/Recipes/Recipe Registry Utility")]

    public static void ShowWindow()
    {
        GetWindow<RecipeRegistryEditorWindow>("Recipe Registry Utility");
    }

    private void OnGUI()
    {
        GUILayout.Label("Recipe Registry Utility", EditorStyles.boldLabel);

        registry = (RecipeRegistry)EditorGUILayout.ObjectField(
            "Recipe Registry", registry, typeof(RecipeRegistry), false);

        recipeFolder = (DefaultAsset)EditorGUILayout.ObjectField(
            "Recipe Folder", recipeFolder, typeof(DefaultAsset), false);

        if (registry == null || recipeFolder == null)
        {
            EditorGUILayout.HelpBox("Assign both a Recipe Registry and a Recipe folder.", MessageType.Info);
            return;
        }

        GUILayout.Space(5);

        if (GUILayout.Button("➕ Assign All Recipes to Registry", GUILayout.Height(25)))
        {
            AssignAllRecipes();
        }
    }

    private void AssignAllRecipes()
    {
        string folderPath = AssetDatabase.GetAssetPath(recipeFolder);
        string[] guids = AssetDatabase.FindAssets("t:RecipeSO", new[] { folderPath });

        int added = 0;

        foreach (string guid in guids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            RecipeSO recipe = AssetDatabase.LoadAssetAtPath<RecipeSO>(assetPath);
            if (recipe == null) continue;

            string key = recipe.name; // Use asset file name as recipe ID

            if (!registry.keys.Contains(key))
            {
                registry.keys.Add(key);
                registry.recipes.Add(recipe);
                added++;
            }
        }

        if (added > 0)
        {
            EditorUtility.SetDirty(registry);
            AssetDatabase.SaveAssets();
#if UNITY_EDITOR
            Debug.Log($"[RecipeRegistry] Added {added} new recipes to registry.");
#endif
        }
        else
        {
#if UNITY_EDITOR
            Debug.Log("[RecipeRegistry] No new recipes added. All already exist.");
#endif
        }
    }
}

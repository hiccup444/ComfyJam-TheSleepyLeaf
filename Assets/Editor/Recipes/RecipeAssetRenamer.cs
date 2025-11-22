using UnityEngine;
using UnityEditor;
using System.IO;

public class RecipeAssetRenamer : EditorWindow
{
    public DefaultAsset recipeFolder;
    private const string oldPattern = "_-_";
    private const string newPattern = "_";

    [MenuItem("Tools/Recipes/Rename Recipe Assets")]
    public static void ShowWindow()
    {
        GetWindow<RecipeAssetRenamer>("Rename RecipeSO Assets");
    }

    private void OnGUI()
    {
        GUILayout.Label("Rename RecipeSO Assets", EditorStyles.boldLabel);
        recipeFolder = (DefaultAsset)EditorGUILayout.ObjectField("Recipe Folder", recipeFolder, typeof(DefaultAsset), false);

        if (recipeFolder == null)
        {
            EditorGUILayout.HelpBox("Assign the folder that contains RecipeSO assets.", MessageType.Info);
            return;
        }

        if (GUILayout.Button("Rename All Recipes (Remove `_-_`)"))
        {
            RenameRecipeAssets();
        }
    }

    private void RenameRecipeAssets()
    {
        string path = AssetDatabase.GetAssetPath(recipeFolder);
        string[] guids = AssetDatabase.FindAssets("t:RecipeSO", new[] { path });
        int renamedCount = 0;

        foreach (string guid in guids)
        {
            string oldPath = AssetDatabase.GUIDToAssetPath(guid);
            string fileName = Path.GetFileNameWithoutExtension(oldPath);

            if (fileName.Contains(oldPattern))
            {
                string newName = fileName.Replace(oldPattern, newPattern);
                AssetDatabase.RenameAsset(oldPath, newName);
                renamedCount++;
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

#if UNITY_EDITOR
        Debug.Log($"[Recipe Renamer] Renamed {renamedCount} RecipeSO assets.");
#endif
    }
}

// Assets/Editor/CustomerOrderEditor.cs
using UnityEngine;
using UnityEditor;
using System.Linq;
using System.Collections.Generic;
using System.IO;

public class CustomerOrderEditor : EditorWindow
{
    public CustomerData customerData;
    public DefaultAsset recipeFolder;   // folder with RecipeSO
    public DefaultAsset dialogueFolder; // folder with DialogueData

    [MenuItem("Tools/Recipes/Customer Order Assigner")]
    public static void OpenWindow() => GetWindow<CustomerOrderEditor>("Customer Order Assigner");

    void OnGUI()
    {
        GUILayout.Label("Assign Preferred Orders", EditorStyles.boldLabel);

        customerData   = (CustomerData)EditorGUILayout.ObjectField("Customer Data", customerData, typeof(CustomerData), false);
        recipeFolder   = (DefaultAsset)EditorGUILayout.ObjectField("Recipe Folder", recipeFolder, typeof(DefaultAsset), false);
        dialogueFolder = (DefaultAsset)EditorGUILayout.ObjectField("Dialogue Folder", dialogueFolder, typeof(DefaultAsset), false);

        if (!Valid()) { EditorGUILayout.HelpBox("Assign a CustomerData and both folders.", MessageType.Warning); return; }

        GUILayout.Space(8);
        if (GUILayout.Button("Add All Recipes"))      Run(Filter.All);
        if (GUILayout.Button("Add All Hot Recipes"))  Run(Filter.Hot);
        if (GUILayout.Button("Add All Cold Recipes")) Run(Filter.Cold);
        if (GUILayout.Button("Add All Milk Recipes")) Run(Filter.Milk);

        GUILayout.Space(8);
        GUILayout.Label("Add All {TeaType} Recipes");
        foreach (TeaType t in System.Enum.GetValues(typeof(TeaType)))
        {
            if (t == TeaType.None) continue;
            if (GUILayout.Button($"Add All {t}")) Run(Filter.ByTeaType, t);
        }
    }

    enum Filter { All, Hot, Cold, Milk, ByTeaType }

    bool Valid() => customerData && recipeFolder && dialogueFolder;

    void Run(Filter filter, TeaType teaType = TeaType.None)
    {
        string recipePath   = AssetDatabase.GetAssetPath(recipeFolder);
        string dialoguePath = AssetDatabase.GetAssetPath(dialogueFolder);

        var recipes = LoadAll<RecipeSO>(recipePath);

        // filter
        IEnumerable<RecipeSO> q = recipes;
        switch (filter)
        {
            case Filter.Hot:  q = q.Where(r => r && r.requiredWater == WaterSource.Hot); break;
            case Filter.Cold: q = q.Where(r => r && r.requiredWater == WaterSource.Cold); break;
            case Filter.Milk:
                q = q.Where(r =>
                    r &&
                    (r.milkRequiredOverride ||
                     (r.milkAllowedKinds != null && r.milkAllowedKinds.Length > 0) ||
                     (r.tea != null && r.tea.milkRequired)));
                break;
            case Filter.ByTeaType: q = q.Where(r => r && r.tea && r.tea.teaType == teaType); break;
            case Filter.All:
            default: break;
        }

        var dialogues = LoadAll<DialogueData>(dialoguePath).ToList();
        var prefs = new List<OrderPreference>(customerData.preferredOrders ?? new OrderPreference[0]);

        int added = 0;
        foreach (var recipe in q)
        {
            if (!recipe) continue;

            // orderName must be human-readable (spaces). Prefer displayName; fallback to asset name converted.
            string orderDisplay = !string.IsNullOrEmpty(recipe.displayName)
                ? recipe.displayName
                : FromAssetNameToDisplay(recipe.name);

            // skip duplicates by display name
            if (prefs.Any(p => p != null && p.orderName == orderDisplay))
                continue;

            // match DialogueData by cleaned dialogueName vs cleaned recipe name
            string needle = Clean(recipe.name);
            string generatedNeedle = Clean($"Generated_{RemoveSpaces(orderDisplay)}");
            DialogueData dlg = dialogues.FirstOrDefault(d => d && (Clean(d.dialogueName) == needle || Clean(d.dialogueName) == generatedNeedle));

            if (dlg == null)
            {
                // create “Generated_<OrderNoSpaces>”
                string compact = RemoveSpaces(orderDisplay);
                string dlgName = $"Generated_{compact}";
                dlg = CreateDummyDialogue(dialoguePath, dlgName, orderDisplay);
                dialogues.Add(dlg);
            }


            prefs.Add(new OrderPreference
            {
                orderName = recipe.name,          // recipe ID (Hot_Lavender_Tea)
                orderDialogue = dlg               // DialogueData ("Generated_HotLavenderTea")
            });
            added++;
        }

        customerData.preferredOrders = prefs.ToArray();
        EditorUtility.SetDirty(customerData);
        AssetDatabase.SaveAssets();

#if UNITY_EDITOR
        Debug.Log($"[CustomerOrderEditor] Added {added} preferred orders to '{customerData.customerName}'.");
#endif
    }

    static string FromAssetNameToDisplay(string assetName)
    {
        // "Hot_Lavender_Tea" -> "Hot Lavender Tea"
        // "Hot-Lavender-Tea" -> "Hot Lavender Tea"
        var s = assetName.Replace('_', ' ').Replace('-', ' ');
        // collapse multiple spaces
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim();
        return s;
    }

    static string RemoveSpaces(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return new string(s.Where(char.IsLetterOrDigit).ToArray());
    }

    static string Clean(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        // strip non-alphanumerics, lowercase; used for matching “HotLavenderTea” vs “Hot_Lavender_Tea”
        return new string(s.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }

    static T[] LoadAll<T>(string folderPath) where T : Object
    {
        if (string.IsNullOrEmpty(folderPath)) return System.Array.Empty<T>();
        var guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}", new[] { folderPath });
        var list = new List<T>(guids.Length);
        foreach (var g in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(g);
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset) list.Add(asset);
        }
        return list.ToArray();
    }

    DialogueData CreateDummyDialogue(string folderPath, string dialogueAssetName, string orderDisplay)
    {
        var dlg = ScriptableObject.CreateInstance<DialogueData>();
        dlg.dialogueName = dialogueAssetName;               // e.g., "Generated_HotLavenderTea"

        // Format order display naturally (e.g., "Lavender Tea with Lemon, Milk, and Mint")
        string formattedOrder = FormatOrderNaturally(orderDisplay);

        // Randomly select a dialogue format
        string[] dialogueFormats = new[]
        {
            $"Gimme a {formattedOrder} please!",
            $"Hmm.. maybe a {formattedOrder} for today?",
            $"Havent tried {formattedOrder} yet, how about we do that?",
            $"Wait! i wasnt finished thinking.. umm.. {formattedOrder}?"
        };
        dlg.lines = new[] { dialogueFormats[Random.Range(0, dialogueFormats.Length)] };

        string fileSafe = FileSafe(dialogueAssetName);
        string target = Path.Combine(folderPath, fileSafe + ".asset").Replace("\\", "/");
        target = AssetDatabase.GenerateUniqueAssetPath(target);

        AssetDatabase.CreateAsset(dlg, target);
        EditorUtility.SetDirty(dlg);
        return dlg;
    }

    static string FormatOrderNaturally(string orderDisplay)
    {
        // Convert "Lavender Tea - Lemon, Milk, Mint" to "Lavender Tea with Lemon, Milk, and Mint"
        // or "Tea - Lemon" to "Tea with Lemon"

        if (!orderDisplay.Contains("-"))
            return orderDisplay;

        var parts = orderDisplay.Split(new[] { " - " }, System.StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
            return orderDisplay;

        string baseName = parts[0].Trim();
        string addons = parts[1].Trim();

        // Split addons by comma
        var addonList = addons.Split(new[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries)
                              .Select(s => s.Trim())
                              .ToArray();

        if (addonList.Length == 0)
            return baseName;

        if (addonList.Length == 1)
            return $"{baseName} with {addonList[0]}";

        // Multiple addons: "with X, Y, and Z"
        string lastAddon = addonList[addonList.Length - 1];
        string otherAddons = string.Join(", ", addonList.Take(addonList.Length - 1));
        return $"{baseName} with {otherAddons}, and {lastAddon}";
    }

    static string FileSafe(string s)
    {
        var bad = Path.GetInvalidFileNameChars();
        return new string(s.Select(ch => bad.Contains(ch) ? '_' : ch).ToArray());
    }
}
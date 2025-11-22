#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public class RecipeIconGenerator : EditorWindow
{
    private GameObject mugPrefab;
    private string recipesFolder = "Assets/Recipes/Generated";
    private string outputFolder = "Assets/Sprites/RecipeIcons";
    private int iconWidth = 256;
    private int iconHeight = 256;
    private int iconsPerRow = 8; // 8x8 = 64 icons per sheet, adjust as needed
    private Camera captureCamera;
    private Vector3 cameraOffset = new Vector3(0, 0, -10);
    private float orthographicSize = 2f;
    
    private bool isGenerating = false;
    private int currentIndex = 0;
    private int totalRecipes = 0;
    private List<RecipeSO> recipesToProcess = new List<RecipeSO>();
    private List<Texture2D> capturedTextures = new List<Texture2D>();
    
    [MenuItem("Tools/Recipes/Generate Recipe Icons")]
    static void ShowWindow()
    {
        var window = GetWindow<RecipeIconGenerator>("Recipe Icon Generator");
        window.minSize = new Vector2(450, 400);
        window.Show();
    }
    
    void OnGUI()
    {
        GUILayout.Label("Recipe Icon Generator", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("This tool will automatically set up the mug for each recipe, capture sprites, and combine them into sprite sheets.", MessageType.Info);
        
        EditorGUILayout.Space();
        
        mugPrefab = (GameObject)EditorGUILayout.ObjectField("Mug Prefab", mugPrefab, typeof(GameObject), false);
        EditorGUILayout.HelpBox("The mug prefab with MugController, CupState, and MugBeverageState components.", MessageType.None);
        
        EditorGUILayout.Space();
        
        recipesFolder = EditorGUILayout.TextField("Recipes Folder", recipesFolder);
        outputFolder = EditorGUILayout.TextField("Output Folder", outputFolder);
        
        EditorGUILayout.Space();
        
        EditorGUILayout.LabelField("Sprite Sheet Settings", EditorStyles.boldLabel);
        iconWidth = EditorGUILayout.IntField("Icon Width", iconWidth);
        iconHeight = EditorGUILayout.IntField("Icon Height", iconHeight);
        iconsPerRow = EditorGUILayout.IntField("Icons Per Row", iconsPerRow);
        
        int iconsPerSheet = iconsPerRow * iconsPerRow;
        EditorGUILayout.HelpBox($"Each sprite sheet will contain up to {iconsPerSheet} icons ({iconsPerRow}x{iconsPerRow} grid).", MessageType.Info);
        
        EditorGUILayout.Space();
        
        EditorGUILayout.LabelField("Capture Settings", EditorStyles.boldLabel);
        orthographicSize = EditorGUILayout.FloatField("Camera Size", orthographicSize);
        cameraOffset = EditorGUILayout.Vector3Field("Camera Offset", cameraOffset);
        
        EditorGUILayout.Space();
        
        GUI.enabled = !isGenerating;
        if (GUILayout.Button("Generate Sprite Sheets for All Recipes", GUILayout.Height(30)))
        {
            StartGeneration();
        }
        GUI.enabled = true;
        
        if (isGenerating)
        {
            EditorGUILayout.Space();
            float progress = totalRecipes > 0 ? (float)currentIndex / totalRecipes : 0f;
            EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(), progress, $"Processing {currentIndex}/{totalRecipes}");
            
            if (GUILayout.Button("Cancel"))
            {
                StopGeneration();
            }
        }
    }
    
    void StartGeneration()
    {
        if (mugPrefab == null)
        {
            EditorUtility.DisplayDialog("Error", "Please assign a mug prefab first!", "OK");
            return;
        }
        
        // Load all recipes
        recipesToProcess.Clear();
        string[] guids = AssetDatabase.FindAssets("t:RecipeSO", new[] { recipesFolder });
        
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            RecipeSO recipe = AssetDatabase.LoadAssetAtPath<RecipeSO>(path);
            if (recipe != null)
            {
                recipesToProcess.Add(recipe);
            }
        }
        
        if (recipesToProcess.Count == 0)
        {
            EditorUtility.DisplayDialog("Error", $"No recipes found in '{recipesFolder}'", "OK");
            return;
        }
        
        // Create output folder
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
        
        totalRecipes = recipesToProcess.Count;
        currentIndex = 0;
        isGenerating = true;
        
        // Set up capture camera
        SetupCaptureCamera();
        
        // Start processing
        EditorApplication.update += ProcessNextRecipe;
    }
    
    void StopGeneration()
    {
        isGenerating = false;
        EditorApplication.update -= ProcessNextRecipe;
        
        // Build sprite sheets from captured textures
        if (capturedTextures.Count > 0)
        {
            BuildSpriteSheets();
        }
        
        CleanupCaptureCamera();
        
        // Clean up captured textures
        foreach (var tex in capturedTextures)
        {
            if (tex != null)
                DestroyImmediate(tex);
        }
        capturedTextures.Clear();
        
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

#if UNITY_EDITOR
        Debug.Log($"[RecipeIconGenerator] Completed. Processed {currentIndex}/{totalRecipes} recipes.");
#endif
    }
    
    void ProcessNextRecipe()
    {
        if (!isGenerating || currentIndex >= recipesToProcess.Count)
        {
            StopGeneration();
            
            int totalSheets = Mathf.CeilToInt((float)capturedTextures.Count / (iconsPerRow * iconsPerRow));
            EditorUtility.DisplayDialog("Complete", 
                $"Generated {capturedTextures.Count} recipe icons in {totalSheets} sprite sheet(s)!", 
                "OK");
            return;
        }
        
        RecipeSO recipe = recipesToProcess[currentIndex];
        
        try
        {
            CaptureRecipeIcon(recipe);
        }
        catch (System.Exception e)
        {
#if UNITY_EDITOR
            Debug.LogError($"[RecipeIconGenerator] Error processing '{recipe.displayName}': {e.Message}");
#endif
        }
        
        currentIndex++;
        Repaint();
    }
    
    void SetupCaptureCamera()
    {
        // Create a temporary camera for capturing
        GameObject camObj = new GameObject("_CaptureCamera");
        captureCamera = camObj.AddComponent<Camera>();
        captureCamera.orthographic = true;
        captureCamera.orthographicSize = orthographicSize;
        captureCamera.clearFlags = CameraClearFlags.SolidColor;
        captureCamera.backgroundColor = new Color(0, 0, 0, 0); // transparent
        captureCamera.cullingMask = LayerMask.GetMask("Default");
        captureCamera.transform.position = cameraOffset;
    }
    
    void CleanupCaptureCamera()
    {
        if (captureCamera != null)
        {
            DestroyImmediate(captureCamera.gameObject);
            captureCamera = null;
        }
    }
    
    void CaptureRecipeIcon(RecipeSO recipe)
    {
        // Instantiate mug in scene
        GameObject mugInstance = (GameObject)PrefabUtility.InstantiatePrefab(mugPrefab);
        mugInstance.transform.position = Vector3.zero;
        
        // Set up the mug according to recipe
        SetupMugForRecipe(mugInstance, recipe);
        
        // Capture the sprite
        Texture2D texture = CaptureSprite(mugInstance);
        
        // Store texture and recipe reference
        capturedTextures.Add(texture);
        
        // Clean up mug instance
        DestroyImmediate(mugInstance);
    }
    
    void BuildSpriteSheets()
    {
        int iconsPerSheet = iconsPerRow * iconsPerRow;
        int sheetCount = Mathf.CeilToInt((float)capturedTextures.Count / iconsPerSheet);
        
        for (int sheetIndex = 0; sheetIndex < sheetCount; sheetIndex++)
        {
            int startIdx = sheetIndex * iconsPerSheet;
            int endIdx = Mathf.Min(startIdx + iconsPerSheet, capturedTextures.Count);
            int iconsInThisSheet = endIdx - startIdx;
            
            // Calculate sheet dimensions
            int sheetWidth = iconWidth * iconsPerRow;
            int sheetHeight = iconHeight * iconsPerRow;
            
            // Create sprite sheet texture
            Texture2D spriteSheet = new Texture2D(sheetWidth, sheetHeight, TextureFormat.RGBA32, false);
            
            // Fill with transparent
            Color[] clearPixels = new Color[sheetWidth * sheetHeight];
            for (int i = 0; i < clearPixels.Length; i++)
                clearPixels[i] = Color.clear;
            spriteSheet.SetPixels(clearPixels);
            
            // Copy each icon into the sheet
            for (int i = 0; i < iconsInThisSheet; i++)
            {
                int localIdx = i;
                int row = localIdx / iconsPerRow;
                int col = localIdx % iconsPerRow;
                
                // Unity textures are bottom-up, so we flip the row
                int x = col * iconWidth;
                int y = (iconsPerRow - 1 - row) * iconHeight;
                
                Texture2D iconTexture = capturedTextures[startIdx + i];
                Color[] pixels = iconTexture.GetPixels();
                spriteSheet.SetPixels(x, y, iconWidth, iconHeight, pixels);
            }
            
            spriteSheet.Apply();

            // Save sprite sheet
            string sheetPath = $"{outputFolder}/RecipeIcons_Sheet{sheetIndex}.png";
            SaveTextureAsPNG(spriteSheet, sheetPath);

#if UNITY_EDITOR
            Debug.Log($"[RecipeIconGenerator] Saved sprite sheet {sheetIndex} with {iconsInThisSheet} icons");
#endif

            DestroyImmediate(spriteSheet);
        }
        
        // After saving all sheets, configure them and assign sprites to recipes
        AssetDatabase.Refresh();
        ConfigureSpriteSheets(sheetCount);
    }
    
    void ConfigureSpriteSheets(int sheetCount)
    {
        int iconsPerSheet = iconsPerRow * iconsPerRow;
        
        for (int sheetIndex = 0; sheetIndex < sheetCount; sheetIndex++)
        {
            string sheetPath = $"{outputFolder}/RecipeIcons_Sheet{sheetIndex}.png";
            
            // Configure as sprite sheet
            TextureImporter importer = AssetImporter.GetAtPath(sheetPath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Multiple;
                importer.mipmapEnabled = false;
                importer.filterMode = FilterMode.Bilinear;
                
                // Define sprite rectangles
                int startIdx = sheetIndex * iconsPerSheet;
                int endIdx = Mathf.Min(startIdx + iconsPerSheet, recipesToProcess.Count);
                int iconsInThisSheet = endIdx - startIdx;
                
                var factory = new UnityEditor.U2D.Sprites.SpriteDataProviderFactories();
                factory.Init();
                var dataProvider = factory.GetSpriteEditorDataProviderFromObject(importer);
                
                if (dataProvider != null)
                {
                    dataProvider.InitSpriteEditorDataProvider();
                    
                    var spriteRects = new List<UnityEditor.SpriteRect>();
                    
                    for (int i = 0; i < iconsInThisSheet; i++)
                    {
                        int recipeIdx = startIdx + i;
                        RecipeSO recipe = recipesToProcess[recipeIdx];
                        
                        int row = i / iconsPerRow;
                        int col = i % iconsPerRow;
                        
                        // Unity textures are bottom-up
                        int x = col * iconWidth;
                        int y = (iconsPerRow - 1 - row) * iconHeight;
                        
                        var spriteRect = new UnityEditor.SpriteRect
                        {
                            name = SanitizeFileName(recipe.displayName),
                            rect = new Rect(x, y, iconWidth, iconHeight),
                            alignment = (int)SpriteAlignment.Center,
                            pivot = new Vector2(0.5f, 0.5f),
                            spriteID = GUID.Generate()
                        };
                        
                        spriteRects.Add(spriteRect);
                    }
                    
                    dataProvider.SetSpriteRects(spriteRects.ToArray());
                    dataProvider.Apply();
                }
                
                importer.SaveAndReimport();
            }
        }
        
        // After reimport, assign sprites to recipes
        AssetDatabase.Refresh();
        AssignSpritesToRecipes(sheetCount);
    }
    
    void AssignSpritesToRecipes(int sheetCount)
    {
        int iconsPerSheet = iconsPerRow * iconsPerRow;
        
        for (int sheetIndex = 0; sheetIndex < sheetCount; sheetIndex++)
        {
            string sheetPath = $"{outputFolder}/RecipeIcons_Sheet{sheetIndex}.png";
            
            // Load all sprites from this sheet
            Object[] sprites = AssetDatabase.LoadAllAssetsAtPath(sheetPath);
            
            int startIdx = sheetIndex * iconsPerSheet;
            int endIdx = Mathf.Min(startIdx + iconsPerSheet, recipesToProcess.Count);
            
            for (int i = startIdx; i < endIdx; i++)
            {
                RecipeSO recipe = recipesToProcess[i];
                string spriteName = SanitizeFileName(recipe.displayName);
                
                // Find matching sprite
                Sprite matchingSprite = sprites
                    .OfType<Sprite>()
                    .FirstOrDefault(s => s.name == spriteName);
                
                if (matchingSprite != null)
                {
                    recipe.icon = matchingSprite;
                    EditorUtility.SetDirty(recipe);
                }
                else
                {
#if UNITY_EDITOR
                    Debug.LogWarning($"[RecipeIconGenerator] Could not find sprite for '{recipe.displayName}'");
#endif
                }
            }
        }
        
        AssetDatabase.SaveAssets();
    }
    
    void SetupMugForRecipe(GameObject mugInstance, RecipeSO recipe)
    {
        // Get components
        var mugController = mugInstance.GetComponent<MugController>();
        var cupState = mugInstance.GetComponent<CupState>();
        var beverageState = mugInstance.GetComponent<MugBeverageState>();
        var iceState = mugInstance.GetComponent<MugIceState>();

        if (beverageState == null || cupState == null)
        {
#if UNITY_EDITOR
            Debug.LogWarning($"[RecipeIconGenerator] Mug missing required components for '{recipe.displayName}'");
#endif
            return;
        }
        
        // Set up water
        if (recipe.requiredWater != WaterSource.None)
        {
            WaterTemp temp = recipe.requiredWater == WaterSource.Hot ? WaterTemp.Hot : WaterTemp.Cold;
            Color waterColor = temp == WaterTemp.Hot ? new Color(0.8f, 0.9f, 1f) : new Color(0.7f, 0.85f, 1f);
            
            beverageState.RegisterWater(temp, waterColor);
        }
        
        // Set up tea type
        if (recipe.tea != null)
        {
            if (recipe.tea.powderRequired)
            {
                // Powder-based drink (e.g., Hot Chocolate)
                beverageState.SetPowderType(
                    recipe.tea.teaType,
                    recipe.tea.prePowderColor,
                    recipe.tea.postPowderColor,
                    recipe.tea.milkRequired,
                    recipe.tea.powderRequired
                );
            }
            else
            {
                // Regular tea
                beverageState.SetTeaType(
                    recipe.tea.teaType,
                    recipe.tea.targetColor,
                    recipe.tea.milkRequired
                );
                
                // Simulate steeping (3 dips for full brew)
                if (recipe.brewedRequired)
                {
                    for (int i = 0; i < 3; i++)
                    {
                        beverageState.RegisterSteep();
                    }
                }
            }
        }
        
        // Add milk if required
        if (recipe.milkRequiredOverride || (recipe.tea != null && recipe.tea.milkRequired))
        {
            beverageState.AddMilk();
        }
        
        // Add ice if required
        if (recipe.iceRequired && iceState != null)
        {
            iceState.AddIce(recipe.minIceChips);
        }
        
        // Add toppings
        if (recipe.requiredToppings != null)
        {
            // First make sure cup has liquid for toppings that need it
            cupState.SetFillAmount(1f);
            
            foreach (string topping in recipe.requiredToppings)
            {
                cupState.AddTopping(topping);
            }
        }
        
        // Make sure cup is filled
        cupState.SetFillAmount(1f);
    }
    
    Texture2D CaptureSprite(GameObject target)
    {
        if (captureCamera == null)
        {
#if UNITY_EDITOR
            Debug.LogError("[RecipeIconGenerator] Capture camera is null!");
#endif
            return null;
        }
        
        // Create render texture
        RenderTexture renderTexture = new RenderTexture(iconWidth, iconHeight, 24);
        RenderTexture.active = renderTexture;
        
        captureCamera.targetTexture = renderTexture;
        captureCamera.Render();
        
        // Read pixels
        Texture2D texture = new Texture2D(iconWidth, iconHeight, TextureFormat.RGBA32, false);
        texture.ReadPixels(new Rect(0, 0, iconWidth, iconHeight), 0, 0);
        texture.Apply();
        
        // Cleanup
        captureCamera.targetTexture = null;
        RenderTexture.active = null;
        DestroyImmediate(renderTexture);
        
        return texture;
    }
    
    void SaveTextureAsPNG(Texture2D texture, string path)
    {
        byte[] bytes = texture.EncodeToPNG();
        File.WriteAllBytes(path, bytes);
        AssetDatabase.Refresh();
    }
    
    string SanitizeFileName(string name)
    {
        string sanitized = name.Replace(" ", "_");
        sanitized = sanitized.Replace(",", "");
        sanitized = sanitized.Replace("'", "");
        sanitized = sanitized.Replace("-", "");
        return sanitized;
    }
}
#endif
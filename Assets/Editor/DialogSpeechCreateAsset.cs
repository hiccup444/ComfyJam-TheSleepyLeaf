using UnityEditor;
using UnityEngine;

public static class DialogSpeechCreateAsset
{
    [MenuItem("Assets/Create/Audio/DialogSpeech Settings (Default)")]
    private static void CreateDialogSpeechSettings()
    {
        const string folderPath = "Assets/Audio";
        if (!AssetDatabase.IsValidFolder(folderPath))
        {
            AssetDatabase.CreateFolder("Assets", "Audio");
        }

        var asset = ScriptableObject.CreateInstance<DialogSpeechSettings>();
        string assetPath = $"{folderPath}/DialogSpeechSettings.asset";
        AssetDatabase.CreateAsset(asset, assetPath);
        AssetDatabase.SaveAssets();
        EditorUtility.FocusProjectWindow();
        Selection.activeObject = asset;
    }
}

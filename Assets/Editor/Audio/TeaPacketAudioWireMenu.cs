using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class TeaPacketAudioWireMenu
{
    private const string DefaultMainChildName = "teaPacketMain";

    [MenuItem("Tools/Audio/Wire TeaPacket Pickup Audio (Packets Folder)")]
    public static void WirePacketsFolder()
    {
        string[] searchIn = new[] { "Assets/Prefabs/Ingredients/Packets" };
        WireAllTeaPackets(searchIn);
    }

    [MenuItem("Tools/Audio/Wire TeaPacket Pickup Audio (All Prefabs in Assets)")]
    public static void WireAllAssets()
    {
        string[] searchIn = new[] { "Assets" };
        WireAllTeaPackets(searchIn);
    }

    private static void WireAllTeaPackets(string[] searchFolders)
    {
        var guids = AssetDatabase.FindAssets("t:Prefab", searchFolders);
        int modified = 0, total = 0;

        foreach (var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var root = PrefabUtility.LoadPrefabContents(path);
            if (root == null)
                continue;

            try
            {
                var teaPacket = root.GetComponent<TeaPacket>();
                if (teaPacket == null)
                {
                    // Not a tea packet prefab
                    continue;
                }

                total++;

                // Resolve main child name from serialized field
                string mainName = DefaultMainChildName;
                var so = new SerializedObject(teaPacket);
                var prop = so.FindProperty("mainSpriteName");
                if (prop != null && !string.IsNullOrEmpty(prop.stringValue))
                    mainName = prop.stringValue;

                // Try to find the main child
                Transform mainT = root.transform.Find(mainName);
                if (mainT == null)
                {
                    // Fallback: find any child that has DragItem2D
                    var drag = root.GetComponentInChildren<DragItem2D>(true);
                    if (drag != null) mainT = drag.transform;
                }

                if (mainT == null)
                {
#if UNITY_EDITOR
                    Debug.LogWarning($"[TeaPacketAudioWire] Could not find main child '{mainName}' in prefab '{path}'. Skipping.");
#endif
                    continue;
                }

                bool changed = false;
                var go = mainT.gameObject;

                // Ensure AudioSource exists
                var src = go.GetComponent<AudioSource>();
                if (src == null)
                {
                    src = go.AddComponent<AudioSource>();
                    src.playOnAwake = false;
                    src.loop = false;
                    changed = true;
                }

                // Ensure TeaPacketPickupAudio exists
                var audio = go.GetComponent<TeaPacketPickupAudio>();
                if (audio == null)
                {
                    audio = go.AddComponent<TeaPacketPickupAudio>();
                    changed = true;
                }

                if (changed)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    modified++;
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

#if UNITY_EDITOR
        Debug.Log($"[TeaPacketAudioWire] Processed {total} tea packet prefabs. Modified: {modified}.");
#endif
        if (modified > 0)
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
    }
}


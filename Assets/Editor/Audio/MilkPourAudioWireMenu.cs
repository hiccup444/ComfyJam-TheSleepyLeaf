using UnityEditor;
using UnityEngine;

public static class MilkPourAudioWireMenu
{
    [MenuItem("Tools/Audio/Wire Milk Pour Audio (Prefabs with LiquidStream)")]
    public static void WireMilkPrefabs()
    {
        string[] searchIn = new[] { "Assets" };
        string[] guids = AssetDatabase.FindAssets("t:Prefab", searchIn);
        int modified = 0, total = 0;

        foreach (var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var root = PrefabUtility.LoadPrefabContents(path);
            if (root == null) continue;
            try
            {
                var stream = root.GetComponentInChildren<LiquidStream>(true);
                if (stream == null) continue; // not a milk or pour prefab
                total++;

                var host = stream.gameObject;
                bool changed = false;

                var src = host.GetComponent<AudioSource>();
                if (src == null)
                {
                    src = host.AddComponent<AudioSource>();
                    src.playOnAwake = false;
                    src.loop = false;
                    changed = true;
                }

                var audio = host.GetComponent<MilkPourAudio>();
                if (audio == null)
                {
                    audio = host.AddComponent<MilkPourAudio>();
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
        Debug.Log($"[MilkPourAudioWire] Processed {total} pour prefabs. Modified: {modified}.");
#endif
        if (modified > 0)
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
    }
}

